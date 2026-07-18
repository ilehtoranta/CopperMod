using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Copper68k;
using CopperFloat;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

var options = BenchmarkOptions.Parse(args);
if (options.Dispatch)
{
    DispatchBenchmark.Run(options.Instructions, options.WarmupInstructions, options.Repeats);
    return;
}

if (options.ExtF80)
{
    ExtF80KernelBenchmark.Run(options.Instructions, options.WarmupInstructions, options.Repeats);
    return;
}

var workloads = CreateWorkloads()
    .Where(workload => options.Workload is null
        ? workload.IncludeByDefault
        : workload.Name.Contains(options.Workload, StringComparison.OrdinalIgnoreCase))
    .ToArray();
var availableBackends = CreateBackends();
var exactBackendMatch = options.Backend is not null &&
    availableBackends.Any(backend => backend.Name.Equals(options.Backend, StringComparison.OrdinalIgnoreCase));
var backends = availableBackends
    .Where(backend => options.Backend is null
        ? backend.IncludeByDefault
        : exactBackendMatch
            ? backend.Name.Equals(options.Backend, StringComparison.OrdinalIgnoreCase)
            : backend.Name.Contains(options.Backend, StringComparison.OrdinalIgnoreCase))
    .ToArray();

if (workloads.Length == 0)
{
    throw new InvalidOperationException($"No workload matched '{options.Workload}'.");
}

if (backends.Length == 0)
{
    throw new InvalidOperationException($"No backend matched '{options.Backend}'.");
}

if (!workloads.Any(workload => backends.Any(workload.Supports)))
{
    throw new InvalidOperationException("No selected backend supports the selected workload.");
}

Console.WriteLine($"Copper68k backend benchmark, warmup={options.WarmupInstructions}, measured={options.Instructions}, repeats={options.Repeats}, single-step={options.SingleStep}, Release={IsRelease()}");
Console.WriteLine("workload\tbackend\trepeat\tinstructions\tms\tinstr/sec\tmips\tcycles\tnative-cycles\tallocated bytes\tchecksum\tcompiled traces\tfallback instr\tpure trace instr\tbus trace instr\tzero-wait reads\tzero-wait writes");

foreach (var workload in workloads)
{
    var workloadBackends = backends.Where(workload.Supports).ToArray();
    for (var repeat = 1; repeat <= options.Repeats; repeat++)
    {
        for (var backendOffset = 0; backendOffset < workloadBackends.Length; backendOffset++)
        {
            var backend = workloadBackends[(backendOffset + repeat - 1) % workloadBackends.Length];
            var result = RunBenchmark(workload, backend, options);
            Console.WriteLine(
                $"{workload.Name}\t{backend.Name}\t{repeat}\t{result.Instructions}\t{result.Elapsed.TotalMilliseconds:F3}\t{result.InstructionsPerSecond:F0}\t{result.InstructionsPerSecond / 1_000_000.0:F3}\t{result.Cycles}\t{FormatNativeCycles(result.NativeCycles)}\t{result.AllocatedBytes}\t0x{result.Checksum:X8}\t{result.CompiledTraces}\t{result.FallbackInstructions}\t{result.PureTraceInstructions}\t{result.BusTraceInstructions}\t{result.ZeroWaitReads}\t{result.ZeroWaitWrites}");
        }
    }
}

static BenchmarkResult RunBenchmark(BenchmarkWorkload workload, BenchmarkBackend backend, BenchmarkOptions options)
{
    using var run = CreateRun(workload, backend);
    WarmUp(run.Cpu, options.WarmupInstructions, options.SingleStep);
    ResetForMeasurement(run, workload);

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var startCycles = run.Cpu.State.Cycles;
    var startNativeCycles = run.Cpu.State.NativeCycles;
    var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
    var start = Stopwatch.GetTimestamp();
    ExecuteMany(run.Cpu, options.Instructions, options.SingleStep);
    var elapsed = Stopwatch.GetElapsedTime(start);
    var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
    var counters = run.Cpu is M68kJitCore jit ? jit.Counters : default;
    var nativeCycles = run.Cpu is M68kJitCore
        ? (long?)null
        : run.Cpu.State.NativeCycles - startNativeCycles;
    return new BenchmarkResult(
        options.Instructions,
        elapsed,
        run.Cpu.State.Cycles - startCycles,
        nativeCycles,
        allocated,
        CreateChecksum(run.Cpu.State, run.Bus, workload),
        counters.CompiledTraces,
        counters.FallbackInstructions,
        counters.PureTraceBatchInstructions,
        counters.V2BusAccessBatchInstructions,
        counters.V2ZeroWaitReadRealFast + counters.V2ZeroWaitReadRom + counters.V2ZeroWaitReadOverlay,
        counters.V2ZeroWaitWriteRealFast);
}

static void ResetForMeasurement(BenchmarkRun run, BenchmarkWorkload workload)
{
    const uint programCounter = 0x1000;
    const uint stackPointer = 0x00F0_0000;

    run.Bus.RestoreSnapshot(run.InitialBusSnapshot);
    if (run.Cpu is M68kJitCore jit)
    {
        jit.ResetForBenchmark(programCounter, stackPointer);
    }
    else
    {
        run.Cpu.Reset(programCounter, stackPointer);
    }

    workload.SetupState(run.Cpu.State);
    if (run.BackendName == "JitM68040")
    {
        run.Cpu.State.CacheControlRegister = 0x0000_0001;
    }
}

static string FormatNativeCycles(long? nativeCycles)
    => nativeCycles is long value ? value.ToString(CultureInfo.InvariantCulture) : "n/a";

static BenchmarkRun CreateRun(BenchmarkWorkload workload, BenchmarkBackend backend)
{
    var bus = new BenchmarkBus(workload.MemorySize);
    WriteWords(bus, 0x1000, workload.Program);
    workload.Setup(bus);
    var cpu = backend.Create(bus);
    cpu.Reset(0x1000, 0x00F0_0000);
    workload.SetupState(cpu.State);
    if (backend.Name == "JitM68040")
    {
        cpu.State.CacheControlRegister = 0x0000_0001;
    }

    return new BenchmarkRun(cpu, bus, backend.Name, bus.CaptureSnapshot());
}

static void WarmUp(IM68kCore cpu, int instructions, bool singleStep)
{
    ExecuteMany(cpu, instructions, singleStep);
    if (cpu is M68kJitCore jit)
    {
        WarmUpJitUntilStable(jit, singleStep);
    }
}

static void WarmUpJitUntilStable(M68kJitCore jit, bool singleStep)
{
    const int chunkInstructions = 1_000_000;
    const int minInstructions = 10_000_000;
    const int maxChunks = 32;
    const int requiredStableChunks = 2;

    var stableChunks = 0;
    var executedInstructions = 0;
    var previous = JitCompilationSnapshot.Capture(jit);
    for (var chunk = 0;
        chunk < maxChunks && (executedInstructions < minInstructions || stableChunks < requiredStableChunks);
        chunk++)
    {
        ExecuteMany(jit, chunkInstructions, singleStep);
        executedInstructions += chunkInstructions;
        var current = JitCompilationSnapshot.Capture(jit);
        if (current.Equals(previous) && jit.AsyncCompilationIdle)
        {
            stableChunks++;
        }
        else
        {
            stableChunks = 0;
            previous = current;
        }
    }
}

static void ExecuteMany(IM68kCore cpu, int instructions, bool singleStep)
{
    if (singleStep)
    {
        for (var i = 0; i < instructions; i++)
        {
            cpu.ExecuteInstruction();
        }

        return;
    }

    ExecuteManyBatched(cpu, instructions);
}

