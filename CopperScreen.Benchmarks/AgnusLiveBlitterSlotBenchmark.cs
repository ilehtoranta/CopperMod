using System.Diagnostics;
using System.Globalization;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CustomChips.Agnus;

internal static class AgnusLiveBlitterSlotBenchmark
{
    private const string ModeArgument =
        "--agnus-live-blitter-slot-benchmark";
    private const int MaximumDecisions = 100_000;

    public static bool TryRun(string[] args)
    {
        if (!args.Contains(
                ModeArgument,
                StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        var options = ParseOptions(args);
        Console.WriteLine(
            "Live A500 PAL OCS blitter exact-slot benchmark, " +
            $"warmup={options.WarmupDecisions}, " +
            $"measured={options.Decisions}, repeats={options.Repeats}, " +
            $"Release={IsRelease()}");
        Console.WriteLine(
            "repeat\tsearch-grant-ns\texact-grant-ns\t" +
            "exact-denied-ns\texact/search\tgrant-allocated\t" +
            "denied-allocated\tgrant-checksum\tdenied-checksum\t" +
            "nasty-pending-ns\tnice-contention-ns\t" +
            "nasty-pending-allocated\tnice-contention-allocated\t" +
            "nasty-pending-checksum\tnice-contention-checksum");

        _ = RunSearchGrant(CreateFreeFixture(options.WarmupDecisions));
        _ = RunExact(CreateFreeFixture(options.WarmupDecisions), grant: true);
        _ = RunExact(CreateDeniedFixture(options.WarmupDecisions), grant: false);
        _ = RunNastyPending(CreatePendingFixture(options.WarmupDecisions, nasty: true));
        _ = RunNiceContention(CreatePendingFixture(options.WarmupDecisions, nasty: false));

        var searchMedians = new double[options.Repeats];
        var grantMedians = new double[options.Repeats];
        var deniedMedians = new double[options.Repeats];
        var nastyPendingMedians = new double[options.Repeats];
        var niceContentionMedians = new double[options.Repeats];
        for (var repeat = 0; repeat < options.Repeats; repeat++)
        {
            var searchFixture = CreateFreeFixture(options.Decisions);
            var grantFixture = CreateFreeFixture(options.Decisions);
            var deniedFixture = CreateDeniedFixture(options.Decisions);
            var nastyPendingFixture = CreatePendingFixture(options.Decisions, nasty: true);
            var niceContentionFixture = CreatePendingFixture(options.Decisions, nasty: false);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            SlotBenchmarkResult search;
            SlotBenchmarkResult grant;
            SlotBenchmarkResult denied;
            SlotBenchmarkResult nastyPending;
            SlotBenchmarkResult niceContention;
            if ((repeat & 1) == 0)
            {
                search = RunSearchGrant(searchFixture);
                grant = RunExact(grantFixture, grant: true);
                denied = RunExact(deniedFixture, grant: false);
                nastyPending = RunNastyPending(nastyPendingFixture);
                niceContention = RunNiceContention(niceContentionFixture);
            }
            else
            {
                niceContention = RunNiceContention(niceContentionFixture);
                nastyPending = RunNastyPending(nastyPendingFixture);
                denied = RunExact(deniedFixture, grant: false);
                grant = RunExact(grantFixture, grant: true);
                search = RunSearchGrant(searchFixture);
            }

            if (search.Checksum != grant.Checksum)
            {
                throw new InvalidOperationException(
                    "Exact and searching grant paths produced different " +
                    $"checksums: search=0x{search.Checksum:X16}, " +
                    $"exact=0x{grant.Checksum:X16}.");
            }

            searchMedians[repeat] = search.NanosecondsPerDecision;
            grantMedians[repeat] = grant.NanosecondsPerDecision;
            deniedMedians[repeat] = denied.NanosecondsPerDecision;
            nastyPendingMedians[repeat] = nastyPending.NanosecondsPerDecision;
            niceContentionMedians[repeat] = niceContention.NanosecondsPerDecision;
            Console.WriteLine(
                $"{repeat + 1}\t" +
                $"{search.NanosecondsPerDecision:F3}\t" +
                $"{grant.NanosecondsPerDecision:F3}\t" +
                $"{denied.NanosecondsPerDecision:F3}\t" +
                $"{grant.NanosecondsPerDecision / search.NanosecondsPerDecision:F3}\t" +
                $"{grant.AllocatedBytes}\t{denied.AllocatedBytes}\t" +
                $"0x{grant.Checksum:X16}\t0x{denied.Checksum:X16}\t" +
                $"{nastyPending.NanosecondsPerDecision:F3}\t" +
                $"{niceContention.NanosecondsPerDecision:F3}\t" +
                $"{nastyPending.AllocatedBytes}\t" +
                $"{niceContention.AllocatedBytes}\t" +
                $"0x{nastyPending.Checksum:X16}\t" +
                $"0x{niceContention.Checksum:X16}");
        }

        Array.Sort(searchMedians);
        Array.Sort(grantMedians);
        Array.Sort(deniedMedians);
        Array.Sort(nastyPendingMedians);
        Array.Sort(niceContentionMedians);
        var searchMedian = Median(searchMedians);
        var grantMedian = Median(grantMedians);
        var deniedMedian = Median(deniedMedians);
        var nastyPendingMedian = Median(nastyPendingMedians);
        var niceContentionMedian = Median(niceContentionMedians);
        Console.WriteLine(
            $"median\t{searchMedian:F3}\t{grantMedian:F3}\t" +
            $"{deniedMedian:F3}\t{grantMedian / searchMedian:F3}\t" +
            $"{nastyPendingMedian:F3}\t{niceContentionMedian:F3}");
        return true;
    }

    private static SlotBenchmarkFixture CreateFreeFixture(int decisions)
    {
        var engine = new AgnusHrmSlotEngine();
        engine.BlitterPriorityEnabled = true;
        return new SlotBenchmarkFixture(
            engine,
            CreateEligibleCycles(engine, decisions));
    }

    private static SlotBenchmarkFixture CreateDeniedFixture(int decisions)
    {
        var engine = new AgnusHrmSlotEngine();
        engine.BlitterPriorityEnabled = true;
        var cycles = CreateEligibleCycles(engine, decisions);
        for (var index = 0; index < cycles.Length; index++)
        {
            if (!engine.TryCommitPlannedBitplaneSlot(
                    AddressFor(index),
                    cycles[index],
                    out var reservation) ||
                reservation.GrantedCycle != cycles[index])
            {
                throw new InvalidOperationException(
                    $"Could not prepare denied slot {cycles[index]}.");
            }
        }

        return new SlotBenchmarkFixture(engine, cycles);
    }

    private static SlotBenchmarkFixture CreatePendingFixture(
        int decisions,
        bool nasty)
    {
        var engine = new AgnusHrmSlotEngine
        {
            BlitterPriorityEnabled = nasty
        };
        var cycles = CreateEligibleCycles(engine, decisions);
        engine.BeginPendingCpuSlotRequest(
            AmigaBusAccessKind.CpuDataRead,
            AmigaBusAccessTarget.ChipRam,
            0x1000,
            AmigaBusAccessSize.Word,
            requestedCycle: 0,
            isWrite: false);
        return new SlotBenchmarkFixture(engine, cycles);
    }

    private static long[] CreateEligibleCycles(
        AgnusHrmSlotEngine engine,
        int decisions)
    {
        var cycles = new long[decisions];
        var cycle = 0L;
        var count = 0;
        while (count < decisions)
        {
            if (!engine.IsMandatoryRefreshSlot(cycle))
            {
                cycles[count++] = cycle;
            }

            cycle += AgnusChipSlotScheduler.SlotCycles;
        }

        return cycles;
    }

    private static SlotBenchmarkResult RunSearchGrant(
        SlotBenchmarkFixture fixture)
    {
        var allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        ulong checksum = 14695981039346656037UL;
        for (var index = 0; index < fixture.Cycles.Length; index++)
        {
            var cycle = fixture.Cycles[index];
            var result = fixture.Engine.ReserveBlitterDmaWordSlot(
                AddressFor(index),
                cycle,
                isWrite: (index & 1) != 0);
            if (result.GrantedCycle != cycle)
            {
                throw new InvalidOperationException(
                    $"Searching blitter grant moved {cycle} to " +
                    $"{result.GrantedCycle}.");
            }

            checksum = Mix(checksum, result);
        }

        var elapsed = Stopwatch.GetElapsedTime(start);
        var allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return new SlotBenchmarkResult(
            fixture.Cycles.Length,
            elapsed,
            allocated,
            checksum);
    }

    private static SlotBenchmarkResult RunExact(
        SlotBenchmarkFixture fixture,
        bool grant)
    {
        var allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        ulong checksum = 14695981039346656037UL;
        for (var index = 0; index < fixture.Cycles.Length; index++)
        {
            var cycle = fixture.Cycles[index];
            var actual = fixture.Engine.TryReserveBlitterDmaWordExactSlot(
                AddressFor(index),
                cycle,
                cycle,
                isWrite: (index & 1) != 0,
                out var result);
            if (actual != grant || result.GrantedCycle != cycle)
            {
                throw new InvalidOperationException(
                    $"Exact blitter slot {cycle} returned grant={actual}, " +
                    $"cycle={result.GrantedCycle}; expected grant={grant}.");
            }

            checksum = Mix(checksum, result);
        }

        var elapsed = Stopwatch.GetElapsedTime(start);
        var allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return new SlotBenchmarkResult(
            fixture.Cycles.Length,
            elapsed,
            allocated,
            checksum);
    }

    private static SlotBenchmarkResult RunNastyPending(
        SlotBenchmarkFixture fixture)
    {
        var allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        ulong checksum = 14695981039346656037UL;
        for (var index = 0; index < fixture.Cycles.Length; index++)
        {
            var cycle = fixture.Cycles[index];
            if (!fixture.Engine.TryReserveBlitterDmaWordExactSlot(
                    AddressFor(index),
                    cycle,
                    cycle,
                    isWrite: (index & 1) != 0,
                    out var result))
            {
                throw new InvalidOperationException(
                    $"Nasty pending-CPU slot {cycle} was denied.");
            }

            fixture.Engine.ObservePendingCpuDmaCycle(cycle);
            checksum = Mix(checksum, result);
        }

        var elapsed = Stopwatch.GetElapsedTime(start);
        var allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return new SlotBenchmarkResult(
            fixture.Cycles.Length,
            elapsed,
            allocated,
            checksum);
    }

    private static SlotBenchmarkResult RunNiceContention(
        SlotBenchmarkFixture fixture)
    {
        var allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        ulong checksum = 14695981039346656037UL;
        for (var index = 0; index < fixture.Cycles.Length; index++)
        {
            var cycle = fixture.Cycles[index];
            var blitterGranted =
                fixture.Engine.TryReserveBlitterDmaWordExactSlot(
                    AddressFor(index),
                    cycle,
                    cycle,
                    isWrite: (index & 1) != 0,
                    out var result);
            var expectedBlitterGrant = (index & 3) != 3;
            if (blitterGranted != expectedBlitterGrant)
            {
                throw new InvalidOperationException(
                    $"Nice contention slot {cycle} returned blitter " +
                    $"grant={blitterGranted}; expected " +
                    $"{expectedBlitterGrant}.");
            }

            if (blitterGranted)
            {
                fixture.Engine.ObservePendingCpuDmaCycle(cycle);
                checksum = Mix(checksum, result);
                continue;
            }

            if (!fixture.Engine.TryGrantCpuDataSingleExactSlot(
                    AmigaBusAccessKind.CpuDataRead,
                    AmigaBusAccessTarget.ChipRam,
                    0x1000,
                    AmigaBusAccessSize.Word,
                    requestedCycle: 0,
                    cycle,
                    isWrite: false,
                    allowNiceBlitterSteal: true,
                    out var completedCycle))
            {
                throw new InvalidOperationException(
                    $"Nice contention CPU yield at {cycle} was denied.");
            }

            checksum = MixCycles(checksum, cycle, completedCycle);
        }

        var elapsed = Stopwatch.GetElapsedTime(start);
        var allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return new SlotBenchmarkResult(
            fixture.Cycles.Length,
            elapsed,
            allocated,
            checksum);
    }

    private static uint AddressFor(int index)
        => 0x2000u + ((uint)index * 2u);

    private static ulong Mix(
        ulong checksum,
        in AmigaBusAccessResult result)
        => MixCycles(
            checksum,
            result.GrantedCycle,
            result.CompletedCycle);

    private static ulong MixCycles(
        ulong checksum,
        long grantedCycle,
        long completedCycle)
    {
        checksum = unchecked(
            (checksum ^ (ulong)grantedCycle) *
            1099511628211UL);
        checksum = unchecked(
            (checksum ^ (ulong)completedCycle) *
            1099511628211UL);
        return checksum;
    }

    private static BenchmarkOptions ParseOptions(string[] args)
    {
        var warmup = 10_000;
        var decisions = MaximumDecisions;
        var repeats = 7;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case ModeArgument:
                    break;
                case "--warmup":
                    warmup = ParseInt(args, ref index);
                    break;
                case "--decisions":
                    decisions = ParseInt(args, ref index);
                    break;
                case "--repeats":
                    repeats = ParseInt(args, ref index);
                    break;
                default:
                    throw new ArgumentException(
                        "Unknown live blitter benchmark argument " +
                        $"'{args[index]}'.");
            }
        }

        if (warmup < 0 ||
            warmup > MaximumDecisions ||
            decisions <= 0 ||
            decisions > MaximumDecisions ||
            repeats <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(args),
                $"Warmup must be 0..{MaximumDecisions}; decisions must " +
                $"be 1..{MaximumDecisions}; repeats must be positive.");
        }

        return new BenchmarkOptions(warmup, decisions, repeats);
    }

    private static int ParseInt(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException(
                $"Missing value for '{args[index]}'.");
        }

        index++;
        return int.Parse(
            args[index],
            CultureInfo.InvariantCulture);
    }

    private static double Median(double[] sorted)
    {
        var middle = sorted.Length / 2;
        return (sorted.Length & 1) != 0
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private static bool IsRelease()
    {
#if DEBUG
        return false;
#else
        return true;
#endif
    }

    private readonly record struct BenchmarkOptions(
        int WarmupDecisions,
        int Decisions,
        int Repeats);

    private readonly record struct SlotBenchmarkFixture(
        AgnusHrmSlotEngine Engine,
        long[] Cycles);

    private readonly record struct SlotBenchmarkResult(
        int Decisions,
        TimeSpan Elapsed,
        long AllocatedBytes,
        ulong Checksum)
    {
        public double NanosecondsPerDecision =>
            Elapsed.TotalNanoseconds / Decisions;
    }
}
