using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Buffers.Binary;
using CopperMod.Amiga;
using CopperScreen;

const int SampleRate = 44_100;
const int Channels = 2;

var options = BenchmarkOptions.Parse(args);
var allWorkloads = new[]
{
    new BenchmarkWorkload("No disk / insert screen", null),
    new BenchmarkWorkload("Superfrog CSL", "Superfrog (1993)(Team 17)(Disk 1 of 4)[cr CSL].zip"),
    new BenchmarkWorkload("Lemmings SR", "Lemmings (1991)(Psygnosis)(Disk 1 of 2)[cr SR].zip"),
    new BenchmarkWorkload("Full Contact FLT", "Full Contact (1991)(Team 17)(Disk 1 of 2)[cr FLT].zip"),
    new BenchmarkWorkload("Full Contact original single-drive", "Full Contact (1991)(Team 17)(Disk 1 of 2).zip", Profile: "expanded-kickstart13 - singledrive.json"),
    new BenchmarkWorkload("FC FLT intro", "Full Contact (1991)(Team 17)(Disk 1 of 2)[cr FLT].zip", FireFrame: 260),
    new BenchmarkWorkload("North & South CP intro", "North & South (1989)(Infogrames)(M5)[cr CP].zip", FireFrame: 260),
    new BenchmarkWorkload("Shadow of the Beast IPF", "Shadow of the Beast (1989)(Psygnosis)(US)(Disk 1 of 2).zip"),
    new BenchmarkWorkload("Workbench 1.3", "Workbench v1.3 rev 34.20 (1988)(Commodore)(A500-A2000)(Disk 1 of 2)(Workbench)[m].zip"),
};
var workloads = options.Smoke
    ? new[] { allWorkloads[0] }
    : FilterWorkloads(allWorkloads, options.Only);

if (options.DiskDivergenceTrace)
{
    Console.WriteLine($"Disk divergence trace, Warmup={options.WarmupFrames} frames, measured={options.MeasuredFrames} frames, Profile={options.Profile ?? "workload/default"}, Kickstart={FormatKickstartOption(options)}");
    foreach (var workload in workloads)
    {
        RunDiskDivergenceTrace(workload, options);
    }

    return;
}

Console.WriteLine($"Warmup={options.WarmupFrames} frames, measured={options.MeasuredFrames} frames, repeats={options.RepeatCount}, Release={IsRelease()}, Profile={options.Profile ?? "workload/default"}, Agnus=hrm, CPU={options.CpuBackend ?? "profile"}, Kickstart={FormatKickstartOption(options)}");
WriteBenchmarkHeader();

