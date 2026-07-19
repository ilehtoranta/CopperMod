using System.Diagnostics;
using System.Runtime.CompilerServices;
using CopperMod.Amiga;

internal static class CustomRegisterDispatchBenchmark
{
    private const string ModeArgument = "--custom-register-dispatch";
    private const int DefaultOperations = 4_000_000;
    private const int WarmupRepeats = 3;
    private const int MeasuredRepeats = 10;

    private delegate ushort StaticReadHandler(ref DispatchState state, ushort offset);

    private static readonly ushort[] Offsets =
    [
        0x006, 0x01E, 0x006, 0x002,
        0x01E, 0x01A, 0x006, 0x01C,
        0x002, 0x004, 0x01E, 0x1C0,
        0x006, 0x016, 0x07C, 0x010
    ];

    private static readonly ushort[] StablePollOffsets =
    [
        0x01E, 0x01C, 0x010, 0x002,
        0x07C, 0x1C0, 0x016, 0x008
    ];

    private static readonly CustomRegisterReadHandler[] HandlerIds = BuildHandlerIds();
    private static readonly ushort[] PublishedValues = BuildPublishedValues();
    private static readonly StaticReadHandler[] StaticHandlers = BuildStaticHandlers();
    private static readonly ReadDispatchStrategy[] ReadStrategies =
    [
        ReadDispatchStrategy.LegacyProbeCascade,
        ReadDispatchStrategy.HandlerId,
        ReadDispatchStrategy.PublishedCache,
        ReadDispatchStrategy.StaticDelegate
    ];

    private static readonly ushort[] WriteOffsets =
    [
        0x040, 0x096, 0x180, 0x09C,
        0x020, 0x100, 0x080, 0x0A6,
        0x058, 0x1C0, 0x07E, 0x120,
        0x1E4, 0x030, 0x0A8, 0x08E
    ];

    private static readonly CustomRegisterWriteTarget[] WriteTargets = BuildWriteTargets();

