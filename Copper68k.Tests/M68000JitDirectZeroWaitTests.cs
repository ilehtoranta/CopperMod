using Copper68k;

namespace Copper68k.Tests;

public sealed class M68000JitDirectZeroWaitTests
{
    public static IEnumerable<object[]> ImmediateShiftCases()
    {
        for (var type = 0; type < 4; type++)
        {
            for (var direction = 0; direction < 2; direction++)
            {
                for (var size = 0; size < 3; size++)
                {
                    var count = 1 + ((type * 6 + direction * 3 + size) & 7);
                    var encodedCount = count == 8 ? 0 : count;
                    var opcode = 0xE000 |
                        (encodedCount << 9) |
                        (direction << 8) |
                        (size << 6) |
                        (type << 3);
                    yield return
                    [
                        (ushort)opcode,
                        0x8123_4567u,
                        (ushort)(M68kCpuState.Supervisor | M68kCpuState.Extend)
                    ];
                }
            }
        }
    }

    public static IEnumerable<object[]> ClassicMultiplyCases()
    {
        yield return [false, false, (ushort)0x0000, 0xCAFE_0000u];
        yield return [false, false, (ushort)0xFFFF, 0x1234_FFFFu];
        yield return [false, true, (ushort)0x8001, 0xABCD_7FFFu];
        yield return [false, true, (ushort)0x5555, 0xFFFF_AAAAu];
        yield return [true, false, (ushort)0xFFFF, 0x1234_8000u];
        yield return [true, false, (ushort)0x8000, 0xFFFF_FFFFu];
        yield return [true, true, (ushort)0x7FFF, 0xAAAA_FFFFu];
        yield return [true, true, (ushort)0xFFFF, 0x5555_7FFFu];
    }

    public static IEnumerable<object[]> ClassicDivideCases()
    {
        yield return [false, false, (ushort)0x0003, 100u];
        yield return [false, false, (ushort)0x0001, 0x1234_5678u];
        yield return [false, true, (ushort)0xFFFF, 0x0000_FFFFu];
        yield return [false, true, (ushort)0x8000, 0x8000_0000u];
        yield return [true, false, unchecked((ushort)-3), unchecked((uint)-100)];
        yield return [true, false, unchecked((ushort)-1), 0x8000_0000u];
        yield return [true, true, (ushort)1, 0x0000_7FFFu];
        yield return [true, true, unchecked((ushort)-1), 0x0000_8000u];
    }

    [Fact]
    public void V2DirectZeroWaitReadMatchesInterpreterAndElidesFastMemoryProbe()
    {
        const uint code = 0x1000;
        const uint data = 0x2000;
        var jitBus = new DirectZeroWaitBus();
        var interpreterBus = new DirectZeroWaitBus();
        ushort[] program =
        [
            0x2010, // MOVE.L (A0),D0
            0x5280, // ADDQ.L #1,D0
            0x60FA  // BRA.S start
        ];
        jitBus.WriteWords(code, program);
        interpreterBus.WriteWords(code, program);
        jitBus.WriteLongValue(data, 0x1234_5678);
        interpreterBus.WriteLongValue(data, 0x1234_5678);
        using var jit = new M68kJitCore(
            jitBus,
            enableV2: true,
            enableV2BusAccess: true,
            enableV2FastRead: true);
        using var interpreter = new M68kInterpreter(interpreterBus);
        jit.Reset(code, 0xF0000);
        interpreter.Reset(code, 0xF0000);
        jit.State.A[0] = data;
        interpreter.State.A[0] = data;
        var boundary = new BatchBoundary();

        var jitExecuted = jit.ExecuteInstructions(1200, 500_000, boundary);
        var interpreterExecuted = interpreter.ExecuteInstructions(1200, null, boundary);

        Assert.Equal(interpreterExecuted, jitExecuted);
        Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
        Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
        Assert.Equal(interpreter.State.D, jit.State.D);
        Assert.Equal(interpreter.State.A, jit.State.A);
        Assert.True(
            jit.Counters.V2TraceHits > 0,
            $"compiled={jit.Counters.CompiledTraces}, fallback={jit.Counters.FallbackInstructions}, " +
            $"blacklist={jit.Counters.BlacklistCount}, v2Compiled={jit.Counters.V2TraceMethodsCompiled}, " +
            $"traceHits={jit.Counters.TraceHits}, side={jit.Counters.V2SideExits}, " +
            $"entry={jit.Counters.V2SideExitEntryMismatch}, disabledEntry={jit.Counters.V2DisabledEntryMismatchRoots}, " +
            $"unsupportedOp={jit.Counters.V2UnsupportedOperationTop}, unsupportedEa={jit.Counters.V2UnsupportedEaTop}");
        Assert.True(jit.Counters.V2ZeroWaitReadRealFast > 0);

        var probes = jitBus.ZeroWaitReadBufferProbes;
        var busReads = jitBus.DataReads;
        _ = jit.ExecuteInstructions(300, jit.State.Cycles + 100_000, boundary);

        Assert.Equal(probes, jitBus.ZeroWaitReadBufferProbes);
        Assert.Equal(busReads, jitBus.DataReads);
    }

