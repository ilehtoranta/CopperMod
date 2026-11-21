using System.Buffers.Binary;

namespace Copper68k.Tests;

public sealed class M68040FastRamLoopTests
{
    private const uint Code = 0x00500000;
    private const uint Callee = Code + 0x100;
    private const uint Operand = Code + 0x300;
    private const uint Timer = Operand + 4;
    private const uint Stack = Code + 0x900;
    private const int CallCount = 14;
    private const int LoopLength = 3 + 5 * CallCount;

    [Fact]
    public void AHotFastRamLoopMatchesTheInterpreterAtTheSameDeadline()
    {
        var controlBus = new FastRamBus();
        var compiledBus = new FastRamBus();
        using var control = CreateCpu(controlBus, jit: false);
        using var compiled = CreateCpu(compiledBus, jit: true);
        AdvanceBoth(control, compiled, LoopLength * 1000);

        Assert.True(compiled.Counters.V2BoundLapInstructions > 0, DescribeExecution(compiled));
        AssertSameState(control, controlBus, compiled, compiledBus);
        var retiredInsideLoop = compiled.Counters.V2BoundLapInstructions;

        // One cycle short of a full iteration. The partial exit includes
        // the last comparison, and must leave the conditional branch intact.
        AdvanceBoth(control, compiled, LoopLength - 1);
        Assert.Equal(retiredInsideLoop, compiled.Counters.V2BoundLapInstructions);
        AssertSameState(control, controlBus, compiled, compiledBus);

        // Repeated partial slices take the ordinary backedge while the
        // already-promoted loop remains bound in the cache.
        for (var slice = 0; slice < 200; slice++)
            AdvanceBoth(control, compiled, LoopLength - 1);
        Assert.Equal(retiredInsideLoop, compiled.Counters.V2BoundLapInstructions);
        AssertSameState(control, controlBus, compiled, compiledBus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APartialIterationUsesTheFixedDeadlineAndPublishesTheActiveStack(bool fixedDeadline)
    {
        var controlBus = new FastRamBus();
        var compiledBus = new FastRamBus();
        using var control = CreateCpu(controlBus, jit: false);
        using var compiled = CreateCpu(compiledBus, jit: true);
        const int warmup = LoopLength * 1000;
        Assert.Equal(warmup, control.ExecuteInstructions(warmup, warmup, new SliceBoundary()));
        Assert.Equal(warmup, compiled.ExecuteInstructions(warmup, warmup, new SliceBoundary(fixedDeadline)));
        var before = compiled.Counters.V2BoundLapInstructions;
        const int count = LoopLength + 2; // Stop after the first BSR in the next iteration.
        var deadline = control.State.Cycles + count;
        var boundary = new SliceBoundary(fixedDeadline);
        Assert.Equal(count, control.ExecuteInstructions(count + 2, deadline, new SliceBoundary()));
        Assert.Equal(count, compiled.ExecuteInstructions(count + 2, deadline, boundary));

        Assert.Equal(fixedDeadline ? count : LoopLength, boundary.LargestBusBatch);
        Assert.Equal(LoopLength, compiled.Counters.V2BoundLapInstructions - before);
        Assert.Equal(Stack - 4, compiled.State.A[7]);
        AssertSameState(control, controlBus, compiled, compiledBus);

        const int remainder = LoopLength - 2;
        deadline += remainder;
        boundary = new SliceBoundary(fixedDeadline);
        Assert.Equal(remainder, control.ExecuteInstructions(remainder + 2, deadline, new SliceBoundary()));
        Assert.Equal(remainder, compiled.ExecuteInstructions(remainder + 2, deadline, boundary));

        if (fixedDeadline)
            Assert.Equal(remainder, boundary.LargestBusBatch);
        Assert.Equal(Stack, compiled.State.A[7]);
        Assert.Equal(LoopLength, compiled.Counters.V2BoundLapInstructions - before);
        AssertSameState(control, controlBus, compiled, compiledBus);
    }

    [Fact]
    public void AChangedReturnAddressIsUsedAfterTheDeviceDeadline()
    {
        var controlBus = new FastRamBus();
        var compiledBus = new FastRamBus();
        using var control = CreateCpu(controlBus, jit: false);
        using var compiled = CreateCpu(compiledBus, jit: true);
        const int firstSlice = LoopLength + 2;
        var boundary = new SliceBoundary(fixedDeadline: true);
        Assert.Equal(firstSlice, control.ExecuteInstructions(firstSlice, firstSlice, new SliceBoundary()));
        Assert.Equal(firstSlice, compiled.ExecuteInstructions(firstSlice, firstSlice, boundary));
        Assert.Equal(firstSlice, boundary.LargestBusBatch);

        // Redirect the next RTS to the start of the loop.
        controlBus.PatchWord(Stack - 2, (ushort)(Code & 0xFFFF));
        compiledBus.PatchWord(Stack - 2, (ushort)(Code & 0xFFFF));
        const int calleeInstructions = 4;
        const int deadline = firstSlice + calleeInstructions;
        Assert.Equal(calleeInstructions, control.ExecuteInstructions(calleeInstructions, deadline, new SliceBoundary()));
        Assert.Equal(calleeInstructions, compiled.ExecuteInstructions(calleeInstructions, deadline, new SliceBoundary(true)));
        Assert.Equal(Code, compiled.State.ProgramCounter);
        Assert.Equal(Stack, compiled.State.A[7]);
        AssertSameState(control, controlBus, compiled, compiledBus);
    }

    [Fact]
    public void WatchedStackAndChangedCodePreventUsingAnOldFastRamLoop()
    {
        var controlBus = new FastRamBus();
        var compiledBus = new FastRamBus();
        using var control = CreateCpu(controlBus, jit: false);
        using var compiled = CreateCpu(compiledBus, jit: true);
        AdvanceBoth(control, compiled, LoopLength * 1000);
        var previouslyBound = compiled.Counters.V2BoundLapInstructions;
        Assert.True(previouslyBound > 0, DescribeExecution(compiled));

        compiledBus.StackObserved = true;
        AdvanceBoth(control, compiled, LoopLength * 3);
        Assert.Equal(previouslyBound, compiled.Counters.V2BoundLapInstructions);
        AssertSameState(control, controlBus, compiled, compiledBus);

        compiledBus.StackObserved = false;
        controlBus.PatchWord(Code, 0x5487); // ADDQ.L #2,D7
        compiledBus.PatchWord(Code, 0x5487);
        AdvanceBoth(control, compiled, LoopLength);
        Assert.Equal(previouslyBound, compiled.Counters.V2BoundLapInstructions);
        AssertSameState(control, controlBus, compiled, compiledBus);

        AdvanceBoth(control, compiled, LoopLength * 2);
        Assert.True(compiled.Counters.V2BoundLapInstructions > previouslyBound, DescribeExecution(compiled));
        AssertSameState(control, controlBus, compiled, compiledBus);
    }

    [Theory]
    [InlineData(0x4280, 0x5280)] // An isolated counter with a repeating state can wrap in a batch.
    [InlineData(0x2007, 0x5280)] // MOVE.L D7,D0 makes the counter observable.
    [InlineData(0x4280, 0x4E71)] // CLR.L preserves the counter's X without the intervening ADDQ.
    [InlineData(0x4280, 0x5281)] // ADDQ.L #1,D1 means the non-counter state does not repeat.
    public void ManyWholeIterationsPreserveRegisterAndFlagStateAtCounterWrap(int finalOperation, int middleOperation)
    {
        var controlBus = new FastRamBus();
        var compiledBus = new FastRamBus();
        foreach (var bus in new[] { controlBus, compiledBus })
        {
            bus.PatchWord(Callee + 2, (ushort)middleOperation);
            bus.PatchWord(Callee + 4, (ushort)finalOperation);
        }
        using var control = CreateCpu(controlBus, jit: false);
        using var compiled = CreateCpu(compiledBus, jit: true);
        AdvanceBoth(control, compiled, LoopLength * 3);
        var previouslyBound = compiled.Counters.V2BoundLapInstructions;

        // Stop at the exact wrap so that a carry that was skipped cannot
        // be overwritten by a later lap.
        AdvanceBoth(control, compiled, LoopLength * 498);
        Assert.True(compiled.Counters.V2BoundLapInstructions > previouslyBound, DescribeExecution(compiled));
        Assert.Equal(0u, compiled.State.D[7]);
        AssertSameState(control, controlBus, compiled, compiledBus);
    }

    [Fact]
    public void AChangeInReadOnlyInputCanEndAPromotedLoop()
    {
        var controlBus = new FastRamBus();
        var compiledBus = new FastRamBus();
        var fallthrough = Code + 2 + CallCount * 4 + 4;
        foreach (var bus in new[] { controlBus, compiledBus })
            bus.PatchWord(fallthrough, 0x60FE); // BRA.S to itself
        using var control = CreateCpu(controlBus, jit: false);
        using var compiled = CreateCpu(compiledBus, jit: true);
        AdvanceBoth(control, compiled, LoopLength * 500);
        var previouslyBound = compiled.Counters.V2BoundLapInstructions;
        var counter = compiled.State.D[7];
        controlBus.PatchWord(Timer + 2, 101);
        compiledBus.PatchWord(Timer + 2, 101);

        AdvanceBoth(control, compiled, LoopLength * 8);
        Assert.Equal(fallthrough, compiled.State.ProgramCounter);
        Assert.Equal(counter + 1, compiled.State.D[7]);
        Assert.Equal(LoopLength, compiled.Counters.V2BoundLapInstructions - previouslyBound);
        AssertSameState(control, controlBus, compiled, compiledBus);
    }

    private static M68kJitCore CreateCpu(FastRamBus bus, bool jit)
    {
        var cpu = M68kJitCore.CreateM68040ForTesting(bus, enableV2: jit, enableUncachedFastRamTraces: jit);
        cpu.Reset(Code, Stack);
        cpu.State.A[0] = Operand;
        cpu.State.A[4] = Timer;
        cpu.State.D[2] = 100;
        cpu.State.D[7] = uint.MaxValue - 500;
        return cpu;
    }

    private static void AdvanceBoth(M68kJitCore control, M68kJitCore compiled, int count)
    {
        var deadline = control.State.Cycles + count;
        Assert.Equal(count, control.ExecuteInstructions(count, deadline, new SliceBoundary()));
        Assert.Equal(count, compiled.ExecuteInstructions(count, deadline, new SliceBoundary()));
    }

    private static string DescribeExecution(M68kJitCore cpu)
    {
        var counters = cpu.Counters;
        return $"PC {cpu.State.ProgramCounter:X8} D7 {cpu.State.D[7]:X8}, " +
            $"V2 bus batches {counters.V2BusAccessBatchExecutions}, " +
            $"pure batches {counters.PureTraceBatchExecutions}, handoffs {counters.V2TraceHandoffExecutions}.";
    }

    private static void AssertSameState(
        M68kJitCore control, FastRamBus controlBus, M68kJitCore compiled, FastRamBus compiledBus)
    {
        Assert.Equal(control.State.ProgramCounter, compiled.State.ProgramCounter);
        Assert.Equal(control.State.LastInstructionProgramCounter, compiled.State.LastInstructionProgramCounter);
        Assert.Equal(control.State.LastOpcode, compiled.State.LastOpcode);
        Assert.Equal(control.State.Cycles, compiled.State.Cycles);
        Assert.Equal(control.State.StatusRegister, compiled.State.StatusRegister);
        Assert.Equal(control.State.D, compiled.State.D);
        Assert.Equal(control.State.A, compiled.State.A);
        Assert.Equal(control.State.InterruptStackPointer, compiled.State.InterruptStackPointer);
        Assert.Equal(control.State.UserStackPointer, compiled.State.UserStackPointer);
        Assert.Equal(control.State.MasterStackPointer, compiled.State.MasterStackPointer);
        Assert.Equal(controlBus.Memory, compiledBus.Memory);
    }

    private sealed class SliceBoundary(bool fixedDeadline = false) :
        IM68kBusAccessTraceBatchBoundary, IM68kPureCpuTraceBatchBoundary
    {
        public bool PreservesBusDeadlineOnEarlyReturn => fixedDeadline;
        internal int LargestBusBatch { get; private set; }

        public bool BeforeInstruction() => true;
        public void AfterInstruction(long previousCycle, long currentCycle) { }
        public bool TryBeginBusAccessTraceBatch(M68kCpuState state, long target, out long deadline)
        {
            deadline = target;
            return deadline > state.Cycles;
        }

        public void AfterBusAccessTraceBatch(long previousCycle, long currentCycle, int instructionCount)
            => LargestBusBatch = Math.Max(LargestBusBatch, instructionCount);
        public bool TryBeginPureCpuTraceBatch(M68kCpuState state, long target, out long deadline)
            => TryBeginBusAccessTraceBatch(state, target, out deadline);
        public void AfterPureCpuTraceBatch(long previousCycle, long currentCycle, int instructionCount) { }
    }

    private sealed class FastRamBus : IM68kBus, IM68kCodeReader, IM68kJitBus, IM68kJitFastMemoryBus
    {
        internal byte[] Memory { get; } = new byte[4096];
        internal bool StackObserved { get; set; }
        private readonly uint[] _generations = new uint[16];
        public event Action<uint, int>? JitCodeRangeWritten;

        internal FastRamBus()
        {
            PatchWord(Code, 0x5287); // ADDQ.L #1,D7
            var pc = Code + 2;
            for (var call = 0; call < CallCount; call++, pc += 4)
            {
                PatchWord(pc, 0x6100); // BSR.W Callee
                PatchWord(pc + 2, (ushort)(Callee - (pc + 2)));
            }
            PatchWord(pc, 0xB494); // CMP.L (A4),D2
            PatchWord(pc + 2, (ushort)(0x6E00 | ((Code - (pc + 4)) & 0xFF))); // BGT.S Code
            PatchWord(Callee, 0x2010); // MOVE.L (A0),D0
            PatchWord(Callee + 2, 0x5280); // ADDQ.L #1,D0
            PatchWord(Callee + 4, 0x4280); // CLR.L D0
            PatchWord(Callee + 6, 0x4E75); // RTS
            BinaryPrimitives.WriteUInt32BigEndian(At(Operand, 4), uint.MaxValue);
        }

        private Span<byte> At(uint address, int size) => Memory.AsSpan((int)(address - Code), size);

        internal void PatchWord(uint address, ushort value)
        {
            BinaryPrimitives.WriteUInt16BigEndian(At(address, 2), value);
            CompleteJitZeroWaitWrite(address, 2);
        }

        public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind kind) => At(address, 1)[0];
        public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind kind) => ReadJitCodeWord(address);
        public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind kind)
            => BinaryPrimitives.ReadUInt32BigEndian(At(address, 4));

