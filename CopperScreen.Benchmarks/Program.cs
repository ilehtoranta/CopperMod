using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CopperMod.Amiga;
using CopperMod.Amiga.CustomChips.Blitter;
using CopperScreen;

const int SampleRate = 44_100;
const int Channels = 2;
const int OpcodeDispatchDefaultMemorySize = 1 << 20;
const int OpcodeDispatchTransformMemorySize = 1 << 24;

if (Move16AlignmentBenchmark.TryRun(args) ||
    AmigaFastRamBenchmark.TryRun(args) ||
    AmigaBusSaturationBenchmark.TryRun(args) ||
    AmigaInstructionFetchArbitrationBenchmark.TryRun(args) ||
    EcsTimingBenchmark.TryRun(args) ||
    CustomRegisterDispatchBenchmark.TryRun(args))
{
    return;
}

var options = BenchmarkOptions.Parse(args);
if (options.JitFallbackAttribution)
{
    Environment.SetEnvironmentVariable("COPPER_M68K_JIT_FALLBACK_ATTRIBUTION", "1");
}

var allWorkloads = new[]
{
    new BenchmarkWorkload("No disk / insert screen", null),
    new BenchmarkWorkload("Superfrog CSL", "Superfrog (1993)(Team 17)(Disk 1 of 4)[cr CSL].zip"),
    new BenchmarkWorkload("Alien Breed", "Team17\\AlienBreed_998.zip"),
    new BenchmarkWorkload("Alien Breed SE 92", "Team17\\AlienBreedSpecialEdition92_615.zip"),
    new BenchmarkWorkload("Alien Breed II", "Team17\\AlienBreedII-TheHorrorContinues_44.zip"),
    new BenchmarkWorkload("Alien Breed 3D", "Team17\\AlienBreed3D_.zip"),
    new BenchmarkWorkload("Apidya IPF", "Team17\\Apidya_764.zip"),
    new BenchmarkWorkload("F17 Challenge IPF", "Team17\\F17Challenge_1703.zip"),
    new BenchmarkWorkload("Overdrive IPF", "Team17\\downloads_overdrive_IPFs.zip"),
    new BenchmarkWorkload("Worms IPF", "Team17\\Worms_230.zip"),
    new BenchmarkWorkload("Worms Director's Cut IPF", "Team17\\Worms-TheDirectorsCut_605.zip"),
    new BenchmarkWorkload("Desert Strike intro", "Desert Strike - Return to the Gulf (1993)(Electronic Arts)(Disk 1 of 3).zip", FireFrame: 1800),
    new BenchmarkWorkload("Lemmings SR", "Lemmings (1991)(Psygnosis)(Disk 1 of 2)[cr SR].zip"),
    new BenchmarkWorkload("Full Contact FLT", "Full Contact (1991)(Team 17)(Disk 1 of 2)[cr FLT].zip"),
    new BenchmarkWorkload("Full Contact original single-drive", "Full Contact (1991)(Team 17)(Disk 1 of 2).zip", Profile: "expanded-kickstart13 - singledrive.json"),
    new BenchmarkWorkload("Arte sanity single-drive", "Arte (Sanity).zip", Profile: "expanded-kickstart13 - singledrive.json"),
    new BenchmarkWorkload("FC FLT intro", "Full Contact (1991)(Team 17)(Disk 1 of 2)[cr FLT].zip", FireFrame: 260),
    new BenchmarkWorkload(
        "Hired Guns",
        "Hired Guns v1.08.39.25 (1993-09-24)(Psygnosis)(M5)(Disk 1 of 5).zip",
        Profile: "expanded-copperstart.json",
        LaunchPath: "C/SystemTakeover|Hired Guns|Hired Guns.info"),
    new BenchmarkWorkload("North & South CP intro", "North & South (1989)(Infogrames)(M5)[cr CP].zip", FireFrame: 260),
    new BenchmarkWorkload("Shadow of the Beast IPF", "Shadow of the Beast (1989)(Psygnosis)(US)(Disk 1 of 2).zip"),
    new BenchmarkWorkload("Workbench 1.3", "Workbench v1.3 rev 34.20 (1988)(Commodore)(A500-A2000)(Disk 1 of 2)(Workbench)[m].zip"),
    new BenchmarkWorkload("Major Motion OK", "OK\\Major Motion (1988)(Microdeal).zip", LaunchPath: "major"),
    new BenchmarkWorkload("Lotus 2", "Lotus Turbo Challenge 2 (Magnetic Fields + Gremlin).zip"),
    new BenchmarkWorkload("Lotus 3 FLT", "Lotus III - The Ultimate Challenge (1992)(Gremlin)(Disk 1 of 2)[cr FLT - Crack Inc].zip"),
    new BenchmarkWorkload("Super Cars II Flashtro", "OK\\Super Cars II (1991)(Gremlin)(Disk 1 of 2)[cr Flashtro].zip"),
};
var workloads = options.Smoke
    ? new[] { allWorkloads[0] }
    : FilterWorkloads(allWorkloads, options.Only);

if (options.OpcodeDispatchBenchmark)
{
    RunOpcodeDispatchBenchmarks(options);
    return;
}

if (options.CiaPollBenchmark)
{
    RunCiaPollBenchmark(options);
    return;
}

if (options.SyntheticBlitterBenchmark)
{
    RunSyntheticBlitterBenchmarks(options);
    return;
}

if (options.DiskDivergenceTrace)
{
    Console.WriteLine($"Disk divergence trace, Warmup={options.WarmupFrames} frames, measured={options.MeasuredFrames} frames, Profile={options.Profile ?? "workload/default"}, Kickstart={FormatKickstartOption(options)}");
    foreach (var workload in workloads)
    {
        RunDiskDivergenceTrace(workload, options);
    }

    return;
}

Console.WriteLine($"Warmup={options.WarmupFrames} frames, measured={options.MeasuredFrames} frames, repeats={options.RepeatCount}, Release={IsRelease()}, Profile={options.Profile ?? "workload/default"}, Agnus=hrm, CPU={options.CpuBackend ?? "profile"}, OpcodeDispatch={options.OpcodeDispatch?.ToString() ?? "default"}, JitFallbackAttribution={options.JitFallbackAttribution}, HardwareSpecialization={options.HardwareSpecialization}, BlitterAdvance={options.BlitterAdvanceMode.ToString().ToLowerInvariant()}, CopperQuiescenceFastPath={options.CopperQuiescenceFastPath}, CopperQuiescenceFastPathVerify={options.CopperQuiescenceFastPathVerify}, DeferredCpuBusBatch={options.DeferredCpuBusBatch}, DeferredCpuBusBatchVerify={options.DeferredCpuBusBatchVerify}, CpuWaitSlotReference={options.CpuWaitSlotReference}, Kickstart={FormatKickstartOption(options)}");
WriteBenchmarkHeader();

foreach (var workload in workloads)
{
    for (var repeat = 0; repeat < options.RepeatCount; repeat++)
    {
        var result = RunBenchmark(workload, options);
        WriteBenchmarkResult(result, options);
        ValidateExpectedChecksums(result, options);
        WriteCopperQuiescenceAuditIfNeeded(result, options);
    }
}

static void RunOpcodeDispatchBenchmarks(BenchmarkOptions options)
{
    var variants = options.OpcodeDispatch.HasValue
        ? new[] { options.OpcodeDispatch.Value }
        : new[]
        {
            M68kOpcodePlanDispatch.Scalar,
            M68kOpcodePlanDispatch.KindTable,
            M68kOpcodePlanDispatch.PackedPlan
        };
    var workloads = CreateOpcodeDispatchWorkloads();
    Console.WriteLine(
        $"Opcode dispatch bench, warmup={options.OpcodeDispatchWarmupInstructions}, measured={options.OpcodeDispatchInstructions}, repeats={options.RepeatCount}, Release={IsRelease()}");
    Console.WriteLine("opcode-dispatch\tworkload\tvariant\trepeat\tinstructions\tms\tinstr/sec\tcycles\tallocated bytes\tplanned fast\tplanned fallback\tplanned hit%\tchecksum");
    foreach (var workload in workloads)
    {
        foreach (var variant in variants)
        {
            for (var repeat = 0; repeat < options.RepeatCount; repeat++)
            {
                var result = RunOpcodeDispatchBenchmark(workload, variant, options);
                Console.WriteLine(
                    $"opcode-dispatch\t{workload.Name}\t{variant}\t{repeat + 1}\t{result.Instructions}\t{result.Elapsed.TotalMilliseconds:F3}\t{result.InstructionsPerSecond:F0}\t{result.Cycles}\t{result.AllocatedBytes}\t{result.Counters.FastInstructions}\t{result.Counters.ScalarFallbackInstructions}\t{FormatPlannedHitPercent(result.Counters)}\t0x{result.Checksum:X8}");
            }
        }
    }
}

static void RunCiaPollBenchmark(BenchmarkOptions options)
{
    Console.WriteLine(
        $"CIA poll bench, warmup={options.OpcodeDispatchWarmupInstructions}, measured={options.OpcodeDispatchInstructions}, repeats={options.RepeatCount}, Release={IsRelease()}");
    Console.WriteLine("cia-poll\trepeat\tinstructions\tms\tinstr/sec\tcycles\tallocated bytes\tchecksum");
    for (var repeat = 0; repeat < options.RepeatCount; repeat++)
    {
        var result = RunCiaPollBenchmarkIteration(options);
        Console.WriteLine(
            $"cia-poll\t{repeat + 1}\t{result.Instructions}\t{result.Elapsed.TotalMilliseconds:F3}\t{result.InstructionsPerSecond:F0}\t{result.Cycles}\t{result.AllocatedBytes}\t0x{result.Checksum:X8}");
    }
}

static CiaPollBenchmarkResult RunCiaPollBenchmarkIteration(BenchmarkOptions options)
{
    using (var warmup = CreateCiaPollCpu())
    {
        for (var i = 0; i < options.OpcodeDispatchWarmupInstructions; i++)
        {
            warmup.Cpu.ExecuteInstruction();
        }
    }

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    using var run = CreateCiaPollCpu();
    var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
    var start = Stopwatch.GetTimestamp();
    for (var i = 0; i < options.OpcodeDispatchInstructions; i++)
    {
        run.Cpu.ExecuteInstruction();
    }

    var elapsed = Stopwatch.GetElapsedTime(start);
    var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
    return new CiaPollBenchmarkResult(
        options.OpcodeDispatchInstructions,
        elapsed,
        run.Cpu.State.Cycles,
        allocated,
        CreateCiaPollChecksum(run.Cpu.State));
}

static CiaPollBenchmarkRun CreateCiaPollCpu()
{
    var bus = new AmigaBus(
        chipRamSize: AmigaConstants.A500BootChipRamSize,
        expansionRamSize: AmigaConstants.A500BootPseudoFastRamSize,
        captureBusAccesses: false,
        enableLiveDisplayDma: false);
    bus.Reset();
    WriteWords(
        bus.ChipRam,
        0x1000,
        [
            0x0839, 0x0006, 0x00BF, 0xE001, // BTST #6,$00BFE001.L
            0x60F6 // BRA.S loop
        ]);
    var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
    cpu.Reset(0x1000, 0x8000);
    return new CiaPollBenchmarkRun(cpu, bus);
}

static uint CreateCiaPollChecksum(M68kCpuState state)
{
    var checksum = state.ProgramCounter ^ state.LastInstructionProgramCounter ^ state.LastOpcode;
    checksum ^= (uint)state.Cycles;
    checksum = unchecked((checksum * 16777619u) ^ state.D[0]);
    checksum = unchecked((checksum * 16777619u) ^ state.StatusRegister);
    return checksum;
}

static OpcodeDispatchBenchmarkResult RunOpcodeDispatchBenchmark(
    OpcodeDispatchBenchmarkWorkload workload,
    M68kOpcodePlanDispatch dispatch,
    BenchmarkOptions options)
{
    using var warmup = CreateOpcodeDispatchCpu(workload, dispatch);
    for (var i = 0; i < options.OpcodeDispatchWarmupInstructions; i++)
    {
        warmup.Cpu.ExecuteInstruction();
    }

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    using var run = CreateOpcodeDispatchCpu(workload, dispatch);
    run.Cpu.PlannedInterpreterCountersEnabled = true;
    run.Cpu.ResetPlannedInterpreterCounters();
    var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
    var start = Stopwatch.GetTimestamp();
    for (var i = 0; i < options.OpcodeDispatchInstructions; i++)
    {
        run.Cpu.ExecuteInstruction();
    }

    var elapsed = Stopwatch.GetElapsedTime(start);
    var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
    ValidateOpcodeDispatchProgramUnchanged(workload, run.Bus);
    return new OpcodeDispatchBenchmarkResult(
        options.OpcodeDispatchInstructions,
        elapsed,
        run.Cpu.State.Cycles,
        allocated,
        run.Cpu.CapturePlannedInterpreterCounters(),
        CreateOpcodeDispatchChecksum(run.Cpu.State, run.Bus));
}

static OpcodeDispatchBenchmarkRun CreateOpcodeDispatchCpu(
    OpcodeDispatchBenchmarkWorkload workload,
    M68kOpcodePlanDispatch dispatch)
{
    var bus = new OpcodeDispatchBenchmarkBus(workload.MemorySize);
    WriteWords(bus.Memory, 0x1000, workload.Program);
    var cpu = M68kCoreFactory.CreateM68000Core(
        bus,
        default(OpcodeDispatchBenchmarkCpuDataAccess),
        new M68kCpuState(),
        instructionFrequency: null,
        enableInstructionFetchWindow: true,
        enableOpcodePlan: dispatch != M68kOpcodePlanDispatch.Scalar,
        opcodePlanDispatch: dispatch);
    cpu.Reset(0x1000, 0x8000);
    workload.Setup(cpu.State, bus);
    return new OpcodeDispatchBenchmarkRun(cpu, bus);
}

static OpcodeDispatchBenchmarkWorkload[] CreateOpcodeDispatchWorkloads()
    =>
    [
        new OpcodeDispatchBenchmarkWorkload(
            "full-contact-transform",
            [
                0x2018, // MOVE.L (A0)+,D0
                0x221A, // MOVE.L (A2)+,D1
                0xB183, // EOR.L D0,D3
                0xB383, // EOR.L D1,D3
                0xC087, // AND.L D7,D0
                0xC287, // AND.L D7,D1
                0xD080, // ADD.L D0,D0
                0x8081, // OR.L D1,D0
                0x22C0, // MOVE.L D0,(A1)+
                0x51CA, 0xFFEC, // DBRA D2,loop
                0x60E8 // BRA.S loop
            ],
            SetupFullContactTransformBenchmark,
            OpcodeDispatchTransformMemorySize),
        new OpcodeDispatchBenchmarkWorkload(
            "branch-btst-immediate",
            [
                0x322E, 0x0002, // MOVE.W 2(A6),D1
                0x0201, 0x00FF, // ANDI.B #$FF,D1
                0x6702, // BEQ.S skip
                0x5380, // SUBQ.L #1,D0
                0x0814, 0x000E, // BTST #14,(A4)
                0x66F2, // BNE.S start
                0x4E71 // NOP
            ],
            SetupBranchImmediateBenchmark,
            OpcodeDispatchDefaultMemorySize),
        new OpcodeDispatchBenchmarkWorkload(
            "register-hot-mix",
            [
                0x7001, // MOVEQ #1,D0
                0x7202, // MOVEQ #2,D1
                0xD081, // ADD.L D1,D0
                0x8081, // OR.L D1,D0
                0xC087, // AND.L D7,D0
                0x4E71, // NOP
                0x60F2 // BRA.S loop
            ],
            SetupRegisterHotMixBenchmark,
            OpcodeDispatchDefaultMemorySize)
    ];

static void SetupFullContactTransformBenchmark(M68kCpuState state, OpcodeDispatchBenchmarkBus bus)
{
    state.A[0] = 0x20_000;
    state.A[1] = 0x80_0000;
    state.A[2] = 0x22_0000;
    state.D[2] = 255;
    state.D[3] = 0x5555_5555;
    state.D[7] = 0x0F0F_0F0F;
    for (var offset = 0; offset < 0x1000; offset += 4)
    {
        bus.WriteLongValue(0x20_000u + (uint)offset, 0x0102_0304u + (uint)offset);
        bus.WriteLongValue(0x22_0000u + (uint)offset, 0x1020_3040u + (uint)offset);
    }
}

static void SetupBranchImmediateBenchmark(M68kCpuState state, OpcodeDispatchBenchmarkBus bus)
{
    state.D[0] = 1024;
    state.A[4] = 0x2400;
    state.A[6] = 0x2000;
    bus.WriteWordValue(0x2002, 0x007F);
    bus.WriteWordValue(0x2400, 0x4000);
}

static void SetupRegisterHotMixBenchmark(M68kCpuState state, OpcodeDispatchBenchmarkBus bus)
{
    _ = bus;
    state.D[7] = 0x0F0F_0F0F;
}

static uint CreateOpcodeDispatchChecksum(M68kCpuState state, OpcodeDispatchBenchmarkBus bus)
{
    var checksum = state.ProgramCounter ^ state.LastInstructionProgramCounter ^ state.LastOpcode;
    checksum ^= (uint)state.Cycles;
    for (var i = 0; i < 8; i++)
    {
        checksum = unchecked((checksum * 16777619u) ^ state.D[i]);
        checksum = unchecked((checksum * 16777619u) ^ state.A[i]);
    }

    checksum ^= bus.ReadLongValue(0x80_0000);
    checksum ^= bus.ReadLongValue(0x80_0004);
    return checksum;
}

static void ValidateOpcodeDispatchProgramUnchanged(OpcodeDispatchBenchmarkWorkload workload, OpcodeDispatchBenchmarkBus bus)
{
    for (var i = 0; i < workload.Program.Length; i++)
    {
        var address = 0x1000u + (uint)(i * 2);
        var actual = bus.ReadWordValue(address);
        if (actual != workload.Program[i])
        {
            throw new InvalidOperationException(
                $"Opcode dispatch benchmark workload '{workload.Name}' overwrote generated code at 0x{address:X8}: expected 0x{workload.Program[i]:X4}, actual 0x{actual:X4}.");
        }
    }
}

static void WriteWords(byte[] memory, int address, ReadOnlySpan<ushort> words)
{
    for (var i = 0; i < words.Length; i++)
    {
        memory[address + (i * 2)] = (byte)(words[i] >> 8);
        memory[address + (i * 2) + 1] = (byte)words[i];
    }
}