    [Fact]
    public void V2DirectZeroWaitWriteMatchesInterpreterAndElidesFastMemoryProbe()
    {
        const uint code = 0x1000;
        const uint data = 0x2000;
        var jitBus = new DirectZeroWaitBus();
        var interpreterBus = new DirectZeroWaitBus();
        ushort[] program =
        [
            0x2080, // MOVE.L D0,(A0)
            0x60FC  // BRA.S start
        ];
        jitBus.WriteWords(code, program);
        interpreterBus.WriteWords(code, program);
        using var jit = CreateJit(jitBus);
        using var interpreter = new M68kInterpreter(interpreterBus);
        jit.Reset(code, 0xF0000);
        interpreter.Reset(code, 0xF0000);
        jit.State.A[0] = data;
        interpreter.State.A[0] = data;
        jit.State.D[0] = 0x1234_5678;
        interpreter.State.D[0] = 0x1234_5678;
        var boundary = new BatchBoundary();

        var jitExecuted = jit.ExecuteInstructions(1200, 500_000, boundary);
        var interpreterExecuted = interpreter.ExecuteInstructions(1200, null, boundary);

        Assert.Equal(interpreterExecuted, jitExecuted);
        Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
        Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
        Assert.Equal(interpreterBus.ReadLongValue(data), jitBus.ReadLongValue(data));
        Assert.True(jit.Counters.V2TraceHits > 0);
        Assert.True(jit.Counters.V2ZeroWaitWriteRealFast > 0);

        var probes = jitBus.ZeroWaitWriteBufferProbes;
        var completions = jitBus.DirectWriteCompletions;
        _ = jit.ExecuteInstructions(300, jit.State.Cycles + 100_000, boundary);

        Assert.Equal(probes, jitBus.ZeroWaitWriteBufferProbes);
        Assert.True(jitBus.DirectWriteCompletions > completions);
    }

    [Fact]
    public void V2DoesNotUseDirectMapWithoutZeroWaitGuarantee()
    {
        const uint code = 0x1000;
        const uint data = 0x2000;
        var bus = new DirectZeroWaitBus(realFastIsZeroWait: false);
        bus.WriteWords(
            code,
            [
                0x2010, // MOVE.L (A0),D0
                0x60FC  // BRA.S start
            ]);
        bus.WriteLongValue(data, 0x1234_5678);
        using var jit = CreateJit(bus);
        jit.Reset(code, 0xF0000);
        jit.State.A[0] = data;

        _ = jit.ExecuteInstructions(1200, 500_000, new BatchBoundary());

        Assert.True(jit.Counters.V2TraceHits > 0);
        Assert.True(bus.ZeroWaitReadBufferProbes > 0);
    }

    [Fact]
    public void V2DeferredPureBatchCycleFloorMatchesInterpreterAtTightCycleBoundaries()
    {
        const uint code = 0x1000;
        var jitBus = new DirectZeroWaitBus();
        var interpreterBus = new DirectZeroWaitBus();
        ushort[] program =
        [
            0x7003, // MOVEQ #3,D0
            0x7205, // MOVEQ #5,D1
            0xC2C0, // MULU.W D0,D1 (variable internal timing)
            0x5481, // ADDQ.L #2,D1
            0x4A81, // TST.L D1
            0x60F4  // BRA.S start
        ];
        jitBus.WriteWords(code, program);
        interpreterBus.WriteWords(code, program);
        using var jit = CreateJit(jitBus);
        using var interpreter = new M68kInterpreter(interpreterBus);
        jit.Reset(code, 0xF0000);
        interpreter.Reset(code, 0xF0000);
        var boundary = new BatchBoundary();

        for (var batch = 0; batch < 80; batch++)
        {
            var cycleWindow = 7 + (batch % 29);
            var jitExecuted = jit.ExecuteInstructions(97, jit.State.Cycles + cycleWindow, boundary);
            var interpreterExecuted = interpreter.ExecuteInstructions(
                97,
                interpreter.State.Cycles + cycleWindow,
                boundary);

            Assert.Equal(interpreterExecuted, jitExecuted);
            Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
            Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
            Assert.Equal(interpreter.State.D, jit.State.D);
            Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
        }

        Assert.True(jit.Counters.PureTraceBatchInstructions > 0);
    }

    [Fact]
    public void ClassicPureBatchPinsPartialDataRegistersAndMatchesInterpreterAtExits()
    {
        const uint code = 0x1000;
        var jitBus = new DirectZeroWaitBus();
        var interpreterBus = new DirectZeroWaitBus();
        ushort[] program =
        [
            0x70FF,         // MOVEQ #-1,D0
            0x1400,         // MOVE.B D0,D2
            0x5242,         // ADDQ.W #1,D2
            0x3202,         // MOVE.W D2,D1
            0x0C41, 0x0100, // CMPI.W #$0100,D1
            0x60F2          // BRA.S start
        ];
        jitBus.WriteWords(code, program);
        interpreterBus.WriteWords(code, program);
        using var jit = new M68kJitCore(jitBus, enableV2: false);
        using var interpreter = new M68kInterpreter(interpreterBus);
        jit.Reset(code, 0xF0000);
        interpreter.Reset(code, 0xF0000);
        jit.State.D[1] = 0xABCD_0000;
        interpreter.State.D[1] = 0xABCD_0000;
        jit.State.D[2] = 0x1234_0080;
        interpreter.State.D[2] = 0x1234_0080;
        var boundary = new BatchBoundary();

        for (var batch = 0; batch < 100; batch++)
        {
            var cycleWindow = 5 + (batch % 37);
            var jitExecuted = jit.ExecuteInstructions(83, jit.State.Cycles + cycleWindow, boundary);
            var interpreterExecuted = interpreter.ExecuteInstructions(
                83,
                interpreter.State.Cycles + cycleWindow,
                boundary);

            Assert.Equal(interpreterExecuted, jitExecuted);
            Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
            Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
            Assert.Equal(interpreter.State.D, jit.State.D);
            Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
        }

        Assert.Equal(0xABCD_0000u, jit.State.D[1] & 0xFFFF_0000u);
        Assert.Equal(0x1234_0000u, jit.State.D[2] & 0xFFFF_0000u);
        Assert.True(jit.Counters.PureTraceBatchInstructions > 0);
    }

