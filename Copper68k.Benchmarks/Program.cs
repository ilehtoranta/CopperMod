using System.Diagnostics;
using System.Globalization;
using Copper68k;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

var options = BenchmarkOptions.Parse(args);
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
    ExecuteMany(run.Cpu, options.WarmupInstructions);

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
    string? Workload)
{
    public static BenchmarkOptions Parse(string[] args)
    {
        var warmup = 500_000;
        var instructions = 5_000_000;
        var repeats = 3;
        string? backend = null;
        string? workload = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
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
                    Console.WriteLine("Usage: dotnet run -c Release --project Copper68k.Benchmarks -- [--warmup N] [--instructions N] [--repeats N] [--backend text] [--workload text]");
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }
        }

        return new BenchmarkOptions(warmup, instructions, repeats, backend, workload);
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
