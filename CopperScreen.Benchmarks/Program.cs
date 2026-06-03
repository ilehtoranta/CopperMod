using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
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
    new BenchmarkWorkload("FC FLT intro", "Full Contact (1991)(Team 17)(Disk 1 of 2)[cr FLT].zip", FireFrame: 260),
    new BenchmarkWorkload("Shadow of the Beast IPF", "Shadow of the Beast (1989)(Psygnosis)(US)(Disk 1 of 2).zip"),
    new BenchmarkWorkload("Workbench 1.3", "Workbench v1.3 rev 34.20 (1988)(Commodore)(A500-A2000)(Disk 1 of 2)(Workbench)[m].zip"),
};
var workloads = options.Smoke
    ? new[] { allWorkloads[0] }
    : FilterWorkloads(allWorkloads, options.Only);

if (options.CompareAgnus)
{
    Console.WriteLine($"Warmup={options.WarmupFrames} frames, measured={options.MeasuredFrames} frames, repeats={FormatCompareRepeatCount(options)}, Release={IsRelease()}, Agnus=compare legacy-vs-slot");
    WriteBenchmarkHeader();
    WriteComparisonHeader();
    foreach (var workload in workloads)
    {
        if (options.RepeatCount <= 1)
        {
            var legacy = RunBenchmark(workload, "legacy", options);
            WriteBenchmarkResult(legacy, options);
            var slot = RunBenchmark(workload, "slot", options);
            WriteBenchmarkResult(slot, options);
            WriteComparison(legacy, slot);
        }
        else
        {
            WriteRepeatedComparison(workload, options);
        }
    }

    return;
}

Console.WriteLine($"Warmup={options.WarmupFrames} frames, measured={options.MeasuredFrames} frames, repeats={options.RepeatCount}, Release={IsRelease()}, Profile={options.Profile ?? "default"}, Agnus={options.AgnusTimingMode ?? "profile"}, CPU={options.CpuBackend ?? "profile"}, Kickstart={FormatKickstartOption(options)}");
WriteBenchmarkHeader();

foreach (var workload in workloads)
{
    for (var repeat = 0; repeat < options.RepeatCount; repeat++)
    {
        WriteBenchmarkResult(RunBenchmark(workload, options.AgnusTimingMode, options), options);
    }
}

static BenchmarkRunResult RunBenchmark(BenchmarkWorkload workload, string? agnusTimingMode, BenchmarkOptions options)
{
    var timingMode = agnusTimingMode ?? "profile";
    var emulator = CreateEmulator(workload.FileName, agnusTimingMode, options);
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
            M68kInstructionFrequencySnapshot.Empty,
            "missing",
            default,
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

    var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
    var startTimestamp = Stopwatch.GetTimestamp();
    for (var frame = 0; frame < options.MeasuredFrames; frame++)
    {
        ApplyFrameActions(emulator, workload, options.WarmupFrames + frame);
        emulator.RenderNextFrame();
        audioFrames = emulator.RenderAudio(audio, SampleRate, Channels);
    }

    var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
    var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
    var fps = options.MeasuredFrames / elapsed.TotalSeconds;
    var framebufferSummary = CaptureFramebufferSummary(emulator.Framebuffer);
    var audioSummary = CaptureAudioSummary(audio.AsSpan(0, Math.Min(audio.Length, audioFrames * Channels)), audioFrames);
    var displaySummary = CaptureDisplaySummary(GetDisplay(emulator).CaptureSnapshot());
    var diskSummary = CaptureDiskSummary(emulator);
    var specializationSummary = CaptureSpecializationSummary(emulator);
    var instructionFrequency = options.InstructionMatrix
        ? emulator.InstructionFrequency
        : M68kInstructionFrequencySnapshot.Empty;
    if (options.InstructionMatrix)
    {
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
        instructionFrequency,
        emulator.CpuBackendName,
        emulator.JitCounters,
        CaptureStatusText(emulator));
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
    Console.WriteLine("name\tagnus\tbackend\tframes/sec\treal-time\tms/frame\tallocated bytes\tcpu%\tagnus%\tdisplay%\tjit traces\tjit hits\tjit exits\tjit fallback\tjit invalid\tjit unsupported opcode\tjit unsupported ea\tjit host trap\tjit system\tjit exception\tjit generation\tjit boundary\tjit selfmod\tjit direct il\tjit helper il\tjit direct cpu\tjit direct mem\tjit spec helper\tjit v2 tier0\tjit v2 tier1\tjit v2 tier2\tjit v2 tier3\tjit v2 hits\tjit v2 exits\tjit v2 exit entry\tjit v2 exit branch\tjit v2 exit before\tjit v2 exit beyond\tjit v2 exit chip\tjit v2 exit host\tjit v2 exit hole\tjit v2 exit fall\tjit v2 flags\tjit v2 writes\tjit v2 promotions\tjit v2 pressure promotions\tjit v2 worker graphs\tjit v2 worker graph instr\tjit v2 worker graph bytes\tjit async queued\tjit async dedup\tjit async dropped\tjit async started\tjit async completed\tjit async failed\tjit async installed\tjit async stale\tjit async superseded\tjit async tier0\tjit async tier1\tjit async tier2\tjit async tier3\tjit async maxq\tjit async ms\tjit v2 rejected\tjit v2 rej chip\tjit v2 rej host\tjit v2 rej dec\tjit v2 rej op\tjit v2 rej ea\tjit v2 rej budget\tjit v2 rej empty\tjit v2 disabled holes\tjit v2 disabled branches\tjit v2 branch limited\tjit v2 disabled entry\tjit v2 hole compiles\tjit v2 handoff try\tjit v2 handoff hit\tjit v2 handoff instr\tjit v2 handoff fail\tjit v2 bus batch\tjit v2 bus instr\tjit v2 bus saved\tjit v2 bus hist\tjit v2 bus wake\tjit v2 rej op top\tjit v2 rej ea top\tjit v2 hole top\tjit v2 handoff block top\tjit v2 handoff fail top\tjit v2 branch limit top\tjit v2 branch target state top\tjit gen guard\tjit pure batch\tjit pure instr\tjit pure saved\tjit pure exits\tjit pure hist\tjit pure wake\tjit stopped ff\tjit stopped cycles\tlive events\tcopper\tpending\tfetches\tframebuffer\taudio\tdisplay summary\tdisk\tspecialization\tretained slots\tslot grants\tgrant mix\tdenied\tdenied mix\tblocked by\tlast denied\tdivergences\tlast divergence\tstatus");
}

