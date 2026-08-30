using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;

namespace CopperMod.Amiga.Layers.Benchmarks;

internal static class Program
{
    private const int BatchSize = 8;

    [SupportedOSPlatform("windows")]
    private static int Main(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            using var process = Process.GetCurrentProcess();
            var actualMask = process.ProcessorAffinity.ToInt64();
            if (actualMask != options.ExpectedAffinityMask)
            {
                throw new InvalidOperationException(
                    $"Process affinity is {actualMask}, expected {options.ExpectedAffinityMask}.");
            }

            using var context = new BenchmarkContext();
            if (options.AllocationProbe)
            {
                context.PrintRasterAllocationProbe();
                return 0;
            }
            var selectedKinds = options.OnlyWorkload is { } onlyWorkload
                ? new[] { onlyWorkload }
                : WorkloadCatalog.Kinds;
            Console.WriteLine(
                "configuration\t" +
                $"warmup_seconds={options.WarmupSeconds.ToString(CultureInfo.InvariantCulture)}\t" +
                $"measurement_seconds={options.MeasurementSeconds.ToString(CultureInfo.InvariantCulture)}\t" +
                $"repeats={options.Repeats}\tbatch={BatchSize}\t" +
                $"workloads={selectedKinds.Length}\t" +
                $"logical_processor={options.ExpectedLogicalProcessor}\t" +
                $"affinity_mask={options.ExpectedAffinityMask}\t" +
                "order=cyclic\tmorph_render_success=unavailable_provider_capability");

            foreach (var kind in selectedKinds)
                Warmup(context, kind, options.WarmupSeconds);

            VerifyEmptyControlLoop(options.MeasurementSeconds);

            for (var ordinal = 0; ordinal < options.Repeats; ordinal++)
            {
                for (var position = 0; position < selectedKinds.Length; position++)
                {
                    var index = (position + ordinal) % selectedKinds.Length;
                    Measure(context, selectedKinds[index], ordinal + 1,
                        options.MeasurementSeconds);
                }
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void VerifyEmptyControlLoop(double seconds)
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        // Prime the allocation-counter helper outside the measured boundary.
        _ = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        var deadline = start + checked((long)(seconds * Stopwatch.Frequency));
        long operations = 0;
        var allocatedStart = GC.GetAllocatedBytesForCurrentThread();
        do
        {
            for (var index = 0; index < BatchSize; index++)
                operations++;
        }
        while (Stopwatch.GetTimestamp() < deadline);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedStart;
        var end = Stopwatch.GetTimestamp();
        if (allocated != 0)
        {
            throw new InvalidOperationException(
                $"Empty measurement control allocated {allocated} managed bytes.");
        }

        Console.WriteLine(
            "control\tworkload=empty-loop\t" +
            $"operations={operations}\tallocated_bytes={allocated}\t" +
            $"elapsed_seconds={((end - start) / (double)Stopwatch.Frequency).ToString("F6", CultureInfo.InvariantCulture)}");
    }

    private static void Warmup(
        BenchmarkContext context,
        WorkloadKind kind,
        double seconds)
    {
        var deadline = Stopwatch.GetTimestamp() +
            checked((long)(seconds * Stopwatch.Frequency));
        do
        {
            for (var index = 0; index < BatchSize; index++)
                context.Run(kind);
        }
        while (Stopwatch.GetTimestamp() < deadline);
        context.AssertStable(kind);
    }

    private static void Measure(
        BenchmarkContext context,
        WorkloadKind kind,
        int ordinal,
        double seconds)
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        // Fingerprints, output formatting, and collection setup remain outside
        // the measured allocation boundary. The empty-loop control above
        // proves the timing/counter loop itself is allocation-free.
        var assertionStart = context.AssertionCount;
        var start = Stopwatch.GetTimestamp();
        var deadline = start + checked((long)(seconds * Stopwatch.Frequency));
        long operations = 0;
        var allocatedStart = GC.GetAllocatedBytesForCurrentThread();
        do
        {
            for (var index = 0; index < BatchSize; index++)
                context.Run(kind);
            operations += BatchSize;
        }
        while (Stopwatch.GetTimestamp() < deadline);
        var end = Stopwatch.GetTimestamp();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedStart;
        var assertions = context.AssertionCount - assertionStart;
        if (assertions < operations)
        {
            throw new InvalidOperationException(
                $"{WorkloadCatalog.Name(kind)} executed {operations} operations but only {assertions} assertions.");
        }

        var fingerprint = context.AssertStable(kind);
        var elapsedSeconds = (end - start) / (double)Stopwatch.Frequency;
        var rate = operations / elapsedSeconds;
        Console.WriteLine(
            "sample\t" +
            $"workload={WorkloadCatalog.Name(kind)}\tordinal={ordinal}\t" +
            $"ops_per_second={rate.ToString("F3", CultureInfo.InvariantCulture)}\t" +
            $"allocated_bytes={allocated}\tassertions={assertions}\t" +
            $"public_free={fingerprint.PublicFree}\t" +
            $"state_hash={fingerprint.StateHash:X16}\t" +
            $"cliprects={fingerprint.ClipRects}\t" +
            $"backing_bitmaps={fingerprint.BackingBitMaps}\t" +
            $"provider={fingerprint.Provider}");
    }