static void ExecuteManyBatched(IM68kCore cpu, int instructions)
{
    if (cpu is IM68kBatchCore batch)
    {
        var remaining = instructions;
        while (remaining > 0)
        {
            var executed = batch.ExecuteInstructions(remaining, long.MaxValue, BenchmarkBoundary.Instance);
            if (executed == 0)
            {
                throw new InvalidOperationException("CPU stopped before benchmark instruction budget was consumed.");
            }

            remaining -= executed;
        }

        return;
    }

    for (var i = 0; i < instructions; i++)
    {
        cpu.ExecuteInstruction();
    }
}

static BenchmarkBackend[] CreateBackends()
    =>
    [
        new BenchmarkBackend("InterpreterM68000", bus => M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus)),
        new BenchmarkBackend("InterpreterM68010", bus => M68kCoreFactory.Default.Create(M68kCpuModel.M68010, bus)),
        new BenchmarkBackend("InterpreterM68020", bus => M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus)),
        new BenchmarkBackend("InterpreterM68030", bus => M68kCoreFactory.Default.Create(M68kCpuModel.M68030, bus)),
        new BenchmarkBackend("InterpreterM68040", bus => M68kCoreFactory.Default.Create(M68kCpuModel.M68040, bus)),
        new BenchmarkBackend(
            "InterpreterM68040General",
            bus => new M68040Interpreter(
                bus,
                M68020CpuProfile.Ocs68040Accelerator25Mhz,
                new M68kCpuState(),
                enableAdvancedFastPath: false),
            IncludeByDefault: false),
        new BenchmarkBackend("JitM68000", bus => M68kJitCore.CreateM68000(bus)),
        new BenchmarkBackend(
            "JitM68000Classic",
            bus => new M68kJitCore(bus, enableV2: false),
            IncludeByDefault: false),
        new BenchmarkBackend("JitM68040", bus => M68kJitCore.CreateM68040(bus))
    ];

static BenchmarkWorkload[] CreateWorkloads()
    =>
    [
        new BenchmarkWorkload(
            "branch-self-loop",
            [
                0x60FE // BRA.S self
            ],
            SetupNone,
            _ => { },
            1 << 20),
        new BenchmarkWorkload(
            "register-hot-loop",
            [
                0x7001, // MOVEQ #1,D0
                0x7202, // MOVEQ #2,D1
                0x7400, // MOVEQ #0,D2
                0xD081, // ADD.L D1,D0
                0x5482, // ADDQ.L #2,D2
                0x4E71, // NOP
                0x60F8  // BRA.S loop
            ],
            SetupNone,
            state =>
            {
                state.D[7] = 0x0F0F_0F0F;
            },
            1 << 20),
        new BenchmarkWorkload(
            "memory-transform-loop",
            [
                0x207C, 0x0002, 0x0000, // MOVEA.L #$00020000,A0
                0x74FF, // MOVEQ #-1,D2
                0x2018, // MOVE.L (A0)+,D0
                0xD081, // ADD.L D1,D0
                0x23C0, 0x0008, 0x0000, // MOVE.L D0,$00080000.L
                0x51CA, 0xFFF4, // DBRA D2,loop
                0x60E8 // BRA.S reset
            ],
            SetupMemoryTransform,
            state =>
            {
                state.D[1] = 0x0101_0101;
            },
            1 << 20),
        new BenchmarkWorkload(
            "directcpu-clr-loop",
            [
                0x70FF, // MOVEQ #-1,D0
                0x4200, // CLR.B D0
                0x4600, // NOT.B D0
                0x4240, // CLR.W D0
                0x4640, // NOT.W D0
                0x4280, // CLR.L D0
                0x4680, // NOT.L D0
                0x60F2  // BRA.S loop
            ],
            SetupNone,
            _ => { },
            1 << 20,
            SupportsBackend: backend => backend.Name.Contains("M68000", StringComparison.Ordinal),
            IncludeByDefault: false),
        new BenchmarkWorkload(
            "directcpu-immediate-shift-loop",
            [
                0xE380, // ASL.L #1,D0
                0xE440, // ASR.W #2,D0
                0xE709, // LSL.B #3,D1
                0xE889, // LSR.L #4,D1
                0xEB5A, // ROL.W #5,D2
                0xEC1A, // ROR.B #6,D2
                0xEF93, // ROXL.L #7,D3
                0xE053, // ROXR.W #8,D3
                0x60EE  // BRA.S loop
            ],
            SetupNone,
            state =>
            {
                state.D[0] = 0x8123_4567;
                state.D[1] = 0x89AB_CDEF;
                state.D[2] = 0x1357_9BDF;
                state.D[3] = 0x2468_ACE0;
                state.StatusRegister |= M68kCpuState.Extend;
            },
            1 << 20,
            SupportsBackend: backend => backend.Name.Contains("M68000", StringComparison.Ordinal),
            IncludeByDefault: false),
        new BenchmarkWorkload(
            "directcpu-register-bit-loop",
            [
                0x0800, 0x001F, // BTST #31,D0
                0x0840, 0x0000, // BCHG #0,D0
                0x0881, 0x0010, // BCLR #16,D1
                0x08C1, 0x000F, // BSET #15,D1
                0x0500, // BTST D2,D0
                0x0740, // BCHG D3,D0
                0x0981, // BCLR D4,D1
                0x0BC1, // BSET D5,D1
                0x60E6  // BRA.S loop
            ],
            SetupNone,
            state =>
            {
                state.D[0] = 0x8000_0001;
                state.D[1] = 0x0001_0000;
                state.D[2] = 63;
                state.D[3] = 32;
                state.D[4] = 16;
                state.D[5] = 15;
            },
            1 << 20,
            SupportsBackend: backend => backend.Name.Contains("M68000", StringComparison.Ordinal),
            IncludeByDefault: false),
        new BenchmarkWorkload(
            "directcpu-address-register-loop",
            [
                0x2040,             // MOVEA.L D0,A0
                0x327C, 0x7FFF,     // MOVEA.W #$7FFF,A1
                0x247C, 0x0002, 0x0000, // MOVEA.L #$00020000,A2
                0xB1C0,             // CMPA.L D0,A0
                0xB2C1,             // CMPA.W D1,A1
                0x47E8, 0x0004,     // LEA 4(A0),A3
                0x49D3,             // LEA (A3),A4
                0x5288,             // ADDQ.L #1,A0
                0x5388,             // SUBQ.L #1,A0
                0x60E4              // BRA.S loop
            ],
            SetupNone,
            state =>
            {
                state.D[0] = 0x0001_0000;
                state.D[1] = 0x0000_7FFF;
            },
            1 << 20,
            SupportsBackend: backend => backend.Name.Contains("M68000", StringComparison.Ordinal),
            IncludeByDefault: false),
        new BenchmarkWorkload(
            "directcpu-multiply-loop",
            [
                0x7203,       // MOVEQ #3,D1
                0xC2C0,       // MULU.W D0,D1
                0x76FD,       // MOVEQ #-3,D3
                0xC7C2,       // MULS.W D2,D3
                0x7805,       // MOVEQ #5,D4
                0xC8FC, 0x0007, // MULU.W #7,D4
                0x7AFB,       // MOVEQ #-5,D5
                0xCBFC, 0xFFF9, // MULS.W #-7,D5
                0x60EA        // BRA.S loop
            ],
            SetupNone,
            state =>
            {
                state.D[0] = 0x1234;
                state.D[2] = 0xFFFD;
            },
            1 << 20,
            SupportsBackend: backend => backend.Name.Contains("M68000", StringComparison.Ordinal),
            IncludeByDefault: false),
        new BenchmarkWorkload(
            "directcpu-divide-loop",
            [
                0x7264,       // MOVEQ #100,D1
                0x82C0,       // DIVU.W D0,D1
                0x769C,       // MOVEQ #-100,D3
                0x87C2,       // DIVS.W D2,D3
                0x7864,       // MOVEQ #100,D4
                0x88FC, 0x0007, // DIVU.W #7,D4
                0x7A9C,       // MOVEQ #-100,D5
                0x8BFC, 0xFFF9, // DIVS.W #-7,D5
                0x60EA        // BRA.S loop
            ],
            SetupNone,
            state =>
            {
                state.D[0] = 3;
                state.D[2] = 0xFFFD;
            },
            1 << 20,
            SupportsBackend: backend => backend.Name.Contains("M68000", StringComparison.Ordinal),
            IncludeByDefault: false),
        new BenchmarkWorkload(
            "fpu-register-arithmetic-loop",
            [
                0xF200, 0x00A2, // FADD.X FP0,FP1
                0xF200, 0x00A8, // FSUB.X FP0,FP1
                0xF200, 0x00A3, // FMUL.X FP0,FP1
                0xF200, 0x00A0, // FDIV.X FP0,FP1
                0x60EE          // BRA.S loop
            ],
            SetupNone,
            SetupFpuArithmetic,
            1 << 20,
            backend => backend.Name.Contains("M68040", StringComparison.Ordinal),
            ChecksumFpu,
            IncludeByDefault: false),
        CreateFpuRegisterLoop("fpu-add-loop", 0x00A2, SetupFpuArithmetic),
        CreateFpuRegisterLoop("fpu-subtract-loop", 0x00A8, SetupFpuArithmetic),
        CreateFpuRegisterLoop("fpu-multiply-loop", 0x00A3, SetupFpuArithmetic),
        CreateFpuRegisterLoop("fpu-divide-loop", 0x00A0, SetupFpuArithmetic),
        CreateFpuRegisterLoop("fpu-square-root-loop", 0x0084, SetupFpuSquareRoot),
        CreateFpuRegisterLoop("fpu-forced-single-square-root-loop", 0x00C1, SetupFpuSquareRoot),
        CreateFpuRegisterLoop("fpu-forced-double-square-root-loop", 0x00C5, SetupFpuSquareRoot),
        CreateFpuRegisterLoop("fpu-absolute-loop", 0x0D98, SetupFpuUnary),
        new BenchmarkWorkload(
            "fpu-forced-single-arithmetic-loop",
            [
                0xF200, 0x0080, // FMOVE.X FP0,FP1
                0xF200, 0x00E2, // FADD.S FP0,FP1
                0xF200, 0x00E8, // FSUB.S FP0,FP1
                0xF200, 0x00E3, // FMUL.S FP0,FP1
                0x60EE          // BRA.S loop
            ],
            SetupNone,
            SetupFpuArithmetic,
            1 << 20,
            backend => backend.Name.Contains("M68040", StringComparison.Ordinal),
            ChecksumFpu,
            IncludeByDefault: false),
        new BenchmarkWorkload(
            "fpu-forced-single-divide-loop",
            [
                0xF200, 0x0080, // FMOVE.X FP0,FP1
                0xF200, 0x00E0, // FDIV.S FP0,FP1
                0x60F6          // BRA.S loop
            ],
            SetupNone,
            SetupFpuForcedDivide,
            1 << 20,
            backend => backend.Name.Contains("M68040", StringComparison.Ordinal),
            ChecksumFpu,
            IncludeByDefault: false),
        new BenchmarkWorkload(
            "fpu-forced-double-divide-loop",
            [
                0xF200, 0x0080, // FMOVE.X FP0,FP1
                0xF200, 0x00E4, // FDIV.D FP0,FP1
                0x60F6          // BRA.S loop
            ],
            SetupNone,
            SetupFpuForcedDivide,
            1 << 20,
            backend => backend.Name.Contains("M68040", StringComparison.Ordinal),
            ChecksumFpu,
            IncludeByDefault: false),
        new BenchmarkWorkload(
            "fpu-forced-double-arithmetic-loop",
            [
                0xF200, 0x0080, // FMOVE.X FP0,FP1
                0xF200, 0x00E6, // FADD.D FP0,FP1
                0xF200, 0x00EC, // FSUB.D FP0,FP1
                0xF200, 0x00E7, // FMUL.D FP0,FP1
                0x60EE          // BRA.S loop
            ],
            SetupNone,
            SetupFpuArithmetic,
            1 << 20,
            backend => backend.Name.Contains("M68040", StringComparison.Ordinal),
            ChecksumFpu,
            IncludeByDefault: false),
        new BenchmarkWorkload(
            "fpu-conditional-register-loop",
            [
                0xF240, 0x000F, // FST D0
                0x60FA          // BRA.S loop
            ],
            SetupNone,
            SetupFpuArithmetic,
            1 << 20,
            backend => backend.Name.Contains("M68040", StringComparison.Ordinal),
            ChecksumFpu,
            IncludeByDefault: false),
        new BenchmarkWorkload(
            "fpu-compare-branch-loop",
            [
                0xF200, 0x08B8, // FCMP.X FP2,FP1
                0xF281, 0xFFFA  // FBEQ.W loop
            ],
            SetupNone,
            SetupFpuCompare,
            1 << 20,
            backend => backend.Name.Contains("M68040", StringComparison.Ordinal),
            ChecksumFpu,
            IncludeByDefault: false),
        new BenchmarkWorkload(
            "fpu-test-set-loop",
            [
                0xF200, 0x003A, // FTST.X FP0
                0xF240, 0x0001, // FSEQ D0
                0x60F6          // BRA.S loop
            ],
            SetupNone,
            SetupFpuArithmetic,
            1 << 20,
            backend => backend.Name.Contains("M68040", StringComparison.Ordinal),
            ChecksumFpu,
            IncludeByDefault: false)
    ];