static void WriteBenchmarkResult(BenchmarkRunResult result, BenchmarkOptions options)
{
    if (result.Missing)
    {
        Console.WriteLine($"{result.Workload.Name}\t{result.TimingMode}\t{result.CpuBackend}\tmissing{new string('\t', 108)}{result.StatusText}");
        if (options.InstructionMatrix)
        {
            WriteInstructionMatrix(result, options.TopInstructionOpcodes);
        }

        return;
    }

    var agnus = result.Agnus;
    var jit = result.Jit;
    Console.WriteLine(
        $"{result.Workload.Name}\t{result.TimingMode}\t{result.CpuBackend}\t{result.FramesPerSecond:F1}\t{result.FramesPerSecond / 50.0:F2}x\t{result.MillisecondsPerFrame:F2}\t{result.AllocatedBytes}\t{result.Phase.CpuPercent:F1}\t{result.Phase.AgnusPercent:F1}\t{result.Phase.DisplayPercent:F1}\t{jit.CompiledTraces}\t{jit.TraceHits}\t{jit.SideExits}\t{jit.FallbackInstructions}\t{jit.Invalidations}\t{jit.UnsupportedOpcode}\t{jit.UnsupportedEa}\t{jit.HostTrapBailouts}\t{jit.SystemInstructionBailouts}\t{jit.ExceptionInstructionBailouts}\t{jit.GenerationMismatches}\t{jit.BoundarySideExits}\t{jit.SelfModifiedCodeExits}\t{jit.DirectIlInstructions}\t{jit.HelperIlInstructions}\t{jit.DirectCpuIlInstructions}\t{jit.DirectMemoryIlInstructions}\t{jit.SpecializedHelperIlInstructions}\t{jit.V2Tier0CompiledTraces}\t{jit.V2Tier1CompiledTraces}\t{jit.V2Tier2CompiledTraces}\t{jit.V2Tier3CompiledTraces}\t{jit.V2TraceHits}\t{jit.V2SideExits}\t{jit.V2SideExitEntryMismatch}\t{jit.V2SideExitOutOfBlockBranch}\t{jit.V2SideExitBeforeGraph}\t{jit.V2SideExitBeyondGraph}\t{jit.V2SideExitChipRam}\t{jit.V2SideExitHostTrap}\t{jit.V2SideExitGraphHole}\t{jit.V2SideExitConditionalFallthrough}\t{jit.V2FlagMaterializations}\t{jit.V2LazyWritebacks}\t{jit.V2TierPromotions}\t{jit.V2TierPressurePromotions}\t{jit.V2WorkerExpandedGraphs}\t{jit.V2WorkerExpandedGraphInstructions}\t{jit.V2WorkerExpandedGraphBytes}\t{jit.AsyncRequestsQueued}\t{jit.AsyncRequestsDeduped}\t{jit.AsyncRequestsDropped}\t{jit.AsyncWorkerCompilesStarted}\t{jit.AsyncWorkerCompilesCompleted}\t{jit.AsyncWorkerCompilesFailed}\t{jit.AsyncCompletedInstalled}\t{jit.AsyncCompletedDiscardedStale}\t{jit.AsyncCompletedDiscardedSuperseded}\t{jit.AsyncTier0Installs}\t{jit.AsyncTier1Installs}\t{jit.AsyncTier2Installs}\t{jit.AsyncTier3Installs}\t{jit.AsyncMaxQueueDepth}\t{jit.AsyncWorkerCompileMilliseconds}\t{jit.V2RejectedCandidates}\t{jit.V2RejectedChipRam}\t{jit.V2RejectedHostTrap}\t{jit.V2RejectedDecode}\t{jit.V2RejectedUnsupportedOperation}\t{jit.V2RejectedUnsupportedEa}\t{jit.V2RejectedBudget}\t{jit.V2RejectedEmpty}\t{jit.V2DisabledGraphHoleRoots}\t{jit.V2DisabledBranchExitRoots}\t{jit.V2BranchPressureLimitedRoots}\t{jit.V2DisabledEntryMismatchRoots}\t{jit.V2GraphHoleTargetCompiles}\t{jit.V2TraceHandoffAttempts}\t{jit.V2TraceHandoffExecutions}\t{jit.V2TraceHandoffInstructions}\t{jit.V2TraceHandoffFailures}\t{jit.V2BusAccessBatchExecutions}\t{jit.V2BusAccessBatchInstructions}\t{jit.V2BusAccessBatchBoundaryCallsSaved}\t{FormatCounterText(jit.V2BusAccessBatchLengthHistogram)}\t{FormatCounterText(jit.V2BusAccessBatchWakeSourceTop)}\t{FormatCounterText(jit.V2UnsupportedOperationTop)}\t{FormatCounterText(jit.V2UnsupportedEaTop)}\t{FormatCounterText(jit.V2GraphHoleTop)}\t{FormatCounterText(jit.V2TraceHandoffBlockTop)}\t{FormatCounterText(jit.V2TraceHandoffFailureTop)}\t{FormatCounterText(jit.V2BranchPressureLimitTop)}\t{FormatCounterText(jit.V2BranchPressureTargetStateTop)}\t{jit.GenerationGuardExits}\t{jit.PureTraceBatchExecutions}\t{jit.PureTraceBatchInstructions}\t{jit.PureTraceBatchBoundaryCallsSaved}\t{jit.PureTraceBatchSideExits}\t{FormatCounterText(jit.PureTraceBatchLengthHistogram)}\t{FormatCounterText(jit.PureTraceBatchWakeSourceTop)}\t{jit.StoppedFastForwards}\t{jit.StoppedFastForwardCycles}\t{result.LiveDisplayEventCount}\t{result.LiveCopperStepCount}\t{result.LivePendingWriteEventCount}\t{result.LiveFetchBatchWordCount}\t{FormatFramebufferSummary(result.Framebuffer)}\t{FormatAudioSummary(result.Audio)}\t{FormatDisplaySummary(result.Display)}\t{FormatDiskSummary(result.Disk)}\t{FormatSpecializationSummary(result.Specialization)}\t{agnus.SlotReservationCount}\t{agnus.SlotGrantCount}\t{FormatSlotGrantMix(agnus)}\t{agnus.DeniedFixedSlotCount}\t{FormatDeniedSlotMix(agnus)}\t{FormatDeniedBlockerMix(agnus)}\t{FormatLastDeniedFixedSlot(agnus)}\t{agnus.DivergenceCount}\t{FormatLastDivergence(agnus.LastDivergence)}\t{result.StatusText}");
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

static void WriteComparisonHeader()
{
    Console.WriteLine("comparison\tlegacy fps\tslot fps\tfps delta%\tlegacy alloc\tslot alloc\talloc delta\tlegacy agnus ms/frame\tslot agnus ms/frame\tagnus delta%\tframebuffer\taudio\tdisplay\tdisk\tlegacy denied\tslot denied\tslot denied mix\tslot blocked by\tgate");
}

static void WriteComparison(BenchmarkRunResult legacy, BenchmarkRunResult slot)
{
    if (legacy.Missing || slot.Missing)
    {
        Console.WriteLine($"{legacy.Workload.Name}\tmissing\tmissing{new string('\t', 16)}blocked:missing");
        return;
    }

    var agnusDelta = DeltaPercent(slot.Phase.AgnusMillisecondsPerFrame, legacy.Phase.AgnusMillisecondsPerFrame);
    Console.WriteLine(
        $"{legacy.Workload.Name}\t{legacy.FramesPerSecond:F1}\t{slot.FramesPerSecond:F1}\t{DeltaPercent(slot.FramesPerSecond, legacy.FramesPerSecond):F1}\t{legacy.AllocatedBytes}\t{slot.AllocatedBytes}\t{slot.AllocatedBytes - legacy.AllocatedBytes}\t{legacy.Phase.AgnusMillisecondsPerFrame:F4}\t{slot.Phase.AgnusMillisecondsPerFrame:F4}\t{agnusDelta:F1}\t{CompareFramebufferSummary(legacy, slot)}\t{CompareAudioSummary(legacy, slot)}\t{CompareDisplaySummary(legacy, slot)}\t{CompareDiskSummary(legacy, slot)}\t{legacy.Agnus.DeniedFixedSlotCount}\t{slot.Agnus.DeniedFixedSlotCount}\t{FormatDeniedSlotMix(slot.Agnus)}\t{FormatDeniedBlockerMix(slot.Agnus)}\t{BuildGateStatus(legacy, slot)}");
}

static void WriteRepeatedComparison(BenchmarkWorkload workload, BenchmarkOptions options)
{
    var repeatCount = GetBalancedCompareRepeatCount(options);
    var legacyRuns = new BenchmarkRunResult[repeatCount];
    var slotRuns = new BenchmarkRunResult[repeatCount];
    for (var repeat = 0; repeat < repeatCount; repeat++)
    {
        if ((repeat & 1) == 0)
        {
            legacyRuns[repeat] = RunBenchmark(workload, "legacy", options);
            slotRuns[repeat] = RunBenchmark(workload, "slot", options);
        }
        else
        {
            slotRuns[repeat] = RunBenchmark(workload, "slot", options);
            legacyRuns[repeat] = RunBenchmark(workload, "legacy", options);
        }
    }

    if (legacyRuns.Any(run => run.Missing) || slotRuns.Any(run => run.Missing))
    {
        Console.WriteLine($"{workload.Name} median({repeatCount})\tmissing\tmissing{new string('\t', 16)}blocked:missing");
        return;
    }

    var legacyFps = Median(legacyRuns, run => run.FramesPerSecond);
    var slotFps = Median(slotRuns, run => run.FramesPerSecond);
    var legacyAlloc = MaxLong(legacyRuns, run => run.AllocatedBytes);
    var slotAlloc = MaxLong(slotRuns, run => run.AllocatedBytes);
    var legacyAgnus = Median(legacyRuns, run => run.Phase.AgnusMillisecondsPerFrame);
    var slotAgnus = Median(slotRuns, run => run.Phase.AgnusMillisecondsPerFrame);
    var legacyDenied = MaxInt(legacyRuns, run => run.Agnus.DeniedFixedSlotCount);
    var slotDenied = MaxInt(slotRuns, run => run.Agnus.DeniedFixedSlotCount);
    var slotDiagnosticRun = SelectMostDeniedRun(slotRuns);
    var legacyDiagnosticRun = SelectMedianFpsRun(legacyRuns, legacyFps);
    if (slotDiagnosticRun.Agnus.DeniedFixedSlotCount == 0)
    {
        slotDiagnosticRun = SelectMedianFpsRun(slotRuns, slotFps);
    }

    Console.WriteLine(
        $"{workload.Name} median({repeatCount})\t{legacyFps:F1}\t{slotFps:F1}\t{DeltaPercent(slotFps, legacyFps):F1}\t{legacyAlloc}\t{slotAlloc}\t{slotAlloc - legacyAlloc}\t{legacyAgnus:F4}\t{slotAgnus:F4}\t{DeltaPercent(slotAgnus, legacyAgnus):F1}\t{CompareFramebufferSummary(legacyDiagnosticRun, slotDiagnosticRun)}\t{CompareAudioSummary(legacyDiagnosticRun, slotDiagnosticRun)}\t{CompareDisplaySummary(legacyDiagnosticRun, slotDiagnosticRun)}\t{CompareDiskSummary(legacyDiagnosticRun, slotDiagnosticRun)}\t{legacyDenied}\t{slotDenied}\t{FormatDeniedSlotMix(slotDiagnosticRun.Agnus)}\t{FormatDeniedBlockerMix(slotDiagnosticRun.Agnus)}\t{BuildRepeatedGateStatus(legacyFps, slotFps, legacyAlloc, slotAlloc, legacyAgnus, slotAgnus, legacyRuns, slotRuns)}");
}

static string FormatCompareRepeatCount(BenchmarkOptions options)
{
    var repeatCount = GetBalancedCompareRepeatCount(options);
    return repeatCount == options.RepeatCount
        ? repeatCount.ToString()
        : $"{repeatCount} (balanced from {options.RepeatCount})";
}

static int GetBalancedCompareRepeatCount(BenchmarkOptions options)
{
    return options.RepeatCount > 1 && (options.RepeatCount & 1) != 0
        ? options.RepeatCount + 1
        : options.RepeatCount;
}

static string BuildGateStatus(BenchmarkRunResult legacy, BenchmarkRunResult slot)
{
    var status = string.Empty;
    if (slot.FramesPerSecond < legacy.FramesPerSecond)
    {
        AppendGateFailure(ref status, "fps");
    }

    if (slot.AllocatedBytes > legacy.AllocatedBytes)
    {
        AppendGateFailure(ref status, "alloc");
    }

    if (slot.Phase.AgnusMillisecondsPerFrame > legacy.Phase.AgnusMillisecondsPerFrame)
    {
        AppendGateFailure(ref status, "agnus");
    }

    AppendSummaryGateFailures(ref status, legacy, slot);

    return string.IsNullOrEmpty(status) ? "pass" : $"fail:{status}";
}

static string BuildRepeatedGateStatus(
    double legacyFps,
    double slotFps,
    long legacyAllocatedBytes,
    long slotAllocatedBytes,
    double legacyAgnusMilliseconds,
    double slotAgnusMilliseconds,
    BenchmarkRunResult[] legacyRuns,
    BenchmarkRunResult[] slotRuns)
{
    var status = string.Empty;
    if (slotFps < legacyFps)
    {
        AppendGateFailure(ref status, "fps");
    }

    if (slotAllocatedBytes > legacyAllocatedBytes)
    {
        AppendGateFailure(ref status, "alloc");
    }

    if (slotAgnusMilliseconds > legacyAgnusMilliseconds)
    {
        AppendGateFailure(ref status, "agnus");
    }

    AppendRepeatedSummaryGateFailures(ref status, legacyRuns, slotRuns);

    return string.IsNullOrEmpty(status) ? "pass" : $"fail:{status}";
}

static void AppendSummaryGateFailures(ref string status, BenchmarkRunResult legacy, BenchmarkRunResult slot)
{
    if (!legacy.Framebuffer.Equals(slot.Framebuffer))
    {
        AppendGateFailure(ref status, "framebuffer");
    }

    if (!legacy.Audio.Equals(slot.Audio))
    {
        AppendGateFailure(ref status, "audio");
    }

    if (!DisplayEndpointsEqual(legacy.Display, slot.Display))
    {
        AppendGateFailure(ref status, "display");
    }

    if (!legacy.Disk.Equals(slot.Disk))
    {
        AppendGateFailure(ref status, "disk");
    }
}

static void AppendRepeatedSummaryGateFailures(
    ref string status,
    BenchmarkRunResult[] legacyRuns,
    BenchmarkRunResult[] slotRuns)
{
    if (SummaryDiffersAcrossRuns(legacyRuns, slotRuns, run => run.Framebuffer))
    {
        AppendGateFailure(ref status, "framebuffer");
    }

    if (SummaryDiffersAcrossRuns(legacyRuns, slotRuns, run => run.Audio))
    {
        AppendGateFailure(ref status, "audio");
    }

    if (DisplaySummaryDiffersAcrossRuns(legacyRuns, slotRuns))
    {
        AppendGateFailure(ref status, "display");
    }

    if (SummaryDiffersAcrossRuns(legacyRuns, slotRuns, run => run.Disk))
    {
        AppendGateFailure(ref status, "disk");
    }
}

static bool SummaryDiffersAcrossRuns<T>(
    BenchmarkRunResult[] legacyRuns,
    BenchmarkRunResult[] slotRuns,
    Func<BenchmarkRunResult, T> selector)
{
    var expected = selector(legacyRuns[0]);
    var comparer = EqualityComparer<T>.Default;
    for (var i = 0; i < legacyRuns.Length; i++)
    {
        if (!comparer.Equals(selector(legacyRuns[i]), expected))
        {
            return true;
        }
    }

    for (var i = 0; i < slotRuns.Length; i++)
    {
        if (!comparer.Equals(selector(slotRuns[i]), expected))
        {
            return true;
        }
    }

    return false;
}

static bool DisplaySummaryDiffersAcrossRuns(
    BenchmarkRunResult[] legacyRuns,
    BenchmarkRunResult[] slotRuns)
{
    var expected = legacyRuns[0].Display;
    for (var i = 0; i < legacyRuns.Length; i++)
    {
        if (!DisplayEndpointsEqual(legacyRuns[i].Display, expected))
        {
            return true;
        }
    }

    for (var i = 0; i < slotRuns.Length; i++)
    {
        if (!DisplayEndpointsEqual(slotRuns[i].Display, expected))
        {
            return true;
        }
    }

    return false;
}

static bool DisplayEndpointsEqual(DisplaySummary left, DisplaySummary right)
{
    return left.Bplcon0 == right.Bplcon0 &&
        left.Bplcon1 == right.Bplcon1 &&
        left.Bplcon2 == right.Bplcon2 &&
        left.BitplanePixels == right.BitplanePixels &&
        left.BitplaneMinX == right.BitplaneMinX &&
        left.BitplaneMinY == right.BitplaneMinY &&
        left.BitplaneMaxX == right.BitplaneMaxX &&
        left.BitplaneMaxY == right.BitplaneMaxY &&
        left.SpritePixels == right.SpritePixels &&
        left.SpriteMinX == right.SpriteMinX &&
        left.SpriteMinY == right.SpriteMinY &&
        left.SpriteMaxX == right.SpriteMaxX &&
        left.SpriteMaxY == right.SpriteMaxY &&
        left.BitplaneDmaFetches == right.BitplaneDmaFetches &&
        left.SpriteDmaFetches == right.SpriteDmaFetches;
}

static void AppendGateFailure(ref string status, string failure)
{
    status = string.IsNullOrEmpty(status)
        ? failure
        : $"{status},{failure}";
}

static double DeltaPercent(double value, double baseline)
{
    return baseline == 0
        ? 0
        : ((value - baseline) * 100.0) / baseline;
}

static string PercentText(long value, long total)
    => total == 0 ? "0.00" : $"{(value * 100.0) / total:F2}";

static string FormatCounterText(string? value)
    => string.IsNullOrEmpty(value)
        ? string.Empty
        : value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

static double Median(BenchmarkRunResult[] runs, Func<BenchmarkRunResult, double> selector)
{
    var values = new double[runs.Length];
    for (var i = 0; i < runs.Length; i++)
    {
        values[i] = selector(runs[i]);
    }

    Array.Sort(values);
    var middle = values.Length / 2;
    return (values.Length & 1) != 0
        ? values[middle]
        : (values[middle - 1] + values[middle]) / 2.0;
}

static long MaxLong(BenchmarkRunResult[] runs, Func<BenchmarkRunResult, long> selector)
{
    var max = long.MinValue;
    for (var i = 0; i < runs.Length; i++)
    {
        max = Math.Max(max, selector(runs[i]));
    }

    return max;
}

static int MaxInt(BenchmarkRunResult[] runs, Func<BenchmarkRunResult, int> selector)
{
    var max = int.MinValue;
    for (var i = 0; i < runs.Length; i++)
    {
        max = Math.Max(max, selector(runs[i]));
    }

    return max;
}

static BenchmarkRunResult SelectMostDeniedRun(BenchmarkRunResult[] runs)
{
    var selected = runs[0];
    for (var i = 1; i < runs.Length; i++)
    {
        if (runs[i].Agnus.DeniedFixedSlotCount > selected.Agnus.DeniedFixedSlotCount)
        {
            selected = runs[i];
        }
    }

    return selected;
}

static BenchmarkRunResult SelectMedianFpsRun(BenchmarkRunResult[] runs, double medianFps)
{
    var selected = runs[0];
    var selectedDistance = Math.Abs(selected.FramesPerSecond - medianFps);
    for (var i = 1; i < runs.Length; i++)
    {
        var distance = Math.Abs(runs[i].FramesPerSecond - medianFps);
        if (distance < selectedDistance)
        {
            selected = runs[i];
            selectedDistance = distance;
        }
    }

    return selected;
}

static CopperScreenEmulator? CreateEmulator(string? fileName, string? agnusTimingMode, BenchmarkOptions options)
{
    var args = CreateEmulatorArgs(fileName, agnusTimingMode, options);
    if (fileName == null)
    {
        return args.Length == 0
            ? CopperScreenEmulator.CreateWithoutDisk()
            : CopperScreenEmulator.Create(args, AppContext.BaseDirectory);
    }

    var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", fileName);
    return diskPath == null
        ? null
        : CopperScreenEmulator.Create(CreateEmulatorArgs(diskPath, agnusTimingMode, options), AppContext.BaseDirectory);
}

static string[] CreateEmulatorArgs(string? diskPath, string? agnusTimingMode, BenchmarkOptions options)
{
    var count = (diskPath == null ? 0 : 1) +
        (string.IsNullOrWhiteSpace(options.Profile) ? 0 : 2) +
        (options.RealKickstart ? 1 : 0) +
        (string.IsNullOrWhiteSpace(options.KickstartRomPath) ? 0 : 2) +
        (string.IsNullOrWhiteSpace(agnusTimingMode) ? 0 : 2) +
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

    if (!string.IsNullOrWhiteSpace(options.Profile))
    {
        args[index++] = "--profile";
        args[index++] = options.Profile;
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

    if (!string.IsNullOrWhiteSpace(agnusTimingMode))
    {
        args[index++] = "--agnus-timing";
        args[index++] = agnusTimingMode;
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
    var machine = (AmigaMachine)typeof(CopperScreenEmulator)
        .GetField("_machine", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(emulator)!;
    return machine.Bus.Display;
}

static AgnusBeamDmaSnapshot GetAgnusSnapshot(CopperScreenEmulator emulator)
{
    var machine = (AmigaMachine)typeof(CopperScreenEmulator)
        .GetField("_machine", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(emulator)!;
    return machine.Bus.Agnus.CaptureSnapshot();
}

static DiskSummary CaptureDiskSummary(CopperScreenEmulator emulator)
{
    var machine = (AmigaMachine)typeof(CopperScreenEmulator)
        .GetField("_machine", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(emulator)!;
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
        disk.Dskbytr);
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
        display.LastMissedSpriteDmaSlots);
}

static string FormatFramebufferSummary(FramebufferSummary summary)
    => $"{summary.NonBlackPixels}/{summary.DistinctColors}/0x{summary.Checksum:X8}";

static string FormatAudioSummary(AudioSummary summary)
    => $"{summary.Frames}/{summary.NonZeroSamples}/{summary.Peak:F4}/0x{summary.Checksum:X8}";

static string FormatDisplaySummary(DisplaySummary summary)
{
    return $"bpl={summary.BitplanePixels}:{summary.BitplaneMinX},{summary.BitplaneMinY}-{summary.BitplaneMaxX},{summary.BitplaneMaxY}," +
        $"spr={summary.SpritePixels}:{summary.SpriteMinX},{summary.SpriteMinY}-{summary.SpriteMaxX},{summary.SpriteMaxY}," +
        $"dma={summary.BitplaneDmaFetches}/{summary.SpriteDmaFetches},missedSpr={summary.MissedSpriteSlots}," +
        $"bplcon={summary.Bplcon0:X4}/{summary.Bplcon1:X4}/{summary.Bplcon2:X4}";
}

static string FormatDiskSummary(DiskSummary summary)
{
    return $"xfer={summary.TransferCount},words={summary.LastTransferWords},last=d{summary.LastTransferDrive} " +
        $"{summary.LastTransferCylinder}.{summary.LastTransferHead}@0x{summary.LastTransferAddress:X6}," +
        $"selected={summary.SelectedDrive},active={summary.ActiveDma},dsklen=0x{summary.Dsklen:X4},bytr=0x{summary.Dskbytr:X4}";
}

static string FormatSpecializationSummary(HardwareSpecializationSummary summary)
{
    return $"blt={summary.BlitterKernelHits}/{summary.BlitterKernelMisses}/{summary.BlitterGeneratedKernels}/{summary.BlitterFallbacks}," +
        $"dskPrep={summary.DiskPreparedTrackHits}/{summary.DiskPreparedTrackMisses}," +
        $"dskWin={summary.DiskRollingWindowHits}/{summary.DiskRollingWindowMisses}," +
        $"dskSync={summary.DiskSyncIndexHits}/{summary.DiskSyncIndexMisses}";
}

static string CompareFramebufferSummary(BenchmarkRunResult legacy, BenchmarkRunResult slot)
{
    return legacy.Framebuffer.Equals(slot.Framebuffer)
        ? "match"
        : $"{FormatFramebufferSummary(legacy.Framebuffer)}->{FormatFramebufferSummary(slot.Framebuffer)}";
}

static string CompareAudioSummary(BenchmarkRunResult legacy, BenchmarkRunResult slot)
{
    return legacy.Audio.Equals(slot.Audio)
        ? "match"
        : $"{FormatAudioSummary(legacy.Audio)}->{FormatAudioSummary(slot.Audio)}";
}

static string CompareDisplaySummary(BenchmarkRunResult legacy, BenchmarkRunResult slot)
{
    return DisplayEndpointsEqual(legacy.Display, slot.Display)
        ? "match"
        : $"{FormatDisplaySummary(legacy.Display)}->{FormatDisplaySummary(slot.Display)}";
}

static string CompareDiskSummary(BenchmarkRunResult legacy, BenchmarkRunResult slot)
{
    return legacy.Disk.Equals(slot.Disk)
        ? "match"
        : $"{FormatDiskSummary(legacy.Disk)}->{FormatDiskSummary(slot.Disk)}";
}

static string FormatLastDivergence(AgnusSlotDivergenceSnapshot? divergence)
{
    if (!divergence.HasValue)
    {
        return "none";
    }

    var value = divergence.Value;
    var prefix = $"{value.Kind}:{value.Request.Requester}/{value.Request.Kind}/{value.Request.Size}@{value.Request.RequestedCycle}:legacy={FormatDivergenceGrant(value.PrimaryGranted, value.PrimaryGrantedCycle, value.PrimaryCompletedCycle)},slot={FormatDivergenceGrant(value.ShadowGranted, value.ShadowGrantedCycle, value.ShadowCompletedCycle)}";
    return value.HasValueComparison
        ? $"{prefix},values={value.PrimaryValue:X4}->{value.ShadowValue:X4}"
        : prefix;
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

static string FormatDivergenceGrant(bool granted, long grantedCycle, long completedCycle)
{
    return granted
        ? $"{grantedCycle}->{completedCycle}"
        : $"denied@{grantedCycle}";
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

internal readonly record struct BenchmarkWorkload(string Name, string? FileName, int FireFrame = -1);

internal readonly record struct FrameProfile(
    double CpuPercent,
    double AgnusPercent,
    double DisplayPercent,
    double CpuMillisecondsPerFrame,
    double AgnusMillisecondsPerFrame,
    double DisplayMillisecondsPerFrame,
    double TotalMillisecondsPerFrame);

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
    int MissedSpriteSlots);

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
    ushort Dskbytr);

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
    M68kInstructionFrequencySnapshot InstructionFrequency,
    string CpuBackend,
    M68kJitCounters Jit,
    string StatusText);

internal readonly record struct BenchmarkOptions(
    bool Smoke,
    int WarmupFrames,
    int MeasuredFrames,
    int RepeatCount,
    string? Only,
    string? Profile,
    string? AgnusTimingMode,
    string? CpuBackend,
    bool RealKickstart,
    string? KickstartRomPath,
    bool CompareAgnus,
    bool InstructionMatrix,
    int TopInstructionOpcodes)
{
    public static BenchmarkOptions Parse(string[] args)
    {
        var smoke = args.Contains("--smoke", StringComparer.OrdinalIgnoreCase);
        var compareAgnus = args.Contains("--compare", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("--compare-agnus", StringComparer.OrdinalIgnoreCase);
        var warmup = smoke ? 20 : 240;
        var measured = smoke ? 20 : 360;
        var repeatCount = 1;
        var instructionMatrix = false;
        var topInstructionOpcodes = 16;
        string? only = null;
        string? profile = null;
        string? agnusTimingMode = null;
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
            else if ((string.Equals(args[i], "--agnus", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[i], "--agnus-timing", StringComparison.OrdinalIgnoreCase)) &&
                i + 1 < args.Length)
            {
                agnusTimingMode = args[++i];
            }
            else if (string.Equals(args[i], "--cpu", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                cpuBackend = args[++i];
            }
            else if (string.Equals(args[i], "--jit", StringComparison.OrdinalIgnoreCase))
            {
                cpuBackend = "jit";
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
            agnusTimingMode,
            cpuBackend,
            realKickstart,
            kickstartRomPath,
            compareAgnus,
            instructionMatrix,
            Math.Clamp(topInstructionOpcodes, 0, 256));
    }
}