foreach (var workload in workloads)
{
    for (var repeat = 0; repeat < options.RepeatCount; repeat++)
    {
        WriteBenchmarkResult(RunBenchmark(workload, options), options);
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
            "missing",
            default,
            Array.Empty<AmigaDiskTraceEvent>(),
            "missing disk image");
    }

    var audio = new float[emulator.AudioFramesPerAppFrame(SampleRate) * Channels];
    var audioFrames = 0;
    for (var frame = 0; frame < options.WarmupFrames; frame++)
    {
        ApplyFrameActions(emulator, workload, frame);
        emulator.RenderNextFrame();
        audioFrames = emulator.RenderAudio(audio, SampleRate, Channels);
    }

    if (options.InstructionMatrix)
    {
        emulator.ResetInstructionFrequency();
        emulator.SetInstructionFrequencyEnabled(true);
    }

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

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
    var descriptorBeforeMeasured = GetDisplay(emulator).CaptureSnapshot();
    var previousDescriptorBuilds = descriptorBeforeMeasured.LastRasterlineDescriptorBuilds;
    var previousDescriptorReplayAttempts = descriptorBeforeMeasured.LastRasterlineDescriptorReplayAttempts;
    var previousDescriptorReplayedRows = descriptorBeforeMeasured.LastRasterlineDescriptorReplayedRows;
    var previousDescriptorFallbackRows = descriptorBeforeMeasured.LastRasterlineDescriptorFallbackRows;
    var previousDescriptorBitplaneRows = descriptorBeforeMeasured.LastRasterlineDescriptorBitplaneRows;
    var previousDescriptorSpriteRows = descriptorBeforeMeasured.LastRasterlineDescriptorSpriteRows;
    var previousDescriptorMismatches = descriptorBeforeMeasured.LastRasterlineDescriptorMismatches;
    var slowFramesOver20 = 0;
    var slowFramesOver33 = 0;
    var slowFramesOver40 = 0;
    var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
    var startTimestamp = Stopwatch.GetTimestamp();
    for (var frame = 0; frame < options.MeasuredFrames; frame++)
    {
        var schedulerBeforeFrame = CaptureHardwareSchedulerSnapshot(emulator);
        var frameStartTimestamp = Stopwatch.GetTimestamp();
        ApplyFrameActions(emulator, workload, options.WarmupFrames + frame);
        emulator.RenderNextFrame();
        audioFrames = emulator.RenderAudio(audio, SampleRate, Channels);
        var displayFrame = GetDisplay(emulator).CaptureSnapshot();
        measuredDescriptorBuilds += displayFrame.LastRasterlineDescriptorBuilds - previousDescriptorBuilds;
        measuredDescriptorReplayAttempts += displayFrame.LastRasterlineDescriptorReplayAttempts - previousDescriptorReplayAttempts;
        measuredDescriptorReplayedRows += displayFrame.LastRasterlineDescriptorReplayedRows - previousDescriptorReplayedRows;
        measuredDescriptorFallbackRows += displayFrame.LastRasterlineDescriptorFallbackRows - previousDescriptorFallbackRows;
        measuredDescriptorBitplaneRows += displayFrame.LastRasterlineDescriptorBitplaneRows - previousDescriptorBitplaneRows;
        measuredDescriptorSpriteRows += displayFrame.LastRasterlineDescriptorSpriteRows - previousDescriptorSpriteRows;
        measuredDescriptorMismatches += displayFrame.LastRasterlineDescriptorMismatches - previousDescriptorMismatches;
        previousDescriptorBuilds = displayFrame.LastRasterlineDescriptorBuilds;
        previousDescriptorReplayAttempts = displayFrame.LastRasterlineDescriptorReplayAttempts;
        previousDescriptorReplayedRows = displayFrame.LastRasterlineDescriptorReplayedRows;
        previousDescriptorFallbackRows = displayFrame.LastRasterlineDescriptorFallbackRows;
        previousDescriptorBitplaneRows = displayFrame.LastRasterlineDescriptorBitplaneRows;
        previousDescriptorSpriteRows = displayFrame.LastRasterlineDescriptorSpriteRows;
        previousDescriptorMismatches = displayFrame.LastRasterlineDescriptorMismatches;
        var schedulerAfterFrame = CaptureHardwareSchedulerSnapshot(emulator);
        maxFrameSchedulerDrains = Math.Max(
            maxFrameSchedulerDrains,
            schedulerAfterFrame.DrainCount - schedulerBeforeFrame.DrainCount);
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

        fakeQueuedAudioMilliseconds -= frameMilliseconds;
        fakeQueuedAudioMinMilliseconds = Math.Min(fakeQueuedAudioMinMilliseconds, fakeQueuedAudioMilliseconds);
        if (fakeQueuedAudioMilliseconds < 0.0)
        {
            fakeAudioSubmitFailures++;
            fakeQueuedAudioMilliseconds = 0.0;
            fakeQueuedAudioMinMilliseconds = 0.0;
        }

        var audioSpan = audio.AsSpan(0, Math.Min(audio.Length, audioFrames * Channels));
        if (HasActiveAudio(audioSpan))
        {
            activeAudioFrames++;
        }

        fakeQueuedAudioMilliseconds = Math.Min(
            fakeQueuedAudioLimitMilliseconds,
            fakeQueuedAudioMilliseconds + (audioFrames * 1000.0 / SampleRate));
        fakeQueuedAudioMaxMilliseconds = Math.Max(fakeQueuedAudioMaxMilliseconds, fakeQueuedAudioMilliseconds);
    }

    var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
    var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
    var fps = options.MeasuredFrames / elapsed.TotalSeconds;
    var framebufferSummary = CaptureFramebufferSummary(emulator.Framebuffer);
    var audioSummary = CaptureAudioSummary(audio.AsSpan(0, Math.Min(audio.Length, audioFrames * Channels)), audioFrames);
    var displaySummary = CaptureDisplaySummary(GetDisplay(emulator).CaptureSnapshot()) with
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
    var instructionFrequency = options.InstructionMatrix
        ? emulator.InstructionFrequency
        : M68kInstructionFrequencySnapshot.Empty;
    if (options.InstructionMatrix)
    {
        WriteHotLoopDiagnostics(workload, emulator);
        emulator.SetInstructionFrequencyEnabled(false);
    }

    var phase = options.Smoke || workload.FileName == null
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
        emulator.CpuBackendName,
        emulator.JitCounters,
        options.DiskDivergenceTrace ? CaptureDiskTrace(emulator) : Array.Empty<AmigaDiskTraceEvent>(),
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