static BenchmarkWorkload CreateFpuRegisterLoop(
    string name,
    ushort command,
    Action<M68kCpuState> setupState)
    => new(
        name,
        [
            0xF200, command,
            0x60FA // BRA.S loop
        ],
        SetupNone,
        setupState,
        1 << 20,
        backend => backend.Name.Contains("M68040", StringComparison.Ordinal),
        ChecksumFpu,
        IncludeByDefault: false);

static void SetupNone(BenchmarkBus bus)
{
    _ = bus;
}

static void SetupMemoryTransform(BenchmarkBus bus)
{
    for (var offset = 0u; offset < 0x1000; offset += 4)
    {
        bus.WriteLongValue(0x0002_0000u + offset, 0x1020_3040u + offset);
    }
}

static void SetupFpuArithmetic(M68kCpuState state)
{
    var one = ExtF80Math.FromInt32(1);
    state.M68040Fpu.FP[0] = one;
    state.M68040Fpu.FP[1] = one;
}

static void SetupFpuCompare(M68kCpuState state)
{
    SetupFpuArithmetic(state);
    state.M68040Fpu.FP[2] = ExtF80Math.FromInt32(1);
}

static void SetupFpuForcedDivide(M68kCpuState state)
{
    state.M68040Fpu.FP[0] = ExtF80Math.FromInt32(3);
    state.M68040Fpu.FP[1] = ExtF80Math.FromInt32(1);
}

static void SetupFpuSquareRoot(M68kCpuState state)
{
    state.M68040Fpu.FP[0] = ExtF80Math.FromInt32(2);
    state.M68040Fpu.FP[1] = ExtF80Math.FromInt32(1);
}

static void SetupFpuUnary(M68kCpuState state)
    => state.M68040Fpu.FP[3] = ExtF80Math.FromInt32(-2);

static uint ChecksumFpu(M68kCpuState state)
{
    var checksum = 2166136261u;
    foreach (var value in state.M68040Fpu.FP)
    {
        checksum = unchecked((checksum * 16777619u) ^ value.SignExponent);
        checksum = unchecked((checksum * 16777619u) ^ (uint)value.Significand);
        checksum = unchecked((checksum * 16777619u) ^ (uint)(value.Significand >> 32));
    }

    checksum = unchecked((checksum * 16777619u) ^ state.M68040Fpu.Fpsr);
    return unchecked((checksum * 16777619u) ^ state.M68040Fpu.Fpiar);
}

