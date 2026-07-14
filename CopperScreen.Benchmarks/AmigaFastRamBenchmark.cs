using System.Diagnostics;
using System.Globalization;
using Copper68k;
using CopperMod.Amiga;

internal static class AmigaFastRamBenchmark
{
    private const string ModeArgument = "--amiga-fast-ram";
    private const uint CodeBase = 0x00F8_0000;
    private const int ExpansionRamSize = 512 * 1024;
    private const int RealFastRamSize = 1024 * 1024;
    private const int StreamingSpan = 256 * 1024;
    private const uint M68040InstructionCacheEnable = 0x0000_0001;

    public static bool TryRun(string[] args)
    {
        if (!args.Contains(ModeArgument, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        var options = ParseOptions(args);
        var workloads = CreateWorkloads()
            .Where(workload => options.Workload == null
                ? workload.IncludeByDefault
                : workload.Name.Contains(options.Workload, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (workloads.Length == 0)
        {
            throw new InvalidOperationException($"No Amiga fast-RAM workload matched '{options.Workload}'.");
        }

        var directSetting = Environment.GetEnvironmentVariable("COPPER_M68K_JIT_040_DIRECT_RAM");
        Console.WriteLine(
            $"Copper68k MC68040 Amiga fast-RAM benchmark, warmup={options.WarmupInstructions}, " +
            $"measured={options.Instructions}, repeats={options.Repeats}, " +
            $"direct={directSetting ?? "default-on"}, Release={IsRelease()}");
        Console.WriteLine(
            "workload\tstrategy\trepeat\tinstructions\tms\tinstr/sec\tmips\tcycles\tnative-cycles\tallocated bytes\tchecksum\t" +
            "direct reads\tdirect writes\tpseudo timing flushes\twrite completion flushes\tread misses\twrite misses\t" +
            "move16 direct\tmove16 fallback");

        foreach (var workload in workloads)
        {
            var strategies = GetMove16Strategies(workload, options.Move16Strategy);
            for (var repeat = 1; repeat <= options.Repeats; repeat++)
            {
                for (var strategyOffset = 0; strategyOffset < strategies.Length; strategyOffset++)
                {
                    var strategy = strategies[(strategyOffset + repeat - 1) % strategies.Length];
                    var result = Run(workload, strategy, options);
                    Console.WriteLine(
                        $"{workload.Name}\t{strategy}\t{repeat}\t{result.Instructions}\t{result.Elapsed.TotalMilliseconds:F3}\t" +
                        $"{result.InstructionsPerSecond:F0}\t{result.InstructionsPerSecond / 1_000_000.0:F3}\t" +
                        $"{result.Cycles}\t{result.NativeCycles}\t{result.AllocatedBytes}\t0x{result.Checksum:X8}\t" +
                        $"{result.DirectReads}\t{result.DirectWrites}\t{result.PseudoTimingFlushes}\t" +
                        $"{result.WriteCompletionFlushes}\t{result.ReadMisses}\t{result.WriteMisses}\t" +
                        $"{result.Move16DirectCopies}\t{result.Move16Fallbacks}");
                }
            }
        }

        return true;
    }

    private static AmigaFastRamResult Run(
        AmigaFastRamWorkload workload,
        M68040Move16CopyStrategy move16Strategy,
        AmigaFastRamOptions options)
    {
        var bus = new AmigaBus(
            expansionRamSize: ExpansionRamSize,
            captureBusAccesses: false,
            enableLiveAgnusDma: false,
            enableLiveDisplayDma: false,
            realFastRamSize: RealFastRamSize,
            enableHardwareSpecialization: true);
        var memoryBase = workload.PseudoFast ? bus.ExpansionRamBase : bus.RealFastRamBase;
        var source = memoryBase;
        var destination = memoryBase + StreamingSpan;
        bus.MapReadOnlyMemory(CodeBase, CreateProgram(workload, source, destination));
        InitializeSource(bus, source);

        using var cpu = workload.Move16
            ? M68kJitCore.CreateM68040ForTesting(bus, enableV2: true, move16Strategy)
            : M68kJitCore.CreateM68040(bus);
        cpu.Reset(CodeBase, 0x001F_F000);
        cpu.State.CacheControlRegister = M68040InstructionCacheEnable;
        cpu.State.D[1] = 0x1020_3040;
        ExecuteMany(cpu, options.WarmupInstructions);
        WarmUpJitUntilStable(cpu);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var countersBefore = cpu.Counters;
        var cyclesBefore = cpu.State.Cycles;
        var nativeCyclesBefore = cpu.State.NativeCycles;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        ExecuteMany(cpu, options.Instructions);
        var elapsed = Stopwatch.GetElapsedTime(start);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var counters = cpu.Counters;
        var directReads = workload.PseudoFast
            ? counters.M68040DirectPseudoFastReads - countersBefore.M68040DirectPseudoFastReads
            : counters.M68040DirectRealFastReads - countersBefore.M68040DirectRealFastReads;
        var directWrites = workload.PseudoFast
            ? counters.M68040DirectPseudoFastWrites - countersBefore.M68040DirectPseudoFastWrites
            : counters.M68040DirectRealFastWrites - countersBefore.M68040DirectRealFastWrites;
        var checksum = CreateChecksum(cpu.State, bus, source, destination);

        return new AmigaFastRamResult(
            options.Instructions,
            elapsed,
            cpu.State.Cycles - cyclesBefore,
            cpu.State.NativeCycles - nativeCyclesBefore,
            allocated,
            checksum,
            directReads,
            directWrites,
            counters.M68040DirectPseudoFastTimingFlushes - countersBefore.M68040DirectPseudoFastTimingFlushes,
            counters.M68040DirectRamWriteCompletionFlushes - countersBefore.M68040DirectRamWriteCompletionFlushes,
            counters.M68040DirectRamReadMisses - countersBefore.M68040DirectRamReadMisses,
            counters.M68040DirectRamWriteMisses - countersBefore.M68040DirectRamWriteMisses,
            counters.M68040Move16DirectCopies - countersBefore.M68040Move16DirectCopies,
            counters.M68040Move16Fallbacks - countersBefore.M68040Move16Fallbacks);
    }

    private static void ExecuteMany(M68kJitCore cpu, int instructions)
    {
        var remaining = instructions;
        while (remaining > 0)
        {
            var executed = cpu.ExecuteInstructions(remaining, long.MaxValue, AmigaFastRamBenchmarkBoundary.Instance);
            if (executed == 0)
            {
                throw new InvalidOperationException("MC68040 JIT stopped before the benchmark budget was consumed.");
            }

            remaining -= executed;
        }
    }

    private static void WarmUpJitUntilStable(M68kJitCore cpu)
    {
        const int chunkInstructions = 1_000_000;
        const int maximumChunks = 32;
        const int requiredStableChunks = 2;
        var previousCompiledTraces = cpu.Counters.CompiledTraces;
        var stableChunks = 0;

        for (var chunk = 0; chunk < maximumChunks && stableChunks < requiredStableChunks; chunk++)
        {
            ExecuteMany(cpu, chunkInstructions);
            var compiledTraces = cpu.Counters.CompiledTraces;
            if (compiledTraces == previousCompiledTraces && cpu.AsyncCompilationIdle)
            {
                stableChunks++;
            }
            else
            {
                previousCompiledTraces = compiledTraces;
                stableChunks = 0;
            }
        }
    }

    private static byte[] CreateProgram(AmigaFastRamWorkload workload, uint source, uint destination)
    {
        var words = workload.Move16
            ? new ushort[]
            {
                0x207C, (ushort)(source >> 16), (ushort)source,             // MOVEA.L #source,A0
                0x227C, (ushort)(destination >> 16), (ushort)destination, // MOVEA.L #destination,A1
                0xF620, 0x1000,                                           // MOVE16 (A0)+,(A1)+
                0x60EE                                                    // BRA.S reset
            }
            : workload.Streaming
            ? new ushort[]
            {
                0x207C, (ushort)(source >> 16), (ushort)source,           // MOVEA.L #source,A0
                0x227C, (ushort)(destination >> 16), (ushort)destination, // MOVEA.L #destination,A1
                0x74FF,                                                 // MOVEQ #-1,D2
                0x2018,                                                 // MOVE.L (A0)+,D0
                0xD081,                                                 // ADD.L D1,D0
                0x22C0,                                                 // MOVE.L D0,(A1)+
                0x51CA, 0xFFF8,                                         // DBRA D2,loop
                0x60E6                                                  // BRA.S reset
            }
            : new ushort[]
            {
                0x207C, (ushort)(source >> 16), (ushort)source,           // MOVEA.L #source,A0
                0x227C, (ushort)(destination >> 16), (ushort)destination, // MOVEA.L #destination,A1
                0x2010,                                                 // MOVE.L (A0),D0
                0xD081,                                                 // ADD.L D1,D0
                0x2280,                                                 // MOVE.L D0,(A1)
                0x60F8                                                  // BRA.S loop
            };
        var bytes = new byte[words.Length * 2];
        for (var index = 0; index < words.Length; index++)
        {
            bytes[index * 2] = (byte)(words[index] >> 8);
            bytes[(index * 2) + 1] = (byte)words[index];
        }

        return bytes;
    }

    private static void InitializeSource(AmigaBus bus, uint source)
    {
        for (var offset = 0; offset < StreamingSpan; offset += 4)
        {
            bus.WriteLong(source + (uint)offset, 0x0102_0304u + (uint)offset);
        }
    }

    private static uint CreateChecksum(M68kCpuState state, AmigaBus bus, uint source, uint destination)
    {
        var checksum = state.ProgramCounter ^ state.StatusRegister;
        for (var index = 0; index < 8; index++)
        {
            checksum = unchecked((checksum * 16777619u) ^ state.D[index]);
            checksum = unchecked((checksum * 16777619u) ^ state.A[index]);
        }

        checksum = unchecked((checksum * 16777619u) ^ bus.ReadLong(source));
        checksum = unchecked((checksum * 16777619u) ^ bus.ReadLong(destination));
        checksum = unchecked((checksum * 16777619u) ^ bus.ReadLong(destination + StreamingSpan - 4));
        return checksum;
    }

    private static AmigaFastRamWorkload[] CreateWorkloads()
        =>
        [
            new AmigaFastRamWorkload("pseudo-fast-fixed", PseudoFast: true, Streaming: false),
            new AmigaFastRamWorkload("pseudo-fast-streaming", PseudoFast: true, Streaming: true),
            new AmigaFastRamWorkload("real-fast-fixed", PseudoFast: false, Streaming: false),
            new AmigaFastRamWorkload("real-fast-streaming", PseudoFast: false, Streaming: true),
            new AmigaFastRamWorkload("pseudo-fast-move16", PseudoFast: true, Streaming: false, Move16: true, IncludeByDefault: false),
            new AmigaFastRamWorkload("real-fast-move16", PseudoFast: false, Streaming: false, Move16: true, IncludeByDefault: false)
        ];

    private static M68040Move16CopyStrategy[] GetMove16Strategies(
        AmigaFastRamWorkload workload,
        string? requested)
    {
        if (!workload.Move16)
        {
            return [M68040Move16CopyStrategy.Auto];
        }

        if (requested == null || requested.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                M68040Move16CopyStrategy.UInt32,
                M68040Move16CopyStrategy.UInt64,
                M68040Move16CopyStrategy.Vector128,
                M68040Move16CopyStrategy.Fallback
            ];
        }

        if (!Enum.TryParse<M68040Move16CopyStrategy>(requested, ignoreCase: true, out var strategy))
        {
            throw new ArgumentException($"Unknown MOVE16 strategy '{requested}'.");
        }

        return [strategy];
    }

    private static AmigaFastRamOptions ParseOptions(string[] args)
    {
        var warmup = 5_000_000;
        var instructions = 100_000_000;
        var repeats = 7;
        string? workload = null;
        string? move16Strategy = null;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case ModeArgument:
                    break;
                case "--warmup":
                    warmup = ParseInt(args, ref index);
                    break;
                case "--instructions":
                    instructions = ParseInt(args, ref index);
                    break;
                case "--repeats":
                    repeats = ParseInt(args, ref index);
                    break;
                case "--workload":
                    workload = ParseString(args, ref index);
                    break;
                case "--move16-strategy":
                    move16Strategy = ParseString(args, ref index);
                    break;
                case "--help":
                case "-h":
                    Console.WriteLine(
                        "Usage: dotnet run -c Release --project CopperScreen.Benchmarks -- " +
                        "--amiga-fast-ram [--warmup N] [--instructions N] [--repeats N] " +
                        "[--workload text] [--move16-strategy all|Auto|UInt32|UInt64|Vector128|Fallback]");
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"Unknown Amiga fast-RAM benchmark argument '{args[index]}'.");
            }
        }

        return new AmigaFastRamOptions(warmup, instructions, repeats, workload, move16Strategy);
    }

