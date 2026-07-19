using System.Diagnostics;
using System.Globalization;
using Copper68k;
using CopperMod.Amiga;

internal static class AmigaInstructionFetchArbitrationBenchmark
{
    private const string ModeArgument = "--amiga-fetch-arbitration";
    private const int ChipRamSize = 512 * 1024;
    private const int ExpansionRamSize = 512 * 1024;
    private const int RealFastRamSize = 1024 * 1024;
    private const int ProgramWordCount = 64;
    private const int ProgramOffset = 0x8000;
    private const uint BitplaneAddress = 0x0003_0000;

    public static bool TryRun(string[] args)
    {
        if (!args.Contains(ModeArgument, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        var options = ParseOptions(args);
        var scenarios = CreateScenarios();

        Console.WriteLine(
            $"Copper68k MC68000 Amiga instruction-fetch arbitration benchmark, " +
            $"profile=OCS-PAL/A500, warmup={options.WarmupInstructions}, measured={options.Instructions}, " +
            $"repeats={options.Repeats}, scheduler-profile={options.ProfileScheduler}, " +
            $"deferred-cpu={options.DeferredCpuBusBatch}, hardware-specialization={options.HardwareSpecialization}, " +
            $"deferred-read-shadow={options.DeferredDmaReadsVerify}, " +
            $"Release={IsRelease()}");
        Console.WriteLine(
            "scenario\trepeat\tinstructions\tms\tinstr/sec\tns/instr\tcycles/instr\tallocated bytes\tchecksum\t" +
            "drains/instr\tbus drains/instr\tagnus events/instr\tcpu grants/instr\tbitplane grants\t" +
            "stall cycles/instr\tdenied fixed\thost drain ns/instr\thost wake ns/instr\thost agnus ns/instr");

        for (var repeat = 1; repeat <= options.Repeats; repeat++)
        {
            for (var scenarioOffset = 0; scenarioOffset < scenarios.Length; scenarioOffset++)
            {
                var scenario = scenarios[(scenarioOffset + repeat - 1) % scenarios.Length];
                var result = Run(scenario, options);
                Console.WriteLine(
                    $"{scenario.Name}\t{repeat}\t{result.Instructions}\t{result.Elapsed.TotalMilliseconds:F3}\t" +
                    $"{result.InstructionsPerSecond:F0}\t{result.NanosecondsPerInstruction:F3}\t" +
                    $"{result.CyclesPerInstruction:F6}\t{result.AllocatedBytes}\t0x{result.Checksum:X8}\t" +
                    $"{result.DrainsPerInstruction:F6}\t{result.BusDrainsPerInstruction:F6}\t" +
                    $"{result.AgnusEventsPerInstruction:F6}\t{result.CpuGrantsPerInstruction:F6}\t" +
                    $"{result.BitplaneGrants}\t{result.StallCyclesPerInstruction:F6}\t{result.DeniedFixedSlots}\t" +
                    $"{result.HostDrainNanosecondsPerInstruction:F3}\t{result.HostWakeNanosecondsPerInstruction:F3}\t" +
                    $"{result.HostAgnusNanosecondsPerInstruction:F3}");
            }
        }

        return true;
    }

    private static AmigaInstructionFetchArbitrationResult Run(
        AmigaInstructionFetchScenario scenario,
        AmigaInstructionFetchArbitrationOptions options)
    {
        var bus = new AmigaBus(
            chipRamSize: ChipRamSize,
            expansionRamSize: ExpansionRamSize,
            captureBusAccesses: false,
            enableLiveAgnusDma: scenario.LiveAgnus,
            enableLiveDisplayDma: scenario.BitplaneDma,
            realFastRamSize: RealFastRamSize,
            enableHardwareSpecialization: options.HardwareSpecialization,
            enableDeferredCpuBusBatch: options.DeferredCpuBusBatch,
            verifyDeferredDmaReads: options.DeferredDmaReadsVerify,
            chipset: AmigaChipset.OcsPal);
        bus.ConfigureAutoconfigFastRamForHost();
        var programAddress = InstallProgram(bus, scenario.Memory);
        if (scenario.BitplaneDma)
        {
            ConfigureOneBitplaneDma(bus);
        }

        bus.SetHardwareSchedulerHostProfilingEnabled(options.ProfileScheduler);
        using var cpu = (IM68kBatchCore)M68kCoreFactory.CreateM68000Core(
            bus,
            default(AmigaCpuDataAccess),
            enableCpuBusPhaseTrace: false);
        cpu.Reset(programAddress, 0x0007_F000);
        ExecuteMany(cpu, options.WarmupInstructions);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var schedulerBefore = bus.CaptureHardwareSchedulerSnapshot();
        var agnusBefore = bus.Agnus.CaptureSnapshot();
        var cyclesBefore = cpu.State.Cycles;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        ExecuteMany(cpu, options.Instructions);
        var elapsed = Stopwatch.GetElapsedTime(start);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var schedulerAfter = bus.CaptureHardwareSchedulerSnapshot();
        var agnusAfter = bus.Agnus.CaptureSnapshot();

        return new AmigaInstructionFetchArbitrationResult(
            options.Instructions,
            elapsed,
            cpu.State.Cycles - cyclesBefore,
            allocated,
            CreateChecksum(cpu.State, programAddress),
            schedulerAfter.DrainCount - schedulerBefore.DrainCount,
            schedulerAfter.BusAccessDrainCount - schedulerBefore.BusAccessDrainCount,
            schedulerAfter.AgnusEvents - schedulerBefore.AgnusEvents,
            agnusAfter.CpuSlotGrantCount - agnusBefore.CpuSlotGrantCount,
            agnusAfter.BitplaneSlotGrantCount - agnusBefore.BitplaneSlotGrantCount,
            agnusAfter.CpuChipStallCycles - agnusBefore.CpuChipStallCycles,
            agnusAfter.DeniedFixedSlotCount - agnusBefore.DeniedFixedSlotCount,
            schedulerAfter.HostDrainTicks - schedulerBefore.HostDrainTicks,
            schedulerAfter.HostWakeQueryTicks - schedulerBefore.HostWakeQueryTicks,
            schedulerAfter.HostAgnusTicks - schedulerBefore.HostAgnusTicks);
    }

    private static uint InstallProgram(AmigaBus bus, AmigaInstructionMemory memory)
    {
        var program = CreateProgram();
        switch (memory)
        {
            case AmigaInstructionMemory.ChipRam:
                program.CopyTo(bus.ChipRam.AsSpan(ProgramOffset));
                return ProgramOffset;
            case AmigaInstructionMemory.TrapdoorRam:
                program.CopyTo(bus.ExpansionRam.AsSpan(ProgramOffset));
                return bus.ExpansionRamBase + ProgramOffset;
            case AmigaInstructionMemory.RealFastRam:
                program.CopyTo(bus.RealFastRam.AsSpan(ProgramOffset));
                return bus.RealFastRamBase + ProgramOffset;
            default:
                throw new ArgumentOutOfRangeException(nameof(memory), memory, null);
        }
    }

    private static byte[] CreateProgram()
    {
        var program = new byte[ProgramWordCount * 2];
        for (var word = 0; word < ProgramWordCount - 1; word++)
        {
            WriteWord(program, word * 2, 0x5280); // ADDQ.L #1,D0
        }

        WriteWord(program, (ProgramWordCount - 1) * 2, 0x6080); // BRA.S -128
        return program;
    }

    private static void ConfigureOneBitplaneDma(AmigaBus bus)
    {
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x00D0);
        bus.WriteLong(0x00DFF0E0, BitplaneAddress);
        bus.WriteWord(0x00DFF100, 0x1000);
        bus.WriteWord(0x00DFF096, 0x8300);
        bus.EnableLiveAgnusDma();
    }

    private static void ExecuteMany(IM68kBatchCore cpu, int instructions)
    {
        var remaining = instructions;
        while (remaining > 0)
        {
            var executed = cpu.ExecuteInstructions(remaining, null, SyntheticBenchmarkBoundary.Instance);
            if (executed <= 0)
            {
                throw new InvalidOperationException("MC68000 stopped before the fetch benchmark budget was consumed.");
            }

            remaining -= executed;
        }
    }

    private static uint CreateChecksum(M68kCpuState state, uint programAddress)
        => unchecked(
            (((state.ProgramCounter - programAddress) * 16777619u) ^ state.D[0]) * 16777619u ^
            state.StatusRegister);

    private static void WriteWord(Span<byte> target, int offset, ushort value)
    {
        target[offset] = (byte)(value >> 8);
        target[offset + 1] = (byte)value;
    }

    private static AmigaInstructionFetchScenario[] CreateScenarios()
        =>
        [
            new("real-fast", AmigaInstructionMemory.RealFastRam, LiveAgnus: true, BitplaneDma: false),
            new("trapdoor-idle", AmigaInstructionMemory.TrapdoorRam, LiveAgnus: true, BitplaneDma: false),
            new("chip-direct-control", AmigaInstructionMemory.ChipRam, LiveAgnus: false, BitplaneDma: false),
            new("chip-idle", AmigaInstructionMemory.ChipRam, LiveAgnus: true, BitplaneDma: false),
            new("chip-bpl1-dma", AmigaInstructionMemory.ChipRam, LiveAgnus: true, BitplaneDma: true)
        ];

    private static AmigaInstructionFetchArbitrationOptions ParseOptions(string[] args)
    {
        var warmup = 500_000;
        var instructions = 5_000_000;
        var repeats = 7;
        var profileScheduler = false;
        var deferredCpuBusBatch = false;
        var hardwareSpecialization = false;
        var deferredDmaReadsVerify = false;
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
                case "--profile-scheduler":
                    profileScheduler = true;
                    break;
                case "--deferred-cpu":
                    deferredCpuBusBatch = true;
                    break;
                case "--hardware-specialization":
                    hardwareSpecialization = true;
                    break;
                case "--deferred-dma-reads-verify":
                    deferredDmaReadsVerify = true;
                    break;
                case "--help":
                case "-h":
                    Console.WriteLine(
                        "Usage: dotnet run -c Release --project CopperScreen.Benchmarks -- " +
                        "--amiga-fetch-arbitration [--warmup N] [--instructions N] [--repeats N] " +
                        "[--profile-scheduler] [--deferred-cpu] [--hardware-specialization] " +
                        "[--deferred-dma-reads-verify]");
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"Unknown fetch-arbitration benchmark argument '{args[index]}'.");
            }
        }

        if (warmup < 0 || instructions <= 0 || repeats <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(args), "Warmup must be non-negative; instructions and repeats must be positive.");
        }

        return new AmigaInstructionFetchArbitrationOptions(
            warmup,
            instructions,
            repeats,
            profileScheduler,
            deferredCpuBusBatch,
            hardwareSpecialization,
            deferredDmaReadsVerify);
    }

    private static int ParseInt(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for '{args[index]}'.");
        }

        index++;
        return int.Parse(args[index], CultureInfo.InvariantCulture);
    }

    private static bool IsRelease()
    {
#if DEBUG
        return false;
#else
        return true;
#endif
    }

    private enum AmigaInstructionMemory : byte
    {
        ChipRam,
        TrapdoorRam,
        RealFastRam
    }

    private readonly record struct AmigaInstructionFetchScenario(
        string Name,
        AmigaInstructionMemory Memory,
        bool LiveAgnus,
        bool BitplaneDma);

    private readonly record struct AmigaInstructionFetchArbitrationOptions(
        int WarmupInstructions,
        int Instructions,
        int Repeats,
        bool ProfileScheduler,
        bool DeferredCpuBusBatch,
        bool HardwareSpecialization,
        bool DeferredDmaReadsVerify);

    private readonly record struct AmigaInstructionFetchArbitrationResult(
        int Instructions,
        TimeSpan Elapsed,
        long Cycles,
        long AllocatedBytes,
        uint Checksum,
        long Drains,
        long BusDrains,
        long AgnusEvents,
        long CpuGrants,
        long BitplaneGrants,
        long StallCycles,
        long DeniedFixedSlots,
        long HostDrainTicks,
        long HostWakeTicks,
        long HostAgnusTicks)
    {
        public double InstructionsPerSecond => Instructions / Elapsed.TotalSeconds;

        public double NanosecondsPerInstruction => Elapsed.TotalNanoseconds / Instructions;

        public double CyclesPerInstruction => (double)Cycles / Instructions;

        public double DrainsPerInstruction => (double)Drains / Instructions;

        public double BusDrainsPerInstruction => (double)BusDrains / Instructions;

        public double AgnusEventsPerInstruction => (double)AgnusEvents / Instructions;

        public double CpuGrantsPerInstruction => (double)CpuGrants / Instructions;

        public double StallCyclesPerInstruction => (double)StallCycles / Instructions;

        public double HostDrainNanosecondsPerInstruction => TicksToNanosecondsPerInstruction(HostDrainTicks);

        public double HostWakeNanosecondsPerInstruction => TicksToNanosecondsPerInstruction(HostWakeTicks);

        public double HostAgnusNanosecondsPerInstruction => TicksToNanosecondsPerInstruction(HostAgnusTicks);

        private double TicksToNanosecondsPerInstruction(long ticks)
            => ticks * (1_000_000_000.0 / Stopwatch.Frequency) / Instructions;
    }
}
