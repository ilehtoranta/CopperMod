using System.Diagnostics;
using CopperMod.Amiga;
using CopperMod.Amiga.CustomChips.Agnus;
using CopperMod.Amiga.Runtime;

internal static class EcsTimingBenchmark
{
    private const string ModeArgument = "--ecs-timing";
    private const uint CustomBase = 0x00DFF000;

    public static bool TryRun(string[] args)
    {
        if (!args.Contains(ModeArgument, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var warmupFrames = ReadInt(args, "--ecs-timing-warmup", 5);
        var measuredFrames = ReadInt(args, "--ecs-timing-frames", 30);
        var repeats = ReadInt(args, "--ecs-timing-repeats", 5);
        if (warmupFrames < 0 || measuredFrames <= 0 || repeats <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(args), "ECS timing benchmark counts must be positive.");
        }

        Console.WriteLine(
            $"ECS timing benchmark, configuration={BuildConfiguration}, warmup={warmupFrames}, frames={measuredFrames}, repeats={repeats}");
        Console.WriteLine("workload\trepeat\tframes\tms\tframes/sec\tallocated bytes\tchecksum");
        foreach (var workload in Workloads)
        {
            for (var repeat = 1; repeat <= repeats; repeat++)
            {
                var run = new WorkloadRun(workload);
                run.Advance(warmupFrames);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                var checksum = run.Advance(measuredFrames);
                var elapsed = Stopwatch.GetElapsedTime(start);
                var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
                var framesPerSecond = measuredFrames / Math.Max(elapsed.TotalSeconds, double.Epsilon);
                Console.WriteLine(
                    $"{workload.Name}\t{repeat}\t{measuredFrames}\t{elapsed.TotalMilliseconds:F3}\t{framesPerSecond:F2}\t{allocated}\t0x{checksum:X8}");
            }
        }

        return true;
    }

    private sealed class WorkloadRun
    {
        private readonly Workload _workload;
        private readonly AmigaBus _bus;
        private readonly int _sampleStride;
        private readonly uint[] _frameBuffer;
        private long _cursor;
        private uint _checksum = 2166136261u;

        public WorkloadRun(Workload workload)
        {
            _workload = workload;
            _bus = new AmigaBus(
                chipset: workload.Chipset,
                captureBusAccesses: false,
                enableLiveAgnusDma: workload.IncludeDisplay,
                enableLiveDisplayDma: workload.IncludeDisplay);
            if (workload.ProgrammedGeometry)
            {
                _bus.WriteWord(CustomBase + 0x1C0, 199, 0);
                _bus.WriteWord(CustomBase + 0x1C8, 299, 0);
                _bus.WriteWord(CustomBase + 0x1DC, AgnusRegisterBank.VarBeamEnable, 0);
            }

            _sampleStride = Math.Max(2, _bus.RasterTiming.CpuCyclesPerColorClock * 8);
            _frameBuffer = workload.IncludeDisplay
                ? new uint[_bus.Display.Width * _bus.Display.Height]
                : Array.Empty<uint>();
        }

        public uint Advance(int frames)
        {
            for (var frame = 0; frame < frames; frame++)
            {
                var beam = _bus.GetBeamPosition(_cursor);
                var frameStart = beam.FrameStartCycle;
                var frameStop = frameStart + beam.FrameCycles;
                if (_workload.IncludeDisplay)
                {
                    _bus.Display.BeginPresentationFrame(
                        new PresentationFrameTarget(_frameBuffer),
                        frameStart,
                        frameStop);
                }

                for (var cycle = frameStart; cycle < frameStop; cycle += _sampleStride)
                {
                    var position = _bus.GetBeamPosition(cycle);
                    _checksum = unchecked((_checksum * 16777619u) ^ (uint)position.BeamLine);
                    _checksum = unchecked((_checksum * 16777619u) ^ (uint)position.BeamHorizontal);
                    _checksum = unchecked((_checksum * 16777619u) ^ (uint)_bus.PredictDiskDmaGrantCycle(cycle));
                    _ = _bus.ReadCurrentChipDmaWord((uint)(cycle & 0x1FFFFE));
                }

                if (_workload.IncludeDisplay)
                {
                    _bus.Display.CompletePresentationFrame(frameStop);
                    _checksum = unchecked((_checksum * 16777619u) ^ (uint)_frameBuffer[frame & (_frameBuffer.Length - 1)]);
                }

                _bus.AdvanceCiasTo(frameStop);
                _cursor = frameStop;
            }

            return _checksum;
        }
    }

    private static int ReadInt(string[] args, string name, int fallback)
    {
        for (var i = 0; i + 1 < args.Length; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[i + 1], out var value))
            {
                return value;
            }
        }

        return fallback;
    }

#if DEBUG
    private const string BuildConfiguration = "Debug";
#else
    private const string BuildConfiguration = "Release";
#endif

    private readonly record struct Workload(
        string Name,
        AmigaChipset Chipset,
        bool IncludeDisplay,
        bool ProgrammedGeometry);

    private static readonly Workload[] Workloads =
    [
        new("A500 PAL chip-RAM", AmigaChipset.OcsPal, false, false),
        new("A500 PAL display/live-DMA", AmigaChipset.OcsPal, true, false),
        new("ECS PAL", AmigaChipset.EcsPal, true, false),
        new("ECS NTSC", AmigaChipset.EcsNtsc, true, false),
        new("ECS programmed beam", AmigaChipset.EcsNtsc, true, true)
    ];
}