static BenchmarkRunResult RunBenchmark(BenchmarkWorkload workload, BenchmarkOptions options)
{
    var timingMode = "hrm";
    var emulator = CreateEmulator(workload, options);
    if (emulator == null)
    {
        return new BenchmarkRunResult(
            workload,
            timingMode,
            true,
            0,
            0,
            0,
            default,
            default,
            default,
            0,
            0,
            0,
            0,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            0,
            M68kInstructionFrequencySnapshot.Empty,
            M68kPlannedInterpreterCounters.Empty,
            "missing",
            default,
            Array.Empty<AmigaDiskTraceEvent>(),
            false,
            0,
            "missing disk image");
    }

    var benchmarkBus = GetMachine(emulator).Bus;
    benchmarkBus.SetBlitterAdvanceMode(options.BlitterAdvanceMode);
    benchmarkBus.Blitter.SetBoundedFixedSlotExecutionEnabledForTest(
        options.BlitterAdvanceMode == BlitterAdvanceMode.Bounded);

    var audio = new float[emulator.AudioFramesPerAppFrame(SampleRate) * Channels];
    using var audioAudit = CreateAudioAuditWriter(options, workload);
    var audioFrames = 0;
    var warmupFramesRun = 0;
    for (var frame = 0; frame < options.WarmupFrames; frame++)
    {
        var frameStartTimestamp = Stopwatch.GetTimestamp();
        ApplyFrameActions(emulator, workload, options, frame);
        emulator.RenderNextFrame(renderPresentation: !options.SkipPresentation);
        audioFrames = emulator.RenderAudio(audio, SampleRate, Channels);
        warmupFramesRun = frame + 1;
        WriteProgressIfNeeded(emulator, workload, options, "warmup", warmupFramesRun, Stopwatch.GetElapsedTime(frameStartTimestamp).TotalMilliseconds);
        if (ShouldStopAtCylinder(emulator, options.StopCylinder))
        {
            break;
        }
    }

    LaunchWorkbenchPathIfNeeded(emulator, workload);

    if (options.InstructionMatrix)
    {
        emulator.ResetInstructionFrequency();
        emulator.SetInstructionFrequencyEnabled(true);
    }

    if (options.OpcodeDispatch.HasValue)
    {
        emulator.SetPlannedInterpreterCountersEnabled(true);
        emulator.ResetPlannedInterpreterCounters();
    }

    if (options.HardwareProfile)
    {
        var machine = GetMachine(emulator);
        machine.Bus.ResetHardwareSchedulerHostProfile();
        machine.Bus.SetHardwareSchedulerHostProfilingEnabled(true);
    }

    using var slotScheduleAudit = CreateSlotScheduleAuditWriter(options, workload);
    slotScheduleAudit?.Attach(GetMachine(emulator).Bus);

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    if (options.PauseBeforeMeasureMilliseconds > 0)
    {
        Console.WriteLine($"profile-ready pid={Environment.ProcessId} warmup={warmupFramesRun} pause-ms={options.PauseBeforeMeasureMilliseconds}");
        Console.Out.Flush();
        Thread.Sleep(options.PauseBeforeMeasureMilliseconds);
    }

    var nominalFrameAudioMilliseconds = emulator.AudioFramesPerAppFrame(SampleRate) * 1000.0 / SampleRate;
    var fakeQueuedAudioMilliseconds = nominalFrameAudioMilliseconds * 8.0;
    var fakeQueuedAudioLimitMilliseconds = nominalFrameAudioMilliseconds * 8.0;
    var fakeQueuedAudioMinMilliseconds = fakeQueuedAudioMilliseconds;
    var fakeQueuedAudioMaxMilliseconds = fakeQueuedAudioMilliseconds;
    var fakeAudioSubmitFailures = 0;
    var activeAudioFrames = 0;
    var maxFrameMilliseconds = 0.0;
    var maxFrameSchedulerDrains = 0L;
    var measuredDescriptorBuilds = 0;
    var measuredDescriptorReplayAttempts = 0;
    var measuredDescriptorReplayedRows = 0;
    var measuredDescriptorFallbackRows = 0;
    var measuredDescriptorBitplaneRows = 0;
    var measuredDescriptorSpriteRows = 0;
    var measuredDescriptorMismatches = 0;
    var slowFramesOver20 = 0;
    var slowFramesOver33 = 0;
    var slowFramesOver40 = 0;
    var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
    var startTimestamp = Stopwatch.GetTimestamp();
    var currentMeasuredFrame = 0;
    var currentAbsoluteFrame = warmupFramesRun;
    var crashReportWritten = false;
    try
    {
        for (var frame = 0; frame < options.MeasuredFrames; frame++)
        {
            currentMeasuredFrame = frame + 1;
            currentAbsoluteFrame = warmupFramesRun + currentMeasuredFrame;
            slotScheduleAudit?.SetFrame(currentAbsoluteFrame, currentMeasuredFrame);
            var schedulerBeforeFrame = CaptureHardwareSchedulerSnapshot(emulator);
            var diskBeforeFrame = audioAudit == null ? default : CaptureDiskSummary(emulator);
            var frameStartTimestamp = Stopwatch.GetTimestamp();
            ApplyFrameActions(emulator, workload, options, warmupFramesRun + frame);
            emulator.RenderNextFrame(renderPresentation: !options.SkipPresentation);
            audioFrames = emulator.RenderAudio(audio, SampleRate, Channels);
            WriteMeasuredFrameDumpIfNeeded(options, emulator, currentAbsoluteFrame);
            WriteProgressIfNeeded(emulator, workload, options, "measure", currentMeasuredFrame, Stopwatch.GetElapsedTime(frameStartTimestamp).TotalMilliseconds);
            if (options.StopOnDebugSnapshot && emulator.DebugSnapshot != null)
            {
                WriteAudioAuditDebugSnapshot(audioAudit, options, emulator, currentAbsoluteFrame, currentMeasuredFrame);
                crashReportWritten = true;
                throw new InvalidOperationException($"Emulator debug snapshot captured at frame {currentAbsoluteFrame}.");
            }

            var schedulerAfterFrame = CaptureHardwareSchedulerSnapshot(emulator);
            var frameSchedulerDrains = schedulerAfterFrame.DrainCount - schedulerBeforeFrame.DrainCount;
            maxFrameSchedulerDrains = Math.Max(maxFrameSchedulerDrains, frameSchedulerDrains);
            var frameMilliseconds = Stopwatch.GetElapsedTime(frameStartTimestamp).TotalMilliseconds;
            maxFrameMilliseconds = Math.Max(maxFrameMilliseconds, frameMilliseconds);
            if (frameMilliseconds > 20.0)
            {
                slowFramesOver20++;
            }

            if (frameMilliseconds > 33.0)
            {
                slowFramesOver33++;
            }

            if (frameMilliseconds > 40.0)
            {
                slowFramesOver40++;
            }

            var queueBeforeDrain = fakeQueuedAudioMilliseconds;
            fakeQueuedAudioMilliseconds -= frameMilliseconds;
            var queueAfterDrain = fakeQueuedAudioMilliseconds;
            var audioUnderrun = false;
            fakeQueuedAudioMinMilliseconds = Math.Min(fakeQueuedAudioMinMilliseconds, fakeQueuedAudioMilliseconds);
            if (fakeQueuedAudioMilliseconds < 0.0)
            {
                fakeAudioSubmitFailures++;
                fakeQueuedAudioMilliseconds = 0.0;
                fakeQueuedAudioMinMilliseconds = 0.0;
                audioUnderrun = true;
            }

            var audioSpan = audio.AsSpan(0, Math.Min(audio.Length, audioFrames * Channels));
            var activeAudio = HasActiveAudio(audioSpan);
            var frameAudioSummary = CaptureAudioSummary(audioSpan, audioFrames);
            if (activeAudio)
            {
                activeAudioFrames++;
            }

            fakeQueuedAudioMilliseconds = Math.Min(
                fakeQueuedAudioLimitMilliseconds,
                fakeQueuedAudioMilliseconds + (audioFrames * 1000.0 / SampleRate));
            fakeQueuedAudioMaxMilliseconds = Math.Max(fakeQueuedAudioMaxMilliseconds, fakeQueuedAudioMilliseconds);
            if (audioAudit != null &&
                (audioUnderrun ||
                    currentMeasuredFrame == 1 ||
                    currentMeasuredFrame % options.AudioAuditIntervalFrames == 0))
            {
                var diskAfterFrame = CaptureDiskSummary(emulator);
                WriteAudioAuditFrame(
                    audioAudit,
                    emulator,
                    currentAbsoluteFrame,
                    currentMeasuredFrame,
                    frameMilliseconds,
                    frameSchedulerDrains,
                    queueBeforeDrain,
                    queueAfterDrain,
                    fakeQueuedAudioMilliseconds,
                    audioUnderrun,
                    audioFrames,
                    activeAudio,
                    frameAudioSummary,
                    diskAfterFrame,
                    FormatDiskSchedulerDelta(diskBeforeFrame, diskAfterFrame),
                    diagnostics: FormatPaulaAudit(GetMachine(emulator).Bus.Paula));
            }
        }
    }
    catch (Exception ex)
    {
        if (!crashReportWritten)
        {
            WriteAudioAuditCrash(audioAudit, options, emulator, currentAbsoluteFrame, currentMeasuredFrame, ex);
        }

        throw;
    }
    finally
    {
        slotScheduleAudit?.Detach();
    }

    var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
    var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
    if (!string.IsNullOrWhiteSpace(options.DumpFramePath))
    {
        WriteFramebufferBmp(FormatDumpFramePath(options.DumpFramePath, warmupFramesRun + options.MeasuredFrames), emulator.Framebuffer, emulator.Width, emulator.Height);
    }

    if (!string.IsNullOrWhiteSpace(options.DumpChipRamPath))
    {
        WriteChipRamDump(FormatDumpFramePath(options.DumpChipRamPath, warmupFramesRun + options.MeasuredFrames), emulator);
    }

    if (!string.IsNullOrWhiteSpace(options.DumpDebugSnapshotPath))
    {
        WriteDebugSnapshotDump(
            FormatDumpFramePath(options.DumpDebugSnapshotPath, warmupFramesRun + options.MeasuredFrames),
            emulator,
            warmupFramesRun + options.MeasuredFrames,
            "BENCHMARK_DUMP",
            "Benchmark debug snapshot dump.");
    }

    var fps = options.MeasuredFrames / elapsed.TotalSeconds;
    var framebufferSummary = CaptureFramebufferSummary(emulator.Framebuffer);
    var audioSummary = CaptureAudioSummary(audio.AsSpan(0, Math.Min(audio.Length, audioFrames * Channels)), audioFrames);
    var displaySummary = CaptureDisplaySummary(GetDisplay(emulator)) with
    {
        DescriptorBuilds = measuredDescriptorBuilds,
        DescriptorReplayAttempts = measuredDescriptorReplayAttempts,
        DescriptorReplayedRows = measuredDescriptorReplayedRows,
        DescriptorFallbackRows = measuredDescriptorFallbackRows,
        DescriptorBitplaneRows = measuredDescriptorBitplaneRows,
        DescriptorSpriteRows = measuredDescriptorSpriteRows,
        DescriptorMismatches = measuredDescriptorMismatches
    };
    var diskSummary = CaptureDiskSummary(emulator);
    var specializationSummary = CaptureSpecializationSummary(emulator);
    var schedulerSummary = CaptureHardwareSchedulerSnapshot(emulator);
    if (options.HardwareProfile)
    {
        GetMachine(emulator).Bus.SetHardwareSchedulerHostProfilingEnabled(false);
    }

    var instructionFrequency = options.InstructionMatrix
        ? emulator.InstructionFrequency
        : M68kInstructionFrequencySnapshot.Empty;
    var plannedInterpreterCounters = options.OpcodeDispatch.HasValue
        ? emulator.PlannedInterpreterCounters
        : M68kPlannedInterpreterCounters.Empty;
    if (options.InstructionMatrix)
    {
        emulator.SetInstructionFrequencyEnabled(false);
    }

    if (options.OpcodeDispatch.HasValue)
    {
        emulator.SetPlannedInterpreterCountersEnabled(false);
    }

    var phase = options.Smoke || workload.FileName == null || options.SkipPhaseProfile
        ? default
        : ProfileFrames(emulator, Math.Min(120, options.MeasuredFrames));
    var display = GetDisplay(emulator);
    var agnus = GetAgnusSnapshot(emulator);
    return new BenchmarkRunResult(
        workload,
        timingMode,
        false,
        fps,
        elapsed.TotalMilliseconds / options.MeasuredFrames,
        allocated,
        new FrameTimingSummary(maxFrameMilliseconds, slowFramesOver20, slowFramesOver33, slowFramesOver40),
        new AudioQueueSummary(activeAudioFrames, fakeAudioSubmitFailures, fakeQueuedAudioMinMilliseconds, fakeQueuedAudioMaxMilliseconds),
        phase,
        display.LiveDisplayEventCount,
        display.LiveCopperStepCount,
        display.LivePendingWriteEventCount,
        display.LiveFetchBatchWordCount,
        framebufferSummary,
        audioSummary,
        displaySummary,
        diskSummary,
        specializationSummary,
        agnus,
        schedulerSummary,
        maxFrameSchedulerDrains,
        instructionFrequency,
        plannedInterpreterCounters,
        FormatCpuBackendName(emulator.CpuBackendName, options.OpcodeDispatch),
        emulator.JitCounters,
        options.DiskDivergenceTrace ? CaptureDiskTrace(emulator) : Array.Empty<AmigaDiskTraceEvent>(),
        emulator.CopperStartRuntimeHandoffActive,
        emulator.CopperStartRuntimeHandoffCount,
        CaptureStatusText(emulator));
}

static void RunDiskDivergenceTrace(BenchmarkWorkload workload, BenchmarkOptions options)
{
    var previousTrace = Environment.GetEnvironmentVariable("COPPER_DISK_DIVERGENCE_TRACE");
    Environment.SetEnvironmentVariable("COPPER_DISK_DIVERGENCE_TRACE", "1");
    try
    {
        var interpreterOptions = options with
        {
            CpuBackend = "interpreter",
            InstructionMatrix = false
        };
        var jitOptions = options with
        {
            CpuBackend = "jit",
            InstructionMatrix = false
        };

        Console.WriteLine($"disk-trace-workload\t{workload.Name}");
        var interpreter = RunBenchmark(workload, interpreterOptions);
        var jit = RunBenchmark(workload, jitOptions);
        if (interpreter.Missing || jit.Missing)
        {
            Console.WriteLine($"disk-trace-missing\t{workload.Name}\tinterpreter={interpreter.StatusText}\tjit={jit.StatusText}");
            return;
        }

        Console.WriteLine($"disk-trace-summary\tinterpreter\tfps={interpreter.FramesPerSecond:F1}\tdisk={FormatDiskSummary(interpreter.Disk)}\ttrace={interpreter.DiskTrace.Length}\tfirstSeq={GetFirstTraceSequence(interpreter.DiskTrace)}");
        Console.WriteLine($"disk-trace-summary\tjit\tfps={jit.FramesPerSecond:F1}\tdisk={FormatDiskSummary(jit.Disk)}\ttrace={jit.DiskTrace.Length}\tfirstSeq={GetFirstTraceSequence(jit.DiskTrace)}");
        WriteDiskTraceComparison(interpreter.DiskTrace, jit.DiskTrace);
    }
    finally
    {
        Environment.SetEnvironmentVariable("COPPER_DISK_DIVERGENCE_TRACE", previousTrace);
    }
}

static long GetFirstTraceSequence(AmigaDiskTraceEvent[] trace)
    => trace.Length == 0 ? -1 : trace[0].Sequence;

static void WriteDiskTraceComparison(AmigaDiskTraceEvent[] interpreter, AmigaDiskTraceEvent[] jit)
{
    var comparableInterpreter = GetComparableDiskTrace(interpreter);
    var comparableJit = GetComparableDiskTrace(jit);
    Console.WriteLine($"disk-trace-comparable\tinterpreter={comparableInterpreter.Length}\tjit={comparableJit.Length}");
    var max = Math.Max(comparableInterpreter.Length, comparableJit.Length);
    for (var i = 0; i < max; i++)
    {
        var hasInterpreter = i < comparableInterpreter.Length;
        var hasJit = i < comparableJit.Length;
        if (!hasInterpreter || !hasJit)
        {
            Console.WriteLine($"disk-trace-first-mismatch\tindex={i}\tclassification={ClassifyDiskTraceMismatch(hasInterpreter ? comparableInterpreter[i] : null, hasJit ? comparableJit[i] : null)}");
            WriteDiskTraceContext(comparableInterpreter, comparableJit, i);
            return;
        }

        var interpreterText = GetDiskTraceComparisonText(comparableInterpreter[i]);
        var jitText = GetDiskTraceComparisonText(comparableJit[i]);
        if (!string.Equals(interpreterText, jitText, StringComparison.Ordinal))
        {
            Console.WriteLine($"disk-trace-first-mismatch\tindex={i}\tclassification={ClassifyDiskTraceMismatch(comparableInterpreter[i], comparableJit[i])}");
            Console.WriteLine($"disk-trace-interpreter\t{GetDiskTraceSignificantText(comparableInterpreter[i])}");
            Console.WriteLine($"disk-trace-jit\t{GetDiskTraceSignificantText(comparableJit[i])}");
            WriteDiskTraceContext(comparableInterpreter, comparableJit, i);
            return;
        }
    }

    Console.WriteLine("disk-trace-match\tNo disk trace divergence found in captured events.");
}

static AmigaDiskTraceEvent[] GetComparableDiskTrace(AmigaDiskTraceEvent[] trace)
    => trace
        .Where(entry =>
            entry.Kind != AmigaDiskTraceEventKind.DiskInputAdvance &&
            entry.Kind != AmigaDiskTraceEventKind.WakeCandidate)
        .ToArray();

static void WriteDiskTraceContext(AmigaDiskTraceEvent[] interpreter, AmigaDiskTraceEvent[] jit, int mismatchIndex)
{
    var start = Math.Max(0, mismatchIndex - 3);
    var end = Math.Min(Math.Max(interpreter.Length, jit.Length) - 1, mismatchIndex + 3);
    for (var i = start; i <= end; i++)
    {
        Console.WriteLine($"disk-trace-context\t{i}\tinterpreter\t{(i < interpreter.Length ? GetDiskTraceSignificantText(interpreter[i]) : "<missing>")}");
        Console.WriteLine($"disk-trace-context\t{i}\tjit\t{(i < jit.Length ? GetDiskTraceSignificantText(jit[i]) : "<missing>")}");
    }
}

static string ClassifyDiskTraceMismatch(AmigaDiskTraceEvent? interpreter, AmigaDiskTraceEvent? jit)
{
    if (!interpreter.HasValue || !jit.HasValue)
    {
        return "trace length differs; one backend produced extra disk events";
    }

    var left = interpreter.Value;
    var right = jit.Value;
    if (IsCpuDiskWriteKind(left.Kind) || IsCpuDiskWriteKind(right.Kind))
    {
        return "CPU disk register writes differ; likely JIT CPU semantics, PC path, flags, or bus cycle accounting";
    }

    if (left.Kind == AmigaDiskTraceEventKind.DiskInputAdvance &&
        right.Kind == AmigaDiskTraceEventKind.DiskInputAdvance &&
        left.Cycle == right.Cycle &&
        left.TargetCycle == right.TargetCycle)
    {
        return "same disk advance interval produced different disk state; chunk-invariance or float rounding is suspect";
    }

    if (left.Kind == AmigaDiskTraceEventKind.WakeCandidate ||
        right.Kind == AmigaDiskTraceEventKind.WakeCandidate)
    {
        return "disk wake candidate differs; likely JIT batch wake policy or device event clamping";
    }

    if (left.Cycle != right.Cycle)
    {
        return "disk event timing differs after matching CPU writes; likely JIT batching/wake timing or disk advancement ordering";
    }

    return "disk state differs at matching event cycle; likely disk model chunk-invariance or interpreter/JIT state sampling difference";
}

static bool IsCpuDiskWriteKind(AmigaDiskTraceEventKind kind)
    => kind == AmigaDiskTraceEventKind.RegisterWrite ||
        kind == AmigaDiskTraceEventKind.CiaBDriveControlWrite;

static string GetDiskTraceComparisonText(AmigaDiskTraceEvent entry)
{
    var streamText = entry.Kind == AmigaDiskTraceEventKind.DmaWord
        ? string.Empty
        : $",stream={entry.StreamCycle}/{entry.StreamOffset}";
    return FormatDiskTraceText(entry, streamText, includeDskbytr: entry.Kind != AmigaDiskTraceEventKind.DmaWord);
}

static string GetDiskTraceSignificantText(AmigaDiskTraceEvent entry)
{
    var streamText = $",stream={entry.StreamCycle}/{entry.StreamOffset}/{entry.StreamPosition:G17}";
    return FormatDiskTraceText(entry, streamText, includeDskbytr: true);
}

static string FormatDiskTraceText(AmigaDiskTraceEvent entry, string streamText, bool includeDskbytr)
{
    var pcText = IsCpuDiskWriteKind(entry.Kind)
        ? $",pc=0x{entry.LastInstructionProgramCounter & 0x00FF_FFFF:X6},op=0x{entry.LastOpcode:X4}"
        : string.Empty;
    var dskbytrText = includeDskbytr
        ? $",bytr=0x{entry.Dskbytr:X4}"
        : string.Empty;
    return $"{entry.Kind},cycle={entry.Cycle},target={entry.TargetCycle},candidate={entry.CandidateCycle}{pcText},reg=0x{entry.Register:X3},value=0x{entry.Value:X4},beam={entry.BeamLine}:{entry.BeamHorizontal},dmacon=0x{entry.Dmacon:X4},adkcon=0x{entry.Adkcon:X4},intena=0x{entry.Intena:X4},intreq=0x{entry.Intreq:X4},dsklen=0x{entry.Dsklen:X4}{dskbytrText},datr=0x{entry.Dskdatr:X4},ptr=0x{entry.DiskPointer:X6},sel={entry.SelectedDrive},act={entry.ActiveDma},actDrive={entry.ActiveDmaDrive},cyl={entry.Cylinder},head={entry.Head},pending={entry.PendingReadDmaWords},req={entry.RequestedWords},xfer={entry.TransferredWords},src={entry.SourceBit}{streamText},addr=0x{entry.TargetAddress:X6},done={entry.CompletionCycle},detail={FormatCounterText(entry.Detail)}";
}

static void ApplyFrameActions(CopperScreenEmulator emulator, BenchmarkWorkload workload, BenchmarkOptions options, int frame)
{
    if (workload.FireFrame == frame)
    {
        emulator.PulsePrimaryFire(frames: 30);
    }

    if (ShouldPulseFire(options, frame))
    {
        emulator.PulsePrimaryFire(options.FirePulseDurationFrames);
    }

    var primaryFirePressed = IsFrameInRange(frame, options.FireStartFrame, options.FireEndFrame);
    var leftPressed = IsFrameInRange(frame, options.LeftStartFrame, options.LeftEndFrame);
    var rightPressed = IsFrameInRange(frame, options.RightStartFrame, options.RightEndFrame);
    if (HasScriptedJoystickInput(options))
    {
        for (var portIndex = 0; portIndex < 2; portIndex++)
        {
            emulator.SetJoystickPort(
                portIndex,
                up: false,
                down: false,
                left: leftPressed,
                right: rightPressed,
                primaryFirePressed: primaryFirePressed,
                secondFirePressed: false);
        }
    }
}

static bool HasScriptedJoystickInput(BenchmarkOptions options)
{
    return options.FireStartFrame >= 0 ||
        options.LeftStartFrame >= 0 ||
        options.RightStartFrame >= 0;
}

static bool ShouldPulseFire(BenchmarkOptions options, int frame)
{
    if (options.FirePulseStartFrame < 0 ||
        options.FirePulseIntervalFrames <= 0 ||
        frame < options.FirePulseStartFrame ||
        frame > Math.Max(options.FirePulseStartFrame, options.FirePulseEndFrame))
    {
        return false;
    }

    return (frame - options.FirePulseStartFrame) % options.FirePulseIntervalFrames == 0;
}

static bool IsFrameInRange(int frame, int startFrame, int endFrame)
{
    return startFrame >= 0 &&
        frame >= startFrame &&
        frame <= Math.Max(startFrame, endFrame);
}

static bool ShouldStopAtCylinder(CopperScreenEmulator emulator, int? stopCylinder)
{
    if (!stopCylinder.HasValue)
    {
        return false;
    }

    var disk = GetMachine(emulator).Bus.Disk.CaptureSnapshot();
    return disk.LastTransferCylinder >= stopCylinder.Value;
}

static void WriteProgressIfNeeded(
    CopperScreenEmulator emulator,
    BenchmarkWorkload workload,
    BenchmarkOptions options,
    string phase,
    int frame,
    double milliseconds)
{
    if (options.ProgressIntervalFrames <= 0 ||
        frame % options.ProgressIntervalFrames != 0)
    {
        return;
    }

    var machine = GetMachine(emulator);
    var disk = machine.Bus.Disk.CaptureSnapshot();
    var display = emulator.DisplaySnapshot;
    Console.Error.WriteLine(
        $"progress {workload.Name} {phase} frame={frame} ms={milliseconds:F2} " +
        $"cyl={disk.LastTransferCylinder}.{disk.LastTransferHead} driveCyl={disk.Cylinder}.{disk.Head} " +
        $"xfer={disk.TransferCount} active={disk.ActiveDma} dsklen=0x{disk.Dsklen:X4} " +
        $"pc=0x{machine.Cpu.State.ProgramCounter & 0x00FF_FFFF:X6} sr=0x{machine.Cpu.State.StatusRegister:X4} " +
        $"dmacon=0x{machine.Bus.Paula.Dmacon:X4} intena=0x{machine.Bus.Paula.Intena:X4} intreq=0x{machine.Bus.Paula.Intreq:X4} " +
        $"bplcon=0x{display.Bplcon0:X4}/0x{display.Bplcon1:X4}/0x{display.Bplcon2:X4} " +
        $"ddf=0x{display.DdfStart:X4}/0x{display.DdfStop:X4} diw=0x{display.DiwStart:X4}/0x{display.DiwStop:X4} " +
        $"mod={display.Bpl1Mod}/{display.Bpl2Mod} ptr={FormatHexArray(display.BitplanePointers)} base={FormatIntArray(display.BitplaneBaseRows)} " +
        $"bplPix={display.LastBitplaneNonZeroPixels} sprPix={display.LastSpriteNonZeroPixels} spans={GetDisplay(emulator).BitplaneDataSpanCount} " +
        $"copper={GetDisplay(emulator).LiveCopperStepCount} pending={GetDisplay(emulator).LivePendingWriteEventCount} " +
        $"status=\"{emulator.StatusText}\"");
}

static string FormatHexArray(IReadOnlyList<uint> values)
{
    return string.Join(
        '/',
        values.Select(value => value.ToString("X6", System.Globalization.CultureInfo.InvariantCulture)));
}

static string FormatIntArray(IReadOnlyList<int> values)
{
    return string.Join('/', values);
}

static void WriteMeasuredFrameDumpIfNeeded(BenchmarkOptions options, CopperScreenEmulator emulator, int frame)
{
    if (string.IsNullOrWhiteSpace(options.DumpFramePath) ||
        options.DumpFramePath.IndexOf("{frame}", StringComparison.OrdinalIgnoreCase) < 0 ||
        frame % options.DumpFrameInterval != 0)
    {
        return;
    }

    WriteFramebufferBmp(FormatDumpFramePath(options.DumpFramePath, frame), emulator.Framebuffer, emulator.Width, emulator.Height);
}