    [Theory]
    [InlineData(M68kCpuState.Supervisor)]
    [InlineData(M68kCpuState.Supervisor | M68kCpuState.Extend)]
    [InlineData(M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry)]
    public void ClassicPureBatchClrDataRegistersMatchesInterpreterAtTightBoundaries(ushort initialStatus)
    {
        const uint code = 0x1000;
        ushort[] program =
        [
            0x4200, // CLR.B D0
            0x660A, // BNE.S fail
            0x4241, // CLR.W D1
            0x6606, // BNE.S fail
            0x4282, // CLR.L D2
            0x6602, // BNE.S fail
            0x60F2, // BRA.S start
            0x7E7F, // fail: MOVEQ #$7F,D7
            0x60FE  // BRA.S fail loop
        ];
        var jitBus = new DirectZeroWaitBus();
        var interpreterBus = new DirectZeroWaitBus();
        jitBus.WriteWords(code, program);
        interpreterBus.WriteWords(code, program);
        using var jit = new M68kJitCore(jitBus, enableV2: false);
        using var interpreter = new M68kInterpreter(interpreterBus);
        jit.Reset(code, 0xF0000);
        interpreter.Reset(code, 0xF0000);
        jit.State.StatusRegister = interpreter.State.StatusRegister = initialStatus;
        jit.State.D[0] = interpreter.State.D[0] = 0xABCD_12FF;
        jit.State.D[1] = interpreter.State.D[1] = 0x1234_FFFF;
        jit.State.D[2] = interpreter.State.D[2] = 0xFFFF_FFFF;
        jit.State.D[7] = interpreter.State.D[7] = 0xCAFE_BABE;
        var boundary = new BatchBoundary();

        for (var batch = 0; batch < 120; batch++)
        {
            var cycleWindow = 3 + (batch % 31);
            var jitExecuted = jit.ExecuteInstructions(73, jit.State.Cycles + cycleWindow, boundary);
            var interpreterExecuted = interpreter.ExecuteInstructions(
                73,
                interpreter.State.Cycles + cycleWindow,
                boundary);

            Assert.Equal(interpreterExecuted, jitExecuted);
            Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
            Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
            Assert.Equal(interpreter.State.D, jit.State.D);
            Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
        }

        Assert.Equal(0xABCD_1200u, jit.State.D[0]);
        Assert.Equal(0x1234_0000u, jit.State.D[1]);
        Assert.Equal(0u, jit.State.D[2]);
        Assert.Equal(0xCAFE_BABEu, jit.State.D[7]);
        Assert.Equal(initialStatus & M68kCpuState.Extend, jit.State.StatusRegister & M68kCpuState.Extend);
        Assert.True(jit.State.GetFlag(M68kCpuState.Zero));
        Assert.True(jit.Counters.PureTraceBatchInstructions > 0);
    }

    [Theory]
    [MemberData(nameof(ImmediateShiftCases))]
    public void ClassicPureBatchImmediateRegisterShiftsMatchInterpreter(
        ushort opcode,
        uint initialValue,
        ushort initialStatus)
    {
        const uint code = 0x1000;
        ushort[] program =
        [
            opcode,
            0x60FC  // BRA.S shift
        ];
        var jitBus = new DirectZeroWaitBus();
        var interpreterBus = new DirectZeroWaitBus();
        jitBus.WriteWords(code, program);
        interpreterBus.WriteWords(code, program);
        using var jit = new M68kJitCore(jitBus, enableV2: false);
        using var interpreter = new M68kInterpreter(interpreterBus);
        jit.Reset(code, 0xF0000);
        interpreter.Reset(code, 0xF0000);
        jit.State.D[0] = interpreter.State.D[0] = initialValue;
        jit.State.StatusRegister = interpreter.State.StatusRegister = initialStatus;
        var boundary = new BatchBoundary();

        for (var batch = 0; batch < 80; batch++)
        {
            var cycleWindow = 5 + (batch % 43);
            var jitExecuted = jit.ExecuteInstructions(61, jit.State.Cycles + cycleWindow, boundary);
            var interpreterExecuted = interpreter.ExecuteInstructions(
                61,
                interpreter.State.Cycles + cycleWindow,
                boundary);

            Assert.Equal(interpreterExecuted, jitExecuted);
            Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
            Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
            Assert.Equal(interpreter.State.D, jit.State.D);
            Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
        }

        Assert.True(jit.Counters.PureTraceBatchInstructions > 0);
    }