static uint CreateChecksum(M68kCpuState state, BenchmarkBus bus, BenchmarkWorkload workload)
{
    var checksum = state.ProgramCounter ^ state.LastInstructionProgramCounter ^ state.LastOpcode;
    checksum = unchecked((checksum * 16777619u) ^ state.D[0]);
    checksum = unchecked((checksum * 16777619u) ^ state.D[1]);
    checksum = unchecked((checksum * 16777619u) ^ state.D[2]);
    checksum = unchecked((checksum * 16777619u) ^ state.StatusRegister);
    checksum = unchecked((checksum * 16777619u) ^ (uint)state.Cycles);
    checksum = unchecked((checksum * 16777619u) ^ bus.ReadLongValue(0x0008_0000));
    if (workload.AdditionalChecksum is not null)
    {
        checksum = unchecked((checksum * 16777619u) ^ workload.AdditionalChecksum(state));
    }

    return checksum;
}

static void WriteWords(BenchmarkBus bus, uint address, ushort[] words)
{
    for (var i = 0; i < words.Length; i++)
    {
        bus.WriteWordValue(address + (uint)(i * 2), words[i]);
    }
}

static bool IsRelease()
{
#if DEBUG
    return false;
#else
    return true;
#endif
}

internal sealed record BenchmarkOptions(
    int WarmupInstructions,
    int Instructions,
    int Repeats,
    string? Backend,
    string? Workload,
    bool Dispatch,
    bool ExtF80,
    bool SingleStep)
{
    public static BenchmarkOptions Parse(string[] args)
    {
        var warmup = 500_000;
        var instructions = 5_000_000;
        var repeats = 6;
        string? backend = null;
        string? workload = null;
        var dispatch = false;
        var extF80 = false;
        var singleStep = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--dispatch":
                    dispatch = true;
                    break;
                case "--extf80":
                    extF80 = true;
                    break;
                case "--single-step":
                    singleStep = true;
                    break;
                case "--warmup":
                    warmup = ParseInt(args, ref i);
                    break;
                case "--instructions":
                    instructions = ParseInt(args, ref i);
                    break;
                case "--repeats":
                    repeats = ParseInt(args, ref i);
                    break;
                case "--backend":
                    backend = ParseString(args, ref i);
                    break;
                case "--workload":
                    workload = ParseString(args, ref i);
                    break;
                case "--help":
                case "-h":
                    Console.WriteLine("Usage: dotnet run -c Release --project Copper68k.Benchmarks -- [--dispatch|--extf80] [--single-step] [--warmup N] [--instructions N] [--repeats N] [--backend text] [--workload text]");
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }
        }

        return new BenchmarkOptions(warmup, instructions, repeats, backend, workload, dispatch, extF80, singleStep);
    }

    private static int ParseInt(string[] args, ref int index)
    {
        var text = ParseString(args, ref index);
        return int.Parse(text);
    }

    private static string ParseString(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for '{args[index]}'.");
        }

        index++;
        return args[index];
    }
}

internal static class ExtF80KernelBenchmark
{
    private static readonly ExtF80Context Context = new(
        ExtF80RoundingMode.ToNearestEven,
        ExtF80Precision.Extended,
        ExtF80TininessMode.BeforeRounding);