        public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind kind)
        {
            At(address, 1)[0] = value;
            CompleteJitZeroWaitWrite(address, 1);
        }

        public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind kind)
            => PatchWord(address, value);

        public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind kind)
        {
            BinaryPrimitives.WriteUInt32BigEndian(At(address, 4), value);
            CompleteJitZeroWaitWrite(address, 4);
        }

        public void ResetExternalDevices(long cycle) { }
        public bool IsJitCodeAddress(uint address, int size, M68kBusAccessKind kind)
            => address >= Code && (ulong)address + (uint)size <= Code + 0x200;
        public bool IsJitReadOnlyCodeAddress(uint address, int size, M68kBusAccessKind kind) => false;
        public ushort ReadJitCodeWord(uint address) => BinaryPrimitives.ReadUInt16BigEndian(At(address, 2));
        public ushort ReadHostWord(uint address) => ReadJitCodeWord(address);
        public uint GetJitCodePageGeneration(uint address) => _generations[(address - Code) >> 8];
        public bool JitCodeRangeGenerationMatches(uint address, int size, uint first, uint last)
            => first == GetJitCodePageGeneration(address) && last == GetJitCodePageGeneration(address + (uint)size - 1);

        public bool TryCaptureJitCodeSnapshot(uint address, int size, out M68kJitCodeSnapshot snapshot)
        {
            var length = Math.Min(size, 256 - (int)(address & 255)) & ~1;
            if (length <= 0 || !IsJitCodeAddress(address, length, M68kBusAccessKind.CpuInstructionFetch))
            {
                snapshot = default;
                return false;
            }

            snapshot = new(address, At(address, length).ToArray(),
                new M68kCodeGenerationStamp([address & ~255u], [GetJitCodePageGeneration(address)]), []);
            return true;
        }

        public bool TryReadJitZeroWaitMemory(uint address, M68kOperandSize size, out uint value)
        {
            if (!TryGetJitZeroWaitReadMemory(address, (int)size, out _, out _, out _))
            {
                value = 0;
                return false;
            }

            value = size switch
            {
                M68kOperandSize.Byte => At(address, 1)[0],
                M68kOperandSize.Word => ReadJitCodeWord(address),
                _ => BinaryPrimitives.ReadUInt32BigEndian(At(address, 4))
            };
            return true;
        }

        public bool TryWriteJitZeroWaitMemory(uint address, uint value, M68kOperandSize size) => false;

        public bool TryGetJitZeroWaitReadMemory(
            uint address, int size, out byte[] memory, out int offset, out M68kJitMemoryKind kind)
        {
            memory = Memory;
            offset = (int)(address - Code);
            kind = M68kJitMemoryKind.FastRam;
            return size > 0 && address >= Code && (ulong)address + (uint)size <= Code + (uint)Memory.Length;
        }

        public bool TryGetJitZeroWaitWriteMemory(
            uint address, int size, out byte[] memory, out int offset, out M68kJitMemoryKind kind)
            => TryGetJitZeroWaitReadMemory(address, size, out memory, out offset, out kind);

        public bool CanBindUnwatchedFastRam => true;

        public bool TryGetJitUnwatchedFastRam(uint address, int size, out byte[] memory, out int offset)
        {
            var mapped = TryGetJitZeroWaitWriteMemory(address, size, out memory, out offset, out _);
            return mapped && !StackObserved && address >= Stack - 32 && (ulong)address + (uint)size <= Stack;
        }

        public void CompleteJitZeroWaitWrite(uint address, int size)
        {
            if (!IsJitCodeAddress(address, size, M68kBusAccessKind.CpuDataWrite))
                return;
            _generations[(address - Code) >> 8]++;
            JitCodeRangeWritten?.Invoke(address, size);
        }
    }
}
