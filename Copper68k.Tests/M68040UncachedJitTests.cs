using System.Reflection;
using Copper68k.Tests.Synthetic;

namespace Copper68k.Tests;

public sealed class M68040UncachedJitTests
{
    [Fact]
    public void GuardedTierThreeBusTracePreservesAnUntouchedStackPointer()
    {
        const uint code = 0x00500000;
        const uint data = 0x00600000;
        const uint stack = 0x00700000;
        var bus = new WritableCodeBus();
        bus.Initialize(code, 0x2080, 2);     // MOVE.L D0,(A0)
        bus.Initialize(code + 2, 0x60FC, 2); // BRA.S code
        var trace = CompileTierThree(bus, code, pureCpu: false);
        using var cpu = M68kJitCore.CreateM68040ForTesting(bus, enableV2: true,
            enableUncachedFastRamTraces: true);
        cpu.Reset(code, stack);
        cpu.State.A[0] = data;
        cpu.State.D[0] = 0x12345678;

        Assert.Equal(1, (int)trace.DynamicInvoke(cpu, 1, 100L, new Boundary())!);
        Assert.Equal(code + 2, cpu.State.ProgramCounter);
        Assert.Equal(stack, cpu.State.A[7]);
        Assert.Equal(stack, cpu.State.InterruptStackPointer);
        Assert.Equal(0x12345678u, bus.Peek(data, 4));
    }