    [Fact]
    public void ClassicDirectCpuAdmitsOnlyImmediateRegisterShiftCounts()
    {
        const uint code = 0x1000;
        var bus = new DirectZeroWaitBus();
        bus.WriteWords(code, [0xE380, 0xE3A0]); // ASL.L #1,D0; ASL.L D1,D0

        Assert.True(M68kDecoder.TryDecode(bus, code, out var immediate, out _));
        Assert.True(M68kDecoder.TryDecode(bus, code + 2, out var dynamic, out _));
        Assert.Equal(M68kIlInstructionKind.DirectCpu, M68kOperationEmitter.GetInstructionKind(immediate));
        Assert.Equal(M68kIlInstructionKind.Helper, M68kOperationEmitter.GetInstructionKind(dynamic));
    }

    [Theory]
    [InlineData(true, 0, 31)]
    [InlineData(true, 1, 0)]
    [InlineData(true, 2, 16)]
    [InlineData(true, 3, 15)]
    [InlineData(false, 0, 63)]
    [InlineData(false, 1, 32)]
    [InlineData(false, 2, 16)]
    [InlineData(false, 3, 15)]
    public void ClassicPureBatchRegisterBitOperationsMatchInterpreter(
        bool immediateBit,
        int operation,
        int bitValue)
    {
        const uint code = 0x1000;
        var opcode = immediateBit
            ? (ushort)(0x0800 | (operation << 6))
            : (ushort)(0x0300 | (operation << 6)); // D1 supplies bit number, D0 is destination
        ushort[] program = immediateBit
            ? [opcode, (ushort)bitValue, 0x60FA]
            : [opcode, 0x60FC];
        var jitBus = new DirectZeroWaitBus();
        var interpreterBus = new DirectZeroWaitBus();
        jitBus.WriteWords(code, program);
        interpreterBus.WriteWords(code, program);
        using var jit = new M68kJitCore(jitBus, enableV2: false);
        using var interpreter = new M68kInterpreter(interpreterBus);
        jit.Reset(code, 0xF0000);
        interpreter.Reset(code, 0xF0000);
        const ushort initialStatus = M68kCpuState.Supervisor |
            M68kCpuState.Extend |
            M68kCpuState.Negative |
            M68kCpuState.Overflow |
            M68kCpuState.Carry;
        jit.State.StatusRegister = interpreter.State.StatusRegister = initialStatus;
        jit.State.D[0] = interpreter.State.D[0] = 0x8001_0001;
        jit.State.D[1] = interpreter.State.D[1] = (uint)bitValue;
        var boundary = new BatchBoundary();

        for (var batch = 0; batch < 80; batch++)
        {
            var cycleWindow = 3 + (batch % 37);
            var jitExecuted = jit.ExecuteInstructions(67, jit.State.Cycles + cycleWindow, boundary);
            var interpreterExecuted = interpreter.ExecuteInstructions(
                67,
                interpreter.State.Cycles + cycleWindow,
                boundary);

            Assert.Equal(interpreterExecuted, jitExecuted);
            Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
            Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
            Assert.Equal(interpreter.State.D, jit.State.D);
            Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
        }

        Assert.Equal(
            initialStatus & ~M68kCpuState.Zero,
            jit.State.StatusRegister & ~M68kCpuState.Zero);
        Assert.True(jit.Counters.PureTraceBatchInstructions > 0);
    }

    [Fact]
    public void ClassicDirectCpuKeepsMemoryBitOperationsOutOfPureBatches()
    {
        const uint code = 0x1000;
        var bus = new DirectZeroWaitBus();
        bus.WriteWords(code, [0x0800, 0x0001, 0x0810, 0x0001]); // BTST #1,D0; BTST #1,(A0)

        Assert.True(M68kDecoder.TryDecode(bus, code, out var register, out _));
        Assert.True(M68kDecoder.TryDecode(bus, code + 4, out var memory, out _));
        Assert.Equal(M68kIlInstructionKind.DirectCpu, M68kOperationEmitter.GetInstructionKind(register));
        Assert.NotEqual(M68kIlInstructionKind.DirectCpu, M68kOperationEmitter.GetInstructionKind(memory));
    }