    public static void Run(int operations, int warmupOperations, int repeats)
    {
        Console.WriteLine($"CopperFloat extF80 kernel benchmark, warmup={warmupOperations}, measured={operations}, repeats={repeats}, Release={IsReleaseBuild()}");
        Console.WriteLine("operation\trepeat\toperations\tms\tops/sec\tmops\tallocated bytes\tchecksum");

        foreach (var operation in Enum.GetValues<ExtF80KernelOperation>())
        {
            _ = Execute(operation, warmupOperations);
            for (var repeat = 1; repeat <= repeats; repeat++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                var result = Execute(operation, operations);
                var elapsed = Stopwatch.GetElapsedTime(start);
                var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
                var operationsPerSecond = operations / elapsed.TotalSeconds;
                Console.WriteLine(
                    $"{operation.ToString().ToLowerInvariant()}\t{repeat}\t{operations}\t{elapsed.TotalMilliseconds:F3}\t{operationsPerSecond:F0}\t{operationsPerSecond / 1_000_000.0:F3}\t{allocated}\t0x{Checksum(result):X8}");
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ExtF80KernelResult Execute(ExtF80KernelOperation operation, int operations)
        => operation switch
        {
            ExtF80KernelOperation.Add => ExecuteAdd(operations),
            ExtF80KernelOperation.Subtract => ExecuteSubtract(operations),
            ExtF80KernelOperation.Multiply => ExecuteMultiply(operations),
            ExtF80KernelOperation.Divide => ExecuteDivide(operations),
            ExtF80KernelOperation.SquareRoot => ExecuteSquareRoot(operations),
            ExtF80KernelOperation.Round => ExecuteRound(operations),
            _ => ExecuteForced(operation, operations)
        };

    private static ExtF80KernelResult ExecuteForced(ExtF80KernelOperation operation, int operations)
    {
        var operationIndex = ((int)operation - (int)ExtF80KernelOperation.SingleDivide) / 2;
        var reference = (((int)operation - (int)ExtF80KernelOperation.SingleDivide) & 1) != 0;
        var precision = operationIndex == 0 ? ExtF80Precision.Single : ExtF80Precision.Double;
        var context = new ExtF80Context(
            ExtF80RoundingMode.ToNearestEven,
            precision,
            ExtF80TininessMode.BeforeRounding);
        var left = ExtF80Math.FromInt32(1);
        var right = ExtF80Math.FromInt32(3);
        var result = new FloatingPointResult<ExtF80>(left, FloatingPointExceptionFlags.None);
        for (var i = 0; i < operations; i++)
        {
            result = reference
                ? ExtF80Math.DivideReference(left, right, context)
                : ExtF80Math.Divide(left, right, context);
        }

        return new ExtF80KernelResult(result.Value, result.Flags);
    }

    private static ExtF80KernelResult ExecuteAdd(int operations)
    {
        var one = ExtF80Math.FromInt32(1);
        var value = one;
        var flags = FloatingPointExceptionFlags.None;
        for (var i = 0; i < operations; i++)
        {
            var result = ExtF80Math.Add(value, one, Context);
            value = result.Value;
            flags = result.Flags;
        }

        return new ExtF80KernelResult(value, flags);
    }

    private static ExtF80KernelResult ExecuteSubtract(int operations)
    {
        var one = ExtF80Math.FromInt32(1);
        var value = ExtF80Math.FromInt64(long.MaxValue);
        var flags = FloatingPointExceptionFlags.None;
        for (var i = 0; i < operations; i++)
        {
            var result = ExtF80Math.Subtract(value, one, Context);
            value = result.Value;
            flags = result.Flags;
        }

        return new ExtF80KernelResult(value, flags);
    }

    private static ExtF80KernelResult ExecuteMultiply(int operations)
    {
        var factor = ExtF80.FromBits(0x3FFF, 0x8000_0001_0000_0000);
        var value = ExtF80Math.FromInt32(1);
        var flags = FloatingPointExceptionFlags.None;
        for (var i = 0; i < operations; i++)
        {
            var result = ExtF80Math.Multiply(value, factor, Context);
            value = result.Value;
            flags = result.Flags;
        }

        return new ExtF80KernelResult(value, flags);
    }

    private static ExtF80KernelResult ExecuteDivide(int operations)
    {
        var factor = ExtF80.FromBits(0x3FFF, 0x8000_0001_0000_0000);
        var value = ExtF80Math.FromInt32(1);
        var flags = FloatingPointExceptionFlags.None;
        for (var i = 0; i < operations; i++)
        {
            var result = ExtF80Math.Divide(value, factor, Context);
            value = result.Value;
            flags = result.Flags;
        }

        return new ExtF80KernelResult(value, flags);
    }

    private static ExtF80KernelResult ExecuteSquareRoot(int operations)
    {
        var value = ExtF80Math.FromInt32(2);
        var flags = FloatingPointExceptionFlags.None;
        for (var i = 0; i < operations; i++)
        {
            var result = ExtF80Math.SquareRoot(value, Context);
            value = result.Value;
            flags = result.Flags;
        }

        return new ExtF80KernelResult(value, flags);
    }

    private static ExtF80KernelResult ExecuteRound(int operations)
    {
        var value = ExtF80.FromBits(0x3FFF, 0x8000_0000_0000_0001);
        var flags = FloatingPointExceptionFlags.None;
        for (var i = 0; i < operations; i++)
        {
            var result = ExtF80Math.Round(value, Context);
            value = result.Value;
            flags = result.Flags;
        }

        return new ExtF80KernelResult(value, flags);
    }

    private static uint Checksum(ExtF80KernelResult result)
    {
        var checksum = ((uint)result.Value.SignExponent << 16) ^ (uint)result.Value.Significand;
        checksum = unchecked((checksum * 16777619u) ^ (uint)(result.Value.Significand >> 32));
        return unchecked((checksum * 16777619u) ^ (uint)result.Flags);
    }

    private static bool IsReleaseBuild()
    {
#if DEBUG
        return false;
#else
        return true;
#endif
    }
}

internal enum ExtF80KernelOperation
{
    Add,
    Subtract,
    Multiply,
    Divide,
    SquareRoot,
    Round,
    SingleDivide,
    SingleDivideReference,
    DoubleDivide,
    DoubleDivideReference
}

internal readonly record struct ExtF80KernelResult(
    ExtF80 Value,
    FloatingPointExceptionFlags Flags);

internal sealed record BenchmarkBackend(
    string Name,
    Func<BenchmarkBus, IM68kCore> Create,
    bool IncludeByDefault = true);

internal readonly record struct JitCompilationSnapshot(
    long CompiledTraces,
    long AsyncRequestsQueued,
    long AsyncRequestsDeduped,
    long AsyncPriorityBumps,
    long AsyncWorkerCompilesCompleted,
    long AsyncCompletedInstalled,
    long AsyncCompletedDiscardedStale,
    long AsyncCompletedDiscardedSuperseded,
    long V2TierPromotions,
    long V2TierPressurePromotions,
    long V2TraceHandoffQueuedNotReady)
{
    public static JitCompilationSnapshot Capture(M68kJitCore jit)
    {
        var counters = jit.Counters;
        return new JitCompilationSnapshot(
            counters.CompiledTraces,
            counters.AsyncRequestsQueued,
            counters.AsyncRequestsDeduped,
            counters.AsyncPriorityBumps,
            counters.AsyncWorkerCompilesCompleted,
            counters.AsyncCompletedInstalled,
            counters.AsyncCompletedDiscardedStale,
            counters.AsyncCompletedDiscardedSuperseded,
            counters.V2TierPromotions,
            counters.V2TierPressurePromotions,
            counters.V2TraceHandoffQueuedNotReady);
    }
}

internal sealed record BenchmarkWorkload(
    string Name,
    ushort[] Program,
    Action<BenchmarkBus> Setup,
    Action<M68kCpuState> SetupState,
    int MemorySize,
    Func<BenchmarkBackend, bool>? SupportsBackend = null,
    Func<M68kCpuState, uint>? AdditionalChecksum = null,
    bool IncludeByDefault = true)
{
    public bool Supports(BenchmarkBackend backend)
        => SupportsBackend?.Invoke(backend) ?? true;
}

internal sealed record BenchmarkRun(
    IM68kCore Cpu,
    BenchmarkBus Bus,
    string BackendName,
    BenchmarkBusSnapshot InitialBusSnapshot) : IDisposable
{
    public void Dispose()
        => Cpu.Dispose();
}

internal readonly record struct BenchmarkResult(
    int Instructions,
    TimeSpan Elapsed,
    long Cycles,
    long? NativeCycles,
    long AllocatedBytes,
    uint Checksum,
    long CompiledTraces,
    long FallbackInstructions,
    long PureTraceInstructions,
    long BusTraceInstructions,
    long ZeroWaitReads,
    long ZeroWaitWrites)
{
    public double InstructionsPerSecond => Instructions / Elapsed.TotalSeconds;
}

internal sealed record BenchmarkBusSnapshot(byte[] Memory, uint[] CodePageGenerations);

internal sealed class BenchmarkBoundary :
    IM68kInstructionBoundary,
    IM68kPureCpuTraceBatchBoundary,
    IM68kBusAccessTraceBatchBoundary
{
    public static BenchmarkBoundary Instance { get; } = new BenchmarkBoundary();

    public bool BeforeInstruction()
        => true;

    public void AfterInstruction(long previousCycle, long currentCycle)
    {
        _ = previousCycle;
        _ = currentCycle;
    }

    public bool TryBeginPureCpuTraceBatch(M68kCpuState state, long targetCycle, out long batchTargetCycle)
    {
        _ = state;
        batchTargetCycle = targetCycle;
        return true;
    }

    public void AfterPureCpuTraceBatch(long previousCycle, long currentCycle, int instructionCount)
    {
        _ = previousCycle;
        _ = currentCycle;
        _ = instructionCount;
    }

    public bool TryBeginBusAccessTraceBatch(M68kCpuState state, long targetCycle, out long batchTargetCycle)
    {
        _ = state;
        batchTargetCycle = targetCycle;
        return true;
    }

    public void AfterBusAccessTraceBatch(long previousCycle, long currentCycle, int instructionCount)
    {
        _ = previousCycle;
        _ = currentCycle;
        _ = instructionCount;
    }
}

internal sealed class BenchmarkBus :
    IM68kBus,
    IM68kCodeReader,
    IM68kFastMemoryBus,
    IM68kFixedPlanRunBus,
    IM68kJitBus,
    IM68kJitFastMemoryBus,
    IM68kJitTimedMemoryBus,
    IM68kJitDirectRamBus,
    IM68kFixedPhysicalAddressMap
{
    private const int CodeGenerationPageShift = 8;
    private const int CodeGenerationPageSize = 1 << CodeGenerationPageShift;
    private readonly byte[] _memory;
    private readonly uint[] _codePageGenerations;
    private readonly uint[][] _fixedPlanRunWindowGenerations;

    public BenchmarkBus(int memorySize)
    {
        if (memorySize <= 0 || (memorySize & (memorySize - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(memorySize), "Memory size must be a positive power of two.");
        }

        _memory = new byte[memorySize];
        _codePageGenerations = new uint[Math.Max(1, memorySize >> CodeGenerationPageShift)];
        _fixedPlanRunWindowGenerations = Enumerable.Range(
                0,
                Math.Max(1, memorySize >> CodeGenerationPageShift))
            .Select(_ => new[] { 1u })
            .ToArray();
    }

    public BenchmarkBusSnapshot CaptureSnapshot()
        => new((byte[])_memory.Clone(), (uint[])_codePageGenerations.Clone());

    public void RestoreSnapshot(BenchmarkBusSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Memory.Length != _memory.Length ||
            snapshot.CodePageGenerations.Length != _codePageGenerations.Length)
        {
            throw new ArgumentException("The benchmark bus snapshot does not match this bus.", nameof(snapshot));
        }

        Array.Copy(snapshot.Memory, _memory, _memory.Length);
        Array.Copy(snapshot.CodePageGenerations, _codePageGenerations, _codePageGenerations.Length);
    }

    public event Action<uint, int>? JitCodeRangeWritten;

    public bool TryGetCpuPhysicalAddressMappedRange(
        M68kBusAccessKind accessKind,
        out uint startAddress,
        out uint endAddress)
    {
        _ = accessKind;
        startAddress = 0;
        endAddress = uint.MaxValue;
        return true;
    }

    public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
    {
        _ = cycle;
        _ = accessKind;
        return _memory[Normalize(address)];
    }

    public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind)
    {
        _ = cycle;
        _ = accessKind;
        return ReadWordValue(address);
    }

    public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind accessKind)
    {
        _ = cycle;
        _ = accessKind;
        return ReadLongValue(address);
    }

    public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind accessKind)
    {
        _ = accessKind;
        _memory[Normalize(address)] = value;
        InvalidateCode(address, 1);
        _ = cycle;
    }