static void ApplyFrameActions(CopperScreenEmulator emulator, BenchmarkWorkload workload, int frame)
{
    if (workload.FireFrame == frame)
    {
        emulator.PulsePrimaryFire(frames: 30);
    }
}

static void WriteBenchmarkHeader()
{
    Console.WriteLine("name\tagnus\tbackend\tframes/sec\treal-time\tms/frame\tmax ms\tslow>20\tslow>33\tslow>40\tactive audio frames\tfake audio submit failures\tfake audio min ms\tfake audio max ms\tallocated bytes\tcpu%\tagnus%\tdisplay%\tjit traces\tjit hits\tjit exits\tjit fallback\tjit invalid\tjit unsupported opcode\tjit unsupported ea\tjit host trap\tjit system\tjit exception\tjit generation\tjit boundary\tjit selfmod\tjit direct il\tjit helper il\tjit direct cpu\tjit direct mem\tjit spec helper\tjit v2 tier0\tjit v2 tier1\tjit v2 tier2\tjit v2 tier3\tjit v2 hits\tjit v2 exits\tjit v2 exit entry\tjit v2 exit branch\tjit v2 exit before\tjit v2 exit beyond\tjit v2 exit chip\tjit v2 exit host\tjit v2 exit hole\tjit v2 exit fall\tjit v2 flags\tjit v2 branch pending\tjit v2 branch sr\tjit v2 writes\tjit v2 zw read rf\tjit v2 zw read rom\tjit v2 zw read overlay\tjit v2 zw write rf\tjit v2 zw read slow\tjit v2 zw write slow\tjit v2 promotions\tjit v2 pressure promotions\tjit v2 worker graphs\tjit v2 worker graph instr\tjit v2 worker graph bytes\tjit async queued\tjit async dedup\tjit async dropped\tjit async bumps\tjit async snap avoided\tjit async started\tjit async completed\tjit async failed\tjit async installed\tjit async stale\tjit async superseded\tjit async tier0\tjit async tier1\tjit async tier2\tjit async tier3\tjit async maxq\tjit async ms\tjit v2 rejected\tjit v2 rej chip\tjit v2 rej host\tjit v2 rej dec\tjit v2 rej op\tjit v2 rej ea\tjit v2 rej budget\tjit v2 rej empty\tjit v2 disabled holes\tjit v2 disabled branches\tjit v2 branch limited\tjit v2 disabled entry\tjit v2 hole compiles\tjit v2 handoff try\tjit v2 handoff hit\tjit v2 handoff instr\tjit v2 handoff fail\tjit v2 handoff queued wait\tjit v2 bus batch\tjit v2 bus instr\tjit v2 bus saved\tjit v2 bus hist\tjit v2 bus wake\tjit v2 rej op top\tjit v2 rej ea top\tjit v2 hole top\tjit v2 handoff block top\tjit v2 handoff fail top\tjit v2 branch limit top\tjit v2 branch target state top\tjit gen guard\tjit pure batch\tjit pure instr\tjit pure saved\tjit pure exits\tjit pure hist\tjit pure wake\tjit stopped ff\tjit stopped cycles\tlive events\tcopper\tpending\tfetches\tframebuffer\taudio\tdisplay summary\tdisk\tspecialization\tretained slots\tslot grants\tgrant mix\tdenied\tdenied mix\tblocked by\tlast denied\tstatus");
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
    Console.WriteLine(
        $"{result.Workload.Name}\t{result.TimingMode}\t{result.CpuBackend}\t{result.FramesPerSecond:F1}\t{result.FramesPerSecond / 50.0:F2}x\t{result.MillisecondsPerFrame:F2}\t{timing.MaxMilliseconds:F2}\t{timing.SlowFramesOver20}\t{timing.SlowFramesOver33}\t{timing.SlowFramesOver40}\t{audioQueue.ActiveFrames}\t{audioQueue.SubmitFailures}\t{audioQueue.MinQueuedMilliseconds:F2}\t{audioQueue.MaxQueuedMilliseconds:F2}\t{result.AllocatedBytes}\t{result.Phase.CpuPercent:F1}\t{result.Phase.AgnusPercent:F1}\t{result.Phase.DisplayPercent:F1}\t{jit.CompiledTraces}\t{jit.TraceHits}\t{jit.SideExits}\t{jit.FallbackInstructions}\t{jit.Invalidations}\t{jit.UnsupportedOpcode}\t{jit.UnsupportedEa}\t{jit.HostTrapBailouts}\t{jit.SystemInstructionBailouts}\t{jit.ExceptionInstructionBailouts}\t{jit.GenerationMismatches}\t{jit.BoundarySideExits}\t{jit.SelfModifiedCodeExits}\t{jit.DirectIlInstructions}\t{jit.HelperIlInstructions}\t{jit.DirectCpuIlInstructions}\t{jit.DirectMemoryIlInstructions}\t{jit.SpecializedHelperIlInstructions}\t{jit.V2Tier0CompiledTraces}\t{jit.V2Tier1CompiledTraces}\t{jit.V2Tier2CompiledTraces}\t{jit.V2Tier3CompiledTraces}\t{jit.V2TraceHits}\t{jit.V2SideExits}\t{jit.V2SideExitEntryMismatch}\t{jit.V2SideExitOutOfBlockBranch}\t{jit.V2SideExitBeforeGraph}\t{jit.V2SideExitBeyondGraph}\t{jit.V2SideExitChipRam}\t{jit.V2SideExitHostTrap}\t{jit.V2SideExitGraphHole}\t{jit.V2SideExitConditionalFallthrough}\t{jit.V2FlagMaterializations}\t{jit.V2BranchPendingFlagChecks}\t{jit.V2BranchStatusFlagChecks}\t{jit.V2LazyWritebacks}\t{jit.V2ZeroWaitReadRealFast}\t{jit.V2ZeroWaitReadRom}\t{jit.V2ZeroWaitReadOverlay}\t{jit.V2ZeroWaitWriteRealFast}\t{jit.V2ZeroWaitReadSlow}\t{jit.V2ZeroWaitWriteSlow}\t{jit.V2TierPromotions}\t{jit.V2TierPressurePromotions}\t{jit.V2WorkerExpandedGraphs}\t{jit.V2WorkerExpandedGraphInstructions}\t{jit.V2WorkerExpandedGraphBytes}\t{jit.AsyncRequestsQueued}\t{jit.AsyncRequestsDeduped}\t{jit.AsyncRequestsDropped}\t{jit.AsyncPriorityBumps}\t{jit.AsyncSnapshotCapturesAvoided}\t{jit.AsyncWorkerCompilesStarted}\t{jit.AsyncWorkerCompilesCompleted}\t{jit.AsyncWorkerCompilesFailed}\t{jit.AsyncCompletedInstalled}\t{jit.AsyncCompletedDiscardedStale}\t{jit.AsyncCompletedDiscardedSuperseded}\t{jit.AsyncTier0Installs}\t{jit.AsyncTier1Installs}\t{jit.AsyncTier2Installs}\t{jit.AsyncTier3Installs}\t{jit.AsyncMaxQueueDepth}\t{jit.AsyncWorkerCompileMilliseconds}\t{jit.V2RejectedCandidates}\t{jit.V2RejectedChipRam}\t{jit.V2RejectedHostTrap}\t{jit.V2RejectedDecode}\t{jit.V2RejectedUnsupportedOperation}\t{jit.V2RejectedUnsupportedEa}\t{jit.V2RejectedBudget}\t{jit.V2RejectedEmpty}\t{jit.V2DisabledGraphHoleRoots}\t{jit.V2DisabledBranchExitRoots}\t{jit.V2BranchPressureLimitedRoots}\t{jit.V2DisabledEntryMismatchRoots}\t{jit.V2GraphHoleTargetCompiles}\t{jit.V2TraceHandoffAttempts}\t{jit.V2TraceHandoffExecutions}\t{jit.V2TraceHandoffInstructions}\t{jit.V2TraceHandoffFailures}\t{jit.V2TraceHandoffQueuedNotReady}\t{jit.V2BusAccessBatchExecutions}\t{jit.V2BusAccessBatchInstructions}\t{jit.V2BusAccessBatchBoundaryCallsSaved}\t{FormatCounterText(jit.V2BusAccessBatchLengthHistogram)}\t{FormatCounterText(jit.V2BusAccessBatchWakeSourceTop)}\t{FormatCounterText(jit.V2UnsupportedOperationTop)}\t{FormatCounterText(jit.V2UnsupportedEaTop)}\t{FormatCounterText(jit.V2GraphHoleTop)}\t{FormatCounterText(jit.V2TraceHandoffBlockTop)}\t{FormatCounterText(jit.V2TraceHandoffFailureTop)}\t{FormatCounterText(jit.V2BranchPressureLimitTop)}\t{FormatCounterText(jit.V2BranchPressureTargetStateTop)}\t{jit.GenerationGuardExits}\t{jit.PureTraceBatchExecutions}\t{jit.PureTraceBatchInstructions}\t{jit.PureTraceBatchBoundaryCallsSaved}\t{jit.PureTraceBatchSideExits}\t{FormatCounterText(jit.PureTraceBatchLengthHistogram)}\t{FormatCounterText(jit.PureTraceBatchWakeSourceTop)}\t{jit.StoppedFastForwards}\t{jit.StoppedFastForwardCycles}\t{result.LiveDisplayEventCount}\t{result.LiveCopperStepCount}\t{result.LivePendingWriteEventCount}\t{result.LiveFetchBatchWordCount}\t{FormatFramebufferSummary(result.Framebuffer)}\t{FormatAudioSummary(result.Audio)}\t{FormatDisplaySummary(result.Display)}\t{FormatDiskSummary(result.Disk)}\t{FormatSpecializationSummary(result.Specialization)}\t{agnus.SlotReservationCount}\t{agnus.SlotGrantCount}\t{FormatSlotGrantMix(agnus)}\t{agnus.DeniedFixedSlotCount}\t{FormatDeniedSlotMix(agnus)}\t{FormatDeniedBlockerMix(agnus)}\t{FormatLastDeniedFixedSlot(agnus)}\t{result.StatusText}");
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
}