    [Theory]
    [MemberData(nameof(ClassicMultiplyCases))]
    public void ClassicPureBatchMultiplyMatchesInterpreter(
        bool signed,
        bool immediate,
        ushort sourceValue,
        uint destinationValue)
    {
        const uint code = 0x1000;
        var opcode = (ushort)((signed ? 0xC1C0 : 0xC0C0) | (immediate ? 0x003C : 0x0001));
        ushort[] program = immediate
            ? [opcode, sourceValue, 0x60FA]
            : [opcode, 0x60FC];
        var jitBus = new DirectZeroWaitBus();
        var interpreterBus = new DirectZeroWaitBus();
        jitBus.WriteWords(code, program);
        interpreterBus.WriteWords(code, program);
        using var jit = new M68kJitCore(jitBus, enableV2: false);
        using var interpreter = new M68kInterpreter(interpreterBus);
        jit.Reset(code, 0xE0000);
        interpreter.Reset(code, 0xE0000);
        jit.State.D[0] = interpreter.State.D[0] = destinationValue;
        jit.State.D[1] = interpreter.State.D[1] = sourceValue;
        jit.State.StatusRegister = interpreter.State.StatusRegister =
            M68kCpuState.Supervisor |
            M68kCpuState.Extend |
            M68kCpuState.Negative |
            M68kCpuState.Zero |
            M68kCpuState.Overflow |
            M68kCpuState.Carry;
        var boundary = new BatchBoundary();

        for (var batch = 0; batch < 120; batch++)
        {
            var instructionBudget = 1 + (batch % 5);
            var cycleWindow = 3 + (batch % 79);
            var jitExecuted = jit.ExecuteInstructions(
                instructionBudget,
                jit.State.Cycles + cycleWindow,
                boundary);
            var interpreterExecuted = interpreter.ExecuteInstructions(
                instructionBudget,
                interpreter.State.Cycles + cycleWindow,
                boundary);

            Assert.Equal(interpreterExecuted, jitExecuted);
            Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
            Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
            Assert.Equal(interpreter.State.D, jit.State.D);
            Assert.Equal(interpreter.State.A, jit.State.A);
            Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
        }

        Assert.NotEqual(0, jit.State.StatusRegister & M68kCpuState.Extend);
        Assert.True(jit.Counters.PureTraceBatchInstructions > 0);
    }

    [Fact]
    public void ClassicDirectCpuMultiplyClassificationExcludesMemorySources()
    {
        const uint code = 0x1000;
        var bus = new DirectZeroWaitBus();
        bus.WriteWords(
            code,
            [
                0xC0C1,       // MULU.W D1,D0
                0xC1FC, 0xFFFF, // MULS.W #$FFFF,D0
                0xC0D0        // MULU.W (A0),D0
            ]);

        Assert.True(M68kDecoder.TryDecode(bus, code, out var register, out _));
        Assert.True(M68kDecoder.TryDecode(bus, code + 2, out var immediate, out _));
        Assert.True(M68kDecoder.TryDecode(bus, code + 6, out var memory, out _));
        Assert.Equal(M68kIlInstructionKind.DirectCpu, M68kOperationEmitter.GetInstructionKind(register));
        Assert.Equal(M68kIlInstructionKind.DirectCpu, M68kOperationEmitter.GetInstructionKind(immediate));
        Assert.NotEqual(M68kIlInstructionKind.DirectCpu, M68kOperationEmitter.GetInstructionKind(memory));
    }