    public static bool TryRun(string[] args)
    {
        if (!args.Contains(ModeArgument, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var operations = ReadInt(args, "--custom-register-dispatch-operations", DefaultOperations);
        if (operations <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(args), "Operation count must be positive.");
        }

        Console.WriteLine(
            $"Custom-register dispatch benchmark, configuration={BuildConfiguration}, operations={operations}, warmups={WarmupRepeats}, repeats={MeasuredRepeats}");
        Console.WriteLine("workload\tstrategy\tmedian ms\tallocated bytes\tchecksum");

        var dispatch = MeasureReadStrategies(operations);
        var dispatchLegacy = dispatch.Legacy;
        var dispatchIds = dispatch.HandlerId;
        var dispatchPublished = dispatch.PublishedCache;
        var dispatchDelegates = dispatch.StaticDelegate;
        var polling = MeasurePublishedPollingStrategies(operations);
        var writes = MeasureWriteStrategies(operations);
        var legacyWrites = writes.Legacy;
        var targetedWrites = writes.Current;

        WriteResult("dispatch", "legacy-probe-cascade", dispatchLegacy);
        WriteResult("dispatch", "handler-id", dispatchIds);
        WriteResult("dispatch", "published-cache", dispatchPublished);
        WriteResult("dispatch", "static-delegate", dispatchDelegates);
        WriteResult("stable-poll", "legacy-probe-cascade", polling.Legacy);
        WriteResult("stable-poll", "published-cache", polling.PublishedCache);
        WriteResult("write-routing", "legacy-broadcast", legacyWrites);
        WriteResult("write-routing", "owner-targeted", targetedWrites);

        WriteComparison("read-dispatch", dispatchLegacy, dispatchIds);
        WriteComparison("published-cache-vs-old-unobserved", dispatchLegacy, dispatchPublished);
        WriteComparison("stable-poll-with-publication", polling.Legacy, polling.PublishedCache);
        WriteComparison("write-routing", legacyWrites, targetedWrites);

        if (dispatchLegacy.Checksum != dispatchIds.Checksum ||
            dispatchIds.Checksum != dispatchPublished.Checksum ||
            dispatchPublished.Checksum != dispatchDelegates.Checksum ||
            polling.Legacy.Checksum != polling.PublishedCache.Checksum ||
            legacyWrites.Checksum != targetedWrites.Checksum)
        {
            throw new InvalidOperationException("Dispatch strategies produced different checksums.");
        }

        var publishedCacheWins = dispatchPublished.AllocatedBytes == 0 &&
            polling.PublishedCache.AllocatedBytes == 0 &&
            dispatchPublished.MedianMilliseconds <= dispatchLegacy.MedianMilliseconds * 0.95 &&
            polling.PublishedCache.MedianMilliseconds <= polling.Legacy.MedianMilliseconds * 0.95;
        if (!publishedCacheWins)
        {
            throw new InvalidOperationException(
                "Published-cache reads must allocate zero bytes and beat the original probe cascade by at least 5%.");
        }

        var delegatesWin = dispatchDelegates.AllocatedBytes == 0 &&
            dispatchDelegates.MedianMilliseconds <= dispatchPublished.MedianMilliseconds * 0.95;
        Console.WriteLine($"selection\t{(delegatesWin ? "static-delegate" : "published-cache")}");
        return true;
    }

    private static ReadBenchmarkResults MeasureReadStrategies(int operations)
    {
        for (var warmup = 0; warmup < WarmupRepeats; warmup++)
        {
            foreach (var strategy in ReadStrategies)
            {
                _ = Execute(operations / 4, strategy);
            }
        }

        var elapsed = new double[ReadStrategies.Length][];
        var maximumAllocated = new long[ReadStrategies.Length];
        var checksums = new uint[ReadStrategies.Length];
        for (var strategy = 0; strategy < elapsed.Length; strategy++)
        {
            elapsed[strategy] = new double[MeasuredRepeats];
        }

        for (var repeat = 0; repeat < MeasuredRepeats; repeat++)
        {
            for (var order = 0; order < ReadStrategies.Length; order++)
            {
                var strategyIndex = (repeat + order) % ReadStrategies.Length;
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                checksums[strategyIndex] = Execute(
                    operations,
                    ReadStrategies[strategyIndex]);
                elapsed[strategyIndex][repeat] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                maximumAllocated[strategyIndex] = Math.Max(
                    maximumAllocated[strategyIndex],
                    GC.GetAllocatedBytesForCurrentThread() - beforeBytes);
            }
        }

        return new ReadBenchmarkResults(
            BuildResult(elapsed[0], maximumAllocated[0], checksums[0]),
            BuildResult(elapsed[1], maximumAllocated[1], checksums[1]),
            BuildResult(elapsed[2], maximumAllocated[2], checksums[2]),
            BuildResult(elapsed[3], maximumAllocated[3], checksums[3]));
    }

    private static uint Execute(
        int operations,
        ReadDispatchStrategy strategy)
        => strategy switch
        {
            ReadDispatchStrategy.LegacyProbeCascade => ExecuteLegacyReads(operations),
            ReadDispatchStrategy.PublishedCache => ExecutePublishedReads(operations),
            ReadDispatchStrategy.StaticDelegate => ExecuteDelegateReads(operations),
            _ => ExecuteHandlerIdReads(operations)
        };

    private static uint ExecuteLegacyReads(int operations)
    {
        var state = CreateDispatchState();
        uint checksum = 2166136261;
        for (var index = 0; index < operations; index++)
        {
            var offset = Offsets[index & (Offsets.Length - 1)];
            var value = DispatchLegacyProbeCascade(ref state, offset);
            checksum = unchecked((checksum * 16777619) ^ value);
        }

        return checksum;
    }

    private static ReadPollBenchmarkResults MeasurePublishedPollingStrategies(int operations)
    {
        for (var warmup = 0; warmup < WarmupRepeats; warmup++)
        {
            _ = ExecuteLegacyPoll(operations / 4);
            var file = CreatePollingRegisterFile();
            _ = ExecutePublishedPoll(operations / 4, file);
        }

        var elapsed = new[] { new double[MeasuredRepeats], new double[MeasuredRepeats] };
        var maximumAllocated = new long[2];
        var checksums = new uint[2];
        for (var repeat = 0; repeat < MeasuredRepeats; repeat++)
        {
            for (var order = 0; order < 2; order++)
            {
                var strategyIndex = (repeat + order) & 1;
                var file = strategyIndex == 0 ? null : CreatePollingRegisterFile();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                checksums[strategyIndex] = strategyIndex == 0
                    ? ExecuteLegacyPoll(operations)
                    : ExecutePublishedPoll(operations, file!);
                elapsed[strategyIndex][repeat] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                maximumAllocated[strategyIndex] = Math.Max(
                    maximumAllocated[strategyIndex],
                    GC.GetAllocatedBytesForCurrentThread() - beforeBytes);
            }
        }

        return new ReadPollBenchmarkResults(
            BuildResult(elapsed[0], maximumAllocated[0], checksums[0]),
            BuildResult(elapsed[1], maximumAllocated[1], checksums[1]));
    }

    private static CustomRegisterFile CreatePollingRegisterFile()
    {
        var file = new CustomRegisterFile(AmigaChipset.EcsPal);
        foreach (var offset in StablePollOffsets)
        {
            file.PublishStoredValue(offset, PublishedValues[offset >> 1], 0);
        }

        return file;
    }

    private static uint ExecuteLegacyPoll(int operations)
    {
        var state = CreateDispatchState();
        uint checksum = 2166136261;
        for (var index = 0; index < operations; index++)
        {
            if ((index & 63) == 0)
            {
                state.Intreq ^= 0x0020;
            }

            var offset = StablePollOffsets[index & (StablePollOffsets.Length - 1)];
            var value = DispatchLegacyProbeCascade(ref state, offset);
            checksum = unchecked((checksum * 16777619) ^ value);
        }

        return checksum;
    }

    private static uint ExecutePublishedPoll(int operations, CustomRegisterFile file)
    {
        var intreq = (ushort)0x0040;
        uint checksum = 2166136261;
        for (var index = 0; index < operations; index++)
        {
            if ((index & 63) == 0)
            {
                intreq ^= 0x0020;
                file.PublishStoredValue(0x01E, intreq, index);
            }

            var offset = StablePollOffsets[index & (StablePollOffsets.Length - 1)];
            var value = file.GetStoredValue(offset);
            checksum = unchecked((checksum * 16777619) ^ value);
        }

        return checksum;
    }

    private static uint ExecuteHandlerIdReads(int operations)
    {
        var state = CreateDispatchState();
        uint checksum = 2166136261;
        for (var index = 0; index < operations; index++)
        {
            var offset = Offsets[index & (Offsets.Length - 1)];
            var value = DispatchById(HandlerIds[offset >> 1], ref state, offset);
            checksum = unchecked((checksum * 16777619) ^ value);
        }

        return checksum;
    }

    private static uint ExecutePublishedReads(int operations)
    {
        var state = CreateDispatchState();
        uint checksum = 2166136261;
        for (var index = 0; index < operations; index++)
        {
            var offset = Offsets[index & (Offsets.Length - 1)];
            var handler = HandlerIds[offset >> 1];
            var value = handler == CustomRegisterReadHandler.StoredValue
                ? PublishedValues[offset >> 1]
                : DispatchById(handler, ref state, offset);
            checksum = unchecked((checksum * 16777619) ^ value);
        }

        return checksum;
    }

    private static uint ExecuteDelegateReads(int operations)
    {
        var state = CreateDispatchState();
        uint checksum = 2166136261;
        for (var index = 0; index < operations; index++)
        {
            var offset = Offsets[index & (Offsets.Length - 1)];
            var value = StaticHandlers[offset >> 1](ref state, offset);
            checksum = unchecked((checksum * 16777619) ^ value);
        }

        return checksum;
    }

    private static DispatchState CreateDispatchState()
        => new()
        {
            Dmacon = 0x0200,
            Beam = 0x1234,
            Intreq = 0x0040,
            Paula = 0x8000,
            Disk = 0x4489,
            Display = 0x00FC,
            Agnus = 0x00E2
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort DispatchLegacyProbeCascade(ref DispatchState state, ushort offset)
    {
        offset &= 0x01FE;

        // This mirrors the old ReadCustomWord probe order: Agnus, Denise,
        // then the offset switch and Paula fallback.
        if (offset is >= 0x1C0 and <= 0x1E2)
        {
            return ReadAgnus(ref state, offset);
        }

        if (offset == 0x07C)
        {
            return ReadDisplay(ref state, offset);
        }

        return offset switch
        {
            0x002 => ReadDmacon(ref state, offset),
            0x004 or 0x006 => ReadBeam(ref state, offset),
            0x008 or 0x01A or 0x020 or 0x022 or 0x024 or 0x07E => ReadDisk(ref state, offset),
            0x00E => ReadDisplay(ref state, offset),
            0x00A or 0x00C or 0x010 or 0x016 or 0x01C or 0x01E => ReadPaula(ref state, offset),
            _ => ReadOpenBus(ref state, offset)
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort DispatchById(CustomRegisterReadHandler handler, ref DispatchState state, ushort offset)
        => handler switch
        {
            CustomRegisterReadHandler.StoredValue => PublishedValues[offset >> 1],
            CustomRegisterReadHandler.Dmaconr => ReadDmacon(ref state, offset),
            CustomRegisterReadHandler.BeamPosition => ReadBeam(ref state, offset),
            CustomRegisterReadHandler.Disk => ReadDisk(ref state, offset),
            CustomRegisterReadHandler.Agnus => ReadAgnus(ref state, offset),
            CustomRegisterReadHandler.PotGo => ReadPaula(ref state, offset),
            CustomRegisterReadHandler.Paula => ReadPaula(ref state, offset),
            _ => ReadOpenBus(ref state, offset)
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort ReadDmacon(ref DispatchState state, ushort offset)
        => (ushort)(state.Dmacon ^ offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort ReadBeam(ref DispatchState state, ushort offset)
        => (ushort)(state.Beam + offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort ReadDisk(ref DispatchState state, ushort offset)
        => (ushort)(state.Disk ^ offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort ReadDisplay(ref DispatchState state, ushort offset)
        => (ushort)(state.Display + offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort ReadAgnus(ref DispatchState state, ushort offset)
        => (ushort)(state.Agnus + offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort ReadPaula(ref DispatchState state, ushort offset)
        => offset == 0x01E ? state.Intreq : (ushort)(state.Paula ^ offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort ReadOpenBus(ref DispatchState state, ushort offset)
        => (ushort)(0xFFFF ^ offset);

    private static ushort[] BuildPublishedValues()
    {
        var state = CreateDispatchState();
        var result = new ushort[256];
        for (var index = 0; index < result.Length; index++)
        {
            var offset = (ushort)(index << 1);
            result[index] = offset switch
            {
                0x002 => ReadDmacon(ref state, offset),
                0x008 => ReadDisk(ref state, offset),
                0x00A or 0x00C or 0x010 or 0x012 or 0x014 or 0x016 or 0x018 or 0x01C or 0x01E =>
                    ReadPaula(ref state, offset),
                0x07C => ReadDisplay(ref state, offset),
                >= 0x1C0 and <= 0x1E2 and not 0x1DA => ReadAgnus(ref state, offset),
                _ => 0
            };
        }

        return result;
    }

    private static CustomRegisterReadHandler[] BuildHandlerIds()
    {
        var result = new CustomRegisterReadHandler[256];
        var file = new CustomRegisterFile(AmigaChipset.EcsPal).CaptureSnapshot();
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = file[index].ReadHandler;
        }

        return result;
    }

    private static StaticReadHandler[] BuildStaticHandlers()
    {
        var result = new StaticReadHandler[256];
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = HandlerIds[index] switch
            {
                CustomRegisterReadHandler.Dmaconr => ReadDmacon,
                CustomRegisterReadHandler.BeamPosition => ReadBeam,
                CustomRegisterReadHandler.Disk => ReadDisk,
                CustomRegisterReadHandler.Agnus => ReadAgnus,
                CustomRegisterReadHandler.PotGo => ReadPaula,
                CustomRegisterReadHandler.Paula => ReadPaula,
                CustomRegisterReadHandler.StoredValue => ReadPublished,
                _ => ReadOpenBus
            };
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort ReadPublished(ref DispatchState state, ushort offset)
        => PublishedValues[offset >> 1];

    private static CustomRegisterWriteTarget[] BuildWriteTargets()
    {
        var result = new CustomRegisterWriteTarget[256];
        var file = new CustomRegisterFile(AmigaChipset.EcsPal).CaptureSnapshot();
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = file[index].WriteTargets;
        }

        return result;
    }

    private static WriteBenchmarkResults MeasureWriteStrategies(int operations)
    {
        for (var warmup = 0; warmup < WarmupRepeats; warmup++)
        {
            _ = ExecuteWrites(operations / 4, legacyBroadcast: true);
            _ = ExecuteWrites(operations / 4, legacyBroadcast: false);
        }

        var elapsed = new[] { new double[MeasuredRepeats], new double[MeasuredRepeats] };
        var maximumAllocated = new long[2];
        var checksums = new uint[2];
        for (var repeat = 0; repeat < MeasuredRepeats; repeat++)
        {
            for (var order = 0; order < 2; order++)
            {
                var strategyIndex = (repeat + order) & 1;
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                checksums[strategyIndex] = ExecuteWrites(
                    operations,
                    legacyBroadcast: strategyIndex == 0);
                elapsed[strategyIndex][repeat] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                maximumAllocated[strategyIndex] = Math.Max(
                    maximumAllocated[strategyIndex],
                    GC.GetAllocatedBytesForCurrentThread() - beforeBytes);
            }
        }

        return new WriteBenchmarkResults(
            BuildResult(elapsed[0], maximumAllocated[0], checksums[0]),
            BuildResult(elapsed[1], maximumAllocated[1], checksums[1]));
    }

    private static BenchmarkResult BuildResult(double[] elapsed, long allocatedBytes, uint checksum)
    {
        Array.Sort(elapsed);
        return new BenchmarkResult(
            (elapsed[(MeasuredRepeats / 2) - 1] + elapsed[MeasuredRepeats / 2]) / 2,
            allocatedBytes,
            checksum);
    }

    private static uint ExecuteWrites(int operations, bool legacyBroadcast)
    {
        uint state = 2166136261;
        for (var index = 0; index < operations; index++)
        {
            var offset = WriteOffsets[index & (WriteOffsets.Length - 1)];
            var value = (ushort)(index ^ (index >> 16));
            var targets = WriteTargets[offset >> 1];
            if (legacyBroadcast)
            {
                ApplyWriteTarget(ref state, targets, CustomRegisterWriteTarget.Agnus, offset, value);
                ApplyWriteTarget(ref state, targets, CustomRegisterWriteTarget.Paula, offset, value);
                ApplyWriteTarget(ref state, targets, CustomRegisterWriteTarget.Display, offset, value);
                ApplyWriteTarget(ref state, targets, CustomRegisterWriteTarget.Blitter, offset, value);
                ApplyWriteTarget(ref state, targets, CustomRegisterWriteTarget.Disk, offset, value);
            }
            else
            {
                if ((targets & CustomRegisterWriteTarget.Agnus) != 0)
                {
                    ApplyWriteTarget(ref state, targets, CustomRegisterWriteTarget.Agnus, offset, value);
                }
                if ((targets & CustomRegisterWriteTarget.Paula) != 0)
                {
                    ApplyWriteTarget(ref state, targets, CustomRegisterWriteTarget.Paula, offset, value);
                }
                if ((targets & CustomRegisterWriteTarget.Display) != 0)
                {
                    ApplyWriteTarget(ref state, targets, CustomRegisterWriteTarget.Display, offset, value);
                }
                if ((targets & CustomRegisterWriteTarget.Blitter) != 0)
                {
                    ApplyWriteTarget(ref state, targets, CustomRegisterWriteTarget.Blitter, offset, value);
                }
                if ((targets & CustomRegisterWriteTarget.Disk) != 0)
                {
                    ApplyWriteTarget(ref state, targets, CustomRegisterWriteTarget.Disk, offset, value);
                }
            }
        }

        return state;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ApplyWriteTarget(
        ref uint state,
        CustomRegisterWriteTarget targets,
        CustomRegisterWriteTarget target,
        ushort offset,
        ushort value)
    {
        if ((targets & target) == 0)
        {
            return;
        }

        state = unchecked((state * 16777619) ^ (uint)(offset | value | ((ushort)target << 16)));
    }

    private static int ReadInt(string[] args, string name, int fallback)
    {
        for (var index = 0; index + 1 < args.Length; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[index + 1], out var value))
            {
                return value;
            }
        }

        return fallback;
    }

    private static void WriteResult(string workload, string strategy, BenchmarkResult result)
        => Console.WriteLine(
            $"{workload}\t{strategy}\t{result.MedianMilliseconds:F3}\t{result.AllocatedBytes}\t0x{result.Checksum:X8}");

    private static void WriteComparison(
        string workload,
        BenchmarkResult legacy,
        BenchmarkResult current)
    {
        var ratio = current.MedianMilliseconds / legacy.MedianMilliseconds;
        var percent = (ratio - 1) * 100;
        Console.WriteLine(
            $"comparison\t{workload}\tcurrent/old={ratio:F3}x\tdelta={percent:+0.0;-0.0;0.0}%");
    }

#if DEBUG
    private const string BuildConfiguration = "Debug";
#else
    private const string BuildConfiguration = "Release";
#endif

    private enum ReadDispatchStrategy : byte
    {
        LegacyProbeCascade,
        HandlerId,
        PublishedCache,
        StaticDelegate
    }

    private struct DispatchState
    {
        public ushort Dmacon;
        public ushort Beam;
        public ushort Intreq;
        public ushort Paula;
        public ushort Disk;
        public ushort Display;
        public ushort Agnus;
    }

    private readonly record struct BenchmarkResult(
        double MedianMilliseconds,
        long AllocatedBytes,
        uint Checksum);

    private readonly record struct ReadBenchmarkResults(
        BenchmarkResult Legacy,
        BenchmarkResult HandlerId,
        BenchmarkResult PublishedCache,
        BenchmarkResult StaticDelegate);

    private readonly record struct ReadPollBenchmarkResults(
        BenchmarkResult Legacy,
        BenchmarkResult PublishedCache);

    private readonly record struct WriteBenchmarkResults(
        BenchmarkResult Legacy,
        BenchmarkResult Current);
}