static void WriteHotLoopDiagnostics(BenchmarkWorkload workload, CopperScreenEmulator emulator)
{
    var machine = GetMachine(emulator);
    var bus = machine.Bus;
    var a5 = machine.Cpu.State.A[5];
    var emitted = 0;
    ScanHotLoopRegion(workload, "chip", bus.ChipRam, 0, bus, a5, ref emitted);
    ScanHotLoopRegion(workload, "exp", bus.ExpansionRam, bus.ExpansionRamBase, bus, a5, ref emitted);
    ScanHotLoopRegion(workload, "real", bus.RealFastRam, bus.RealFastRamBase, bus, a5, ref emitted);
    if (emitted == 0)
    {
        Console.WriteLine($"hot-loop\t{workload.Name}\tnone");
    }
}

static void ScanHotLoopRegion(
    BenchmarkWorkload workload,
    string regionName,
    byte[] region,
    uint baseAddress,
    AmigaBus bus,
    uint a5,
    ref int emitted)
{
    const int MaxHotLoopDiagnostics = 8;
    if (region.Length < 18 || emitted >= MaxHotLoopDiagnostics)
    {
        return;
    }

    for (var offset = 0; offset <= region.Length - 18 && emitted < MaxHotLoopDiagnostics; offset += 2)
    {
        var span = region.AsSpan(offset);
        if (ReadWord(span, 0) != 0x202D ||
            ReadWord(span, 4) != 0x0280 ||
            ReadWord(span, 10) != 0xB0BC ||
            ReadWord(span, 16) != 0x66EE)
        {
            continue;
        }

        var displacement = unchecked((short)ReadWord(span, 2));
        var andMask = ReadLong(span, 6);
        var compareValue = ReadLong(span, 12);
        var pc = baseAddress + (uint)offset;
        var polledAddress = (uint)(a5 + displacement);
        var polledValue = bus.ReadHostLong(polledAddress);
        Console.WriteLine(
            $"hot-loop\t{workload.Name}\tpc=0x{pc:X6}\tregion={regionName}\ta5=0x{a5:X6}\td16={displacement}\taddr=0x{polledAddress:X6}\tvalue=0x{polledValue:X8}\tand=0x{andMask:X8}\tcmp=0x{compareValue:X8}");
        emitted++;
    }
}