    [Theory]
    [MemberData(nameof(ClassicDivideCases))]
    public void ClassicPureBatchDivideMatchesInterpreter(
        bool signed,
        bool immediate,
        ushort divisor,
        uint dividend)
    {
        const uint code = 0x1000;
        var opcode = (ushort)((signed ? 0x81C0 : 0x80C0) | (immediate ? 0x003C : 0x0001));
        ushort[] program = immediate
            ? [opcode, divisor, 0x60FA]
            : [opcode, 0x60FC];
        var jitBus = new DirectZeroWaitBus();
        var interpreterBus = new DirectZeroWaitBus();
        jitBus.WriteWords(code, program);
        interpreterBus.WriteWords(code, program);
        using var jit = new M68kJitCore(jitBus, enableV2: false);
        using var interpreter = new M68kInterpreter(interpreterBus);
        jit.Reset(code, 0xE0000);
        interpreter.Reset(code, 0xE0000);
        jit.State.D[0] = interpreter.State.D[0] = dividend;
        jit.State.D[1] = interpreter.State.D[1] = divisor;
        jit.State.StatusRegister = interpreter.State.StatusRegister =
            M68kCpuState.Supervisor |
            M68kCpuState.Extend |
            M68kCpuState.Negative |
            M68kCpuState.Zero |
            M68kCpuState.Overflow |
            M68kCpuState.Carry;
        var boundary = new BatchBoundary();

        for (var batch = 0; batch < 120; batch++)
        {
            var instructionBudget = 1 + (batch % 5);
            var cycleWindow = 3 + (batch % 151);
            var jitExecuted = jit.ExecuteInstructions(
                instructionBudget,
                jit.State.Cycles + cycleWindow,
                boundary);
            var interpreterExecuted = interpreter.ExecuteInstructions(
                instructionBudget,
                interpreter.State.Cycles + cycleWindow,
                boundary);

            Assert.Equal(interpreterExecuted, jitExecuted);
            Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
            Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
            Assert.Equal(interpreter.State.D, jit.State.D);
            Assert.Equal(interpreter.State.A, jit.State.A);
            Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
        }

        Assert.NotEqual(0, jit.State.StatusRegister & M68kCpuState.Extend);
        Assert.True(jit.Counters.PureTraceBatchInstructions > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClassicPureBatchDivideByZeroFlushesStateRaisesVectorFiveAndStopsTrace(bool signed)
    {
        const uint code = 0x1000;
        const uint handler = 0x2000;
        var divideOpcode = (ushort)(signed ? 0x81C1 : 0x80C1);
        ushort[] program =
        [
            0x7407,       // MOVEQ #7,D2 (must be flushed before the exception helper)
            divideOpcode, // DIVS/DIVU D1,D0
            0x5282,       // ADDQ.L #1,D2 (must not execute)
            0x60F8        // BRA.S start
        ];
        var jitBus = new DirectZeroWaitBus();
        var interpreterBus = new DirectZeroWaitBus();
        jitBus.WriteWords(code, program);
        interpreterBus.WriteWords(code, program);
        jitBus.WriteWords(handler, [0x60FE]);
        interpreterBus.WriteWords(handler, [0x60FE]);
        jitBus.WriteLongValue(5 * 4, handler);
        interpreterBus.WriteLongValue(5 * 4, handler);
        using var jit = new M68kJitCore(jitBus, enableV2: false);
        using var interpreter = new M68kInterpreter(interpreterBus);
        jit.Reset(code, 0xE0000);
        interpreter.Reset(code, 0xE0000);
        jit.State.D[0] = interpreter.State.D[0] = 100;
        jit.State.D[1] = interpreter.State.D[1] = 3;
        var boundary = new BatchBoundary();

        _ = jit.ExecuteInstructions(2000, null, boundary);
        _ = interpreter.ExecuteInstructions(2000, null, boundary);
        Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
        for (var step = 0; step < 8 && jit.State.ProgramCounter != code; step++)
        {
            Assert.Equal(1, jit.ExecuteInstructions(1, null, boundary));
            Assert.Equal(1, interpreter.ExecuteInstructions(1, null, boundary));
            Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
        }

        Assert.Equal(code, jit.State.ProgramCounter);
        jit.State.D[0] = interpreter.State.D[0] = 100;
        jit.State.D[1] = interpreter.State.D[1] = 0;
        jit.State.D[2] = interpreter.State.D[2] = 0xDEAD_BEEF;
        var initialStackPointer = jit.State.A[7];
        var pureBefore = jit.Counters.PureTraceBatchInstructions;

        var jitExecuted = jit.ExecuteInstructions(2, jit.State.Cycles + 100_000, boundary);
        var interpreterExecuted = interpreter.ExecuteInstructions(2, interpreter.State.Cycles + 100_000, boundary);

        Assert.Equal(2, jitExecuted);
        Assert.Equal(interpreterExecuted, jitExecuted);
        Assert.Equal(handler, jit.State.ProgramCounter);
        Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
        Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
        Assert.Equal(interpreter.State.D, jit.State.D);
        Assert.Equal(interpreter.State.A, jit.State.A);
        Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
        Assert.Equal(7u, jit.State.D[2]);
        Assert.Equal(initialStackPointer - 6, jit.State.A[7]);
        Assert.Equal(5, jit.State.LastExceptionVector);
        Assert.Equal(code + 4, jit.State.LastExceptionStackedProgramCounter);
        Assert.Equal(code + 2, jit.State.LastExceptionInstructionProgramCounter);
        Assert.Equal(0u, jit.State.LastExceptionD1);
        Assert.Equal(interpreter.State.LastExceptionStatusRegister, jit.State.LastExceptionStatusRegister);
        Assert.Equal(interpreterBus.ReadLongValue(interpreter.State.A[7] + 2), jitBus.ReadLongValue(jit.State.A[7] + 2));
        Assert.True(jit.Counters.PureTraceBatchInstructions > pureBefore);
    }

    [Fact]
    public void ClassicDirectCpuDivideClassificationExcludesMemorySources()
    {
        const uint code = 0x1000;
        var bus = new DirectZeroWaitBus();
        bus.WriteWords(
            code,
            [
                0x80C1,       // DIVU.W D1,D0
                0x81FC, 0xFFFF, // DIVS.W #$FFFF,D0
                0x80D0        // DIVU.W (A0),D0
            ]);

        Assert.True(M68kDecoder.TryDecode(bus, code, out var register, out _));
        Assert.True(M68kDecoder.TryDecode(bus, code + 2, out var immediate, out _));
        Assert.True(M68kDecoder.TryDecode(bus, code + 6, out var memory, out _));
        Assert.Equal(M68kIlInstructionKind.DirectCpu, M68kOperationEmitter.GetInstructionKind(register));
        Assert.Equal(M68kIlInstructionKind.DirectCpu, M68kOperationEmitter.GetInstructionKind(immediate));
        Assert.NotEqual(M68kIlInstructionKind.DirectCpu, M68kOperationEmitter.GetInstructionKind(memory));
    }

    [Fact]
    public void V2PureBatchHelperBarrierPreservesPinnedAndUntouchedRegisters()
    {
        const uint code = 0x1000;
        var jitBus = new DirectZeroWaitBus();
        var interpreterBus = new DirectZeroWaitBus();
        ushort[] program =
        [
            0x7001,         // MOVEQ #1,D0
            0x003C, 0x0001, // ORI.B #1,CCR (core helper barrier)
            0x5280,         // ADDQ.L #1,D0
            0x60F6          // BRA.S start
        ];
        jitBus.WriteWords(code, program);
        interpreterBus.WriteWords(code, program);
        using var jit = CreateJit(jitBus);
        using var interpreter = new M68kInterpreter(interpreterBus);
        jit.Reset(code, 0xF0000);
        interpreter.Reset(code, 0xF0000);
        jit.State.D[2] = 0xCAFE_BABE;
        interpreter.State.D[2] = 0xCAFE_BABE;
        var boundary = new BatchBoundary();

        var jitExecuted = jit.ExecuteInstructions(1600, 500_000, boundary);
        var interpreterExecuted = interpreter.ExecuteInstructions(1600, null, boundary);

        Assert.Equal(interpreterExecuted, jitExecuted);
        Assert.Equal(interpreter.State.ProgramCounter, jit.State.ProgramCounter);
        Assert.Equal(interpreter.State.Cycles, jit.State.Cycles);
        Assert.Equal(interpreter.State.D, jit.State.D);
        Assert.Equal(interpreter.State.StatusRegister, jit.State.StatusRegister);
        Assert.Equal(0xCAFE_BABEu, jit.State.D[2]);
        Assert.True(jit.Counters.PureTraceBatchInstructions > 0);
    }

    private static M68kJitCore CreateJit(DirectZeroWaitBus bus)
        => new(
            bus,
            enableV2: true,
            enableV2BusAccess: true,
            enableV2FastRead: true);

    private sealed class BatchBoundary :
        IM68kPureCpuTraceBatchBoundary,
        IM68kBusAccessTraceBatchBoundary
    {
        public bool BeforeInstruction() => true;

        public void AfterInstruction(long previousCycle, long currentCycle)
        {
        }

        public bool TryBeginPureCpuTraceBatch(
            M68kCpuState state,
            long targetCycle,
            out long batchTargetCycle)
        {
            batchTargetCycle = targetCycle;
            return true;
        }

        public void AfterPureCpuTraceBatch(long previousCycle, long currentCycle, int instructionCount)
        {
        }

        public bool TryBeginBusAccessTraceBatch(
            M68kCpuState state,
            long targetCycle,
            out long batchTargetCycle)
        {
            batchTargetCycle = targetCycle;
            return true;
        }

        public void AfterBusAccessTraceBatch(long previousCycle, long currentCycle, int instructionCount)
        {
        }
    }

    private sealed class DirectZeroWaitBus :
        IM68kBus,
        IM68kCodeReader,
        IM68kJitBus,
        IM68kJitFastMemoryBus,
        IM68kJitDirectRamBus
    {
        private const int PageShift = 8;
        private const int BankShift = 20;
        private readonly byte[] _memory = new byte[1 << BankShift];
        private readonly uint[] _generations = new uint[1 << (BankShift - PageShift)];
        private readonly byte[] _bankKinds = [(byte)M68kJitDirectRamBankKind.RealFast];
        private readonly int[] _bankOffsets = [0];
        private readonly bool _realFastIsZeroWait;

        public DirectZeroWaitBus(bool realFastIsZeroWait = true)
        {
            _realFastIsZeroWait = realFastIsZeroWait;
        }

        public event Action<uint, int>? JitCodeRangeWritten;

        public int DataReads { get; private set; }

        public int ZeroWaitReadBufferProbes { get; private set; }

        public int ZeroWaitWriteBufferProbes { get; private set; }

        public int DirectWriteCompletions { get; private set; }

        public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
        {
            if (accessKind == M68kBusAccessKind.CpuDataRead)
            {
                DataReads++;
            }

            return _memory[Offset(address, 1)];
        }

        public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind)
        {
            if (accessKind == M68kBusAccessKind.CpuDataRead)
            {
                DataReads++;
            }

            return ReadWordValue(address);
        }

        public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind accessKind)
        {
            if (accessKind == M68kBusAccessKind.CpuDataRead)
            {
                DataReads++;
            }

            return ReadLongValue(address);
        }

        public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind accessKind)
        {
            _memory[Offset(address, 1)] = value;
            Invalidate(address, 1);
        }