    private readonly record struct Options(
        double WarmupSeconds,
        double MeasurementSeconds,
        int Repeats,
        int ExpectedLogicalProcessor,
        long ExpectedAffinityMask,
        WorkloadKind? OnlyWorkload,
        bool AllocationProbe)
    {
        internal static Options Parse(string[] args)
        {
            var warmup = 1.0;
            var measurement = 3.0;
            var repeats = 5;
            var processor = -1;
            long mask = 0;
            WorkloadKind? onlyWorkload = null;
            var allocationProbe = false;
            for (var index = 0; index < args.Length; index += 2)
            {
                if (index + 1 >= args.Length)
                    throw new ArgumentException($"Missing value for {args[index]}.");
                var value = args[index + 1];
                switch (args[index])
                {
                    case "--warmup-seconds":
                        warmup = double.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "--measurement-seconds":
                        measurement = double.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "--repeats":
                        repeats = int.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "--expected-logical-processor":
                        processor = int.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "--expected-affinity-mask":
                        mask = long.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "--only-workload":
                        if (!WorkloadCatalog.TryParse(value, out var workload))
                            throw new ArgumentException($"Unknown workload {value}.");
                        onlyWorkload = workload;
                        break;
                    case "--allocation-probe":
                        allocationProbe = bool.Parse(value);
                        break;
                    default:
                        throw new ArgumentException($"Unknown argument {args[index]}.");
                }
            }
            if (warmup <= 0 || measurement <= 0 || repeats <= 0 ||
                processor < 0 || mask <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(args),
                    "All timing/count options and the affinity identity must be positive.");
            }
            return new Options(
                warmup, measurement, repeats, processor, mask, onlyWorkload,
                allocationProbe);
        }
    }
}

internal enum WorkloadKind
{
    IdleInit,
    HitTest,
    QueryLock,
    CreateDelete,
    OverlapRebuildSmart,
    MoveSize,
    SuperScrollSyncCopy,
    RefreshBeginEnd,
    GuestHook,
    RasterPlanar,
    RasterRtg,
    MorphRenderDecline
}

internal static class WorkloadCatalog
{
    internal static readonly WorkloadKind[] Kinds =
    [
        WorkloadKind.IdleInit,
        WorkloadKind.HitTest,
        WorkloadKind.QueryLock,
        WorkloadKind.CreateDelete,
        WorkloadKind.OverlapRebuildSmart,
        WorkloadKind.MoveSize,
        WorkloadKind.SuperScrollSyncCopy,
        WorkloadKind.RefreshBeginEnd,
        WorkloadKind.GuestHook,
        WorkloadKind.RasterPlanar,
        WorkloadKind.RasterRtg,
        WorkloadKind.MorphRenderDecline
    ];

    internal static readonly string[] Names =
    [
        "idle-init",
        "hit-test",
        "query-lock",
        "create-delete",
        "overlap-rebuild-smart",
        "move-size",
        "super-scroll-sync-copy",
        "refresh-begin-end",
        "guest-hook",
        "raster-planar",
        "raster-rtg",
        "morph-render-decline"
    ];

    internal static string Name(WorkloadKind kind) => Names[(int)kind];

    internal static bool TryParse(string name, out WorkloadKind kind)
    {
        for (var index = 0; index < Names.Length; index++)
        {
            if (string.Equals(Names[index], name, StringComparison.Ordinal))
            {
                kind = Kinds[index];
                return true;
            }
        }
        kind = default;
        return false;
    }
}