static ushort ReadWord(ReadOnlySpan<byte> span, int offset)
    => BinaryPrimitives.ReadUInt16BigEndian(span.Slice(offset, 2));

static uint ReadLong(ReadOnlySpan<byte> span, int offset)
    => BinaryPrimitives.ReadUInt32BigEndian(span.Slice(offset, 4));

static string PercentText(long value, long total)
    => total == 0 ? "0.00" : $"{(value * 100.0) / total:F2}";

static string FormatCounterText(string? value)
    => string.IsNullOrEmpty(value)
        ? string.Empty
        : value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

static CopperScreenEmulator? CreateEmulator(BenchmarkWorkload workload, BenchmarkOptions options)
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
    return diskPath == null
        ? null
        : CopperScreenEmulator.Create(CreateEmulatorArgs(diskPath, options, workload.Profile), AppContext.BaseDirectory);
}

static string[] CreateEmulatorArgs(string? diskPath, BenchmarkOptions options, string? workloadProfile)
{
    var profile = options.Profile ?? workloadProfile;
    var count = (diskPath == null ? 0 : 1) +
        (string.IsNullOrWhiteSpace(profile) ? 0 : 2) +
        (options.RealKickstart ? 1 : 0) +
        (string.IsNullOrWhiteSpace(options.KickstartRomPath) ? 0 : 2) +
        (string.IsNullOrWhiteSpace(options.CpuBackend) ? 0 : 2);
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
    var renderAudioField = type.GetField("_renderFrameAudioUntil", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var applyInput = type.GetMethod("ApplyInputState", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var advancePendingDiskInsert = type.GetMethod("AdvancePendingDiskInsert", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var beginFrameAudio = type.GetMethod("BeginFrameAudio", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var finishFrameAudio = type.GetMethod("FinishFrameAudio", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var handleBootResult = type.GetMethod("HandleBootResult", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var stabilizeInterlaceFrame = type.GetMethod("StabilizeInterlaceFrame", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var advanceInputPulse = type.GetMethod("AdvanceInputPulse", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var machine = (AmigaMachine)machineField.GetValue(emulator)!;
    var boot = (AmigaBootController)bootField.GetValue(emulator)!;
    var renderAudio = (Action<long, long>)renderAudioField.GetValue(emulator)!;
    var cpuTicks = 0L;
    var agnusTicks = 0L;
    var displayTicks = 0L;
    var otherTicks = 0L;
    var totalStart = Stopwatch.GetTimestamp();

    for (var frame = 0; frame < frames; frame++)
    {
        var phaseStart = Stopwatch.GetTimestamp();
        applyInput.Invoke(emulator, null);
        advancePendingDiskInsert.Invoke(emulator, null);
        var targetCycle = (long)targetCycleField.GetValue(emulator)! + AmigaConstants.A500PalCpuCyclesPerFrame;
        targetCycleField.SetValue(emulator, targetCycle);
        beginFrameAudio.Invoke(emulator, new object[] { targetCycle });
        var liveAgnus = machine.Bus.LiveAgnusDmaEnabled;
        if (liveAgnus)
        {
            machine.Bus.SetLiveAgnusDmaEnabled(false);
        }

        otherTicks += Stopwatch.GetTimestamp() - phaseStart;

        phaseStart = Stopwatch.GetTimestamp();
        var result = boot.ContinueExecutionUntilCycle(targetCycle, maxInstructions: 100_000, renderAudio);
        cpuTicks += Stopwatch.GetTimestamp() - phaseStart;

        phaseStart = Stopwatch.GetTimestamp();
        if (liveAgnus)
        {
            machine.Bus.SetLiveAgnusDmaEnabled(true);
        }

        finishFrameAudio.Invoke(emulator, null);
        var stopped = (bool)handleBootResult.Invoke(emulator, new object[] { result })!;
        otherTicks += Stopwatch.GetTimestamp() - phaseStart;
        if (stopped)
        {
            continue;
        }

        machine.Bus.AdvanceRasterTo(targetCycle);
        machine.Bus.AdvanceCiasTo(targetCycle);
        machine.Bus.Paula.AdvanceTo(targetCycle);
        machine.Bus.Disk.AdvanceTo(targetCycle);

        phaseStart = Stopwatch.GetTimestamp();
        if (machine.Bus.LiveAgnusDmaEnabled)
        {
            machine.Bus.Agnus.AdvanceTo(targetCycle);
        }

        agnusTicks += Stopwatch.GetTimestamp() - phaseStart;
        machine.Bus.Blitter.AdvanceTo(targetCycle);
        machine.Bus.Paula.AdvanceTo(targetCycle);

        phaseStart = Stopwatch.GetTimestamp();
        machine.Bus.Display.RenderFrame(
            MemoryMarshal.Cast<int, uint>(emulator.Framebuffer.AsSpan()),
            targetCycle - AmigaConstants.A500PalCpuCyclesPerFrame,
            targetCycle);
        stabilizeInterlaceFrame.Invoke(emulator, null);
        displayTicks += Stopwatch.GetTimestamp() - phaseStart;

        phaseStart = Stopwatch.GetTimestamp();
        advanceInputPulse.Invoke(emulator, null);
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

static AmigaMachine GetMachine(CopperScreenEmulator emulator)
{
    return (AmigaMachine)typeof(CopperScreenEmulator)
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
    var machine = (AmigaMachine)typeof(CopperScreenEmulator)
        .GetField("_machine", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(emulator)!;
    return machine.Bus.Disk.CaptureDivergenceTrace();
}

static HardwareSpecializationSummary CaptureSpecializationSummary(CopperScreenEmulator emulator)
{
    var machine = (AmigaMachine)typeof(CopperScreenEmulator)
        .GetField("_machine", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(emulator)!;
    var blitter = machine.Bus.Blitter.CaptureSnapshot().SpecializationCounters;
    var disk = machine.Bus.Disk.CaptureSnapshot().SpecializationCounters;
    return new HardwareSpecializationSummary(
        blitter.KernelHits,
        blitter.KernelMisses,
        blitter.GeneratedKernels,
        blitter.ScalarFallbacks,
        disk.PreparedTrackHits,
        disk.PreparedTrackMisses,
        disk.RollingWindowHits,
        disk.RollingWindowMisses,
        disk.SyncIndexHits,
        disk.SyncIndexMisses);
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

static DisplaySummary CaptureDisplaySummary(OcsDisplaySnapshot display)
{
    return new DisplaySummary(
        display.Bplcon0,
        display.Bplcon1,
        display.Bplcon2,
        display.LastBitplaneNonZeroPixels,
        display.LastBitplaneMinX,
        display.LastBitplaneMinY,
        display.LastBitplaneMaxX,
        display.LastBitplaneMaxY,
        display.LastSpriteNonZeroPixels,
        display.LastSpriteMinX,
        display.LastSpriteMinY,
        display.LastSpriteMaxX,
        display.LastSpriteMaxY,
        display.LastBitplaneDmaFetches,
        display.LastSpriteDmaFetches,
        display.LastMissedSpriteDmaSlots,
        display.LastRasterlineDescriptorBuilds,
        display.LastRasterlineDescriptorReplayAttempts,
        display.LastRasterlineDescriptorReplayedRows,
        display.LastRasterlineDescriptorFallbackRows,
        display.LastRasterlineDescriptorBitplaneRows,
        display.LastRasterlineDescriptorSpriteRows,
        display.LastRasterlineDescriptorMismatches);
}

static string FormatFramebufferSummary(FramebufferSummary summary)
    => $"{summary.NonBlackPixels}/{summary.DistinctColors}/0x{summary.Checksum:X8}";

static string FormatAudioSummary(AudioSummary summary)
    => $"{summary.Frames}/{summary.NonZeroSamples}/{summary.Peak:F4}/0x{summary.Checksum:X8}";

static string FormatStatusWithScheduler(BenchmarkRunResult result)
{
    var scheduler = result.Scheduler;
    return $"{FormatCounterText(result.StatusText)} | scheduler last={scheduler.LastDrainCycle}, drains={scheduler.DrainCount}, max-frame-drains={result.MaxFrameSchedulerDrains}, bus-drains={scheduler.BusAccessDrainCount}, same-cycle={scheduler.SameCycleDrainCount}, line-cache=hit:{scheduler.RasterlineCacheHits},miss:{scheduler.RasterlineCacheMisses},rebuild:{scheduler.RasterlineCacheRebuilds},inv:{scheduler.RasterlineCacheInvalidations}, events=raster:{scheduler.RasterEvents},cia:{scheduler.CiaEvents},paula:{scheduler.PaulaEvents},disk:{scheduler.DiskEvents},agnus:{scheduler.AgnusEvents},blitter:{scheduler.BlitterEvents}";
}

static string FormatDisplaySummary(DisplaySummary summary)
{
    return $"bpl={summary.BitplanePixels}:{summary.BitplaneMinX},{summary.BitplaneMinY}-{summary.BitplaneMaxX},{summary.BitplaneMaxY}," +
        $"spr={summary.SpritePixels}:{summary.SpriteMinX},{summary.SpriteMinY}-{summary.SpriteMaxX},{summary.SpriteMaxY}," +
        $"dma={summary.BitplaneDmaFetches}/{summary.SpriteDmaFetches},missedSpr={summary.MissedSpriteSlots}," +
        $"desc={summary.DescriptorBuilds}/{summary.DescriptorReplayAttempts}/{summary.DescriptorReplayedRows}/{summary.DescriptorFallbackRows}," +
        $"descRows={summary.DescriptorBitplaneRows}/{summary.DescriptorSpriteRows},descMis={summary.DescriptorMismatches}," +
        $"bplcon={summary.Bplcon0:X4}/{summary.Bplcon1:X4}/{summary.Bplcon2:X4}";
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

static string FormatSpecializationSummary(HardwareSpecializationSummary summary)
{
    return $"blt={summary.BlitterKernelHits}/{summary.BlitterKernelMisses}/{summary.BlitterGeneratedKernels}/{summary.BlitterFallbacks}," +
        $"dskPrep={summary.DiskPreparedTrackHits}/{summary.DiskPreparedTrackMisses}," +
        $"dskWin={summary.DiskRollingWindowHits}/{summary.DiskRollingWindowMisses}," +
        $"dskSync={summary.DiskSyncIndexHits}/{summary.DiskSyncIndexMisses}";
}

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

internal readonly record struct BenchmarkWorkload(string Name, string? FileName, int FireFrame = -1, string? Profile = null);

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
    int DescriptorMismatches);

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
    long DiskPreparedTrackHits,
    long DiskPreparedTrackMisses,
    long DiskRollingWindowHits,
    long DiskRollingWindowMisses,
    long DiskSyncIndexHits,
    long DiskSyncIndexMisses);

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
    string CpuBackend,
    M68kJitCounters Jit,
    AmigaDiskTraceEvent[] DiskTrace,
    string StatusText);

internal readonly record struct BenchmarkOptions(
    bool Smoke,
    int WarmupFrames,
    int MeasuredFrames,
    int RepeatCount,
    string? Only,
    string? Profile,
    string? CpuBackend,
    bool RealKickstart,
    string? KickstartRomPath,
    bool InstructionMatrix,
    bool DiskDivergenceTrace,
    int TopInstructionOpcodes)
{
    public static BenchmarkOptions Parse(string[] args)
    {
        var smoke = args.Contains("--smoke", StringComparer.OrdinalIgnoreCase);
        var warmup = smoke ? 20 : 240;
        var measured = smoke ? 20 : 360;
        var repeatCount = 1;
        var instructionMatrix = false;
        var diskDivergenceTrace = false;
        var topInstructionOpcodes = 16;
        string? only = null;
        string? profile = null;
        string? cpuBackend = null;
        var realKickstart = false;
        string? kickstartRomPath = null;
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
            else if (string.Equals(args[i], "--top-opcodes", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                instructionMatrix = true;
                _ = int.TryParse(args[++i], out topInstructionOpcodes);
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
            realKickstart,
            kickstartRomPath,
            instructionMatrix,
            diskDivergenceTrace,
            Math.Clamp(topInstructionOpcodes, 0, 256));
    }
}