        public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
        {
            WriteWordValue(address, value);
            Invalidate(address, 2);
        }

        public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
        {
            WriteLongValue(address, value);
            Invalidate(address, 4);
        }

        public bool HasHostTrapStub(uint address) => false;

        public bool TryInvokeHostTrap(uint instructionProgramCounter, ushort trapId, M68kCpuState state) => false;

        public void ResetExternalDevices(long cycle)
        {
        }

        public ushort ReadHostWord(uint address) => ReadWordValue(address);

        public bool IsJitCodeAddress(uint physicalAddress, int byteCount, M68kBusAccessKind accessKind)
            => Contains(physicalAddress, byteCount);

        public bool IsJitReadOnlyCodeAddress(uint physicalAddress, int byteCount, M68kBusAccessKind accessKind)
            => Contains(physicalAddress, byteCount);

        public ushort ReadJitCodeWord(uint physicalAddress) => ReadWordValue(physicalAddress);

        public uint GetJitCodePageGeneration(uint physicalAddress)
            => _generations[Offset(physicalAddress, 1) >> PageShift];

        public bool JitCodeRangeGenerationMatches(
            uint physicalAddress,
            int byteCount,
            uint startGeneration,
            uint endGeneration)
        {
            if (!Contains(physicalAddress, byteCount))
            {
                return false;
            }

            var startPage = Offset(physicalAddress, 1) >> PageShift;
            var endPage = Offset(physicalAddress + (uint)(byteCount - 1), 1) >> PageShift;
            return _generations[startPage] == startGeneration && _generations[endPage] == endGeneration;
        }