    private static int ParseInt(string[] args, ref int index)
        => int.Parse(ParseString(args, ref index), CultureInfo.InvariantCulture);

    private static string ParseString(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for '{args[index]}'.");
        }

        index++;
        return args[index];
    }

    private static bool IsRelease()
    {
#if DEBUG
        return false;
#else
        return true;
#endif
    }

    private readonly record struct AmigaFastRamOptions(
        int WarmupInstructions,
        int Instructions,
        int Repeats,
        string? Workload,
        string? Move16Strategy);

    private readonly record struct AmigaFastRamWorkload(
        string Name,
        bool PseudoFast,
        bool Streaming,
        bool Move16 = false,
        bool IncludeByDefault = true);

    private readonly record struct AmigaFastRamResult(
        int Instructions,
        TimeSpan Elapsed,
        long Cycles,
        long NativeCycles,
        long AllocatedBytes,
        uint Checksum,
        long DirectReads,
        long DirectWrites,
        long PseudoTimingFlushes,
        long WriteCompletionFlushes,
        long ReadMisses,
        long WriteMisses,
        long Move16DirectCopies,
        long Move16Fallbacks)
    {
        public double InstructionsPerSecond => Instructions / Elapsed.TotalSeconds;
    }
}

internal sealed class AmigaFastRamBenchmarkBoundary :
    IM68kInstructionBoundary,
    IM68kPureCpuTraceBatchBoundary,
    IM68kBusAccessTraceBatchBoundary
{
    public static AmigaFastRamBenchmarkBoundary Instance { get; } = new();

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
