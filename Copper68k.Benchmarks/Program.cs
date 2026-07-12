using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Copper68k;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

var options = BenchmarkOptions.Parse(args);
if (options.Dispatch)
{
    DispatchBenchmark.Run(options.Instructions, options.WarmupInstructions, options.Repeats);
    return;
}

var workloads = CreateWorkloads()
    .Where(workload => options.Workload is null ||
        workload.Name.Contains(options.Workload, StringComparison.OrdinalIgnoreCase))
    .ToArray();
var backends = CreateBackends()
    .Where(backend => options.Backend is null ||
        backend.Name.Contains(options.Backend, StringComparison.OrdinalIgnoreCase))
    .ToArray();

if (workloads.Length == 0)
{
    throw new InvalidOperationException($"No workload matched '{options.Workload}'.");
}

if (backends.Length == 0)
{
    throw new InvalidOperationException($"No backend matched '{options.Backend}'.");
}

Console.WriteLine($"Copper68k backend benchmark, warmup={options.WarmupInstructions}, measured={options.Instructions}, repeats={options.Repeats}, Release={IsRelease()}");
Console.WriteLine("workload\tbackend\trepeat\tinstructions\tms\tinstr/sec\tmips\tcycles\tnative-cycles\tallocated bytes\tchecksum\tcompiled traces\tfallback instr\tpure trace instr\tbus trace instr\tzero-wait reads\tzero-wait writes");

foreach (var workload in workloads)
{
    foreach (var backend in backends)
    {
        for (var repeat = 1; repeat <= options.Repeats; repeat++)
        {
            var result = RunBenchmark(workload, backend, options);
            Console.WriteLine(
                $"{workload.Name}\t{backend.Name}\t{repeat}\t{result.Instructions}\t{result.Elapsed.TotalMilliseconds:F3}\t{result.InstructionsPerSecond:F0}\t{result.InstructionsPerSecond / 1_000_000.0:F3}\t{result.Cycles}\t{result.NativeCycles}\t{result.AllocatedBytes}\t0x{result.Checksum:X8}\t{result.CompiledTraces}\t{result.FallbackInstructions}\t{result.PureTraceInstructions}\t{result.BusTraceInstructions}\t{result.ZeroWaitReads}\t{result.ZeroWaitWrites}");
        }
    }
}

static BenchmarkResult RunBenchmark(BenchmarkWorkload workload, BenchmarkBackend backend, BenchmarkOptions options)
{
    using var run = CreateRun(workload, backend);
    WarmUp(run.Cpu, options.WarmupInstructions);

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
    var start = Stopwatch.GetTimestamp();
    ExecuteMany(run.Cpu, options.Instructions);
    var elapsed = Stopwatch.GetElapsedTime(start);
    var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
    var counters = run.Cpu is M68kJitCore jit ? jit.Counters : default;
    return new BenchmarkResult(
        options.Instructions,
        elapsed,
        run.Cpu.State.Cycles,
        run.Cpu.State.NativeCycles,
        allocated,
        CreateChecksum(run.Cpu.State, run.Bus),
        counters.CompiledTraces,
        counters.FallbackInstructions,
        counters.PureTraceBatchInstructions,
        counters.V2BusAccessBatchInstructions,
        counters.V2ZeroWaitReadRealFast + counters.V2ZeroWaitReadRom + counters.V2ZeroWaitReadOverlay,
        counters.V2ZeroWaitWriteRealFast);
}

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

    return new BenchmarkRun(cpu, bus);
}

static void WarmUp(IM68kCore cpu, int instructions)
{
    ExecuteMany(cpu, instructions);
    if (cpu is M68kJitCore jit)
    {
        WarmUpJitUntilStable(jit);
    }
}

static void WarmUpJitUntilStable(M68kJitCore jit)
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
        ExecuteMany(jit, chunkInstructions);
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

static void ExecuteMany(IM68kCore cpu, int instructions)
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
        new BenchmarkBackend("JitM68000", bus => M68kJitCore.CreateM68000(bus)),
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
            1 << 20)
    ];

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

static uint CreateChecksum(M68kCpuState state, BenchmarkBus bus)
{
    var checksum = state.ProgramCounter ^ state.LastInstructionProgramCounter ^ state.LastOpcode;
    checksum = unchecked((checksum * 16777619u) ^ state.D[0]);
    checksum = unchecked((checksum * 16777619u) ^ state.D[1]);
    checksum = unchecked((checksum * 16777619u) ^ state.D[2]);
    checksum = unchecked((checksum * 16777619u) ^ state.StatusRegister);
    checksum = unchecked((checksum * 16777619u) ^ (uint)state.Cycles);
    checksum = unchecked((checksum * 16777619u) ^ bus.ReadLongValue(0x0008_0000));
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
    bool Dispatch)
{
    public static BenchmarkOptions Parse(string[] args)
    {
        var warmup = 500_000;
        var instructions = 5_000_000;
        var repeats = 3;
        string? backend = null;
        string? workload = null;
        var dispatch = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--dispatch":
                    dispatch = true;
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
                    Console.WriteLine("Usage: dotnet run -c Release --project Copper68k.Benchmarks -- [--dispatch] [--warmup N] [--instructions N] [--repeats N] [--backend text] [--workload text]");
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }
        }

        return new BenchmarkOptions(warmup, instructions, repeats, backend, workload, dispatch);
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

internal sealed record BenchmarkBackend(string Name, Func<BenchmarkBus, IM68kCore> Create);

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
    int MemorySize);

internal sealed record BenchmarkRun(IM68kCore Cpu, BenchmarkBus Bus) : IDisposable
{
    public void Dispose()
        => Cpu.Dispose();
}

internal readonly record struct BenchmarkResult(
    int Instructions,
    TimeSpan Elapsed,
    long Cycles,
    long NativeCycles,
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
    IM68kJitBus,
    IM68kJitFastMemoryBus,
    IM68kJitTimedMemoryBus,
    IM68kPhysicalAddressMap
{
    private const int CodeGenerationPageShift = 8;
    private const int CodeGenerationPageSize = 1 << CodeGenerationPageShift;
    private readonly byte[] _memory;
    private readonly uint[] _codePageGenerations;

    public BenchmarkBus(int memorySize)
    {
        if (memorySize <= 0 || (memorySize & (memorySize - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(memorySize), "Memory size must be a positive power of two.");
        }

        _memory = new byte[memorySize];
        _codePageGenerations = new uint[Math.Max(1, memorySize >> CodeGenerationPageShift)];
    }

    public event Action<uint, int>? JitCodeRangeWritten;

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