    public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
    {
        _ = accessKind;
        WriteWordValue(address, value);
        InvalidateCode(address, 2);
        _ = cycle;
    }

    public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
    {
        _ = accessKind;
        WriteLongValue(address, value);
        InvalidateCode(address, 4);
        _ = cycle;
    }

    public bool HasHostTrapStub(uint address)
    {
        _ = address;
        return false;
    }

    public bool TryInvokeHostTrap(uint instructionProgramCounter, ushort trapId, M68kCpuState state)
    {
        _ = instructionProgramCounter;
        _ = trapId;
        _ = state;
        return false;
    }

    public void ResetExternalDevices(long cycle)
    {
        _ = cycle;
    }

    public ushort ReadHostWord(uint address)
        => ReadWordValue(address);

    public bool TryReadFastByte(
        uint address,
        M68kBusAccessKind accessKind,
        out byte value)
    {
        _ = accessKind;
        if (!IsRangeMapped(address, 1))
        {
            value = 0;
            return false;
        }

        value = _memory[Normalize(address)];
        return true;
    }

    public bool TryReadFastWord(
        uint address,
        M68kBusAccessKind accessKind,
        out ushort value)
    {
        _ = accessKind;
        if (!IsRangeMapped(address, 2))
        {
            value = 0;
            return false;
        }

        value = ReadWordValue(address);
        return true;
    }

    public bool TryReadFastLong(
        uint address,
        M68kBusAccessKind accessKind,
        out uint value)
    {
        _ = accessKind;
        if (!IsRangeMapped(address, 4))
        {
            value = 0;
            return false;
        }

        value = ReadLongValue(address);
        return true;
    }

    public bool TryWriteFastByte(
        uint address,
        byte value,
        M68kBusAccessKind accessKind)
    {
        _ = accessKind;
        if (!IsRangeMapped(address, 1))
        {
            return false;
        }

        _memory[Normalize(address)] = value;
        InvalidateCode(address, 1);
        return true;
    }

    public bool TryWriteFastWord(
        uint address,
        ushort value,
        M68kBusAccessKind accessKind)
    {
        _ = accessKind;
        if (!IsRangeMapped(address, 2))
        {
            return false;
        }

        WriteWordValue(address, value);
        InvalidateCode(address, 2);
        return true;
    }

    public bool TryWriteFastLong(
        uint address,
        uint value,
        M68kBusAccessKind accessKind)
    {
        _ = accessKind;
        if (!IsRangeMapped(address, 4))
        {
            return false;
        }

        WriteLongValue(address, value);
        InvalidateCode(address, 4);
        return true;
    }

    public bool TryGetFixedPlanRunWindow(uint address, out M68kFixedPlanRunWindow window)
    {
        const uint windowSize = 256;
        window = default;
        if ((address & 1) != 0 || !IsRangeMapped(address, 2))
        {
            return false;
        }

        var startAddress = address & ~(windowSize - 1);
        var endAddress = Math.Min(
            startAddress + windowSize,
            (uint)_memory.Length);
        var generationSource = _fixedPlanRunWindowGenerations[
            Normalize(startAddress) >> CodeGenerationPageShift];
        var fetchWindow = new M68kInstructionFetchWindow(
            _memory,
            Normalize(startAddress),
            startAddress,
            endAddress,
            (uint)(_memory.Length - 1),
            0,
            generationSource,
            generationSource[0]);
        window = new M68kFixedPlanRunWindow(
            in fetchWindow,
            readyCycleOffset: 0,
            nextBusCycleOffset: 0,
            deferredBatchEligible: false);
        return true;
    }

    public bool IsCpuPhysicalAddressMapped(uint address, int byteCount, M68kBusAccessKind accessKind)
    {
        _ = accessKind;
        return IsRangeMapped(address, byteCount);
    }

    public bool IsJitCodeAddress(uint physicalAddress, int byteCount, M68kBusAccessKind accessKind)
    {
        _ = accessKind;
        return IsRangeMapped(physicalAddress, byteCount);
    }

    public bool IsJitReadOnlyCodeAddress(uint physicalAddress, int byteCount, M68kBusAccessKind accessKind)
    {
        _ = accessKind;
        return IsRangeMapped(physicalAddress, byteCount);
    }

    public ushort ReadJitCodeWord(uint physicalAddress)
        => ReadWordValue(physicalAddress);

    public uint GetJitCodePageGeneration(uint physicalAddress)
        => _codePageGenerations[CodePageIndex(physicalAddress)];

    public bool JitCodeRangeGenerationMatches(uint physicalAddress, int byteCount, uint startGeneration, uint endGeneration)
    {
        if (!IsRangeMapped(physicalAddress, byteCount))
        {
            return false;
        }

        var startPage = CodePageIndex(physicalAddress);
        var endPage = CodePageIndex(physicalAddress + (uint)Math.Max(0, byteCount - 1));
        return _codePageGenerations[startPage] == startGeneration &&
            _codePageGenerations[endPage] == endGeneration;
    }

