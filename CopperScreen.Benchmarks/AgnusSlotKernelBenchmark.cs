using System.Diagnostics;
using System.Globalization;
using CopperMod.Amiga;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CustomChips.Agnus;
using CopperMod.Amiga.CustomChips.Denise;
using CopperMod.Amiga.Runtime;

internal static class AgnusSlotKernelBenchmark
{
	private const string ModeArgument = "--agnus-slot-kernel-benchmark";
	private const string LegacyModeArgument = "--agnus-slot-kernel";
	private const int SlotsPerLine = AmigaConstants.A500PalColorClocksPerRasterLine;
	private const int BitplaneEntries = 80;
	private const int SpriteEntries = 16;

	public static bool TryRun(string[] args)
	{
		var explicitBenchmark = args.Contains(ModeArgument, StringComparer.OrdinalIgnoreCase);
		var legacyBenchmark =
			args.Contains(LegacyModeArgument, StringComparer.OrdinalIgnoreCase) &&
			args.Contains("--decisions", StringComparer.OrdinalIgnoreCase);
		if (!explicitBenchmark && !legacyBenchmark)
		{
			return false;
		}

		CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
		CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
		var options = ParseOptions(args);
		var fixture = CreateFixture();
		var legacyNanoseconds = new double[options.Repeats];
		var kernelNanoseconds = new double[options.Repeats];
		Console.WriteLine(
			$"Offline A500 PAL OCS Agnus slot-decision benchmark, " +
			$"warmup={options.WarmupDecisions}, measured={options.Decisions}, " +
			$"repeats={options.Repeats}, Release={IsRelease()}");
		Console.WriteLine(
			"repeat\tlegacy-ns/decision\tkernel-ns/decision\tkernel/legacy\t" +
			"legacy-allocated\tkernel-allocated\tlegacy-checksum\tkernel-checksum");

		for (var repeat = 0; repeat < options.Repeats; repeat++)
		{
			_ = RunLegacy(fixture, options.WarmupDecisions);
			_ = RunKernel(fixture, options.WarmupDecisions);
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();

			DecisionBenchmarkResult legacy;
			DecisionBenchmarkResult kernel;
			if ((repeat & 1) == 0)
			{
				legacy = RunLegacy(fixture, options.Decisions);
				kernel = RunKernel(fixture, options.Decisions);
			}
			else
			{
				kernel = RunKernel(fixture, options.Decisions);
				legacy = RunLegacy(fixture, options.Decisions);
			}

			if (legacy.Checksum != kernel.Checksum)
			{
				throw new InvalidOperationException(
					$"Owner checksum mismatch: legacy=0x{legacy.Checksum:X16}, " +
					$"kernel=0x{kernel.Checksum:X16}.");
			}

			legacyNanoseconds[repeat] = legacy.NanosecondsPerDecision;
			kernelNanoseconds[repeat] = kernel.NanosecondsPerDecision;
			Console.WriteLine(
				$"{repeat + 1}\t{legacy.NanosecondsPerDecision:F3}\t" +
				$"{kernel.NanosecondsPerDecision:F3}\t" +
				$"{kernel.NanosecondsPerDecision / legacy.NanosecondsPerDecision:F3}\t" +
				$"{legacy.AllocatedBytes}\t{kernel.AllocatedBytes}\t" +
				$"0x{legacy.Checksum:X16}\t0x{kernel.Checksum:X16}");
		}

		Array.Sort(legacyNanoseconds);
		Array.Sort(kernelNanoseconds);
		var legacyMedian = Median(legacyNanoseconds);
		var kernelMedian = Median(kernelNanoseconds);
		Console.WriteLine(
			$"median\t{legacyMedian:F3}\t{kernelMedian:F3}\t" +
			$"{kernelMedian / legacyMedian:F3}");
		return true;
	}

	private static DecisionBenchmarkResult RunLegacy(
		DecisionBenchmarkFixture fixture,
		int decisions)
	{
		var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
		var start = Stopwatch.GetTimestamp();
		ulong checksum = 14695981039346656037UL;
		for (var index = 0; index < decisions; index++)
		{
			var slot = index % SlotsPerLine;
			var cycle = (long)slot * AgnusChipSlotScheduler.SlotCycles;
			AgnusChipSlotOwner owner;
			if (AgnusHrmOcsSlotTable.IsMandatoryRefreshSlot(cycle))
			{
				owner = AgnusChipSlotOwner.Refresh;
			}
			else if (!fixture.LegacyPlans.TryGetFixedOwnerAt(cycle, out owner, out _))
			{
				owner = AgnusChipSlotOwner.Cpu;
			}

			checksum = unchecked((checksum ^ (byte)owner) * 1099511628211UL);
		}

		var elapsed = Stopwatch.GetElapsedTime(start);
		var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
		return new DecisionBenchmarkResult(decisions, elapsed, allocated, checksum);
	}