static string FormatDumpFramePath(string path, int frame)
{
    return path.Replace("{frame}", frame.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
}

static void WriteBenchmarkHeader()
{
    Console.WriteLine("name\tagnus\tbackend\tframes/sec\treal-time\tms/frame\tmax ms\tslow>20\tslow>33\tslow>40\tactive audio frames\tfake audio submit failures\tfake audio min ms\tfake audio max ms\tallocated bytes\tplanned fast\tplanned fallback\tplanned hit%\tplanned nop\tplanned moveq\tplanned branch\tplanned dbcc\tplanned quick\tplanned move\tplanned imm\tplanned btst\tplanned arith\tcpu%\tagnus%\tdisplay%\tjit traces\tjit classic methods\tjit pure classic methods\tjit v2 methods\tjit sync compile ms\tjit sync compile alloc\tjit hits\tjit exits\tjit fallback\tjit fallback reason top\tjit fallback instr top\tjit fallback root top\tjit invalid\tjit unsupported opcode\tjit unsupported ea\tjit host trap\tjit system\tjit exception\tjit generation\tjit boundary\tjit selfmod\tjit direct il\tjit helper il\tjit direct cpu\tjit direct mem\tjit spec helper\tjit v2 tier0\tjit v2 tier1\tjit v2 tier2\tjit v2 tier3\tjit v2 hits\tjit v2 exits\tjit v2 exit entry\tjit v2 exit branch\tjit v2 exit before\tjit v2 exit beyond\tjit v2 exit chip\tjit v2 exit host\tjit v2 exit hole\tjit v2 exit fall\tjit v2 flags\tjit v2 branch pending\tjit v2 branch sr\tjit v2 writes\tjit v2 zw read rf\tjit v2 zw read rom\tjit v2 zw read overlay\tjit v2 zw write rf\tjit v2 zw read slow\tjit v2 zw write slow\tjit v2 promotions\tjit v2 pressure promotions\tjit v2 worker graphs\tjit v2 worker graph instr\tjit v2 worker graph bytes\tjit async queued\tjit async dedup\tjit async dropped\tjit async bumps\tjit async snap avoided\tjit async started\tjit async completed\tjit async failed\tjit async installed\tjit async stale\tjit async superseded\tjit async tier0\tjit async tier1\tjit async tier2\tjit async tier3\tjit async maxq\tjit async ms\tjit async alloc\tjit v2 rejected\tjit v2 rej chip\tjit v2 rej host\tjit v2 rej dec\tjit v2 rej op\tjit v2 rej ea\tjit v2 rej budget\tjit v2 rej empty\tjit v2 disabled holes\tjit v2 disabled branches\tjit v2 branch limited\tjit v2 disabled entry\tjit v2 hole compiles\tjit v2 handoff try\tjit v2 handoff hit\tjit v2 handoff instr\tjit v2 handoff fail\tjit v2 handoff queued wait\tjit v2 handoff cache hit\tjit v2 handoff cache store\tjit v2 handoff no slack\tjit v2 bus batch\tjit v2 bus instr\tjit v2 bus saved\tjit v2 bus hist\tjit v2 bus wake\tjit v2 rej op top\tjit v2 rej ea top\tjit v2 hole top\tjit v2 handoff block top\tjit v2 handoff fail top\tjit v2 branch limit top\tjit v2 branch target state top\tjit gen guard\tjit pure batch\tjit pure instr\tjit pure saved\tjit pure exits\tjit pure hist\tjit pure wake\tjit stopped ff\tjit stopped cycles\tlive events\tcopper\tpending\tfetches\tframebuffer\taudio\tdisplay summary\tdisk\tspecialization\tretained slots\tslot grants\tgrant mix\tdenied\tdenied mix\tblocked by\tlast denied\thardware profile\tstatus");
}

static void WriteBenchmarkResult(BenchmarkRunResult result, BenchmarkOptions options)
{
    if (result.Missing)
    {
        Console.WriteLine($"{result.Workload.Name}\t{result.TimingMode}\t{result.CpuBackend}\tmissing{new string('\t', 120)}{result.StatusText}");
        if (options.InstructionMatrix)
        {
            WriteInstructionMatrix(result, options.TopInstructionOpcodes);
        }

        return;
    }

    result = result with { StatusText = FormatStatusWithScheduler(result) };
    var agnus = result.Agnus;
    var jit = result.Jit;
    var timing = result.Timing;
    var audioQueue = result.AudioQueue;
    var planned = result.PlannedInterpreterCounters;
    Console.WriteLine(
        $"{result.Workload.Name}\t{result.TimingMode}\t{result.CpuBackend}\t{result.FramesPerSecond:F1}\t{result.FramesPerSecond / 50.0:F2}x\t{result.MillisecondsPerFrame:F2}\t{timing.MaxMilliseconds:F2}\t{timing.SlowFramesOver20}\t{timing.SlowFramesOver33}\t{timing.SlowFramesOver40}\t{audioQueue.ActiveFrames}\t{audioQueue.SubmitFailures}\t{audioQueue.MinQueuedMilliseconds:F2}\t{audioQueue.MaxQueuedMilliseconds:F2}\t{result.AllocatedBytes}\t{planned.FastInstructions}\t{planned.ScalarFallbackInstructions}\t{FormatPlannedHitPercent(planned)}\t{planned.NopInstructions}\t{planned.MoveqInstructions}\t{planned.BranchInstructions}\t{planned.DbccInstructions}\t{planned.QuickRegisterInstructions}\t{planned.MoveInstructions}\t{planned.ImmediateInstructions}\t{planned.ImmediateBtstInstructions}\t{planned.RegisterArithmeticInstructions}\t{result.Phase.CpuPercent:F1}\t{result.Phase.AgnusPercent:F1}\t{result.Phase.DisplayPercent:F1}\t{jit.CompiledTraces}\t{jit.ClassicTraceMethodsCompiled}\t{jit.PureClassicTraceMethodsCompiled}\t{jit.V2TraceMethodsCompiled}\t{jit.SynchronousCompileMilliseconds}\t{jit.SynchronousCompileAllocatedBytes}\t{jit.TraceHits}\t{jit.SideExits}\t{jit.FallbackInstructions}\t{FormatCounterText(jit.FallbackReasonTop)}\t{FormatCounterText(jit.FallbackInstructionTop)}\t{FormatCounterText(jit.FallbackRootTop)}\t{jit.Invalidations}\t{jit.UnsupportedOpcode}\t{jit.UnsupportedEa}\t{jit.HostTrapBailouts}\t{jit.SystemInstructionBailouts}\t{jit.ExceptionInstructionBailouts}\t{jit.GenerationMismatches}\t{jit.BoundarySideExits}\t{jit.SelfModifiedCodeExits}\t{jit.DirectIlInstructions}\t{jit.HelperIlInstructions}\t{jit.DirectCpuIlInstructions}\t{jit.DirectMemoryIlInstructions}\t{jit.SpecializedHelperIlInstructions}\t{jit.V2Tier0CompiledTraces}\t{jit.V2Tier1CompiledTraces}\t{jit.V2Tier2CompiledTraces}\t{jit.V2Tier3CompiledTraces}\t{jit.V2TraceHits}\t{jit.V2SideExits}\t{jit.V2SideExitEntryMismatch}\t{jit.V2SideExitOutOfBlockBranch}\t{jit.V2SideExitBeforeGraph}\t{jit.V2SideExitBeyondGraph}\t{jit.V2SideExitChipRam}\t{jit.V2SideExitHostTrap}\t{jit.V2SideExitGraphHole}\t{jit.V2SideExitConditionalFallthrough}\t{jit.V2FlagMaterializations}\t{jit.V2BranchPendingFlagChecks}\t{jit.V2BranchStatusFlagChecks}\t{jit.V2LazyWritebacks}\t{jit.V2ZeroWaitReadRealFast}\t{jit.V2ZeroWaitReadRom}\t{jit.V2ZeroWaitReadOverlay}\t{jit.V2ZeroWaitWriteRealFast}\t{jit.V2ZeroWaitReadSlow}\t{jit.V2ZeroWaitWriteSlow}\t{jit.V2TierPromotions}\t{jit.V2TierPressurePromotions}\t{jit.V2WorkerExpandedGraphs}\t{jit.V2WorkerExpandedGraphInstructions}\t{jit.V2WorkerExpandedGraphBytes}\t{jit.AsyncRequestsQueued}\t{jit.AsyncRequestsDeduped}\t{jit.AsyncRequestsDropped}\t{jit.AsyncPriorityBumps}\t{jit.AsyncSnapshotCapturesAvoided}\t{jit.AsyncWorkerCompilesStarted}\t{jit.AsyncWorkerCompilesCompleted}\t{jit.AsyncWorkerCompilesFailed}\t{jit.AsyncCompletedInstalled}\t{jit.AsyncCompletedDiscardedStale}\t{jit.AsyncCompletedDiscardedSuperseded}\t{jit.AsyncTier0Installs}\t{jit.AsyncTier1Installs}\t{jit.AsyncTier2Installs}\t{jit.AsyncTier3Installs}\t{jit.AsyncMaxQueueDepth}\t{jit.AsyncWorkerCompileMilliseconds}\t{jit.AsyncWorkerCompileAllocatedBytes}\t{jit.V2RejectedCandidates}\t{jit.V2RejectedChipRam}\t{jit.V2RejectedHostTrap}\t{jit.V2RejectedDecode}\t{jit.V2RejectedUnsupportedOperation}\t{jit.V2RejectedUnsupportedEa}\t{jit.V2RejectedBudget}\t{jit.V2RejectedEmpty}\t{jit.V2DisabledGraphHoleRoots}\t{jit.V2DisabledBranchExitRoots}\t{jit.V2BranchPressureLimitedRoots}\t{jit.V2DisabledEntryMismatchRoots}\t{jit.V2GraphHoleTargetCompiles}\t{jit.V2TraceHandoffAttempts}\t{jit.V2TraceHandoffExecutions}\t{jit.V2TraceHandoffInstructions}\t{jit.V2TraceHandoffFailures}\t{jit.V2TraceHandoffQueuedNotReady}\t{jit.V2TraceHandoffCacheHits}\t{jit.V2TraceHandoffCacheStores}\t{jit.V2TraceHandoffNoCycleSlack}\t{jit.V2BusAccessBatchExecutions}\t{jit.V2BusAccessBatchInstructions}\t{jit.V2BusAccessBatchBoundaryCallsSaved}\t{FormatCounterText(jit.V2BusAccessBatchLengthHistogram)}\t{FormatCounterText(jit.V2BusAccessBatchWakeSourceTop)}\t{FormatCounterText(jit.V2UnsupportedOperationTop)}\t{FormatCounterText(jit.V2UnsupportedEaTop)}\t{FormatCounterText(jit.V2GraphHoleTop)}\t{FormatCounterText(jit.V2TraceHandoffBlockTop)}\t{FormatCounterText(jit.V2TraceHandoffFailureTop)}\t{FormatCounterText(jit.V2BranchPressureLimitTop)}\t{FormatCounterText(jit.V2BranchPressureTargetStateTop)}\t{jit.GenerationGuardExits}\t{jit.PureTraceBatchExecutions}\t{jit.PureTraceBatchInstructions}\t{jit.PureTraceBatchBoundaryCallsSaved}\t{jit.PureTraceBatchSideExits}\t{FormatCounterText(jit.PureTraceBatchLengthHistogram)}\t{FormatCounterText(jit.PureTraceBatchWakeSourceTop)}\t{jit.StoppedFastForwards}\t{jit.StoppedFastForwardCycles}\t{result.LiveDisplayEventCount}\t{result.LiveCopperStepCount}\t{result.LivePendingWriteEventCount}\t{result.LiveFetchBatchWordCount}\t{FormatFramebufferSummary(result.Framebuffer)}\t{FormatAudioSummary(result.Audio)}\t{FormatDisplaySummary(result.Display)}\t{FormatDiskSummary(result.Disk)}\t{FormatSpecializationSummary(result.Specialization)}\t{agnus.SlotReservationCount}\t{agnus.SlotGrantCount}\t{FormatSlotGrantMix(agnus)}\t{agnus.DeniedFixedSlotCount}\t{FormatDeniedSlotMix(agnus)}\t{FormatDeniedBlockerMix(agnus)}\t{FormatLastDeniedFixedSlot(agnus)}\t{FormatHardwareProfile(result.Scheduler)}\t{result.StatusText}");
    if (options.InstructionMatrix)
    {
        WriteInstructionMatrix(result, options.TopInstructionOpcodes);
    }
}

static void WriteInstructionMatrix(BenchmarkRunResult result, int topOpcodeCount)
{
    var snapshot = result.InstructionFrequency;
    if (snapshot.TotalInstructions <= 0)
    {
        Console.WriteLine($"instruction-summary\t{result.Workload.Name}\t{result.CpuBackend}\t0");
        return;
    }

    Console.WriteLine($"instruction-summary\t{result.Workload.Name}\t{result.CpuBackend}\t{snapshot.TotalInstructions}");
    foreach (var family in snapshot.Families)
    {
        Console.WriteLine(
            $"instruction-family\t{result.Workload.Name}\t{result.CpuBackend}\t{family.FamilyName}\t{family.Count}\t{PercentText(family.Count, snapshot.TotalInstructions)}");
    }

    foreach (var target in snapshot.JitTargets)
    {
        Console.WriteLine(
            $"instruction-jit-target\t{result.Workload.Name}\t{result.CpuBackend}\t{target.TargetName}\t{target.Count}\t{PercentText(target.Count, snapshot.TotalInstructions)}");
    }

    foreach (var opcode in snapshot.Opcodes
        .Where(opcode => opcode.JitTarget != M68kJitTarget.None)
        .Take(Math.Max(0, topOpcodeCount)))
    {
        Console.WriteLine(
            $"instruction-jit-target-opcode\t{result.Workload.Name}\t{result.CpuBackend}\t0x{opcode.Opcode:X4}\t{opcode.Mnemonic}\t{opcode.JitTargetName}\t{opcode.FamilyName}\t{opcode.Count}\t{PercentText(opcode.Count, snapshot.TotalInstructions)}");
    }

    foreach (var opcode in snapshot.Opcodes.Take(Math.Max(0, topOpcodeCount)))
    {
        Console.WriteLine(
            $"instruction-opcode\t{result.Workload.Name}\t{result.CpuBackend}\t0x{opcode.Opcode:X4}\t{opcode.Mnemonic}\t{opcode.FamilyName}\t{opcode.Count}\t{PercentText(opcode.Count, snapshot.TotalInstructions)}");
    }

    foreach (var pc in snapshot.HotPcs.Take(Math.Max(0, topOpcodeCount)))
    {
        Console.WriteLine(
            $"instruction-pc\t{result.Workload.Name}\t{result.CpuBackend}\t{FormatProgramCounter(pc.ProgramCounter)}\t0x{pc.Opcode:X4}\t{pc.Mnemonic}\t{pc.FamilyName}\t{pc.Count}\t{PercentText(pc.Count, snapshot.TotalInstructions)}");
    }

    foreach (var loop in snapshot.HotLoops.Take(Math.Max(0, topOpcodeCount)))
    {
        Console.WriteLine(
            $"hot-loop-block\t{result.Workload.Name}\t{result.CpuBackend}\tstart={FormatProgramCounter(loop.StartProgramCounter)}\tend={FormatProgramCounter(loop.EndProgramCounter)}\tbranch={FormatProgramCounter(loop.BranchProgramCounter)}\ttarget={FormatProgramCounter(loop.TargetProgramCounter)}\top=0x{loop.BranchOpcode:X4}\t{loop.BranchMnemonic}\tbytes={loop.ByteLength}\tcount={loop.Count}\t{PercentText(loop.Count, snapshot.TotalInstructions)}");
    }
}

static string FormatProgramCounter(uint programCounter)
    => programCounter <= 0x00FF_FFFFu
        ? $"0x{programCounter:X6}"
        : $"0x{programCounter:X8}";

static string PercentText(long value, long total)
    => total == 0 ? "0.00" : $"{(value * 100.0) / total:F2}";

static string FormatPlannedHitPercent(M68kPlannedInterpreterCounters counters)
{
    var total = counters.FastInstructions + counters.ScalarFallbackInstructions;
    return total == 0 ? "0.00" : $"{(counters.FastInstructions * 100.0) / total:F2}";
}

static string FormatCounterText(string? value)
    => string.IsNullOrEmpty(value)
        ? string.Empty
        : value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

static CopperScreenEmulator? CreateEmulator(BenchmarkWorkload workload, BenchmarkOptions options)
{
    var previousDispatch = M68kCoreFactory.M68000OpcodePlanDispatch;
    M68kCoreFactory.M68000OpcodePlanDispatch = options.OpcodeDispatch ?? M68kOpcodePlanDispatch.KindTable;
    try
    {
        var fileName = workload.FileName;
        var args = CreateEmulatorArgs(fileName, options, workload.Profile);
        if (fileName == null)
        {
            return args.Length == 0
                ? CopperScreenEmulator.CreateWithoutDisk()
                : CopperScreenEmulator.Create(args, AppContext.BaseDirectory);
        }

        var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", fileName);
        if (diskPath == null)
        {
            return null;
        }

        return CopperScreenEmulator.Create(CreateEmulatorArgs(diskPath, options, workload.Profile), AppContext.BaseDirectory);
    }
    finally
    {
        M68kCoreFactory.M68000OpcodePlanDispatch = previousDispatch;
    }
}

static string[] CreateEmulatorArgs(string? diskPath, BenchmarkOptions options, string? workloadProfile)
{
    var profile = options.Profile ?? workloadProfile;
    var copperQuiescenceDiagnostics = !string.IsNullOrWhiteSpace(options.CopperQuiescenceAuditPath);
    var count = (diskPath == null ? 0 : 1) +
        (string.IsNullOrWhiteSpace(profile) ? 0 : 2) +
        (options.RealKickstart ? 1 : 0) +
        (string.IsNullOrWhiteSpace(options.KickstartRomPath) ? 0 : 2) +
        (string.IsNullOrWhiteSpace(options.CpuBackend) ? 0 : 2) +
        (options.CopperQuiescenceFastPath ? 1 : 0) +
        (options.CopperQuiescenceFastPathVerify ? 1 : 0) +
        (copperQuiescenceDiagnostics ? 1 : 0) +
        (options.DeferredCpuBusBatch ? 1 : 0) +
        (options.DeferredCpuBusBatchVerify ? 1 : 0) +
        (options.CpuWaitSlotReference ? 1 : 0) +
        (options.HardwareSpecialization ? 1 : 0);
    if (count == 0)
    {
        return Array.Empty<string>();
    }

    var args = new string[count];
    var index = 0;
    if (diskPath != null)
    {
        args[index++] = diskPath;
    }

    if (!string.IsNullOrWhiteSpace(profile))
    {
        args[index++] = "--profile";
        args[index++] = profile;
    }

    if (options.RealKickstart)
    {
        args[index++] = "--real-kickstart";
    }

    if (!string.IsNullOrWhiteSpace(options.KickstartRomPath))
    {
        args[index++] = "--kickstart-rom";
        args[index++] = options.KickstartRomPath;
    }

    if (!string.IsNullOrWhiteSpace(options.CpuBackend))
    {
        args[index++] = "--cpu";
        args[index++] = options.CpuBackend;
    }

    if (options.CopperQuiescenceFastPath)
    {
        args[index++] = "--copper-quiescence-fastpath";
    }

    if (options.CopperQuiescenceFastPathVerify)
    {
        args[index++] = "--copper-quiescence-fastpath-verify";
    }

    if (copperQuiescenceDiagnostics)
    {
        args[index++] = "--copper-quiescence-diagnostics";
    }

    if (options.DeferredCpuBusBatch)
    {
        args[index++] = "--cpu-deferred-bus-batch";
    }

    if (options.DeferredCpuBusBatchVerify)
    {
        args[index++] = "--cpu-deferred-bus-batch-verify";
    }

    if (options.CpuWaitSlotReference)
    {
        args[index++] = "--cpu-wait-slot-reference";
    }

    if (options.HardwareSpecialization)
    {
        args[index++] = "--hardware-specialization";
    }

    return args;
}

static string FormatKickstartOption(BenchmarkOptions options)
{
    if (!string.IsNullOrWhiteSpace(options.KickstartRomPath))
    {
        return options.KickstartRomPath;
    }

    return options.RealKickstart ? "real" : "profile";
}

static string FormatCpuBackendName(string backend, M68kOpcodePlanDispatch? opcodeDispatch)
    => opcodeDispatch.HasValue
        ? $"{backend}/{opcodeDispatch.Value}"
        : backend;

static string CaptureStatusText(CopperScreenEmulator emulator)
{
    var status = emulator.StatusText;
    if (!status.StartsWith("AMIGA_BOOT_", StringComparison.Ordinal))
    {
        return status;
    }

    var bootField = typeof(CopperScreenEmulator).GetField("_boot", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var boot = (AmigaBootController)bootField.GetValue(emulator)!;
    var details = boot.Diagnostics
        .Where(diagnostic => status.Contains(diagnostic.Code, StringComparison.Ordinal))
        .Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}");
    var detailText = string.Join(" | ", details);
    return string.IsNullOrEmpty(detailText) ? status : detailText;
}

static string? TryFindWorkspaceFile(params string[] parts)
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory != null)
    {
        var segments = new string[parts.Length + 1];
        segments[0] = directory.FullName;
        Array.Copy(parts, 0, segments, 1, parts.Length);
        var candidate = Path.Combine(segments);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        directory = directory.Parent;
    }

    return null;
}