    public bool TryCaptureJitCodeSnapshot(uint physicalRoot, int maxBytes, out M68kJitCodeSnapshot snapshot)
    {
        if (!IsRangeMapped(physicalRoot, maxBytes))
        {
            snapshot = default;
            return false;
        }

        var bytes = new byte[maxBytes];
        var offset = Normalize(physicalRoot);
        Array.Copy(_memory, offset, bytes, 0, maxBytes);
        var startPage = CodePageIndex(physicalRoot);
        var endPage = CodePageIndex(physicalRoot + (uint)Math.Max(0, maxBytes - 1));
        var pages = new uint[endPage - startPage + 1];
        var generations = new uint[pages.Length];
        for (var i = 0; i < pages.Length; i++)
        {
            var page = startPage + i;
            pages[i] = (uint)(page * CodeGenerationPageSize);
            generations[i] = _codePageGenerations[page];
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
        if (!IsRangeMapped(physicalAddress, ByteCount(size)))
        {
            value = 0;
            return false;
        }

        value = size switch
        {
            M68kOperandSize.Byte => _memory[Normalize(physicalAddress)],
            M68kOperandSize.Word => ReadWordValue(physicalAddress),
            M68kOperandSize.Long => ReadLongValue(physicalAddress),
            _ => 0
        };
        return true;
    }

    public bool TryWriteJitZeroWaitMemory(uint physicalAddress, uint value, M68kOperandSize size)
    {
        if (!IsRangeMapped(physicalAddress, ByteCount(size)))
        {
            return false;
        }

        switch (size)
        {
            case M68kOperandSize.Byte:
                _memory[Normalize(physicalAddress)] = (byte)value;
                InvalidateCode(physicalAddress, 1);
                break;
            case M68kOperandSize.Word:
                WriteWordValue(physicalAddress, (ushort)value);
                InvalidateCode(physicalAddress, 2);
                break;
            case M68kOperandSize.Long:
                WriteLongValue(physicalAddress, value);
                InvalidateCode(physicalAddress, 4);
                break;
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
        memoryKind = M68kJitMemoryKind.FastRam;
        return TryGetJitMemory(physicalAddress, byteCount, out memory, out offset);
    }

    public bool TryGetJitZeroWaitWriteMemory(
        uint physicalAddress,
        int byteCount,
        out byte[] memory,
        out int offset,
        out M68kJitMemoryKind memoryKind)
    {
        memoryKind = M68kJitMemoryKind.FastRam;
        return TryGetJitMemory(physicalAddress, byteCount, out memory, out offset);
    }

    public bool TryGetJitDirectRamMap(out M68kJitDirectRamMap map)
    {
        const int bankShift = 20;
        var bankKinds = new byte[1 << (24 - bankShift)];
        var bankOffsets = new int[bankKinds.Length];
        Array.Fill(bankKinds, (byte)M68kJitDirectRamBankKind.RealFast);
        map = new M68kJitDirectRamMap(
            bankKinds,
            bankOffsets,
            [],
            _memory,
            bankShift,
            realFastIsZeroWait: true);
        return true;
    }

    public void ReplayJitPseudoFastAccesses(ref long cycle, int accessCount, ulong longAccessBits)
    {
        _ = cycle;
        _ = accessCount;
        _ = longAccessBits;
    }

    public void ReplayJitMove16PseudoFastAccesses(
        ref long retireCycle,
        bool sourcePseudoFast,
        bool destinationPseudoFast)
    {
        _ = retireCycle;
        _ = sourcePseudoFast;
        _ = destinationPseudoFast;
    }

    public void CompleteJitDirectRamWrite(uint physicalAddress, int byteCount)
        => InvalidateCode(physicalAddress, byteCount);

    public void CompleteJitZeroWaitWrite(uint physicalAddress, int byteCount)
        => InvalidateCode(physicalAddress, byteCount);

    public uint ReadJitTimedMemory(ref long cycle, uint physicalAddress, M68kOperandSize size)
    {
        _ = cycle;
        _ = TryReadJitZeroWaitMemory(physicalAddress, size, out var value);
        return value;
    }

    public void WriteJitTimedMemory(ref long cycle, uint physicalAddress, uint value, M68kOperandSize size)
    {
        _ = cycle;
        _ = TryWriteJitZeroWaitMemory(physicalAddress, value, size);
    }

    public bool TryReadJitMaxSpeedDeviceRegister(uint physicalAddress, M68kOperandSize size, out uint value)
    {
        _ = physicalAddress;
        _ = size;
        value = 0;
        return false;
    }

    public bool TryWriteJitMaxSpeedDeviceRegister(uint physicalAddress, uint value, M68kOperandSize size, long cycle)
    {
        _ = physicalAddress;
        _ = value;
        _ = size;
        _ = cycle;
        return false;
    }

    public ushort ReadWordValue(uint address)
    {
        var offset = Normalize(address);
        return (ushort)((_memory[offset] << 8) | _memory[Normalize(address + 1)]);
    }

    public uint ReadLongValue(uint address)
        => ((uint)ReadWordValue(address) << 16) | ReadWordValue(address + 2);

    public void WriteWordValue(uint address, ushort value)
    {
        var offset = Normalize(address);
        _memory[offset] = (byte)(value >> 8);
        _memory[Normalize(address + 1)] = (byte)value;
    }

    public void WriteLongValue(uint address, uint value)
    {
        WriteWordValue(address, (ushort)(value >> 16));
        WriteWordValue(address + 2, (ushort)value);
    }

    private bool TryGetJitMemory(uint physicalAddress, int byteCount, out byte[] memory, out int offset)
    {
        if (!IsRangeMapped(physicalAddress, byteCount))
        {
            memory = [];
            offset = 0;
            return false;
        }

        memory = _memory;
        offset = Normalize(physicalAddress);
        return true;
    }

    private void InvalidateCode(uint address, int byteCount)
    {
        if (!IsRangeMapped(address, byteCount))
        {
            return;
        }

        var startPage = CodePageIndex(address);
        var endPage = CodePageIndex(address + (uint)Math.Max(0, byteCount - 1));
        for (var page = startPage; page <= endPage; page++)
        {
            _codePageGenerations[page]++;
        }

        for (var page = startPage; page <= endPage; page++)
        {
            var generationSource = _fixedPlanRunWindowGenerations[page];
            var generation = generationSource[0] + 1u;
            generationSource[0] = generation == 0 ? 1u : generation;
        }

        JitCodeRangeWritten?.Invoke(address, byteCount);
    }

    private bool IsRangeMapped(uint address, int byteCount)
        => byteCount >= 0 &&
            Normalize(address) + byteCount <= _memory.Length &&
            address + (uint)byteCount >= address;

    private int CodePageIndex(uint address)
        => Normalize(address) >> CodeGenerationPageShift;

    private int Normalize(uint address)
        => (int)(address & (uint)(_memory.Length - 1));

    private static int ByteCount(M68kOperandSize size)
        => size switch
        {
            M68kOperandSize.Byte => 1,
            M68kOperandSize.Word => 2,
            M68kOperandSize.Long => 4,
            _ => 1
        };
}

internal static unsafe class DispatchBenchmark
{
    private const int CaseCount = 4096;
    private static readonly DispatchCase[] Cases = CreateCases();
    private static readonly ManagedDispatchWrite[] ManagedDelegates =
    [
        RawDelegateWrite,
        CustomDelegateWrite
    ];

    private static readonly delegate*<ref DispatchState, uint, ushort, long, void>[] ManagedFunctionPointers =
    [
        &RawManagedFunctionPointerWrite,
        &CustomManagedFunctionPointerWrite
    ];

    private static readonly delegate* unmanaged<DispatchState*, uint, ushort, long, void>[] UnmanagedDelegates =
    [
        &RawUnmanagedWrite,
        &CustomUnmanagedWrite
    ];

    public static void Run(int iterations, int warmupIterations, int repeats)
    {
        Console.WriteLine($"Dispatch benchmark, warmup={warmupIterations}, measured={iterations}, repeats={repeats}, Release={IsRelease()}");
        Console.WriteLine("variant\trepeat\titerations\tms\tops/sec\tallocated bytes\tchecksum");

        WarmUp(warmupIterations);

        for (var repeat = 1; repeat <= repeats; repeat++)
        {
            RunVariant("if-else", repeat, iterations, RunIfElse);
            RunVariant("managed-delegate", repeat, iterations, RunManagedDelegate);
            RunVariant("managed-fnptr", repeat, iterations, RunManagedFunctionPointer);
            RunVariant("route-fnptr", repeat, iterations, RunRouteFunctionPointer);
            RunVariant("unmanaged-fnptr", repeat, iterations, RunUnmanagedFunctionPointer);
            RunVariant("generic-struct", repeat, iterations, RunGenericStruct);
        }
    }

    private static void WarmUp(int iterations)
    {
        _ = RunIfElse(iterations);
        _ = RunManagedDelegate(iterations);
        _ = RunManagedFunctionPointer(iterations);
        _ = RunRouteFunctionPointer(iterations);
        _ = RunUnmanagedFunctionPointer(iterations);
        _ = RunGenericStruct(iterations);
    }

    private static void RunVariant(string name, int repeat, int iterations, Func<int, uint> run)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        var checksum = run(iterations);
        var elapsed = Stopwatch.GetElapsedTime(start);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
        Console.WriteLine($"{name}\t{repeat}\t{iterations}\t{elapsed.TotalMilliseconds:F3}\t{iterations / elapsed.TotalSeconds:F0}\t{allocated}\t0x{checksum:X8}");
    }

    private static uint RunIfElse(int iterations)
    {
        var state = new DispatchState();
        for (var i = 0; i < iterations; i++)
        {
            ref readonly var item = ref Cases[i & (CaseCount - 1)];
            if (item.Target == DispatchTarget.Custom)
            {
                CustomWrite(ref state, item.Address, item.Value, item.Cycle);
            }
            else
            {
                RawWrite(ref state, item.Address, item.Value, item.Cycle);
            }
        }

        return state.Checksum;
    }

    private static uint RunManagedDelegate(int iterations)
    {
        var state = new DispatchState();
        for (var i = 0; i < iterations; i++)
        {
            ref readonly var item = ref Cases[i & (CaseCount - 1)];
            ManagedDelegates[(int)item.Target](ref state, item.Address, item.Value, item.Cycle);
        }

        return state.Checksum;
    }

    private static uint RunManagedFunctionPointer(int iterations)
    {
        var state = new DispatchState();
        for (var i = 0; i < iterations; i++)
        {
            ref readonly var item = ref Cases[i & (CaseCount - 1)];
            ManagedFunctionPointers[(int)item.Target](ref state, item.Address, item.Value, item.Cycle);
        }

        return state.Checksum;
    }

    private static uint RunRouteFunctionPointer(int iterations)
    {
        var state = new DispatchState();
        for (var i = 0; i < iterations; i++)
        {
            ref readonly var item = ref Cases[i & (CaseCount - 1)];
            var write = ClassifyRouteFunctionPointer(item.Address);
            write(ref state, item.Address, item.Value, item.Cycle);
        }

        return state.Checksum;
    }

    private static uint RunUnmanagedFunctionPointer(int iterations)
    {
        var state = new DispatchState();
        for (var i = 0; i < iterations; i++)
        {
            ref readonly var item = ref Cases[i & (CaseCount - 1)];
            UnmanagedDelegates[(int)item.Target](&state, item.Address, item.Value, item.Cycle);
        }

        return state.Checksum;
    }

    private static uint RunGenericStruct(int iterations)
    {
        var state = new DispatchState();
        for (var i = 0; i < iterations; i++)
        {
            ref readonly var item = ref Cases[i & (CaseCount - 1)];
            if (item.Target == DispatchTarget.Custom)
            {
                WriteWithPolicy<CustomPolicy>(ref state, item.Address, item.Value, item.Cycle);
            }
            else
            {
                WriteWithPolicy<RawPolicy>(ref state, item.Address, item.Value, item.Cycle);
            }
        }

        return state.Checksum;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteWithPolicy<TPolicy>(ref DispatchState state, uint address, ushort value, long cycle)
        where TPolicy : struct, IDispatchPolicy
        => default(TPolicy).Write(ref state, address, value, cycle);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void RawWrite(ref DispatchState state, uint address, ushort value, long cycle)
    {
        state.RawWrites++;
        state.Checksum = Mix(state.Checksum, address, value, cycle);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CustomWrite(ref DispatchState state, uint address, ushort value, long cycle)
    {
        if (address >= 0x00DFF000 && address < 0x00DFF1FF)
        {
            state.CustomWordWrites++;
            state.Checksum = Mix(state.Checksum ^ 0x9E37_79B9u, address, value, cycle);
            return;
        }

        if (address >= 0x00DFF000 && address < 0x00DFF200)
        {
            state.CustomByteWrites++;
            state.Checksum = Mix(state.Checksum ^ 0x85EB_CA6Bu, address, (ushort)(value >> 8), cycle);
        }

        RawWrite(ref state, address + 1, (byte)value, cycle);
    }

    private static void RawDelegateWrite(ref DispatchState state, uint address, ushort value, long cycle)
        => RawWrite(ref state, address, value, cycle);

    private static void CustomDelegateWrite(ref DispatchState state, uint address, ushort value, long cycle)
        => CustomWrite(ref state, address, value, cycle);

    private static void RawManagedFunctionPointerWrite(ref DispatchState state, uint address, ushort value, long cycle)
        => RawWrite(ref state, address, value, cycle);

    private static void CustomManagedFunctionPointerWrite(ref DispatchState state, uint address, ushort value, long cycle)
        => CustomWrite(ref state, address, value, cycle);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static delegate*<ref DispatchState, uint, ushort, long, void> ClassifyRouteFunctionPointer(uint address)
        => address >= 0x00DFF000 && address < 0x00DFF200
            ? &RouteCustomWrite
            : &RouteRawWrite;

    private static void RouteRawWrite(ref DispatchState state, uint address, ushort value, long cycle)
        => RawWrite(ref state, address, value, cycle);

    private static void RouteCustomWrite(ref DispatchState state, uint address, ushort value, long cycle)
        => CustomWrite(ref state, address, value, cycle);

    [UnmanagedCallersOnly]
    private static void RawUnmanagedWrite(DispatchState* state, uint address, ushort value, long cycle)
        => RawWrite(ref *state, address, value, cycle);

    [UnmanagedCallersOnly]
    private static void CustomUnmanagedWrite(DispatchState* state, uint address, ushort value, long cycle)
        => CustomWrite(ref *state, address, value, cycle);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Mix(uint checksum, uint address, ushort value, long cycle)
    {
        checksum = unchecked((checksum * 16777619u) ^ address);
        checksum = unchecked((checksum * 16777619u) ^ value);
        checksum = unchecked((checksum * 16777619u) ^ (uint)cycle);
        return checksum;
    }

    private static DispatchCase[] CreateCases()
    {
        var cases = new DispatchCase[CaseCount];
        for (var i = 0; i < cases.Length; i++)
        {
            var cycle = i * 4L;
            var value = (ushort)((i * 73) ^ (i >> 3));
            if ((i & 63) == 0)
            {
                cases[i] = new DispatchCase(DispatchTarget.Custom, 0x00DFF1FF, value, cycle);
            }
            else if ((i & 15) == 0)
            {
                cases[i] = new DispatchCase(DispatchTarget.Custom, 0x00DFF180u + (uint)((i >> 4) & 0x3E), value, cycle);
            }
            else
            {
                cases[i] = new DispatchCase(DispatchTarget.Raw, (uint)((i * 131) & 0x000F_FFFE), value, cycle);
            }
        }

        return cases;
    }

    private static bool IsRelease()
    {
#if DEBUG
        return false;
#else
        return true;
#endif
    }
}

internal delegate void ManagedDispatchWrite(ref DispatchState state, uint address, ushort value, long cycle);

internal interface IDispatchPolicy
{
    void Write(ref DispatchState state, uint address, ushort value, long cycle);
}

internal readonly struct RawPolicy : IDispatchPolicy
{
    public void Write(ref DispatchState state, uint address, ushort value, long cycle)
        => DispatchPolicyHelpers.Raw(ref state, address, value, cycle);
}

internal readonly struct CustomPolicy : IDispatchPolicy
{
    public void Write(ref DispatchState state, uint address, ushort value, long cycle)
        => DispatchPolicyHelpers.Custom(ref state, address, value, cycle);
}

internal static class DispatchPolicyHelpers
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Raw(ref DispatchState state, uint address, ushort value, long cycle)
    {
        state.RawWrites++;
        state.Checksum = Mix(state.Checksum, address, value, cycle);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Custom(ref DispatchState state, uint address, ushort value, long cycle)
    {
        if (address >= 0x00DFF000 && address < 0x00DFF1FF)
        {
            state.CustomWordWrites++;
            state.Checksum = Mix(state.Checksum ^ 0x9E37_79B9u, address, value, cycle);
            return;
        }

        if (address >= 0x00DFF000 && address < 0x00DFF200)
        {
            state.CustomByteWrites++;
            state.Checksum = Mix(state.Checksum ^ 0x85EB_CA6Bu, address, (ushort)(value >> 8), cycle);
        }

        Raw(ref state, address + 1, (byte)value, cycle);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Mix(uint checksum, uint address, ushort value, long cycle)
    {
        checksum = unchecked((checksum * 16777619u) ^ address);
        checksum = unchecked((checksum * 16777619u) ^ value);
        checksum = unchecked((checksum * 16777619u) ^ (uint)cycle);
        return checksum;
    }
}

internal enum DispatchTarget
{
    Raw = 0,
    Custom = 1
}

internal readonly record struct DispatchCase(DispatchTarget Target, uint Address, ushort Value, long Cycle);

internal struct DispatchState
{
    public uint Checksum;
    public uint RawWrites;
    public uint CustomWordWrites;
    public uint CustomByteWrites;
}
