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
    new BenchmarkWorkload("Shadow of the Beast IPF", "Shadow of the Beast (1989)(Psygnosis)(US)(Disk 1 of 2).zip"),
};
var workloads = options.Smoke
    ? new[] { allWorkloads[0] }
    : FilterWorkloads(allWorkloads, options.Only);

Console.WriteLine($"Warmup={options.WarmupFrames} frames, measured={options.MeasuredFrames} frames, Release={IsRelease()}");
Console.WriteLine("name\tframes/sec\treal-time\tms/frame\tallocated bytes\tcpu%\tagnus%\tdisplay%\tlive events\tcopper\tpending\tfetches\tstatus");

foreach (var workload in workloads)
{
    var emulator = CreateEmulator(workload.FileName);
    if (emulator == null)
    {
        Console.WriteLine($"{workload.Name}\tmissing");
        continue;
    }

    var audio = new float[emulator.AudioFramesPerAppFrame(SampleRate) * Channels];
    for (var frame = 0; frame < options.WarmupFrames; frame++)
    {
        emulator.RenderNextFrame();
        _ = emulator.RenderAudio(audio, SampleRate, Channels);
    }

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
    var startTimestamp = Stopwatch.GetTimestamp();
    for (var frame = 0; frame < options.MeasuredFrames; frame++)
    {
        emulator.RenderNextFrame();
        _ = emulator.RenderAudio(audio, SampleRate, Channels);
    }

    var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
    var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
    var fps = options.MeasuredFrames / elapsed.TotalSeconds;
    var phase = options.Smoke || workload.FileName == null
        ? default
        : ProfileFrames(emulator, Math.Min(120, options.MeasuredFrames));
    var display = GetDisplay(emulator);
    Console.WriteLine(
        $"{workload.Name}\t{fps:F1}\t{fps / 50.0:F2}x\t{elapsed.TotalMilliseconds / options.MeasuredFrames:F2}\t{allocated}\t{phase.CpuPercent:F1}\t{phase.AgnusPercent:F1}\t{phase.DisplayPercent:F1}\t{display.LiveDisplayEventCount}\t{display.LiveCopperStepCount}\t{display.LivePendingWriteEventCount}\t{display.LiveFetchBatchWordCount}\t{emulator.StatusText}");
}

static CopperScreenEmulator? CreateEmulator(string? fileName)
{
    if (fileName == null)
    {
        return CopperScreenEmulator.CreateWithoutDisk();
    }

    var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", fileName);
    return diskPath == null
        ? null
        : CopperScreenEmulator.Create(new[] { diskPath }, AppContext.BaseDirectory);
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
    return new FrameProfile(Percent(cpuTicks, totalTicks), Percent(agnusTicks, totalTicks), Percent(displayTicks, totalTicks));
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

static double Percent(long value, long total)
    => total == 0 ? 0 : value * 100.0 / total;

static bool IsRelease()
{
#if DEBUG
    return false;
#else
    return true;
#endif
}

internal readonly record struct BenchmarkWorkload(string Name, string? FileName);

internal readonly record struct FrameProfile(double CpuPercent, double AgnusPercent, double DisplayPercent);

internal readonly record struct BenchmarkOptions(bool Smoke, int WarmupFrames, int MeasuredFrames, string? Only)
{
    public static BenchmarkOptions Parse(string[] args)
    {
        var smoke = args.Contains("--smoke", StringComparer.OrdinalIgnoreCase);
        var warmup = smoke ? 20 : 240;
        var measured = smoke ? 20 : 360;
        string? only = null;
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
            else if (string.Equals(args[i], "--only", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                only = args[++i];
            }
        }

        return new BenchmarkOptions(smoke, Math.Max(0, warmup), Math.Max(1, measured), only);
    }
}