static FrameProfile ProfileFrames(CopperScreenEmulator emulator, int frames)
{
    var type = typeof(CopperScreenEmulator);
    var machineField = type.GetField("_machine", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var bootField = type.GetField("_boot", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var targetCycleField = type.GetField("_targetCycle", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var executionBoundaryScheduleField = type.GetField("_executionBoundarySchedule", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var advancePendingDiskInsert = type.GetMethod("AdvancePendingDiskInsert", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var handleBootResult = type.GetMethod("HandleBootResult", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var stabilizeInterlaceFrame = type.GetMethod("StabilizeInterlaceFrame", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var machine = (Machine)machineField.GetValue(emulator)!;
    var boot = (AmigaBootController)bootField.GetValue(emulator)!;
    var executionBoundarySchedule = (IAmigaExecutionBoundarySchedule)executionBoundaryScheduleField.GetValue(emulator)!;
    var cpuTicks = 0L;
    var agnusTicks = 0L;
    var displayTicks = 0L;
    var otherTicks = 0L;
    var totalStart = Stopwatch.GetTimestamp();

    for (var frame = 0; frame < frames; frame++)
    {
        var phaseStart = Stopwatch.GetTimestamp();
        executionBoundarySchedule.BeginFrame();
        advancePendingDiskInsert.Invoke(emulator, null);
        var frameStartCycle = (long)targetCycleField.GetValue(emulator)!;
        var targetCycle = frameStartCycle + AmigaConstants.A500PalCpuCyclesPerFrame;
        targetCycleField.SetValue(emulator, targetCycle);
        machine.Bus.Display.BeginPresentationFrame(
            new PresentationFrameTarget(emulator.Framebuffer),
            frameStartCycle,
            targetCycle);
        executionBoundarySchedule.BeginExecution(frameStartCycle, targetCycle);

        otherTicks += Stopwatch.GetTimestamp() - phaseStart;

        phaseStart = Stopwatch.GetTimestamp();
        var result = boot.ContinueExecutionUntilCycle(
            targetCycle,
            maxInstructions: 100_000,
            executionBoundarySchedule);
        cpuTicks += Stopwatch.GetTimestamp() - phaseStart;

        phaseStart = Stopwatch.GetTimestamp();
        executionBoundarySchedule.CompleteExecution(targetCycle);
        var stopped = (bool)handleBootResult.Invoke(emulator, new object[] { result })!;
        otherTicks += Stopwatch.GetTimestamp() - phaseStart;
        if (stopped)
        {
            machine.Bus.Display.AbortPresentationFrame();
            executionBoundarySchedule.CompleteFrame();
            continue;
        }

        machine.Bus.AdvanceRasterTo(targetCycle);
        machine.Bus.AdvanceCiasTo(targetCycle);
        machine.Bus.SynchronizePaulaThrough(targetCycle);
        machine.Bus.SynchronizeDiskThrough(targetCycle);

        phaseStart = Stopwatch.GetTimestamp();
        if (machine.Bus.LiveAgnusDmaEnabled)
        {
            machine.Bus.SynchronizeLiveDisplayThrough(targetCycle);
        }

        agnusTicks += Stopwatch.GetTimestamp() - phaseStart;
        machine.Bus.AdvanceDmaTo(targetCycle);
        machine.Bus.SynchronizePaulaThrough(targetCycle);

        phaseStart = Stopwatch.GetTimestamp();
        machine.Bus.Display.CompletePresentationFrame(targetCycle);
        stabilizeInterlaceFrame.Invoke(emulator, null);
        displayTicks += Stopwatch.GetTimestamp() - phaseStart;

        phaseStart = Stopwatch.GetTimestamp();
        executionBoundarySchedule.CompleteFrame();
        otherTicks += Stopwatch.GetTimestamp() - phaseStart;
    }

    var totalTicks = Stopwatch.GetTimestamp() - totalStart;
    return new FrameProfile(
        Percent(cpuTicks, totalTicks),
        Percent(agnusTicks, totalTicks),
        Percent(displayTicks, totalTicks),
        MillisecondsPerFrame(cpuTicks, frames),
        MillisecondsPerFrame(agnusTicks, frames),
        MillisecondsPerFrame(displayTicks, frames),
        MillisecondsPerFrame(totalTicks, frames));
}

static BenchmarkWorkload[] FilterWorkloads(BenchmarkWorkload[] workloads, string? only)
{
    if (string.IsNullOrWhiteSpace(only))
    {
        return workloads;
    }

    var count = 0;
    for (var i = 0; i < workloads.Length; i++)
    {
        if (workloads[i].Name.Contains(only, StringComparison.OrdinalIgnoreCase))
        {
            count++;
        }
    }

    if (count == 0)
    {
        return workloads;
    }

    var result = new BenchmarkWorkload[count];
    var index = 0;
    for (var i = 0; i < workloads.Length; i++)
    {
        if (workloads[i].Name.Contains(only, StringComparison.OrdinalIgnoreCase))
        {
            result[index++] = workloads[i];
        }
    }

    return result;
}

static OcsDisplay GetDisplay(CopperScreenEmulator emulator)
{
    return GetMachine(emulator).Bus.Display;
}

static Machine GetMachine(CopperScreenEmulator emulator)
{
    return (Machine)typeof(CopperScreenEmulator)
        .GetField("_machine", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(emulator)!;
}

static AgnusBeamDmaSnapshot GetAgnusSnapshot(CopperScreenEmulator emulator)
{
    return GetMachine(emulator).Bus.Agnus.CaptureSnapshot();
}

static AmigaHardwareSchedulerSnapshot CaptureHardwareSchedulerSnapshot(CopperScreenEmulator emulator)
{
    return GetMachine(emulator).Bus.CaptureHardwareSchedulerSnapshot();
}

static DiskSummary CaptureDiskSummary(CopperScreenEmulator emulator)
{
    var machine = GetMachine(emulator);
    var disk = machine.Bus.Disk.CaptureSnapshot();
    return new DiskSummary(
        disk.TransferCount,
        disk.LastTransferWords,
        disk.LastTransferDrive,
        disk.LastTransferCylinder,
        disk.LastTransferHead,
        disk.LastTransferAddress,
        disk.SelectedDrive,
        disk.ActiveDma,
        disk.Dsklen,
        disk.Dskbytr,
        disk.SchedulerCounters.NextWakeCandidateQueries,
        disk.SchedulerCounters.NextEventWakeCandidateQueries,
        disk.SchedulerCounters.HasWakeCandidateThroughQueries,
        disk.SchedulerCounters.HasEventWakeCandidateThroughQueries,
        disk.SchedulerCounters.RefreshNextIndexPulseQueries,
        disk.SchedulerCounters.InputAdvanceCalls,
        disk.SchedulerCounters.SchedulerGateTrue,
        disk.SchedulerCounters.SchedulerGateFalse,
        disk.SchedulerCounters.PendingDmaWakeSources,
        disk.SchedulerCounters.ActiveDmaProgressWakeSources,
        disk.SchedulerCounters.ActiveDmaCompletionWakeSources,
        disk.SchedulerCounters.SyncCandidateWakeSources,
        disk.SchedulerCounters.IndexPulseWakeSources,
        disk.SchedulerCounters.PassiveByteReadyWakeSources);
}

static AmigaDiskTraceEvent[] CaptureDiskTrace(CopperScreenEmulator emulator)
{
    var machine = (Machine)typeof(CopperScreenEmulator)
        .GetField("_machine", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(emulator)!;
    return machine.Bus.Disk.CaptureDivergenceTrace();
}

static HardwareSpecializationSummary CaptureSpecializationSummary(CopperScreenEmulator emulator)
{
    var machine = (Machine)typeof(CopperScreenEmulator)
        .GetField("_machine", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(emulator)!;
    var blitterSnapshot = machine.Bus.Blitter.CaptureSnapshot();
    var blitter = blitterSnapshot.SpecializationCounters;
    var advance = blitterSnapshot.AdvanceCounters;
    var disk = machine.Bus.Disk.CaptureSnapshot().SpecializationCounters;
    return new HardwareSpecializationSummary(
        blitter.KernelHits,
        blitter.KernelMisses,
        blitter.GeneratedKernels,
        blitter.ScalarFallbacks,
        blitter.SlotQueueAttempts,
        blitter.SlotQueueEnabledBlits,
        blitter.SlotQueueUnsupportedBlits,
        blitter.SlotQueueWords,
        blitter.SlotQueueCommittedOps,
        blitter.SpecializedReservations,
        blitter.RowPipelineAttempts,
        blitter.RowPipelineUsed,
        blitter.RowPipelineWords,
        blitter.RowPipelineCompletions,
        blitter.DOnlyRowWords,
        blitter.AToDRowWords,
        blitter.RowPipelineFallbacks,
        advance.Calls,
        advance.IdleExits,
        advance.HorizonExits,
        advance.BoundedAttempts,
        advance.BoundedUses,
        advance.SlotsExamined,
        advance.MicroOpsCompleted,
        advance.WordsCompleted,
        advance.DeniedSlots,
        advance.DisplayPreparations,
        advance.PaulaSlots,
        advance.DiskSlots,
        advance.Barriers,
        advance.Fallbacks,
        advance.VerifyMatches,
        advance.VerifyMismatches,
        advance.FirstMismatch,
        blitterSnapshot.TopPatterns,
        disk.PreparedTrackHits,
        disk.PreparedTrackMisses,
        disk.RollingWindowHits,
        disk.RollingWindowMisses,
        disk.SyncIndexHits,
        disk.SyncIndexMisses,
        disk.ScalarBitReads,
        disk.FullTrackSyncScans,
        disk.FullTrackSyncScanBits,
        disk.FullTrackWordSyncScans,
        disk.FullTrackByteSyncScans,
        disk.ActiveDmaPredictionPasses,
        disk.ActiveDmaPredictedWordPlans,
        disk.ActiveDmaRuntimeWordPlans,
        disk.ActiveDmaRequestsCreated,
        disk.ActiveDmaRequestsServed,
        disk.ActiveDmaRequestsBlocked);
}

static FramebufferSummary CaptureFramebufferSummary(IReadOnlyList<int> framebuffer)
{
    const int Black = unchecked((int)0xFF000000);
    const int ColorLimit = 1024;
    var checksum = 2166136261u;
    var nonBlack = 0;
    var colors = new HashSet<int>();
    foreach (var pixel in framebuffer)
    {
        checksum = (checksum ^ unchecked((uint)pixel)) * 16777619u;
        if (pixel != Black)
        {
            nonBlack++;
        }

        if (colors.Count < ColorLimit)
        {
            colors.Add(pixel);
        }
    }

    return new FramebufferSummary(nonBlack, colors.Count, checksum);
}

static void WriteFramebufferBmp(string path, IReadOnlyList<int> framebuffer, int width, int height)
{
    if (framebuffer.Count < width * height)
    {
        throw new ArgumentException("Framebuffer is smaller than the requested BMP dimensions.", nameof(framebuffer));
    }

    var fullPath = Path.GetFullPath(path);
    var directory = Path.GetDirectoryName(fullPath);
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }

    const int fileHeaderSize = 14;
    const int dibHeaderSize = 40;
    var strideBytes = width * sizeof(uint);
    var imageBytes = strideBytes * height;
    using var stream = File.Create(fullPath);
    using var writer = new BinaryWriter(stream);
    writer.Write((byte)'B');
    writer.Write((byte)'M');
    writer.Write(fileHeaderSize + dibHeaderSize + imageBytes);
    writer.Write((ushort)0);
    writer.Write((ushort)0);
    writer.Write(fileHeaderSize + dibHeaderSize);
    writer.Write(dibHeaderSize);
    writer.Write(width);
    writer.Write(-height);
    writer.Write((ushort)1);
    writer.Write((ushort)32);
    writer.Write(0);
    writer.Write(imageBytes);
    writer.Write(2835);
    writer.Write(2835);
    writer.Write(0);
    writer.Write(0);

    for (var i = 0; i < width * height; i++)
    {
        writer.Write(unchecked((uint)framebuffer[i]));
    }
}

static void WriteChipRamDump(string path, CopperScreenEmulator emulator)
{
    var fullPath = Path.GetFullPath(path);
    var directory = Path.GetDirectoryName(fullPath);
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }

    var machine = GetMachine(emulator);
    File.WriteAllBytes(fullPath, machine.Bus.ChipRam);

    var display = emulator.DisplaySnapshot;
    var displayCounters = GetDisplay(emulator).CaptureSnapshot();
    File.WriteAllLines(
        Path.ChangeExtension(fullPath, ".txt"),
        [
            $"bplcon={display.Bplcon0:X4}/{display.Bplcon1:X4}/{display.Bplcon2:X4}",
            $"ddf={display.DdfStart:X4}/{display.DdfStop:X4}",
            $"diw={display.DiwStart:X4}/{display.DiwStop:X4}",
            $"mod={display.Bpl1Mod}/{display.Bpl2Mod}",
            $"ptr={FormatHexArray(display.BitplanePointers)}",
            $"base={FormatIntArray(display.BitplaneBaseRows)}",
            $"colors={FormatColorArray(display.Colors)}",
            $"timeline=segments:{displayCounters.LastTimelineSegmentCount},fallback:{displayCounters.LastTimelineFallbackCount},active:{displayCounters.LastActiveTimelineFrameCount},archived:{displayCounters.LastArchivedTimelineFrameCount},sprites:{displayCounters.LastTimelineSpriteCommandCount},fast:{displayCounters.LastTimelineFastPathRowCount}/{displayCounters.LastTimelineFastPathMissCount}",
            $"timelineReject=frameIncomplete:{displayCounters.LastArchiveRejectFrameIncomplete},timelineInvalid:{displayCounters.LastArchiveRejectTimelineInvalid},unsafe:{displayCounters.LastArchiveRejectUnsafeWrite}@{displayCounters.LastArchiveRejectUnsafeOffset:X4}/copper:{displayCounters.LastArchiveRejectUnsafeIsCopper},segments:{displayCounters.LastArchiveRejectSegmentCapacity},missingLine:{displayCounters.LastArchiveRejectMissingLine},unsafeLine:{displayCounters.LastArchiveRejectUnsafeLine},missingBpl:{displayCounters.LastArchiveRejectMissingBitplaneFetch},missingSpr:{displayCounters.LastArchiveRejectMissingSpriteFetch}"
        ]);
}

static string FormatColorArray(IReadOnlyList<ushort> values)
{
    return string.Join(
        '/',
        values.Select(value => ((int)value).ToString("X4", System.Globalization.CultureInfo.InvariantCulture)));
}

static AudioSummary CaptureAudioSummary(ReadOnlySpan<float> samples, int frames)
{
    var checksum = 2166136261u;
    var nonZero = 0;
    var peak = 0.0f;
    foreach (var sample in samples)
    {
        var bits = BitConverter.SingleToUInt32Bits(sample);
        checksum = (checksum ^ bits) * 16777619u;
        if (sample != 0.0f)
        {
            nonZero++;
            peak = Math.Max(peak, Math.Abs(sample));
        }
    }

    return new AudioSummary(frames, nonZero, peak, checksum);
}

static bool HasActiveAudio(ReadOnlySpan<float> samples)
{
    foreach (var sample in samples)
    {
        if (sample != 0.0f)
        {
            return true;
        }
    }

    return false;
}

static StreamWriter? CreateAudioAuditWriter(BenchmarkOptions options, BenchmarkWorkload workload)
{
    if (string.IsNullOrWhiteSpace(options.AudioAuditPath))
    {
        return null;
    }

    var fullPath = Path.GetFullPath(options.AudioAuditPath);
    var directory = Path.GetDirectoryName(fullPath);
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }

    var writer = File.CreateText(fullPath);
    writer.AutoFlush = true;
    writer.WriteLine("# workload\t" + SanitizeTsv(workload.Name));
    writer.WriteLine("# created\t" + DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));
    writer.WriteLine("# path\t" + fullPath);
    writer.WriteLine(string.Join(
        '\t',
        "absolute_frame",
        "measured_frame",
        "frame_ms",
        "scheduler_drains",
        "queue_before_ms",
        "queue_after_drain_ms",
        "queue_after_submit_ms",
        "underrun",
        "audio_frames",
        "active_audio",
        "audio_nonzero_samples",
        "audio_peak",
        "audio_checksum",
        "cpu_cycles",
        "pc",
        "last_pc",
        "sr",
        "drive0",
        "display",
        "disk",
        "disk_delta",
        "diagnostics",
        "status"));
    return writer;
}

static void ValidateExpectedChecksums(BenchmarkRunResult result, BenchmarkOptions options)
{
    if (result.Missing)
    {
        return;
    }

    if (options.ExpectedFramebufferChecksum is uint expectedFramebuffer &&
        result.Framebuffer.Checksum != expectedFramebuffer)
    {
        throw new InvalidOperationException(
            $"Framebuffer checksum mismatch for {result.Workload.Name}: expected 0x{expectedFramebuffer:X8}, actual 0x{result.Framebuffer.Checksum:X8}.");
    }

    if (options.ExpectedAudioChecksum is uint expectedAudio &&
        result.Audio.Checksum != expectedAudio)
    {
        throw new InvalidOperationException(
            $"Audio checksum mismatch for {result.Workload.Name}: expected 0x{expectedAudio:X8}, actual 0x{result.Audio.Checksum:X8}.");
    }
}

static string FormatPaulaAudit(Paula paula)
{
    var parts = new string[4];
    for (var channel = 0; channel < parts.Length; channel++)
    {
        var snapshot = paula.GetChannelSnapshot(channel);
        parts[channel] = string.Create(
            CultureInfo.InvariantCulture,
            $"ch{channel}:loc={snapshot.Location:X6},cur={snapshot.CurrentAddress:X6},len={snapshot.LengthWords},rem={snapshot.RemainingWords},per={snapshot.Period},vol={snapshot.Volume},sample={snapshot.CurrentSample},dma={(snapshot.DmaEnabled ? 1 : 0)},enable={snapshot.LastDmaEnableCycle},word={snapshot.DataWord:X4},has={(snapshot.HasDataWord ? 1 : 0)},low={(snapshot.NextByteIsLow ? 1 : 0)},next={snapshot.NextSampleCycle}");
    }

    return string.Join(';', parts);
}

static void RunSyntheticBlitterBenchmarks(BenchmarkOptions options)
{
    Console.WriteLine(
        $"Synthetic ROM/expansion blitter bench, warmup={options.OpcodeDispatchWarmupInstructions}, measured={options.OpcodeDispatchInstructions}, repeats={options.RepeatCount}, deferred={options.DeferredCpuBusBatch}, verify={options.DeferredCpuBusBatchVerify}, Release={IsRelease()}");
    Console.WriteLine("synthetic-blitter\tsource\trepeat\tinstructions\tms\tinstr/sec\tcycles\tpc\td0\tallocated bytes\tbatch used\twaitfast used\tblitter overlap\tshadow mismatch\tchecksum");
    foreach (var executeFromExpansion in new[] { false, true })
    {
        for (var repeat = 0; repeat < options.RepeatCount; repeat++)
        {
            using (var warmup = CreateSyntheticBlitterCpu(executeFromExpansion, options))
            {
                _ = warmup.Cpu.ExecuteInstructions(
                    options.OpcodeDispatchWarmupInstructions,
                    null,
                    SyntheticBenchmarkBoundary.Instance);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            using var run = CreateSyntheticBlitterCpu(executeFromExpansion, options);
            var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            var executed = run.Cpu.ExecuteInstructions(
                options.OpcodeDispatchInstructions,
                null,
                SyntheticBenchmarkBoundary.Instance);
            var elapsed = Stopwatch.GetElapsedTime(start);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
            var scheduler = run.Bus.CaptureHardwareSchedulerSnapshot();
            var checksum = CreateSyntheticBlitterChecksum(run.Cpu.State, run.Bus);
            Console.WriteLine(
                $"synthetic-blitter\t{(executeFromExpansion ? "expansion" : "rom")}\t{repeat + 1}\t{executed}\t{elapsed.TotalMilliseconds:F3}\t{executed / Math.Max(elapsed.TotalSeconds, double.Epsilon):F0}\t{run.Cpu.State.Cycles}\t0x{run.Cpu.State.ProgramCounter:X8}\t0x{run.Cpu.State.D[0]:X8}\t{allocated}\t{scheduler.DeferredCpuBusBatchUsed}\t{scheduler.DeferredCpuWaitWindowFastPathUsed}\t{scheduler.DeferredCpuWaitBlitterOverlapAttempts}\t{scheduler.DeferredCpuWaitSlotShadowMismatches}\t0x{checksum:X8}");
            if (scheduler.DeferredCpuWaitSlotShadowMismatches != 0)
            {
                Console.WriteLine($"synthetic-blitter-mismatch\t{(executeFromExpansion ? "expansion" : "rom")}\t{scheduler.DeferredCpuWaitSlotShadowFirstMismatch}");
            }
        }
    }
}

static SyntheticBlitterBenchmarkRun CreateSyntheticBlitterCpu(
    bool executeFromExpansion,
    BenchmarkOptions options)
{
    const uint romBase = 0x00FC0000;
    const int expansionOffset = 0x8000;
    var program = CreateSyntheticBlitterBenchmarkProgram();
    var bus = new AmigaBus(
        expansionRamSize: AmigaConstants.A500BootPseudoFastRamSize,
        captureBusAccesses: false,
        enableDeferredCpuBusBatch: options.DeferredCpuBusBatch,
        verifyDeferredCpuBusBatch: options.DeferredCpuBusBatchVerify);
    var programAddress = executeFromExpansion
        ? bus.ExpansionRamBase + expansionOffset
        : romBase;
    if (executeFromExpansion)
    {
        program.AsSpan().CopyTo(bus.ExpansionRam.AsSpan(expansionOffset));
    }
    else
    {
        bus.MapReadOnlyMemory(programAddress, program);
    }

    for (var offset = 0; offset < 0x2000; offset += 2)
    {
        WriteWords(bus.ChipRam, offset, [(ushort)(0x4000 + offset)]);
    }

    bus.WriteWord(0x00DFF040, 0x09F0, 0);
    bus.WriteWord(0x00DFF042, 0x0000, 0);
    bus.WriteWord(0x00DFF050, 0x0000, 0);
    bus.WriteWord(0x00DFF052, 0x3000, 0);
    bus.WriteWord(0x00DFF054, 0x0000, 0);
    bus.WriteWord(0x00DFF056, 0x4000, 0);
    bus.WriteWord(0x00DFF096, 0x8240, 0);

    var cpu = (IM68kBatchCore)M68kCoreFactory.CreateM68000Core(
        bus,
        default(AmigaCpuDataAccess),
        enableCpuBusPhaseTrace: false);
    cpu.Reset(programAddress, 0x3000);
    cpu.State.A[0] = 0x1000;
    return new SyntheticBlitterBenchmarkRun(cpu, bus);
}

static byte[] CreateSyntheticBlitterBenchmarkProgram()
{
    var program = new byte[42];
    WriteWords(program, 0,
    [
        0x33FC, 0x0044, 0x00DF, 0xF058,
        0x3010, 0x3010, 0x3010, 0x3010,
        0x3010, 0x3010, 0x3010, 0x3010,
        0x3010, 0x3010, 0x3010, 0x3010,
        0x3010, 0x3010, 0x3010, 0x3010,
        0x60D6
    ]);
    return program;
}

static uint CreateSyntheticBlitterChecksum(M68kCpuState state, AmigaBus bus)
{
    var checksum = state.ProgramCounter ^ (uint)state.Cycles ^ state.D[0];
    checksum = unchecked((checksum * 16777619u) ^ state.A[0]);
    checksum = unchecked((checksum * 16777619u) ^ bus.ReadWord(0x4000));
    return checksum;
}

static SlotScheduleAuditWriter? CreateSlotScheduleAuditWriter(BenchmarkOptions options, BenchmarkWorkload workload)
{
    if (string.IsNullOrWhiteSpace(options.SlotScheduleAuditPath))
    {
        return null;
    }

    return new SlotScheduleAuditWriter(
        options.SlotScheduleAuditPath,
        workload.Name,
        options.SlotScheduleAuditLimit);
}

static void WriteAudioAuditFrame(
    StreamWriter? writer,
    CopperScreenEmulator emulator,
    int absoluteFrame,
    int measuredFrame,
    double frameMilliseconds,
    long schedulerDrains,
    double queueBeforeDrainMilliseconds,
    double queueAfterDrainMilliseconds,
    double queueAfterSubmitMilliseconds,
    bool underrun,
    int audioFrames,
    bool activeAudio,
    AudioSummary audioSummary,
    DiskSummary diskSummary,
    string diskSchedulerDelta,
    string diagnostics)
{
    if (writer == null)
    {
        return;
    }

    var cpu = emulator.CpuState;
    var drives = emulator.CaptureDriveStates();
    writer.WriteLine(string.Join(
        '\t',
        absoluteFrame.ToString(CultureInfo.InvariantCulture),
        measuredFrame.ToString(CultureInfo.InvariantCulture),
        frameMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
        schedulerDrains.ToString(CultureInfo.InvariantCulture),
        queueBeforeDrainMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
        queueAfterDrainMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
        queueAfterSubmitMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
        underrun ? "1" : "0",
        audioFrames.ToString(CultureInfo.InvariantCulture),
        activeAudio ? "1" : "0",
        audioSummary.NonZeroSamples.ToString(CultureInfo.InvariantCulture),
        audioSummary.Peak.ToString("F6", CultureInfo.InvariantCulture),
        $"0x{audioSummary.Checksum:X8}",
        GetMachine(emulator).Cpu.State.Cycles.ToString(CultureInfo.InvariantCulture),
        $"0x{cpu.ProgramCounter:X6}",
        $"0x{cpu.LastInstructionProgramCounter:X6}",
        $"0x{cpu.StatusRegister:X4}",
        drives.Length > 0 ? SanitizeTsv(CopperScreenDebugSnapshot.FormatDrive(drives[0])) : string.Empty,
        SanitizeTsv(FormatDisplaySummary(CaptureDisplaySummary(GetDisplay(emulator)))),
        SanitizeTsv(FormatDiskSummary(diskSummary)),
        SanitizeTsv(diskSchedulerDelta),
        SanitizeTsv(diagnostics),
        SanitizeTsv(CaptureStatusText(emulator))));
}

static void WriteAudioAuditDebugSnapshot(
    StreamWriter? writer,
    BenchmarkOptions options,
    CopperScreenEmulator emulator,
    int absoluteFrame,
    int measuredFrame)
{
    if (emulator.DebugSnapshot == null)
    {
        return;
    }

    var reportPath = WriteAudioAuditSnapshotReport(
        options,
        emulator.DebugSnapshot,
        "debug-snapshot",
        absoluteFrame,
        measuredFrame);
    writer?.WriteLine("# debug-snapshot\t" + absoluteFrame.ToString(CultureInfo.InvariantCulture) + "\t" + SanitizeTsv(reportPath));
}

static void WriteAudioAuditCrash(
    StreamWriter? writer,
    BenchmarkOptions options,
    CopperScreenEmulator emulator,
    int absoluteFrame,
    int measuredFrame,
    Exception exception)
{
    var snapshot = emulator.CaptureDebugSnapshot(
        "BENCHMARK_EXCEPTION",
        exception.GetType().FullName + ": " + exception.Message,
        exception.ToString());
    var reportPath = WriteAudioAuditSnapshotReport(options, snapshot, "exception", absoluteFrame, measuredFrame);
    writer?.WriteLine("# exception\t" + absoluteFrame.ToString(CultureInfo.InvariantCulture) + "\t" + SanitizeTsv(reportPath));
}

static string WriteAudioAuditSnapshotReport(
    BenchmarkOptions options,
    CopperScreenDebugSnapshot snapshot,
    string suffix,
    int absoluteFrame,
    int measuredFrame)
{
    var basePath = string.IsNullOrWhiteSpace(options.AudioAuditPath)
        ? Path.Combine("artifacts", "benchmark-audit.tsv")
        : options.AudioAuditPath;
    var fullAuditPath = Path.GetFullPath(basePath);
    var directory = Path.GetDirectoryName(fullAuditPath) ?? Directory.GetCurrentDirectory();
    Directory.CreateDirectory(directory);
    var stem = Path.GetFileNameWithoutExtension(fullAuditPath);
    var reportPath = Path.Combine(directory, $"{stem}.frame-{absoluteFrame}.{suffix}.txt");
    File.WriteAllText(
        reportPath,
        $"absolute_frame={absoluteFrame.ToString(CultureInfo.InvariantCulture)}{Environment.NewLine}" +
        $"measured_frame={measuredFrame.ToString(CultureInfo.InvariantCulture)}{Environment.NewLine}" +
        snapshot.ToReport());
    return reportPath;
}

static void WriteDebugSnapshotDump(
    string path,
    CopperScreenEmulator emulator,
    int absoluteFrame,
    string reasonCode,
    string message)
{
    var fullPath = Path.GetFullPath(path);
    var directory = Path.GetDirectoryName(fullPath);
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }

    var snapshot = emulator.CaptureDebugSnapshot(reasonCode, message);
    File.WriteAllText(
        fullPath,
        $"absolute_frame={absoluteFrame.ToString(CultureInfo.InvariantCulture)}{Environment.NewLine}" +
        snapshot.ToReport());
}

static string SanitizeTsv(string? value)
    => string.IsNullOrEmpty(value)
        ? string.Empty
        : value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

static DisplaySummary CaptureDisplaySummary(OcsDisplay display)
{
    var snapshot = display.CaptureSnapshot();
    return new DisplaySummary(
        snapshot.Bplcon0,
        snapshot.Bplcon1,
        snapshot.Bplcon2,
        snapshot.LastBitplaneNonZeroPixels,
        snapshot.LastBitplaneMinX,
        snapshot.LastBitplaneMinY,
        snapshot.LastBitplaneMaxX,
        snapshot.LastBitplaneMaxY,
        snapshot.LastSpriteNonZeroPixels,
        snapshot.LastSpriteMinX,
        snapshot.LastSpriteMinY,
        snapshot.LastSpriteMaxX,
        snapshot.LastSpriteMaxY,
        snapshot.LastBitplaneDmaFetches,
        snapshot.LastSpriteDmaFetches,
        snapshot.LastMissedSpriteDmaSlots,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        snapshot.LastRowDmaPlansBuilt,
        snapshot.LastRowDmaPlannedRowsExecuted,
        snapshot.LastRowDmaBitplaneEntriesExecuted,
        snapshot.LastRowDmaSpriteEntriesExecuted,
        snapshot.LastRowDmaScalarFallbackRows,
        snapshot.LastRowDmaPlanInvalidationRows,
        snapshot.LastRowDmaPlanMismatchRows,
        snapshot.CopperQuiescentWindowCount,
        snapshot.CopperQuiescentTotalCycles,
        snapshot.CopperQuiescentMaxCycles,
        display.BitplaneDataSpanCount);
}

static string FormatFramebufferSummary(FramebufferSummary summary)
    => $"{summary.NonBlackPixels}/{summary.DistinctColors}/0x{summary.Checksum:X8}";

static string FormatAudioSummary(AudioSummary summary)
    => $"{summary.Frames}/{summary.NonZeroSamples}/{summary.Peak:F4}/0x{summary.Checksum:X8}";

static string FormatStatusWithScheduler(BenchmarkRunResult result)
{
    var scheduler = result.Scheduler;
    var boundaryMode = result.CopperStartRuntimeHandoffActive ? "runtime" : "boot";
    return $"{FormatCounterText(result.StatusText)} | boundary={boundaryMode}/{result.CopperStartRuntimeHandoffCount}, scheduler last={scheduler.LastDrainCycle}, drains={scheduler.DrainCount}, max-frame-drains={result.MaxFrameSchedulerDrains}, bus-drains={scheduler.BusAccessDrainCount}, same-cycle={scheduler.SameCycleDrainCount}, line-cache=hit:{scheduler.RasterlineCacheHits},miss:{scheduler.RasterlineCacheMisses},rebuild:{scheduler.RasterlineCacheRebuilds},inv:{scheduler.RasterlineCacheInvalidations}, wake-agenda=hit:{scheduler.WakeAgendaCacheHits},miss:{scheduler.WakeAgendaCacheMisses},skip:{scheduler.WakeAgendaDrainSkips},inv:{scheduler.WakeAgendaInvalidations}, agnexec=agenda:{scheduler.AgnusExecutorAgendaReads}/{scheduler.AgnusExecutorAgendaUpdates},shadow:{scheduler.AgnusExecutorShadowMatches}/{scheduler.AgnusExecutorShadowMismatches},first:{FormatCounterText(scheduler.AgnusExecutorFirstShadowMismatch)},fixed:{scheduler.AgnusFixedPlanShadowMatches}/{scheduler.AgnusFixedPlanShadowMismatches},fixedFirst:{FormatCounterText(scheduler.AgnusFixedPlanFirstShadowMismatch)}, cpuevent=hit:{scheduler.CpuVisibleNoEventCacheHits},miss:{scheduler.CpuVisibleNoEventCacheMisses},inv:{scheduler.CpuVisibleNoEventCacheInvalidations}, copperq=slot:{scheduler.CopperQuiescentSlotContendedAccesses},customw:{scheduler.CopperQuiescentCustomRegisterWrites},cpuw:{scheduler.CopperQuiescentCpuScheduleAffectingCustomWrites}/{scheduler.CopperQuiescentCpuBenignCustomWrites},copw:{scheduler.CopperQuiescentCopperScheduleAffectingCustomMoves}/{scheduler.CopperQuiescentCopperBenignCustomMoves},drain:{scheduler.CopperQuiescentSchedulerDrains},pred:{scheduler.CopperQuiescentShadowPredictions}/{scheduler.CopperQuiescentShadowMatches}/{scheduler.CopperQuiescentShadowUnsupported}/{scheduler.CopperQuiescentShadowMismatches},fast:{scheduler.CopperQuiescentFastPathAttempts}/{scheduler.CopperQuiescentFastPathUsed}/{scheduler.CopperQuiescentFastPathSkippedDrains}/{scheduler.CopperQuiescentFastPathRejectedUnsupported}/{scheduler.CopperQuiescentFastPathRejectedInvalidated}/{scheduler.CopperQuiescentFastPathRejectedDynamicDma}/{scheduler.CopperQuiescentFastPathVerificationMismatches}, cpubatch={FormatDeferredCpuBusBatchSummary(scheduler)}, events=raster:{scheduler.RasterEvents},cia:{scheduler.CiaEvents},paula:{scheduler.PaulaEvents},disk:{scheduler.DiskEvents},agnus:{scheduler.AgnusEvents},blitter:{scheduler.BlitterEvents}";
}

static void LaunchWorkbenchPathIfNeeded(CopperScreenEmulator emulator, BenchmarkWorkload workload)
{
    if (string.IsNullOrWhiteSpace(workload.LaunchPath))
    {
        return;
    }

    foreach (var path in workload.LaunchPath.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (emulator.LaunchCopperBenchPath(path, out var message))
        {
            Console.WriteLine($"launch\t{workload.Name}\t{path}\t{message}");
            return;
        }

        Console.WriteLine($"launch-candidate\t{workload.Name}\t{path}\t{message}");
    }

    Console.WriteLine($"launch\t{workload.Name}\tfailed\t{emulator.StatusText}");
    WriteLaunchPathProbe(emulator, workload);
}

static void WriteLaunchPathProbe(CopperScreenEmulator emulator, BenchmarkWorkload workload)
{
    if (string.IsNullOrWhiteSpace(emulator.DiskPath))
    {
        return;
    }

    try
    {
        var disk = CopperScreenDiskImageArchive.LoadDiskImage(emulator.DiskPath);
        var fileSystem = new AmigaDosFileSystem(disk);
        WriteLaunchPathProbeDirectory(fileSystem, workload.Name, string.Empty, depth: 0);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AmigaEmulationException or ArgumentException or InvalidDataException)
    {
        Console.WriteLine($"launch-probe\t{workload.Name}\tfailed\t{ex.Message}");
    }
}

static void WriteLaunchPathProbeDirectory(AmigaDosFileSystem fileSystem, string workloadName, string path, int depth)
{
    if (depth > 2)
    {
        return;
    }

    foreach (var entry in fileSystem.ListDirectory(path))
    {
        var entryPath = AmigaDosFileSystem.CombinePath(path, entry.Name);
        Console.WriteLine($"launch-probe\t{workloadName}\t{entryPath}\t{(entry.IsDirectory ? "dir" : "file")}\t{entry.Size}");
        if (entry.IsDirectory)
        {
            WriteLaunchPathProbeDirectory(fileSystem, workloadName, entryPath, depth + 1);
        }
    }
}

static string FormatDeferredCpuBusBatchSummary(AmigaHardwareSchedulerSnapshot scheduler)
{
    var batch = $"{scheduler.DeferredCpuBusBatchAttempts}/{scheduler.DeferredCpuBusBatchUsed}/{scheduler.DeferredCpuBusBatchInstructions}/{scheduler.DeferredCpuBusBatchSkippedInstructionFlushes}/{scheduler.DeferredCpuBusBatchFlushes}";
    var exits = $"exit={scheduler.DeferredCpuBusBatchExitTargetCycle}/{scheduler.DeferredCpuBusBatchExitMaxInstructions}/{scheduler.DeferredCpuBusBatchExitChipVisibleAccess}/{scheduler.DeferredCpuBusBatchExitPcLeftFastWindow}/{scheduler.DeferredCpuBusBatchExitException}/{scheduler.DeferredCpuBusBatchExitUnsupported}";
    var wakes = $"wake={scheduler.DeferredCpuBusBatchWakeTargetCycle}/{scheduler.DeferredCpuBusBatchWakePendingInterrupt}/{scheduler.DeferredCpuBusBatchWakeVerticalBlank}/{scheduler.DeferredCpuBusBatchWakeHorizontalSyncTod}/{scheduler.DeferredCpuBusBatchWakeCiaTimer}/{scheduler.DeferredCpuBusBatchWakeDisk}/{scheduler.DeferredCpuBusBatchWakePaula}/{scheduler.DeferredCpuBusBatchWakeCopper}/{scheduler.DeferredCpuBusBatchWakeBlitter}";
    var diskWakes = $"diskwake={scheduler.DeferredCpuBusBatchDiskWakePendingDma}/{scheduler.DeferredCpuBusBatchDiskWakeActiveDmaProgress}/{scheduler.DeferredCpuBusBatchDiskWakeActiveDmaCompletion}/{scheduler.DeferredCpuBusBatchDiskWakeSyncCandidate}/{scheduler.DeferredCpuBusBatchDiskWakeIndexPulse}/{scheduler.DeferredCpuBusBatchDiskWakePassiveByteReady}/{scheduler.DeferredCpuBusBatchDiskWakeUnknown}";
    var internalWindow = $"internal={scheduler.DeferredCpuInternalNoBusWindowAttempts}/{scheduler.DeferredCpuInternalNoBusWindowUsed}/{scheduler.DeferredCpuInternalNoBusWindowTotalCycles}/{scheduler.DeferredCpuInternalNoBusWindowAdvancedCycles},op={scheduler.DeferredCpuInternalNoBusWindowMultiply}/{scheduler.DeferredCpuInternalNoBusWindowDivide},iwake={scheduler.DeferredCpuInternalNoBusWindowWakeTargetCycle}/{scheduler.DeferredCpuInternalNoBusWindowWakePendingInterrupt}/{scheduler.DeferredCpuInternalNoBusWindowWakeVerticalBlank}/{scheduler.DeferredCpuInternalNoBusWindowWakeHorizontalSyncTod}/{scheduler.DeferredCpuInternalNoBusWindowWakeCiaTimer}/{scheduler.DeferredCpuInternalNoBusWindowWakeDisk}/{scheduler.DeferredCpuInternalNoBusWindowWakePaula}/{scheduler.DeferredCpuInternalNoBusWindowWakeCopper}/{scheduler.DeferredCpuInternalNoBusWindowWakeBlitter},iverify={scheduler.DeferredCpuInternalNoBusWindowVerificationMismatches}";
    var waitWindow = $"waitwin={scheduler.DeferredCpuWaitWindowAttempts}/{scheduler.DeferredCpuWaitWindowEligible}/{scheduler.DeferredCpuWaitWindowTotalCycles}/{scheduler.DeferredCpuWaitWindowMaxCycles},kind={scheduler.DeferredCpuWaitWindowInstructionFetch}/{scheduler.DeferredCpuWaitWindowDataRead}/{scheduler.DeferredCpuWaitWindowDataWrite}/{scheduler.DeferredCpuWaitWindowCustom},target={scheduler.DeferredCpuWaitWindowChipRam}/{scheduler.DeferredCpuWaitWindowExpansionRam}/{scheduler.DeferredCpuWaitWindowRealTimeClock}/{scheduler.DeferredCpuWaitWindowCustomRegisters},size={scheduler.DeferredCpuWaitWindowByte}/{scheduler.DeferredCpuWaitWindowWord}/{scheduler.DeferredCpuWaitWindowLong},rw={scheduler.DeferredCpuWaitWindowRead}/{scheduler.DeferredCpuWaitWindowWrite},slots={scheduler.DeferredCpuWaitWindowSingleSlot}/{scheduler.DeferredCpuWaitWindowLongSlot}";
    var waitFast = $"waitfast={scheduler.DeferredCpuWaitWindowFastPathAttempts}/{scheduler.DeferredCpuWaitWindowFastPathUsed}/{scheduler.DeferredCpuWaitWindowFastPathRejectedUnsupported}/{scheduler.DeferredCpuWaitWindowFastPathRejectedDynamicDma}/{scheduler.DeferredCpuWaitWindowFastPathRejectedUnstable}/{scheduler.DeferredCpuWaitWindowFastPathAdvancedCycles}/{scheduler.DeferredCpuWaitWindowFastPathMaxAdvancedCycles}";
    var slotShadow = $"slotshadow={scheduler.DeferredCpuWaitSlotShadowAttempts}/{scheduler.DeferredCpuWaitSlotShadowMatches}/{scheduler.DeferredCpuWaitSlotShadowMismatches}/{scheduler.DeferredCpuWaitSlotShadowUnsupported},slotreason={scheduler.DeferredCpuWaitSlotShadowGrantMismatches}/{scheduler.DeferredCpuWaitSlotShadowCompletionMismatches}/{scheduler.DeferredCpuWaitSlotShadowSlotOwnerMismatches}/{scheduler.DeferredCpuWaitSlotShadowBlitterStateMismatches}/{scheduler.DeferredCpuWaitSlotShadowPaulaMismatches}/{scheduler.DeferredCpuWaitSlotShadowDiskMismatches}/{scheduler.DeferredCpuWaitSlotShadowDisplayMismatches}/{scheduler.DeferredCpuWaitSlotShadowCopperMismatches},slotlive={scheduler.DeferredCpuWaitSlotShadowLiveAttempts}/{scheduler.DeferredCpuWaitSlotShadowLiveSupported}/{scheduler.DeferredCpuWaitSlotShadowLiveUnsupported}/{scheduler.DeferredCpuWaitSlotShadowLiveLongAccesses},slotliveunsup={scheduler.DeferredCpuWaitSlotShadowLiveUnsupportedPendingWrite}/{scheduler.DeferredCpuWaitSlotShadowLiveUnsupportedBitplaneWindow}/{scheduler.DeferredCpuWaitSlotShadowLiveUnsupportedCopperWaitWindow}/{scheduler.DeferredCpuWaitSlotShadowLiveUnsupportedRasterlinePlan}/{scheduler.DeferredCpuWaitSlotShadowLiveUnsupportedCpuPredict}/{scheduler.DeferredCpuWaitSlotShadowLiveUnsupportedUnstable}/{scheduler.DeferredCpuWaitSlotShadowLiveUnsupportedScratchWrite}/{scheduler.DeferredCpuWaitSlotShadowLiveUnsupportedLongWrite}/{scheduler.DeferredCpuWaitSlotShadowLiveUnsupportedOther},slotdma={scheduler.DeferredCpuWaitSlotShadowLiveBitplaneFetches}/{scheduler.DeferredCpuWaitSlotShadowLiveSpriteFetches}/{scheduler.DeferredCpuWaitSlotShadowLiveCopperSteps},slotblt={scheduler.DeferredCpuWaitSlotShadowBlitterScratchAttempts}/{scheduler.DeferredCpuWaitSlotShadowBlitterScratchSupported}/{scheduler.DeferredCpuWaitSlotShadowBlitterScratchUnsupported}/{scheduler.DeferredCpuWaitSlotShadowBlitterScratchMatches}/{scheduler.DeferredCpuWaitSlotShadowBlitterScratchMismatches}/{scheduler.DeferredCpuWaitSlotShadowBlitterScratchPartial}/{scheduler.DeferredCpuWaitSlotShadowBlitterScratchMicroOps},slotfirst={scheduler.DeferredCpuWaitSlotShadowFirstMismatch}";
    var fixedImage = $"fixedimg={scheduler.DeferredCpuWaitFixedImageAttempts}/{scheduler.DeferredCpuWaitFixedImageSupported}/{scheduler.DeferredCpuWaitFixedImageMatches}/{scheduler.DeferredCpuWaitFixedImageMismatches}/{scheduler.DeferredCpuWaitFixedImageUnsupported},cache={scheduler.DeferredCpuWaitFixedImageBuilds}/{scheduler.DeferredCpuWaitFixedImageHits}/{scheduler.DeferredCpuWaitFixedImageMisses}/{scheduler.DeferredCpuWaitFixedImageInvalidations},slots={scheduler.DeferredCpuWaitFixedImagePredictedSlots},unsup={scheduler.DeferredCpuWaitFixedImageUnsupportedFrame}/{scheduler.DeferredCpuWaitFixedImageUnsupportedCopper}/{scheduler.DeferredCpuWaitFixedImageUnsupportedPendingWrite}/{scheduler.DeferredCpuWaitFixedImageUnsupportedRasterlinePlan}/{scheduler.DeferredCpuWaitFixedImageUnsupportedSpriteState},first={scheduler.DeferredCpuWaitFixedImageFirstMismatch}";
    var fixedProduction = $"fixedprod={scheduler.DeferredCpuWaitFixedImageProductionAttempts}/{scheduler.DeferredCpuWaitFixedImageProductionUsed}/{scheduler.DeferredCpuWaitFixedImageProductionPreGrantDrainsSkipped}/{scheduler.DeferredCpuWaitFixedImageProductionPostGrantCatchups}/{scheduler.DeferredCpuWaitFixedImageProductionPredictedWaitCycles},fallback={scheduler.DeferredCpuWaitFixedImageProductionFallbackUnsupported}/{scheduler.DeferredCpuWaitFixedImageProductionFallbackDynamicDma}/{scheduler.DeferredCpuWaitFixedImageProductionFallbackFrame}/{scheduler.DeferredCpuWaitFixedImageProductionFallbackCopper}/{scheduler.DeferredCpuWaitFixedImageProductionFallbackPendingWrite}/{scheduler.DeferredCpuWaitFixedImageProductionFallbackRasterlinePlan}/{scheduler.DeferredCpuWaitFixedImageProductionFallbackSpriteState}/{scheduler.DeferredCpuWaitFixedImageProductionFallbackUnstable},verify={scheduler.DeferredCpuWaitFixedImageProductionVerificationMatches}/{scheduler.DeferredCpuWaitFixedImageProductionVerificationMismatches},disabled={(scheduler.DeferredCpuWaitFixedImageProductionDisabled ? 1 : 0)},first={scheduler.DeferredCpuWaitFixedImageProductionFirstMismatch}";
    var blitterOverlap = $"bltoverlap={scheduler.DeferredCpuWaitBlitterOverlapAttempts}/{scheduler.DeferredCpuWaitBlitterOverlapSupported}/{scheduler.DeferredCpuWaitBlitterOverlapUnsupported}/{scheduler.DeferredCpuWaitBlitterOverlapNasty}";
    return $"{batch},{exits},{wakes},{diskWakes},verify={scheduler.DeferredCpuBusBatchVerificationMismatches},{internalWindow},{waitWindow},{waitFast},{slotShadow},{fixedImage},{fixedProduction},{blitterOverlap}";
}

static string FormatHardwareProfile(AmigaHardwareSchedulerSnapshot scheduler)
{
    var copperQuiescence = $"copperq:slot={scheduler.CopperQuiescentSlotContendedAccesses},customw={scheduler.CopperQuiescentCustomRegisterWrites},cpuw={scheduler.CopperQuiescentCpuScheduleAffectingCustomWrites}/{scheduler.CopperQuiescentCpuBenignCustomWrites},copw={scheduler.CopperQuiescentCopperScheduleAffectingCustomMoves}/{scheduler.CopperQuiescentCopperBenignCustomMoves},drain={scheduler.CopperQuiescentSchedulerDrains},pred={scheduler.CopperQuiescentShadowPredictions}/{scheduler.CopperQuiescentShadowMatches}/{scheduler.CopperQuiescentShadowUnsupported}/{scheduler.CopperQuiescentShadowMismatches},fast={scheduler.CopperQuiescentFastPathAttempts}/{scheduler.CopperQuiescentFastPathUsed}/{scheduler.CopperQuiescentFastPathSkippedDrains}/{scheduler.CopperQuiescentFastPathRejectedUnsupported}/{scheduler.CopperQuiescentFastPathRejectedInvalidated}/{scheduler.CopperQuiescentFastPathRejectedDynamicDma}/{scheduler.CopperQuiescentFastPathVerificationMismatches}";
    var deferredCpuBatch = $"cpubatch:{FormatDeferredCpuBusBatchSummary(scheduler)}";
    var hasCopperQuiescence =
        scheduler.CopperQuiescentSlotContendedAccesses != 0 ||
        scheduler.CopperQuiescentCustomRegisterWrites != 0 ||
        scheduler.CopperQuiescentCpuScheduleAffectingCustomWrites != 0 ||
        scheduler.CopperQuiescentCpuBenignCustomWrites != 0 ||
        scheduler.CopperQuiescentCopperScheduleAffectingCustomMoves != 0 ||
        scheduler.CopperQuiescentCopperBenignCustomMoves != 0 ||
        scheduler.CopperQuiescentSchedulerDrains != 0 ||
        scheduler.CopperQuiescentShadowPredictions != 0 ||
        scheduler.CopperQuiescentShadowUnsupported != 0 ||
        scheduler.CopperQuiescentShadowMismatches != 0 ||
        scheduler.CopperQuiescentFastPathAttempts != 0 ||
        scheduler.CopperQuiescentFastPathUsed != 0 ||
        scheduler.CopperQuiescentFastPathVerificationMismatches != 0;
    var hasDeferredCpuBatch =
        scheduler.DeferredCpuBusBatchAttempts != 0 ||
        scheduler.DeferredCpuBusBatchUsed != 0 ||
        scheduler.DeferredCpuBusBatchInstructions != 0 ||
        scheduler.DeferredCpuBusBatchVerificationMismatches != 0 ||
        scheduler.DeferredCpuInternalNoBusWindowAttempts != 0 ||
        scheduler.DeferredCpuInternalNoBusWindowUsed != 0 ||
        scheduler.DeferredCpuInternalNoBusWindowVerificationMismatches != 0 ||
        scheduler.DeferredCpuWaitWindowAttempts != 0 ||
        scheduler.DeferredCpuWaitWindowEligible != 0 ||
        scheduler.DeferredCpuWaitWindowFastPathAttempts != 0 ||
        scheduler.DeferredCpuWaitWindowFastPathUsed != 0 ||
        scheduler.DeferredCpuWaitSlotShadowAttempts != 0 ||
        scheduler.DeferredCpuWaitSlotShadowMismatches != 0;
    if (!scheduler.HostProfilingEnabled && scheduler.HostDrainTicks == 0)
    {
        return string.Join(",", new[] { hasCopperQuiescence ? copperQuiescence : string.Empty, hasDeferredCpuBatch ? deferredCpuBatch : string.Empty }.Where(static value => value.Length != 0));
    }

    var accountedTicks =
        scheduler.HostWakeQueryTicks +
        scheduler.HostSameCycleQueryTicks +
        scheduler.HostRasterlineSkipTicks +
        scheduler.HostRasterTicks +
        scheduler.HostCiaTicks +
        scheduler.HostPaulaTicks +
        scheduler.HostDiskTicks +
        scheduler.HostAgnusTicks +
        scheduler.HostBlitterTicks;
    var otherTicks = Math.Max(0, scheduler.HostDrainTicks - accountedTicks);
    return string.Join(
        ',',
        FormatHostTicks("drain", scheduler.HostDrainTicks, scheduler.HostDrainTicks),
        FormatHostTicks("wakeq", scheduler.HostWakeQueryTicks, scheduler.HostDrainTicks),
        FormatHostTicks("sameq", scheduler.HostSameCycleQueryTicks, scheduler.HostDrainTicks),
        FormatHostTicks("skip", scheduler.HostRasterlineSkipTicks, scheduler.HostDrainTicks),
        FormatHostTicks("raster", scheduler.HostRasterTicks, scheduler.HostDrainTicks),
        FormatHostTicks("cia", scheduler.HostCiaTicks, scheduler.HostDrainTicks),
        FormatHostTicks("paula", scheduler.HostPaulaTicks, scheduler.HostDrainTicks),
        FormatHostTicks("disk", scheduler.HostDiskTicks, scheduler.HostDrainTicks),
        FormatHostTicks("agnus", scheduler.HostAgnusTicks, scheduler.HostDrainTicks),
        FormatHostTicks("blit", scheduler.HostBlitterTicks, scheduler.HostDrainTicks),
        FormatHostTicks("other", otherTicks, scheduler.HostDrainTicks),
        $"agenda:{scheduler.WakeAgendaCacheHits}/{scheduler.WakeAgendaCacheMisses}/{scheduler.WakeAgendaDrainSkips}/{scheduler.WakeAgendaInvalidations}",
        $"cpuevent:{scheduler.CpuVisibleNoEventCacheHits}/{scheduler.CpuVisibleNoEventCacheMisses}/{scheduler.CpuVisibleNoEventCacheInvalidations}",
        copperQuiescence,
        hasDeferredCpuBatch ? deferredCpuBatch : string.Empty);
}

static string FormatHostTicks(string name, long ticks, long totalTicks)
{
    var milliseconds = ticks * 1000.0 / Stopwatch.Frequency;
    var percent = totalTicks <= 0 ? 0.0 : ticks * 100.0 / totalTicks;
    return string.Create(
        CultureInfo.InvariantCulture,
        $"{name}:{milliseconds:F2}ms/{percent:F1}%");
}

static string FormatDisplaySummary(DisplaySummary summary)
{
    return $"bpl={summary.BitplanePixels}:{summary.BitplaneMinX},{summary.BitplaneMinY}-{summary.BitplaneMaxX},{summary.BitplaneMaxY}," +
        $"spr={summary.SpritePixels}:{summary.SpriteMinX},{summary.SpriteMinY}-{summary.SpriteMaxX},{summary.SpriteMaxY}," +
        $"dma={summary.BitplaneDmaFetches}/{summary.SpriteDmaFetches},missedSpr={summary.MissedSpriteSlots},spans={summary.BitplaneDataSpans}," +
        $"desc={summary.DescriptorBuilds}/{summary.DescriptorReplayAttempts}/{summary.DescriptorReplayedRows}/{summary.DescriptorFallbackRows}," +
        $"descRows={summary.DescriptorBitplaneRows}/{summary.DescriptorSpriteRows},descMis={summary.DescriptorMismatches}," +
        $"rowPlan={summary.RowDmaPlansBuilt}/{summary.RowDmaPlannedRowsExecuted}/{summary.RowDmaBitplaneEntriesExecuted}/{summary.RowDmaSpriteEntriesExecuted}/{summary.RowDmaScalarFallbackRows}/{summary.RowDmaPlanInvalidationRows}/{summary.RowDmaPlanMismatchRows}," +
        $"copperq={summary.CopperQuiescentWindowCount}/{summary.CopperQuiescentTotalCycles}/{summary.CopperQuiescentMaxCycles}," +
        $"bplcon={summary.Bplcon0:X4}/{summary.Bplcon1:X4}/{summary.Bplcon2:X4}";
}

static void WriteCopperQuiescenceAuditIfNeeded(BenchmarkRunResult result, BenchmarkOptions options)
{
    if (string.IsNullOrWhiteSpace(options.CopperQuiescenceAuditPath))
    {
        return;
    }

    var path = Path.GetFullPath(options.CopperQuiescenceAuditPath);
    var directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }

    var writeHeader = !File.Exists(path) || new FileInfo(path).Length == 0;
    using var writer = new StreamWriter(path, append: true);
    if (writeHeader)
    {
        writer.WriteLine("workload\tbackend\tfps\tms_frame\tstatus\tq_windows\tq_cycles\tq_max_cycles\tq_slot_accesses\tq_custom_writes\tq_cpu_hazard_writes\tq_cpu_benign_writes\tq_copper_hazard_moves\tq_copper_benign_moves\tq_drains\tq_predictions\tq_matches\tq_unsupported\tq_mismatches\tq_fast_attempts\tq_fast_used\tq_fast_skipped_drains\tq_fast_reject_unsupported\tq_fast_reject_invalidated\tq_fast_reject_dynamic\tq_fast_verify_mismatches\tq_first_mismatch\tq_fast_first_mismatch\tcpu_event_cache_hits\tcpu_event_cache_misses\tcpu_event_cache_invalidations\tcpu_batch_attempts\tcpu_batch_used\tcpu_batch_instructions\tcpu_batch_skipped_flushes\tcpu_batch_flushes\tcpu_batch_exit_target\tcpu_batch_exit_max\tcpu_batch_exit_chip\tcpu_batch_exit_pc\tcpu_batch_exit_exception\tcpu_batch_exit_unsupported\tcpu_batch_verify_mismatches\tcpu_batch_first_mismatch\tcpu_batch_wake_target\tcpu_batch_wake_pending\tcpu_batch_wake_vblank\tcpu_batch_wake_hsync_tod\tcpu_batch_wake_cia_timer\tcpu_batch_wake_disk\tcpu_batch_wake_paula\tcpu_batch_wake_copper\tcpu_batch_wake_blitter\tcpu_batch_diskwake_pending_dma\tcpu_batch_diskwake_active_progress\tcpu_batch_diskwake_active_completion\tcpu_batch_diskwake_sync\tcpu_batch_diskwake_index\tcpu_batch_diskwake_passive_byte\tcpu_batch_diskwake_unknown\tcpu_internal_attempts\tcpu_internal_used\tcpu_internal_cycles\tcpu_internal_advanced_cycles\tcpu_internal_mul\tcpu_internal_div\tcpu_internal_wake_target\tcpu_internal_wake_pending\tcpu_internal_wake_vblank\tcpu_internal_wake_hsync_tod\tcpu_internal_wake_cia_timer\tcpu_internal_wake_disk\tcpu_internal_wake_paula\tcpu_internal_wake_copper\tcpu_internal_wake_blitter\tcpu_internal_verify_mismatches\tcpu_internal_first_mismatch\tcpu_waitwin_attempts\tcpu_waitwin_eligible\tcpu_waitwin_total_cycles\tcpu_waitwin_max_cycles\tcpu_waitwin_fetch\tcpu_waitwin_data_read\tcpu_waitwin_data_write\tcpu_waitwin_custom\tcpu_waitwin_chip\tcpu_waitwin_expansion\tcpu_waitwin_rtc\tcpu_waitwin_custom_regs\tcpu_waitwin_byte\tcpu_waitwin_word\tcpu_waitwin_long\tcpu_waitwin_read\tcpu_waitwin_write\tcpu_waitwin_single_slot\tcpu_waitwin_long_slot\tcpu_slotshadow_attempts\tcpu_slotshadow_matches\tcpu_slotshadow_mismatches\tcpu_slotshadow_unsupported\tcpu_slotshadow_grant\tcpu_slotshadow_completion\tcpu_slotshadow_slot_owner\tcpu_slotshadow_blitter\tcpu_slotshadow_paula\tcpu_slotshadow_disk\tcpu_slotshadow_display\tcpu_slotshadow_copper\tcpu_slotshadow_live_attempts\tcpu_slotshadow_live_supported\tcpu_slotshadow_live_unsupported\tcpu_slotshadow_live_pending_write\tcpu_slotshadow_live_bitplane_window\tcpu_slotshadow_live_copper_wait_window\tcpu_slotshadow_live_rasterline_plan\tcpu_slotshadow_live_cpu_predict\tcpu_slotshadow_live_unstable\tcpu_slotshadow_live_scratch_write\tcpu_slotshadow_live_long_write\tcpu_slotshadow_live_other\tcpu_slotshadow_live_long\tcpu_slotshadow_live_bitplane_fetches\tcpu_slotshadow_live_sprite_fetches\tcpu_slotshadow_live_copper_steps\tcpu_slotshadow_blitter_scratch_attempts\tcpu_slotshadow_blitter_scratch_supported\tcpu_slotshadow_blitter_scratch_unsupported\tcpu_slotshadow_blitter_scratch_matches\tcpu_slotshadow_blitter_scratch_mismatches\tcpu_slotshadow_blitter_scratch_partial\tcpu_slotshadow_blitter_scratch_micro_ops\tcpu_slotshadow_first_mismatch\tcpu_fixedprod_attempts\tcpu_fixedprod_used\tcpu_fixedprod_pregrant_skipped\tcpu_fixedprod_postgrant_catchups\tcpu_fixedprod_wait_cycles\tcpu_fixedprod_fallback_unsupported\tcpu_fixedprod_fallback_dynamic\tcpu_fixedprod_fallback_frame\tcpu_fixedprod_fallback_copper\tcpu_fixedprod_fallback_pending\tcpu_fixedprod_fallback_plan\tcpu_fixedprod_fallback_sprite\tcpu_fixedprod_fallback_unstable\tcpu_fixedprod_verify_matches\tcpu_fixedprod_verify_mismatches\tcpu_fixedprod_disabled\tcpu_fixedprod_first_mismatch");
    }

    var scheduler = result.Scheduler;
    writer.WriteLine(string.Join(
        '\t',
        SanitizeTsv(result.Workload.Name),
        SanitizeTsv(result.CpuBackend),
        result.FramesPerSecond.ToString("F1", CultureInfo.InvariantCulture),
        result.MillisecondsPerFrame.ToString("F2", CultureInfo.InvariantCulture),
        SanitizeTsv(result.StatusText),
        result.Display.CopperQuiescentWindowCount.ToString(CultureInfo.InvariantCulture),
        result.Display.CopperQuiescentTotalCycles.ToString(CultureInfo.InvariantCulture),
        result.Display.CopperQuiescentMaxCycles.ToString(CultureInfo.InvariantCulture),
        scheduler.CopperQuiescentSlotContendedAccesses.ToString(CultureInfo.InvariantCulture),
        scheduler.CopperQuiescentCustomRegisterWrites.ToString(CultureInfo.InvariantCulture),
        scheduler.CopperQuiescentCpuScheduleAffectingCustomWrites.ToString(CultureInfo.InvariantCulture),
        scheduler.CopperQuiescentCpuBenignCustomWrites.ToString(CultureInfo.InvariantCulture),
        scheduler.CopperQuiescentCopperScheduleAffectingCustomMoves.ToString(CultureInfo.InvariantCulture),
        scheduler.CopperQuiescentCopperBenignCustomMoves.ToString(CultureInfo.InvariantCulture),
        scheduler.CopperQuiescentSchedulerDrains.ToString(CultureInfo.InvariantCulture),
        scheduler.CopperQuiescentShadowPredictions.ToString(CultureInfo.InvariantCulture),
        scheduler.CopperQuiescentShadowMatches.ToString(CultureInfo.InvariantCulture),
        scheduler.CopperQuiescentShadowUnsupported.ToString(CultureInfo.InvariantCulture),
        scheduler.CopperQuiescentShadowMismatches.ToString(CultureInfo.InvariantCulture),
        scheduler.CopperQuiescentFastPathAttempts.ToString(CultureInfo.InvariantCulture),
        scheduler.CopperQuiescentFastPathUsed.ToString(CultureInfo.InvariantCulture),
        scheduler.CopperQuiescentFastPathSkippedDrains.ToString(CultureInfo.InvariantCulture),
        scheduler.CopperQuiescentFastPathRejectedUnsupported.ToString(CultureInfo.InvariantCulture),
        scheduler.CopperQuiescentFastPathRejectedInvalidated.ToString(CultureInfo.InvariantCulture),
        scheduler.CopperQuiescentFastPathRejectedDynamicDma.ToString(CultureInfo.InvariantCulture),
        scheduler.CopperQuiescentFastPathVerificationMismatches.ToString(CultureInfo.InvariantCulture),
        SanitizeTsv(scheduler.CopperQuiescentFirstShadowMismatch),
        SanitizeTsv(scheduler.CopperQuiescentFastPathFirstMismatch),
        scheduler.CpuVisibleNoEventCacheHits.ToString(CultureInfo.InvariantCulture),
        scheduler.CpuVisibleNoEventCacheMisses.ToString(CultureInfo.InvariantCulture),
        scheduler.CpuVisibleNoEventCacheInvalidations.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuBusBatchAttempts.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuBusBatchUsed.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuBusBatchInstructions.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuBusBatchSkippedInstructionFlushes.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuBusBatchFlushes.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuBusBatchExitTargetCycle.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuBusBatchExitMaxInstructions.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuBusBatchExitChipVisibleAccess.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuBusBatchExitPcLeftFastWindow.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuBusBatchExitException.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuBusBatchExitUnsupported.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuBusBatchVerificationMismatches.ToString(CultureInfo.InvariantCulture),
        SanitizeTsv(scheduler.DeferredCpuBusBatchFirstMismatch),
        scheduler.DeferredCpuBusBatchWakeTargetCycle.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuBusBatchWakePendingInterrupt.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuBusBatchWakeVerticalBlank.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuBusBatchWakeHorizontalSyncTod.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuBusBatchWakeCiaTimer.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuBusBatchWakeDisk.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuBusBatchWakePaula.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuBusBatchWakeCopper.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuBusBatchWakeBlitter.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuBusBatchDiskWakePendingDma.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuBusBatchDiskWakeActiveDmaProgress.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuBusBatchDiskWakeActiveDmaCompletion.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuBusBatchDiskWakeSyncCandidate.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuBusBatchDiskWakeIndexPulse.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuBusBatchDiskWakePassiveByteReady.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuBusBatchDiskWakeUnknown.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuInternalNoBusWindowAttempts.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuInternalNoBusWindowUsed.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuInternalNoBusWindowTotalCycles.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuInternalNoBusWindowAdvancedCycles.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuInternalNoBusWindowMultiply.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuInternalNoBusWindowDivide.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuInternalNoBusWindowWakeTargetCycle.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuInternalNoBusWindowWakePendingInterrupt.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuInternalNoBusWindowWakeVerticalBlank.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuInternalNoBusWindowWakeHorizontalSyncTod.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuInternalNoBusWindowWakeCiaTimer.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuInternalNoBusWindowWakeDisk.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuInternalNoBusWindowWakePaula.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuInternalNoBusWindowWakeCopper.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuInternalNoBusWindowWakeBlitter.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuInternalNoBusWindowVerificationMismatches.ToString(CultureInfo.InvariantCulture),
        SanitizeTsv(scheduler.DeferredCpuInternalNoBusWindowFirstMismatch),
        scheduler.DeferredCpuWaitWindowAttempts.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitWindowEligible.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitWindowTotalCycles.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitWindowMaxCycles.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitWindowInstructionFetch.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitWindowDataRead.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitWindowDataWrite.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitWindowCustom.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitWindowChipRam.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitWindowExpansionRam.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitWindowRealTimeClock.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitWindowCustomRegisters.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitWindowByte.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitWindowWord.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitWindowLong.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitWindowRead.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitWindowWrite.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitWindowSingleSlot.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitWindowLongSlot.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowAttempts.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowMatches.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowMismatches.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowUnsupported.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowGrantMismatches.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowCompletionMismatches.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowSlotOwnerMismatches.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowBlitterStateMismatches.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowPaulaMismatches.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowDiskMismatches.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowDisplayMismatches.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowCopperMismatches.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowLiveAttempts.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowLiveSupported.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowLiveUnsupported.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowLiveUnsupportedPendingWrite.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowLiveUnsupportedBitplaneWindow.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowLiveUnsupportedCopperWaitWindow.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowLiveUnsupportedRasterlinePlan.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowLiveUnsupportedCpuPredict.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowLiveUnsupportedUnstable.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowLiveUnsupportedScratchWrite.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowLiveUnsupportedLongWrite.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowLiveUnsupportedOther.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowLiveLongAccesses.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowLiveBitplaneFetches.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowLiveSpriteFetches.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowLiveCopperSteps.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowBlitterScratchAttempts.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowBlitterScratchSupported.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowBlitterScratchUnsupported.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowBlitterScratchMatches.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowBlitterScratchMismatches.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowBlitterScratchPartial.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitSlotShadowBlitterScratchMicroOps.ToString(CultureInfo.InvariantCulture),
        SanitizeTsv(scheduler.DeferredCpuWaitSlotShadowFirstMismatch),
        scheduler.DeferredCpuWaitFixedImageProductionAttempts.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitFixedImageProductionUsed.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitFixedImageProductionPreGrantDrainsSkipped.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitFixedImageProductionPostGrantCatchups.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitFixedImageProductionPredictedWaitCycles.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitFixedImageProductionFallbackUnsupported.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitFixedImageProductionFallbackDynamicDma.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitFixedImageProductionFallbackFrame.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitFixedImageProductionFallbackCopper.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitFixedImageProductionFallbackPendingWrite.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitFixedImageProductionFallbackRasterlinePlan.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitFixedImageProductionFallbackSpriteState.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitFixedImageProductionFallbackUnstable.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitFixedImageProductionVerificationMatches.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitFixedImageProductionVerificationMismatches.ToString(CultureInfo.InvariantCulture),
        scheduler.DeferredCpuWaitFixedImageProductionDisabled ? "1" : "0",
        SanitizeTsv(scheduler.DeferredCpuWaitFixedImageProductionFirstMismatch)));
}

static string FormatDiskSummary(DiskSummary summary)
{
    return $"xfer={summary.TransferCount},words={summary.LastTransferWords},last=d{summary.LastTransferDrive} " +
        $"{summary.LastTransferCylinder}.{summary.LastTransferHead}@0x{summary.LastTransferAddress:X6}," +
        $"selected={summary.SelectedDrive},active={summary.ActiveDma},dsklen=0x{summary.Dsklen:X4},bytr=0x{summary.Dskbytr:X4}," +
        $"sched=nw:{summary.DiskNextWakeCandidateQueries},ne:{summary.DiskNextEventWakeCandidateQueries}," +
        $"hw:{summary.DiskHasWakeCandidateThroughQueries},he:{summary.DiskHasEventWakeCandidateThroughQueries}," +
        $"idx:{summary.DiskRefreshNextIndexPulseQueries},in:{summary.DiskInputAdvanceCalls}," +
        $"gate={summary.DiskSchedulerGateTrue}/{summary.DiskSchedulerGateFalse}," +
        $"why=pd:{summary.DiskPendingDmaWakeSources},ap:{summary.DiskActiveDmaProgressWakeSources}," +
        $"ac:{summary.DiskActiveDmaCompletionWakeSources},sy:{summary.DiskSyncCandidateWakeSources}," +
        $"ix:{summary.DiskIndexPulseWakeSources},pb:{summary.DiskPassiveByteReadyWakeSources}";
}

static string FormatDiskSchedulerDelta(DiskSummary before, DiskSummary after)
{
    return $"nw:{after.DiskNextWakeCandidateQueries - before.DiskNextWakeCandidateQueries}," +
        $"ne:{after.DiskNextEventWakeCandidateQueries - before.DiskNextEventWakeCandidateQueries}," +
        $"hw:{after.DiskHasWakeCandidateThroughQueries - before.DiskHasWakeCandidateThroughQueries}," +
        $"he:{after.DiskHasEventWakeCandidateThroughQueries - before.DiskHasEventWakeCandidateThroughQueries}," +
        $"idx:{after.DiskRefreshNextIndexPulseQueries - before.DiskRefreshNextIndexPulseQueries}," +
        $"in:{after.DiskInputAdvanceCalls - before.DiskInputAdvanceCalls}," +
        $"gate:{after.DiskSchedulerGateTrue - before.DiskSchedulerGateTrue}/" +
        $"{after.DiskSchedulerGateFalse - before.DiskSchedulerGateFalse}," +
        $"why=pd:{after.DiskPendingDmaWakeSources - before.DiskPendingDmaWakeSources}," +
        $"ap:{after.DiskActiveDmaProgressWakeSources - before.DiskActiveDmaProgressWakeSources}," +
        $"ac:{after.DiskActiveDmaCompletionWakeSources - before.DiskActiveDmaCompletionWakeSources}," +
        $"sy:{after.DiskSyncCandidateWakeSources - before.DiskSyncCandidateWakeSources}," +
        $"ix:{after.DiskIndexPulseWakeSources - before.DiskIndexPulseWakeSources}," +
        $"pb:{after.DiskPassiveByteReadyWakeSources - before.DiskPassiveByteReadyWakeSources}";
}

static string FormatSpecializationSummary(HardwareSpecializationSummary summary)
{
    return $"blt={summary.BlitterKernelHits}/{summary.BlitterKernelMisses}/{summary.BlitterGeneratedKernels}/{summary.BlitterFallbacks}," +
        $"bltQueue={summary.BlitterSlotQueueAttempts}/{summary.BlitterSlotQueueEnabledBlits}/{summary.BlitterSlotQueueUnsupportedBlits}/{summary.BlitterSlotQueueWords}/{summary.BlitterSlotQueueCommittedOps}," +
        $"bltRes={summary.BlitterSpecializedReservations}," +
        $"bltRow={summary.BlitterRowPipelineAttempts}/{summary.BlitterRowPipelineUsed}/{summary.BlitterRowPipelineWords}/{summary.BlitterRowPipelineCompletions}/{summary.BlitterDOnlyRowWords}/{summary.BlitterAToDRowWords}/{summary.BlitterRowPipelineFallbacks}," +
        $"bltAdvance={summary.BlitterAdvanceCalls}/{summary.BlitterAdvanceIdleExits}/{summary.BlitterAdvanceHorizonExits}/{summary.BlitterAdvanceBoundedAttempts}/{summary.BlitterAdvanceBoundedUses}/{summary.BlitterAdvanceSlotsExamined}/{summary.BlitterAdvanceMicroOps}/{summary.BlitterAdvanceWords}/{summary.BlitterAdvanceDeniedSlots}/{summary.BlitterAdvanceDisplayPreparations}/{summary.BlitterAdvancePaulaSlots}/{summary.BlitterAdvanceDiskSlots}/{summary.BlitterAdvanceBarriers}/{summary.BlitterAdvanceFallbacks}/{summary.BlitterAdvanceVerifyMatches}/{summary.BlitterAdvanceVerifyMismatches}," +
        $"bltAdvanceFirst={FormatCounterText(summary.BlitterAdvanceFirstMismatch)}," +
        $"bltTop={FormatBlitterTopPatterns(summary.BlitterTopPatterns)}," +
        $"dskPrep={summary.DiskPreparedTrackHits}/{summary.DiskPreparedTrackMisses}," +
        $"dskWin={summary.DiskRollingWindowHits}/{summary.DiskRollingWindowMisses}," +
        $"dskSync={summary.DiskSyncIndexHits}/{summary.DiskSyncIndexMisses}," +
        $"dskScalar={summary.DiskScalarBitReads}," +
        $"dskScan={summary.DiskFullTrackSyncScans}/{summary.DiskFullTrackSyncScanBits}," +
        $"dskScanMode={summary.DiskFullTrackWordSyncScans}/{summary.DiskFullTrackByteSyncScans}," +
        $"dskPlan={summary.DiskActiveDmaPredictionPasses}/{summary.DiskActiveDmaPredictedWordPlans}/{summary.DiskActiveDmaRuntimeWordPlans}," +
        $"dskReq={summary.DiskActiveDmaRequestsCreated}/{summary.DiskActiveDmaRequestsServed}/{summary.DiskActiveDmaRequestsBlocked}";
}

static string FormatBlitterTopPatterns(IReadOnlyList<BlitterPatternEntry> patterns)
{
    if (patterns.Count == 0)
    {
        return "none";
    }

    return string.Join(
        ';',
        patterns.Select(pattern =>
            $"{pattern.Count}x[{(pattern.LineMode ? "line" : "area")},c0={pattern.Bltcon0:X4},c1={pattern.Bltcon1:X4},mt={pattern.Minterm:X2},use={FormatBlitterUse(pattern)},fill={(pattern.FillEnabled ? (pattern.FillExclusive ? "xor" : "inc") : "no")},desc={(pattern.Descending ? 1 : 0)},size={pattern.WidthWords}x{pattern.Height}]"));
}

static string FormatBlitterUse(BlitterPatternEntry pattern)
    => string.Create(4, pattern, static (span, value) =>
    {
        span[0] = value.UseA ? 'A' : '-';
        span[1] = value.UseB ? 'B' : '-';
        span[2] = value.UseC ? 'C' : '-';
        span[3] = value.UseD ? 'D' : '-';
    });

static string FormatLastDeniedFixedSlot(AgnusBeamDmaSnapshot agnus)
{
    if (!agnus.LastDeniedFixedSlot.HasValue)
    {
        return "none";
    }

    var denied = agnus.LastDeniedFixedSlot.Value;
    var blocker = agnus.LastDeniedFixedSlotBlocker;
    var blockedBy = blocker.HasValue
        ? $",blocked-by={blocker.Value.Owner}/{blocker.Value.Kind}@{blocker.Value.RequestedCycle}->{blocker.Value.GrantedCycle}"
        : string.Empty;
    return $"{denied.Owner}/{denied.Kind}/addr={denied.Address:X6}@{denied.RequestedCycle}->{denied.GrantedCycle}{blockedBy}";
}

static string FormatSlotGrantMix(AgnusBeamDmaSnapshot agnus)
{
    return $"cpu={agnus.CpuSlotGrantCount},blt={agnus.BlitterSlotGrantCount},cop={agnus.CopperSlotGrantCount},pau={agnus.PaulaSlotGrantCount},dsk={agnus.DiskSlotGrantCount},spr={agnus.SpriteSlotGrantCount},bpl={agnus.BitplaneSlotGrantCount}";
}

static string FormatDeniedSlotMix(AgnusBeamDmaSnapshot agnus)
{
    return $"cpu={agnus.CpuDeniedFixedSlotCount},blt={agnus.BlitterDeniedFixedSlotCount},cop={agnus.CopperDeniedFixedSlotCount},pau={agnus.PaulaDeniedFixedSlotCount},dsk={agnus.DiskDeniedFixedSlotCount},spr={agnus.SpriteDeniedFixedSlotCount},bpl={agnus.BitplaneDeniedFixedSlotCount}";
}

static string FormatDeniedBlockerMix(AgnusBeamDmaSnapshot agnus)
{
    return $"cpu={agnus.CpuDeniedFixedSlotBlockerCount},blt={agnus.BlitterDeniedFixedSlotBlockerCount},cop={agnus.CopperDeniedFixedSlotBlockerCount},pau={agnus.PaulaDeniedFixedSlotBlockerCount},dsk={agnus.DiskDeniedFixedSlotBlockerCount},spr={agnus.SpriteDeniedFixedSlotBlockerCount},bpl={agnus.BitplaneDeniedFixedSlotBlockerCount}";
}

static double Percent(long value, long total)
    => total == 0 ? 0 : value * 100.0 / total;

static double MillisecondsPerFrame(long ticks, int frames)
    => frames <= 0 ? 0 : ticks * 1000.0 / Stopwatch.Frequency / frames;

static bool IsRelease()
{
#if DEBUG
    return false;
#else
    return true;
#endif
}

internal readonly record struct BenchmarkWorkload(
    string Name,
    string? FileName,
    int FireFrame = -1,
    string? Profile = null,
    string? LaunchPath = null);

internal readonly record struct FrameProfile(
    double CpuPercent,
    double AgnusPercent,
    double DisplayPercent,
    double CpuMillisecondsPerFrame,
    double AgnusMillisecondsPerFrame,
    double DisplayMillisecondsPerFrame,
    double TotalMillisecondsPerFrame);

internal readonly record struct FrameTimingSummary(
    double MaxMilliseconds,
    int SlowFramesOver20,
    int SlowFramesOver33,
    int SlowFramesOver40);

internal readonly record struct AudioQueueSummary(
    int ActiveFrames,
    int SubmitFailures,
    double MinQueuedMilliseconds,
    double MaxQueuedMilliseconds);

internal readonly record struct FramebufferSummary(int NonBlackPixels, int DistinctColors, uint Checksum);

internal readonly record struct AudioSummary(int Frames, int NonZeroSamples, float Peak, uint Checksum);

internal readonly record struct DisplaySummary(
    ushort Bplcon0,
    ushort Bplcon1,
    ushort Bplcon2,
    int BitplanePixels,
    int BitplaneMinX,
    int BitplaneMinY,
    int BitplaneMaxX,
    int BitplaneMaxY,
    int SpritePixels,
    int SpriteMinX,
    int SpriteMinY,
    int SpriteMaxX,
    int SpriteMaxY,
    int BitplaneDmaFetches,
    int SpriteDmaFetches,
    int MissedSpriteSlots,
    int DescriptorBuilds,
    int DescriptorReplayAttempts,
    int DescriptorReplayedRows,
    int DescriptorFallbackRows,
    int DescriptorBitplaneRows,
    int DescriptorSpriteRows,
    int DescriptorMismatches,
    int RowDmaPlansBuilt,
    int RowDmaPlannedRowsExecuted,
    int RowDmaBitplaneEntriesExecuted,
    int RowDmaSpriteEntriesExecuted,
    int RowDmaScalarFallbackRows,
    int RowDmaPlanInvalidationRows,
    int RowDmaPlanMismatchRows,
    long CopperQuiescentWindowCount,
    long CopperQuiescentTotalCycles,
    long CopperQuiescentMaxCycles,
    int BitplaneDataSpans);

internal readonly record struct DiskSummary(
    int TransferCount,
    int LastTransferWords,
    int LastTransferDrive,
    int LastTransferCylinder,
    int LastTransferHead,
    uint LastTransferAddress,
    int SelectedDrive,
    bool ActiveDma,
    ushort Dsklen,
    ushort Dskbytr,
    long DiskNextWakeCandidateQueries,
    long DiskNextEventWakeCandidateQueries,
    long DiskHasWakeCandidateThroughQueries,
    long DiskHasEventWakeCandidateThroughQueries,
    long DiskRefreshNextIndexPulseQueries,
    long DiskInputAdvanceCalls,
    long DiskSchedulerGateTrue,
    long DiskSchedulerGateFalse,
    long DiskPendingDmaWakeSources,
    long DiskActiveDmaProgressWakeSources,
    long DiskActiveDmaCompletionWakeSources,
    long DiskSyncCandidateWakeSources,
    long DiskIndexPulseWakeSources,
    long DiskPassiveByteReadyWakeSources);

internal readonly record struct HardwareSpecializationSummary(
    long BlitterKernelHits,
    long BlitterKernelMisses,
    long BlitterGeneratedKernels,
    long BlitterFallbacks,
    long BlitterSlotQueueAttempts,
    long BlitterSlotQueueEnabledBlits,
    long BlitterSlotQueueUnsupportedBlits,
    long BlitterSlotQueueWords,
    long BlitterSlotQueueCommittedOps,
    long BlitterSpecializedReservations,
    long BlitterRowPipelineAttempts,
    long BlitterRowPipelineUsed,
    long BlitterRowPipelineWords,
    long BlitterRowPipelineCompletions,
    long BlitterDOnlyRowWords,
    long BlitterAToDRowWords,
    long BlitterRowPipelineFallbacks,
    long BlitterAdvanceCalls,
    long BlitterAdvanceIdleExits,
    long BlitterAdvanceHorizonExits,
    long BlitterAdvanceBoundedAttempts,
    long BlitterAdvanceBoundedUses,
    long BlitterAdvanceSlotsExamined,
    long BlitterAdvanceMicroOps,
    long BlitterAdvanceWords,
    long BlitterAdvanceDeniedSlots,
    long BlitterAdvanceDisplayPreparations,
    long BlitterAdvancePaulaSlots,
    long BlitterAdvanceDiskSlots,
    long BlitterAdvanceBarriers,
    long BlitterAdvanceFallbacks,
    long BlitterAdvanceVerifyMatches,
    long BlitterAdvanceVerifyMismatches,
    string BlitterAdvanceFirstMismatch,
    BlitterPatternEntry[] BlitterTopPatterns,
    long DiskPreparedTrackHits,
    long DiskPreparedTrackMisses,
    long DiskRollingWindowHits,
    long DiskRollingWindowMisses,
    long DiskSyncIndexHits,
    long DiskSyncIndexMisses,
    long DiskScalarBitReads,
    long DiskFullTrackSyncScans,
    long DiskFullTrackSyncScanBits,
    long DiskFullTrackWordSyncScans,
    long DiskFullTrackByteSyncScans,
    long DiskActiveDmaPredictionPasses,
    long DiskActiveDmaPredictedWordPlans,
    long DiskActiveDmaRuntimeWordPlans,
    long DiskActiveDmaRequestsCreated,
    long DiskActiveDmaRequestsServed,
    long DiskActiveDmaRequestsBlocked);

internal readonly record struct BenchmarkRunResult(
    BenchmarkWorkload Workload,
    string TimingMode,
    bool Missing,
    double FramesPerSecond,
    double MillisecondsPerFrame,
    long AllocatedBytes,
    FrameTimingSummary Timing,
    AudioQueueSummary AudioQueue,
    FrameProfile Phase,
    int LiveDisplayEventCount,
    int LiveCopperStepCount,
    int LivePendingWriteEventCount,
    int LiveFetchBatchWordCount,
    FramebufferSummary Framebuffer,
    AudioSummary Audio,
    DisplaySummary Display,
    DiskSummary Disk,
    HardwareSpecializationSummary Specialization,
    AgnusBeamDmaSnapshot Agnus,
    AmigaHardwareSchedulerSnapshot Scheduler,
    long MaxFrameSchedulerDrains,
    M68kInstructionFrequencySnapshot InstructionFrequency,
    M68kPlannedInterpreterCounters PlannedInterpreterCounters,
    string CpuBackend,
    M68kJitCounters Jit,
    AmigaDiskTraceEvent[] DiskTrace,
    bool CopperStartRuntimeHandoffActive,
    long CopperStartRuntimeHandoffCount,
    string StatusText);

internal readonly record struct BenchmarkOptions(
    bool Smoke,
    int WarmupFrames,
    int MeasuredFrames,
    int RepeatCount,
    string? Only,
    string? Profile,
    string? CpuBackend,
    bool JitFallbackAttribution,
    bool RealKickstart,
    string? KickstartRomPath,
    bool InstructionMatrix,
    bool DiskDivergenceTrace,
    bool SkipPresentation,
    bool SkipPhaseProfile,
    bool HardwareProfile,
    int TopInstructionOpcodes,
    int ProgressIntervalFrames,
    int FireStartFrame,
    int FireEndFrame,
    int LeftStartFrame,
    int LeftEndFrame,
    int RightStartFrame,
    int RightEndFrame,
    int FirePulseStartFrame,
    int FirePulseEndFrame,
    int FirePulseIntervalFrames,
    int FirePulseDurationFrames,
    int? StopCylinder,
    string? AudioAuditPath,
    int AudioAuditIntervalFrames,
    string? CopperQuiescenceAuditPath,
    string? SlotScheduleAuditPath,
    int SlotScheduleAuditLimit,
    bool CopperQuiescenceFastPath,
    bool CopperQuiescenceFastPathVerify,
    bool DeferredCpuBusBatch,
    bool DeferredCpuBusBatchVerify,
    bool CpuWaitSlotReference,
    bool HardwareSpecialization,
    BlitterAdvanceMode BlitterAdvanceMode,
    bool StopOnDebugSnapshot,
    int PauseBeforeMeasureMilliseconds,
    string? DumpDebugSnapshotPath,
    string? DumpFramePath,
    int DumpFrameInterval,
    string? DumpChipRamPath,
    bool OpcodeDispatchBenchmark,
    bool CiaPollBenchmark,
    bool SyntheticBlitterBenchmark,
    M68kOpcodePlanDispatch? OpcodeDispatch,
    int OpcodeDispatchWarmupInstructions,
    int OpcodeDispatchInstructions,
    uint? ExpectedFramebufferChecksum,
    uint? ExpectedAudioChecksum)
{
    public static BenchmarkOptions Parse(string[] args)
    {
        var smoke = args.Contains("--smoke", StringComparer.OrdinalIgnoreCase);
        var warmup = smoke ? 20 : 240;
        var measured = smoke ? 20 : 360;
        var repeatCount = 1;
        var instructionMatrix = false;
        var diskDivergenceTrace = false;
        var skipPresentation = false;
        var skipPhaseProfile = false;
        var hardwareProfile = false;
        var topInstructionOpcodes = 16;
        var progressIntervalFrames = 0;
        var fireStartFrame = -1;
        var fireEndFrame = -1;
        var leftStartFrame = -1;
        var leftEndFrame = -1;
        var rightStartFrame = -1;
        var rightEndFrame = -1;
        var firePulseStartFrame = -1;
        var firePulseEndFrame = -1;
        var firePulseIntervalFrames = 100;
        var firePulseDurationFrames = 8;
        int? stopCylinder = null;
        string? audioAuditPath = null;
        var audioAuditIntervalFrames = 500;
        string? copperQuiescenceAuditPath = null;
        string? slotScheduleAuditPath = null;
        var slotScheduleAuditLimit = 1_000_000;
        var copperQuiescenceFastPath = false;
        var copperQuiescenceFastPathVerify = false;
        var deferredCpuBusBatch = false;
        var deferredCpuBusBatchVerify = false;
        var cpuWaitSlotReference = false;
        var hardwareSpecialization = false;
        var blitterAdvanceMode = BlitterAdvanceMode.Reference;
        var stopOnDebugSnapshot = false;
        var pauseBeforeMeasureMilliseconds = 0;
        string? dumpDebugSnapshotPath = null;
        string? dumpFramePath = null;
        var dumpFrameInterval = 1;
        string? dumpChipRamPath = null;
        var opcodeDispatchBenchmark = false;
        var ciaPollBenchmark = false;
        var syntheticBlitterBenchmark = false;
        M68kOpcodePlanDispatch? opcodeDispatch = null;
        var opcodeDispatchWarmupInstructions = smoke ? 20_000 : 200_000;
        var opcodeDispatchInstructions = smoke ? 200_000 : 2_000_000;
        string? only = null;
        string? profile = null;
        string? cpuBackend = null;
        var jitFallbackAttribution = false;
        var realKickstart = false;
        string? kickstartRomPath = null;
        uint? expectedFramebufferChecksum = null;
        uint? expectedAudioChecksum = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--warmup", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                _ = int.TryParse(args[++i], out warmup);
            }
            else if (string.Equals(args[i], "--frames", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                _ = int.TryParse(args[++i], out measured);
            }
            else if (string.Equals(args[i], "--repeat", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                _ = int.TryParse(args[++i], out repeatCount);
            }
            else if (string.Equals(args[i], "--only", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                only = args[++i];
            }
            else if ((string.Equals(args[i], "--profile", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[i], "-p", StringComparison.OrdinalIgnoreCase)) &&
                i + 1 < args.Length)
            {
                profile = args[++i];
            }
            else if (string.Equals(args[i], "--cpu", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                cpuBackend = args[++i];
            }
            else if (string.Equals(args[i], "--jit", StringComparison.OrdinalIgnoreCase))
            {
                cpuBackend = "jit";
            }
            else if (string.Equals(args[i], "--jit-fallback-attribution", StringComparison.OrdinalIgnoreCase))
            {
                jitFallbackAttribution = true;
            }
            else if (string.Equals(args[i], "--jit-m68040", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[i], "--jit-68040", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[i], "--m68040-jit", StringComparison.OrdinalIgnoreCase))
            {
                cpuBackend = "jitm68040";
            }
            else if (string.Equals(args[i], "--real-kickstart", StringComparison.OrdinalIgnoreCase))
            {
                realKickstart = true;
            }
            else if ((string.Equals(args[i], "--kickstart-rom", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[i], "--kickstart", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[i], "--rom", StringComparison.OrdinalIgnoreCase)) &&
                i + 1 < args.Length)
            {
                kickstartRomPath = args[++i];
            }
            else if (string.Equals(args[i], "--instruction-matrix", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[i], "--instructions", StringComparison.OrdinalIgnoreCase))
            {
                instructionMatrix = true;
            }
            else if (string.Equals(args[i], "--disk-divergence-trace", StringComparison.OrdinalIgnoreCase))
            {
                diskDivergenceTrace = true;
            }
            else if (string.Equals(args[i], "--opcode-dispatch-bench", StringComparison.OrdinalIgnoreCase))
            {
                opcodeDispatchBenchmark = true;
            }
            else if (string.Equals(args[i], "--cia-poll-bench", StringComparison.OrdinalIgnoreCase))
            {
                ciaPollBenchmark = true;
            }
            else if (string.Equals(args[i], "--synthetic-blitter-bench", StringComparison.OrdinalIgnoreCase))
            {
                syntheticBlitterBenchmark = true;
            }
            else if (string.Equals(args[i], "--opcode-dispatch", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                opcodeDispatch = ParseOpcodeDispatch(args[++i]);
            }
            else if (string.Equals(args[i], "--opcode-dispatch-warmup-instructions", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                _ = int.TryParse(args[++i], out opcodeDispatchWarmupInstructions);
            }
            else if (string.Equals(args[i], "--opcode-dispatch-instructions", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                _ = int.TryParse(args[++i], out opcodeDispatchInstructions);
            }
            else if (string.Equals(args[i], "--skip-presentation", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[i], "--no-present", StringComparison.OrdinalIgnoreCase))
            {
                skipPresentation = true;
            }
            else if (string.Equals(args[i], "--skip-phase-profile", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[i], "--no-phase-profile", StringComparison.OrdinalIgnoreCase))
            {
                skipPhaseProfile = true;
            }
            else if (string.Equals(args[i], "--hardware-profile", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[i], "--host-profile", StringComparison.OrdinalIgnoreCase))
            {
                hardwareProfile = true;
            }
            else if (string.Equals(args[i], "--top-opcodes", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                instructionMatrix = true;
                _ = int.TryParse(args[++i], out topInstructionOpcodes);
            }
            else if (string.Equals(args[i], "--progress", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out var parsedProgress))
                {
                    progressIntervalFrames = parsedProgress;
                    i++;
                }
                else
                {
                    progressIntervalFrames = 100;
                }
            }
            else if (string.Equals(args[i], "--fire-from", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                _ = int.TryParse(args[++i], out fireStartFrame);
                if (fireEndFrame < 0)
                {
                    fireEndFrame = int.MaxValue;
                }
            }
            else if (string.Equals(args[i], "--fire-to", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                _ = int.TryParse(args[++i], out fireEndFrame);
            }
            else if (string.Equals(args[i], "--left-from", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                _ = int.TryParse(args[++i], out leftStartFrame);
                if (leftEndFrame < 0)
                {
                    leftEndFrame = int.MaxValue;
                }
            }
            else if (string.Equals(args[i], "--left-to", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                _ = int.TryParse(args[++i], out leftEndFrame);
            }
            else if (string.Equals(args[i], "--right-from", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                _ = int.TryParse(args[++i], out rightStartFrame);
                if (rightEndFrame < 0)
                {
                    rightEndFrame = int.MaxValue;
                }
            }
            else if (string.Equals(args[i], "--right-to", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                _ = int.TryParse(args[++i], out rightEndFrame);
            }
            else if (string.Equals(args[i], "--pulse-fire-from", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                _ = int.TryParse(args[++i], out firePulseStartFrame);
                if (firePulseEndFrame < 0)
                {
                    firePulseEndFrame = int.MaxValue;
                }
            }
            else if (string.Equals(args[i], "--pulse-fire-to", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                _ = int.TryParse(args[++i], out firePulseEndFrame);
            }
            else if (string.Equals(args[i], "--pulse-fire-every", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                _ = int.TryParse(args[++i], out firePulseIntervalFrames);
            }
            else if (string.Equals(args[i], "--pulse-fire-duration", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                _ = int.TryParse(args[++i], out firePulseDurationFrames);
            }
            else if (string.Equals(args[i], "--stop-cylinder", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out var parsedCylinder))
                {
                    stopCylinder = parsedCylinder;
                }
            }
            else if (string.Equals(args[i], "--audio-audit", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                audioAuditPath = args[++i];
            }
            else if (string.Equals(args[i], "--audio-audit-interval", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                _ = int.TryParse(args[++i], out audioAuditIntervalFrames);
            }
            else if (string.Equals(args[i], "--copper-quiescence-audit", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                copperQuiescenceAuditPath = args[++i];
            }
            else if (string.Equals(args[i], "--slot-schedule-audit", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                slotScheduleAuditPath = args[++i];
            }
            else if (string.Equals(args[i], "--slot-schedule-audit-limit", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                _ = int.TryParse(args[++i], out slotScheduleAuditLimit);
            }
            else if (string.Equals(args[i], "--copper-quiescence-fastpath", StringComparison.OrdinalIgnoreCase))
            {
                copperQuiescenceFastPath = true;
            }
            else if (string.Equals(args[i], "--copper-quiescence-fastpath-verify", StringComparison.OrdinalIgnoreCase))
            {
                copperQuiescenceFastPath = true;
                copperQuiescenceFastPathVerify = true;
            }
            else if (string.Equals(args[i], "--cpu-deferred-bus-batch", StringComparison.OrdinalIgnoreCase))
            {
                deferredCpuBusBatch = true;
            }
            else if (string.Equals(args[i], "--cpu-deferred-bus-batch-verify", StringComparison.OrdinalIgnoreCase))
            {
                deferredCpuBusBatch = true;
                deferredCpuBusBatchVerify = true;
            }
            else if (string.Equals(args[i], "--cpu-wait-slot-reference", StringComparison.OrdinalIgnoreCase))
            {
                cpuWaitSlotReference = true;
            }
            else if (string.Equals(args[i], "--hardware-specialization", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[i], "--hw-specialization", StringComparison.OrdinalIgnoreCase))
            {
                hardwareSpecialization = true;
            }
            else if (string.Equals(args[i], "--blitter-advance-mode", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                blitterAdvanceMode = ParseBlitterAdvanceMode(args[++i]);
            }
            else if (string.Equals(args[i], "--stop-on-debug-snapshot", StringComparison.OrdinalIgnoreCase))
            {
                stopOnDebugSnapshot = true;
            }
            else if (string.Equals(args[i], "--pause-before-measure-ms", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                _ = int.TryParse(args[++i], out pauseBeforeMeasureMilliseconds);
            }
            else if (string.Equals(args[i], "--dump-debug-snapshot", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                dumpDebugSnapshotPath = args[++i];
            }
            else if (string.Equals(args[i], "--dump-frame", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                dumpFramePath = args[++i];
            }
            else if (string.Equals(args[i], "--dump-frame-interval", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                _ = int.TryParse(args[++i], out dumpFrameInterval);
            }
            else if (string.Equals(args[i], "--dump-chipram", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                dumpChipRamPath = args[++i];
            }
            else if (string.Equals(args[i], "--expect-framebuffer-checksum", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                expectedFramebufferChecksum = ParseChecksum(args[++i]);
            }
            else if (string.Equals(args[i], "--expect-audio-checksum", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                expectedAudioChecksum = ParseChecksum(args[++i]);
            }
        }

        return new BenchmarkOptions(
            smoke,
            Math.Max(0, warmup),
            Math.Max(1, measured),
            Math.Max(1, repeatCount),
            only,
            profile,
            cpuBackend,
            jitFallbackAttribution,
            realKickstart,
            kickstartRomPath,
            instructionMatrix,
            diskDivergenceTrace,
            skipPresentation,
            skipPhaseProfile,
            hardwareProfile,
            Math.Clamp(topInstructionOpcodes, 0, 256),
            Math.Max(0, progressIntervalFrames),
            Math.Max(-1, fireStartFrame),
            Math.Max(-1, fireEndFrame),
            Math.Max(-1, leftStartFrame),
            Math.Max(-1, leftEndFrame),
            Math.Max(-1, rightStartFrame),
            Math.Max(-1, rightEndFrame),
            Math.Max(-1, firePulseStartFrame),
            Math.Max(-1, firePulseEndFrame),
            Math.Max(1, firePulseIntervalFrames),
            Math.Max(1, firePulseDurationFrames),
            stopCylinder,
            audioAuditPath,
            Math.Max(1, audioAuditIntervalFrames),
            copperQuiescenceAuditPath,
            slotScheduleAuditPath,
            Math.Max(1, slotScheduleAuditLimit),
            copperQuiescenceFastPath,
            copperQuiescenceFastPathVerify,
            deferredCpuBusBatch,
            deferredCpuBusBatchVerify,
            cpuWaitSlotReference,
            hardwareSpecialization,
            blitterAdvanceMode,
            stopOnDebugSnapshot,
            Math.Max(0, pauseBeforeMeasureMilliseconds),
            dumpDebugSnapshotPath,
            dumpFramePath,
            Math.Max(1, dumpFrameInterval),
            dumpChipRamPath,
            opcodeDispatchBenchmark,
            ciaPollBenchmark,
            syntheticBlitterBenchmark,
            opcodeDispatch,
            Math.Max(0, opcodeDispatchWarmupInstructions),
            Math.Max(1, opcodeDispatchInstructions),
            expectedFramebufferChecksum,
            expectedAudioChecksum);
    }

    private static M68kOpcodePlanDispatch? ParseOpcodeDispatch(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "scalar" or "off" or "none" => M68kOpcodePlanDispatch.Scalar,
            "kind" or "kindtable" or "table" => M68kOpcodePlanDispatch.KindTable,
            "packed" or "packedplan" => M68kOpcodePlanDispatch.PackedPlan,
            _ => null
        };

    private static BlitterAdvanceMode ParseBlitterAdvanceMode(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "reference" => BlitterAdvanceMode.Reference,
            "bounded" => BlitterAdvanceMode.Bounded,
            "verify" => BlitterAdvanceMode.Verify,
            _ => throw new ArgumentException(
                $"Unsupported blitter advance mode '{value}'. Expected reference, bounded, or verify.")
        };

    private static uint ParseChecksum(string value)
    {
        var text = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? value[2..]
            : value;
        if (!uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var checksum))
        {
            throw new ArgumentException($"Invalid checksum '{value}'. Expected an eight-digit hexadecimal value.");
        }

        return checksum;
    }

}

internal readonly record struct CiaPollBenchmarkResult(
    int Instructions,
    TimeSpan Elapsed,
    long Cycles,
    long AllocatedBytes,
    uint Checksum)
{
    public double InstructionsPerSecond => Instructions / Math.Max(Elapsed.TotalSeconds, double.Epsilon);
}

internal readonly record struct SyntheticBlitterBenchmarkRun(IM68kBatchCore Cpu, AmigaBus Bus) : IDisposable
{
    public void Dispose()
        => Cpu.Dispose();
}

internal sealed class SyntheticBenchmarkBoundary :
    IM68kInstructionBoundary,
    IM68kDeferredCpuBusBatchBoundary
{
    public static SyntheticBenchmarkBoundary Instance { get; } = new();

    public bool BeforeInstruction()
        => true;

    public bool TryPrepareDeferredCpuBusBatch(M68kCpuState state) => true;

    public long ClampDeferredCpuBusBatchTarget(M68kCpuState state, long targetCycle)
        => targetCycle;

    public void AfterDeferredCpuBusBatch(long previousCycle, long currentCycle, int instructionCount)
    {
        _ = previousCycle;
        _ = currentCycle;
        _ = instructionCount;
    }

    public void AfterInstruction(long previousCycle, long currentCycle)
    {
        _ = previousCycle;
        _ = currentCycle;
    }
}

internal sealed record CiaPollBenchmarkRun(IM68kCore Cpu, AmigaBus Bus) : IDisposable
{
    public void Dispose()
        => Cpu.Dispose();
}

internal readonly record struct OpcodeDispatchBenchmarkWorkload(
    string Name,
    ushort[] Program,
    Action<M68kCpuState, OpcodeDispatchBenchmarkBus> Setup,
    int MemorySize);

internal readonly record struct OpcodeDispatchBenchmarkResult(
    int Instructions,
    TimeSpan Elapsed,
    long Cycles,
    long AllocatedBytes,
    M68kPlannedInterpreterCounters Counters,
    uint Checksum)
{
    public double InstructionsPerSecond => Instructions / Math.Max(Elapsed.TotalSeconds, double.Epsilon);
}

internal sealed record OpcodeDispatchBenchmarkRun(
    M68kInterpreterCore<OpcodeDispatchBenchmarkBus, OpcodeDispatchBenchmarkCpuDataAccess> Cpu,
    OpcodeDispatchBenchmarkBus Bus) : IDisposable
{
    public void Dispose()
        => Cpu.Dispose();
}

internal readonly struct OpcodeDispatchBenchmarkCpuDataAccess :
    IM68kCpuDataAccess<OpcodeDispatchBenchmarkBus, OpcodeDispatchBenchmarkCpuDataAccess>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte ReadByte(OpcodeDispatchBenchmarkBus bus, uint address, ref long cycle)
        => bus.TryReadExactCpuDataByte(address, ref cycle, out var value)
            ? value
            : ReadByteFallback(bus, address, ref cycle);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort ReadWord(OpcodeDispatchBenchmarkBus bus, uint address, ref long cycle)
        => bus.TryReadExactCpuDataWord(address, ref cycle, out var value)
            ? value
            : ReadWordFallback(bus, address, ref cycle);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ReadLong(OpcodeDispatchBenchmarkBus bus, uint address, ref long cycle)
        => bus.TryReadExactCpuDataLong(address, ref cycle, out var value)
            ? value
            : ReadLongFallback(bus, address, ref cycle);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteByte(OpcodeDispatchBenchmarkBus bus, uint address, byte value, ref long cycle)
    {
        if (!bus.TryWriteExactCpuDataByte(address, value, ref cycle))
        {
            WriteByteFallback(bus, address, value, ref cycle);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteTasByte(OpcodeDispatchBenchmarkBus bus, uint address, byte value, ref long cycle)
        => WriteByte(bus, address, value, ref cycle);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteWord(OpcodeDispatchBenchmarkBus bus, uint address, ushort value, ref long cycle)
    {
        if (!bus.TryWriteExactCpuDataWord(address, value, ref cycle))
        {
            WriteWordFallback(bus, address, value, ref cycle);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteLong(OpcodeDispatchBenchmarkBus bus, uint address, uint value, ref long cycle)
    {
        if (!bus.TryWriteExactCpuDataLong(address, value, ref cycle))
        {
            WriteLongFallback(bus, address, value, ref cycle);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static byte ReadByteFallback(OpcodeDispatchBenchmarkBus bus, uint address, ref long cycle)
        => bus.ReadByte(address, ref cycle, M68kBusAccessKind.CpuDataRead);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ushort ReadWordFallback(OpcodeDispatchBenchmarkBus bus, uint address, ref long cycle)
        => bus.ReadWord(address, ref cycle, M68kBusAccessKind.CpuDataRead);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static uint ReadLongFallback(OpcodeDispatchBenchmarkBus bus, uint address, ref long cycle)
        => bus.ReadLong(address, ref cycle, M68kBusAccessKind.CpuDataRead);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteByteFallback(OpcodeDispatchBenchmarkBus bus, uint address, byte value, ref long cycle)
        => bus.WriteByte(address, value, ref cycle, M68kBusAccessKind.CpuDataWrite);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteWordFallback(OpcodeDispatchBenchmarkBus bus, uint address, ushort value, ref long cycle)
        => bus.WriteWord(address, value, ref cycle, M68kBusAccessKind.CpuDataWrite);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteLongFallback(OpcodeDispatchBenchmarkBus bus, uint address, uint value, ref long cycle)
        => bus.WriteLong(address, value, ref cycle, M68kBusAccessKind.CpuDataWrite);
}

internal sealed class OpcodeDispatchBenchmarkBus : IM68kBus, IM68kInstructionFetchWindowBus
{
    private readonly uint[] _generation = [1];
    private readonly int _memorySize;
    private readonly uint _addressMask;
    private M68kInstructionFetchWindow _window;

    public OpcodeDispatchBenchmarkBus(int memorySize)
    {
        if (memorySize <= 0 || (memorySize & (memorySize - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(memorySize), memorySize, "Benchmark memory size must be a positive power of two.");
        }

        _memorySize = memorySize;
        _addressMask = (uint)(memorySize - 1);
        Memory = new byte[memorySize];
        _window = new M68kInstructionFetchWindow(
            Memory,
            0,
            0,
            (uint)_memorySize,
            _addressMask,
            0,
            _generation,
            _generation[0]);
    }

    public byte[] Memory { get; }

    public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
    {
        _ = cycle;
        _ = accessKind;
        return Memory[Offset(address)];
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
        _ = cycle;
        _ = accessKind;
        Memory[Offset(address)] = value;
    }

    public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
    {
        _ = cycle;
        _ = accessKind;
        WriteWordValue(address, value);
    }

    public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
    {
        _ = cycle;
        _ = accessKind;
        WriteLongValue(address, value);
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
        => _ = cycle;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadExactCpuDataByte(uint address, ref long cycle, out byte value)
    {
        _ = cycle;
        value = Memory[Offset(address)];
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadExactCpuDataWord(uint address, ref long cycle, out ushort value)
    {
        _ = cycle;
        value = ReadWordValue(address);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadExactCpuDataLong(uint address, ref long cycle, out uint value)
    {
        _ = cycle;
        value = ReadLongValue(address);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryWriteExactCpuDataByte(uint address, byte value, ref long cycle)
    {
        _ = cycle;
        Memory[Offset(address)] = value;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryWriteExactCpuDataWord(uint address, ushort value, ref long cycle)
    {
        _ = cycle;
        WriteWordValue(address, value);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryWriteExactCpuDataLong(uint address, uint value, ref long cycle)
    {
        _ = cycle;
        WriteLongValue(address, value);
        return true;
    }

    public bool TryGetInstructionFetchWindow(uint address, out M68kInstructionFetchWindow window)
    {
        _ = address;
        window = _window;
        return true;
    }

    public void CommitInstructionFetchWindowWord(in M68kInstructionFetchWindow window, uint address, ref long cycle)
    {
        _ = window;
        _ = address;
        _ = cycle;
    }

    public ushort ReadWordValue(uint address)
    {
        var offset = Offset(address);
        return (ushort)((Memory[offset] << 8) | Memory[Offset(address + 1)]);
    }

    public uint ReadLongValue(uint address)
        => ((uint)ReadWordValue(address) << 16) | ReadWordValue(address + 2);

    public void WriteWordValue(uint address, ushort value)
    {
        var offset = Offset(address);
        Memory[offset] = (byte)(value >> 8);
        Memory[Offset(address + 1)] = (byte)value;
    }

    public void WriteLongValue(uint address, uint value)
    {
        WriteWordValue(address, (ushort)(value >> 16));
        WriteWordValue(address + 2, (ushort)value);
    }

    private int Offset(uint address)
        => (int)(address & _addressMask);
}

internal sealed class SlotScheduleAuditWriter : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly int _limit;
    private AmigaBus? _bus;
    private int _absoluteFrame;
    private int _measuredFrame;
    private int _rowsWritten;
    private bool _limitReached;
    private bool _disposed;

    public SlotScheduleAuditWriter(string path, string workloadName, int limit)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var writeHeader = !File.Exists(fullPath) || new FileInfo(fullPath).Length == 0;
        _writer = new StreamWriter(fullPath, append: true);
        _limit = Math.Max(1, limit);
        _writer.WriteLine("# workload\t" + Sanitize(workloadName));
        _writer.WriteLine("# created\t" + DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));
        _writer.WriteLine("# path\t" + fullPath);
        _writer.WriteLine("# row_limit\t" + _limit.ToString(CultureInfo.InvariantCulture));
        if (writeHeader)
        {
            _writer.WriteLine(string.Join(
                '\t',
                "commit_sequence",
                "absolute_frame",
                "measured_frame",
                "beam_frame",
                "line",
                "hpos",
                "slot_cycle",
                "requested_cycle",
                "granted_cycle",
                "completed_cycle",
                "wait_cycles",
                "owner",
                "rw",
                "requester",
                "kind",
                "target",
                "size",
                "address",
                "source",
                "source_a",
                "source_b",
                "source_c",
                "replaced",
                "replaced_owner",
                "fixed_owner",
                "cpu_accessible"));
        }
    }

    public void Attach(AmigaBus bus)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _bus.SetSlotScheduleAuditSink(Record);
    }

    public void Detach()
    {
        _bus?.SetSlotScheduleAuditSink(null);
        _bus = null;
    }

    public void SetFrame(int absoluteFrame, int measuredFrame)
    {
        _absoluteFrame = absoluteFrame;
        _measuredFrame = measuredFrame;
    }

    public void Record(AgnusSlotScheduleAuditEntry entry)
    {
        if (_disposed)
        {
            return;
        }

        if (_rowsWritten >= _limit)
        {
            if (!_limitReached)
            {
                _limitReached = true;
                _writer.WriteLine("# limit_reached\t" + _limit.ToString(CultureInfo.InvariantCulture));
                Detach();
            }

            return;
        }

        var frameCycle = AmigaConstants.A500PalCpuCyclesPerFrame;
        var lineCycle = AmigaConstants.A500PalCpuCyclesPerRasterLine;
        var beamFrame = (int)(entry.SlotCycle / frameCycle);
        var cycleInFrame = entry.SlotCycle % frameCycle;
        if (cycleInFrame < 0)
        {
            cycleInFrame += frameCycle;
        }

        var line = (int)(cycleInFrame / lineCycle);
        var hpos = AgnusHrmOcsSlotTable.GetHorizontal(entry.SlotCycle);
        var fixedOwner = AgnusHrmOcsSlotTable.GetFixedOwner(hpos);
        var cpuAccessible = AgnusHrmOcsSlotTable.IsCpuAccessibleSlot(entry.SlotCycle);
        var waitCycles = entry.SlotCycle - entry.RequestedCycle;
        _writer.WriteLine(string.Join(
            '\t',
            entry.Sequence.ToString(CultureInfo.InvariantCulture),
            _absoluteFrame.ToString(CultureInfo.InvariantCulture),
            _measuredFrame.ToString(CultureInfo.InvariantCulture),
            beamFrame.ToString(CultureInfo.InvariantCulture),
            line.ToString(CultureInfo.InvariantCulture),
            hpos.ToString(CultureInfo.InvariantCulture),
            entry.SlotCycle.ToString(CultureInfo.InvariantCulture),
            entry.RequestedCycle.ToString(CultureInfo.InvariantCulture),
            entry.SlotCycle.ToString(CultureInfo.InvariantCulture),
            entry.CompletedCycle.ToString(CultureInfo.InvariantCulture),
            waitCycles.ToString(CultureInfo.InvariantCulture),
            entry.Owner.ToString(),
            entry.IsWrite ? "W" : "R",
            entry.Requester.ToString(),
            entry.Kind.ToString(),
            entry.Target.ToString(),
            entry.Size.ToString(),
            "0x" + entry.Address.ToString("X6", CultureInfo.InvariantCulture),
            entry.Source.ToString(),
            entry.SourceA.ToString(CultureInfo.InvariantCulture),
            entry.SourceB.ToString(CultureInfo.InvariantCulture),
            entry.SourceC.ToString(CultureInfo.InvariantCulture),
            entry.ReplacedExisting ? "1" : "0",
            entry.ReplacedOwner.ToString(),
            fixedOwner.ToString(),
            cpuAccessible ? "1" : "0"));
        _rowsWritten++;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Detach();
        _writer.WriteLine("# rows_written\t" + _rowsWritten.ToString(CultureInfo.InvariantCulture));
        _writer.Dispose();
        _disposed = true;
    }

    private static string Sanitize(string value)
        => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