    [Theory]
    [InlineData(0x5280, 0xFFFFFFFFu, 0x7200, true, true)]  // ADDQ.L carry, then MOVEQ
    [InlineData(0x5280, 0u, 0x7200, false, false)]          // ADDQ.L clears X, then MOVEQ
    [InlineData(0x5300, 0xABCD0000u, 0xB281, false, true)]  // SUBQ.B borrow, then CMP.L D1,D1
    [InlineData(0x5340, 0xFFFF0001u, 0xB281, true, false)] // SUBQ.W clears X, then CMP
    public void V2KeepsExtendAcrossInstructionsThatReplaceTheOtherFlags(
        ushort arithmetic, uint initialValue, ushort follower, bool pureCpu, bool expectedExtend)
    {
        const uint code = 0x00500000;
        var bus = new WritableCodeBus();
        bus.Initialize(code, arithmetic, 2);
        bus.Initialize(code + 2, follower, 2);
        var trace = CompileTierThree(bus, code, pureCpu);
        using var cpu = M68kJitCore.CreateM68040ForTesting(bus, enableV2: true,
            enableUncachedFastRamTraces: true);
        cpu.Reset(code, 0x00700000);
        cpu.State.D[0] = initialValue;
        cpu.State.StatusRegister = (ushort)(M68kCpuState.Supervisor | (expectedExtend ? 0 : M68kCpuState.Extend));

        Assert.Equal(2, (int)trace.DynamicInvoke(cpu, 2, 100L, new Boundary())!);
        Assert.Equal(code + 4, cpu.State.ProgramCounter);
        Assert.Equal(2L, cpu.State.Cycles);
        Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Zero | (expectedExtend ? M68kCpuState.Extend : 0),
            cpu.State.StatusRegister);
    }

    [Fact]
    public void TimedTransferFinishesCurrentInstructionBeforeNextCompiledInstruction()
    {
        const uint code = 0x00500000;
        const uint source = 0x00600000;
        const uint destination = source + 0x100;
        var bus = new WritableCodeBus();
        bus.Initialize(code, 0x2290, 2);     // MOVE.L (A0),(A1)
        bus.Initialize(code + 2, 0x5282, 2); // ADDQ.L #1,D2
        bus.Initialize(source, 0xABCD1234, 4);
        var trace = CompileTierThree(bus, code, pureCpu: false);
        using var cpu = M68kJitCore.CreateM68040ForTesting(bus, enableV2: true,
            enableUncachedFastRamTraces: true);
        cpu.Reset(code, 0x00700000);
        cpu.State.A[0] = source;
        cpu.State.A[1] = destination;
        bus.OnLongTransfer = cpu.GetTimedBusBoundaryRequest();
        Assert.NotNull(bus.OnLongTransfer);

        Assert.Equal(1, (int)trace.DynamicInvoke(cpu, 2, 100L, new Boundary())!);
        Assert.Equal(code + 2, cpu.State.ProgramCounter);
        Assert.Equal(16L, cpu.State.Cycles);
        Assert.Equal(0u, cpu.State.D[2]);
        Assert.Collection(bus.Accesses,
            read => Assert.Equal(new BusAccess(source, 4, false, 0xABCD1234, M68kBusAccessKind.CpuDataRead), read),
            write => Assert.Equal(new BusAccess(destination, 4, true, 0xABCD1234, M68kBusAccessKind.CpuDataWrite), write));
    }

    [Theory]
    [InlineData(2, 100, 0x4E75, false, false, 2)]
    [InlineData(1, 100, 0x4E75, false, false, 1)]
    [InlineData(2, 1, 0x4E75, false, false, 1)]
    [InlineData(2, 100, 0x7003, false, false, 1)]
    [InlineData(2, 100, 0x4E75, true, false, 1)]
    [InlineData(2, 100, 0x4E75, false, true, 2)]
    public void GuardedLeafReturnHonorsGuestMemoryAndBatchLimits(
        int limit, int targetCycle, ushort liveOpcode, bool instructionCache, bool patchStack, int expectedCount)
    {
        const uint code = 0x00500000;
        const uint leaf = code + 0x80;
        const uint stack = 0x00600800;
        const uint continuation = code + 4;
        var bus = new LeafCallBus(leaf, stack - 4, patchStack ? code + 0x200 : 0u);
        bus.Initialize(code, 0x6100007E, 4); // BSR.W to leaf
        bus.Initialize(leaf, 0x4E75, 2);     // RTS
        var instructions = new M68kDecodedInstruction[1];
        Assert.True(M68kDecoder.TryDecode(bus, code, out instructions[0], out _, M68kJitCpuModel.M68040));
        var trace = CompileV2ForTest(code, instructions, "Tier1", pureCpu: false);
        bus.Initialize(leaf, liveOpcode, 2);
        using var cpu = M68kJitCore.CreateM68040ForTesting(bus, enableV2: true,
            enableUncachedFastRamTraces: true);
        cpu.Reset(code, stack);
        cpu.State.StatusRegister = 0x2711;
        if (instructionCache)
            cpu.State.CacheControlRegister = 0x8000;

        Assert.Equal(expectedCount, (int)trace.DynamicInvoke(cpu, limit, (long)targetCycle, new Boundary())!);
        var stackedReturn = patchStack ? code + 0x200 : continuation;
        var expectedStack = expectedCount == 2 ? stack : stack - 4;
        Assert.Equal(expectedCount == 2 ? stackedReturn : leaf, cpu.State.ProgramCounter);
        Assert.Equal(expectedCount == 2 ? leaf : code, cpu.State.LastInstructionProgramCounter);
        Assert.Equal((long)expectedCount, cpu.State.Cycles);
        Assert.Equal(expectedStack, cpu.State.A[7]);
        Assert.Equal(expectedStack, cpu.State.InterruptStackPointer);
        Assert.Equal(0x2711, cpu.State.StatusRegister);
        Assert.Equal(stackedReturn, bus.Peek(stack - 4, 4));
    }

    [Theory]
    [InlineData(1, 10, false, 1, 0x15)]
    [InlineData(4, 1, true, 1, 0x15)]
    [InlineData(2, 10, true, 2, 0x19)]
    [InlineData(4, 2, false, 2, 0)]
    [InlineData(3, 10, false, 3, 0x04)]
    [InlineData(4, 3, true, 3, 0x14)]
    [InlineData(4, 10, false, 4, 0x04)]
    [InlineData(4, 4, true, 4, 0x14)]
    public void GuardedRegisterRunPreservesFlagsAtEachExit(
        int limit, int targetCycle, bool borrow, int expectedCount, int expectedFlags)
    {
        const uint code = 0x00500000;
        var bus = new WritableCodeBus();
        ushort[] opcodes = [0x5280, 0x5381, 0x7400, 0xC744]; // ADDQ, SUBQ, MOVEQ, EXG
        var instructions = new M68kDecodedInstruction[opcodes.Length];
        for (var index = 0; index < opcodes.Length; index++)
        {
            var address = code + (uint)index * 2;
            bus.Initialize(address, opcodes[index], 2);
            Assert.True(M68kDecoder.TryDecode(bus, address, out instructions[index], out _, M68kJitCpuModel.M68040));
        }
        var trace = CompileV2ForTest(code, instructions, "Tier1", pureCpu: false);
        using var cpu = M68kJitCore.CreateM68040ForTesting(bus, enableV2: true,
            enableUncachedFastRamTraces: true);
        cpu.Reset(code, 0x00700000);
        cpu.State.D[0] = uint.MaxValue;
        cpu.State.D[1] = borrow ? 0u : 2u;
        cpu.State.D[2] = 0xDEADBEEF;
        cpu.State.D[3] = 0x12345678;
        cpu.State.D[4] = 0x87654321;

        Assert.Equal(expectedCount, (int)trace.DynamicInvoke(cpu, limit, (long)targetCycle, new Boundary())!);
        Assert.Equal((long)expectedCount, cpu.State.Cycles);
        Assert.Equal(code + (uint)expectedCount * 2, cpu.State.ProgramCounter);
        Assert.Equal(expectedFlags, cpu.State.StatusRegister & 0x1F);
        Assert.Equal(0u, cpu.State.D[0]);
        Assert.Equal(expectedCount >= 3 ? 0u : 0xDEADBEEFu, cpu.State.D[2]);
        Assert.Equal(expectedCount == 4 ? 0x87654321u : 0x12345678u, cpu.State.D[3]);
        Assert.Equal(expectedCount == 4 ? 0x12345678u : 0x87654321u, cpu.State.D[4]);
    }

    [Fact]
    public void GuardedRegisterRunStartsAtAnOverlappingInstructionSuccessor()
    {
        const uint code = 0x00500000;
        var bus = new WritableCodeBus();
        bus.Initialize(code, 0x6602, 2);      // BNE.S to the extension word at code + 4
        bus.Initialize(code + 2, 0x203C, 2);  // MOVE.L #$223C1234,D0
        bus.Initialize(code + 4, 0x223C, 2);  // Alternate entry: MOVE.L #$12347001,D1
        bus.Initialize(code + 6, 0x1234, 2);
        bus.Initialize(code + 8, 0x7001, 2);  // MOVEQ #1,D0 on the first path
        bus.Initialize(code + 10, 0x7402, 2);
        bus.Initialize(code + 12, 0x7603, 2);
        bus.Initialize(code + 14, 0x7804, 2);
        uint[] offsets = [0, 2, 4, 8, 10, 12, 14];
        var instructions = new M68kDecodedInstruction[offsets.Length];
        for (var index = 0; index < offsets.Length; index++)
            Assert.True(M68kDecoder.TryDecode(bus, code + offsets[index], out instructions[index],
                out _, M68kJitCpuModel.M68040));
        var trace = CompileV2ForTest(code, instructions, "Tier1", pureCpu: false);
        using var cpu = M68kJitCore.CreateM68040ForTesting(bus, enableV2: true,
            enableUncachedFastRamTraces: true);
        cpu.Reset(code, 0x00700000);
        cpu.State.StatusRegister = 0x2710;

        Assert.Equal(5, (int)trace.DynamicInvoke(cpu, 5, 5L, new Boundary())!);
        Assert.Equal(5L, cpu.State.Cycles);
        Assert.Equal(code + 16, cpu.State.ProgramCounter);
        Assert.Equal(new uint[] { 0, 0x12347001, 2, 3, 4 }, cpu.State.D.Take(5));
        Assert.Equal(0x2710, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData("Tier1", false, 0xE188, 0u)]  // LSL.L #8,D0
    [InlineData("Tier3", true, 0xE1A8, 0u)]   // LSL.L D0,D0 with zero count
    [InlineData("Tier3", false, 0xE1A8, 40u)]
    public void CompiledRegisterShiftUsesTheM68040CycleFloor(
        string tier, bool pureCpu, ushort opcode, uint value)
    {
        const uint code = 0x00500000;
        const uint stack = 0x00700000;
        var bus = new WritableCodeBus();
        bus.Initialize(code, opcode, 2);
        bus.Initialize(code + 2, 0x4E71, 2); // NOP
        var instructions = new M68kDecodedInstruction[2];
        for (var index = 0; index < instructions.Length; index++)
            Assert.True(M68kDecoder.TryDecode(bus, code + (uint)index * 2, out instructions[index],
                out _, M68kJitCpuModel.M68040));
        var trace = CompileV2ForTest(code, instructions, tier, pureCpu);
        using var compiled = M68kJitCore.CreateM68040ForTesting(bus, enableV2: true,
            enableUncachedFastRamTraces: true);
        using var interpreted = M68kJitCore.CreateM68040ForTesting(bus, enableV2: false);
        compiled.Reset(code, stack);
        interpreted.Reset(code, stack);
        compiled.State.D[0] = interpreted.State.D[0] = value;
        compiled.State.StatusRegister = interpreted.State.StatusRegister = 0x2711;

        Assert.Equal(1, (int)trace.DynamicInvoke(compiled, 2, 1L, new Boundary())!);
        Assert.Equal(1, interpreted.ExecuteInstructions(2, 1L, new Boundary()));
        Assert.Equal(1L, compiled.State.Cycles);
        Assert.Equal(interpreted.State.Cycles, compiled.State.Cycles);
        Assert.Equal(interpreted.State.ProgramCounter, compiled.State.ProgramCounter);
        Assert.Equal(interpreted.State.D[0], compiled.State.D[0]);
        Assert.Equal(interpreted.State.StatusRegister, compiled.State.StatusRegister);
    }

    private static Delegate CompileTierThree(WritableCodeBus bus, uint code, bool pureCpu)
    {
        var instructions = new M68kDecodedInstruction[2];
        Assert.True(M68kDecoder.TryDecode(bus, code, out instructions[0], out _, M68kJitCpuModel.M68040));
        Assert.True(M68kDecoder.TryDecode(bus, code + 2, out instructions[1], out _, M68kJitCpuModel.M68040));

        return CompileV2ForTest(code, instructions, "Tier3", pureCpu);
    }

    private static Delegate CompileV2ForTest(
        uint code, M68kDecodedInstruction[] instructions, string tierName, bool pureCpu)
    {
        var compile = typeof(M68kJitCore).GetMethod("CompileV2", BindingFlags.NonPublic | BindingFlags.Static)!;
        var tier = Enum.Parse(compile.GetParameters()[1].ParameterType, tierName);
        return (Delegate)compile.Invoke(null,
            [code, tier, instructions, code, instructions.Sum(instruction => instruction.Length),
                pureCpu, false, true, false, false, false, true, true])!;
    }

    public enum CodeChange
    {
        BeforeDispatch,
        AtBatchEntry,
        InsideTrace
    }

    [Theory]
    [InlineData(CodeChange.BeforeDispatch)]
    [InlineData(CodeChange.AtBatchEntry)]
    [InlineData(CodeChange.InsideTrace)]
    public void UncachedFastRamTraceSeesChangedOpcodeBeforeItExecutes(CodeChange change)
    {
        const uint code = 0x00500000;
        var bus = new WritableCodeBus();
        bus.Initialize(code, 0x3082, 2);     // MOVE.W D2,(A0)
        bus.Initialize(code + 2, 0x7001, 2); // MOVEQ #1,D0
        bus.Initialize(code + 4, 0x60FA, 2); // BRA.S code
        using var cpu = M68kJitCore.CreateM68040ForTesting(bus, enableV2: true,
            enableUncachedFastRamTraces: true);
        cpu.Reset(code, 0x6000);
        cpu.State.A[0] = code + 0x100;
        cpu.State.D[2] = 0x7002;
        var boundary = new Boundary();
        Assert.Equal(300, cpu.ExecuteInstructions(300, 10000, boundary));
        Assert.Equal(0u, cpu.State.CacheControlRegister);
        Assert.Equal(code, cpu.State.ProgramCounter);
        Assert.Equal(1u, cpu.State.D[0]);
        Assert.True(cpu.GetJitStatistics().CompiledInstructions > 0);

        var invalidations = cpu.GetJitStatistics().Invalidations;
        var from = cpu.State.Cycles;
        if (change == CodeChange.InsideTrace)
            cpu.State.A[0] = code + 2;
        else if (change == CodeChange.AtBatchEntry)
            boundary.AtBatchEntry = () => bus.PatchWord(code + 2, 0x7002);
        else
            bus.PatchWord(code + 2, 0x7002);

        Assert.Equal(2, cpu.ExecuteInstructions(2, from + 100, boundary));
        Assert.Equal(2u, cpu.State.D[0]);
        Assert.Equal(code + 4, cpu.State.ProgramCounter);
        Assert.Null(boundary.AtBatchEntry);
        Assert.True(cpu.GetJitStatistics().Invalidations > invalidations);
    }

    private sealed class Boundary : IM68kBusAccessTraceBatchBoundary, IM68kPureCpuTraceBatchBoundary
    {
        public Action? AtBatchEntry { get; set; }

        public bool BeforeInstruction() => true;
        public void AfterInstruction(long previous, long current) { }

        public bool TryBeginBusAccessTraceBatch(M68kCpuState state, long target, out long batchTarget)
        {
            var onEntry = AtBatchEntry;
            AtBatchEntry = null;
            onEntry?.Invoke();
            batchTarget = target;
            return true;
        }

        public void AfterBusAccessTraceBatch(long previous, long current, int count) { }

        public bool TryBeginPureCpuTraceBatch(M68kCpuState state, long target, out long batchTarget)
            => TryBeginBusAccessTraceBatch(state, target, out batchTarget);

        public void AfterPureCpuTraceBatch(long previous, long current, int count) { }
    }

    private sealed class LeafCallBus(uint leaf, uint stackAddress, uint patchedReturn)
        : WritableCodeBus, IM68kJitFastMemoryBus
    {
        private readonly byte[] _stack = new byte[4];

        public bool TryReadJitZeroWaitMemory(uint address, M68kOperandSize size, out uint value)
        {
            if ((address == leaf && size == M68kOperandSize.Word) ||
                (address == stackAddress && size == M68kOperandSize.Long))
            {
                value = Peek(address, (int)size);
                return true;
            }

            value = 0;
            return false;
        }

        public bool TryWriteJitZeroWaitMemory(uint address, uint value, M68kOperandSize size) => false;

        public bool TryGetJitZeroWaitReadMemory(
            uint address, int count, out byte[] memory, out int offset, out M68kJitMemoryKind kind)
        {
            memory = _stack;
            offset = 0;
            kind = M68kJitMemoryKind.FastRam;
            return address == stackAddress && count == 4;
        }

        public bool TryGetJitZeroWaitWriteMemory(
            uint address, int count, out byte[] memory, out int offset, out M68kJitMemoryKind kind)
            => TryGetJitZeroWaitReadMemory(address, count, out memory, out offset, out kind);

        public void CompleteJitZeroWaitWrite(uint address, int count)
            => Initialize(address, patchedReturn != 0
                ? patchedReturn
                : System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(_stack), count);
    }

    private class WritableCodeBus : SparseRecordingBus, IM68kBus, IM68kCodeReader, IM68kJitBus
    {
        private uint _generation;
        public event Action<uint, int>? JitCodeRangeWritten;

        public Action? OnLongTransfer { get; set; }

        public new uint ReadLong(uint address, ref long cycle, M68kBusAccessKind kind)
        {
            cycle += 8;
            OnLongTransfer?.Invoke();
            return base.ReadLong(address, ref cycle, kind);
        }

        public new void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind kind)
        {
            cycle += 8;
            OnLongTransfer?.Invoke();
            base.WriteLong(address, value, ref cycle, kind);
        }

        public void PatchWord(uint address, ushort value)
        {
            Initialize(address, value, 2);
            PublishWrite(address, 2);
        }

        public new void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind kind)
        {
            base.WriteWord(address, value, ref cycle, kind);
            PublishWrite(address, 2);
        }

        private void PublishWrite(uint address, int bytes)
        {
            if (!IsJitCodeAddress(address, bytes, M68kBusAccessKind.CpuInstructionFetch))
                return;
            _generation++;
            JitCodeRangeWritten?.Invoke(address, bytes);
        }

        public bool IsJitCodeAddress(uint address, int count, M68kBusAccessKind kind)
            => address >= 0x00500000 && (ulong)address + (uint)count <= 0x00500100;

        public bool IsJitReadOnlyCodeAddress(uint address, int count, M68kBusAccessKind kind) => false;
        public ushort ReadJitCodeWord(uint address) => ReadHostWord(address);
        public ushort ReadHostWord(uint address) => (ushort)Peek(address, 2);
        public uint GetJitCodePageGeneration(uint address) => _generation;

        public bool JitCodeRangeGenerationMatches(uint address, int count, uint first, uint last)
            => first == _generation && last == _generation;

        public bool TryCaptureJitCodeSnapshot(uint address, int count, out M68kJitCodeSnapshot snapshot)
        {
            snapshot = default;
            return false;
        }
    }
}