	private static DecisionBenchmarkResult RunKernel(
		DecisionBenchmarkFixture fixture,
		int decisions)
	{
		var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
		var start = Stopwatch.GetTimestamp();
		ulong checksum = 14695981039346656037UL;
		for (var index = 0; index < decisions; index++)
		{
			var slot = index % SlotsPerLine;
			var cycle = (long)slot * AgnusChipSlotScheduler.SlotCycles;
			var owner = fixture.Kernel.GetOwnerForSlot(cycle, cpuPending: true);
			checksum = unchecked((checksum ^ (byte)owner) * 1099511628211UL);
		}

		var elapsed = Stopwatch.GetElapsedTime(start);
		var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
		return new DecisionBenchmarkResult(decisions, elapsed, allocated, checksum);
	}

	private static DecisionBenchmarkFixture CreateFixture()
	{
		var plans = new AgnusRasterlineDmaPlanRing(
			maxBitplaneEntriesPerLine: BitplaneEntries,
			maxSpriteEntriesPerLine: SpriteEntries);
		var trace = new AgnusOfflineReplayTrace(firstCycle: 0, slotCount: SlotsPerLine);
		for (var index = 0; index < SpriteEntries; index++)
		{
			var horizontal = AgnusHrmOcsSlotTable.FirstSpriteHorizontal + (index * 2);
			var cycle = (long)horizontal * AgnusChipSlotScheduler.SlotCycles;
			var channel = index / 2;
			var phase = index & 1;
			plans.SetSpriteEntry(index, new RowDmaSpriteEntry(cycle, channel, phase));
			trace.SetInitialFixedSlot(
				cycle,
				new FixedSlotPlanEntry(
					AgnusChipSlotOwner.Sprite,
					(byte)channel,
					(byte)phase),
				(uint)(0x3000 + (index * 2)));
		}

		for (var index = 0; index < BitplaneEntries; index++)
		{
			var horizontal = 0x40 + (index * 2);
			var cycle = (long)horizontal * AgnusChipSlotScheduler.SlotCycles;
			var plane = index & 3;
			var word = index >> 2;
			plans.SetBitplaneEntry(
				index,
				new RowDmaBitplaneEntry(
					checked((int)cycle),
					plane,
					word,
					index,
					(uint)(0x1000 + (index * 2)),
					rowPresent: true));
			trace.SetInitialFixedSlot(
				cycle,
				new FixedSlotPlanEntry(
					AgnusChipSlotOwner.Bitplane,
					(byte)plane,
					(byte)word),
				(uint)(0x1000 + (index * 2)));
		}

		plans.Commit(
			ringSlot: 0,
			new RowDmaPlan(
				generation: 1,
				row: 0,
				lineStartCycle: 0,
				dmacon: 0x03A0,
				bplcon0: 0x4000,
				dmaPlanVersion: 1,
				signature: 1,
				bitplaneStart: 0,
				bitplaneCount: BitplaneEntries,
				spriteStart: 0,
				spriteCount: SpriteEntries,
				valid: true));
		var kernel = new AgnusSlotKernel(
			AmigaChipset.OcsPal,
			maximumSlots: SlotsPerLine);
		kernel.Load(trace, new byte[64 * 1024]);
		return new DecisionBenchmarkFixture(plans, kernel);
	}

	private static BenchmarkOptions ParseOptions(string[] args)
	{
		var warmup = 1_000_000;
		var decisions = 10_000_000;
		var repeats = 7;
		for (var index = 0; index < args.Length; index++)
		{
			switch (args[index])
			{
				case ModeArgument:
				case LegacyModeArgument:
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
						$"Unknown Agnus slot-kernel benchmark argument '{args[index]}'.");
			}
		}

		if (warmup < 0 || decisions <= 0 || repeats <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(args),
				"Warmup must be non-negative; decisions and repeats must be positive.");
		}

		return new BenchmarkOptions(warmup, decisions, repeats);
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

	private readonly record struct DecisionBenchmarkFixture(
		AgnusRasterlineDmaPlanRing LegacyPlans,
		AgnusSlotKernel Kernel);

	private readonly record struct DecisionBenchmarkResult(
		int Decisions,
		TimeSpan Elapsed,
		long AllocatedBytes,
		ulong Checksum)
	{
		public double NanosecondsPerDecision
			=> Elapsed.TotalNanoseconds / Decisions;
	}
}