        public bool TryCaptureJitCodeSnapshot(
            uint physicalRoot,
            int maxBytes,
            out M68kJitCodeSnapshot snapshot)
        {
            if (!Contains(physicalRoot, maxBytes))
            {
                snapshot = default;
                return false;
            }

            var bytes = new byte[maxBytes];
            Array.Copy(_memory, (int)physicalRoot, bytes, 0, maxBytes);
            var firstPage = (int)physicalRoot >> PageShift;
            var lastPage = ((int)physicalRoot + maxBytes - 1) >> PageShift;
            var pages = new uint[lastPage - firstPage + 1];
            var generations = new uint[pages.Length];
            for (var index = 0; index < pages.Length; index++)
            {
                var page = firstPage + index;
                pages[index] = (uint)(page << PageShift);
                generations[index] = _generations[page];
            }

            snapshot = new M68kJitCodeSnapshot(
                physicalRoot,
                bytes,
                new M68kCodeGenerationStamp(pages, generations),
                []);
            return true;
        }

        public bool TryReadJitZeroWaitMemory(uint physicalAddress, M68kOperandSize size, out uint value)
        {
            value = size switch
            {
                M68kOperandSize.Byte => _memory[Offset(physicalAddress, 1)],
                M68kOperandSize.Word => ReadWordValue(physicalAddress),
                _ => ReadLongValue(physicalAddress)
            };
            return true;
        }

        public bool TryWriteJitZeroWaitMemory(uint physicalAddress, uint value, M68kOperandSize size)
        {
            if (size == M68kOperandSize.Byte)
            {
                _memory[Offset(physicalAddress, 1)] = (byte)value;
            }
            else if (size == M68kOperandSize.Word)
            {
                WriteWordValue(physicalAddress, (ushort)value);
            }
            else
            {
                WriteLongValue(physicalAddress, value);
            }

            return true;
        }

        public bool TryGetJitZeroWaitReadMemory(
            uint physicalAddress,
            int byteCount,
            out byte[] memory,
            out int offset,
            out M68kJitMemoryKind memoryKind)
        {
            ZeroWaitReadBufferProbes++;
            memory = _memory;
            offset = Offset(physicalAddress, byteCount);
            memoryKind = M68kJitMemoryKind.FastRam;
            return true;
        }

        public bool TryGetJitZeroWaitWriteMemory(
            uint physicalAddress,
            int byteCount,
            out byte[] memory,
            out int offset,
            out M68kJitMemoryKind memoryKind)
        {
            ZeroWaitWriteBufferProbes++;
            memory = _memory;
            offset = Offset(physicalAddress, byteCount);
            memoryKind = M68kJitMemoryKind.FastRam;
            return true;
        }

        public void CompleteJitZeroWaitWrite(uint physicalAddress, int byteCount)
            => Invalidate(physicalAddress, byteCount);

        public bool TryGetJitDirectRamMap(out M68kJitDirectRamMap map)
        {
            map = new M68kJitDirectRamMap(
                _bankKinds,
                _bankOffsets,
                [],
                _memory,
                BankShift,
                _realFastIsZeroWait);
            return true;
        }

        public void ReplayJitPseudoFastAccesses(ref long cycle, int accessCount, ulong longAccessBits)
        {
        }

        public void ReplayJitMove16PseudoFastAccesses(
            ref long retireCycle,
            bool sourcePseudoFast,
            bool destinationPseudoFast)
        {
        }

        public void CompleteJitDirectRamWrite(uint physicalAddress, int byteCount)
        {
            DirectWriteCompletions++;
            Invalidate(physicalAddress, byteCount);
        }

        public void WriteWords(uint address, ushort[] words)
        {
            foreach (var word in words)
            {
                WriteWordValue(address, word);
                address += 2;
            }
        }

        public void WriteLongValue(uint address, uint value)
        {
            WriteWordValue(address, (ushort)(value >> 16));
            WriteWordValue(address + 2, (ushort)value);
        }

        private ushort ReadWordValue(uint address)
        {
            var offset = Offset(address, 2);
            return (ushort)((_memory[offset] << 8) | _memory[offset + 1]);
        }

        public uint ReadLongValue(uint address)
            => ((uint)ReadWordValue(address) << 16) | ReadWordValue(address + 2);

        private void WriteWordValue(uint address, ushort value)
        {
            var offset = Offset(address, 2);
            _memory[offset] = (byte)(value >> 8);
            _memory[offset + 1] = (byte)value;
        }

        private void Invalidate(uint address, int byteCount)
        {
            var firstPage = Offset(address, byteCount) >> PageShift;
            var lastPage = Offset(address + (uint)(byteCount - 1), 1) >> PageShift;
            for (var page = firstPage; page <= lastPage; page++)
            {
                _generations[page]++;
            }

            JitCodeRangeWritten?.Invoke(address, byteCount);
        }

        private bool Contains(uint address, int byteCount)
            => byteCount > 0 && address <= _memory.Length - byteCount;

        private int Offset(uint address, int byteCount)
        {
            if (!Contains(address, byteCount))
            {
                throw new ArgumentOutOfRangeException(nameof(address));
            }

            return (int)address;
        }
    }
}
