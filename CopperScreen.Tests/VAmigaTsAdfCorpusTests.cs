using CopperMod.Amiga;
using System.Diagnostics;
using System.Text.Json;
using Xunit.Abstractions;

namespace CopperScreen.Tests;

public sealed class VAmigaTsAdfCorpusTests
{
	private const string RootEnvironmentVariable = "COPPER_AMIGA_VAMIGATS_ROOT";
	private const string CasesEnvironmentVariable = "COPPER_AMIGA_VAMIGATS_CASES";
	private const string MaxFramesEnvironmentVariable = "COPPER_AMIGA_VAMIGATS_MAX_FRAMES";
	private const string KickstartRomEnvironmentVariable = "COPPER_AMIGA_VAMIGATS_KICK13_ROM";
	private const string CompareRawEnvironmentVariable = "COPPER_AMIGA_VAMIGATS_COMPARE_RAW";
	private const string DumpRawEnvironmentVariable = "COPPER_AMIGA_VAMIGATS_DUMP_RAW";
	private const string HardwareSpecializationEnvironmentVariable = "COPPER_AMIGA_VAMIGATS_HARDWARE_SPECIALIZATION";
	private const string AgnusArbitrationEnvironmentVariable = "COPPER_AMIGA_VAMIGATS_AGNUS_ARBITRATION";
	private const string DisableDeferredCpuBatchEnvironmentVariable = "COPPER_AMIGA_VAMIGATS_DISABLE_DEFERRED_CPU_BATCH";
	private const string DisableDeferredCpuCompositionEnvironmentVariable = "COPPER_AMIGA_VAMIGATS_DISABLE_DEFERRED_CPU_COMPOSITION";
	private const string OpcodePlanDispatchEnvironmentVariable = "COPPER_AMIGA_VAMIGATS_OPCODE_PLAN_DISPATCH";
	private const string ForceGcBetweenCasesEnvironmentVariable = "COPPER_AMIGA_VAMIGATS_FORCE_GC_BETWEEN_CASES";
	private const string StopOnFirstFailureEnvironmentVariable = "COPPER_AMIGA_VAMIGATS_STOP_ON_FIRST_FAILURE";
	private const string TraceWritesEnvironmentVariable = "COPPER_AMIGA_VAMIGATS_TRACE_WRITES";
	private const string TracePresentationEnvironmentVariable = "COPPER_AMIGA_VAMIGATS_TRACE_PRESENTATION";
	private const string SkipRawOffsetScanEnvironmentVariable = "COPPER_AMIGA_VAMIGATS_SKIP_RAW_OFFSET_SCAN";
	private const string TraceCycleBoundariesOnlyEnvironmentVariable = "COPPER_AMIGA_VAMIGATS_TRACE_CYCLE_BOUNDARIES_ONLY";
	private const string AgnusLiveRequesterStageEnvironmentVariable = "COPPER_AMIGA_VAMIGATS_AGNUS_LIVE_STAGE";
	private const string CaseTimeoutSecondsEnvironmentVariable = "COPPER_AMIGA_VAMIGATS_CASE_TIMEOUT_SECONDS";
	private const string ProgressPathEnvironmentVariable = "COPPER_AMIGA_VAMIGATS_PROGRESS_PATH";
	private const string ResultsPathEnvironmentVariable = "COPPER_AMIGA_VAMIGATS_RESULTS_PATH";
	private const int DefaultMaxFramesPerCase = 180;
	private const int DefaultCaseTimeoutSeconds = 120;
	private const int ProgressFrameInterval = 10;
	private const int RawReferenceWidth = 716;
	private const int RawReferenceHeight = 285;
	private const int RawReferenceBytesPerPixel = 3;
	private const int RawOffsetScanMaxDx = 120;
	private const int RawOffsetScanMaxDy = 60;
	private const int RawFrameProbeRadius = 3;
	private const int MinVisibleFrame = 2;
	private const int MinNonBlackPixels = 512;
	private const int MinDistinctColors = 3;

	private static readonly string[] DefaultCases =
	[
		"Agnus/Blitter/bbusy/bbusy0/bbusy0.adf",
		"Agnus/Copper/Wait/copwait1/copwait1.adf",
		"Agnus/DDF/DDF/ddf1/ddf1.adf",
		"CIA/TOD/tod/tod1/tod1.adf",
		"Denise/Sprites/attached/attached1/attached1.adf",
		"Paula/Interrupts/basicint/basicint1/basicint1.adf"
	];

	private static readonly HashSet<string> VisibleFrameCases = new(StringComparer.OrdinalIgnoreCase)
	{
		"Agnus/Blitter/bbusy/bbusy0/bbusy0.adf"
	};

	private readonly ITestOutputHelper _output;

	public VAmigaTsAdfCorpusTests(ITestOutputHelper output)
	{
		_output = output;
	}

	[Theory]
	[InlineData(0xFF333333u, 0x303030)]
	[InlineData(0xFFCCCCCCu, 0xC0C0C0)]
	[InlineData(0xFF660066u, 0x60005F)]
	[InlineData(0xFFAA00AAu, 0xA0009F)]
	[InlineData(0xFF0000FFu, 0x0000EF)]
	[InlineData(0xFF00FFFFu, 0x00F0F0)]
	public void VAmigaRawColorConversionMatchesReferencePacking(uint argb, int expected)
	{
		Assert.Equal(expected, ToVAmigaRawColor(unchecked((int)argb)));
	}

	[Theory]
	[InlineData(0x0F0F10, 0x101010, true)]
	[InlineData(0xC04F60, 0xC05060, true)]
	[InlineData(0x3F4F9F, 0x4050A0, true)]
	[InlineData(0x0F0F10, 0x101012, false)]
	[InlineData(0xF000EF, 0x60005F, false)]
	public void VAmigaRawColorComparisonAllowsOnlyCaptureQuantization(int expected, int actual, bool matches)
	{
		Assert.Equal(matches, RawColorsMatch(expected, actual));
	}

	[Fact]
	public void Cycle01vRawReferenceDocumentsFirstPostBlackStripeOnset()
	{
		var root = ResolveVAmigaTsRoot();
		if (root == null)
		{
			return;
		}

		var rawPath = Path.Combine(root, "Agnus", "Registers", "VPOS", "cycle01v", "cycle01v_ocs.raw");
		if (!File.Exists(rawPath))
		{
			return;
		}

		var onset = FindFirstPostBlackStripeOnset(File.ReadAllBytes(rawPath));
		Assert.Equal((186, 168, 0xF000EF), onset);
	}

	[Fact]
	public void Cycle01vRawReferenceCopperLandmarksCalibrateCaptureCoordinates()
	{
		var root = ResolveVAmigaTsRoot();
		if (root == null)
		{
			return;
		}

		var rawPath = Path.Combine(root, "Agnus", "Registers", "VPOS", "cycle01v", "cycle01v_ocs.raw");
		if (!File.Exists(rawPath))
		{
			return;
		}

		var raw = File.ReadAllBytes(rawPath);
		const int copperLine = 0x40;
		const int copperClearHorizontal = 0xD9;
		const int setColor = 0xF00000;
		const int clearedBit15Color = 0xC0C0C0;
		var spans = FindRawColorSpans(raw, row: 38, setColor);

		Assert.Equal(26, copperLine - 38);
		Assert.Equal((0, 675), spans[0]);
		Assert.Equal(clearedBit15Color, ReadRawColor(raw, 38, 676));
		Assert.Equal(0, ReadRawColor(raw, 37, 0));
		Assert.NotEqual(
			(copperClearHorizontal - 0x38) * 4,
			676);
	}

	[Fact]
	public void Cycle01vRawReferenceDocumentsPostBlackStripeCadence()
	{
		var root = ResolveVAmigaTsRoot();
		if (root == null)
		{
			return;
		}

		var rawPath = Path.Combine(root, "Agnus", "Registers", "VPOS", "cycle01v", "cycle01v_ocs.raw");
		if (!File.Exists(rawPath))
		{
			return;
		}

		const int stripeColor = 0xF000EF;
		var raw = File.ReadAllBytes(rawPath);
		var onsetSpans = FindRawColorSpans(raw, row: 186, stripeColor);
		Assert.Equal((168, 191), onsetSpans[0]);

		var firstCompleteStripeStarts = new List<int>();
		for (var row = 189; row <= 207; row++)
		{
			var completeSpans = FindRawColorSpans(raw, row, stripeColor)
				.Where(span => span.Start > 0 && span.Stop < RawReferenceWidth - 1)
				.ToArray();
			Assert.NotEmpty(completeSpans);
			Assert.All(completeSpans, span => Assert.Equal(24, span.Stop - span.Start + 1));
			for (var index = 1; index < completeSpans.Length; index++)
			{
				Assert.Equal(68, completeSpans[index].Start - completeSpans[index - 1].Start);
			}

			firstCompleteStripeStarts.Add(completeSpans[0].Start);
		}

		Assert.Equal(
			new[] { 32, 8, 56, 32, 8, 56, 32, 8, 56, 32, 8, 56, 32, 8, 56, 32, 8, 56, 32 },
			firstCompleteStripeStarts);
	}

	[Fact]
	public void Cycle01vKickstartBootResetUsesRomVectors()
	{
		var root = ResolveVAmigaTsRoot();
		var kickstartRomPath = ResolveKickstartRomPath();
		if (root == null || kickstartRomPath == null)
		{
			return;
		}

		var adfPath = Path.Combine(
			root,
			NormalizeRelativePath("Agnus/Registers/VPOS/cycle01v/cycle01v.adf"));
		if (!File.Exists(adfPath))
		{
			return;
		}

		var hardwareSpecialization = IsEnabled(
			Environment.GetEnvironmentVariable(HardwareSpecializationEnvironmentVariable));
		using var emulator = CopperScreenEmulator.Create(
			CreateEmulatorArgs(adfPath, hardwareSpecialization, kickstartRomPath),
			AppContext.BaseDirectory);
		var machine = GetMachine(emulator);
		GetBoot(emulator).StartKickstartRomBoot();

		var rom = File.ReadAllBytes(kickstartRomPath);
		var expectedStackPointer = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(rom);
		var expectedProgramCounter = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(rom.AsSpan(4));
		Assert.Equal(expectedStackPointer, machine.Cpu.State.A[7]);
		Assert.Equal(expectedProgramCounter, machine.Cpu.State.ProgramCounter);
		Assert.Equal(expectedProgramCounter, machine.Bus.ReadLong(4));
		var romBaseAddress = 0x0100_0000u - checked((uint)rom.Length);
		var expectedOpcode = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(
			rom.AsSpan(checked((int)(expectedProgramCounter - romBaseAddress))));
		Assert.Equal(expectedOpcode, machine.Bus.ReadWord(expectedProgramCounter));
	}

	[Fact]
	public void Cycle01vKickstartBootDoesNotEnterUnmappedExecution()
	{
		var root = ResolveVAmigaTsRoot();
		var kickstartRomPath = ResolveKickstartRomPath();
		if (root == null || kickstartRomPath == null)
		{
			return;
		}

		var adfPath = Path.Combine(
			root,
			NormalizeRelativePath("Agnus/Registers/VPOS/cycle01v/cycle01v.adf"));
		if (!File.Exists(adfPath))
		{
			return;
		}

		var hardwareSpecialization = IsEnabled(
			Environment.GetEnvironmentVariable(HardwareSpecializationEnvironmentVariable));
		using var emulator = CopperScreenEmulator.Create(
			CreateEmulatorArgs(adfPath, hardwareSpecialization, kickstartRomPath),
			AppContext.BaseDirectory);
		var machine = GetMachine(emulator);
		var states = new List<string>();
		for (var frame = 1; frame <= 20; frame++)
		{
			emulator.RenderNextFrame();
			var state = machine.Cpu.State;
			var pc = state.ProgramCounter & 0x00FF_FFFF;
			states.Add(
				$"f{frame}:pc=0x{pc:X6},last=0x{state.LastInstructionProgramCounter & 0x00FF_FFFF:X6}," +
				$"sp=0x{state.A[7]:X8},sr=0x{state.StatusRegister:X4},cycles={state.Cycles}");
			var mapped = pc < machine.Bus.ChipRam.Length || pc >= 0x00F80000;
			Assert.True(
				mapped,
				$"Kickstart entered unmapped execution after frame {frame}.{Environment.NewLine}" +
				string.Join(Environment.NewLine, states));
		}
	}

	[Fact]
	public void Vhpos2RawReferenceDocumentsIrqVhposrValue()
	{
		var root = ResolveVAmigaTsRoot();
		if (root == null)
		{
			return;
		}

		var rawPath = Path.Combine(root, "Agnus", "Registers", "VPOS", "vhpos2", "vhpos2_ocs.raw");
		if (!File.Exists(rawPath))
		{
			return;
		}

		const int clearColor = 0x303030;
		const int setColor = 0xC0C0C0;
		var raw = File.ReadAllBytes(rawPath);
		ushort value = 0;
		for (var bit = 15; bit >= 0; bit--)
		{
			var row = 22 + ((15 - bit) * 8);
			var color = ReadRawColor(raw, row, 700);
			Assert.True(color is clearColor or setColor, $"bit={bit}, row={row}, color=#{color:X6}");
			if (color == setColor)
			{
				value |= (ushort)(1 << bit);
			}
		}

		Assert.Equal(0x0071, value);
	}

	[Fact]
	public void CorpusEmulatorOptionsConfigureDf0Writable()
	{
		var options = CreateEmulatorOptions(
			"vamigats-fixture.adf",
			hardwareSpecialization: false,
			kickstartRomPath: null);

		Assert.False(options.DriveWriteProtected[0]);
	}

	[Fact]
	public void SelectedVAmigaTsAdfImagesRunWithoutFatalBootStatusWhenCorpusIsAvailable()
	{
		var root = ResolveVAmigaTsRoot();
		if (root == null)
		{
			_output.WriteLine(
				$"vAmigaTS corpus not configured. Set {RootEnvironmentVariable} or clone to third_party/vAmigaTS.");
			return;
		}

		var cases = ResolveCases(root);
		Assert.NotEmpty(cases);

		var failures = new List<string>();
		var compareRaw = IsEnabled(Environment.GetEnvironmentVariable(CompareRawEnvironmentVariable));
		var dumpRaw = IsEnabled(Environment.GetEnvironmentVariable(DumpRawEnvironmentVariable));
		var hardwareSpecialization = IsEnabled(Environment.GetEnvironmentVariable(HardwareSpecializationEnvironmentVariable));
		var kickstartRomPath = ResolveKickstartRomPath();
		var stopOnFirstFailure = IsEnabled(Environment.GetEnvironmentVariable(StopOnFirstFailureEnvironmentVariable));
		var traceWrites = IsEnabled(Environment.GetEnvironmentVariable(TraceWritesEnvironmentVariable));
		var tracePresentation = IsEnabled(Environment.GetEnvironmentVariable(TracePresentationEnvironmentVariable));
		var agnusLiveRequesterStage = ResolveAgnusLiveRequesterStage();
		var caseTimeout = ResolveCaseTimeout();
		var progressPath = ResolveOptionalOutputPath(ProgressPathEnvironmentVariable);
		var resultsPath = ResolveOptionalOutputPath(ResultsPathEnvironmentVariable);
		InitializeCorpusOutput(progressPath, resultsPath, cases.Count, caseTimeout);
		for (var caseIndex = 0; caseIndex < cases.Count; caseIndex++)
		{
			var relativePath = cases[caseIndex];
			var caseNumber = caseIndex + 1;
			var stopwatch = Stopwatch.StartNew();
			WriteProgress(progressPath, $"START {caseNumber}/{cases.Count} {relativePath}");
			var result = RunCase(
				root,
				relativePath,
				ResolveMaxFrames(),
				compareRaw,
				dumpRaw,
				hardwareSpecialization,
				traceWrites,
				tracePresentation,
				agnusLiveRequesterStage,
				kickstartRomPath,
				caseTimeout,
				frame => WriteProgress(
					progressPath,
					$"HEARTBEAT {caseNumber}/{cases.Count} {relativePath} frame={frame} elapsed={stopwatch.Elapsed:c}"));
			stopwatch.Stop();
			_output.WriteLine(result.Summary);
			foreach (var diagnostic in result.Diagnostics)
			{
				_output.WriteLine(diagnostic);
			}
			var outcome = result.Failure == null ? "PASS" : "FAIL";
			WriteProgress(
				progressPath,
				$"END {caseNumber}/{cases.Count} {outcome} {relativePath} frames={result.Frames} elapsed={stopwatch.Elapsed:c}");
			AppendResult(resultsPath, caseNumber, cases.Count, result, stopwatch.Elapsed);
			if (IsEnabled(Environment.GetEnvironmentVariable(ForceGcBetweenCasesEnvironmentVariable)))
			{
				GC.Collect();
				GC.WaitForPendingFinalizers();
				GC.Collect();
			}

			if (result.Failure != null)
			{
				failures.Add(result.Failure);
				if (stopOnFirstFailure)
				{
					break;
				}
			}
		}
		WriteProgress(progressPath, $"COMPLETE cases={cases.Count} failures={failures.Count}");

		Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
	}

	private static VAmigaTsCaseResult RunCase(
		string root,
		string relativePath,
		int maxFrames,
		bool compareRaw,
		bool dumpRaw,
		bool hardwareSpecialization,
		bool traceWrites,
		bool tracePresentation,
		int agnusLiveRequesterStage,
		string? kickstartRomPath,
		TimeSpan caseTimeout,
		Action<int>? heartbeat)
	{
		var caseStopwatch = Stopwatch.StartNew();
		var previousOpcodePlanDispatch = M68kCoreFactory.M68000OpcodePlanDispatch;
		M68kCoreFactory.M68000OpcodePlanDispatch = ResolveOpcodePlanDispatch(previousOpcodePlanDispatch);
		Queue<M68kInstructionRetirementTrace>? vprobeInstructionRetirements = null;
		var waitbltGuestExecutionObserved = false;
		var waitbltUnmappedRetirementObserved = false;
		M68kInstructionRetirementTrace? waitbltFirstMainLoopRetirement = null;
		var waitbltStartupTail = new Queue<M68kInstructionRetirementTrace>(64);
		M68kInstructionRetirementTrace[]? waitbltStartupRetirements = null;
		string? waitbltStartupBusLedger = null;
		var waitbltFirstMainLoopRetirements = new List<M68kInstructionRetirementTrace>(8);
		var adfPath = Path.Combine(root, NormalizeRelativePath(relativePath));
		if (!File.Exists(adfPath))
		{
			return VAmigaTsCaseResult.Fail(relativePath, 0, string.Empty, 0, 0, $"vAmigaTS ADF not found: {relativePath}", []);
		}

		try
		{
			var startupOptions = CreateEmulatorOptions(
				adfPath,
				hardwareSpecialization,
				kickstartRomPath);
			if (kickstartRomPath != null)
			{
				Assert.True(startupOptions.Profile.UsesKickstartRom);
				Assert.Equal(Path.GetFullPath(kickstartRomPath), startupOptions.KickstartRomPath);
				Assert.Null(startupOptions.Error);
			}
			using var emulator = agnusLiveRequesterStage != 0
				? CopperScreenEmulator.CreateForAgnusLiveRequesterStageTests(
					startupOptions,
					agnusLiveRequesterStage)
				: CopperScreenEmulator.Create(startupOptions);
			if (agnusLiveRequesterStage != 0)
			{
				var liveBus = GetMachine(emulator).Bus;
				Assert.Equal(
					agnusLiveRequesterStage >= 1,
					liveBus.AgnusLiveDisplayLedgerEnabled);
				Assert.Equal(
					agnusLiveRequesterStage >= 2,
					liveBus.AgnusLiveCopperEnabled);
				Assert.Equal(
					agnusLiveRequesterStage >= 3,
					liveBus.AgnusLiveBlitterEnabled);
				Assert.Equal(
					agnusLiveRequesterStage >= 5,
					liveBus.AgnusLiveDiskEnabled);
			}
			Cycle01vDelayedFetchSlotTrace? cycle01vSlotTrace = null;
			Cycle01vBootProgressTrace? cycle01vBootTrace = null;
			var isProbe10 = NormalizeCasePath(relativePath).Equals(
				"Agnus/Registers/VPOS/probe10/probe10.adf",
				StringComparison.OrdinalIgnoreCase);
			var normalizedCasePath = NormalizeCasePath(relativePath);
			var isVprobe = normalizedCasePath.Equals(
					"Agnus/Registers/VPOS/vprobe2/vprobe2.adf",
					StringComparison.OrdinalIgnoreCase) ||
				normalizedCasePath.Equals(
					"Agnus/Registers/VPOS/vprobe3/vprobe3.adf",
					StringComparison.OrdinalIgnoreCase);
			var isWaitblt = normalizedCasePath.StartsWith(
				"Agnus/Copper/Wait/waitblt",
				StringComparison.OrdinalIgnoreCase);
			var isVhpos2 = normalizedCasePath.Equals(
				"Agnus/Registers/VPOS/vhpos2/vhpos2.adf",
				StringComparison.OrdinalIgnoreCase);
			var isCycle01v =
				normalizedCasePath.Equals(
					"Agnus/Registers/VPOS/cycle01v/cycle01v.adf",
					StringComparison.OrdinalIgnoreCase) ||
				normalizedCasePath.Equals(
					"Agnus/Registers/VPOS/cycle01vh/cycle01vh.adf",
					StringComparison.OrdinalIgnoreCase) ||
				normalizedCasePath.Equals(
					"Agnus/Registers/VPOS/cycleD9v/cycleD9v.adf",
					StringComparison.OrdinalIgnoreCase) ||
				normalizedCasePath.Equals(
					"Agnus/Registers/VPOS/cycleD9vh/cycleD9vh.adf",
					StringComparison.OrdinalIgnoreCase);
			if (traceWrites && isProbe10)
			{
				var machine = GetMachine(emulator);
				machine.Bus.CaptureCpuChipRamWriteTrace(0x00070000, 0x10000, 131072);
				machine.Bus.CaptureCustomRegisterReadTrace(0x006, 2, 1048576);
				machine.CaptureInterruptDispatchTrace(2048, busPhaseWindowCycles: 128);
			}
			else if (tracePresentation && isProbe10)
			{
				GetMachine(emulator).Bus.CaptureCustomRegisterReadTrace(0x006, 2, 1048576);
			}
			else if (traceWrites && isVprobe)
			{
				var machine = GetMachine(emulator);
				vprobeInstructionRetirements = new Queue<M68kInstructionRetirementTrace>(32768);
				machine.Bus.SetCpuInstructionRetirementObserver(trace =>
				{
					vprobeInstructionRetirements.Enqueue(trace);
					if (vprobeInstructionRetirements.Count > 32768)
					{
						vprobeInstructionRetirements.Dequeue();
					}
				});
				machine.Bus.CaptureCpuChipRamWriteTrace(0x00070000, 0x10000, 16384);
				// The probe's vertical handoff reads VHPOSR and then performs
				// a read-modify-write on VPOSR. Capture the pair so the causal
				// exit boundary is visible in the same gated diagnostic.
				machine.Bus.CaptureCustomRegisterReadTrace(0x004, 4, 4096);
				machine.CaptureInterruptDispatchTrace(2048, busPhaseWindowCycles: 128);
			}
			else if (traceWrites && isVhpos2)
			{
				var machine = GetMachine(emulator);
				machine.Bus.CaptureCustomRegisterReadTrace(0x006, 2, 1048576);
				machine.CaptureInterruptDispatchTrace(2048, busPhaseWindowCycles: 128);
			}
			else if (traceWrites && isCycle01v)
			{
				var machine = GetMachine(emulator);
				machine.Bus.CaptureCpuChipRamWriteTrace(0x00070000, 0x10000, 131072);
				machine.Bus.CaptureCustomRegisterReadTrace(0x004, 4, 1048576);
				machine.CaptureInterruptDispatchTrace(65536, busPhaseWindowCycles: 128);
				cycle01vSlotTrace = new Cycle01vDelayedFetchSlotTrace(machine.Bus);
				cycle01vBootTrace = new Cycle01vBootProgressTrace(machine);
				cycle01vBootTrace.Capture(frame: 0);
				machine.Bus.SetSlotScheduleAuditSink(cycle01vSlotTrace.Capture);
			}
			else if (traceWrites &&
				normalizedCasePath.StartsWith(
					"Agnus/Blitter/",
					StringComparison.OrdinalIgnoreCase))
			{
				GetMachine(emulator).CaptureInterruptDispatchTrace(
					2048,
					busPhaseWindowCycles: 160);
			}
			else if (traceWrites && isWaitblt)
			{
				var machine = GetMachine(emulator);
				vprobeInstructionRetirements = new Queue<M68kInstructionRetirementTrace>(32768);
				machine.Bus.SetCpuInstructionRetirementObserver(trace =>
				{
					if (waitbltUnmappedRetirementObserved)
					{
						return;
					}

					vprobeInstructionRetirements.Enqueue(trace);
					if (vprobeInstructionRetirements.Count > 32768)
					{
						vprobeInstructionRetirements.Dequeue();
					}

					var pc = trace.ProgramCounter & 0x00FF_FFFF;
					if (pc == 0x070218 && !waitbltFirstMainLoopRetirement.HasValue)
					{
						waitbltFirstMainLoopRetirement = trace;
						waitbltStartupRetirements = waitbltStartupTail.Append(trace).ToArray();
						waitbltStartupBusLedger = FormatBusAccesses(
							machine.Bus.BusAccesses.Where(access =>
								access.GrantedCycle >= trace.StartCycle - 48 &&
								access.GrantedCycle <= trace.RetireCycle + 4));
					}
					if (pc == 0x070218 && waitbltFirstMainLoopRetirements.Count < 8)
					{
						waitbltFirstMainLoopRetirements.Add(trace);
					}
					if (waitbltStartupRetirements == null)
					{
						waitbltStartupTail.Enqueue(trace);
						if (waitbltStartupTail.Count > 64)
						{
							waitbltStartupTail.Dequeue();
						}
					}
					waitbltGuestExecutionObserved |=
						pc >= 0x00F8_0000 ||
						(trace.StartCycle >= 20_000_000 &&
							pc is >= 0x070000 and < 0x080000);
					waitbltUnmappedRetirementObserved =
						waitbltGuestExecutionObserved &&
						(pc == 0 ||
							(pc >= AmigaConstants.A500BootChipRamSize && pc < 0x00F8_0000));
				});
				machine.CaptureInterruptDispatchTrace(2048, busPhaseWindowCycles: 160);
			}
			else if (traceWrites &&
				normalizedCasePath.StartsWith(
					"Paula/Drive/",
					StringComparison.OrdinalIgnoreCase))
			{
				GetMachine(emulator).CaptureInterruptDispatchTrace(
					4096,
					busPhaseWindowCycles: 160);
			}

			var bestNonBlack = 0;
			var bestDistinctColors = 0;
			var renderedVisibleFrame = false;
			var framesRendered = 0;
			var expectsVisibleFrame = VisibleFrameCases.Contains(NormalizeCasePath(relativePath));
			var rawComparisonFrame = compareRaw
				? Math.Max(maxFrames, ResolveRetroshScreenshotFrameCount(adfPath) ?? maxFrames)
				: maxFrames;
			var targetFrames = compareRaw ? rawComparisonFrame + RawFrameProbeRadius : maxFrames;
			var rawFrameProbes = compareRaw ? new Dictionary<int, int[]>() : null;
			var rawFrameCaptureTraces = traceWrites && isCycle01v && compareRaw &&
				!IsEnabled(Environment.GetEnvironmentVariable(TraceCycleBoundariesOnlyEnvironmentVariable))
				? new Dictionary<int, string>()
				: null;
			string[]? probe10PresentationTrace = null;
			var probe10FramePhaseLedger = (traceWrites || tracePresentation) && isProbe10 && compareRaw
				? new Dictionary<int, string>()
				: null;
			var probe10InterruptLedger = traceWrites && isProbe10 && compareRaw
				? new Dictionary<int, string>()
				: null;
			var waitbltPresentationFrameLedger = tracePresentation && isWaitblt && compareRaw
				? new Dictionary<int, string>()
				: null;

			for (; framesRendered < targetFrames; framesRendered++)
			{
				if (caseStopwatch.Elapsed >= caseTimeout)
				{
					return CreateCaseTimeoutResult(
						relativePath,
						framesRendered,
						emulator.StatusText,
						bestNonBlack,
						bestDistinctColors,
						caseTimeout);
				}

			emulator.RenderNextFrame();
				var currentFrame = framesRendered + 1;
				if (currentFrame == 1 || currentFrame % ProgressFrameInterval == 0)
				{
					heartbeat?.Invoke(currentFrame);
				}
				if (caseStopwatch.Elapsed >= caseTimeout)
				{
					return CreateCaseTimeoutResult(
						relativePath,
						currentFrame,
						emulator.StatusText,
						bestNonBlack,
						bestDistinctColors,
						caseTimeout);
				}
				cycle01vBootTrace?.Capture(currentFrame);
				var nonBlack = CountNonBlackPixels(emulator.Framebuffer);
				var distinctColors = CountDistinctColors(emulator.Framebuffer, MinDistinctColors);
				bestNonBlack = Math.Max(bestNonBlack, nonBlack);
				bestDistinctColors = Math.Max(bestDistinctColors, distinctColors);

				if (rawFrameProbes != null &&
					Math.Abs(currentFrame - rawComparisonFrame) <= RawFrameProbeRadius)
				{
					rawFrameProbes[currentFrame] = emulator.Framebuffer.ToArray();
					if (waitbltPresentationFrameLedger != null)
					{
						var machine = GetMachine(emulator);
						var display = machine.Bus.Display;
						var irq = machine.InterruptDispatchTrace.LastOrDefault(dispatch => dispatch.Level == 3);
						var colorPhase = machine.Bus.CpuBusPhases.LastOrDefault(phase =>
							(phase.CpuPhase.Address & 0x00FF_FFFE) == 0x00DFF180);
						var prefetch = irq.PrefetchBefore;
						waitbltPresentationFrameLedger[currentFrame] = string.Join(
							"; ",
							display.RenderedCopperTimelineSegments
								.Where(segment => segment.Row is 22 or 192 or 214 or 234 or 248)
								.Select(segment =>
									$"row={segment.Row} {segment.XStart}-{segment.XStop} " +
									$"color0=0x{segment.Color0:X3}")) +
							$" irq={irq.CpuVisibleCycle},{irq.CpuSampleCycle},{irq.AcceptanceCycle},{irq.EntryCompletedCycle}" +
							$" color={colorPhase.BusAccess?.GrantedCycle ?? -1}" +
							(prefetch.HasValue
								? $" pf=q{prefetch.Value.PrefetchCount}/tail{prefetch.Value.InterruptEntryBranchTailAdjustmentCycles}"
								: string.Empty);
					}
					if (rawFrameCaptureTraces != null)
					{
						var machine = GetMachine(emulator);
						rawFrameCaptureTraces[currentFrame] = FormatCycleAdfIterationBoundaries(
							machine,
							machine.Bus.CustomRegisterReads.ToArray());
					}
					if ((traceWrites || tracePresentation) && isProbe10 && currentFrame == rawComparisonFrame)
					{
						probe10PresentationTrace = FormatProbe10PresentationTrace(
							GetMachine(emulator),
							adfPath,
							emulator.Framebuffer,
							currentFrame);
					}
					if (probe10InterruptLedger != null &&
						Math.Abs(currentFrame - rawComparisonFrame) <= RawFrameProbeRadius)
					{
						probe10InterruptLedger[currentFrame] = FormatProbe10InterruptLedger(GetMachine(emulator), currentFrame);
					}
				}
				if (probe10FramePhaseLedger != null &&
					(currentFrame <= 12 || Math.Abs(currentFrame - rawComparisonFrame) <= RawFrameProbeRadius))
				{
					probe10FramePhaseLedger[currentFrame] = FormatProbe10FramePhaseLedger(GetMachine(emulator));
				}

				if (IsFatalBootStatus(emulator.StatusText))
				{
					break;
				}

				if (framesRendered >= MinVisibleFrame &&
					nonBlack >= MinNonBlackPixels &&
					distinctColors >= MinDistinctColors)
				{
					renderedVisibleFrame = true;
					if (expectsVisibleFrame)
					{
						if (!compareRaw)
						{
							break;
						}
					}
				}
			}

			var traceCycleBoundariesOnly = IsEnabled(Environment.GetEnvironmentVariable(TraceCycleBoundariesOnlyEnvironmentVariable));
			var diagnostics = (traceWrites || tracePresentation)
				? traceCycleBoundariesOnly
					? [$"TRACE {relativePath}: cycle ADF boundaries {FormatCycleAdfIterationBoundarySummary(GetMachine(emulator), GetMachine(emulator).Bus.CustomRegisterReads.ToArray())}"]
					: TraceCustomRegisterWrites(relativePath, GetMachine(emulator))
				: [];
			if (vprobeInstructionRetirements != null && !traceCycleBoundariesOnly)
			{
				diagnostics = diagnostics.Append(
					FormatVprobeInstructionRetirements(
						relativePath,
						GetMachine(emulator),
						vprobeInstructionRetirements))
					.ToArray();
				if (waitbltFirstMainLoopRetirement.HasValue)
				{
					var firstLoop = waitbltFirstMainLoopRetirement.Value;
					diagnostics = diagnostics.Append(
						$"TRACE {relativePath}: first BRA loop " +
						$"start={FormatBeam(firstLoop.StartCycle)} " +
						$"retire={FormatBeam(firstLoop.RetireCycle)} " +
						$"delta={firstLoop.RetireCycle - firstLoop.StartCycle}")
						.ToArray();
					diagnostics = diagnostics.Append(
						$"TRACE {relativePath}: startup to BRA " +
						string.Join(' ', (waitbltStartupRetirements ?? []).Select(trace =>
							$"{trace.ProgramCounter & 0x00FF_FFFF:X6}/" +
							$"{trace.Opcode:X4}@{FormatBeam(trace.StartCycle)}->" +
							$"{FormatBeam(trace.RetireCycle)}/" +
							$"{trace.RetireCycle - trace.StartCycle}")))
						.ToArray();
					diagnostics = diagnostics.Append(
						$"TRACE {relativePath}: startup BRA bus [{waitbltStartupBusLedger}]")
						.ToArray();
					diagnostics = diagnostics.Append(
						$"TRACE {relativePath}: first BRA retirements " +
						string.Join(' ', waitbltFirstMainLoopRetirements.Select(trace =>
							$"{FormatBeam(trace.StartCycle)}->" +
							$"{FormatBeam(trace.RetireCycle)}/" +
							$"{trace.RetireCycle - trace.StartCycle}")))
						.ToArray();
					foreach (var beamLine in new[] { 48, 240, 304 })
					{
						var lineBraRetirements = vprobeInstructionRetirements
							.Where(trace =>
								(trace.ProgramCounter & 0x00FF_FFFF) == 0x070218 &&
								GetPalBeamLine(trace.StartCycle) == beamLine)
							.Take(32);
						diagnostics = diagnostics.Append(
							$"TRACE {relativePath}: steady v{beamLine} BRA retirements " +
							string.Join(' ', lineBraRetirements.Select(trace =>
								$"{FormatBeam(trace.StartCycle)}->" +
								$"{FormatBeam(trace.RetireCycle)}/" +
								$"{trace.RetireCycle - trace.StartCycle}")))
							.ToArray();
						var lineBraPhases = GetMachine(emulator).Bus.CpuBusPhases
							.Where(phase =>
								(phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF) == 0x070218 &&
								GetPalBeamLine(phase.CpuPhase.RequestedCycle) == beamLine)
							.Take(40);
						diagnostics = diagnostics.Append(
							$"TRACE {relativePath}: steady v{beamLine} BRA bus " +
							FormatCpuBusPhases(lineBraPhases))
							.ToArray();
					}
				}
			}
			if (traceWrites && (isVprobe || isCycle01v) && !traceCycleBoundariesOnly)
			{
				diagnostics = diagnostics.Append(
					FormatExpiredDbraTransitions(relativePath, GetMachine(emulator)))
					.ToArray();
			}
			if (cycle01vSlotTrace != null && !traceCycleBoundariesOnly)
			{
				diagnostics = diagnostics.Append(
					$"TRACE {relativePath}: delayed fetch slot owners {cycle01vSlotTrace.Format()}")
					.ToArray();
			}
			if (cycle01vBootTrace != null && !traceCycleBoundariesOnly)
			{
				diagnostics = diagnostics.Append(
					$"TRACE {relativePath}: boot progress {cycle01vBootTrace.Format()}")
					.ToArray();
			}
			if (rawFrameCaptureTraces != null)
			{
				diagnostics = diagnostics.Concat(
					rawFrameCaptureTraces
						.OrderBy(pair => pair.Key)
						.Select(pair => $"TRACE {relativePath}: raw capture frame {pair.Key}: {pair.Value}"))
					.ToArray();
			}
			if (probe10PresentationTrace != null)
			{
				diagnostics = diagnostics.Concat(probe10PresentationTrace).ToArray();
			}
			if (probe10FramePhaseLedger != null)
			{
				diagnostics = diagnostics.Concat(
					probe10FramePhaseLedger
						.OrderBy(pair => pair.Key)
						.Select(pair => $"TRACE probe10 framePhase frame={pair.Key}: {pair.Value}"))
					.ToArray();
			}
			if (probe10InterruptLedger != null)
			{
				diagnostics = diagnostics.Concat(
					probe10InterruptLedger
						.OrderBy(pair => pair.Key)
						.Select(pair => $"TRACE probe10 interruptLedger frame={pair.Key}: {pair.Value}")
						.ToArray()).ToArray();
			}
			if (waitbltPresentationFrameLedger != null)
			{
				diagnostics = diagnostics.Concat(
					waitbltPresentationFrameLedger
						.OrderBy(pair => pair.Key)
					.Select(pair => $"TRACE waitblt presentation frame={pair.Key}: {pair.Value}"))
					.ToArray();
			}
			var frameCount = Math.Min(framesRendered + 1, targetFrames);

			if (IsFatalBootStatus(emulator.StatusText))
			{
				return VAmigaTsCaseResult.Fail(
					relativePath,
					frameCount,
					emulator.StatusText,
					bestNonBlack,
					bestDistinctColors,
					$"{relativePath} stopped with fatal boot status '{emulator.StatusText}'.",
					diagnostics);
			}

			if (expectsVisibleFrame && !renderedVisibleFrame)
			{
				return VAmigaTsCaseResult.Fail(
					relativePath,
					frameCount,
					emulator.StatusText,
					bestNonBlack,
					bestDistinctColors,
					$"{relativePath} did not render a visible frame within {maxFrames} frames. " +
					$"Best non-black pixels={bestNonBlack}, best distinct colors={bestDistinctColors}, " +
					$"status='{emulator.StatusText}'.",
					diagnostics);
			}

			if (compareRaw)
			{
				var primaryRawFrame = rawFrameProbes != null && rawFrameProbes.TryGetValue(rawComparisonFrame, out var capturedFrame)
					? capturedFrame
					: emulator.Framebuffer.ToArray();
				var rawComparison = CompareRawReference(adfPath, primaryRawFrame);
				diagnostics = diagnostics.Concat(rawComparison.Diagnostics).ToArray();
				if (rawFrameProbes != null)
				{
					diagnostics = diagnostics.Concat(CompareRawReferenceFrameWindow(adfPath, rawFrameProbes, rawComparisonFrame)).ToArray();
				}

				if (dumpRaw)
				{
					DumpRawFrame(adfPath, primaryRawFrame);
				}

				if (rawComparison.Failure != null)
				{
					var failure = rawComparison.Failure;
					if (isProbe10 && traceWrites)
					{
						var irq1Dispatches = GetMachine(emulator).InterruptDispatchTrace
							.Where(dispatch => dispatch.Level == 1)
							.Take(4)
							.ToArray();
						var machine = GetMachine(emulator);
						failure += $"; irq1={FormatInterruptDispatches(irq1Dispatches)}";
						failure += $"; irq1Phases={FormatProbe10Irq1FirstReadPhases(machine, irq1Dispatches)}";
					}

					return VAmigaTsCaseResult.Fail(
						relativePath,
						frameCount,
						emulator.StatusText,
						bestNonBlack,
						bestDistinctColors,
						failure,
						diagnostics);
				}
			}

			return VAmigaTsCaseResult.Pass(relativePath, frameCount, emulator.StatusText, bestNonBlack, bestDistinctColors, diagnostics);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or AmigaEmulationException)
		{
			var diagnostics = vprobeInstructionRetirements == null
				? Array.Empty<string>()
				:
				[
					"TRACE final retirements " +
					string.Join(
						' ',
						vprobeInstructionRetirements
							.TakeLast(32768)
							.Select(trace =>
								$"{trace.ProgramCounter & 0x00FF_FFFF:X6}/" +
								$"{trace.Opcode:X4}@" +
								$"{trace.StartCycle}->{trace.RetireCycle}"))
				];
			return VAmigaTsCaseResult.Fail(
				relativePath,
				0,
				string.Empty,
				0,
				0,
				$"{relativePath} threw {ex}",
				diagnostics);
		}
		finally
		{
			M68kCoreFactory.M68000OpcodePlanDispatch = previousOpcodePlanDispatch;
		}
	}

	private static M68kOpcodePlanDispatch ResolveOpcodePlanDispatch(M68kOpcodePlanDispatch fallback)
	{
		return Environment.GetEnvironmentVariable(OpcodePlanDispatchEnvironmentVariable)?.Trim().ToLowerInvariant() switch
		{
			"scalar" => M68kOpcodePlanDispatch.Scalar,
			"kind" or "kindtable" => M68kOpcodePlanDispatch.KindTable,
			"packed" or "packedplan" => M68kOpcodePlanDispatch.PackedPlan,
			_ => fallback
		};
	}

	private static VAmigaTsCaseResult CreateCaseTimeoutResult(
		string relativePath,
		int frames,
		string status,
		int bestNonBlack,
		int bestDistinctColors,
		TimeSpan timeout)
		=> VAmigaTsCaseResult.Fail(
			relativePath,
			frames,
			status,
			bestNonBlack,
			bestDistinctColors,
			$"{relativePath} exceeded the per-case timeout of {timeout.TotalSeconds:F0} seconds at frame {frames}.",
			[]);

	private static IReadOnlyList<string> ResolveCases(string root)
	{
		var configured = Environment.GetEnvironmentVariable(CasesEnvironmentVariable);
		if (string.IsNullOrWhiteSpace(configured))
		{
			return DefaultCases;
		}

		var cases = new List<string>();
		foreach (var entry in configured.Split([';', ',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			if (entry.Equals("default", StringComparison.OrdinalIgnoreCase))
			{
				cases.AddRange(DefaultCases);
				continue;
			}

			if (entry.Equals("all", StringComparison.OrdinalIgnoreCase))
			{
				AddAdfsFromDirectory(root, root, cases);
				continue;
			}

			var candidate = Path.Combine(root, NormalizeRelativePath(entry));
			if (Directory.Exists(candidate))
			{
				AddAdfsFromDirectory(root, candidate, cases);
				continue;
			}

			cases.Add(NormalizeCasePath(entry));
		}

		return cases
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Order(StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	private static int ResolveMaxFrames()
	{
		var configured = Environment.GetEnvironmentVariable(MaxFramesEnvironmentVariable);
		if (string.IsNullOrWhiteSpace(configured))
		{
			return DefaultMaxFramesPerCase;
		}

		Assert.True(
			int.TryParse(configured, out var frames) && frames > 0,
			$"{MaxFramesEnvironmentVariable} must be a positive integer.");
		return frames;
	}

	private static int ResolveAgnusLiveRequesterStage()
	{
		var configured = Environment.GetEnvironmentVariable(
			AgnusLiveRequesterStageEnvironmentVariable);
		if (string.IsNullOrWhiteSpace(configured))
		{
			return 0;
		}

		Assert.True(
			int.TryParse(configured, out var stage) && stage is >= 0 and <= 5,
			$"{AgnusLiveRequesterStageEnvironmentVariable} must be an integer from 0 through 5.");
		return stage;
	}

	private static TimeSpan ResolveCaseTimeout()
	{
		var configured = Environment.GetEnvironmentVariable(CaseTimeoutSecondsEnvironmentVariable);
		if (string.IsNullOrWhiteSpace(configured))
		{
			return TimeSpan.FromSeconds(DefaultCaseTimeoutSeconds);
		}

		Assert.True(
			int.TryParse(configured, out var seconds) && seconds > 0,
			$"{CaseTimeoutSecondsEnvironmentVariable} must be a positive integer.");
		return TimeSpan.FromSeconds(seconds);
	}

	private static string? ResolveOptionalOutputPath(string environmentVariable)
	{
		var configured = Environment.GetEnvironmentVariable(environmentVariable);
		if (string.IsNullOrWhiteSpace(configured))
		{
			return null;
		}

		return Path.GetFullPath(configured);
	}

	private static void InitializeCorpusOutput(
		string? progressPath,
		string? resultsPath,
		int caseCount,
		TimeSpan caseTimeout)
	{
		if (progressPath != null)
		{
			EnsureParentDirectory(progressPath);
			File.WriteAllText(
				progressPath,
				$"{DateTimeOffset.UtcNow:O} BEGIN cases={caseCount} timeoutSeconds={caseTimeout.TotalSeconds:F0}{Environment.NewLine}");
		}

		if (resultsPath != null)
		{
			EnsureParentDirectory(resultsPath);
			File.WriteAllText(resultsPath, string.Empty);
		}
	}

	private static void WriteProgress(string? path, string message)
	{
		if (path == null)
		{
			return;
		}

		File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
	}

	private static void AppendResult(
		string? path,
		int caseNumber,
		int caseCount,
		VAmigaTsCaseResult result,
		TimeSpan elapsed)
	{
		if (path == null)
		{
			return;
		}

		var json = JsonSerializer.Serialize(new
		{
			timestamp = DateTimeOffset.UtcNow,
			caseNumber,
			caseCount,
			outcome = result.Failure == null ? "PASS" : "FAIL",
			path = result.RelativePath,
			elapsedMilliseconds = (long)elapsed.TotalMilliseconds,
			frames = result.Frames,
			status = result.Status,
			bestNonBlack = result.BestNonBlack,
			bestDistinctColors = result.BestDistinctColors,
			failure = result.Failure
		});
		File.AppendAllText(path, json + Environment.NewLine);
	}

	private static void EnsureParentDirectory(string path)
	{
		var directory = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}
	}

	private static string? ResolveKickstartRomPath()
	{
		var configured = Environment.GetEnvironmentVariable(KickstartRomEnvironmentVariable);
		if (string.IsNullOrWhiteSpace(configured))
		{
			return null;
		}

		var path = Path.GetFullPath(configured);
		Assert.True(File.Exists(path), $"{KickstartRomEnvironmentVariable} points to a missing file: {path}");
		return path;
	}

	private static string[] CreateEmulatorArgs(string adfPath, bool hardwareSpecialization, string? kickstartRomPath)
	{
		var args = new List<string>(8);
		if (string.IsNullOrWhiteSpace(kickstartRomPath))
		{
			args.AddRange(["--profile", "expanded-copperstart"]);
		}
		else
		{
			args.AddRange(["--profile", "vanilla-kickstart13", "--kickstart-rom", kickstartRomPath]);
		}

		if (hardwareSpecialization)
		{
			args.Add("--hardware-specialization");
		}

		switch (Environment.GetEnvironmentVariable(AgnusArbitrationEnvironmentVariable)?.Trim().ToLowerInvariant())
		{
			case "legacy":
			case "forced-legacy":
				args.Add("--agnus-legacy");
				break;
			case "slot-kernel":
				args.Add("--agnus-slot-kernel");
				break;
		}

		if (IsEnabled(Environment.GetEnvironmentVariable(DisableDeferredCpuBatchEnvironmentVariable)))
		{
			args.Add("--no-cpu-deferred-bus-batch");
		}

		if (IsEnabled(Environment.GetEnvironmentVariable(DisableDeferredCpuCompositionEnvironmentVariable)))
		{
			args.Add("--no-cpu-deferred-custom-composition-writes");
		}

		args.Add(adfPath);
		return args.ToArray();
	}

	private static CopperScreenStartupOptions CreateEmulatorOptions(
		string adfPath,
		bool hardwareSpecialization,
		string? kickstartRomPath)
	{
		var options = CopperScreenStartupOptions.Parse(
			CreateEmulatorArgs(adfPath, hardwareSpecialization, kickstartRomPath),
			AppContext.BaseDirectory);
		options.DriveWriteProtected[0] = false;
		return options;
	}

	private static int? ResolveRetroshScreenshotFrameCount(string adfPath)
	{
		var retroshPath = ResolveReferencePath(adfPath, ".retrosh");
		if (!File.Exists(retroshPath))
		{
			return null;
		}

		foreach (var line in File.ReadLines(retroshPath))
		{
			var trimmed = line.Trim();
			if (!trimmed.StartsWith("wait ", StringComparison.OrdinalIgnoreCase) ||
				!trimmed.EndsWith(" seconds", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var secondsText = trimmed["wait ".Length..^" seconds".Length].Trim();
			if (int.TryParse(secondsText, out var seconds) && seconds > 0)
			{
				return (int)Math.Ceiling(seconds * AmigaConstants.A500PalVBlankHz);
			}
		}

		return null;
	}

	private static string ResolveReferencePath(string adfPath, string extension)
	{
		var exactPath = Path.ChangeExtension(adfPath, extension);
		if (File.Exists(exactPath))
		{
			return exactPath;
		}

		var directory = Path.GetDirectoryName(adfPath) ?? ".";
		var stem = Path.GetFileNameWithoutExtension(adfPath);
		foreach (var suffix in new[] { "_OCS", "_ECS" })
		{
			var variantPath = Path.Combine(directory, stem + suffix + extension);
			if (File.Exists(variantPath))
			{
				return variantPath;
			}
		}

		return exactPath;
	}

	private static RawComparisonResult CompareRawReference(string adfPath, ReadOnlySpan<int> framebuffer)
	{
		var rawPath = ResolveReferencePath(adfPath, ".raw");
		if (!File.Exists(rawPath))
		{
			return new RawComparisonResult($"Raw reference not found: {rawPath}", []);
		}

		var raw = File.ReadAllBytes(rawPath);
		var expectedLength = RawReferenceWidth * RawReferenceHeight * RawReferenceBytesPerPixel;
		if (raw.Length != expectedLength)
		{
			return new RawComparisonResult(
				$"Raw reference has unexpected length {raw.Length}; expected {expectedLength}: {rawPath}",
				[]);
		}

		var mismatches = 0;
		var firstMismatch = string.Empty;
		var firstMismatchRow = -1;
		for (var y = 0; y < RawReferenceHeight; y++)
		{
			var frameRow = y * 2;
			for (var x = 0; x < RawReferenceWidth; x++)
			{
				var rawOffset = ((y * RawReferenceWidth) + x) * RawReferenceBytesPerPixel;
				var actual = framebuffer[(frameRow * RawReferenceWidth) + x];
				var actualRaw = ToVAmigaRawColor(actual);
				var actualR = (actualRaw >> 16) & 0xFF;
				var actualG = (actualRaw >> 8) & 0xFF;
				var actualB = actualRaw & 0xFF;
				if (RawColorsMatch(raw, rawOffset, actualRaw))
				{
					continue;
				}

				mismatches++;
				if (firstMismatch.Length == 0)
				{
					firstMismatchRow = y;
					firstMismatch =
						$"first mismatch at ({x},{y}): expected #{raw[rawOffset]:X2}{raw[rawOffset + 1]:X2}{raw[rawOffset + 2]:X2}, " +
						$"actual #{actualR:X2}{actualG:X2}{actualB:X2}";
				}
			}
		}

		if (mismatches == 0)
		{
			return RawComparisonResult.Pass;
		}

		var geometry = $"{FindRawBounds(raw, framebuffer)}; ";
		geometry += IsEnabled(Environment.GetEnvironmentVariable(SkipRawOffsetScanEnvironmentVariable))
			? "raw offset scan skipped"
			: $"{FindBestRawOffset(raw, framebuffer)}; {FindBestRawSourceYOffset(raw, framebuffer)}";
		return new RawComparisonResult(
			$"Raw reference mismatch: {mismatches}/{RawReferenceWidth * RawReferenceHeight} pixels differ; {firstMismatch}; {geometry}",
			BuildRawMismatchDiagnostics(rawPath, raw, framebuffer, firstMismatchRow));
	}

	private static string[] CompareRawReferenceFrameWindow(
		string adfPath,
		IReadOnlyDictionary<int, int[]> frames,
		int primaryFrame)
	{
		var rawPath = ResolveReferencePath(adfPath, ".raw");
		if (!File.Exists(rawPath))
		{
			return [];
		}

		var raw = File.ReadAllBytes(rawPath);
		var expectedLength = RawReferenceWidth * RawReferenceHeight * RawReferenceBytesPerPixel;
		if (raw.Length != expectedLength)
		{
			return [];
		}

		var diagnostics = new List<string>
		{
			$"RAW frame timing probe around frame {primaryFrame} (+/-{RawFrameProbeRadius})"
		};
		var bestFrame = -1;
		var bestMismatches = int.MaxValue;
		foreach (var frame in frames.Keys.OrderBy(static frame => frame))
		{
			var mismatches = CountRawMismatches(raw, frames[frame]);
			var marker = frame == primaryFrame ? " primary" : string.Empty;
			var offset = frame - primaryFrame;
			diagnostics.Add($"RAW frame {frame} ({offset:+#;-#;0}){marker}: mismatches={mismatches}");
			if (mismatches < bestMismatches)
			{
				bestFrame = frame;
				bestMismatches = mismatches;
			}
		}

		if (bestFrame >= 0)
		{
			diagnostics.Add(
				$"RAW frame timing probe best frame {bestFrame} ({bestFrame - primaryFrame:+#;-#;0}): mismatches={bestMismatches}");

			var bestFramebuffer = frames[bestFrame];
			var geometry = $"RAW best frame geometry: {FindRawBounds(raw, bestFramebuffer)}; ";
			geometry += IsEnabled(Environment.GetEnvironmentVariable(SkipRawOffsetScanEnvironmentVariable))
				? "raw offset scan skipped"
				: $"{FindBestRawOffset(raw, bestFramebuffer)}; {FindBestRawSourceYOffset(raw, bestFramebuffer)}";
			diagnostics.Add(geometry);

			if (Path.GetFileName(adfPath).Equals("cycle01v.adf", StringComparison.OrdinalIgnoreCase))
			{
				var expectedOnset = FindFirstPostBlackStripeOnset(raw);
				try
				{
					var actualOnset = FindFirstPostBlackStripeOnset(bestFramebuffer);
					diagnostics.Add(
						$"RAW cycle01v stripe onset: expected row={expectedOnset.Row},x={expectedOnset.X}; " +
						$"bestFrame={bestFrame} actual row={actualOnset.Row},x={actualOnset.X}; " +
						$"delta row={actualOnset.Row - expectedOnset.Row:+#;-#;0},x={actualOnset.X - expectedOnset.X:+#;-#;0}");
				}
				catch (InvalidOperationException ex)
				{
					diagnostics.Add($"RAW cycle01v stripe onset: bestFrame={bestFrame} unavailable: {ex.Message}");
				}
				foreach (var row in new[] { 0, 38, 183, 186, 214, 215, 229, 230 })
				{
					diagnostics.Add($"RAW best frame {bestFrame}: {FormatRawRowDiagnostic(row, raw, bestFramebuffer)}");
				}
			}
		}

		return diagnostics.ToArray();
	}

	private static int CountRawMismatches(ReadOnlySpan<byte> raw, ReadOnlySpan<int> framebuffer)
	{
		var mismatches = 0;
		for (var y = 0; y < RawReferenceHeight; y++)
		{
			var frameRow = y * 2;
			for (var x = 0; x < RawReferenceWidth; x++)
			{
				var rawOffset = ((y * RawReferenceWidth) + x) * RawReferenceBytesPerPixel;
				var actual = framebuffer[(frameRow * RawReferenceWidth) + x];
				var actualRaw = ToVAmigaRawColor(actual);
				if (!RawColorsMatch(raw, rawOffset, actualRaw))
				{
					mismatches++;
				}
			}
		}

		return mismatches;
	}

	private static string[] BuildRawMismatchDiagnostics(
		string rawPath,
		ReadOnlySpan<byte> raw,
		ReadOnlySpan<int> framebuffer,
		int firstMismatchRow)
	{
		var rows = firstMismatchRow >= 0
			? new[] { 0, 6, 7, 24, 25, firstMismatchRow - 1, firstMismatchRow, firstMismatchRow + 1, 214, 215, 230, 231, 284 }
			: new[] { 0, 6, 7, 24, 25, 214, 215, 230, 231, 284 };
		var diagnostics = new List<string>
		{
			$"RAW {Path.GetFileName(rawPath)}: row diagnostics compare expected raw rows to actual framebuffer rows y*2"
		};
		foreach (var row in rows.Distinct())
		{
			if ((uint)row >= RawReferenceHeight)
			{
				continue;
			}

			diagnostics.Add(FormatRawRowDiagnostic(row, raw, framebuffer));
		}

		diagnostics.Add($"RAW {Path.GetFileName(rawPath)}: expected colors {FormatRawExpectedColors(raw, maxColors: 8)}");
		diagnostics.Add($"RAW {Path.GetFileName(rawPath)}: actual colors {FormatRawActualColors(framebuffer, maxColors: 8)}");
		if (firstMismatchRow >= 0)
		{
			diagnostics.Add(
				$"RAW {Path.GetFileName(rawPath)}: first mismatch row spans expected {FormatRawExpectedRowSpans(firstMismatchRow, raw, maxSpans: 24)}");
			diagnostics.Add(
				$"RAW {Path.GetFileName(rawPath)}: first mismatch row spans actual {FormatRawActualRowSpans(firstMismatchRow, framebuffer, maxSpans: 24)}");
			var mismatchRows = new List<int>();
			for (var row = 0; row < RawReferenceHeight; row++)
			{
				for (var x = 0; x < RawReferenceWidth; x++)
				{
					var rawOffset = ((row * RawReferenceWidth) + x) * RawReferenceBytesPerPixel;
					var actualRaw = ToVAmigaRawColor(framebuffer[((row * 2) * RawReferenceWidth) + x]);
					if (RawColorsMatch(raw, rawOffset, actualRaw))
					{
						continue;
					}

					mismatchRows.Add(row);
					break;
				}
			}
			diagnostics.Add($"RAW {Path.GetFileName(rawPath)}: mismatch rows {string.Join(",", mismatchRows)}");
			foreach (var row in mismatchRows)
			{
				diagnostics.Add(
					$"RAW mismatch row {row}: expected {FormatRawExpectedRowSpans(row, raw, maxSpans: 24)}; actual {FormatRawActualRowSpans(row, framebuffer, maxSpans: 24)}");
			}
		}

		return diagnostics.ToArray();
	}

	private static string FormatRawRowDiagnostic(int row, ReadOnlySpan<byte> raw, ReadOnlySpan<int> framebuffer)
	{
		return
			$"RAW row {row}: " +
			$"expected {FormatRawExpectedRow(row, raw, maxColors: 6)}; " +
			$"actual {FormatRawActualRow(row, framebuffer, maxColors: 6)}";
	}

	private static string FormatRawExpectedRow(int row, ReadOnlySpan<byte> raw, int maxColors)
	{
		var counts = new Dictionary<int, int>();
		var minX = RawReferenceWidth;
		var maxX = -1;
		var nonBlack = 0;
		for (var x = 0; x < RawReferenceWidth; x++)
		{
			var offset = ((row * RawReferenceWidth) + x) * RawReferenceBytesPerPixel;
			var color = (raw[offset] << 16) | (raw[offset + 1] << 8) | raw[offset + 2];
			counts[color] = counts.GetValueOrDefault(color) + 1;
			if (color == 0)
			{
				continue;
			}

			nonBlack++;
			minX = Math.Min(minX, x);
			maxX = Math.Max(maxX, x);
		}

		return FormatRawRowSummary(nonBlack, minX, maxX, counts, maxColors);
	}

	private static string FormatRawActualRow(int row, ReadOnlySpan<int> framebuffer, int maxColors)
	{
		var counts = new Dictionary<int, int>();
		var minX = RawReferenceWidth;
		var maxX = -1;
		var nonBlack = 0;
		var frameRow = row * 2;
		for (var x = 0; x < RawReferenceWidth; x++)
		{
			var actual = framebuffer[(frameRow * RawReferenceWidth) + x];
			var color = ToVAmigaRawColor(actual);
			counts[color] = counts.GetValueOrDefault(color) + 1;
			if (color == 0)
			{
				continue;
			}

			nonBlack++;
			minX = Math.Min(minX, x);
			maxX = Math.Max(maxX, x);
		}

		return FormatRawRowSummary(nonBlack, minX, maxX, counts, maxColors);
	}

	private static string FormatRawExpectedRowSpans(int row, ReadOnlySpan<byte> raw, int maxSpans)
	{
		var spans = new List<string>();
		var start = 0;
		var previous = -1;
		for (var x = 0; x < RawReferenceWidth; x++)
		{
			var offset = ((row * RawReferenceWidth) + x) * RawReferenceBytesPerPixel;
			var color = (raw[offset] << 16) | (raw[offset + 1] << 8) | raw[offset + 2];
			if (previous < 0)
			{
				previous = color;
				start = x;
				continue;
			}

			if (color == previous)
			{
				continue;
			}

			AddRawRowSpan(spans, start, x - 1, previous, maxSpans);
			start = x;
			previous = color;
		}

		AddRawRowSpan(spans, start, RawReferenceWidth - 1, previous, maxSpans);
		return string.Join(",", spans);
	}

	private static string FormatRawActualRowSpans(int row, ReadOnlySpan<int> framebuffer, int maxSpans)
	{
		var spans = new List<string>();
		var frameRow = row * 2;
		var start = 0;
		var previous = -1;
		for (var x = 0; x < RawReferenceWidth; x++)
		{
			var color = ToVAmigaRawColor(framebuffer[(frameRow * RawReferenceWidth) + x]);
			if (previous < 0)
			{
				previous = color;
				start = x;
				continue;
			}

			if (color == previous)
			{
				continue;
			}

			AddRawRowSpan(spans, start, x - 1, previous, maxSpans);
			start = x;
			previous = color;
		}

		AddRawRowSpan(spans, start, RawReferenceWidth - 1, previous, maxSpans);
		return string.Join(",", spans);
	}

	private static void AddRawRowSpan(List<string> spans, int start, int stop, int color, int maxSpans)
	{
		if (spans.Count >= maxSpans)
		{
			if (spans.Count == maxSpans)
			{
				spans.Add("...");
			}

			return;
		}

		spans.Add($"{start}-{stop}:#{color:X6}");
	}

	private static string FormatRawRowSummary(int nonBlack, int minX, int maxX, Dictionary<int, int> counts, int maxColors)
	{
		var span = maxX < minX ? "empty" : $"{minX}-{maxX}";
		return $"nb={nonBlack}, span={span}, colors={FormatRawColorCounts(counts, maxColors)}";
	}

	private static string FormatRawExpectedColors(ReadOnlySpan<byte> raw, int maxColors)
	{
		var counts = new Dictionary<int, int>();
		for (var i = 0; i < raw.Length; i += RawReferenceBytesPerPixel)
		{
			var color = (raw[i] << 16) | (raw[i + 1] << 8) | raw[i + 2];
			counts[color] = counts.GetValueOrDefault(color) + 1;
		}

		return FormatRawColorCounts(counts, maxColors);
	}

	private static string FormatRawActualColors(ReadOnlySpan<int> framebuffer, int maxColors)
	{
		var counts = new Dictionary<int, int>();
		for (var y = 0; y < RawReferenceHeight; y++)
		{
			var frameRow = y * 2;
			for (var x = 0; x < RawReferenceWidth; x++)
			{
				var actual = framebuffer[(frameRow * RawReferenceWidth) + x];
				var color = ToVAmigaRawColor(actual);
				counts[color] = counts.GetValueOrDefault(color) + 1;
			}
		}

		return FormatRawColorCounts(counts, maxColors);
	}

	private static string FormatRawColorCounts(Dictionary<int, int> counts, int maxColors)
		=> string.Join(
			",",
			counts
				.OrderByDescending(pair => pair.Value)
				.ThenBy(pair => pair.Key)
				.Take(maxColors)
				.Select(pair => $"#{pair.Key:X6}:{pair.Value}"));

	private static string FindRawBounds(ReadOnlySpan<byte> raw, ReadOnlySpan<int> framebuffer)
	{
		var expected = FindRawExpectedBounds(raw);
		var actual = FindRawActualBounds(framebuffer);
		return $"bounds expected={expected}, actual={actual}";
	}

	private static string FindRawExpectedBounds(ReadOnlySpan<byte> raw)
	{
		var minX = RawReferenceWidth;
		var minY = RawReferenceHeight;
		var maxX = -1;
		var maxY = -1;
		for (var y = 0; y < RawReferenceHeight; y++)
		{
			for (var x = 0; x < RawReferenceWidth; x++)
			{
				var offset = ((y * RawReferenceWidth) + x) * RawReferenceBytesPerPixel;
				if (raw[offset] == 0 && raw[offset + 1] == 0 && raw[offset + 2] == 0)
				{
					continue;
				}

				minX = Math.Min(minX, x);
				minY = Math.Min(minY, y);
				maxX = Math.Max(maxX, x);
				maxY = Math.Max(maxY, y);
			}
		}

		return FormatBounds(minX, minY, maxX, maxY);
	}

	private static string FindRawActualBounds(ReadOnlySpan<int> framebuffer)
	{
		var minX = RawReferenceWidth;
		var minY = RawReferenceHeight;
		var maxX = -1;
		var maxY = -1;
		for (var y = 0; y < RawReferenceHeight; y++)
		{
			var frameRow = y * 2;
			for (var x = 0; x < RawReferenceWidth; x++)
			{
				var actual = framebuffer[(frameRow * RawReferenceWidth) + x];
				if (ToVAmigaRawColor(actual) == 0)
				{
					continue;
				}

				minX = Math.Min(minX, x);
				minY = Math.Min(minY, y);
				maxX = Math.Max(maxX, x);
				maxY = Math.Max(maxY, y);
			}
		}

		return FormatBounds(minX, minY, maxX, maxY);
	}

	private static string FormatBounds(int minX, int minY, int maxX, int maxY)
		=> maxX < minX || maxY < minY
			? "empty"
			: $"({minX},{minY})-({maxX},{maxY})";

	private static string FindBestRawOffset(ReadOnlySpan<byte> raw, ReadOnlySpan<int> framebuffer)
	{
		var bestDx = 0;
		var bestDy = 0;
		var bestMismatches = int.MaxValue;
		var bestCompared = 0;
		for (var dy = -RawOffsetScanMaxDy; dy <= RawOffsetScanMaxDy; dy++)
		{
			var yStart = Math.Max(0, -dy);
			var yStop = Math.Min(RawReferenceHeight, RawReferenceHeight - dy);
			if (yStop <= yStart)
			{
				continue;
			}

			for (var dx = -RawOffsetScanMaxDx; dx <= RawOffsetScanMaxDx; dx++)
			{
				var xStart = Math.Max(0, -dx);
				var xStop = Math.Min(RawReferenceWidth, RawReferenceWidth - dx);
				if (xStop <= xStart)
				{
					continue;
				}

				var mismatches = 0;
				var compared = 0;
				for (var y = yStart; y < yStop; y++)
				{
					var rawRow = y * RawReferenceWidth;
					var frameRow = (y + dy) * 2;
					for (var x = xStart; x < xStop; x++)
					{
						var rawOffset = ((rawRow + x) * RawReferenceBytesPerPixel);
						var actual = framebuffer[(frameRow * RawReferenceWidth) + x + dx];
						var actualRaw = ToVAmigaRawColor(actual);
						if (!RawColorsMatch(raw, rawOffset, actualRaw))
						{
							mismatches++;
						}

						compared++;
					}
				}

				if (mismatches < bestMismatches)
				{
					bestDx = dx;
					bestDy = dy;
					bestMismatches = mismatches;
					bestCompared = compared;
				}
			}
		}

		return $"bestOffset dx={bestDx}, dy={bestDy}, mismatches={bestMismatches}/{bestCompared}";
	}

	private static string FindBestRawSourceYOffset(ReadOnlySpan<byte> raw, ReadOnlySpan<int> framebuffer)
	{
		var bestOffset = 0;
		var bestMismatches = int.MaxValue;
		var bestCompared = 0;
		for (var sourceYOffset = -80; sourceYOffset <= 80; sourceYOffset++)
		{
			var yStart = Math.Max(0, -sourceYOffset);
			var yStop = Math.Min(RawReferenceHeight, RawReferenceHeight - sourceYOffset);
			if (yStop <= yStart)
			{
				continue;
			}

			var mismatches = 0;
			var compared = 0;
			for (var y = yStart; y < yStop; y++)
			{
				var rawRow = y * RawReferenceWidth;
				var frameRow = (y + sourceYOffset) * 2;
				for (var x = 0; x < RawReferenceWidth; x++)
				{
					var rawOffset = ((rawRow + x) * RawReferenceBytesPerPixel);
					var actual = framebuffer[(frameRow * RawReferenceWidth) + x];
					var actualRaw = ToVAmigaRawColor(actual);
					if (!RawColorsMatch(raw, rawOffset, actualRaw))
					{
						mismatches++;
					}

					compared++;
				}
			}

			if (mismatches < bestMismatches)
			{
				bestMismatches = mismatches;
				bestCompared = compared;
				bestOffset = sourceYOffset;
			}
		}

		return $"bestSourceYOffset dy={bestOffset}, mismatches={bestMismatches}/{bestCompared}";
	}

	private static void DumpRawFrame(string adfPath, ReadOnlySpan<int> framebuffer)
	{
		var outputPath = Path.Combine(
			Path.GetDirectoryName(adfPath) ?? ".",
			Path.GetFileNameWithoutExtension(adfPath) + "_coppermod.raw");
		var raw = new byte[RawReferenceWidth * RawReferenceHeight * RawReferenceBytesPerPixel];
		for (var y = 0; y < RawReferenceHeight; y++)
		{
			var frameRow = y * 2;
			for (var x = 0; x < RawReferenceWidth; x++)
			{
				var actual = framebuffer[(frameRow * RawReferenceWidth) + x];
				var actualRaw = ToVAmigaRawColor(actual);
				var rawOffset = ((y * RawReferenceWidth) + x) * RawReferenceBytesPerPixel;
				raw[rawOffset] = (byte)((actualRaw >> 16) & 0xFF);
				raw[rawOffset + 1] = (byte)((actualRaw >> 8) & 0xFF);
				raw[rawOffset + 2] = (byte)(actualRaw & 0xFF);
			}
		}

		File.WriteAllBytes(outputPath, raw);
	}

	private static int ToVAmigaRawColor(int argb)
	{
		var r = ((argb >> 16) & 0xFF) & 0xF0;
		var g = ((argb >> 8) & 0xFF) & 0xF0;
		var b = (argb & 0xFF) & 0xF0;
		if (b != 0 && !(r == g && g == b) && (r != 0 || g == 0))
		{
			b--;
		}

		return (r << 16) | (g << 8) | b;
	}

	private static bool RawColorsMatch(ReadOnlySpan<byte> raw, int offset, int actual)
		=> RawColorsMatch(
			(raw[offset] << 16) | (raw[offset + 1] << 8) | raw[offset + 2],
			actual);

	private static bool RawColorsMatch(int expected, int actual)
		=> Math.Abs(((expected >> 16) & 0xFF) - ((actual >> 16) & 0xFF)) <= 1 &&
			Math.Abs(((expected >> 8) & 0xFF) - ((actual >> 8) & 0xFF)) <= 1 &&
			Math.Abs((expected & 0xFF) - (actual & 0xFF)) <= 1;

	private static (int Row, int X, int Color) FindFirstPostBlackStripeOnset(ReadOnlySpan<byte> raw)
	{
		const int stripeColor = 0xF000EF;
		var expectedLength = RawReferenceWidth * RawReferenceHeight * RawReferenceBytesPerPixel;
		if (raw.Length != expectedLength)
		{
			throw new ArgumentException($"Unexpected raw reference length {raw.Length}.", nameof(raw));
		}

		var maxStripePixels = 0;
		var maxStripeRow = -1;
		for (var row = 160; row < RawReferenceHeight; row++)
		{
			var stripePixels = 0;
			for (var x = 0; x < RawReferenceWidth; x++)
			{
				if (ReadRawColor(raw, row, x) == stripeColor)
				{
					stripePixels++;
				}
			}

			if (stripePixels > maxStripePixels)
			{
				maxStripePixels = stripePixels;
				maxStripeRow = row;
			}

			if (stripePixels >= 128)
			{
				var firstX = FindRawColorSpans(raw, row, stripeColor)[0].Start;
				return (row, firstX, stripeColor);
			}
		}

		throw new InvalidOperationException(
			$"No post-black cycle stripe onset was found in the raw reference; " +
			$"max #{stripeColor:X6} count={maxStripePixels} at row={maxStripeRow}.");
	}

	private static (int Row, int X, int Color) FindFirstPostBlackStripeOnset(ReadOnlySpan<int> framebuffer)
	{
		const int stripeColor = 0xF000EF;
		var maxStripePixels = 0;
		var maxStripeRow = -1;
		for (var row = 160; row < RawReferenceHeight; row++)
		{
			var stripePixels = 0;
			var firstX = -1;
			var frameRow = row * 2;
			for (var x = 0; x < RawReferenceWidth; x++)
			{
				if (ToVAmigaRawColor(framebuffer[(frameRow * RawReferenceWidth) + x]) != stripeColor)
				{
					continue;
				}

				stripePixels++;
				if (firstX < 0)
				{
					firstX = x;
				}
			}

			if (stripePixels > maxStripePixels)
			{
				maxStripePixels = stripePixels;
				maxStripeRow = row;
			}

			if (stripePixels >= 128)
			{
				return (row, firstX, stripeColor);
			}
		}

		throw new InvalidOperationException(
			$"No post-black cycle stripe onset was found in the framebuffer; " +
			$"max #{stripeColor:X6} count={maxStripePixels} at row={maxStripeRow}.");
	}

	private static int ReadRawColor(ReadOnlySpan<byte> raw, int row, int x)
	{
		var offset = ((row * RawReferenceWidth) + x) * RawReferenceBytesPerPixel;
		return (raw[offset] << 16) | (raw[offset + 1] << 8) | raw[offset + 2];
	}

	private static List<(int Start, int Stop)> FindRawColorSpans(ReadOnlySpan<byte> raw, int row, int color)
	{
		var spans = new List<(int Start, int Stop)>();
		var start = -1;
		for (var x = 0; x < RawReferenceWidth; x++)
		{
			if (ReadRawColor(raw, row, x) == color)
			{
				if (start < 0)
				{
					start = x;
				}

				continue;
			}

			if (start >= 0)
			{
				spans.Add((start, x - 1));
				start = -1;
			}
		}

		if (start >= 0)
		{
			spans.Add((start, RawReferenceWidth - 1));
		}

		return spans;
	}

	private static string FormatCycleRawStripeReference(string relativePath)
	{
		if (!NormalizeCasePath(relativePath).Equals(
			"Agnus/Registers/VPOS/cycle01v/cycle01v.adf",
			StringComparison.OrdinalIgnoreCase))
		{
			return "not cycle01v";
		}

		var root = ResolveVAmigaTsRoot();
		if (root == null)
		{
			return "corpus unavailable";
		}

		var adfPath = Path.Combine(root, NormalizeRelativePath(relativePath));
		var rawPath = ResolveReferencePath(adfPath, ".raw");
		if (!File.Exists(rawPath))
		{
			return "raw unavailable";
		}

		var onset = FindFirstPostBlackStripeOnset(File.ReadAllBytes(rawPath));
		// The cycle01v raw capture geometry maps row 186/x168 to PAL beam v212/h98.
		return $"row={onset.Row}, x={onset.X}, color=#{onset.Color:X6}, mappedBeam=v212h098";
	}

	private static string FormatCycleAdfIterationBoundarySummary(
		Machine machine,
		IReadOnlyList<CustomRegisterRead> reads)
	{
		var phases = machine.Bus.CpuBusPhases
			.OrderBy(phase => phase.CpuPhase.RequestedCycle)
			.ToArray();
		var colorWrites = machine.Bus.CustomRegisterWrites
			.Where(write => write.Address == 0x180)
			.OrderBy(write => write.Cycle)
			.ToArray();
		var stripePairs = colorWrites
			.Select((write, index) => (Write: write, Index: index))
			.Where(item => item.Write.Value == 0x0F0F &&
				item.Index + 1 < colorWrites.Length &&
				colorWrites[item.Index + 1].Value == 0x0000 &&
				colorWrites[item.Index + 1].Cycle - item.Write.Cycle <= 20)
			.ToArray();
		var stripe = stripePairs.LastOrDefault().Write;
		if (stripe.Cycle == 0)
		{
			return "stripe unavailable";
		}

		var finalDbra = phases.LastOrDefault(phase =>
			IsCycleDelayDbraInstruction(machine.Bus.ChipRam, phase.CpuPhase.InstructionProgramCounter) &&
			phase.CpuPhase.RequestedCycle < stripe.Cycle);
		stripe = stripePairs.First(item => item.Write.Cycle > finalDbra.CpuPhase.CompletedCycle).Write;
		var probe = phases.LastOrDefault(phase =>
			IsCycleProbeInstruction(machine.Bus.ChipRam, phase.CpuPhase.InstructionProgramCounter) &&
			(phase.CpuPhase.Address & 0x00FF_FFFE) is 0x00DFF004 or 0x00DFF006 &&
			phase.CpuPhase.RequestedCycle < finalDbra.CpuPhase.RequestedCycle);
		var probeRead = FindCustomRegisterRead(reads, probe);
		var dispatch = machine.InterruptDispatchTrace
			.Where(item =>
				item.Level == 3 &&
				item.AcceptanceCycle >= probe.CpuPhase.RequestedCycle &&
				item.AcceptanceCycle < stripe.Cycle)
			.LastOrDefault();
		var handlerPc = machine.Bus.ReadLong((24u + 3u) * 4u) & 0x00FF_FFFF;
		var rtePc = FindRteInstruction(machine.Bus.ChipRam, handlerPc);
		var rte = phases.LastOrDefault(phase =>
			(phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF) == rtePc &&
			phase.CpuPhase.RequestedCycle > probe.CpuPhase.RequestedCycle &&
			phase.CpuPhase.RequestedCycle < stripe.Cycle);
		var resumed = rte.CpuPhase.RequestedCycle == 0
			? default
			: phases.FirstOrDefault(phase => phase.CpuPhase.RequestedCycle > rte.CpuPhase.RequestedCycle);
		var stripePhase = FindCpuPhaseForCustomWrite(phases, stripe);
		var origin = probe.CpuPhase.RequestedCycle;
		var handlerWaits = phases
			.Where(phase =>
				phase.CpuPhase.RequestedCycle >= dispatch.EntryCompletedCycle &&
				phase.CpuPhase.RequestedCycle < resumed.CpuPhase.RequestedCycle &&
				phase.BusAccess?.WaitCycles > 0)
			.Select(phase =>
				$"pc{phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF:X6}/" +
				$"{phase.CpuPhase.AccessKind}/a{phase.CpuPhase.Address & 0x00FF_FFFF:X6}/" +
				$"{FormatBeam(phase.CpuPhase.RequestedCycle)}+{phase.BusAccess!.Value.WaitCycles}")
			.ToArray();
		var handlerPhases = phases.Where(phase =>
			phase.CpuPhase.RequestedCycle >= dispatch.EntryCompletedCycle &&
			phase.CpuPhase.RequestedCycle < resumed.CpuPhase.RequestedCycle);
		var preIrqPhases = phases.Where(phase =>
			phase.CpuPhase.RequestedCycle >= probe.CpuPhase.RequestedCycle &&
			phase.CpuPhase.RequestedCycle <= dispatch.AcceptanceCycle).TakeLast(16);
		var preIrqBus = machine.Bus.BusAccesses.Where(access =>
			access.GrantedCycle >= dispatch.CpuVisibleCycle &&
			access.GrantedCycle <= dispatch.AcceptanceCycle + 4);
		return
			$"probe={FormatCycleBoundary(probe, probeRead?.SampleCycle)}/a=0x{probe.CpuPhase.Address & 0x00FF_FFFF:X6}/v={(probeRead.HasValue ? $"0x{probeRead.Value.Value:X4}" : "missing")}; " +
			$"irq=visible+{dispatch.CpuVisibleCycle - origin}/sample+{dispatch.CpuSampleCycle - origin}/accept+{dispatch.AcceptanceCycle - origin}/entry+{dispatch.EntryCompletedCycle - origin}/" +
			$"{FormatBeam(dispatch.CpuVisibleCycle)}->{FormatBeam(dispatch.CpuSampleCycle)}->{FormatBeam(dispatch.AcceptanceCycle)}->{FormatBeam(dispatch.EntryCompletedCycle)}; " +
			$"rte={FormatCycleBoundary(rte, null)}; resumed={FormatCycleBoundary(resumed, null)}/+{resumed.CpuPhase.RequestedCycle - origin}; waits=[{string.Join(',', handlerWaits)}]; preirq=[{FormatCpuBusPhases(preIrqPhases)}]; irqbus=[{FormatBusAccesses(preIrqBus)}]; handler=[{FormatCpuBusPhases(handlerPhases)}]; " +
			$"finalDbra={FormatCycleBoundary(finalDbra, null)}/+{finalDbra.CpuPhase.RequestedCycle - origin}; " +
			$"stripe={FormatCycleBoundary(stripePhase.GetValueOrDefault(), stripe.Cycle)}/+{stripe.Cycle - origin}";

	}

	private static string FormatCycleAdfIterationBoundaries(
		Machine machine,
		IReadOnlyList<CustomRegisterRead> reads)
	{
		var phases = machine.Bus.CpuBusPhases
			.OrderBy(phase => phase.CpuPhase.RequestedCycle)
			.ToArray();
		var colorWrites = machine.Bus.CustomRegisterWrites
			.Where(write => write.Address == 0x180)
			.OrderBy(write => write.Cycle)
			.ToArray();
		var stripePairs = colorWrites
			.Select((write, index) => (Write: write, Index: index))
			.Where(item => item.Write.Value == 0x0F0F &&
				item.Index + 1 < colorWrites.Length &&
				colorWrites[item.Index + 1].Value == 0x0000 &&
				colorWrites[item.Index + 1].Cycle - item.Write.Cycle <= 20)
			.ToArray();
		if (stripePairs.Length == 0)
		{
			return "stripe pair unavailable";
		}

		var finalDbra = phases.LastOrDefault(phase =>
			IsCycleDelayDbraInstruction(machine.Bus.ChipRam, phase.CpuPhase.InstructionProgramCounter) &&
			phase.CpuPhase.RequestedCycle < stripePairs[^1].Write.Cycle);
		if (finalDbra.CpuPhase.RequestedCycle == 0)
		{
			return "delay DBRA unavailable";
		}

		var stripe = stripePairs
			.FirstOrDefault(item => item.Write.Cycle > finalDbra.CpuPhase.CompletedCycle)
			.Write;
		if (stripe.Cycle == 0)
		{
			return "post-delay stripe pair unavailable";
		}

		var probe = phases
			.Where(phase =>
				IsCycleProbeInstruction(machine.Bus.ChipRam, phase.CpuPhase.InstructionProgramCounter) &&
				(phase.CpuPhase.Address & 0x00FF_FFFE) is 0x00DFF004 or 0x00DFF006 &&
				phase.CpuPhase.RequestedCycle < finalDbra.CpuPhase.RequestedCycle)
			.LastOrDefault();
		var probeRead = FindCustomRegisterRead(reads, probe);
		var handlerPc = machine.Bus.ReadLong((24u + 3u) * 4u) & 0x00FF_FFFF;
		var irqEntry = phases.FirstOrDefault(phase =>
			(phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF) == handlerPc &&
			phase.CpuPhase.RequestedCycle > probe.CpuPhase.RequestedCycle &&
			phase.CpuPhase.RequestedCycle < stripe.Cycle);
		var irqAcknowledge = phases.LastOrDefault(phase =>
			phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuInterruptAcknowledge &&
			phase.CpuPhase.RequestedCycle < irqEntry.CpuPhase.RequestedCycle);
		var stripePhase = FindCpuPhaseForCustomWrite(phases, stripe);
		var rtePc = FindRteInstruction(machine.Bus.ChipRam, handlerPc);
		var rte = phases.LastOrDefault(phase =>
			(phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF) == rtePc &&
			phase.CpuPhase.RequestedCycle > probe.CpuPhase.RequestedCycle &&
			phase.CpuPhase.RequestedCycle < stripe.Cycle);
		var rteNext = rte.CpuPhase.RequestedCycle == 0
			? default
			: phases.FirstOrDefault(phase => phase.CpuPhase.RequestedCycle > rte.CpuPhase.RequestedCycle);
		var probeValue = probeRead.HasValue ? $"0x{probeRead.Value.Value:X4}" : "missing";
		var probeOpcode = probe.CpuPhase.RequestedCycle == 0
			? "missing"
			: $"0x{ReadChipRamWord(machine.Bus.ChipRam, probe.CpuPhase.InstructionProgramCounter):X4}";
		var finalDbraOpcode = $"0x{ReadChipRamWord(machine.Bus.ChipRam, finalDbra.CpuPhase.InstructionProgramCounter):X4}";
		var stripeOpcode = stripePhase.HasValue
			? $"0x{ReadChipRamWord(machine.Bus.ChipRam, stripePhase.Value.CpuPhase.InstructionProgramCounter):X4}"
			: "missing";
		var delayStart = rteNext.CpuPhase.RequestedCycle;
		var delayStop = finalDbra.CpuPhase.CompletedCycle;
		var allDbraPhases = phases
			.Where(phase =>
				phase.CpuPhase.InstructionProgramCounter == finalDbra.CpuPhase.InstructionProgramCounter &&
				phase.CpuPhase.RequestedCycle > probe.CpuPhase.CompletedCycle &&
				phase.CpuPhase.RequestedCycle <= delayStop)
			.ToArray();
		var preIrqDbraPhases = allDbraPhases
			.Where(phase => phase.CpuPhase.RequestedCycle < irqAcknowledge.CpuPhase.RequestedCycle)
			.ToArray();
		var dbraPhases = phases
			.Where(phase =>
				phase.CpuPhase.InstructionProgramCounter == finalDbra.CpuPhase.InstructionProgramCounter &&
				phase.CpuPhase.RequestedCycle >= delayStart &&
				phase.CpuPhase.RequestedCycle <= delayStop)
			.ToArray();
		var displacementFetches = dbraPhases.Count(phase =>
			phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuInstructionFetch &&
			phase.CpuPhase.Address == finalDbra.CpuPhase.InstructionProgramCounter + 2);
		var waitHistogram = string.Join(",", dbraPhases
			.GroupBy(phase => (phase.BusAccess?.GrantedCycle ?? phase.CpuPhase.RequestedCycle) - phase.CpuPhase.RequestedCycle)
			.OrderBy(group => group.Key)
			.Select(group => $"{group.Key}:{group.Count()}"));
		var waitTotal = dbraPhases.Sum(phase =>
			(phase.BusAccess?.GrantedCycle ?? phase.CpuPhase.RequestedCycle) - phase.CpuPhase.RequestedCycle);
		var waitBlockers = new Dictionary<(string Stage, AgnusChipSlotOwner Owner), int>();
		foreach (var phase in dbraPhases)
		{
			var stage = phase.CpuPhase.Address == finalDbra.CpuPhase.InstructionProgramCounter
				? "opcode"
				: phase.CpuPhase.Address == finalDbra.CpuPhase.InstructionProgramCounter + 2
					? "disp"
					: "other";
			var schedulerRequest = phase.BusAccess?.Request.RequestedCycle ?? phase.CpuPhase.RequestedCycle;
			var grant = phase.BusAccess?.GrantedCycle ?? schedulerRequest;
			for (var cycle = AgnusChipSlotScheduler.AlignToSlot(schedulerRequest);
				cycle < grant;
				cycle += AgnusChipSlotScheduler.SlotCycles)
			{
				machine.Bus.TryGetCommittedAgnusSlotOwner(cycle, out var owner);
				var key = (stage, owner);
				waitBlockers[key] = waitBlockers.GetValueOrDefault(key) + 1;
			}
		}
		var waitBlockerSummary = string.Join(",", waitBlockers
			.OrderBy(pair => pair.Key.Stage)
			.ThenBy(pair => pair.Key.Owner)
			.Select(pair => $"{pair.Key.Stage}/{pair.Key.Owner}:{pair.Value}"));
		var allDbraSummary = FormatDbraPhaseSummary(
			allDbraPhases,
			finalDbra.CpuPhase.InstructionProgramCounter,
			machine.Bus);
		var preIrqDbraSummary = FormatDbraPhaseSummary(
			preIrqDbraPhases,
			finalDbra.CpuPhase.InstructionProgramCounter,
			machine.Bus);
		var postRteDbraSummary = FormatDbraPhaseSummary(
			dbraPhases,
			finalDbra.CpuPhase.InstructionProgramCounter,
			machine.Bus);
		var dbraEdgePhases = allDbraPhases
			.Take(8)
			.Concat(allDbraPhases.TakeLast(8))
			.ToArray();
		var dbraEdgeState = string.Join("; ", dbraEdgePhases.Select(phase =>
			$"i={FormatBeam(phase.CpuPhase.InstructionStartCycle)}/" +
			$"entry={FormatBeam(phase.CpuPhase.InstructionEntryBusCycle)}/" +
			$"q{phase.CpuPhase.InstructionEntryPrefetchCount}/" +
			$"r{FormatBeam(phase.CpuPhase.InstructionEntryReadyCycle0)},{FormatBeam(phase.CpuPhase.InstructionEntryReadyCycle1)}/" +
			$"a={phase.CpuPhase.Address & 0x00FF_FFFF:X6}/" +
			$"req={FormatBeam(phase.CpuPhase.RequestedCycle)}"));
		var handoffPhases = phases
			.Where(phase =>
				phase.CpuPhase.InstructionStartCycle >= finalDbra.CpuPhase.InstructionStartCycle &&
				phase.CpuPhase.RequestedCycle <= stripe.Cycle)
			.ToArray();
		var handoffState = string.Join("; ", handoffPhases.Select(phase =>
			$"pc={phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF:X6}/" +
			$"i={FormatBeam(phase.CpuPhase.InstructionStartCycle)}/" +
			$"a={phase.CpuPhase.Address & 0x00FF_FFFF:X6}/" +
			$"req={FormatBeam(phase.CpuPhase.RequestedCycle)}/" +
			$"done={FormatBeam(phase.CpuPhase.CompletedCycle)}"));
		var postRteEdgeState = string.Join("; ", dbraPhases.Take(8).Select(phase =>
			$"i={FormatBeam(phase.CpuPhase.InstructionStartCycle)}/" +
			$"entry={FormatBeam(phase.CpuPhase.InstructionEntryBusCycle)}/" +
			$"q{phase.CpuPhase.InstructionEntryPrefetchCount}/" +
			$"r{FormatBeam(phase.CpuPhase.InstructionEntryReadyCycle0)},{FormatBeam(phase.CpuPhase.InstructionEntryReadyCycle1)}/" +
			$"a={phase.CpuPhase.Address & 0x00FF_FFFF:X6}/" +
			$"req={FormatBeam(phase.CpuPhase.RequestedCycle)}/" +
			$"done={FormatBeam(phase.CpuPhase.CompletedCycle)}"));
		var preIrqEdgeState = string.Join("; ", preIrqDbraPhases.TakeLast(8).Select(phase =>
			$"i={FormatBeam(phase.CpuPhase.InstructionStartCycle)}/" +
			$"entry={FormatBeam(phase.CpuPhase.InstructionEntryBusCycle)}/" +
			$"q{phase.CpuPhase.InstructionEntryPrefetchCount}/" +
			$"r{FormatBeam(phase.CpuPhase.InstructionEntryReadyCycle0)},{FormatBeam(phase.CpuPhase.InstructionEntryReadyCycle1)}/" +
			$"a={phase.CpuPhase.Address & 0x00FF_FFFF:X6}/" +
			$"req={FormatBeam(phase.CpuPhase.RequestedCycle)}/" +
			$"done={FormatBeam(phase.CpuPhase.CompletedCycle)}"));
		var postRteInstructionStarts = dbraPhases
			.Select(phase => phase.CpuPhase.InstructionStartCycle)
			.Distinct()
			.OrderBy(cycle => cycle)
			.ToArray();
		var frameStartCycle = delayStart - Math.Max(0, delayStart % AmigaConstants.A500PalCpuCyclesPerFrame);
		var postRteCheckpoints = string.Join("; ", new[] { 2, 3, 4, 5, 10, 20, 30, 50, 100, 150, 200 }.Select(line =>
		{
			var targetCycle = frameStartCycle + line * AmigaConstants.A500PalCpuCyclesPerRasterLine;
			var index = Array.FindIndex(postRteInstructionStarts, cycle => cycle >= targetCycle);
			if (index < 0)
			{
				return $"v{line}=missing";
			}
			var d3 = unchecked((ushort)(0x24DB - index));
			return $"v{line}=i{index}/d3{d3:X4}/{FormatBeam(postRteInstructionStarts[index])}";
		}));
		var postRteLineEdges = string.Join("; ", new[] { 3, 4, 100, 150, 200 }.Select(line =>
		{
			var lineStart = frameStartCycle + line * AmigaConstants.A500PalCpuCyclesPerRasterLine;
			var edgeStart = lineStart - 10 * AmigaConstants.A500PalCpuCyclesPerColorClock;
			var edgeStop = lineStart + 15 * AmigaConstants.A500PalCpuCyclesPerColorClock;
			var entries = postRteInstructionStarts
				.Select((cycle, index) => (Cycle: cycle, Index: index))
				.Where(entry => entry.Cycle >= edgeStart && entry.Cycle <= edgeStop)
				.Select(entry =>
				{
					var d3 = unchecked((ushort)(0x24DB - entry.Index));
					return $"d3{d3:X4}@{FormatBeam(entry.Cycle)}";
				});
			return $"v{line}=[{string.Join(',', entries)}]";
		}));
		var postRteLineWaits = string.Join("; ", new[] { 3, 4, 100, 150, 200 }.Select(line =>
		{
			var lineStart = frameStartCycle + line * AmigaConstants.A500PalCpuCyclesPerRasterLine;
			var lineStop = lineStart + AmigaConstants.A500PalCpuCyclesPerRasterLine;
			var waits = postRteInstructionStarts
				.Select((cycle, index) => (Cycle: cycle, Index: index))
				.Where(entry =>
					entry.Index > 0 &&
					entry.Cycle >= lineStart &&
					entry.Cycle < lineStop &&
					entry.Cycle - postRteInstructionStarts[entry.Index - 1] > 10)
				.Select(entry =>
				{
					var d3 = unchecked((ushort)(0x24DB - entry.Index));
					var delta = entry.Cycle - postRteInstructionStarts[entry.Index - 1];
					return $"d3{d3:X4}@{FormatBeam(entry.Cycle)}/d{delta}";
				});
			return $"v{line}=[{string.Join(',', waits)}]";
		}));
		var dbraLineSlots = string.Join("; ", new[] { 3, 4, 100, 150, 200, 210, 211, 212 }.Select(line =>
		{
			var lineStart = frameStartCycle + line * AmigaConstants.A500PalCpuCyclesPerRasterLine;
			var lineStop = lineStart + 50 * AmigaConstants.A500PalCpuCyclesPerColorClock;
			var slots = dbraPhases
				.Where(phase =>
					phase.CpuPhase.RequestedCycle >= lineStart &&
					phase.CpuPhase.RequestedCycle < lineStop)
				.Select(phase =>
				{
					var grant = phase.BusAccess?.GrantedCycle ?? phase.CpuPhase.RequestedCycle;
					var beam = machine.Bus.GetBeamPosition(grant);
					return $"h{beam.BeamHorizontal:X}:{phase.CpuPhase.Address - finalDbra.CpuPhase.InstructionProgramCounter:X}";
				});
			return $"v{line}=[{string.Join(',', slots)}]";
		}));
		var futureDbraEntries = dbraPhases
			.GroupBy(phase => phase.CpuPhase.InstructionStartCycle)
			.Select(group => group.First().CpuPhase)
			.Where(phase => phase.InstructionEntryReadyCycle1 > phase.InstructionStartCycle)
			.ToArray();
		var futureDbra = $"count={futureDbraEntries.Length}/" + string.Join(',', futureDbraEntries.Take(24).Select(phase =>
			$"{FormatBeam(phase.InstructionStartCycle)}:+{phase.InstructionEntryReadyCycle1 - phase.InstructionStartCycle}"));
		var delayEdge = allDbraPhases.Length == 0
			? "missing"
			: $"first={FormatBeam(allDbraPhases[0].CpuPhase.InstructionStartCycle)}/" +
			  $"req={FormatBeam(allDbraPhases[0].CpuPhase.RequestedCycle)}/" +
			  $"last={FormatBeam(allDbraPhases[^1].CpuPhase.InstructionStartCycle)}/" +
			  $"req={FormatBeam(allDbraPhases[^1].CpuPhase.RequestedCycle)}";
		var earlyPostStart = frameStartCycle +
			2 * AmigaConstants.A500PalCpuCyclesPerRasterLine +
			180 * AmigaConstants.A500PalCpuCyclesPerColorClock;
		var earlyPostStop = frameStartCycle +
			3 * AmigaConstants.A500PalCpuCyclesPerRasterLine +
			20 * AmigaConstants.A500PalCpuCyclesPerColorClock;
		var earlyPostPhases = dbraPhases
			.Where(phase =>
				phase.CpuPhase.RequestedCycle >= earlyPostStart &&
				phase.CpuPhase.RequestedCycle <= earlyPostStop)
			.ToArray();
		var stripeLoopDbraPc = stripePhase.HasValue
			? (stripePhase.Value.CpuPhase.InstructionProgramCounter + 8) & 0x00FF_FFFF
			: 0u;
		var stripeLoopStop = stripe.Cycle + (2 * AmigaConstants.A500PalCpuCyclesPerRasterLine);
		var stripeLeadPhases = phases
			.Where(phase =>
				phase.CpuPhase.RequestedCycle >= finalDbra.CpuPhase.RequestedCycle - 32 &&
				phase.CpuPhase.RequestedCycle <= stripe.Cycle + 32)
			.ToArray();
		var stripeLoopWrapPhases = phases
			.Where(phase =>
				phase.CpuPhase.RequestedCycle >= stripe.Cycle &&
				phase.CpuPhase.RequestedCycle <= stripeLoopStop &&
				((machine.Bus.GetBeamPosition(phase.CpuPhase.RequestedCycle).BeamHorizontal >= 218) ||
					 machine.Bus.GetBeamPosition(phase.CpuPhase.RequestedCycle).BeamHorizontal <= 24))
			.ToArray();
		var stripeLoopDbraState = string.Join("; ", phases
			.Where(phase =>
				(phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF) == stripeLoopDbraPc &&
				phase.CpuPhase.RequestedCycle >= stripe.Cycle &&
				phase.CpuPhase.RequestedCycle <= stripeLoopStop)
			.Take(12)
			.Select(phase =>
				$"i={FormatBeam(phase.CpuPhase.InstructionStartCycle)}/" +
				$"entry={FormatBeam(phase.CpuPhase.InstructionEntryBusCycle)}/" +
				$"q{phase.CpuPhase.InstructionEntryPrefetchCount}/" +
				$"r{FormatBeam(phase.CpuPhase.InstructionEntryReadyCycle0)},{FormatBeam(phase.CpuPhase.InstructionEntryReadyCycle1)}/" +
				$"a={phase.CpuPhase.Address & 0x00FF_FFFF:X6}/" +
				$"req={FormatBeam(phase.CpuPhase.RequestedCycle)}/" +
				$"done={FormatBeam(phase.CpuPhase.CompletedCycle)}"));
		var handlerPhases = phases
			.Where(phase =>
				phase.CpuPhase.RequestedCycle >= irqEntry.CpuPhase.RequestedCycle &&
				phase.CpuPhase.RequestedCycle < rteNext.CpuPhase.RequestedCycle)
			.ToArray();
		var handlerWaits = handlerPhases
			.Select(phase =>
			{
				var request = phase.BusAccess?.Request.RequestedCycle ?? phase.CpuPhase.RequestedCycle;
				var grant = phase.BusAccess?.GrantedCycle ?? request;
				return (Phase: phase, Request: request, Grant: grant);
			})
			.Where(item => item.Grant > item.Request)
			.Select(item =>
			{
				var phase = item.Phase;
				var request = item.Request;
				var grant = item.Grant;
				machine.Bus.TryGetCommittedAgnusSlotOwner(AgnusChipSlotScheduler.AlignToSlot(request), out var owner);
				return $"pc{phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF:X6}/" +
					$"{phase.CpuPhase.AccessKind}/a{phase.CpuPhase.Address & 0x00FF_FFFF:X6}/" +
					$"r{FormatBeam(request)}/g{FormatBeam(grant)}/w{grant - request}/{owner}";
			})
			.ToArray();
		var sync4Markers = colorWrites
			.Where(write => write.Value == 0x0404 && write.Cycle < probe.CpuPhase.RequestedCycle)
			.TakeLast(2)
			.ToArray();
		var sync4Phases = sync4Markers.Length == 2
			? phases.Where(phase =>
				phase.CpuPhase.RequestedCycle >= sync4Markers[0].Cycle &&
				phase.CpuPhase.RequestedCycle < sync4Markers[1].Cycle).ToArray()
			: Array.Empty<AmigaCpuBusPhaseTrace>();
		var sync4Waits = string.Join(",", sync4Phases
			.Where(phase => phase.BusAccess?.WaitCycles > 0)
			.Select(phase =>
				$"pc={phase.CpuPhase.InstructionProgramCounter:X6}/a={phase.CpuPhase.Address:X6}/w={phase.BusAccess!.Value.WaitCycles}"));
		var sync4Elapsed = sync4Markers.Length == 2
			? sync4Markers[1].Cycle - sync4Markers[0].Cycle
			: -1;
		var irqDispatch = machine.InterruptDispatchTrace
			.Where(dispatch =>
				dispatch.Level == 3 &&
				dispatch.AcceptanceCycle >= probe.CpuPhase.RequestedCycle &&
				dispatch.AcceptanceCycle <= stripe.Cycle)
			.TakeLast(1);
		var interruptPhases = irqDispatch
			.Select(dispatch => machine.InterruptBusPhaseTrace.FirstOrDefault(window =>
				window.Level == dispatch.Level &&
				window.AcceptanceCycle == dispatch.AcceptanceCycle))
			.Where(window => window != null)
			.SelectMany(window => window!.Phases)
			.OrderBy(phase => phase.CpuPhase.RequestedCycle)
			.ToArray();
		return
			$"postRteLineEdges=[{postRteLineEdges}],postRteLineWaits=[{postRteLineWaits}]," +
			$"dbraLineSlots=[{dbraLineSlots}]," +
			$"probe={FormatCycleBoundary(probe, probeRead?.SampleCycle)},value={probeValue},opcode={probeOpcode}, " +
			$"irqAcknowledge={FormatCycleBoundary(irqAcknowledge, null)}," +
			$"irqEntryDelay={irqEntry.CpuPhase.RequestedCycle - irqAcknowledge.CpuPhase.RequestedCycle}, " +
			$"irqEntry={FormatCycleBoundary(irqEntry, null)}, " +
			$"irqDispatch=[{FormatInterruptDispatches(irqDispatch)}], " +
			$"irqPhases=[{FormatCpuBusPhases(interruptPhases)}], " +
			$"irqRteComplete={FormatCycleBoundary(rteNext, null)}, " +
			$"finalDbra={FormatCycleBoundary(finalDbra, null)},opcode={finalDbraOpcode}, " +
			$"stripe={FormatCycleBoundary(stripePhase.GetValueOrDefault(), stripe.Cycle)},value=0x{stripe.Value:X4},opcode={stripeOpcode}, " +
			$"probeToStripe={(probe.CpuPhase.RequestedCycle == 0 ? -1 : stripe.Cycle - probe.CpuPhase.RequestedCycle)}, " +
			$"dbraElapsed={delayStop - delayStart},dbraIterations={displacementFetches}," +
			$"dbraWait={waitTotal},dbraWaitHistogram=[{waitHistogram}],dbraBlockers=[{waitBlockerSummary}]," +
			$"dbraFull=[{allDbraSummary}],dbraPreIrq=[{preIrqDbraSummary}],dbraPostRte=[{postRteDbraSummary}]," +
			$"dbraEdges=[{FormatCpuBusPhases(dbraEdgePhases)}]," +
			$"dbraEdgeState=[{dbraEdgeState}]," +
			$"handoff=[{handoffState}]," +
			$"postRteEdges=[{postRteEdgeState}]," +
			$"preIrqEdges=[{preIrqEdgeState}]," +
			$"postRteCheckpoints=[{postRteCheckpoints}]," +
			$"futureDbra=[{futureDbra}]," +
			$"earlyPost=[{FormatCpuBusPhases(earlyPostPhases)}]," +
			$"stripeLead=[{FormatCpuBusPhases(stripeLeadPhases)}]," +
			$"stripeLoopDbra=[{stripeLoopDbraState}]," +
			$"stripeLoopWrap=[{FormatCpuBusPhases(stripeLoopWrapPhases)}]," +
			$"delayEdge=[{delayEdge}]," +
			$"handlerElapsed={rteNext.CpuPhase.RequestedCycle - irqEntry.CpuPhase.RequestedCycle}," +
			$"handlerWaits=[{string.Join(',', handlerWaits)}]," +
			$"sync4Elapsed={sync4Elapsed},sync4Waits=[{sync4Waits}]";
	}

	private static string FormatDbraPhaseSummary(
		IReadOnlyList<AmigaCpuBusPhaseTrace> phases,
		uint programCounter,
		AmigaBus bus)
	{
		var displacementAddress = programCounter + 2;
		var displacementFetches = phases
			.Where(phase =>
				phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuInstructionFetch &&
				phase.CpuPhase.Address == displacementAddress)
			.ToArray();
		var deltas = displacementFetches
			.Zip(displacementFetches.Skip(1), (current, next) =>
				next.CpuPhase.RequestedCycle - current.CpuPhase.RequestedCycle)
			.GroupBy(delta => delta)
			.OrderBy(group => group.Key)
			.Select(group => $"{group.Key}:{group.Count()}");
		var waits = phases
			.Select(phase => (phase.BusAccess?.GrantedCycle ?? phase.CpuPhase.RequestedCycle) -
				phase.CpuPhase.RequestedCycle)
			.Where(wait => wait > 0)
			.ToArray();
		var waitPhases = phases
			.Select(phase =>
			{
				var request = phase.BusAccess?.Request.RequestedCycle ?? phase.CpuPhase.RequestedCycle;
				var grant = phase.BusAccess?.GrantedCycle ?? request;
				var stage = phase.CpuPhase.Address == programCounter
					? "op"
					: phase.CpuPhase.Address == displacementAddress ? "disp" : "other";
				return (Stage: stage, Horizontal: bus.GetBeamPosition(request).BeamHorizontal, Wait: grant - request);
			})
			.Where(item => item.Wait > 0)
			.GroupBy(item => (item.Stage, item.Horizontal, item.Wait))
			.OrderBy(group => group.Key.Stage)
			.ThenBy(group => group.Key.Horizontal)
			.ThenBy(group => group.Key.Wait)
			.Select(group => $"{group.Key.Stage}@h{group.Key.Horizontal}/w{group.Key.Wait}:{group.Count()}");
		var blockers = new Dictionary<AgnusChipSlotOwner, int>();
		foreach (var phase in phases)
		{
			var schedulerRequest = phase.BusAccess?.Request.RequestedCycle ?? phase.CpuPhase.RequestedCycle;
			var grant = phase.BusAccess?.GrantedCycle ?? schedulerRequest;
			for (var cycle = AgnusChipSlotScheduler.AlignToSlot(schedulerRequest);
				cycle < grant;
				cycle += AgnusChipSlotScheduler.SlotCycles)
			{
				bus.TryGetCommittedAgnusSlotOwner(cycle, out var owner);
				blockers[owner] = blockers.GetValueOrDefault(owner) + 1;
			}
		}

		var elapsed = phases.Count == 0
			? 0
			: phases[^1].CpuPhase.CompletedCycle - phases[0].CpuPhase.RequestedCycle;
		return $"phases={phases.Count},disp={displacementFetches.Length},elapsed={elapsed}," +
			$"wait={waits.Sum()},deltas={string.Join(',', deltas)}," +
			$"blockers={string.Join(',', blockers.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}:{pair.Value}"))}," +
			$"waitPhases={string.Join(',', waitPhases)}";
	}

	private static bool IsCycleProbeInstruction(byte[] chipRam, uint programCounter)
		=> ReadChipRamWord(chipRam, programCounter) == 0x3029 &&
			ReadChipRamWord(chipRam, programCounter + 2) is 0x0004 or 0x0006;

	private static bool IsCycleDelayDbraInstruction(byte[] chipRam, uint programCounter)
		=> ReadChipRamWord(chipRam, programCounter) == 0x51CB &&
			ReadChipRamWord(chipRam, programCounter + 2) == 0xFFFE;

	private static uint FindRteInstruction(byte[] chipRam, uint handlerPc)
	{
		for (uint offset = 0; offset < 1024; offset += 2)
		{
			var address = handlerPc + offset;
			if (ReadChipRamWord(chipRam, address) == 0x4E73)
			{
				return address & 0x00FF_FFFF;
			}
		}

		return 0;
	}

	private static ushort ReadChipRamWord(byte[] chipRam, uint address)
	{
		var offset = (int)(address & (uint)(chipRam.Length - 1));
		var next = (offset + 1) & (chipRam.Length - 1);
		return (ushort)((chipRam[offset] << 8) | chipRam[next]);
	}

	private static string FormatCycleBoundary(AmigaCpuBusPhaseTrace phase, long? sampleCycle)
	{
		if (phase.CpuPhase.RequestedCycle == 0 &&
			phase.CpuPhase.CompletedCycle == 0 &&
			phase.CpuPhase.Address == 0)
		{
			return "missing";
		}

		var sample = sampleCycle.HasValue ? $",sample={FormatBeam(sampleCycle.Value)}" : string.Empty;
		return $"pc=0x{phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF:X6}@{FormatBeam(phase.CpuPhase.RequestedCycle)}{sample}";
	}

	private static string[] TraceCustomRegisterWrites(string relativePath, Machine machine)
	{
		var writes = machine.Bus.CustomRegisterWrites;
		var customRegisterReads = machine.Bus.CustomRegisterReads.ToArray();
		var diagnosticRegisterReads = machine.Bus.CustomRegisterReadTrace.Count > 0
			? machine.Bus.CustomRegisterReadTrace.ToArray()
			: customRegisterReads;
		var display = machine.Bus.Display.CaptureSnapshot();
		var liveFrameWriteCount = 0;
		var pendingWriteCount = GetPrivateListCount(machine.Bus.Display, "_pendingWrites");
		var pendingWriteIndex = GetPrivateField<int>(machine.Bus.Display, "_pendingIndex");
		var liveFrameWriteOverflowed = false;
		var archivedFrameStartCycle = GetPrivateField<long>(machine.Bus.Display, "_boundPresentationFrameStartCycle");
		var archivedFrameStopCycle = GetPrivateField<long>(machine.Bus.Display, "_boundPresentationFrameStopCycle");
		var colorWrites = writes.Where(write => write.Address == 0x180).ToArray();
		var syncColorWrites = colorWrites
			.Where(write => IsSyncColorValue(write.Value))
			.ToArray();
		var archivedFrameColorWrites = archivedFrameStartCycle >= 0
			? colorWrites
				.Where(write => write.Cycle >= archivedFrameStartCycle &&
					write.Cycle < archivedFrameStartCycle + AmigaConstants.A500PalCpuCyclesPerFrame)
				.ToArray()
			: [];
		var previousFrameColorWrites = archivedFrameStartCycle >= AmigaConstants.A500PalCpuCyclesPerFrame
			? colorWrites
				.Where(write => write.Cycle >= archivedFrameStartCycle - AmigaConstants.A500PalCpuCyclesPerFrame &&
					write.Cycle < archivedFrameStartCycle)
				.ToArray()
			: [];
		var intreqWrites = writes.Where(write => write.Address == 0x09C).ToArray();
		var archivedFrameIntreqWrites = archivedFrameStartCycle >= 0
			? intreqWrites
				.Where(write => write.Cycle >= archivedFrameStartCycle &&
					write.Cycle < archivedFrameStopCycle)
				.ToArray()
			: [];
		var nearbyIntreqWrites = archivedFrameStartCycle >= AmigaConstants.A500PalCpuCyclesPerFrame
			? intreqWrites.Where(write =>
				write.Cycle >= archivedFrameStartCycle - AmigaConstants.A500PalCpuCyclesPerFrame &&
				write.Cycle < archivedFrameStopCycle).ToArray()
			: [];
		var intenaWrites = writes.Where(write => write.Address == 0x09A).ToArray();
		var dmaconWrites = writes.Where(write => write.Address == 0x096).ToArray();
		var bltsizeWrites = writes.Where(write => write.Address == 0x058).ToArray();
		var dmaconReads = machine.Bus.BusAccesses
			.Where(access => access.Request.Target == AmigaBusAccessTarget.CustomRegisters &&
				access.Request.Kind == AmigaBusAccessKind.CpuDataRead &&
				(access.Request.Address & 0x01FE) == 0x002)
			.ToArray();
		var beamReads = machine.Bus.BusAccesses
			.Where(access => access.Request.Target == AmigaBusAccessTarget.CustomRegisters &&
				access.Request.Kind == AmigaBusAccessKind.CpuDataRead &&
				((access.Request.Address & 0x01FE) == 0x004 || (access.Request.Address & 0x01FE) == 0x006))
			.ToArray();
		var vposrReads = beamReads
			.Where(access => (access.Request.Address & 0x01FE) == 0x004)
			.ToArray();
		var archivedFrameCustomRegisterReads = archivedFrameStartCycle >= 0
			? customRegisterReads
				.Where(read => read.RequestedCycle >= archivedFrameStartCycle &&
					read.RequestedCycle < archivedFrameStartCycle + AmigaConstants.A500PalCpuCyclesPerFrame)
				.ToArray()
			: [];
		var archivedFrameBeamReads = archivedFrameStartCycle >= 0
			? beamReads
				.Where(access => access.Request.RequestedCycle >= archivedFrameStartCycle &&
					access.Request.RequestedCycle < archivedFrameStartCycle + AmigaConstants.A500PalCpuCyclesPerFrame)
				.ToArray()
			: [];
		var beamCpuPhases = machine.Bus.CpuBusPhases
			.Where(phase => IsBeamRegisterAddress(phase.CpuPhase.Address))
			.ToArray();
		var vposrCpuPhases = beamCpuPhases
			.Where(phase => (phase.CpuPhase.Address & 0x00FF_FFFE) == 0x00DFF004)
			.ToArray();
		var color00CpuPhases = machine.Bus.CpuBusPhases
			.Where(phase => (phase.CpuPhase.Address & 0x00FF_FFFE) == 0x00DFF180)
			.ToArray();
		var archivedFrameColor00CpuPhases = archivedFrameStartCycle >= 0
			? color00CpuPhases
				.Where(phase => phase.CpuPhase.RequestedCycle >= archivedFrameStartCycle &&
					phase.CpuPhase.RequestedCycle < archivedFrameStartCycle + AmigaConstants.A500PalCpuCyclesPerFrame)
				.ToArray()
			: [];
		var archivedFrameBeamCpuPhases = archivedFrameStartCycle >= 0
			? beamCpuPhases
				.Where(phase => phase.CpuPhase.RequestedCycle >= archivedFrameStartCycle &&
					phase.CpuPhase.RequestedCycle < archivedFrameStartCycle + AmigaConstants.A500PalCpuCyclesPerFrame)
				.ToArray()
			: [];
		var normalizedCasePath = NormalizeCasePath(relativePath);
		if (normalizedCasePath.StartsWith(
			"Paula/Drive/",
			StringComparison.OrdinalIgnoreCase))
		{
			var disk = machine.Bus.Disk.CaptureSnapshot();
			var dmaTrace = machine.Bus.Disk.CaptureDmaTrace();
			var frameInterrupts = machine.InterruptDispatchTrace
				.Where(dispatch =>
					dispatch.AcceptanceCycle >= archivedFrameStartCycle &&
					dispatch.AcceptanceCycle < archivedFrameStopCycle)
				.ToArray();
			return
			[
				$"TRACE {relativePath}: cpu d0=0x{machine.Cpu.State.D[0]:X8}, " +
				$"d1=0x{machine.Cpu.State.D[1]:X8}, d2=0x{machine.Cpu.State.D[2]:X8}, " +
				$"pc=0x{machine.Cpu.State.ProgramCounter & 0x00FF_FFFF:X6}",
				$"TRACE {relativePath}: disk ptr=0x{disk.DiskPointer:X8}, " +
				$"dsklen=0x{disk.Dsklen:X4}, dskbytr=0x{disk.Dskbytr:X4}, " +
				$"dskdatr=0x{disk.Dskdatr:X4}, selected={disk.SelectedDrive}, " +
				$"cyl={disk.Cylinder}, head={disk.Head}, motor={disk.MotorOn}, " +
				$"transfers={disk.TransferCount}",
				$"TRACE {relativePath}: DMA " + string.Join(
					"; ",
					dmaTrace.TakeLast(32).Select(entry =>
						$"{entry.Kind}@{FormatBeam(entry.Cycle)} " +
						$"len=0x{entry.Dsklen:X4} req={entry.RequestedWords} " +
						$"done={entry.TransferredWords} complete={FormatBeam(entry.CompletionCycle)} " +
						$"blocked={entry.BlockedReason}")),
				$"TRACE {relativePath}: interrupts {FormatInterruptDispatches(frameInterrupts)}",
				$"TRACE {relativePath}: frame COLOR00 " + string.Join(
					"; ",
					archivedFrameColorWrites.Select(write =>
						$"{FormatBeam(write.Cycle)}=0x{write.Value:X4}")),
				$"TRACE {relativePath}: frame INTREQ " + string.Join(
					"; ",
					archivedFrameIntreqWrites.Select(write =>
						$"{FormatBeam(write.Cycle)}=0x{write.Value:X4}"))
			];
		}

		if (normalizedCasePath.StartsWith(
				"Agnus/Blitter/",
				StringComparison.OrdinalIgnoreCase))
		{
			var frameStarts = bltsizeWrites
				.Where(write => write.Cycle >= archivedFrameStartCycle &&
					write.Cycle < archivedFrameStopCycle)
				.ToArray();
			var frameCompletions = machine.Bus.Blitter.CompletionCycles
				.Where(cycle => cycle >= archivedFrameStartCycle &&
					cycle < archivedFrameStopCycle)
				.ToArray();
			var frameBusyReads = dmaconReads
				.Where(access => access.GrantedCycle >= archivedFrameStartCycle &&
					access.GrantedCycle < archivedFrameStopCycle)
				.ToArray();
			var frameBlitterPriorityWrites = dmaconWrites
				.Where(write => write.Cycle >= archivedFrameStartCycle &&
					write.Cycle < archivedFrameStopCycle &&
					(write.Value & 0x0400) != 0)
				.ToArray();
			var firstBlitterPriorityClear = frameBlitterPriorityWrites
				.FirstOrDefault(write =>
					frameStarts.Length != 0 &&
					write.Cycle > frameStarts[0].Cycle &&
					(write.Value & 0x8000) == 0);
			var firstCpuStripe = archivedFrameColorWrites
				.FirstOrDefault(write =>
					firstBlitterPriorityClear.Cycle != 0 &&
					write.Cycle > firstBlitterPriorityClear.Cycle &&
					write.Value == 0x044F);
			var firstPrepareBlitStart = archivedFrameColorWrites
				.FirstOrDefault(write =>
					write.Value == 0x0F00 &&
					archivedFrameIntreqWrites.Any(interrupt =>
						interrupt.Cycle < write.Cycle &&
						(interrupt.Value & 0x8004) == 0x8004));
			var firstPrepareBlitEnd = archivedFrameColorWrites
				.FirstOrDefault(write =>
					firstPrepareBlitStart.Cycle != 0 &&
					write.Cycle > firstPrepareBlitStart.Cycle &&
					write.Value == 0x0666);
			var bltpriReleaseCpuPhases =
				firstBlitterPriorityClear.Cycle != 0 &&
				firstCpuStripe.Cycle != 0
					? machine.Bus.CpuBusPhases
						.Where(phase =>
							phase.CpuPhase.RequestedCycle >=
								firstBlitterPriorityClear.Cycle - 4 &&
							phase.CpuPhase.RequestedCycle <=
								firstCpuStripe.Cycle + 4)
						.ToArray()
					: [];
			var frameBlitterProgramCpuPhases = machine.Bus.CpuBusPhases
				.Where(phase =>
				{
					var pc = phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF;
					return phase.CpuPhase.RequestedCycle >= archivedFrameStartCycle &&
						phase.CpuPhase.RequestedCycle < archivedFrameStopCycle &&
						pc is >= 0x0703D0 and <= 0x070460;
				})
				.ToArray();
			var framePrepareBlitCpuPhases = machine.Bus.CpuBusPhases
				.Where(phase =>
				{
					var pc = phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF;
					return phase.CpuPhase.RequestedCycle >= archivedFrameStartCycle &&
						phase.CpuPhase.RequestedCycle < archivedFrameStopCycle &&
						pc is >= 0x070364 and <= 0x0703BC;
				})
				.ToArray();
			var frameBlitterProgramLongWaitCpuPhases =
				frameBlitterProgramCpuPhases
					.Where(phase =>
						phase.CpuPhase.CompletedCycle -
							phase.CpuPhase.RequestedCycle >
						(4 * AgnusChipSlotScheduler.SlotCycles))
					.ToArray();
			var frameInterruptDispatches = machine.InterruptDispatchTrace
				.Where(dispatch =>
					dispatch.AcceptanceCycle >= archivedFrameStartCycle &&
					dispatch.AcceptanceCycle < archivedFrameStopCycle)
				.ToArray();
			var frameInterruptBusWindows = frameInterruptDispatches
				.Select(dispatch =>
					machine.InterruptBusPhaseTrace.FirstOrDefault(window =>
						window.Level == dispatch.Level &&
						window.AcceptanceCycle == dispatch.AcceptanceCycle))
				.Where(window => window != null)
				.Select(window =>
					$"L{window!.Level}@{FormatBeam(window.AcceptanceCycle)} " +
					$"[{FormatCpuBusPhases(window.Phases)}]")
				.ToArray();
			var firstInterruptCpuWindow = frameInterruptDispatches.Length == 0
				? []
				: machine.Bus.CpuBusPhases
					.Where(phase =>
						phase.CpuPhase.RequestedCycle >=
							frameInterruptDispatches[0].CpuVisibleCycle - 48 &&
						phase.CpuPhase.RequestedCycle <=
							frameInterruptDispatches[0].AcceptanceCycle)
					.ToArray();
			var frameCopperColors = machine.Bus.Display.CopperDisplayWrites
				.Where(write =>
					write.Address == 0x180 &&
					write.Cycle >= archivedFrameStartCycle &&
					write.Cycle < archivedFrameStopCycle)
				.ToArray();
			var frameBlitWindowStats = frameStarts
				.Select(start =>
				{
					var completion = frameCompletions
						.FirstOrDefault(cycle => cycle >= start.Cycle);
					if (completion <= 0)
					{
						completion = archivedFrameStopCycle;
					}
					var accesses = machine.Bus.BusAccesses
						.Where(access =>
							access.GrantedCycle >= start.Cycle &&
							access.GrantedCycle <= completion)
						.ToArray();
					var blitterAccesses = accesses
						.Where(access =>
							access.Request.Requester ==
								AmigaBusRequester.Blitter)
						.ToArray();
					var cpuAccesses = accesses
						.Where(access =>
							access.Request.Requester ==
								AmigaBusRequester.Cpu)
						.ToArray();
					var occupiedCycles = accesses
						.Select(access => access.GrantedCycle)
						.ToHashSet();
					var collidingSlots = accesses
						.GroupBy(access => access.GrantedCycle)
						.Where(group => group.Count() > 1)
						.ToArray();
					var collisionHistogram = collidingSlots
						.GroupBy(group => GetPalBeamHorizontal(group.Key))
						.OrderBy(group => group.Key)
						.Select(group => $"{group.Key:D3}={group.Count()}");
					var collisionOwners = collidingSlots
						.GroupBy(group => string.Join(
							'+',
							group
								.Select(access => access.Request.Requester)
								.OrderBy(owner => owner)))
						.OrderBy(group => group.Key)
						.Select(group => $"{group.Key}={group.Count()}");
					var physicalSlotCount = checked((int)(
						(completion - start.Cycle) /
							AgnusChipSlotScheduler.SlotCycles) + 1);
					var gapCycles = Enumerable
						.Range(0, physicalSlotCount)
						.Select(index =>
							start.Cycle +
							(index * AgnusChipSlotScheduler.SlotCycles))
						.Where(cycle => !occupiedCycles.Contains(cycle))
						.ToArray();
					var gapHistogram = gapCycles
						.GroupBy(GetPalBeamHorizontal)
						.OrderBy(group => group.Key)
						.Select(group => $"{group.Key:D3}={group.Count()}");
					var copperPairGuardCycles = gapCycles
						.Where(cycle => accesses.Any(access =>
							access.GrantedCycle ==
								cycle - AgnusChipSlotScheduler.SlotCycles &&
							access.Request.Requester ==
								AmigaBusRequester.Copper))
						.ToHashSet();
					var unclassifiedGapCycles = gapCycles
						.Where(cycle =>
							!machine.Bus.IsMandatoryRefreshSlot(cycle) &&
							!copperPairGuardCycles.Contains(cycle))
						.Select(FormatBeam);
					return
						$"{FormatBeam(start.Cycle)}..{FormatBeam(completion)}:" +
						$"span={completion - start.Cycle}," +
						$"blitter={blitterAccesses.Length}," +
						$"cpu={cpuAccesses.Length}," +
						$"cpuFetch={cpuAccesses.Count(access => access.Request.Kind == AmigaBusAccessKind.CpuInstructionFetch)}," +
						$"cpuCustom={cpuAccesses.Count(access => access.Request.Target == AmigaBusAccessTarget.CustomRegisters)}," +
						$"cpuRows=[{string.Join(',', cpuAccesses
							.GroupBy(access => GetPalBeamLine(access.GrantedCycle))
							.OrderBy(group => group.Key)
							.Select(group => $"{group.Key:D3}={group.Count()}"))}]," +
						$"collisions={collidingSlots.Length}:[{string.Join(',', collisionHistogram)}]/[{string.Join(',', collisionOwners)}]," +
						$"gaps=[{string.Join(',', gapHistogram)}]," +
						$"copperPairGuards=[{string.Join(',', copperPairGuardCycles.Select(FormatBeam))}]," +
						$"unclassifiedGaps=[{string.Join(',', unclassifiedGapCycles)}]," +
						$"owners=[{string.Join(',', accesses
							.GroupBy(access => access.Request.Requester)
							.OrderBy(group => group.Key)
							.Select(group => $"{group.Key}={group.Count()}"))}]";
				})
				.ToArray();
			var liveDiagnostics = machine.Bus.AgnusLiveBlitterDiagnostics;
			var liveCopperDiagnostics = machine.Bus.AgnusLiveCopperDiagnostics;
			var earlyWindowStart = archivedFrameStartCycle + (30 * AmigaConstants.A500PalCpuCyclesPerRasterLine);
			var earlyWindowStop = archivedFrameStartCycle + (35 * AmigaConstants.A500PalCpuCyclesPerRasterLine);
			var earlyBeamReads = customRegisterReads
				.Where(read =>
					(read.Address & 0x01FE) == 0x006 &&
					read.RequestedCycle >= earlyWindowStart &&
					read.RequestedCycle < earlyWindowStop)
				.ToArray();
			var earlyCpuPhases = machine.Bus.CpuBusPhases
				.Where(phase =>
					phase.CpuPhase.RequestedCycle >= earlyWindowStart &&
					phase.CpuPhase.RequestedCycle < earlyWindowStop)
				.ToArray();
			return
			[
				$"TRACE {relativePath}: BLTSIZE {string.Join("; ", frameStarts.Select(write => $"{FormatBeam(write.Cycle)}=0x{write.Value:X4}"))}",
				$"TRACE {relativePath}: completion {string.Join("; ", frameCompletions.Select(FormatBeam))}",
				$"TRACE {relativePath}: completion bus {string.Join(" | ", frameCompletions.Select(completion =>
					$"{FormatBeam(completion)}=[{FormatBusAccesses(machine.Bus.BusAccesses.Where(access =>
						access.GrantedCycle >= completion - 12 &&
						access.GrantedCycle <= completion + 12))}]"))}",
				$"TRACE {relativePath}: blit windows {string.Join("; ", frameBlitWindowStats)}",
				$"TRACE {relativePath}: BLTPRI {string.Join("; ", frameBlitterPriorityWrites.Select(write => $"{FormatBeam(write.Cycle)}={((write.Value & 0x8000) != 0 ? "SET" : "CLEAR")}(0x{write.Value:X4})"))}",
				$"TRACE {relativePath}: INTREQ {string.Join("; ", archivedFrameIntreqWrites.Select(write => $"{FormatBeam(write.Cycle)}=0x{write.Value:X4}"))}",
				$"TRACE {relativePath}: DMACONR reads {FormatBusAccesses(frameBusyReads)}",
				$"TRACE {relativePath}: DMACONR values {FormatCustomRegisterReads(customRegisterReads.Where(read =>
					(read.Address & 0x01FE) == 0x002 &&
					read.RequestedCycle >= archivedFrameStartCycle &&
					read.RequestedCycle < archivedFrameStopCycle))}",
				$"TRACE {relativePath}: early VHPOSR reads {FormatCustomRegisterReads(earlyBeamReads)}",
				$"TRACE {relativePath}: early CPU phases {FormatCpuBusPhases(earlyCpuPhases)}",
				$"TRACE {relativePath}: COLOR00 count={archivedFrameColorWrites.Length}, " +
					$"all={string.Join("; ", archivedFrameColorWrites.Select(write => $"{FormatBeam(write.Cycle)}=0x{write.Value:X4}"))}",
				$"TRACE {relativePath}: COLOR00 CPU phases first={FormatCpuBusPhases(archivedFrameColor00CpuPhases.Take(24))}",
				$"TRACE {relativePath}: BLTPRI release CPU phases {FormatCpuBusPhases(bltpriReleaseCpuPhases)}",
				$"TRACE {relativePath}: BLTPRI release bus {FormatBusAccesses(machine.Bus.BusAccesses.Where(access => firstBlitterPriorityClear.Cycle != 0 && firstCpuStripe.Cycle != 0 && access.GrantedCycle >= firstBlitterPriorityClear.Cycle - 12 && access.GrantedCycle <= firstCpuStripe.Cycle + 4))}",
				$"TRACE {relativePath}: first BLTSIZE bus {FormatBusAccesses(machine.Bus.BusAccesses.Where(access => frameStarts.Length != 0 && access.GrantedCycle >= frameStarts[0].Cycle - 16 && access.GrantedCycle <= frameStarts[0].Cycle + 32))}",
				$"TRACE {relativePath}: prepareblit CPU phases first={FormatCpuBusPhases(framePrepareBlitCpuPhases.Take(64))}",
				$"TRACE {relativePath}: prepareblit bus first={FormatBusAccesses(machine.Bus.BusAccesses.Where(access => firstPrepareBlitStart.Cycle != 0 && firstPrepareBlitEnd.Cycle != 0 && access.GrantedCycle >= firstPrepareBlitStart.Cycle && access.GrantedCycle <= firstPrepareBlitEnd.Cycle))}",
				$"TRACE {relativePath}: blitter-program long-wait CPU phases first={FormatCpuBusPhases(frameBlitterProgramLongWaitCpuPhases.Take(32))}",
				$"TRACE {relativePath}: blitter-program long-wait CPU phases last={FormatCpuBusPhases(frameBlitterProgramLongWaitCpuPhases.TakeLast(32))}",
				$"TRACE {relativePath}: interrupt dispatches {FormatInterruptDispatches(frameInterruptDispatches)}",
				$"TRACE {relativePath}: first interrupt CPU window {FormatCpuBusPhases(firstInterruptCpuWindow)}",
				$"TRACE {relativePath}: interrupt bus windows {string.Join(" | ", frameInterruptBusWindows)}",
				$"TRACE {relativePath}: first INTREQ chip bus {FormatBusAccesses(machine.Bus.BusAccesses.Where(access => archivedFrameIntreqWrites.Length != 0 && access.GrantedCycle >= archivedFrameIntreqWrites[0].Cycle - 20 && access.GrantedCycle <= archivedFrameIntreqWrites[0].Cycle + 8))}",
				$"TRACE {relativePath}: Copper COLOR00 {string.Join("; ", frameCopperColors.Select(write => $"{FormatBeam(write.Cycle)}=0x{write.Value:X4}"))}",
				$"TRACE {relativePath}: Copper WAIT control {string.Join("; ", machine.Bus.Display.CopperWaitTransitions.Where(trace => trace.SatisfiedCycle >= archivedFrameStartCycle && trace.SatisfiedCycle < archivedFrameStopCycle).Select(trace => $"pc=0x{trace.Pc:X6} wait=0x{trace.WaitFirst:X4}/0x{trace.WaitSecond:X4} cmp={FormatBeam(trace.ComparisonCycle)} sat={FormatBeam(trace.SatisfiedCycle)} restart={FormatBeam(trace.RestartCycle)} carry={trace.CarryPending}/{trace.CarrySkipCount} blocked={trace.RestartIncomingRgaBlocked}"))}",
				$"TRACE {relativePath}: boundary presentation {string.Join("; ", machine.Bus.Display.CopperPresentationTransitions
					.Where(trace => trace.Row is 22 or 53 or 86 or 123 or 124 or 166 or 192 or 215)
					.Select(trace =>
						$"{(trace.IsCopper ? "copper" : "cpu")}@{FormatBeam(trace.Cycle)} " +
						$"row={trace.Row} x={trace.X} reg=0x{trace.Offset:X3} value=0x{trace.Value:X4} " +
						$"started={trace.HasStartedLine} bplcon0=0x{trace.Bplcon0:X4} " +
						$"dmacon=0x{trace.Dmacon:X4} planes={trace.PlaneCount}"))}",
				$"TRACE {relativePath}: early boundary presentation {string.Join("; ", machine.Bus.Display.CopperPresentationTransitions
					.Where(trace => trace.Row is 6 or 7)
					.Select(trace =>
						$"{(trace.IsCopper ? "copper" : "cpu")}@{FormatBeam(trace.Cycle)} " +
						$"row={trace.Row} x={trace.X} reg=0x{trace.Offset:X3} value=0x{trace.Value:X4} " +
						$"started={trace.HasStartedLine} bplcon0=0x{trace.Bplcon0:X4} " +
						$"dmacon=0x{trace.Dmacon:X4} planes={trace.PlaneCount}"))}",
				$"TRACE {relativePath}: early rendered timeline {string.Join("; ", machine.Bus.Display.RenderedCopperTimelineSegments
					.Where(segment => segment.Row is >= 5 and <= 9)
					.Select(segment => $"row={segment.Row} {segment.XStart}-{segment.XStop} palette={segment.PaletteSnapshotIndex} color0=0x{segment.Color0:X3}"))}",
				$"TRACE {relativePath}: mid rendered timeline {string.Join("; ", machine.Bus.Display.RenderedCopperTimelineSegments
					.Where(segment => segment.Row is >= 12 and <= 17)
					.Select(segment => $"row={segment.Row} {segment.XStart}-{segment.XStop} palette={segment.PaletteSnapshotIndex} color0=0x{segment.Color0:X3}"))}",
				$"TRACE {relativePath}: selected rendered rows " + string.Join("; ", new[] { 38, 39, 40, 41, 42, 43, 44, 45 }.Select(row =>
					$"row={row}[{string.Join(',', machine.Bus.Display.CaptureCopperTimelineSegments(row).Select(segment => string.Concat(segment.XStart, "-", segment.XStop, ":p", segment.PaletteSnapshotIndex, "/c", segment.Color0.ToString("X3"))))}]")),
				$"TRACE {relativePath}: selected presentation rows " + string.Join("; ", machine.Bus.Display.CopperPresentationTransitions
					.Where(trace => trace.Row is >= 38 and <= 45)
					.Select(trace => $"{(trace.IsCopper ? "copper" : "cpu")}@{FormatBeam(trace.Cycle)} row={trace.Row} x={trace.X} reg=0x{trace.Offset:X3} value=0x{trace.Value:X4}")),
				$"TRACE {relativePath}: live requests={liveDiagnostics.PublishedRequests}, grants={liveDiagnostics.GrantedRequests}, denials={liveDiagnostics.DeniedRequests}, areaWords={liveDiagnostics.AreaWords}, linePixels={liveDiagnostics.LinePixels}, completions={liveDiagnostics.Completions}, interrupts={liveDiagnostics.Interrupts}, forbidden={liveDiagnostics.ForbiddenCalls}",
				$"TRACE {relativePath}: live Copper requests={liveCopperDiagnostics.PublishedRequests}, grants={liveCopperDiagnostics.GrantedRequests}, denials={liveCopperDiagnostics.DeniedRequests}, causalDeferrals={liveCopperDiagnostics.CausalDeferrals}, deferredCycles={liveCopperDiagnostics.CausalDeferredCycles}, maxLag={liveCopperDiagnostics.MaxCausalLagCycles}, firstDeferral={FormatBeam(liveCopperDiagnostics.FirstCausalDeferralCycle)}, forbidden={liveCopperDiagnostics.ForbiddenCalls}"
			];
		}

		if (normalizedCasePath.StartsWith("Agnus/Copper/Wait/waitblt", StringComparison.OrdinalIgnoreCase))
		{
			var frameBlitter = machine.Bus.BusAccesses
				.Where(access => access.Request.Requester == AmigaBusRequester.Blitter &&
					access.GrantedCycle >= archivedFrameStartCycle &&
					access.GrantedCycle < archivedFrameStopCycle)
				.ToArray();
			var frameStarts = bltsizeWrites
				.Where(write => write.Cycle >= archivedFrameStartCycle && write.Cycle < archivedFrameStopCycle)
				.ToArray();
			var frameColors = machine.Bus.Display.CopperDisplayWrites
				.Where(write => write.Address == 0x180 &&
					write.Cycle >= archivedFrameStartCycle && write.Cycle < archivedFrameStopCycle)
				.ToArray();
			var frameBlitterInterrupts = intreqWrites
				.Where(write => (write.Value & AmigaConstants.IntreqBlitter) != 0 &&
					write.Cycle >= archivedFrameStartCycle && write.Cycle < archivedFrameStopCycle)
				.ToArray();
			var frameCompletions = machine.Bus.Blitter.CompletionCycles
				.Where(cycle => cycle >= archivedFrameStartCycle && cycle < archivedFrameStopCycle)
				.ToArray();
			var frameIntreqWrites = intreqWrites
				.Where(write => write.Cycle >= archivedFrameStartCycle && write.Cycle < archivedFrameStopCycle)
				.ToArray();
			var frameDisplayControlWrites = writes
				.Where(write => write.Cycle >= archivedFrameStartCycle &&
					write.Cycle < archivedFrameStopCycle &&
					write.Address is 0x08E or 0x090 or 0x092 or 0x094 or 0x096 or
						0x100 or 0x102 or 0x108 or 0x10A)
				.ToArray();
			var firstVisibleBitplaneFetches = machine.Bus.BusAccesses
				.Where(access => access.Request.Requester == AmigaBusRequester.Bitplane &&
					access.GrantedCycle >= archivedFrameStartCycle &&
					access.GrantedCycle < archivedFrameStopCycle)
				.Take(24)
				.ToArray();
			var cpuBltsizePhases = machine.Bus.CpuBusPhases
				.Where(phase => phase.CpuPhase.IsWrite &&
					(phase.CpuPhase.Address & 0x00FF_FFFE) == 0x00DFF058 &&
					phase.CpuPhase.RequestedCycle >= archivedFrameStartCycle &&
					phase.CpuPhase.RequestedCycle < archivedFrameStopCycle)
				.ToArray();
			var frameInterruptDispatches = machine.InterruptDispatchTrace
				.Where(dispatch => dispatch.AcceptanceCycle >= archivedFrameStartCycle &&
					dispatch.AcceptanceCycle < archivedFrameStopCycle)
				.ToArray();
			var irqDispatchWindows = frameInterruptDispatches.Select(dispatch =>
			{
				var phases = machine.Bus.CpuBusPhases.Where(phase =>
					phase.CpuPhase.RequestedCycle >= dispatch.AcceptanceCycle - 40 &&
					phase.CpuPhase.RequestedCycle <= dispatch.EntryCompletedCycle + 48);
				return $"L{dispatch.Level}@{FormatBeam(dispatch.AcceptanceCycle)} [{FormatCpuBusPhases(phases)}]";
			}).ToArray();
			var irqHandlerWindows = cpuBltsizePhases.Select(bltsize =>
			{
				var start = bltsize.CpuPhase.RequestedCycle - 80;
				var stop = bltsize.CpuPhase.CompletedCycle + 16;
				var phases = machine.Bus.CpuBusPhases.Where(phase =>
					phase.CpuPhase.RequestedCycle >= start &&
					phase.CpuPhase.RequestedCycle <= stop);
				return $"BLTSIZE={FormatBeam(bltsize.CpuPhase.RequestedCycle)} [{FormatCpuBusPhases(phases)}]";
			}).ToArray();
			var blitLedgers = frameStarts.Select((start, index) =>
			{
				var stop = index + 1 < frameStarts.Length ? frameStarts[index + 1].Cycle : archivedFrameStopCycle;
				var accesses = frameBlitter
					.Where(access => access.GrantedCycle >= start.Cycle && access.GrantedCycle < stop)
					.ToArray();
				var competingOwners = machine.Bus.BusAccesses
					.Where(access => access.GrantedCycle >= start.Cycle &&
						access.GrantedCycle <= (accesses.Length == 0 ? stop : accesses[^1].CompletedCycle) &&
						access.Request.Requester != AmigaBusRequester.Blitter)
					.GroupBy(access => access.Request.Requester)
					.OrderBy(group => group.Key)
					.Select(group => $"{group.Key}={group.Count()}");
				var copperAccesses = machine.Bus.BusAccesses
					.Where(access => access.Request.Requester == AmigaBusRequester.Copper &&
						access.GrantedCycle >= start.Cycle &&
						access.GrantedCycle <= (accesses.Length == 0 ? stop : accesses[^1].CompletedCycle))
					.ToArray();
				var copperWindow = copperAccesses.Select(copper =>
					$"C:{FormatBeam(copper.Request.RequestedCycle)}>{FormatBeam(copper.GrantedCycle)} B:[{FormatBusAccesses(accesses.Where(access => Math.Abs(access.GrantedCycle - copper.GrantedCycle) <= 16))}]");
				var completion = index < frameCompletions.Length ? frameCompletions[index] : stop;
				var drainOwners = accesses.Length == 0
					? Array.Empty<string>()
					: machine.Bus.BusAccesses
						.Where(access => access.GrantedCycle >= accesses[^1].CompletedCycle &&
							access.GrantedCycle < completion)
						.GroupBy(access => access.Request.Requester)
						.OrderBy(group => group.Key)
						.Select(group => $"{group.Key}={group.Count()}")
						.ToArray();
				return accesses.Length == 0
					? $"{FormatBeam(start.Cycle)}:none"
					: $"{FormatBeam(start.Cycle)}:count={accesses.Length},last={FormatBeam(accesses[^1].GrantedCycle)}..{FormatBeam(accesses[^1].CompletedCycle)},completion={FormatBeam(completion)},drain=[{string.Join(",", drainOwners)}],competing=[{string.Join(",", competingOwners)}],windows=[{string.Join(";", copperWindow)}]";
			}).ToArray();
			var bOnlyCadence = frameStarts.Select((start, index) =>
			{
				var stop = index + 1 < frameStarts.Length ? frameStarts[index + 1].Cycle : archivedFrameStopCycle;
				var accesses = frameBlitter
					.Where(access => access.GrantedCycle >= start.Cycle && access.GrantedCycle < stop)
					.ToArray();
				var gapHistogram = accesses
					.Skip(1)
					.Select((access, accessIndex) => access.GrantedCycle - accesses[accessIndex].GrantedCycle)
					.GroupBy(gap => gap)
					.OrderBy(group => group.Key)
					.Select(group => $"{group.Key}={group.Count()}");
				return $"{FormatBeam(start.Cycle)} gaps=[{string.Join(',', gapHistogram)}]";
			}).ToArray();
			var niceCpuGrantLedgers = frameStarts.Select((start, index) =>
			{
				var completion = index < frameCompletions.Length
					? frameCompletions[index]
					: archivedFrameStopCycle;
				var grants = Enumerable.Range(
					0,
					(int)Math.Max(0, (completion - start.Cycle) / AgnusChipSlotScheduler.SlotCycles + 1))
					.Select(slot => start.Cycle + slot * AgnusChipSlotScheduler.SlotCycles)
					.Where(machine.Bus.IsCpuGrantAfterNiceBlitterWait)
					.ToArray();
				return grants.Length == 0
					? $"{FormatBeam(start.Cycle)}:none"
					: $"{FormatBeam(start.Cycle)}:count={grants.Length},first={FormatBeam(grants[0])},last={FormatBeam(grants[^1])}";
			}).ToArray();
			var niceCpuGrantCycles = frameStarts.SelectMany((start, index) =>
			{
				var completion = index < frameCompletions.Length
					? frameCompletions[index]
					: archivedFrameStopCycle;
				return Enumerable.Range(
						0,
						(int)Math.Max(0, (completion - start.Cycle) / AgnusChipSlotScheduler.SlotCycles + 1))
					.Select(slot => start.Cycle + slot * AgnusChipSlotScheduler.SlotCycles)
					.Where(machine.Bus.IsCpuGrantAfterNiceBlitterWait);
			}).ToArray();
			var niceCpuGrantWindowCycles = frameStarts.SelectMany((start, index) =>
			{
				var completion = index < frameCompletions.Length
					? frameCompletions[index]
					: archivedFrameStopCycle;
				var grants = niceCpuGrantCycles
					.Where(cycle => cycle >= start.Cycle && cycle <= completion)
					.ToArray();
				return grants.Take(1).Concat(grants.TakeLast(1));
			}).Distinct();
			var niceCpuGrantWindows = niceCpuGrantWindowCycles
				.Select(grant =>
					$"{FormatBeam(grant)}=[{FormatBusAccesses(machine.Bus.BusAccesses.Where(access =>
						access.GrantedCycle >= grant - 12 &&
						access.GrantedCycle <= grant + 12))}]")
				.ToArray();
			var liveBlitter = machine.Bus.AgnusLiveBlitterDiagnostics;
			return
			[
				$"TRACE {relativePath}: first IRQ chip bus {FormatBusAccesses(machine.Bus.BusAccesses.Where(access => frameIntreqWrites.Length != 0 && access.GrantedCycle >= frameIntreqWrites[0].Cycle - 12 && access.GrantedCycle <= frameIntreqWrites[0].Cycle + 16))}",
				$"TRACE {relativePath}: first IRQ prelude bus {FormatBusAccesses(machine.Bus.BusAccesses.Where(access => frameIntreqWrites.Length != 0 && access.GrantedCycle >= frameIntreqWrites[0].Cycle - 52 && access.GrantedCycle <= frameIntreqWrites[0].Cycle + 8))}",
				$"TRACE {relativePath}: steady branch all bus " + FormatBusAccesses(machine.Bus.BusAccesses.Where(access =>
					GetPalBeamLine(access.GrantedCycle) is 48 or 240 &&
					GetPalBeamHorizontal(access.GrantedCycle) >= 28 &&
					GetPalBeamHorizontal(access.GrantedCycle) <= 44)),
				$"TRACE {relativePath}: first IRQ extended bus {FormatBusAccesses(machine.Bus.BusAccesses.Where(access => frameIntreqWrites.Length != 0 && access.GrantedCycle >= frameIntreqWrites[0].Cycle - 20 && access.GrantedCycle <= frameIntreqWrites[0].Cycle + 120))}",
				$"TRACE {relativePath}: frame COLOR00 writes {string.Join("; ", archivedFrameColorWrites.Select(write => $"{FormatBeam(write.Cycle)}=0x{write.Value:X4}"))}",
				$"TRACE {relativePath}: frame COLOR00 CPU phases {FormatCpuBusPhases(archivedFrameColor00CpuPhases)}",
				$"TRACE {relativePath}: blitter starts {string.Join("; ", frameStarts.Select(write => $"{FormatBeam(write.Cycle)}=0x{write.Value:X4}"))}",
				$"TRACE {relativePath}: blitter ledgers {string.Join("; ", blitLedgers)}",
				$"TRACE {relativePath}: B-only cadence {string.Join("; ", bOnlyCadence)}",
				$"TRACE {relativePath}: nice CPU grants {string.Join("; ", niceCpuGrantLedgers)}",
				$"TRACE {relativePath}: nice CPU grant windows {string.Join("; ", niceCpuGrantWindows)}",
				$"TRACE {relativePath}: blitter completions {string.Join("; ", frameCompletions.Select(FormatBeam))}",
				$"TRACE {relativePath}: blitter DMA count={frameBlitter.Length}, first={FormatBusAccesses(frameBlitter.Take(8))}, last={FormatBusAccesses(frameBlitter.TakeLast(8))}",
				$"TRACE {relativePath}: live blitter requests={liveBlitter.PublishedRequests}, grants={liveBlitter.GrantedRequests}, denials={liveBlitter.DeniedRequests}, lastDenied={FormatBeam(liveBlitter.LastDeniedCycle)}/{liveBlitter.LastDeniedOwner}",
				$"TRACE {relativePath}: blitter INTREQ {string.Join("; ", frameBlitterInterrupts.Select(write => $"{FormatBeam(write.Cycle)}=0x{write.Value:X4}"))}",
				$"TRACE {relativePath}: frame INTREQ {string.Join("; ", frameIntreqWrites.Select(write => $"{FormatBeam(write.Cycle)}=0x{write.Value:X4}"))}",
				$"TRACE {relativePath}: frame display control {string.Join("; ", frameDisplayControlWrites.Select(write => $"{FormatBeam(write.Cycle)} reg=0x{write.Address:X3} value=0x{write.Value:X4}"))}",
				$"TRACE {relativePath}: first bitplane DMA {FormatBusAccesses(firstVisibleBitplaneFetches)}",
				$"TRACE {relativePath}: Copper WAIT control {string.Join("; ", machine.Bus.Display.CopperWaitTransitions.Where(trace => trace.SatisfiedCycle >= archivedFrameStartCycle && trace.SatisfiedCycle < archivedFrameStopCycle).Select(trace => $"pc=0x{trace.Pc:X6} wait=0x{trace.WaitFirst:X4}/0x{trace.WaitSecond:X4} cmp={FormatBeam(trace.ComparisonCycle)} sat={FormatBeam(trace.SatisfiedCycle)} restart={FormatBeam(trace.RestartCycle)} carry={trace.CarryPending}/{trace.CarrySkipCount} blocked={trace.RestartIncomingRgaBlocked}"))}",
				$"TRACE {relativePath}: interrupt dispatches {FormatInterruptDispatches(frameInterruptDispatches)}",
				$"TRACE {relativePath}: interrupt bus windows {string.Join(" | ", irqDispatchWindows)}",
				$"TRACE {relativePath}: IRQ handler windows {string.Join(" | ", irqHandlerWindows)}",
				$"TRACE {relativePath}: Copper COLOR00 {string.Join("; ", frameColors.Select(write => $"{FormatBeam(write.Cycle)}=0x{write.Value:X3}"))}",
				$"TRACE {relativePath}: boundary presentation {string.Join("; ", machine.Bus.Display.CopperPresentationTransitions
					.Where(trace => trace.Row is 22 or 192 or 214 or 248)
					.TakeLast(12)
					.Select(trace =>
						$"{(trace.IsCopper ? "copper" : "cpu")}@{FormatBeam(trace.Cycle)} " +
						$"row={trace.Row} x={trace.X} reg=0x{trace.Offset:X3} value=0x{trace.Value:X4} " +
						$"started={trace.HasStartedLine} bplcon0=0x{trace.Bplcon0:X4} " +
						$"dmacon=0x{trace.Dmacon:X4} planes={trace.PlaneCount}"))}",
				$"TRACE {relativePath}: early boundary presentation {string.Join("; ", machine.Bus.Display.CopperPresentationTransitions
					.Where(trace => trace.Row is 6 or 7)
					.Select(trace =>
						$"{(trace.IsCopper ? "copper" : "cpu")}@{FormatBeam(trace.Cycle)} " +
						$"row={trace.Row} x={trace.X} reg=0x{trace.Offset:X3} value=0x{trace.Value:X4} " +
						$"started={trace.HasStartedLine} bplcon0=0x{trace.Bplcon0:X4} " +
						$"dmacon=0x{trace.Dmacon:X4} planes={trace.PlaneCount}"))}",
				$"TRACE {relativePath}: rendered boundary segments {string.Join("; ", machine.Bus.Display.RenderedCopperTimelineSegments
					.Where(segment => segment.Row is 214 or 234 or 248)
					.Select(segment => $"row={segment.Row} {segment.XStart}-{segment.XStop} palette={segment.PaletteSnapshotIndex} color0=0x{segment.Color0:X3}"))}",
				$"TRACE {relativePath}: archivedTimelineRows {TraceArchivedTimelineRows(machine.Bus.Display, 22, 23, 24, 25, 29, 30, 34, 38, 214, 215, 234, 248)}"
			];
		}
		if (normalizedCasePath.StartsWith("Agnus/DDF/DDF/", StringComparison.OrdinalIgnoreCase) &&
			normalizedCasePath.EndsWith(".adf", StringComparison.OrdinalIgnoreCase))
		{
			var displayWrites = machine.Bus.Display.CopperDisplayWrites
				.Where(write => write.Address is 0x08E or 0x090 or 0x092 or 0x094 or 0x096 or 0x100 or 0x108 or 0x10A)
				.Where(write => archivedFrameStartCycle < 0 ||
					(write.Cycle >= archivedFrameStartCycle && write.Cycle < archivedFrameStopCycle))
				.ToArray();
			var farRightFetches = machine.Bus.BusAccesses
				.Where(access => access.Request.Requester == AmigaBusRequester.Bitplane &&
					access.Request.Kind == AmigaBusAccessKind.Bitplane &&
					access.GrantedCycle >= archivedFrameStartCycle &&
					access.GrantedCycle < archivedFrameStopCycle)
				.Where(access =>
				{
					var frameCycle = access.GrantedCycle - archivedFrameStartCycle;
					var rasterLine = frameCycle / AmigaConstants.A500PalCpuCyclesPerRasterLine;
					var horizontalCycle = frameCycle % AmigaConstants.A500PalCpuCyclesPerRasterLine;
					return rasterLine is >= 142 and <= 145 && horizontalCycle >= 400;
				})
				.ToArray();
			var farRightBus = machine.Bus.BusAccesses
				.Where(access => access.GrantedCycle >= archivedFrameStartCycle &&
					access.GrantedCycle < archivedFrameStopCycle)
				.Where(access =>
				{
					var frameCycle = access.GrantedCycle - archivedFrameStartCycle;
					var rasterLine = frameCycle / AmigaConstants.A500PalCpuCyclesPerRasterLine;
					var horizontalCycle = frameCycle % AmigaConstants.A500PalCpuCyclesPerRasterLine;
					return rasterLine == 144 && horizontalCycle >= 400;
				})
				.ToArray();
			return
			[
				$"TRACE {relativePath}: profile=A500 PAL OCS, display regs {FormatDisplayRegisterSummary(writes, machine.Bus.ChipRam)}",
				$"TRACE {relativePath}: display writes {string.Join("; ", displayWrites.Select(write => $"{FormatBeam(write.Cycle)} reg=0x{write.Address:X3} val=0x{write.Value:X4}"))}",
				$"TRACE {relativePath}: far-right bitplane fetches {FormatBusAccesses(farRightFetches)}",
				$"TRACE {relativePath}: v144 far-right bus {FormatBusAccesses(farRightBus)}",
				$"TRACE {relativePath}: archivedTimelineRows {TraceArchivedTimelineRows(machine.Bus.Display, 22, 23, 24, 28, 34, 40, 117, 118, 119, 133, 134, 135, 139, 140, 145, 146, 151, 152, 157, 158, 159, 160)}"
			];
		}
		if (NormalizeCasePath(relativePath).Equals(
				"Agnus/Registers/VPOS/vprobe2/vprobe2.adf",
				StringComparison.OrdinalIgnoreCase) ||
			NormalizeCasePath(relativePath).Equals(
				"Agnus/Registers/VPOS/vprobe3/vprobe3.adf",
				StringComparison.OrdinalIgnoreCase))
		{
			var sync1ContentionWindow = machine.Bus.BusAccesses
				.Where(access => access.GrantedCycle >= archivedFrameStartCycle &&
					access.GrantedCycle < archivedFrameStopCycle)
				.Where(access =>
				{
					var frameCycle = access.GrantedCycle - archivedFrameStartCycle;
					var line = frameCycle / AmigaConstants.A500PalCpuCyclesPerRasterLine;
					var horizontal = (frameCycle % AmigaConstants.A500PalCpuCyclesPerRasterLine) / 2;
					return line == 32 && horizontal is >= 128 and <= 210;
				})
				.ToArray();
			var allIrq2HandlerPhases = machine.Bus.CpuBusPhases
				.Where(phase =>
					phase.CpuPhase.RequestedCycle >= archivedFrameStartCycle &&
					phase.CpuPhase.RequestedCycle < archivedFrameStopCycle &&
					(phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF) is >= 0x0703D6 and < 0x0704C0)
				.OrderBy(phase => phase.CpuPhase.RequestedCycle)
				.ToArray();
			var irq2HandlerPhases = allIrq2HandlerPhases
				.Take(128)
				.Concat(allIrq2HandlerPhases.Skip(Math.Max(128, allIrq2HandlerPhases.Length - 256)))
				.ToArray();
			var glyphDbraPhases = machine.Bus.CpuBusPhases
				.Where(phase =>
					phase.CpuPhase.RequestedCycle >= archivedFrameStartCycle &&
					phase.CpuPhase.RequestedCycle < archivedFrameStopCycle &&
					(phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF) is >= 0x0701EC and <= 0x07020C)
				.Take(3000)
				.ToArray();
			var referenceHistoryWindow = machine.Bus.CpuBusPhases
				.Where(phase =>
				phase.CpuPhase.RequestedCycle >= archivedFrameStartCycle &&
				phase.CpuPhase.RequestedCycle < archivedFrameStopCycle)
				.Where(phase =>
				{
					var frameCycle = phase.CpuPhase.InstructionStartCycle - archivedFrameStartCycle;
					var line = frameCycle / AmigaConstants.A500PalCpuCyclesPerRasterLine;
					var horizontal = (frameCycle % AmigaConstants.A500PalCpuCyclesPerRasterLine) / 2;
					return (line == 48 && horizontal >= 96) ||
						(line == 49 && horizontal <= 210);
				})
				.GroupBy(phase => (
					phase.CpuPhase.InstructionStartCycle,
					phase.CpuPhase.InstructionProgramCounter))
				.Select(group => group.First().CpuPhase)
				.OrderBy(phase => phase.InstructionStartCycle)
				.TakeLast(64)
				.Select(phase =>
				{
					var frameCycle = phase.InstructionStartCycle - archivedFrameStartCycle;
					return
						$"{(phase.InstructionProgramCounter & 0x00FF_FFFF):X6}" +
						$"@{frameCycle / AmigaConstants.A500PalCpuCyclesPerRasterLine:D3}:" +
						$"{(frameCycle % AmigaConstants.A500PalCpuCyclesPerRasterLine) / 2:D3}";
				})
				.ToArray();
			var rendererEntryHistory = machine.Bus.CpuBusPhases
				.Where(phase =>
					phase.CpuPhase.RequestedCycle >= archivedFrameStartCycle &&
					phase.CpuPhase.RequestedCycle < archivedFrameStopCycle)
				.Where(phase =>
				{
					var frameCycle = phase.CpuPhase.InstructionStartCycle - archivedFrameStartCycle;
					var line = frameCycle / AmigaConstants.A500PalCpuCyclesPerRasterLine;
					return line is >= 46 and <= 49;
				})
				.GroupBy(phase => (
					phase.CpuPhase.InstructionStartCycle,
					phase.CpuPhase.InstructionProgramCounter))
				.Select(group => group.First().CpuPhase)
				.OrderBy(phase => phase.InstructionStartCycle)
				.Select(phase =>
				{
					var frameCycle = phase.InstructionStartCycle - archivedFrameStartCycle;
					return
						$"{(phase.InstructionProgramCounter & 0x00FF_FFFF):X6}" +
						$"@{frameCycle / AmigaConstants.A500PalCpuCyclesPerRasterLine:D3}:" +
						$"{(frameCycle % AmigaConstants.A500PalCpuCyclesPerRasterLine) / 2:D3}";
				})
				.ToArray();
			var outerLoopEntries = machine.Bus.CpuBusPhases
				.Where(phase =>
					phase.CpuPhase.RequestedCycle >= archivedFrameStartCycle &&
					phase.CpuPhase.RequestedCycle < archivedFrameStopCycle &&
					(phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF) is
						0x070470 or 0x070472 or 0x070444)
				.GroupBy(phase => (
					phase.CpuPhase.InstructionStartCycle,
					phase.CpuPhase.InstructionProgramCounter))
				.Select(group => group.First().CpuPhase)
				.OrderBy(phase => phase.InstructionStartCycle)
				.Take(48)
				.Select(phase =>
				{
					var frameCycle = phase.InstructionStartCycle - archivedFrameStartCycle;
					return
						$"{phase.InstructionProgramCounter & 0x00FF_FFFF:X6}" +
						$"@{frameCycle / AmigaConstants.A500PalCpuCyclesPerRasterLine:D3}:" +
						$"{(frameCycle % AmigaConstants.A500PalCpuCyclesPerRasterLine) / 2:D3}" +
						$"/q{phase.InstructionEntryPrefetchCount}" +
						$"/bus{phase.InstructionEntryBusCycle - phase.InstructionStartCycle}";
				})
				.ToArray();
			var rendererWaitExitPhases = machine.Bus.CpuBusPhases
				.Where(phase =>
					phase.CpuPhase.RequestedCycle >= archivedFrameStartCycle &&
					phase.CpuPhase.RequestedCycle < archivedFrameStopCycle &&
					(phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF) is
						0x0705A2 or 0x0705A4 or 0x0705AA or 0x0705AE or
						0x0705B2 or 0x0705B6 or 0x0705BC or 0x07042E)
				.Where(phase =>
				{
					var frameCycle = phase.CpuPhase.InstructionStartCycle - archivedFrameStartCycle;
					return frameCycle / AmigaConstants.A500PalCpuCyclesPerRasterLine == 48;
				})
				.ToArray();
			var rendererWaitExitBus = machine.Bus.BusAccesses
				.Where(access =>
					access.GrantedCycle >= archivedFrameStartCycle &&
					access.GrantedCycle < archivedFrameStopCycle)
				.Where(access =>
				{
					var frameCycle = access.GrantedCycle - archivedFrameStartCycle;
					var line = frameCycle / AmigaConstants.A500PalCpuCyclesPerRasterLine;
					var horizontal = (frameCycle % AmigaConstants.A500PalCpuCyclesPerRasterLine) / 2;
					return line == 48 && horizontal is >= 58 and <= 96;
				})
				.ToArray();
			var rendererWaitExitEntries = rendererWaitExitPhases
				.GroupBy(phase => (
					phase.CpuPhase.InstructionStartCycle,
					phase.CpuPhase.InstructionProgramCounter))
				.Select(group => group.First().CpuPhase)
				.Select(phase =>
					$"{phase.InstructionProgramCounter & 0x00FF_FFFF:X6}" +
					$"/q{phase.InstructionEntryPrefetchCount}" +
					$"/bus={phase.InstructionEntryBusCycle - phase.InstructionStartCycle}" +
					$"/r0={phase.InstructionEntryReadyCycle0 - phase.InstructionStartCycle}" +
					$"/r1={phase.InstructionEntryReadyCycle1 - phase.InstructionStartCycle}")
				.ToArray();
			var outerLoopPhases = machine.Bus.CpuBusPhases
				.Where(phase =>
					phase.CpuPhase.RequestedCycle >= archivedFrameStartCycle &&
					phase.CpuPhase.RequestedCycle < archivedFrameStopCycle &&
					(phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF) is
						0x070444 or 0x070468 or 0x07046C or 0x070470 or 0x070472 or
						0x070212 or 0x070220 or 0x070234 or 0x07023E or 0x070248)
				.ToArray();
			var glyphBranchPhases = machine.Bus.CpuBusPhases
				.Where(phase =>
					phase.CpuPhase.RequestedCycle >= archivedFrameStartCycle &&
					phase.CpuPhase.RequestedCycle < archivedFrameStopCycle &&
					(phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF) is
						0x070218 or 0x07021C or 0x07022E)
				.Take(512)
				.ToArray();
			var glyphBranchEntries = glyphBranchPhases
				.GroupBy(phase => (
					phase.CpuPhase.InstructionStartCycle,
					phase.CpuPhase.InstructionProgramCounter))
				.Select(group =>
				{
					var first = group.First().CpuPhase;
					return
						$"pc={first.InstructionProgramCounter & 0x00FF_FFFF:X6}/" +
						$"start={first.InstructionStartCycle}/" +
						$"entryBus={first.InstructionEntryBusCycle - first.InstructionStartCycle}/" +
						$"q={first.InstructionEntryPrefetchCount}/" +
						$"r0={first.InstructionEntryReadyCycle0 - first.InstructionStartCycle}/" +
						$"r1={first.InstructionEntryReadyCycle1 - first.InstructionStartCycle}";
				})
				.Take(128)
				.ToArray();
			var oneWordBranchEntryCycles = glyphBranchPhases
				.GroupBy(phase => (
					phase.CpuPhase.InstructionStartCycle,
					phase.CpuPhase.InstructionProgramCounter))
				.Select(group => group.First().CpuPhase)
				.Where(phase => phase.InstructionEntryPrefetchCount == 1)
				.Select(phase => phase.InstructionStartCycle)
				.Take(8)
				.ToArray();
			var oneWordBranchHandoffs = oneWordBranchEntryCycles
				.SelectMany(start => machine.Bus.CpuBusPhases
					.Where(phase =>
						phase.CpuPhase.RequestedCycle >= start - 32 &&
						phase.CpuPhase.RequestedCycle < start + 32))
				.OrderBy(phase => phase.CpuPhase.RequestedCycle)
				.ToArray();
			var matchingReferenceGlyphCycles = machine.Bus.CpuBusPhases
				.Where(phase =>
					(phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF) == 0x070216 &&
					(phase.CpuPhase.Address & 0x00FF_FFFF) == 0x0705C9)
				.Select(phase => phase.CpuPhase.InstructionStartCycle)
				.Take(4)
				.ToArray();
			var matchingReferenceGlyphHandoffs = matchingReferenceGlyphCycles
				.SelectMany(start => machine.Bus.CpuBusPhases
					.Where(phase =>
						phase.CpuPhase.RequestedCycle >= start - 32 &&
						phase.CpuPhase.RequestedCycle < start + 32))
				.OrderBy(phase => phase.CpuPhase.RequestedCycle)
				.ToArray();
			var glyphContentionBus = machine.Bus.BusAccesses
				.Where(access =>
				access.GrantedCycle >= archivedFrameStartCycle &&
				access.GrantedCycle < archivedFrameStopCycle)
				.Where(access =>
				(access.GrantedCycle - archivedFrameStartCycle) /
					AmigaConstants.A500PalCpuCyclesPerRasterLine is >= 49 and <= 50 or >= 83 and <= 85)
				.ToArray();
			var level1CopperBus = machine.Bus.BusAccesses
				.Where(access =>
					access.GrantedCycle >= archivedFrameStartCycle &&
					access.GrantedCycle < archivedFrameStopCycle)
				.Where(access =>
				{
					var frameCycle = access.GrantedCycle - archivedFrameStartCycle;
					var line = frameCycle / AmigaConstants.A500PalCpuCyclesPerRasterLine;
					var horizontal = (frameCycle % AmigaConstants.A500PalCpuCyclesPerRasterLine) / 2;
					return line == 255 && horizontal is >= 120 and <= 145;
				})
				.ToArray();
			var steadyColorWrites = archivedFrameColorWrites
				.Where(write => write.Value <= 0x000F)
				.OrderBy(write => write.Cycle)
				.ToArray();
			var firstStripeStartIndex = Array.FindIndex(steadyColorWrites, write => write.Value == 0x000F);
			var firstStripeRefreshOverlaps = firstStripeStartIndex >= 0 &&
				firstStripeStartIndex + 1 < steadyColorWrites.Length
				? machine.Bus.BusAccesses
					.Where(access =>
						access.Request.Requester == AmigaBusRequester.Cpu &&
						access.GrantedCycle >= steadyColorWrites[firstStripeStartIndex].Cycle &&
						access.GrantedCycle < steadyColorWrites[firstStripeStartIndex + 1].Cycle &&
						AgnusHrmOcsSlotTable.IsMandatoryRefreshSlot(access.GrantedCycle))
					.ToArray()
				: [];
			var firstStripeCpuPhases = firstStripeStartIndex >= 0 &&
				firstStripeStartIndex + 1 < steadyColorWrites.Length
				? machine.Bus.CpuBusPhases
					.Where(phase =>
						phase.CpuPhase.RequestedCycle >= steadyColorWrites[firstStripeStartIndex].Cycle &&
						phase.CpuPhase.RequestedCycle < steadyColorWrites[firstStripeStartIndex + 1].Cycle)
					.ToArray()
				: [];
			var firstStripeDeniedRefreshSlots = firstStripeCpuPhases
				.Where(phase => phase.BusAccess is { WaitCycles: > 0 })
				.SelectMany(phase =>
				{
					var access = phase.BusAccess!.Value;
					return Enumerable.Range(
							0,
							checked((int)((access.GrantedCycle - access.RequestedCycle) / 2)))
						.Select(index => access.RequestedCycle + (index * 2))
						.Where(AgnusHrmOcsSlotTable.IsMandatoryRefreshSlot)
						.Select(cycle => new
						{
							Cycle = cycle,
							Pc = phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF,
							Address = phase.CpuPhase.Address & 0x00FF_FFFF,
							Requested = access.RequestedCycle,
							Granted = access.GrantedCycle
						});
				})
				.ToArray();
			var firstStripeDelayedPhases = firstStripeCpuPhases
				.Where(phase => phase.BusAccess is { WaitCycles: > 0 })
				.ToArray();
			var steadyIterationStart = steadyColorWrites.Length >= 8
				? steadyColorWrites[^8].Cycle
				: -1;
			var steadyIterationStop = steadyColorWrites.Length >= 7
				? steadyColorWrites[^7].Cycle
				: -1;
			var steadyIterationCpuPhases = steadyIterationStart >= 0
				? machine.Bus.CpuBusPhases
					.Where(phase =>
						phase.CpuPhase.RequestedCycle >= steadyIterationStart &&
						phase.CpuPhase.RequestedCycle < steadyIterationStop)
					.ToArray()
				: [];
			var steadyIterationBus = steadyIterationStart >= 0
				? machine.Bus.BusAccesses
					.Where(access =>
						access.GrantedCycle >= steadyIterationStart &&
						access.GrantedCycle < steadyIterationStop)
					.ToArray()
				: [];
			var steadyInstructionStarts = steadyIterationCpuPhases
				.GroupBy(phase => (
					phase.CpuPhase.InstructionStartCycle,
					phase.CpuPhase.InstructionProgramCounter))
				.Select(group => group.First())
				.OrderBy(phase => phase.CpuPhase.InstructionStartCycle)
				.ToArray();
			var steadyInstructionLedger = steadyInstructionStarts
				.Take(Math.Max(0, steadyInstructionStarts.Length - 1))
				.Select((phase, index) => new
				{
					Pc = phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF,
					Cycles = steadyInstructionStarts[index + 1].CpuPhase.InstructionStartCycle -
						phase.CpuPhase.InstructionStartCycle
				})
				.GroupBy(entry => entry.Pc)
				.Select(group => new
				{
					Pc = group.Key,
					Count = group.Count(),
					Cycles = group.Sum(entry => entry.Cycles)
				})
				.OrderByDescending(entry => entry.Cycles)
				.ToArray();
			var steadyBitplaneCycles = steadyIterationBus
				.Where(access => access.Request.Requester == AmigaBusRequester.Bitplane)
				.Select(access => access.GrantedCycle)
				.ToHashSet();
			var steadyCpuCycles = steadyIterationCpuPhases
				.Where(phase =>
					phase.BusAccess is { } access &&
					access.Request.Target is AmigaBusAccessTarget.ChipRam or
						AmigaBusAccessTarget.CustomRegisters)
				.Select(phase => phase.BusAccess!.Value.GrantedCycle)
				.ToHashSet();
			var steadyCpuBitplaneOverlapCycles = steadyCpuCycles
				.Where(steadyBitplaneCycles.Contains)
				.Order()
				.ToArray();
			var steadyDeniedRefreshSlots = 0;
			var steadyDeniedBitplaneSlots = 0;
			var steadyDeniedOtherSlots = 0;
			var steadyWaitsByInstruction = new Dictionary<uint, (int Phases, long WaitCycles)>();
			var steadyDbraWaitsByAddress = new Dictionary<uint, (int Phases, long WaitCycles)>();
			foreach (var phase in steadyIterationCpuPhases)
			{
				if (phase.BusAccess is not { } access)
				{
					continue;
				}

				if (access.WaitCycles > 0)
				{
					var pc = phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF;
					steadyWaitsByInstruction.TryGetValue(pc, out var waits);
					steadyWaitsByInstruction[pc] = (waits.Phases + 1, waits.WaitCycles + access.WaitCycles);
					if (pc == 0x070208)
					{
						var address = phase.CpuPhase.Address & 0x00FF_FFFF;
						steadyDbraWaitsByAddress.TryGetValue(address, out var addressWaits);
						steadyDbraWaitsByAddress[address] =
							(addressWaits.Phases + 1, addressWaits.WaitCycles + access.WaitCycles);
					}
				}

				for (var cycle = access.RequestedCycle; cycle < access.GrantedCycle; cycle += 2)
				{
					if (AgnusHrmOcsSlotTable.IsMandatoryRefreshSlot(cycle))
					{
						steadyDeniedRefreshSlots++;
					}
					else if (steadyBitplaneCycles.Contains(cycle))
					{
						steadyDeniedBitplaneSlots++;
					}
					else
					{
						steadyDeniedOtherSlots++;
					}
				}
			}
			var outputIterationWaits = steadyColorWrites
				.Zip(steadyColorWrites.Skip(1), (start, stop) =>
				{
					var phases = machine.Bus.CpuBusPhases
						.Where(phase =>
							phase.CpuPhase.RequestedCycle >= start.Cycle &&
							phase.CpuPhase.RequestedCycle < stop.Cycle)
						.ToArray();
					return
						$"{start.Value:X3}->{stop.Value:X3}:" +
						$"dt={stop.Cycle - start.Cycle}/" +
						$"wait={phases.Sum(phase => phase.BusAccess?.WaitCycles ?? 0)}/" +
						$"denied={phases.Count(phase => phase.BusAccess?.WaitCycles > 0)}";
				})
				.ToArray();
			var syncStart = archivedFrameColorWrites.FirstOrDefault(write => write.Value == 0x0F0F);
			var syncStop = archivedFrameColorWrites.FirstOrDefault(write =>
				write.Value == 0x0606 && write.Cycle > syncStart.Cycle);
			var syncVhposrReads = machine.Bus.CustomRegisterReadTrace
				.Where(read =>
					read.Address == 0x006 &&
					read.SampleCycle >= syncStart.Cycle &&
					read.SampleCycle <= syncStop.Cycle)
				.ToArray();
			var syncSecondStop = archivedFrameColorWrites.FirstOrDefault(write =>
				write.Value == 0x0A0A && write.Cycle > syncStop.Cycle);
			var syncSecondVhposrReads = machine.Bus.CustomRegisterReadTrace
				.Where(read =>
					read.Address == 0x006 &&
					read.SampleCycle >= syncStop.Cycle &&
					read.SampleCycle <= syncSecondStop.Cycle)
				.ToArray();
			return
			[
				$"TRACE {relativePath}: interrupt dispatches first={FormatInterruptDispatches(machine.InterruptDispatchTrace.Take(12))}; " +
					$"last={FormatInterruptDispatches(machine.InterruptDispatchTrace.Skip(Math.Max(0, machine.InterruptDispatchTrace.Count - 12)))}",
				$"TRACE {relativePath}: cpuCycles={machine.Cpu.State.Cycles}, " +
				$"pc=0x{machine.Cpu.State.ProgramCounter & 0x00FF_FFFF:X6}, " +
				$"lastPc=0x{machine.Cpu.State.LastInstructionProgramCounter & 0x00FF_FFFF:X6}, " +
				$"sr=0x{machine.Cpu.State.StatusRegister:X4}, code={FormatCodeWords(machine, machine.Cpu.State.LastInstructionProgramCounter, 8)}, " +
				$"intena=0x{machine.Bus.Paula.Intena:X4}, intreq=0x{machine.Bus.Paula.Intreq:X4}, " +
				$"dmacon=0x{machine.Bus.Paula.Dmacon:X4}",
				$"TRACE {relativePath}: timing loop code {FormatCodeWords(machine, 0x070420, 80)}",
				$"TRACE {relativePath}: timing loop helpers {FormatCodeWords(machine, 0x070200, 80)}",
				$"TRACE {relativePath}: character helper code {FormatCodeWords(machine, 0x070180, 72)}",
				$"TRACE {relativePath}: renderer wait exit code {FormatCodeWords(machine, 0x0704D0, 124)}",
				$"TRACE {relativePath}: glyph DBRA phases {FormatCpuBusPhases(glyphDbraPhases)}",
				$"TRACE {relativePath}: reference history window {string.Join(' ', referenceHistoryWindow)}",
				$"TRACE {relativePath}: renderer entry history {string.Join(' ', rendererEntryHistory)}",
				$"TRACE {relativePath}: outer loop entries {string.Join(' ', outerLoopEntries)}",
				$"TRACE {relativePath}: first stripe CPU/refresh overlaps {FormatBusAccesses(firstStripeRefreshOverlaps)}",
				$"TRACE {relativePath}: first stripe denied refresh " +
					$"count={firstStripeDeniedRefreshSlots.Length} " +
					$"{string.Join(';', firstStripeDeniedRefreshSlots.Select(slot =>
						$"{FormatBeam(slot.Cycle)}:" +
						$"pc={slot.Pc:X6}/addr={slot.Address:X6}/" +
						$"req={slot.Requested}/grant={slot.Granted}"))}",
				$"TRACE {relativePath}: first stripe delayed phases {FormatCpuBusPhases(firstStripeDelayedPhases)}",
				$"TRACE {relativePath}: renderer wait exit phases {FormatCpuBusPhases(rendererWaitExitPhases)}",
				$"TRACE {relativePath}: renderer wait exit entries {string.Join("; ", rendererWaitExitEntries)}",
				$"TRACE {relativePath}: renderer wait exit bus {FormatBusAccesses(rendererWaitExitBus)}",
				$"TRACE {relativePath}: glyph Bcc phases {FormatCpuBusPhases(glyphBranchPhases)}",
				$"TRACE {relativePath}: glyph Bcc entries {string.Join("; ", glyphBranchEntries)}",
				$"TRACE {relativePath}: one-word Bcc handoffs {FormatCpuBusPhases(oneWordBranchHandoffs)}",
				$"TRACE {relativePath}: matching glyph handoffs {FormatCpuBusPhases(matchingReferenceGlyphHandoffs)}",
				$"TRACE {relativePath}: outer loop boundary phases {FormatCpuBusPhases(outerLoopPhases)}",
				$"TRACE {relativePath}: glyph contention bus {FormatBusAccesses(glyphContentionBus)}",
				$"TRACE {relativePath}: level1 branch window {FormatCpuBusPhases(GetCpuPhasesInBeamWindow(machine.Bus.CpuBusPhases, archivedFrameStartCycle, 255, 120, 256, 0))}",
				$"TRACE {relativePath}: level1 copper bus {FormatBusAccesses(level1CopperBus)}",
				$"TRACE {relativePath}: presentation transitions {string.Join("; ", machine.Bus.Display.CopperPresentationTransitions
					.Where(trace => trace.Offset is >= 0x180 and < 0x1C0)
					.Select(trace =>
						$"{(trace.IsCopper ? "copper" : "cpu")}@{FormatBeam(trace.Cycle)} " +
						$"row={trace.Row} x={trace.X} value=0x{trace.Value:X3} " +
						$"started={trace.HasStartedLine} bplcon0=0x{trace.Bplcon0:X4} " +
						$"dmacon=0x{trace.Dmacon:X4} planes={trace.PlaneCount}"))}",
				$"TRACE {relativePath}: steady iteration {steadyIterationStart}->{steadyIterationStop} " +
					$"delta={steadyIterationStop - steadyIterationStart} phases={FormatCpuBusPhases(steadyIterationCpuPhases)}",
				$"TRACE {relativePath}: steady iteration requester counts " +
					$"{string.Join(',', steadyIterationBus
						.GroupBy(access => access.Request.Requester)
						.OrderBy(group => group.Key)
						.Select(group => $"{group.Key}={group.Count()}"))}",
				$"TRACE {relativePath}: steady instruction ledger " +
					$"{string.Join(',', steadyInstructionLedger.Select(entry =>
						$"{entry.Pc:X6}={entry.Count}/{entry.Cycles}"))}",
				$"TRACE {relativePath}: steady denied owners " +
					$"refresh={steadyDeniedRefreshSlots},bitplane={steadyDeniedBitplaneSlots},other={steadyDeniedOtherSlots}, " +
					$"cpuBitplaneOverlap={steadyCpuBitplaneOverlapCycles.Length}" +
					$"{(steadyCpuBitplaneOverlapCycles.Length == 0 ? string.Empty : $"@{string.Join(',', steadyCpuBitplaneOverlapCycles.Take(32).Select(FormatBeam))}")}",
				$"TRACE {relativePath}: steady waits by instruction " +
					$"{string.Join(',', steadyWaitsByInstruction
						.OrderByDescending(entry => entry.Value.WaitCycles)
						.Select(entry => $"{entry.Key:X6}={entry.Value.Phases}/{entry.Value.WaitCycles}"))}",
				$"TRACE {relativePath}: steady DBRA waits by address " +
					$"{string.Join(',', steadyDbraWaitsByAddress
						.OrderByDescending(entry => entry.Value.WaitCycles)
						.Select(entry => $"{entry.Key:X6}={entry.Value.Phases}/{entry.Value.WaitCycles}"))}",
				$"TRACE {relativePath}: steady iteration bus {FormatBusAccesses(steadyIterationBus)}",
				$"TRACE {relativePath}: output iteration waits {string.Join(';', outputIterationWaits)}",
				$"TRACE {relativePath}: rendered color-band timeline " +
					$"{string.Join("; ", machine.Bus.Display.RenderedCopperTimelineSegments
						.Where(segment => segment.Row is >= 22 and <= 172 && (segment.Row - 22) % 10 == 0)
						.Select(segment => $"row={segment.Row} {segment.XStart}-{segment.XStop} color0=0x{segment.Color0:X3}"))}",
				$"TRACE {relativePath}: timeline active={display.LastActiveTimelineFrameCount}, archived={display.LastArchivedTimelineFrameCount}, " +
				$"fallback={display.LastTimelineFallbackCount}, missingBitplaneFallback={display.LastTimelineMissingBitplaneFallbackCount}, " +
				$"fastRows={display.LastTimelineFastPathRowCount}, fastMiss={display.LastTimelineFastPathMissCount}, " +
				$"segments={display.LastTimelineSegmentCount}, liveWrites={liveFrameWriteCount}, pending={pendingWriteCount}, " +
				$"pendingIndex={pendingWriteIndex}, overflow={liveFrameWriteOverflowed}, " +
				$"archivedTimeline={FormatBeam(archivedFrameStartCycle)}..{FormatBeam(archivedFrameStopCycle)}",
				$"TRACE {relativePath}: display regs {FormatDisplayRegisterSummary(writes, machine.Bus.ChipRam)}",
				$"TRACE {relativePath}: display render stats bitplanePixels={display.LastBitplaneNonZeroPixels}, " +
				$"bitplaneBounds={FormatBounds(display.LastBitplaneMinX, display.LastBitplaneMinY, display.LastBitplaneMaxX, display.LastBitplaneMaxY)}, " +
				$"normalPixels={display.LastNormalPlayfieldNonZeroPixels}, " +
				$"normalBounds={FormatBounds(display.LastNormalPlayfieldMinX, display.LastNormalPlayfieldMinY, display.LastNormalPlayfieldMaxX, display.LastNormalPlayfieldMaxY)}, " +
				$"bplDma={display.LastBitplaneDmaFetches}, colors={FormatIndexedCounts(display.BitplaneColorCounts)}",
				$"TRACE {relativePath}: color00 frame count={archivedFrameColorWrites.Length}, " +
					$"first={FormatWrites(archivedFrameColorWrites.Take(24))}, " +
					$"last={FormatWrites(archivedFrameColorWrites.Skip(Math.Max(0, archivedFrameColorWrites.Length - 24)))}",
				$"TRACE {relativePath}: color00 cpu phases frameFirst={FormatCpuBusPhases(archivedFrameColor00CpuPhases.Take(48))}, " +
					$"frameLast={FormatCpuBusPhases(archivedFrameColor00CpuPhases.Skip(Math.Max(0, archivedFrameColor00CpuPhases.Length - 48)))}",
				$"TRACE {relativePath}: intreq frame={FormatWrites(archivedFrameIntreqWrites)}",
				$"TRACE {relativePath}: irq2 handler phases={FormatCpuBusPhases(irq2HandlerPhases)}",
				$"TRACE {relativePath}: irq2 sync transition phases={FormatCpuBusPhases(GetCpuPhasesInBeamWindow(machine.Bus.CpuBusPhases, archivedFrameStartCycle, 31, 96, 33, 32))}",
				$"TRACE {relativePath}: sync1 VHPOSR values={FormatCustomRegisterReads(syncVhposrReads)}",
				$"TRACE {relativePath}: sync2 VHPOSR values={FormatCustomRegisterReads(syncSecondVhposrReads)}",
				$"TRACE {relativePath}: sync1 first-loop physical ledger " +
					$"{FormatCpuBusPhases(GetCpuPhasesInBeamWindow(machine.Bus.CpuBusPhases, archivedFrameStartCycle, 32, 44, 32, 128))}",
				$"TRACE {relativePath}: sync1 first-loop exit ledger " +
					$"{FormatCpuBusPhases(GetCpuPhasesInBeamWindow(machine.Bus.CpuBusPhases, archivedFrameStartCycle, 32, 208, 33, 24))}",
				$"TRACE {relativePath}: sync2 line-wrap ledger " +
					$"{FormatCpuBusPhases(GetCpuPhasesInBeamWindow(machine.Bus.CpuBusPhases, archivedFrameStartCycle, 33, 208, 34, 32))}",
				$"TRACE {relativePath}: sync1 contention={FormatBusAccesses(sync1ContentionWindow)}",
				$"TRACE {relativePath}: vprobe value candidates {FormatVprobe2ValueCandidates(machine.Bus.ChipRam)}",
				$"TRACE {relativePath}: vprobe lea values candidates {FormatProbe10LeaA2Targets(machine.Bus.ChipRam)}",
				$"TRACE {relativePath}: vprobe measured write bursts {FormatProbe10MeasuredWriteBursts(machine.Bus.CpuBusPhases, machine.Bus.ChipRam)}",
				$"TRACE {relativePath}: vprobe value write phases {FormatProbe10ValueWritePhases(machine.Bus.CpuBusPhases, machine.Bus.ChipRam)}",
				$"TRACE {relativePath}: vprobe watched value writes {FormatProbe10WatchedValueWrites(machine.Bus.CpuChipRamWriteTrace, machine.Bus.ChipRam)}",
				$"TRACE {relativePath}: vprobe watched vposr reads {FormatProbe10WatchedVhposrReads(machine.Bus.CustomRegisterReadTrace, machine.Bus.CpuChipRamWriteTrace, machine.Bus.ChipRam, 0x004)}",
				$"TRACE {relativePath}: vposr read bursts {FormatVhposrReadBursts(customRegisterReads, 0x004)}",
				$"TRACE {relativePath}: vposr reads count={vposrReads.Length}, " +
				$"first={FormatBusAccesses(vposrReads.Take(16))}, " +
				$"last={FormatBusAccesses(vposrReads.Skip(Math.Max(0, vposrReads.Length - 16)))}",
				$"TRACE {relativePath}: vposr cpu phases first={FormatCpuBusPhases(vposrCpuPhases.Take(16))}, " +
				$"last={FormatCpuBusPhases(vposrCpuPhases.Skip(Math.Max(0, vposrCpuPhases.Length - 16)))}",
				$"TRACE {relativePath}: level1 pending gap phases {FormatCpuBusPhases(GetCpuPhasesInBeamWindow(machine.Bus.CpuBusPhases, archivedFrameStartCycle, 247, 160, 258, 24).Take(96))}",
				$"TRACE {relativePath}: beam cpu phases frameFirst={FormatCpuBusPhases(archivedFrameBeamCpuPhases.Take(64))}, " +
					$"frameLast={FormatCpuBusPhases(archivedFrameBeamCpuPhases.Skip(Math.Max(0, archivedFrameBeamCpuPhases.Length - 64)))}",
				$"TRACE {relativePath}: beam reads by pc {FormatBeamReadsByProgramCounter(beamCpuPhases, customRegisterReads)}",
				$"TRACE {relativePath}: archivedTimelineRows {TraceArchivedTimelineRows(machine.Bus.Display, 0, 6, 7, 8, 9, 10, 52, 56, 62, 70, 182, 194, 209, 221, 230, 231, 232)}"
			];
		}
		if (NormalizeCasePath(relativePath).Equals(
			"Agnus/Registers/VPOS/probe10/probe10.adf",
			StringComparison.OrdinalIgnoreCase))
		{
			var firstSyncWrite = archivedFrameColorWrites.FirstOrDefault(write => write.Value == 0x0F0F);
			var secondSyncWrite = archivedFrameColorWrites.FirstOrDefault(write =>
				write.Value == 0x0606 && write.Cycle > firstSyncWrite.Cycle);
			var sync1Reads = firstSyncWrite.Cycle > 0 && secondSyncWrite.Cycle > firstSyncWrite.Cycle
				? diagnosticRegisterReads.Where(read =>
					read.Address == 0x006 &&
					read.SampleCycle >= firstSyncWrite.Cycle &&
					read.SampleCycle <= secondSyncWrite.Cycle).ToArray()
				: [];
			return
			[
				$"TRACE {relativePath}: interrupt dispatches first={FormatInterruptDispatches(machine.InterruptDispatchTrace.Take(12))}; " +
					$"last={FormatInterruptDispatches(machine.InterruptDispatchTrace.Skip(Math.Max(0, machine.InterruptDispatchTrace.Count - 12)))}",
				$"TRACE {relativePath}: cpuCycles={machine.Cpu.State.Cycles}, " +
				$"pc=0x{machine.Cpu.State.ProgramCounter & 0x00FF_FFFF:X6}, " +
				$"lastPc=0x{machine.Cpu.State.LastInstructionProgramCounter & 0x00FF_FFFF:X6}, " +
				$"sr=0x{machine.Cpu.State.StatusRegister:X4}, code={FormatCodeWords(machine, machine.Cpu.State.LastInstructionProgramCounter, 8)}, " +
				$"intena=0x{machine.Bus.Paula.Intena:X4}, intreq=0x{machine.Bus.Paula.Intreq:X4}, " +
				$"dmacon=0x{machine.Bus.Paula.Dmacon:X4}",
				$"TRACE {relativePath}: intreq first={FormatWrites(intreqWrites.Take(12))}",
				$"TRACE {relativePath}: intreq last={FormatWrites(intreqWrites.Skip(Math.Max(0, intreqWrites.Length - 12)))}",
				$"TRACE {relativePath}: timeline active={display.LastActiveTimelineFrameCount}, archived={display.LastArchivedTimelineFrameCount}, " +
				$"fallback={display.LastTimelineFallbackCount}, missingBitplaneFallback={display.LastTimelineMissingBitplaneFallbackCount}, " +
				$"fastRows={display.LastTimelineFastPathRowCount}, fastMiss={display.LastTimelineFastPathMissCount}, " +
				$"segments={display.LastTimelineSegmentCount}, sprites={display.LastTimelineSpriteCommandCount}, " +
				$"planarCache={display.LastPlanarChunkCacheHits}/{display.LastPlanarChunkCacheMisses}, " +
				$"liveWrites={liveFrameWriteCount}, pending={pendingWriteCount}, pendingIndex={pendingWriteIndex}, overflow={liveFrameWriteOverflowed}, " +
				$"archivedTimeline={FormatBeam(archivedFrameStartCycle)}..{FormatBeam(archivedFrameStopCycle)}",
				$"TRACE {relativePath}: display regs {FormatDisplayRegisterSummary(writes, machine.Bus.ChipRam)}",
				$"TRACE {relativePath}: display render stats bitplanePixels={display.LastBitplaneNonZeroPixels}, " +
				$"bitplaneBounds={FormatBounds(display.LastBitplaneMinX, display.LastBitplaneMinY, display.LastBitplaneMaxX, display.LastBitplaneMaxY)}, " +
				$"normalPixels={display.LastNormalPlayfieldNonZeroPixels}, " +
				$"normalBounds={FormatBounds(display.LastNormalPlayfieldMinX, display.LastNormalPlayfieldMinY, display.LastNormalPlayfieldMaxX, display.LastNormalPlayfieldMaxY)}, " +
				$"bplDma={display.LastBitplaneDmaFetches}, colors={FormatIndexedCounts(display.BitplaneColorCounts)}",
				$"TRACE {relativePath}: color00 frame count={archivedFrameColorWrites.Length}, " +
				$"first={FormatWrites(archivedFrameColorWrites.Take(24))}, " +
				$"last={FormatWrites(archivedFrameColorWrites.Skip(Math.Max(0, archivedFrameColorWrites.Length - 24)))}",
				$"TRACE {relativePath}: color00 cpu phases frameFirst={FormatCpuBusPhases(archivedFrameColor00CpuPhases.Take(32))}, " +
				$"frameLast={FormatCpuBusPhases(archivedFrameColor00CpuPhases.Skip(Math.Max(0, archivedFrameColor00CpuPhases.Length - 32)))}",
				$"TRACE {relativePath}: archivedTimelineRows {TraceArchivedTimelineRows(machine.Bus.Display, 6, 7, 24, 25, 38, 39, 40, 126, 127, 128, 214, 215)}",
				$"TRACE {relativePath}: probe10 value candidates {FormatProbe10ValueCandidates(machine.Bus.ChipRam)}",
				$"TRACE {relativePath}: probe10 lea values candidates {FormatProbe10LeaA2Targets(machine.Bus.ChipRam)}",
				$"TRACE {relativePath}: probe10 measured write bursts {FormatProbe10MeasuredWriteBursts(machine.Bus.CpuBusPhases, machine.Bus.ChipRam)}",
				$"TRACE {relativePath}: probe10 value write phases {FormatProbe10ValueWritePhases(machine.Bus.CpuBusPhases, machine.Bus.ChipRam)}",
				$"TRACE {relativePath}: probe10 watched value writes {FormatProbe10WatchedValueWrites(machine.Bus.CpuChipRamWriteTrace, machine.Bus.ChipRam)}",
				$"TRACE {relativePath}: probe10 watched vhposr reads {FormatProbe10WatchedVhposrReads(machine.Bus.CustomRegisterReadTrace, machine.Bus.CpuChipRamWriteTrace, machine.Bus.ChipRam)}",
				$"TRACE {relativePath}: vhposr read bursts {FormatVhposrReadBursts(diagnosticRegisterReads)}",
				$"TRACE {relativePath}: sync1 vhposr reads count={sync1Reads.Length}, " +
					$"first={FormatCustomRegisterReads(sync1Reads.Take(32))}, " +
					$"last={FormatCustomRegisterReads(sync1Reads.Skip(Math.Max(0, sync1Reads.Length - 32)))}",
				$"TRACE {relativePath}: beam reads by pc {FormatBeamReadsByProgramCounter(beamCpuPhases, customRegisterReads)}",
				$"TRACE {relativePath}: beam cpu phases frameLast={FormatCpuBusPhases(archivedFrameBeamCpuPhases.Skip(Math.Max(0, archivedFrameBeamCpuPhases.Length - 32)))}"
			];
		}
		var syncLoopCpuPhases = machine.Bus.CpuBusPhases
			.Where(phase =>
			{
				var pc = phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF;
				return pc >= 0x0702C0 && pc <= 0x0703D4;
			})
			.ToArray();
		var archivedFrameSyncLoopCpuDataPhases = archivedFrameStartCycle >= 0
			? syncLoopCpuPhases
				.Where(phase => phase.CpuPhase.RequestedCycle >= archivedFrameStartCycle &&
					phase.CpuPhase.RequestedCycle < archivedFrameStartCycle + AmigaConstants.A500PalCpuCyclesPerFrame &&
					phase.CpuPhase.AccessKind is M68kBusAccessKind.CpuDataRead or M68kBusAccessKind.CpuDataWrite)
				.ToArray()
			: [];
		var archivedFrameSyncLoopCpuPhases = archivedFrameStartCycle >= 0
			? syncLoopCpuPhases
				.Where(phase => phase.CpuPhase.RequestedCycle >= archivedFrameStartCycle &&
					phase.CpuPhase.RequestedCycle < archivedFrameStartCycle + AmigaConstants.A500PalCpuCyclesPerFrame)
				.ToArray()
			: [];
		var archivedInitialWaitLinePhases = archivedFrameSyncLoopCpuPhases
			.Where(phase =>
			{
				var frameCycle = phase.CpuPhase.RequestedCycle % AmigaConstants.A500PalCpuCyclesPerFrame;
				var line = frameCycle / AmigaConstants.A500PalCpuCyclesPerRasterLine;
				var lineCycle = frameCycle % AmigaConstants.A500PalCpuCyclesPerRasterLine;
				return line == 235 &&
					lineCycle < 48 * AmigaConstants.A500PalCpuCyclesPerColorClock;
			})
			.ToArray();
		var archivedStripeHandoffPhases = machine.Bus.CpuBusPhases
			.Where(phase =>
			{
				var frameCycle = phase.CpuPhase.RequestedCycle % AmigaConstants.A500PalCpuCyclesPerFrame;
				var line = frameCycle / AmigaConstants.A500PalCpuCyclesPerRasterLine;
				var lineCycle = frameCycle % AmigaConstants.A500PalCpuCyclesPerRasterLine;
				var hpos = lineCycle / AmigaConstants.A500PalCpuCyclesPerColorClock;
				return (line == 234 && hpos >= 208) || (line == 235 && hpos < 18);
			})
			.ToArray();
		var archivedFirstStripeWrapPhases = machine.Bus.CpuBusPhases
			.Where(phase =>
			{
				var pc = phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF;
				if (pc < 0x0702A8 || pc > 0x0702BA)
				{
					return false;
				}

				var frameCycle = phase.CpuPhase.RequestedCycle % AmigaConstants.A500PalCpuCyclesPerFrame;
				var line = frameCycle / AmigaConstants.A500PalCpuCyclesPerRasterLine;
				var lineCycle = frameCycle % AmigaConstants.A500PalCpuCyclesPerRasterLine;
				var hpos = lineCycle / AmigaConstants.A500PalCpuCyclesPerColorClock;
				return (line == 212 && hpos >= 180) || (line == 213 && hpos < 48);
			})
			.ToArray();
		var archivedFirstWaitWrapPhases = archivedFrameSyncLoopCpuPhases
			.Where(phase =>
			{
				var frameCycle = phase.CpuPhase.RequestedCycle % AmigaConstants.A500PalCpuCyclesPerFrame;
				var line = frameCycle / AmigaConstants.A500PalCpuCyclesPerRasterLine;
				var lineCycle = frameCycle % AmigaConstants.A500PalCpuCyclesPerRasterLine;
				var hpos = lineCycle / AmigaConstants.A500PalCpuCyclesPerColorClock;
				return (line == 235 && hpos >= 208) || (line == 236 && hpos < 18);
			})
			.ToArray();
		var archivedSyncLoopVerticalBoundaryPhases = archivedFrameSyncLoopCpuDataPhases
			.Where(phase =>
			{
				var line = GetPalBeamLine(phase.CpuPhase.RequestedCycle);
				return line >= 250 && line <= 260;
			})
			.ToArray();
		var archivedSyncLoopVhposBoundaryReads = archivedSyncLoopVerticalBoundaryPhases
			.Where(phase =>
				phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuDataRead &&
				(phase.CpuPhase.Address & 0x00FF_FFFE) == 0x00DFF006)
			.ToArray();
		var archivedSyncCpu3BoundaryVhposReads = archivedSyncLoopVhposBoundaryReads
			.Where(phase => (phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF) == 0x0702F2)
			.ToArray();
		var archivedSyncCpu3VhposReads = archivedFrameSyncLoopCpuDataPhases
			.Where(phase =>
				(phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF) == 0x0702F2 &&
				phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuDataRead &&
				(phase.CpuPhase.Address & 0x00FF_FFFE) == 0x00DFF006)
			.ToArray();
		var archivedWaitLineVhposReads = GetSyncCpuVhposReads(archivedFrameSyncLoopCpuDataPhases, 0x0702C0);
		var archivedWaitLineEdgeReads = archivedWaitLineVhposReads
			.GroupBy(phase => GetPalBeamLine(phase.CpuPhase.RequestedCycle))
			.OrderBy(group => group.Key)
			.SelectMany(group => group.Count() == 1
				? [group.First()]
				: new[] { group.First(), group.Last() })
			.ToArray();
		var archivedSyncCpu1VhposReads = GetSyncCpuVhposReads(archivedFrameSyncLoopCpuDataPhases, 0x0702DA);
		var archivedSyncCpu2VhposReads = GetSyncCpuVhposReads(archivedFrameSyncLoopCpuDataPhases, 0x0702E6);
		var archivedSyncCpu3LowByteWrapReads = archivedSyncCpu3VhposReads
			.Where(phase =>
			{
				var read = FindCustomRegisterRead(customRegisterReads, phase);
				if (!read.HasValue)
				{
					return false;
				}

				var low = read.Value.Value & 0x00FF;
				return low <= 0x08 || low >= 0xF8;
			})
			.ToArray();
		var archivedSyncCpu4VhposReads = archivedFrameSyncLoopCpuDataPhases
			.Where(phase =>
				(phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF) == 0x0703CC &&
				phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuDataRead &&
				(phase.CpuPhase.Address & 0x00FF_FFFE) == 0x00DFF006)
			.ToArray();
		var syncLoopPcCounts = syncLoopCpuPhases
			.GroupBy(phase => phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF)
			.OrderBy(group => group.Key)
			.Select(group => $"0x{group.Key:X6}:{group.Count()}");
		var archivedSyncLoopPcCounts = archivedFrameSyncLoopCpuDataPhases
			.GroupBy(phase => phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF)
			.OrderBy(group => group.Key)
			.Select(group => $"0x{group.Key:X6}:{group.Count()}");
		var irq3Vector = machine.Bus.ReadLong((24u + 3u) * 4u) & 0x00FF_FFFF;
		var interruptDispatches = machine.InterruptDispatchTrace;
		var lastInterruptDispatch = interruptDispatches.Count > 0
			? interruptDispatches[^1]
			: default;
		var firstReadAfterLastDispatch = interruptDispatches.Count > 0
			? customRegisterReads.FirstOrDefault(read =>
				read.Address == 0x006 &&
				read.RequestedCycle >= lastInterruptDispatch.EntryCompletedCycle)
			: default;
		var lastInterruptPrefixPhases = firstReadAfterLastDispatch.RequestedCycle > 0
			? machine.Bus.CpuBusPhases.Where(phase =>
				phase.CpuPhase.RequestedCycle >= lastInterruptDispatch.AcceptanceCycle &&
				phase.CpuPhase.RequestedCycle <= firstReadAfterLastDispatch.CompletedCycle).ToArray()
			: [];

		return
		[
			$"TRACE {relativePath}: stripe-to-sync starts {FormatStripeToSyncStarts(machine.Bus.CpuBusPhases, archivedFrameStartCycle)}",
			$"TRACE {relativePath}: stripe handoff phases {FormatCpuBusPhases(archivedStripeHandoffPhases)}",
			$"TRACE {relativePath}: late transition phases {FormatLateTransitionPhases(archivedFrameSyncLoopCpuPhases, customRegisterReads, archivedFrameStartCycle)}",
			$"TRACE {relativePath}: archived synccpu1 vhpos reads {FormatSyncCpuStageSummary(archivedSyncCpu1VhposReads, customRegisterReads)}",
			$"TRACE {relativePath}: archived synccpu1 physical {FormatCpuBusPhaseReads(archivedSyncCpu1VhposReads, customRegisterReads)}",
			$"TRACE {relativePath}: archived synccpu exits " +
			$"sync1={FormatSyncCpuExitTransitions(archivedSyncCpu1VhposReads, customRegisterReads, syncColorWrites, 0x000F)}",
			$"TRACE {relativePath}: cpuCycles={machine.Cpu.State.Cycles}, " +
			$"pc=0x{machine.Cpu.State.ProgramCounter & 0x00FF_FFFF:X6}, " +
			$"lastPc=0x{machine.Cpu.State.LastInstructionProgramCounter & 0x00FF_FFFF:X6}, " +
			$"sr=0x{machine.Cpu.State.StatusRegister:X4}, code={FormatCodeWords(machine, machine.Cpu.State.LastInstructionProgramCounter, 8)}, " +
			$"writes(total={writes.Count}, color00={colorWrites.Length}, intreq={intreqWrites.Length}, " +
			$"intena={intenaWrites.Length}, dmacon={dmaconWrites.Length}, bltsize={bltsizeWrites.Length}), " +
			$"dmaconrCpuReads={dmaconReads.Length}, intena=0x{machine.Bus.Paula.Intena:X4}, " +
			$"intreq=0x{machine.Bus.Paula.Intreq:X4}, dmacon=0x{machine.Bus.Paula.Dmacon:X4}, " +
			$"blitterBusy={machine.Bus.Blitter.CaptureSnapshot().Busy}",
			$"TRACE {relativePath}: display bplcon0=0x{display.Bplcon0:X4}, ddf=0x{display.DdfStart:X4}-0x{display.DdfStop:X4}, " +
			$"diw=0x{display.DiwStart:X4}-0x{display.DiwStop:X4}, bitplanePixels={display.LastBitplaneNonZeroPixels}, " +
			$"bitplaneRows={display.LastBitplaneRows}, bitplaneWords={display.LastBitplaneWords}, " +
			$"bitplaneColors=[{string.Join(',', display.BitplaneColorCounts.Select((count, index) => count == 0 ? null : $"{index}:{count}").Where(item => item != null))}]",
			$"TRACE {relativePath}: timeline active={display.LastActiveTimelineFrameCount}, archived={display.LastArchivedTimelineFrameCount}, " +
			$"segments={display.LastTimelineSegmentCount}, coalesced={display.LastTimelineCoalescedSegmentCount}, " +
			$"fallbacks={display.LastTimelineFallbackCount}, missingBitplaneFallbacks={display.LastTimelineMissingBitplaneFallbackCount}, " +
			$"fastRows={display.LastTimelineFastPathRowCount}, " +
			$"fastMissRows={display.LastTimelineFastPathMissCount}",
			$"TRACE {relativePath}: displayQueues liveFrameWrites={liveFrameWriteCount}, liveOverflow={liveFrameWriteOverflowed}, " +
			$"pendingWrites={pendingWriteCount}, pendingIndex={pendingWriteIndex}",
			$"TRACE {relativePath}: archiveRejects incomplete={display.LastArchiveRejectFrameIncomplete}, invalid={display.LastArchiveRejectTimelineInvalid}, " +
			$"unsafeWrite={display.LastArchiveRejectUnsafeWrite}, segmentCapacity={display.LastArchiveRejectSegmentCapacity}, " +
			$"missingLine={display.LastArchiveRejectMissingLine}, unsafeLine={display.LastArchiveRejectUnsafeLine}, " +
			$"missingBitplane={display.LastArchiveRejectMissingBitplaneFetch}, missingSprite={display.LastArchiveRejectMissingSpriteFetch}",
			$"TRACE {relativePath}: archivedTimeline frame={FormatBeam(archivedFrameStartCycle)}..{FormatBeam(archivedFrameStopCycle)}, " +
			$"color00InFrame={archivedFrameColorWrites.Length}, color00FrameFirst={FormatWrites(archivedFrameColorWrites.Take(16))}, " +
			$"color00FrameLast={FormatWrites(archivedFrameColorWrites.Skip(Math.Max(0, archivedFrameColorWrites.Length - 16)))}",
			$"TRACE {relativePath}: previousFrameColor00 count={previousFrameColorWrites.Length}, " +
			$"syncValuesTotal={FormatColorValueCounts(syncColorWrites)}, syncValuesPrev={FormatColorValueCounts(previousFrameColorWrites)}, " +
			$"syncValuesFrame={FormatColorValueCounts(archivedFrameColorWrites)}, " +
			$"previousFrameFirst={FormatWrites(previousFrameColorWrites.Take(16))}",
			$"TRACE {relativePath}: syncColor first={FormatWrites(syncColorWrites.Take(24))}",
			$"TRACE {relativePath}: syncColor last={FormatWrites(syncColorWrites.Skip(Math.Max(0, syncColorWrites.Length - 24)))}",
			$"TRACE {relativePath}: archivedTimelineRows {TraceArchivedTimelineRows(machine.Bus.Display, 0, 6, 22, 24, 34, 36, 38, 126, 127, 128, 158, 159, 160, 183, 184, 185, 186, 187, 188, 189, 190, 197, 205, 214, 215, 216, 221, 229, 260, 284)}",
			$"TRACE {relativePath}: archivedPaletteColor00 {TraceArchivedPaletteColor00(machine.Bus.Display, 20, 40)}",
			$"TRACE {relativePath}: cycle bit copper operand writes {FormatCycleBitCopperOperandWrites(machine.Bus.CpuChipRamWriteTrace, machine.Bus.ChipRam)}",
			$"TRACE {relativePath}: cycle bit copper operand phases {FormatCycleBitCopperOperandPhases(machine.Bus.CpuBusPhases, machine.Bus.ChipRam)}",
			$"TRACE {relativePath}: cycle raw stripe reference {FormatCycleRawStripeReference(relativePath)}",
			$"TRACE {relativePath}: cycle ADF boundaries {FormatCycleAdfIterationBoundaries(machine, customRegisterReads)}",
			$"TRACE {relativePath}: color00 presentation rows158-160 {FormatColor00PresentationWindow(archivedFrameColorWrites, archivedFrameColor00CpuPhases, archivedFrameStartCycle, 158, 160)}",
			$"TRACE {relativePath}: color00 presentation firstMismatchRows {FormatColor00PresentationWindow(archivedFrameColorWrites, archivedFrameColor00CpuPhases, archivedFrameStartCycle, 183, 183)}",
			$"TRACE {relativePath}: color00 presentation rows214-216 {FormatColor00PresentationWindow(archivedFrameColorWrites, archivedFrameColor00CpuPhases, archivedFrameStartCycle, 214, 216)}",
			$"TRACE {relativePath}: color00 first={FormatWrites(colorWrites.Take(16))}",
			$"TRACE {relativePath}: color00 last={FormatWrites(colorWrites.Skip(Math.Max(0, colorWrites.Length - 16)))}",
			$"TRACE {relativePath}: bitplane pointer writes {FormatWrites(writes.Where(write => write.Address is >= 0x0E0 and <= 0x0F6).TakeLast(10))}",
			$"TRACE {relativePath}: copper bus v098/v100 {FormatBusAccesses(machine.Bus.BusAccesses.Where(access => access.Request.Kind == AmigaBusAccessKind.Copper && GetPalBeamLine(access.GrantedCycle) is 98 or 100))}",
			$"TRACE {relativePath}: copper bus remaining wait rows {FormatBusAccesses(machine.Bus.BusAccesses.Where(access => access.Request.Kind == AmigaBusAccessKind.Copper && GetPalBeamLine(access.GrantedCycle) is 170 or 172 or 244 or 246))}",
			$"TRACE {relativePath}: all bus v098h056-090 {FormatBusAccesses(machine.Bus.BusAccesses.Where(access => GetPalBeamLine(access.GrantedCycle) == 98 && GetPalBeamHorizontal(access.GrantedCycle) is >= 56 and <= 90))}",
			$"TRACE {relativePath}: all bus remaining wait windows {FormatBusAccesses(machine.Bus.BusAccesses.Where(access => GetPalBeamLine(access.GrantedCycle) is 100 or 170 or 172 or 244 or 246 && GetPalBeamHorizontal(access.GrantedCycle) is >= 56 and <= 110))}",
			$"TRACE {relativePath}: copper words 070190 {FormatCodeWords(machine, 0x070190, 72)}",
			$"TRACE {relativePath}: copper WAIT control {string.Join("; ", machine.Bus.Display.CopperWaitTransitions.Where(trace => trace.SatisfiedCycle >= archivedFrameStartCycle && trace.SatisfiedCycle < archivedFrameStopCycle).Select(trace => $"pc=0x{trace.Pc:X6} wait=0x{trace.WaitFirst:X4} cmp={FormatBeam(trace.ComparisonCycle)} sat={FormatBeam(trace.SatisfiedCycle)} restart={FormatBeam(trace.RestartCycle)} carry={trace.CarryPending}/{trace.CarrySkipCount} blocked={trace.RestartIncomingRgaBlocked}"))}",
			$"TRACE {relativePath}: copper presentation transitions{Environment.NewLine}{string.Join(Environment.NewLine, machine.Bus.Display.CopperPresentationTransitions.Where(trace => trace.Row is 72 or 73 or 74 or 75 or 144 or 145 or 146 or 147 or 148 or 218 or 219 or 220 or 221).Select(trace => $"{FormatBeam(trace.Cycle)} row={trace.Row} x={trace.X} reg=0x{trace.Offset:X3} value=0x{trace.Value:X4} planes={trace.PlaneCount}/0x{trace.PlaneHasRowMask:X2} bases={trace.BitplaneBaseRow0},{trace.BitplaneBaseRow1},{trace.BitplaneBaseRow2},{trace.BitplaneBaseRow3},{trace.BitplaneBaseRow4} wait=0x{trace.WaitFirst:X4} run={trace.SatisfiedWaitRunCount} beganBlocked={trace.WaitRunBeganWithBlockingComparison} blocked={trace.WaitRunControlBlocked} compare={FormatBeam(trace.WaitComparisonStartCycle)} satisfied={FormatBeam(trace.WaitSatisfiedCycle)} restart={FormatBeam(trace.WaitRestartCycle)} rga={trace.WaitRestartRgaDecision}/{trace.WaitRestartRgaIncoming}/{trace.WaitRestartRgaOutput} request={FormatBeam(trace.SecondWordRequestedCycle)} offsets={trace.WaitPresentationPixelOffset}/{trace.FivePlaneWaitMovePixelOffset}"))}",
			$"TRACE {relativePath}: v098 DDF-tail bitplanes {string.Join("; ", machine.Bus.BusAccesses.Where(access => access.Request.Kind == AmigaBusAccessKind.Bitplane && GetPalBeamLine(access.GrantedCycle) == 98 && GetPalBeamHorizontal(access.GrantedCycle) is >= 145 and <= 170).Select(access => $"{FormatBeam(access.GrantedCycle)} addr=0x{access.Request.Address:X6} word=0x{BigEndian.ReadUInt16(machine.Bus.ChipRam, (int)access.Request.Address, "bitplane trace"):X4}"))}",
			$"TRACE {relativePath}: rendered timeline rows72-74 {string.Join("; ", machine.Bus.Display.RenderedCopperTimelineSegments.Where(segment => segment.Row is >= 72 and <= 74).Select(segment => $"row={segment.Row} {segment.XStart}-{segment.XStop} palette={segment.PaletteSnapshotIndex} color0=0x{segment.Color0:X3}"))}",
			$"TRACE {relativePath}: rendered pixel tails {string.Join("; ", machine.Bus.Display.RenderedCopperPixelTraces.Select(trace => $"row={trace.Row} stage={trace.Stage} x217-222={trace.X217:X8},{trace.X218:X8},{trace.X219:X8},{trace.X220:X8},{trace.X221:X8},{trace.X222:X8}"))}",
			$"TRACE {relativePath}: color00 cpu phases frameFirst={FormatCpuBusPhases(archivedFrameColor00CpuPhases.Take(32))}, " +
			$"frameLast={FormatCpuBusPhases(archivedFrameColor00CpuPhases.Skip(Math.Max(0, archivedFrameColor00CpuPhases.Length - 32)))}",
			$"TRACE {relativePath}: intreq first={FormatWrites(intreqWrites.Take(16))}",
			$"TRACE {relativePath}: intreq near archived frame={FormatWrites(nearbyIntreqWrites)}",
			$"TRACE {relativePath}: interrupt dispatches count={interruptDispatches.Count}, " +
			$"last={FormatInterruptDispatches(interruptDispatches.Skip(Math.Max(0, interruptDispatches.Count - 8)))}",
			$"TRACE {relativePath}: last interrupt prefix phases " +
			$"{FormatCpuBusPhases(lastInterruptPrefixPhases)}",
			$"TRACE {relativePath}: irq3 vector=0x{irq3Vector:X6}, code={FormatCodeWords(machine, irq3Vector, 96)}",
			$"TRACE {relativePath}: bltsize first={FormatWrites(bltsizeWrites.Take(16))}",
			$"TRACE {relativePath}: dmaconr reads first={FormatBusAccesses(dmaconReads.Take(16))}",
			$"TRACE {relativePath}: dmaconr reads last={FormatBusAccesses(dmaconReads.Skip(Math.Max(0, dmaconReads.Length - 16)))}",
			$"TRACE {relativePath}: beam reads count={beamReads.Length}, frameCount={archivedFrameBeamReads.Length}, " +
			$"frameFirst={FormatBusAccesses(archivedFrameBeamReads.Take(16))}, " +
			$"frameLast={FormatBusAccesses(archivedFrameBeamReads.Skip(Math.Max(0, archivedFrameBeamReads.Length - 16)))}",
			$"TRACE {relativePath}: beam register values frameCount={archivedFrameCustomRegisterReads.Length}, " +
			$"frameFirst={FormatCustomRegisterReads(archivedFrameCustomRegisterReads.Take(24))}, " +
			$"frameLast={FormatCustomRegisterReads(archivedFrameCustomRegisterReads.Skip(Math.Max(0, archivedFrameCustomRegisterReads.Length - 24)))}",
			$"TRACE {relativePath}: probe10 value candidates {FormatProbe10ValueCandidates(machine.Bus.ChipRam)}",
			$"TRACE {relativePath}: vhposr read bursts {FormatVhposrReadBursts(customRegisterReads)}",
			$"TRACE {relativePath}: vposr reads count={vposrReads.Length}, " +
			$"first={FormatBusAccesses(vposrReads.Take(16))}, " +
			$"last={FormatBusAccesses(vposrReads.Skip(Math.Max(0, vposrReads.Length - 32)))}",
			$"TRACE {relativePath}: vposr cpu phases first={FormatCpuBusPhases(vposrCpuPhases.Take(16))}, " +
			$"last={FormatCpuBusPhases(vposrCpuPhases.Skip(Math.Max(0, vposrCpuPhases.Length - 16)))}",
			$"TRACE {relativePath}: beam cpu phases count={beamCpuPhases.Length}, frameCount={archivedFrameBeamCpuPhases.Length}, " +
			$"frameFirst={FormatCpuBusPhases(archivedFrameBeamCpuPhases.Take(16))}, " +
			$"frameLast={FormatCpuBusPhases(archivedFrameBeamCpuPhases.Skip(Math.Max(0, archivedFrameBeamCpuPhases.Length - 16)))}",
			$"TRACE {relativePath}: beam reads by pc {FormatBeamReadsByProgramCounter(beamCpuPhases, customRegisterReads)}",
			$"TRACE {relativePath}: synccpu pcCounts={string.Join(',', syncLoopPcCounts)}",
			$"TRACE {relativePath}: archived synccpu data pcCounts={string.Join(',', archivedSyncLoopPcCounts)}",
			$"TRACE {relativePath}: archived synccpu boundary v250-v260 reads={archivedSyncLoopVhposBoundaryReads.Length}, " +
			$"synccpu3={FormatCpuBusPhaseReads(archivedSyncCpu3BoundaryVhposReads.Take(48), customRegisterReads)}",
			$"TRACE {relativePath}: archived waitLine vhpos reads {FormatSyncCpuStageSummary(archivedWaitLineVhposReads, customRegisterReads)}",
			$"TRACE {relativePath}: archived waitLine edge reads {FormatCpuBusPhaseReads(archivedWaitLineEdgeReads, customRegisterReads)}",
			$"TRACE {relativePath}: archived synccpu1 vhpos reads {FormatSyncCpuStageSummary(archivedSyncCpu1VhposReads, customRegisterReads)}",
			$"TRACE {relativePath}: archived synccpu2 vhpos reads {FormatSyncCpuStageSummary(archivedSyncCpu2VhposReads, customRegisterReads)}",
			$"TRACE {relativePath}: archived synccpu exits " +
			$"sync1={FormatSyncCpuExitTransitions(archivedSyncCpu1VhposReads, customRegisterReads, syncColorWrites, 0x000F)}, " +
			$"sync2={FormatSyncCpuExitTransitions(archivedSyncCpu2VhposReads, customRegisterReads, syncColorWrites, 0x001F)}, " +
			$"sync3={FormatSyncCpuExitTransitions(archivedSyncCpu3VhposReads, customRegisterReads, syncColorWrites, 0x00FF)}",
			$"TRACE {relativePath}: late transition code {FormatCodeWords(machine, 0x0702C0, 24)}",
			$"TRACE {relativePath}: stripe-to-sync starts {FormatStripeToSyncStarts(machine.Bus.CpuBusPhases, archivedFrameStartCycle)}",
			$"TRACE {relativePath}: stripe handoff phases {FormatCpuBusPhases(archivedStripeHandoffPhases)}",
			$"TRACE {relativePath}: first stripe wrap phases {FormatCpuBusPhases(archivedFirstStripeWrapPhases)}",
			$"TRACE {relativePath}: initial wait-line phases {FormatCpuBusPhases(archivedInitialWaitLinePhases)}",
			$"TRACE {relativePath}: first wait-line wrap phases {FormatCpuBusPhases(archivedFirstWaitWrapPhases)}",
			$"TRACE {relativePath}: late transition phases {FormatLateTransitionPhases(archivedFrameSyncLoopCpuPhases, customRegisterReads, archivedFrameStartCycle)}",
			$"TRACE {relativePath}: archived synccpu3 vhpos reads count={archivedSyncCpu3VhposReads.Length}, " +
			$"first={FormatCpuBusPhaseReads(archivedSyncCpu3VhposReads.Take(24), customRegisterReads)}, " +
			$"last={FormatCpuBusPhaseReads(archivedSyncCpu3VhposReads.Skip(Math.Max(0, archivedSyncCpu3VhposReads.Length - 24)), customRegisterReads)}, " +
			$"lowWrap={FormatCpuBusPhaseReads(archivedSyncCpu3LowByteWrapReads, customRegisterReads)}",
			$"TRACE {relativePath}: archived synccpu4 vhpos reads count={archivedSyncCpu4VhposReads.Length}, " +
			$"first={FormatCpuBusPhaseReads(archivedSyncCpu4VhposReads.Take(24), customRegisterReads)}, " +
			$"last={FormatCpuBusPhaseReads(archivedSyncCpu4VhposReads.Skip(Math.Max(0, archivedSyncCpu4VhposReads.Length - 24)), customRegisterReads)}",
			$"TRACE {relativePath}: archived synccpu data first={FormatCpuBusPhases(archivedFrameSyncLoopCpuDataPhases.Take(48))}",
			$"TRACE {relativePath}: archived synccpu data last={FormatCpuBusPhases(archivedFrameSyncLoopCpuDataPhases.Skip(Math.Max(0, archivedFrameSyncLoopCpuDataPhases.Length - 48)))}",
			$"TRACE {relativePath}: synccpu3 phases first={FormatCpuBusPhases(syncLoopCpuPhases.Take(24))}",
			$"TRACE {relativePath}: synccpu3 phases last={FormatCpuBusPhases(syncLoopCpuPhases.Skip(Math.Max(0, syncLoopCpuPhases.Length - 24)))}"
		];
	}

	private static Machine GetMachine(CopperScreenEmulator emulator)
	{
		var field = typeof(CopperScreenEmulator).GetField("_machine", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
		Assert.NotNull(field);
		return Assert.IsType<Machine>(field.GetValue(emulator));
	}

	private static AmigaBootController GetBoot(CopperScreenEmulator emulator)
	{
		var field = typeof(CopperScreenEmulator).GetField(
			"_boot",
			System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
		return Assert.IsType<AmigaBootController>(field?.GetValue(emulator));
	}

	private static string[] FormatProbe10PresentationTrace(
		Machine machine,
		string adfPath,
		ReadOnlySpan<int> framebuffer,
		int rawFrame)
	{
		var display = machine.Bus.Display;
		var archivedFrameStartCycle = GetPrivateField<long>(display, "_archivedTimelineFrameStartCycle");
		var archivedFrameStopCycle = GetPrivateField<long>(display, "_archivedTimelineFrameStopCycle");
		var hasArchivedFrame = archivedFrameStartCycle >= 0 && archivedFrameStopCycle > archivedFrameStartCycle;
		var cpuDisplayWrites = machine.Bus.CustomRegisterWrites
			.Where(write => hasArchivedFrame &&
				write.Cycle >= archivedFrameStartCycle &&
				write.Cycle < archivedFrameStopCycle &&
				write.Address is 0x08E or 0x090 or 0x092 or 0x094 or 0x096 or 0x0E0 or 0x0E2 or 0x0E4 or 0x0E6)
			.ToArray();
		var color00Writes = machine.Bus.CustomRegisterWrites
			.Where(write => hasArchivedFrame &&
				write.Cycle >= archivedFrameStartCycle &&
				write.Cycle < archivedFrameStopCycle &&
				write.Address == 0x180)
			.ToArray();
		var precedingCpuDisplaySetup = machine.Bus.CustomRegisterWrites
			.Where(write => hasArchivedFrame &&
				write.Cycle >= archivedFrameStartCycle - (2 * AmigaConstants.A500PalCpuCyclesPerFrame) &&
				write.Cycle < archivedFrameStartCycle &&
				write.Address is 0x08E or 0x090 or 0x092 or 0x094 or 0x096 or 0x0E0 or 0x0E2 or 0x0E4 or 0x0E6)
			.TakeLast(32)
			.ToArray();
		var copperDisplayWrites = display.CopperDisplayWrites
			.Where(write => hasArchivedFrame &&
				write.Cycle >= archivedFrameStartCycle &&
				write.Cycle < archivedFrameStopCycle &&
				write.Address is 0x100 or 0x08E or 0x090 or 0x092 or 0x094 or 0x0E0 or 0x0E2 or 0x0E4 or 0x0E6)
			.ToArray();
		var bitplaneFetches = machine.Bus.BusAccesses
			.Where(access => hasArchivedFrame &&
				access.Request.Requester == AmigaBusRequester.Bitplane &&
				access.Request.Kind == AmigaBusAccessKind.Bitplane &&
				access.GrantedCycle >= archivedFrameStartCycle &&
				access.GrantedCycle < archivedFrameStopCycle)
			.ToArray();
		var nonZeroChipWrites = machine.Bus.CpuChipRamWriteTrace
			.Where(write => hasArchivedFrame &&
				write.Cycle >= archivedFrameStartCycle &&
				write.Cycle < archivedFrameStopCycle &&
				write.Value != 0)
			.ToArray();
		var rows = Enumerable.Range(18, 27).ToArray();

		return
		[
			$"TRACE probe10 presentation rawFrame={rawFrame}, archivedFrame={FormatBeam(archivedFrameStartCycle)}..{FormatBeam(archivedFrameStopCycle)}",
			$"TRACE probe10 presentation color00={FormatAddressedWrites(color00Writes)}",
			$"TRACE probe10 presentation cpuDisplaySetup={FormatAddressedWrites(cpuDisplayWrites)}",
			$"TRACE probe10 presentation precedingCpuDisplaySetup={FormatAddressedWrites(precedingCpuDisplaySetup)}",
			$"TRACE probe10 presentation copperDisplayMoves={FormatAddressedWrites(copperDisplayWrites)}",
			$"TRACE probe10 presentation bitplaneFetches count={bitplaneFetches.Length}, first={FormatBusAccesses(bitplaneFetches.Take(32))}",
			$"TRACE probe10 presentation firstNonZeroChipWrites={FormatCpuChipRamWrites(nonZeroChipWrites.Take(64))}",
			$"TRACE probe10 presentation timelineRows {TraceArchivedTimelineRows(display, rows)}",
			$"TRACE probe10 presentation rawRows {FormatProbe10RawRows(adfPath, framebuffer, 18, 44)}"
		];
	}

	private static string FormatProbe10FramePhaseLedger(Machine machine)
	{
		var display = machine.Bus.Display;
		var frameStart = GetPrivateField<long>(display, "_archivedTimelineFrameStartCycle");
		var frameStop = GetPrivateField<long>(display, "_archivedTimelineFrameStopCycle");
		if (frameStart < 0 || frameStop <= frameStart)
		{
			return "archived-frame=missing";
		}

		var colorWrites = machine.Bus.CustomRegisterWrites
			.Where(write =>
				write.Cycle >= frameStart &&
				write.Cycle < frameStop &&
				write.Address == 0x180)
			.OrderBy(write => write.Cycle)
			.ToArray();
		var purple = colorWrites.FirstOrDefault(write => write.Value == 0x0F0F);
		var green = colorWrites.FirstOrDefault(write => write.Value == 0x0606 && write.Cycle > purple.Cycle);
		var sync1Reads = purple.Cycle > 0 && green.Cycle > purple.Cycle
			? machine.Bus.CustomRegisterReadTrace
				.Where(read =>
					read.Address == 0x006 &&
					read.SampleCycle >= purple.Cycle &&
					read.SampleCycle <= green.Cycle)
				.ToArray()
			: [];

		var firstRead = sync1Reads.Take(1).ToArray();
		var lastRead = sync1Reads.TakeLast(1).ToArray();
		return $"archived={FormatBeam(frameStart)}..{FormatBeam(frameStop)}, " +
			$"colors={FormatAddressedWrites(colorWrites.Take(6))}, " +
			$"sync1Count={sync1Reads.Length}, first={FormatCustomRegisterReads(firstRead)}, " +
			$"last={FormatCustomRegisterReads(lastRead)}";
	}

	private static string FormatProbe10InterruptLedger(Machine machine, int frame)
	{
		var display = machine.Bus.Display;
		var frameStart = GetPrivateField<long>(display, "_archivedTimelineFrameStartCycle");
		var frameStop = GetPrivateField<long>(display, "_archivedTimelineFrameStopCycle");
		if (frameStart < 0 || frameStop <= frameStart)
		{
			return "archived-frame=missing";
		}

		var inFrame = machine.Bus.CustomRegisterWrites
			.Where(write => write.Cycle >= frameStart && write.Cycle < frameStop)
			.ToArray();
		var intreq = inFrame
			.Where(write => write.Address == 0x09C && (write.Value & 0x8008) == 0x8008)
			.ToArray();
		var vposw = inFrame
			.Where(write => write.Address == 0x02A)
			.ToArray();
		var displaySetup = inFrame
			.Where(write => write.Address is 0x0E0 or 0x0E2 or 0x0E4 or 0x0E6 or 0x092 or 0x094 or 0x08E or 0x090)
			.ToArray();
		var firstHandlerPhase = machine.Bus.CpuBusPhases
			.Where(phase =>
				phase.CpuPhase.RequestedCycle >= frameStart &&
				phase.CpuPhase.RequestedCycle < frameStop &&
				phase.CpuPhase.IsWrite &&
				(phase.CpuPhase.Address & 0x00FF_FFFE) == 0x00DFF0E0)
			.OrderBy(phase => phase.CpuPhase.RequestedCycle)
			.Take(1)
			.ToArray();
		var irq2Vector = BigEndian.ReadUInt32(machine.Bus.ChipRam, 0x0068, "irq2 vector") & 0x00FF_FFFF;
		var rtePhases = machine.Bus.CpuBusPhases
			.Where(phase =>
				phase.CpuPhase.RequestedCycle >= frameStart &&
				phase.CpuPhase.RequestedCycle < frameStop &&
				phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuInstructionFetch &&
				(phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF) >= irq2Vector &&
				phase.CpuPhase.Address + 1 < machine.Bus.ChipRam.Length &&
				BigEndian.ReadUInt16(machine.Bus.ChipRam, (int)phase.CpuPhase.Address, "irq2 RTE") == 0x4E73)
			.OrderBy(phase => phase.CpuPhase.RequestedCycle)
			.Take(1)
			.ToArray();
		var vectorCpuPhases = machine.Bus.CpuBusPhases
			.Where(phase =>
				phase.CpuPhase.Address == 0x0068 &&
				phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuDataRead &&
				phase.CpuPhase.RequestedCycle >= frameStart - 128 &&
				phase.CpuPhase.RequestedCycle < frameStop)
			.OrderBy(phase => phase.CpuPhase.RequestedCycle)
			.Take(1)
			.ToArray();
		var colorWrites = inFrame
			.Where(write => write.Address == 0x180)
			.ToArray();
		var vectorReads = machine.Bus.BusAccesses
			.Where(access =>
				access.Request.Requester == AmigaBusRequester.Cpu &&
				access.Request.Kind == AmigaBusAccessKind.CpuDataRead &&
				access.Request.Address == 0x0068 &&
				access.Request.RequestedCycle >= frameStart - 128 &&
				access.Request.RequestedCycle < frameStop)
			.ToArray();
		var copperBplcon = display.CopperDisplayWrites
			.Where(write =>
				write.Cycle >= frameStart &&
				write.Cycle < frameStop &&
				write.Address == 0x100)
			.ToArray();
		var position = machine.Bus.GetBeamPosition(Math.Max(frameStart, frameStop - 1));
		var firstIntreq = intreq.FirstOrDefault();
		var firstSetup = displaySetup.FirstOrDefault();
		var firstHandler = firstHandlerPhase.FirstOrDefault();
		var hasHandler = firstHandlerPhase.Length != 0;
		var hasIntreq = intreq.Length != 0;
		var setupDelta = hasIntreq && displaySetup.Length != 0 ? firstSetup.Cycle - firstIntreq.Cycle : -1;
		var handlerDelta = hasIntreq && hasHandler ? firstHandler.CpuPhase.RequestedCycle - firstIntreq.Cycle : -1;
		var rteDelta = hasIntreq && rtePhases.Length != 0
			? rtePhases[0].CpuPhase.RequestedCycle - firstIntreq.Cycle
			: -1;
		var rteTargetPrefetch = rtePhases.Length != 0
			? FormatCpuStateAfterRte(
				machine,
				rtePhases[0].CpuPhase.CompletedCycle,
				rtePhases[0].CpuPhase.InstructionProgramCounter)
			: "rteTarget=missing";
		var rteWindow = rtePhases.Length != 0
			? FormatCpuPhaseWindow(
				machine,
				rtePhases[0].CpuPhase.RequestedCycle - 32,
				rtePhases[0].CpuPhase.RequestedCycle + 128)
			: "rteWindow=missing";
		var interruptCpuStates = string.Join(
			"; ",
			intreq.Select(write => FormatCpuStateAroundCycle(machine, write.Cycle)));
		return
			$"archive={FormatBeam(frameStart)}..{FormatBeam(frameStop)}, " +
			$"lines={position.RasterLines},long={position.IsLongFrame},cycles={frameStop - frameStart}, " +
			$"vposw={FormatAddressedWrites(vposw)}, " +
			$"intreq2={FormatWrites(intreq)}, " +
			$"cpuStateAroundIntreq={interruptCpuStates}, " +
			$"vector68={FormatBusAccesses(vectorReads)}, " +
			$"vectorCpuState={FormatCpuStatePhase("vector", vectorCpuPhases)}, " +
			$"handlerBpl1={FormatCpuBusPhases(firstHandlerPhase)},handlerDelta={handlerDelta}, " +
			$"irq2Vector=0x{irq2Vector:X6},rte={FormatCpuStatePhase("rte", rtePhases)},rteTarget={rteTargetPrefetch},rteWindow={rteWindow},rteDelta={rteDelta}, " +
			$"setupFirst={FormatAddressedWrites(displaySetup.Take(1))},setupDelta={setupDelta}, " +
			$"setupLast={FormatAddressedWrites(displaySetup.TakeLast(8))}, " +
			$"colorFirst={FormatAddressedWrites(colorWrites.Take(4))}, " +
			$"copperBplcon={FormatAddressedWrites(copperBplcon)}";
	}

	private static string FormatCpuStateAroundCycle(Machine machine, long cycle)
	{
		var before = machine.Bus.CpuBusPhases
			.Where(phase => phase.CpuPhase.CompletedCycle <= cycle)
			.OrderByDescending(phase => phase.CpuPhase.CompletedCycle)
			.Take(1)
			.ToArray();
		var after = machine.Bus.CpuBusPhases
			.Where(phase => phase.CpuPhase.RequestedCycle >= cycle)
			.OrderBy(phase => phase.CpuPhase.RequestedCycle)
			.Take(1)
			.ToArray();
		return $"{FormatCpuStatePhase("before", before)}|{FormatCpuStatePhase("after", after)}";
	}

	private static string FormatCpuStatePhase(string label, IReadOnlyList<AmigaCpuBusPhaseTrace> phases)
	{
		if (phases.Count == 0)
		{
			return $"{label}=missing";
		}

		var phase = phases[0].CpuPhase;
		return $"{label}=pc0x{phase.InstructionProgramCounter & 0x00FF_FFFF:X6}/" +
			$"sr0x{phase.StatusRegister:X4}/req{FormatBeam(phase.RequestedCycle)}/done{FormatBeam(phase.CompletedCycle)}";
	}

	private static string FormatCpuStateAfterRte(Machine machine, long cycle, uint rteProgramCounter)
	{
		var phases = machine.Bus.CpuBusPhases
			.Where(phase =>
				phase.CpuPhase.RequestedCycle > cycle &&
				phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuInstructionFetch &&
				phase.CpuPhase.InstructionProgramCounter != rteProgramCounter)
			.OrderBy(phase => phase.CpuPhase.RequestedCycle)
			.Take(1)
			.ToArray();
		return FormatCpuStatePhase("target", phases);
	}

	private static string FormatCpuPhaseWindow(Machine machine, long startCycle, long stopCycle)
		=> string.Join(", ", machine.Bus.CpuBusPhases
			.Where(phase =>
				phase.CpuPhase.RequestedCycle >= startCycle &&
				phase.CpuPhase.RequestedCycle <= stopCycle)
			.OrderBy(phase => phase.CpuPhase.RequestedCycle)
			.Select(phase =>
			{
				var cpu = phase.CpuPhase;
				return $"pc0x{cpu.InstructionProgramCounter & 0x00FF_FFFF:X6}/" +
					$"0x{cpu.Address & 0x00FF_FFFE:X6}/{cpu.AccessKind}/sr0x{cpu.StatusRegister:X4}/" +
					$"{FormatBeam(cpu.RequestedCycle)}..{FormatBeam(cpu.CompletedCycle)}";
			}));

	private static string FormatAddressedWrites(IEnumerable<CustomRegisterWrite> writes)
		=> string.Join("; ", writes.Select(write =>
			$"0x{write.Address:X3}=0x{write.Value:X4}@{FormatBeam(write.Cycle)}"));

	private static string FormatProbe10RawRows(
		string adfPath,
		ReadOnlySpan<int> framebuffer,
		int firstRow,
		int lastRow)
	{
		var rawPath = ResolveReferencePath(adfPath, ".raw");
		if (!File.Exists(rawPath) || framebuffer.Length < RawReferenceWidth * RawReferenceHeight * 2)
		{
			return "unavailable";
		}

		var expected = File.ReadAllBytes(rawPath);
		var rows = new List<string>();
		for (var row = firstRow; row <= lastRow; row++)
		{
			var expectedBounds = FindRawNonBlackBounds(expected, row);
			var actualBounds = FindFramebufferRawRowNonBlackBounds(framebuffer, row);
			if (expectedBounds == actualBounds)
			{
				continue;
			}

			rows.Add($"r{row}:expected={FormatRawBounds(expectedBounds)} actual={FormatRawBounds(actualBounds)}");
		}

		return rows.Count == 0 ? "match" : string.Join("; ", rows);
	}

	private static (int Count, int FirstX, int LastX) FindRawNonBlackBounds(ReadOnlySpan<byte> raw, int row)
	{
		var count = 0;
		var firstX = -1;
		var lastX = -1;
		for (var x = 0; x < RawReferenceWidth; x++)
		{
			if (ReadRawColor(raw, row, x) == 0)
			{
				continue;
			}

			count++;
			firstX = firstX < 0 ? x : firstX;
			lastX = x;
		}

		return (count, firstX, lastX);
	}

	private static (int Count, int FirstX, int LastX) FindFramebufferRawRowNonBlackBounds(ReadOnlySpan<int> framebuffer, int row)
	{
		var count = 0;
		var firstX = -1;
		var lastX = -1;
		var rowOffset = row * 2 * RawReferenceWidth;
		for (var x = 0; x < RawReferenceWidth; x++)
		{
			if (ToVAmigaRawColor(framebuffer[rowOffset + x]) == 0)
			{
				continue;
			}

			count++;
			firstX = firstX < 0 ? x : firstX;
			lastX = x;
		}

		return (count, firstX, lastX);
	}

	private static string FormatRawBounds((int Count, int FirstX, int LastX) bounds)
		=> bounds.Count == 0 ? "none" : $"{bounds.Count}@{bounds.FirstX}-{bounds.LastX}";

	private static int GetPrivateListCount(object instance, string fieldName)
	{
		var field = instance.GetType().GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
		Assert.NotNull(field);
		return Assert.IsAssignableFrom<System.Collections.ICollection>(field.GetValue(instance)).Count;
	}

	private static T GetPrivateField<T>(object instance, string fieldName)
	{
		return Assert.IsType<T>(GetPrivateFieldValue(instance, fieldName));
	}

	private static object? GetPrivateFieldValue(object instance, string fieldName)
	{
		var field = instance.GetType().GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
		Assert.NotNull(field);
		return field.GetValue(instance);
	}

	private static string TraceArchivedTimelineRows(OcsDisplay display, params int[] rows)
	{
		var timelineField = display.GetType().GetField(
			"_archivedDisplayTimeline",
			System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) ??
			display.GetType().GetField(
				"_displayTimeline",
				System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
		if (timelineField?.GetValue(display) is not object timeline)
		{
			return "unavailable (causal rasterline presentation has no archived frame timeline)";
		}
		var lines = Assert.IsAssignableFrom<Array>(GetPrivateFieldValue(timeline, "_lines"));
		var states = Assert.IsAssignableFrom<System.Collections.IList>(
			timeline.GetType()
				.GetField("_states", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
				.GetValue(timeline));
		var generation = GetPrivateField<int>(timeline, "_generation");
		var palettePoolField = display.GetType().GetField(
			"_archivedPaletteSnapshotColors",
			System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
		var paletteColors = palettePoolField?.GetValue(display) as ushort[];
		var paletteCount = 0;
		if (paletteColors == null)
		{
			var livePalettePool = GetPrivateFieldValue(display, "_livePaletteSnapshots");
			Assert.NotNull(livePalettePool);
			paletteColors = GetPrivateField<ushort[]>(livePalettePool, "_encodedColors");
			paletteCount = GetMemberValue<int>(livePalettePool, "Count");
		}
		else
		{
			paletteCount = GetPrivateField<int>(display, "_archivedPaletteSnapshotCount");
		}
		return string.Join(" | ", rows.Select(row =>
		{
			var lineIndex = lines.Length == 0 ? -1 :
				lines.Length > row ? row : row % lines.Length;
			var line = lineIndex >= 0 ? lines.GetValue(lineIndex) : null;
			if (line == null)
			{
				return $"row{row}:missing";
			}
			if (TryGetMemberInt(line, "Row") is int storedRow && storedRow != row)
			{
				return $"row{row}:slot={lineIndex}/stored={storedRow}";
			}

			var lineGeneration = GetMemberValue<int>(line, "Generation");
			var valid = GetMemberValue<bool>(line, "Valid") && lineGeneration == generation;
			if (!valid)
			{
				return $"row{row}:invalid(gen={lineGeneration}/{generation})";
			}

			var segments = Assert.IsAssignableFrom<System.Collections.IList>(GetMemberRawValue(line, "Segments"));
			var fetchMasks = Assert.IsAssignableFrom<UInt128[]>(GetMemberRawValue(line, "BitplaneFetchMasks"));
			var deniedMasks = Assert.IsAssignableFrom<UInt128[]>(GetMemberRawValue(line, "BitplaneDeniedMasks"));
			var bitplaneWords = Assert.IsAssignableFrom<ushort[]>(GetMemberRawValue(line, "BitplaneWords"));
			var segmentText = string.Join(",", segments.Cast<object>().Take(16).Select(segment =>
			{
				var xStart = GetMemberValue<int>(segment, "XStart");
				var xStop = GetMemberValue<int>(segment, "XStop");
				var stateIndex = GetMemberValue<int>(segment, "StateIndex");
				var state = states[stateIndex]!;
				var paletteIndex = GetMemberValue<int>(state, "PaletteSnapshotIndex");
				var color00 = paletteIndex >= 0 && paletteIndex < paletteCount
					? paletteColors[paletteIndex * 32]
					: (ushort)0;
				var bplcon0 = GetMemberValue<ushort>(state, "Bplcon0");
				var dmacon = GetMemberValue<ushort>(state, "Dmacon");
				var verticalOpen = GetMemberValue<bool>(state, "DisplayWindowVerticallyOpen");
				var deniseVerticalOpen = TryGetBoolMember(state, "DeniseDisplayWindowVerticallyOpen") ?? verticalOpen;
				var lineStart = GetMemberValue<long>(state, "LineStartCycle");
				var pointers = GetMemberValue<uint[]>(state, "BitplanePointers");
				var baseRows = GetMemberValue<int[]>(state, "BitplaneBaseRows");
				var rowAddresses = GetMemberValue<uint[]>(state, "BitplaneRowAddresses");
				return $"{xStart}-{xStop}:s{stateIndex}/p{paletteIndex}/c0=0x{color00:X3}/bpl=0x{bplcon0:X4}/dma=0x{dmacon:X4}/win={verticalOpen}/{deniseVerticalOpen}/" +
					$"bp0=0x{pointers[0]:X6}/br0={baseRows[0]}/ra0=0x{rowAddresses[0]:X6}/ls={FormatBeam(lineStart)}";
			}));
			var fetchText = string.Join(",", fetchMasks.Select((mask, plane) =>
			{
				if (mask == 0)
				{
					return $"p{plane}=0";
				}

				var denied = deniedMasks[plane];
				var firstWords = string.Join("/", Enumerable.Range(0, Math.Min(8, 64))
					.Where(word => (mask & (1UL << word)) != 0)
					.Select(word => bitplaneWords[(plane * 64) + word].ToString("X4")));
				return $"p{plane}=0x{mask:X}/d=0x{denied:X}/w={firstWords}";
			}));
			return $"row{row}:segments={segments.Count}[{segmentText}] fetch=[{fetchText}]";
		}));
	}

	private static string TraceArchivedPaletteColor00(OcsDisplay display, int firstIndex, int lastIndex)
	{
		var snapshots = GetPrivateFieldValue(display, "_paletteFrameSnapshots");
		Assert.NotNull(snapshots);
		var colors = GetMemberValue<ushort[]>(snapshots, "_encodedColors");
		var count = GetMemberValue<int>(snapshots, "Count");
		if (count <= 0)
		{
			return "none";
		}

		firstIndex = Math.Clamp(firstIndex, 0, count - 1);
		lastIndex = Math.Clamp(lastIndex, firstIndex, count - 1);
		return string.Join(
			",",
			Enumerable.Range(firstIndex, lastIndex - firstIndex + 1)
				.Select(index => $"p{index}=0x{colors[index * 32]:X3}"));
	}

	private static T GetMemberValue<T>(object instance, string memberName)
		=> Assert.IsType<T>(GetMemberRawValue(instance, memberName));

	private static bool? TryGetBoolMember(object instance, string memberName)
	{
		var flags = System.Reflection.BindingFlags.Instance |
			System.Reflection.BindingFlags.Public |
			System.Reflection.BindingFlags.NonPublic;
		var type = instance.GetType();
		var field = type.GetField(memberName, flags);
		if (field != null)
		{
			return field.GetValue(instance) is bool value ? value : null;
		}

		var property = type.GetProperty(memberName, flags);
		return property?.GetValue(instance) is bool propertyValue ? propertyValue : null;
	}

	private static int? TryGetMemberInt(object instance, string memberName)
	{
		var flags = System.Reflection.BindingFlags.Instance |
			System.Reflection.BindingFlags.Public |
			System.Reflection.BindingFlags.NonPublic;
		var type = instance.GetType();
		var field = type.GetField(memberName, flags);
		if (field != null)
		{
			return field.GetValue(instance) is int value ? value : null;
		}

		var property = type.GetProperty(memberName, flags);
		return property?.GetValue(instance) is int propertyValue ? propertyValue : null;
	}

	private static object? GetMemberRawValue(object instance, string memberName)
	{
		var flags = System.Reflection.BindingFlags.Instance |
			System.Reflection.BindingFlags.Public |
			System.Reflection.BindingFlags.NonPublic;
		var type = instance.GetType();
		var field = type.GetField(memberName, flags);
		if (field != null)
		{
			return field.GetValue(instance);
		}

		var property = type.GetProperty(memberName, flags);
		Assert.NotNull(property);
		return property.GetValue(instance);
	}

	private static string FormatWrites(IEnumerable<CustomRegisterWrite> writes)
		=> string.Join("; ", writes.Select(write => $"{FormatBeam(write.Cycle)}=0x{write.Value:X4}"));

	private static string FormatColor00PresentationWindow(
		IReadOnlyList<CustomRegisterWrite> writes,
		IReadOnlyList<AmigaCpuBusPhaseTrace> cpuPhases,
		long frameStartCycle,
		int firstRow,
		int lastRow)
	{
		if (frameStartCycle < 0)
		{
			return "no archived frame";
		}

		var selected = writes
			.Select((write, index) => (Write: write, Index: index, Placement: CalculatePresentationPlacement(frameStartCycle, write.Cycle)))
			.Where(item => item.Placement.Row >= firstRow - 2 && item.Placement.Row <= lastRow + 2)
			.ToArray();
		if (selected.Length == 0)
		{
			return "none";
		}

		var firstIndex = Math.Max(0, selected[0].Index - 4);
		var lastIndex = Math.Min(writes.Count - 1, selected[^1].Index + 4);
		return string.Join("; ", Enumerable.Range(firstIndex, lastIndex - firstIndex + 1).Select(index =>
		{
			var write = writes[index];
			var placement = CalculatePresentationPlacement(frameStartCycle, write.Cycle);
			var cpu = FindCpuPhaseForCustomWrite(cpuPhases, write);
			var source = cpu.HasValue
				? $"cpu pc=0x{cpu.Value.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF:X6} req={FormatBeam(cpu.Value.CpuPhase.RequestedCycle)} done={FormatBeam(cpu.Value.CpuPhase.CompletedCycle)}"
				: "cpu=none";
			return $"#{index} {FormatBeam(write.Cycle)} beam={placement.Line}.{placement.Horizontal:D3} " +
				$"row={placement.Row} xLo={placement.LowResX} xRaw={placement.RawX} val=0x{write.Value:X4} {source}";
		}));
	}

	private static (int Line, int Horizontal, int Row, int LowResX, int RawX) CalculatePresentationPlacement(
		long frameStartCycle,
		long cycle)
	{
		const int standardVStart = 0x2C - AmigaConstants.PalLowResOverscanBorderY;
		const int defaultDdfStart = 0x0038;
		var frameCycle = Math.Max(0, cycle - frameStartCycle);
		var line = (int)(frameCycle / AmigaConstants.A500PalCpuCyclesPerRasterLine);
		line = Math.Clamp(line, 0, AmigaConstants.A500PalRasterLines - 1);
		var lineCycle = frameCycle - ((long)line * AmigaConstants.A500PalCpuCyclesPerRasterLine);
		var horizontal = (int)(lineCycle / AmigaConstants.A500PalCpuCyclesPerColorClock);
		var lowResX = Math.Clamp((horizontal - defaultDdfStart) * 2, 0, AmigaConstants.PalLowResWidth);
		return (line, horizontal, line - standardVStart, lowResX, lowResX * 2);
	}

	private static AmigaCpuBusPhaseTrace? FindCpuPhaseForCustomWrite(
		IReadOnlyList<AmigaCpuBusPhaseTrace> phases,
		CustomRegisterWrite write)
	{
		for (var i = 0; i < phases.Count; i++)
		{
			var phase = phases[i];
			var cpu = phase.CpuPhase;
			if (!cpu.IsWrite ||
				(cpu.Address & 0x00FF_FFFE) != 0x00DFF180)
			{
				continue;
			}

			if (phase.BusAccess.HasValue &&
				phase.BusAccess.Value.GrantedCycle == write.Cycle)
			{
				return phase;
			}

			if (cpu.CompletedCycle == write.Cycle ||
				(cpu.RequestedCycle <= write.Cycle && write.Cycle <= cpu.CompletedCycle))
			{
				return phase;
			}
		}

		return null;
	}

	private static string FormatColorValueCounts(IEnumerable<CustomRegisterWrite> writes)
	{
		var interesting = new HashSet<ushort> { 0x0F0F, 0x0606, 0x0A0A, 0x0404, 0x0000, 0x0F4F, 0x0FFF };
		return string.Join(
			",",
			writes
				.Where(write => interesting.Contains((ushort)(write.Value & 0x0FFF)))
				.GroupBy(write => (ushort)(write.Value & 0x0FFF))
				.OrderBy(group => group.Key)
				.Select(group => $"0x{group.Key:X3}:{group.Count()}"));
	}

	private static bool IsSyncColorValue(ushort value)
	{
		return (value & 0x0FFF) is 0x0F0F or 0x0606 or 0x0A0A or 0x0404 or 0x0000 or 0x0F4F or 0x0FFF;
	}

	private static string FormatIndexedCounts(IReadOnlyList<int> counts)
		=> string.Join(
			",",
			counts
				.Select((count, index) => (Count: count, Index: index))
				.Where(item => item.Count != 0)
				.OrderByDescending(item => item.Count)
				.ThenBy(item => item.Index)
				.Take(16)
				.Select(item => $"{item.Index}:{item.Count}"));

	private static string FormatDisplayRegisterSummary(IReadOnlyList<CustomRegisterWrite> writes, byte[] chipRam)
	{
		var bpl1 = TryGetLastPointer(writes, 0x0E0, out var bpl1Pointer)
			? $"BPL1=0x{bpl1Pointer:X6}/{FormatChipRangeSummary(chipRam, bpl1Pointer, 40 * 32)}"
			: "BPL1=?";
		var bpl2 = TryGetLastPointer(writes, 0x0E4, out var bpl2Pointer)
			? $"BPL2=0x{bpl2Pointer:X6}/{FormatChipRangeSummary(chipRam, bpl2Pointer, 40 * 32)}"
			: "BPL2=?";

		return
			$"{bpl1}, {bpl2}, " +
			$"BPLCON0={FormatLastRegisterWrite(writes, 0x100)}, " +
			$"DIWSTRT={FormatLastRegisterWrite(writes, 0x08E)}, DIWSTOP={FormatLastRegisterWrite(writes, 0x090)}, " +
			$"DDFSTRT={FormatLastRegisterWrite(writes, 0x092)}, DDFSTOP={FormatLastRegisterWrite(writes, 0x094)}, " +
			$"BPL1MOD={FormatLastRegisterWrite(writes, 0x108)}, BPL2MOD={FormatLastRegisterWrite(writes, 0x10A)}, " +
			$"DMACON={FormatLastRegisterWrite(writes, 0x096)}";
	}

	private static bool TryGetLastPointer(IReadOnlyList<CustomRegisterWrite> writes, ushort highOffset, out uint pointer)
	{
		var high = writes.LastOrDefault(write => write.Address == highOffset);
		var low = writes.LastOrDefault(write => write.Address == highOffset + 2);
		if (high.Address != highOffset || low.Address != highOffset + 2)
		{
			pointer = 0;
			return false;
		}

		pointer = (((uint)high.Value << 16) | low.Value) & 0x00FF_FFFE;
		return true;
	}

	private static string FormatLastRegisterWrite(IReadOnlyList<CustomRegisterWrite> writes, ushort offset)
	{
		var write = writes.LastOrDefault(write => write.Address == offset);
		return write.Address == offset
			? $"0x{write.Value:X4}@{FormatBeam(write.Cycle)}"
			: "?";
	}

	private static string FormatChipRangeSummary(byte[] chipRam, uint address, int length)
	{
		if (chipRam.Length == 0)
		{
			return "empty-chip";
		}

		var mask = chipRam.Length - 1;
		var nonZero = 0;
		var firstOffsets = new List<string>();
		for (var i = 0; i < length; i++)
		{
			var value = chipRam[(int)(address + (uint)i) & mask];
			if (value == 0)
			{
				continue;
			}

			nonZero++;
			if (firstOffsets.Count < 12)
			{
				firstOffsets.Add($"+0x{i:X}=0x{value:X2}");
			}
		}

		return $"nz={nonZero}/{length}, first=[{string.Join(",", firstOffsets)}]";
	}

	private static string FormatCodeWords(Machine machine, uint startAddress, int wordCount)
	{
		startAddress &= 0x00FF_FFFE;
		return string.Join(
			" ",
			Enumerable.Range(0, wordCount)
				.Select(index =>
				{
					var address = (startAddress + (uint)(index * 2)) & 0x00FF_FFFE;
					var value = machine.Bus.ReadWord(address);
					return $"{address:X6}:{value:X4}";
				}));
	}

	private static string FormatBusAccesses(IEnumerable<AmigaBusAccessResult> accesses)
		=> string.Join("; ", accesses.Select(access =>
			$"0x{access.Request.Address & 0x00FF_FFFE:X6}/{access.Request.Size}/{access.Request.Kind} " +
			$"{FormatBeam(access.Request.RequestedCycle)}->{FormatBeam(access.GrantedCycle)}..{FormatBeam(access.CompletedCycle)}"));

	private static string FormatCustomRegisterReads(IEnumerable<CustomRegisterRead> reads)
		=> string.Join("; ", reads.Select(read =>
			$"0x{0x00DFF000 + read.Address:X6}/{read.Kind} value=0x{read.Value:X4} " +
			$"req={FormatBeam(read.RequestedCycle)} grant={FormatBeam(read.GrantedCycle)} " +
			$"sample={FormatBeam(read.SampleCycle)} done={FormatBeam(read.CompletedCycle)}"));

	private static string FormatProbe10ValueCandidates(byte[] chipRam)
	{
		var expected = new ushort[]
		{
			0x38C2, 0x38CC, 0x38D6, 0x38E0,
			0x000D, 0x0017, 0x0021, 0x002B,
			0x0035, 0x003F, 0x0049, 0x0053,
			0x005D, 0x0067, 0x0071, 0x007B
		};
		var candidates = new List<(int Offset, int Score, ushort[] Values)>();
		for (var offset = 0; offset <= chipRam.Length - (expected.Length * 2); offset += 2)
		{
			var values = new ushort[expected.Length];
			var score = 0;
			for (var i = 0; i < values.Length; i++)
			{
				var valueOffset = offset + (i * 2);
				var value = (ushort)((chipRam[valueOffset] << 8) | chipRam[valueOffset + 1]);
				values[i] = value;
				if (value == expected[i])
				{
					score += 10;
				}
				else if ((value & 0xFF00) is 0x3800 or 0x0000)
				{
					score += 3;
				}
				else if ((value & 0x8000) == 0 && (value & 0x00FF) <= 0xE4)
				{
					score++;
				}
			}

			if (score >= 28 && values.Any(value => value != 0))
			{
				candidates.Add((offset, score, values));
			}
		}

		return string.Join("; ", candidates
			.OrderByDescending(candidate => candidate.Score)
			.ThenBy(candidate => candidate.Offset)
			.Take(12)
			.Select(candidate =>
				$"0x{candidate.Offset:X5}/score={candidate.Score} [" +
				$"{string.Join(",", candidate.Values.Select(value => $"0x{value:X4}"))}]"));
	}

	private static string FormatVprobe2ValueCandidates(byte[] chipRam)
	{
		var candidates = new List<(int Offset, int Score, ushort[] Values)>();
		for (var offset = 0; offset <= chipRam.Length - 32; offset += 2)
		{
			var values = new ushort[16];
			var score = 0;
			for (var i = 0; i < values.Length; i++)
			{
				var valueOffset = offset + (i * 2);
				var value = (ushort)((chipRam[valueOffset] << 8) | chipRam[valueOffset + 1]);
				values[i] = value;
				if (value is 0x8000 or 0x8001)
				{
					score += 10;
				}
				else if ((value & 0xFF00) == 0x8000)
				{
					score += 3;
				}
			}

			if (score >= 80 && values.Any(value => value != 0))
			{
				candidates.Add((offset, score, values));
			}
		}

		return string.Join("; ", candidates
			.OrderByDescending(candidate => candidate.Score)
			.ThenBy(candidate => candidate.Offset)
			.Take(12)
			.Select(candidate =>
				$"0x{candidate.Offset:X5}/score={candidate.Score} [" +
				$"{string.Join(",", candidate.Values.Select(value => $"0x{value:X4}"))}]"));
	}

	private static string FormatVprobeInstructionRetirements(
		string relativePath,
		Machine machine,
		IEnumerable<M68kInstructionRetirementTrace> captured)
	{
		var steadyWrites = machine.Bus.CustomRegisterWrites
			.Where(write => write.Address == 0x180 && write.Value <= 0x000F)
			.OrderBy(write => write.Cycle)
			.ToArray();
		if (steadyWrites.Length < 8)
		{
			return $"TRACE {relativePath}: complete instruction ledger unavailable";
		}

		var startCycle = steadyWrites[^8].Cycle;
		var stopCycle = steadyWrites[^7].Cycle;
		var retirements = captured
			.Where(trace => trace.StartCycle >= startCycle && trace.StartCycle < stopCycle)
			.OrderBy(trace => trace.StartCycle)
			.ToArray();
		var elapsed = retirements.Sum(trace => trace.RetireCycle - trace.StartCycle);
		var gaps = retirements
			.Zip(retirements.Skip(1), (current, next) =>
				Math.Max(0, next.StartCycle - current.RetireCycle))
			.Sum();
		var entries = retirements
			.GroupBy(trace => (Pc: trace.ProgramCounter & 0x00FF_FFFF, trace.Opcode))
			.Select(group => new
			{
				group.Key.Pc,
				group.Key.Opcode,
				Count = group.Count(),
				Cycles = group.Sum(trace => trace.RetireCycle - trace.StartCycle)
			})
			.OrderByDescending(entry => entry.Cycles)
			.ThenBy(entry => entry.Pc)
			.Select(entry =>
				$"{entry.Pc:X6}/{entry.Opcode:X4}={entry.Count}/{entry.Cycles}");
		var lastSequenceStart = Array.FindLastIndex(
			steadyWrites,
			write => write.Value == 0x000F);
		var sequenceWrites = lastSequenceStart >= 0
			? steadyWrites.Skip(lastSequenceStart).ToArray()
			: [];
		var perIteration = sequenceWrites
			.Zip(sequenceWrites.Skip(1), (start, stop) =>
			{
				var window = captured
					.Where(trace => trace.StartCycle >= start.Cycle && trace.StartCycle < stop.Cycle)
					.ToArray();
				var grouped = window
					.GroupBy(trace => trace.ProgramCounter & 0x00FF_FFFF)
					.Select(group => new
					{
						Pc = group.Key,
						Count = group.Count(),
						Cycles = group.Sum(trace => trace.RetireCycle - trace.StartCycle)
					})
					.OrderByDescending(entry => entry.Cycles)
					.Take(8)
					.Select(entry => $"{entry.Pc:X6}={entry.Count}/{entry.Cycles}");
				var control = window
					.Where(trace =>
						(trace.Opcode & 0xF000) == 0x6000 ||
						(trace.Opcode & 0xF0F8) == 0x50C8)
					.GroupBy(trace => (
						Pc: trace.ProgramCounter & 0x00FF_FFFF,
						trace.Opcode,
						Cycles: trace.RetireCycle - trace.StartCycle))
					.OrderBy(entry => entry.Key.Pc)
					.ThenBy(entry => entry.Key.Cycles)
					.Select(entry =>
						$"{entry.Key.Pc:X6}/{entry.Key.Opcode:X4}/" +
						$"{entry.Key.Cycles}x{entry.Count()}");
				return
					$"{start.Value:X3}->{stop.Value:X3}/dt={stop.Cycle - start.Cycle}/" +
					$"n={window.Length}[" + string.Join(',', grouped) + "]" +
					$"/ctl[" + string.Join(',', control) + "]";
			});
		var firstIterationStartIndex = Array.FindLastIndex(steadyWrites, write => write.Value == 0x000F);
		var firstIterationRetirements = firstIterationStartIndex >= 0 &&
			firstIterationStartIndex + 1 < steadyWrites.Length
				? captured
					.Where(trace =>
						trace.StartCycle >= steadyWrites[firstIterationStartIndex].Cycle &&
						trace.StartCycle < steadyWrites[firstIterationStartIndex + 1].Cycle)
					.OrderBy(trace => trace.StartCycle)
					.Take(120)
					.Select(trace =>
						$"{FormatBeam(trace.StartCycle)}:" +
						$"{trace.ProgramCounter & 0x00FF_FFFF:X6}/" +
						$"{trace.Opcode:X4}/{trace.RetireCycle - trace.StartCycle}")
				: [];
		var firstIterationMovemTransitions = firstIterationStartIndex >= 0 &&
			firstIterationStartIndex + 1 < steadyWrites.Length
				? captured
					.Where(trace =>
						trace.StartCycle >= steadyWrites[firstIterationStartIndex].Cycle &&
						trace.StartCycle < steadyWrites[firstIterationStartIndex + 1].Cycle &&
						(trace.ProgramCounter & 0x00FF_FFFF) == 0x0701EC)
					.OrderBy(trace => trace.StartCycle)
					.Select(trace =>
					{
						var phases = machine.Bus.CpuBusPhases
							.Where(phase =>
								phase.CpuPhase.InstructionStartCycle == trace.StartCycle &&
								(phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF) == 0x0701EC)
							.OrderBy(phase => phase.CpuPhase.RequestedCycle)
							.ToArray();
						var entry = phases.FirstOrDefault().CpuPhase;
						return
							$"{FormatBeam(trace.StartCycle)}/dt={trace.RetireCycle - trace.StartCycle}/" +
							$"q={entry.InstructionEntryPrefetchCount}/" +
							$"entryBus={entry.InstructionEntryBusCycle - trace.StartCycle}/" +
							$"r0={entry.InstructionEntryReadyCycle0 - trace.StartCycle}/" +
							$"r1={entry.InstructionEntryReadyCycle1 - trace.StartCycle}/" +
							$"bus=[" + string.Join(',', phases.Select(phase =>
								$"{phase.CpuPhase.Address & 0x00FF_FFFF:X6}:" +
								$"{phase.CpuPhase.RequestedCycle - trace.StartCycle}->" +
								$"{phase.CpuPhase.CompletedCycle - trace.StartCycle}")) + "]";
					})
				: [];
		var firstIterationBsrTransitions = firstIterationStartIndex >= 0 &&
			firstIterationStartIndex + 1 < steadyWrites.Length
				? captured
					.Where(trace =>
						trace.StartCycle >= steadyWrites[firstIterationStartIndex].Cycle &&
						trace.StartCycle < steadyWrites[firstIterationStartIndex + 1].Cycle &&
						(trace.Opcode & 0xFF00) == 0x6100)
					.OrderBy(trace => trace.StartCycle)
					.Select(trace =>
					{
						var phases = machine.Bus.CpuBusPhases
							.Where(phase =>
								phase.CpuPhase.InstructionStartCycle == trace.StartCycle &&
								(phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF) ==
									(trace.ProgramCounter & 0x00FF_FFFF))
							.OrderBy(phase => phase.CpuPhase.RequestedCycle)
							.ToArray();
						var entry = phases.FirstOrDefault().CpuPhase;
						return
							$"{FormatBeam(trace.StartCycle)}/pc={trace.ProgramCounter & 0x00FF_FFFF:X6}/" +
							$"dt={trace.RetireCycle - trace.StartCycle}/" +
							$"q={entry.InstructionEntryPrefetchCount}/" +
							$"entryBus={entry.InstructionEntryBusCycle - trace.StartCycle}/" +
							$"r0={entry.InstructionEntryReadyCycle0 - trace.StartCycle}/" +
							$"r1={entry.InstructionEntryReadyCycle1 - trace.StartCycle}/" +
							$"bus=[" + string.Join(',', phases.Select(phase =>
								$"{phase.CpuPhase.Address & 0x00FF_FFFF:X6}:" +
								$"{phase.CpuPhase.RequestedCycle - trace.StartCycle}->" +
								$"{phase.CpuPhase.CompletedCycle - trace.StartCycle}")) + "]";
					})
				: [];
		return
			$"TRACE {relativePath}: complete instruction ledger " +
			$"window={startCycle}->{stopCycle}/delta={stopCycle - startCycle}/" +
			$"count={retirements.Length}/elapsed={elapsed}/gaps={gaps} " +
			string.Join(',', entries) +
			$"; per iteration {string.Join(';', perIteration)}" +
			$"; first sequence {string.Join(',', firstIterationRetirements)}" +
			$"; first MOVEM transitions {string.Join(';', firstIterationMovemTransitions)}" +
			$"; first BSR transitions {string.Join(';', firstIterationBsrTransitions)}";
	}

	private static string FormatExpiredDbraTransitions(string relativePath, Machine machine)
	{
		var transitions = machine.Bus.CpuBusPhases
			.GroupBy(phase => (
				phase.CpuPhase.InstructionStartCycle,
				Pc: phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF))
			.Where(group =>
			{
				var pc = group.Key.Pc;
				return pc + 1 < machine.Bus.ChipRam.Length &&
					(ReadChipRamWord(machine.Bus.ChipRam, pc) & 0xFFF8) == 0x51C8 &&
					group.Any(phase =>
						(phase.CpuPhase.Address & 0x00FF_FFFF) == ((pc + 4) & 0x00FF_FFFF));
			})
			.OrderBy(group => group.Key.InstructionStartCycle)
			.TakeLast(24)
			.Select(group =>
			{
				var first = group.First().CpuPhase;
				var phases = group
					.Select(phase =>
						$"{phase.CpuPhase.Address & 0x00FF_FFFF:X6}:" +
						$"{phase.CpuPhase.RequestedCycle - first.InstructionStartCycle}->" +
						$"{phase.CpuPhase.CompletedCycle - first.InstructionStartCycle}")
					.ToArray();
				return
					$"pc={group.Key.Pc:X6}/start={group.Key.InstructionStartCycle}/" +
					$"entryBus={first.InstructionEntryBusCycle - first.InstructionStartCycle}/" +
					$"q={first.InstructionEntryPrefetchCount}/" +
					$"r0={first.InstructionEntryReadyCycle0 - first.InstructionStartCycle}/" +
					$"r1={first.InstructionEntryReadyCycle1 - first.InstructionStartCycle}/" +
					$"phases=[{string.Join(',', phases)}]";
			});
		return $"TRACE {relativePath}: expired DBRA transitions {string.Join("; ", transitions)}";
	}

	private static string FormatProbe10LeaA2Targets(byte[] chipRam)
	{
		var entries = new List<string>();
		foreach (var target in FindProbe10ValueTargets(chipRam).Take(16))
		{
			var words = Enumerable.Range(0, 16)
				.Select(index =>
				{
					var wordOffset = target.Target + (index * 2);
					return (ushort)((chipRam[wordOffset] << 8) | chipRam[wordOffset + 1]);
				});
			entries.Add($"pc=0x{target.Leapc:X5}->0x{target.Target:X5} [{string.Join(",", words.Select(value => $"0x{value:X4}"))}]");
		}

		return string.Join("; ", entries);
	}

	private static IEnumerable<(int Leapc, int Target)> FindProbe10ValueTargets(byte[] chipRam)
	{
		for (var offset = 0; offset <= chipRam.Length - 4; offset += 2)
		{
			var opcode = (ushort)((chipRam[offset] << 8) | chipRam[offset + 1]);
			if (opcode != 0x45FA)
			{
				continue;
			}

			var displacement = unchecked((short)((chipRam[offset + 2] << 8) | chipRam[offset + 3]));
			var target = offset + 2 + displacement;
			if (target < 0 || target + 31 >= chipRam.Length)
			{
				continue;
			}

			yield return (offset, target);
		}
	}

	private static string FormatProbe10MeasuredWriteBursts(
		IReadOnlyList<AmigaCpuBusPhaseTrace> phases,
		byte[] chipRam)
	{
		var writes = phases
			.Where(phase =>
				phase.CpuPhase.IsWrite &&
				phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuDataWrite &&
				phase.CpuPhase.Size == M68kOperandSize.Word &&
				phase.CpuPhase.Address < chipRam.Length &&
				phase.BusAccess.HasValue &&
				phase.BusAccess.Value.Request.Target == AmigaBusAccessTarget.ChipRam)
			.OrderBy(phase => phase.CpuPhase.CompletedCycle)
			.ToArray();
		var bursts = new List<AmigaCpuBusPhaseTrace[]>();
		var current = new List<AmigaCpuBusPhaseTrace>();
		foreach (var write in writes)
		{
			if (current.Count != 0)
			{
				var previous = current[^1];
				var sequentialAddress = (previous.CpuPhase.Address + 2) == write.CpuPhase.Address;
				var nearbyCycle = write.CpuPhase.CompletedCycle - previous.CpuPhase.CompletedCycle <= 64;
				if (!sequentialAddress || !nearbyCycle)
				{
					if (current.Count >= 8)
					{
						bursts.Add(current.ToArray());
					}

					current.Clear();
				}
			}

			current.Add(write);
		}

		if (current.Count >= 8)
		{
			bursts.Add(current.ToArray());
		}

		return string.Join("; ", bursts
			.OrderByDescending(burst => burst.Length == 16 ? 1 : 0)
			.ThenBy(burst => burst[0].CpuPhase.CompletedCycle)
			.Take(12)
			.Select(burst =>
			{
				var address = (int)burst[0].CpuPhase.Address;
				var words = Enumerable.Range(0, Math.Min(16, burst.Length))
					.Select(index =>
					{
						var offset = address + (index * 2);
						return offset + 1 < chipRam.Length
							? (ushort)((chipRam[offset] << 8) | chipRam[offset + 1])
							: (ushort)0;
					});
				return $"0x{address:X5}/count={burst.Length}/pc=0x{burst[0].CpuPhase.InstructionProgramCounter & 0x00FF_FFFF:X6}/" +
					$"{FormatBeam(burst[0].CpuPhase.CompletedCycle)}..{FormatBeam(burst[^1].CpuPhase.CompletedCycle)} " +
					$"[{string.Join(",", words.Select(value => $"0x{value:X4}"))}]";
			}));
	}

	private static string FormatProbe10ValueWritePhases(
		IReadOnlyList<AmigaCpuBusPhaseTrace> phases,
		byte[] chipRam)
	{
		var targets = FindProbe10ValueTargets(chipRam)
			.Take(8)
			.ToArray();
		if (targets.Length == 0)
		{
			return "no LEA values targets";
		}

		var entries = new List<string>();
		foreach (var target in targets)
		{
			var writes = phases
				.Where(phase =>
					phase.CpuPhase.IsWrite &&
					phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuDataWrite &&
					phase.CpuPhase.Size == M68kOperandSize.Word &&
					phase.CpuPhase.Address >= target.Target &&
					phase.CpuPhase.Address < target.Target + 32)
				.OrderBy(phase => phase.CpuPhase.CompletedCycle)
				.ToArray();
			if (writes.Length == 0)
			{
				entries.Add($"pc=0x{target.Leapc:X5}->0x{target.Target:X5}: no retained writes");
				continue;
			}

			entries.Add(
				$"pc=0x{target.Leapc:X5}->0x{target.Target:X5}/count={writes.Length} " +
				$"{FormatCpuBusPhases(writes.Take(24))}");
		}

		return string.Join(" || ", entries);
	}

	private static string FormatProbe10WatchedValueWrites(
		IReadOnlyList<CpuChipRamWriteTrace> traces,
		byte[] chipRam)
	{
		var targets = FindProbe10ValueTargets(chipRam)
			.Take(8)
			.ToArray();
		if (targets.Length == 0)
		{
			return $"no LEA values targets; watchedWrites={traces.Count}";
		}

		var entries = new List<string>();
		foreach (var target in targets)
		{
			var writes = traces
				.Where(trace =>
					trace.Size == M68kOperandSize.Word &&
					trace.Address >= target.Target &&
					trace.Address < target.Target + 32)
				.OrderBy(trace => trace.Cycle)
				.ToArray();
			if (writes.Length == 0)
			{
				entries.Add($"pc=0x{target.Leapc:X5}->0x{target.Target:X5}: no watched writes");
				continue;
			}

			var first = writes.Take(20);
			var last = writes.Skip(Math.Max(0, writes.Length - 20));
			entries.Add(
				$"pc=0x{target.Leapc:X5}->0x{target.Target:X5}/count={writes.Length} " +
				$"first={FormatCpuChipRamWrites(first)} last={FormatCpuChipRamWrites(last)}");
		}

		return string.Join(" || ", entries);
	}

	private static string FormatProbe10WatchedVhposrReads(
		IReadOnlyList<CustomRegisterRead> reads,
		IReadOnlyList<CpuChipRamWriteTrace> writes,
		byte[] chipRam,
		ushort readOffset = 0x006)
	{
		var target = FindProbe10ValueTargets(chipRam)
			.Select(candidate => candidate.Target)
			.FirstOrDefault();
		if (target == 0)
		{
			return $"no LEA values target; watchedReads={reads.Count}";
		}

		var firstWriteCycle = writes
			.Where(write =>
				write.Size == M68kOperandSize.Word &&
				write.Address >= target &&
				write.Address < target + 32)
			.Select(write => (long?)write.Cycle)
			.Min();
		if (!firstWriteCycle.HasValue)
		{
			return $"no watched writes; watchedReads={reads.Count}";
		}

		var nearbyReads = reads
			.Where(read =>
				read.Address == readOffset &&
				read.SampleCycle >= firstWriteCycle.Value - 512 &&
				read.SampleCycle <= firstWriteCycle.Value + 128)
			.OrderBy(read => read.SampleCycle)
			.ToArray();
		if (nearbyReads.Length == 0)
		{
			return $"firstWrite={firstWriteCycle.Value}/{FormatBeam(firstWriteCycle.Value)}; no nearby watched reads; watchedReads={reads.Count}";
		}

		return $"firstWrite={firstWriteCycle.Value}/{FormatBeam(firstWriteCycle.Value)} count={nearbyReads.Length} " +
			string.Join(" ", nearbyReads.Select(read =>
				$"0x{read.Value:X4}@req{read.RequestedCycle}/{FormatBeam(read.RequestedCycle)}" +
				$" grant{read.GrantedCycle}/{FormatBeam(read.GrantedCycle)}" +
				$" sample{read.SampleCycle}/{FormatBeam(read.SampleCycle)}"));
	}

	private static string FormatCpuChipRamWrites(IEnumerable<CpuChipRamWriteTrace> traces)
	{
		return string.Join(" ", traces.Select(trace =>
			$"0x{trace.Address:X5}=0x{trace.Value:X4}@{trace.Cycle}/{FormatBeam(trace.Cycle)}"));
	}

	private static string FormatCycleBitCopperOperandWrites(IReadOnlyList<CpuChipRamWriteTrace> traces, byte[] chipRam)
	{
		if (!TryFindCycleCopperListBase(chipRam, out var copperBase))
		{
			return "copper-list-pattern-not-found";
		}

		var entries = new List<string>();
		for (var bit = 15; bit >= 0; bit--)
		{
			var address = GetCycleCopperBitOperandAddress(copperBase, bit);
			var writes = traces
				.Where(trace => trace.Size == M68kOperandSize.Word && trace.Address == address)
				.OrderBy(trace => trace.Cycle)
				.ToArray();
			if (writes.Length == 0)
			{
				entries.Add($"b{bit}@0x{address:X5}:none");
				continue;
			}

			entries.Add($"b{bit}@0x{address:X5}:{FormatCpuChipRamWrites(writes)}");
		}

		return string.Join(" | ", entries);
	}

	private static string FormatCycleBitCopperOperandPhases(IReadOnlyList<AmigaCpuBusPhaseTrace> phases, byte[] chipRam)
	{
		if (!TryFindCycleCopperListBase(chipRam, out var copperBase))
		{
			return "copper-list-pattern-not-found";
		}

		var entries = new List<string>();
		for (var bit = 15; bit >= 0; bit--)
		{
			var address = GetCycleCopperBitOperandAddress(copperBase, bit);
			var writes = phases
				.Where(phase =>
					phase.CpuPhase.IsWrite &&
					phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuDataWrite &&
					(phase.CpuPhase.Address & 0x00FF_FFFE) == address)
				.OrderBy(phase => phase.CpuPhase.RequestedCycle)
				.ToArray();
			if (writes.Length == 0)
			{
				entries.Add($"b{bit}@0x{address:X5}:none");
				continue;
			}

			entries.Add($"b{bit}@0x{address:X5}:{FormatCpuBusPhases(writes)}");
		}

		return string.Join(" | ", entries);
	}

	private static bool TryFindCycleCopperListBase(byte[] chipRam, out uint copperBase)
	{
		ReadOnlySpan<ushort> pattern =
		[
			0x4001, 0xFFFE,
			0x0180, 0x0F00,
			0x40D9, 0xFFFE,
			0x0180
		];

		for (var offset = 0; offset <= chipRam.Length - (pattern.Length * 2); offset += 2)
		{
			var matches = true;
			for (var i = 0; i < pattern.Length; i++)
			{
				var wordOffset = offset + (i * 2);
				var value = (ushort)((chipRam[wordOffset] << 8) | chipRam[wordOffset + 1]);
				if (value != pattern[i])
				{
					matches = false;
					break;
				}
			}

			if (matches)
			{
				copperBase = (uint)offset;
				return true;
			}
		}

		copperBase = 0;
		return false;
	}

	private static uint GetCycleCopperBitOperandAddress(uint copperBase, int bit)
	{
		const int bit15OperandOffset = 14;
		return copperBase + bit15OperandOffset + (uint)((15 - bit) * 16);
	}

	private static string FormatVhposrReadBursts(IReadOnlyList<CustomRegisterRead> reads, ushort readOffset = 0x006)
	{
		var vhposrReads = reads
			.Where(read => read.Address == readOffset && read.Kind == AmigaBusAccessKind.CpuDataRead)
			.OrderBy(read => read.SampleCycle)
			.ToArray();
		var bursts = new List<CustomRegisterRead[]>();
		var current = new List<CustomRegisterRead>();
		foreach (var read in vhposrReads)
		{
			if (current.Count != 0 && read.SampleCycle - current[^1].SampleCycle > 44)
			{
				if (current.Count >= 4)
				{
					bursts.Add(current.ToArray());
				}

				current.Clear();
			}

			current.Add(read);
		}

		if (current.Count >= 4)
		{
			bursts.Add(current.ToArray());
		}

		return string.Join("; ", bursts
			.OrderBy(burst => burst.Length is >= 10 and <= 24 ? 0 : 1)
			.ThenBy(burst => burst[0].SampleCycle)
			.Take(12)
			.Select(burst =>
		{
			var values = burst
				.Take(20)
				.Select(read => $"0x{read.Value:X4}@{FormatBeam(read.SampleCycle)}");
			return $"count={burst.Length} [{string.Join(", ", values)}]";
		}));
	}

	private static string FormatCpuBusPhases(IEnumerable<AmigaCpuBusPhaseTrace> phases)
		=> string.Join("; ", phases.Select(phase =>
		{
			var cpu = phase.CpuPhase;
			var bus = phase.BusAccess.HasValue
				? $"{FormatBeam(phase.BusAccess.Value.RequestedCycle)}->{FormatBeam(phase.BusAccess.Value.GrantedCycle)}..{FormatBeam(phase.BusAccess.Value.CompletedCycle)}"
				: "no-bus";
			var slot = phase.GrantedSlot.HasValue
				? $"slot={FormatBeam(phase.GrantedSlot.Value.GrantedCycle)}"
				: "slot=none";
			return $"pc=0x{cpu.InstructionProgramCounter & 0x00FF_FFFF:X6} " +
				$"{(cpu.IsWrite ? "W" : "R")} 0x{cpu.Address & 0x00FF_FFFE:X6}/{cpu.Size}/{cpu.AccessKind} " +
				$"ins={FormatBeam(cpu.InstructionStartCycle)}/entry={FormatBeam(cpu.InstructionEntryBusCycle)}/" +
				$"q{cpu.InstructionEntryPrefetchCount}@{cpu.InstructionEntryReadyCycle0},{cpu.InstructionEntryReadyCycle1} " +
				$"cpu={FormatBeam(cpu.RequestedCycle)}..{FormatBeam(cpu.CompletedCycle)} bus={bus} second={FormatBeam(phase.SecondWordCycle)} {slot}";
		}));

	private static IEnumerable<AmigaCpuBusPhaseTrace> GetCpuPhasesInBeamWindow(
		IReadOnlyList<AmigaCpuBusPhaseTrace> phases,
		long frameStartCycle,
		int startLine,
		int startHorizontal,
		int endLine,
		int endHorizontal)
	{
		if (frameStartCycle < 0)
		{
			return [];
		}

		var startCycle = frameStartCycle + (startLine * AmigaConstants.A500PalCpuCyclesPerRasterLine) + (startHorizontal * AmigaConstants.A500PalCpuCyclesPerColorClock);
		var endCycle = frameStartCycle + (endLine * AmigaConstants.A500PalCpuCyclesPerRasterLine) + (endHorizontal * AmigaConstants.A500PalCpuCyclesPerColorClock);
		return phases
			.Where(phase =>
				phase.CpuPhase.RequestedCycle >= startCycle &&
				phase.CpuPhase.RequestedCycle <= endCycle)
			.OrderBy(phase => phase.CpuPhase.RequestedCycle);
	}

	private static string FormatCpuBusPhaseReads(
		IEnumerable<AmigaCpuBusPhaseTrace> phases,
		IReadOnlyList<CustomRegisterRead> reads)
		=> string.Join("; ", phases.Select(phase =>
		{
			var cpu = phase.CpuPhase;
			var read = FindCustomRegisterRead(reads, phase);
			var value = read.HasValue
				? $"value=0x{read.Value.Value:X4} sample={FormatBeam(read.Value.SampleCycle)}"
				: "value=?";
			return $"pc=0x{cpu.InstructionProgramCounter & 0x00FF_FFFF:X6} " +
				$"0x{cpu.Address & 0x00FF_FFFE:X6}/{cpu.AccessKind} {value} " +
				$"cpu={FormatBeam(cpu.RequestedCycle)}..{FormatBeam(cpu.CompletedCycle)}";
		}));

	private static string FormatBeamReadsByProgramCounter(
		IReadOnlyList<AmigaCpuBusPhaseTrace> phases,
		IReadOnlyList<CustomRegisterRead> reads)
	{
		var groups = phases
			.Where(phase => phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuDataRead)
			.GroupBy(phase => phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF)
			.OrderBy(group => group.Count() is >= 16 and <= 20 ? 0 : 1)
			.ThenByDescending(group => group.Count())
			.Take(18)
			.Select(group =>
			{
				var selected = group.Take(8).Concat(group.Skip(Math.Max(0, group.Count() - 8))).ToArray();
				var values = selected
					.Select(phase =>
					{
						var read = FindCustomRegisterRead(reads, phase);
						return read.HasValue
							? $"0x{read.Value.Value:X4}@{FormatBeam(read.Value.SampleCycle)}"
							: $"?@{FormatBeam(phase.CpuPhase.RequestedCycle)}";
					});
				return $"pc=0x{group.Key:X6}/count={group.Count()} [{string.Join(", ", values)}]";
			});

		return string.Join("; ", groups);
	}

	private static AmigaCpuBusPhaseTrace[] GetSyncCpuVhposReads(
		IEnumerable<AmigaCpuBusPhaseTrace> phases,
		uint instructionProgramCounter)
		=> phases
			.Where(phase =>
				(phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF) == instructionProgramCounter &&
				phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuDataRead &&
				(phase.CpuPhase.Address & 0x00FF_FFFE) == 0x00DFF006)
			.ToArray();

	private static string FormatSyncCpuStageSummary(
		IReadOnlyList<AmigaCpuBusPhaseTrace> phases,
		IReadOnlyList<CustomRegisterRead> reads)
	{
		var lowWrap = phases
			.Where(phase =>
			{
				var read = FindCustomRegisterRead(reads, phase);
				if (!read.HasValue)
				{
					return false;
				}

				var low = read.Value.Value & 0x00FF;
				return low <= 0x08 || low >= 0xF8;
			})
			.ToArray();

		return $"count={phases.Count}, " +
			$"first={FormatCpuBusPhaseReads(phases.Take(12), reads)}, " +
			$"last={FormatCpuBusPhaseReads(phases.Skip(Math.Max(0, phases.Count - 12)), reads)}, " +
			$"lowWrap={FormatCpuBusPhaseReads(lowWrap, reads)}";
	}

	private static string FormatSyncCpuExitTransitions(
		IReadOnlyList<AmigaCpuBusPhaseTrace> phases,
		IReadOnlyList<CustomRegisterRead> reads,
		IReadOnlyList<CustomRegisterWrite> syncColorWrites,
		ushort mask)
	{
		var exits = phases
			.Select(phase => (Phase: phase, Read: FindCustomRegisterRead(reads, phase)))
			.Where(item => item.Read.HasValue && (item.Read.Value.Value & mask) == 0)
			.ToArray();
		if (exits.Length == 0)
		{
			return "none";
		}

		var selected = exits.Length <= 16
			? exits
			: exits[..8].Concat(exits[^8..]).ToArray();
		var text = string.Join("; ", selected.Select(item =>
		{
			var phase = item.Phase.CpuPhase;
			var read = item.Read.GetValueOrDefault();
			var nextWrite = syncColorWrites.FirstOrDefault(write => write.Cycle >= phase.CompletedCycle);
			var nextWriteText = nextWrite.Equals(default(CustomRegisterWrite))
				? "nextColor=none"
				: $"nextColor={FormatBeam(nextWrite.Cycle)}=0x{nextWrite.Value:X4}/delta={nextWrite.Cycle - phase.CompletedCycle}";
			return $"pc=0x{phase.InstructionProgramCounter & 0x00FF_FFFF:X6} " +
				$"read=0x{read.Value:X4} sample={FormatBeam(read.SampleCycle)} " +
				$"cpuDone={FormatBeam(phase.CompletedCycle)} {nextWriteText}";
		}));
		return $"count={exits.Length}, {text}";
	}

	private static string FormatStripeToSyncStarts(
		IReadOnlyList<AmigaCpuBusPhaseTrace> phases,
		long frameStartCycle)
	{
		var syncWrite = phases.FirstOrDefault(phase =>
			(phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF) == 0x0702D4 &&
			phase.CpuPhase.IsWrite &&
			phase.CpuPhase.RequestedCycle >= frameStartCycle);
		if (syncWrite.CpuPhase.RequestedCycle == 0)
		{
			return "none";
		}

		uint[] programCounters =
		[
			0x0702B4, 0x0702B8, 0x070208, 0x0702BC,
			0x0702C0, 0x0702C2, 0x0702C6, 0x0702CA,
			0x0702CC, 0x0702D2, 0x0702D4
		];
		return string.Join("; ", programCounters.Select(programCounter =>
		{
			var phase = phases.LastOrDefault(candidate =>
				(candidate.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF) == programCounter &&
				candidate.CpuPhase.RequestedCycle <= syncWrite.CpuPhase.RequestedCycle);
			if (phase.CpuPhase.RequestedCycle == 0)
			{
				return $"pc=0x{programCounter:X6}/missing";
			}

			var cpu = phase.CpuPhase;
			return $"pc=0x{programCounter:X6}/i={FormatBeam(cpu.InstructionStartCycle)}/" +
				$"entry={FormatBeam(cpu.InstructionEntryBusCycle)}/q{cpu.InstructionEntryPrefetchCount}/" +
				$"r={FormatBeam(cpu.InstructionEntryReadyCycle0)},{FormatBeam(cpu.InstructionEntryReadyCycle1)}/" +
				$"a=0x{cpu.Address & 0x00FF_FFFF:X6}/req={FormatBeam(cpu.RequestedCycle)}";
		}));
	}

	private static string FormatLateTransitionPhases(
		IReadOnlyList<AmigaCpuBusPhaseTrace> phases,
		IReadOnlyList<CustomRegisterRead> reads,
		long frameStartCycle)
	{
		var interesting = phases
			.Where(phase =>
			{
				var pc = phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF;
				return pc >= 0x0702C0 && pc <= 0x0702E4;
			})
			.ToArray();
		if (interesting.Length == 0)
		{
			return "none";
		}

		var anchors = interesting
			.Where(phase =>
			{
				var pc = phase.CpuPhase.InstructionProgramCounter & 0x00FF_FFFF;
				return phase.CpuPhase.IsWrite &&
					(phase.CpuPhase.Address & 0x00FF_FFFE) == 0x00DFF180 &&
					(pc == 0x0702D4 || pc == 0x0702E0);
			})
			.ToArray();
		if (anchors.Length == 0)
		{
			anchors = interesting
				.Where(phase => phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuDataRead)
				.TakeLast(4)
				.ToArray();
		}

		return string.Join(" | ", anchors.Take(8).Select(anchor =>
		{
			var anchorCycle = anchor.CpuPhase.RequestedCycle;
			var window = interesting
				.Where(phase => Math.Abs(phase.CpuPhase.RequestedCycle - anchorCycle) <= 180)
				.OrderBy(phase => phase.CpuPhase.RequestedCycle)
				.ToArray();
			return $"anchor={FormatLatePhase(anchor, reads, frameStartCycle)} seq=[{string.Join("; ", window.Select(phase => FormatLatePhase(phase, reads, frameStartCycle)))}]";
		}));
	}

	private static string FormatLatePhase(
		AmigaCpuBusPhaseTrace phase,
		IReadOnlyList<CustomRegisterRead> reads,
		long frameStartCycle)
	{
		var cpu = phase.CpuPhase;
		var pc = cpu.InstructionProgramCounter & 0x00FF_FFFF;
		var placement = frameStartCycle >= 0
			? CalculatePresentationPlacement(frameStartCycle, cpu.RequestedCycle)
			: (Line: -1, Horizontal: -1, Row: -1, LowResX: -1, RawX: -1);
		var read = !cpu.IsWrite ? FindCustomRegisterRead(reads, phase) : null;
		var value = read.HasValue
			? $" value=0x{read.Value.Value:X4} sample={FormatBeam(read.Value.SampleCycle)}"
			: string.Empty;
		return $"pc=0x{pc:X6} {(cpu.IsWrite ? "W" : "R")} 0x{cpu.Address & 0x00FF_FFFE:X6}/{cpu.AccessKind} " +
			$"req={FormatBeam(cpu.RequestedCycle)} done={FormatBeam(cpu.CompletedCycle)} row={placement.Row} xRaw={placement.RawX}{value}";
	}

	private static CustomRegisterRead? FindCustomRegisterRead(
		IReadOnlyList<CustomRegisterRead> reads,
		AmigaCpuBusPhaseTrace phase)
	{
		var address = (ushort)(phase.CpuPhase.Address & 0x01FE);
		var requestedCycle = phase.BusAccess?.RequestedCycle ?? phase.CpuPhase.RequestedCycle;
		for (var i = reads.Count - 1; i >= 0; i--)
		{
			var read = reads[i];
			if (read.Address == address &&
				read.RequestedCycle == requestedCycle &&
				read.Kind == ToAmigaBusAccessKind(phase.CpuPhase.AccessKind))
			{
				return read;
			}
		}

		return null;
	}

	private static AmigaBusAccessKind ToAmigaBusAccessKind(M68kBusAccessKind kind)
		=> kind switch
		{
			M68kBusAccessKind.CpuInstructionFetch => AmigaBusAccessKind.CpuInstructionFetch,
			M68kBusAccessKind.CpuDataRead => AmigaBusAccessKind.CpuDataRead,
			M68kBusAccessKind.CpuDataWrite => AmigaBusAccessKind.CpuDataWrite,
			_ => AmigaBusAccessKind.CpuDataRead
		};

	private static bool IsBeamRegisterAddress(uint address)
	{
		var normalized = address & 0x00FF_FFFE;
		return normalized == 0x00DFF004 || normalized == 0x00DFF006;
	}

	private static string FormatInterruptDispatches(IEnumerable<InterruptDispatchTrace> dispatches)
		=> string.Join(
			"; ",
			dispatches.Select(dispatch =>
				$"l{dispatch.Level}/bits=0x{dispatch.ActiveInterruptBits:X4}/" +
				$"visible={FormatBeam(dispatch.CpuVisibleCycle)}/" +
				$"sample={FormatBeam(dispatch.CpuSampleCycle)}/" +
				$"accept={FormatBeam(dispatch.AcceptanceCycle)}/" +
				$"entry={FormatBeam(dispatch.EntryCompletedCycle)}/" +
				$"pc=0x{dispatch.InterruptedProgramCounter & 0x00FF_FFFF:X6}->" +
				$"0x{dispatch.HandlerProgramCounter & 0x00FF_FFFF:X6}/" +
				$"sr=0x{dispatch.SavedStatusRegister:X4}/" +
				$"d3=0x{dispatch.DataRegister3:X8}/" +
				$"pf={FormatInterruptPrefetch(dispatch.PrefetchBefore, dispatch.CpuVisibleCycle)}=>" +
				$"{FormatInterruptPrefetch(dispatch.PrefetchAfter, dispatch.CpuVisibleCycle)}"));

	private static string FormatInterruptPrefetch(M68000PrefetchDiagnosticState? state, long originCycle)
	{
		if (!state.HasValue)
		{
			return "n/a";
		}

		var value = state.Value;
		return $"pc{value.ProgramCounter & 0x00FF_FFFF:X6}/cy{value.Cycles - originCycle}/" +
			$"q{value.PrefetchCount}@{value.PrefetchAddress & 0x00FF_FFFF:X6}/" +
			$"w{value.Word0:X4},{value.Word1:X4}/" +
			$"r{value.ReadyCycle0 - originCycle},{value.ReadyCycle1 - originCycle}/" +
			$"b{value.BusCycle - originCycle}/ret{value.RetireBusCycle - originCycle}/" +
			$"fence{value.ExceptionEntryNotBeforeCycle - originCycle}/" +
			$"tail{value.InterruptEntryBranchTailAdjustmentCycles}/" +
			$"branch={value.BranchInterruptBoundaryCycle - originCycle}," +
			$"{value.BranchInterruptRequestedCycle - originCycle}," +
			$"{value.BranchInterruptRetireCycle - originCycle}/" +
			$"entryq{value.BranchInterruptEntryPrefetchCount}@" +
			$"{value.BranchInterruptEntryBusCycle - originCycle}," +
			$"{value.BranchInterruptEntryReadyCycle0 - originCycle}," +
			$"{value.BranchInterruptInstructionEntryCycle - originCycle}/" +
			$"entrytok={value.BranchInterruptEntryTimingToken0}," +
			$"{value.BranchInterruptEntryTimingToken1}/" +
			$"pred={value.BranchInterruptPredecessorToken}/" +
			$"ircgap={value.BranchInterruptRequiresIrcGap}/" +
			$"deferred={value.BranchInterruptCapturedDuringDeferredBatch}/" +
			$"control={value.BranchInterruptPendingInternalCycles}/" +
			$"ipl={value.IplPipelineState.PinLevel}@{value.IplPipelineState.PinChangeCycle - originCycle}," +
			$"prev{value.IplPipelineState.PreviousPinLevel}@{value.IplPipelineState.PreviousPinChangeCycle - originCycle}," +
			$"lat{value.IplPipelineState.LatchedLevel},sec{value.IplPipelineState.SecondaryLevel}," +
			$"eval{value.IplPipelineState.EvaluatedSampleLevel}@{value.IplPipelineState.EvaluatedSampleCycle - originCycle}/" +
			$"lastsample{value.LastInterruptSampleCycle - originCycle}/" +
			$"pending={(value.HasPendingPrefetch ? $"0x{value.PendingPrefetchAddress & 0x00FF_FFFF:X6}@{value.PendingPrefetchEarliestCycle - originCycle}" : "none")}";
	}

	private static string FormatProbe10Irq1FirstReadPhases(
		Machine machine,
		IEnumerable<InterruptDispatchTrace> dispatches)
	{
		var dispatchArray = dispatches.Take(2).ToArray();
		return string.Join(" | ", dispatchArray.Select(dispatch =>
		{
			var firstRead = machine.Bus.CustomRegisterReadTrace.FirstOrDefault(read =>
				read.Kind == AmigaBusAccessKind.CpuDataRead &&
				read.Address == 0x006 &&
				read.SampleCycle >= dispatch.EntryCompletedCycle &&
				read.SampleCycle <= dispatch.EntryCompletedCycle + 128);
			if (firstRead.Address != 0x006)
			{
				return $"visible={dispatch.CpuVisibleCycle}:no-vhposr";
			}

			var window = machine.InterruptBusPhaseTrace.FirstOrDefault(candidate =>
				candidate.Level == dispatch.Level &&
				candidate.AcceptanceCycle == dispatch.AcceptanceCycle);
			var phases = window?.Phases.Where(phase =>
				phase.CpuPhase.RequestedCycle <= firstRead.CompletedCycle) ?? [];
			return $"visible={dispatch.CpuVisibleCycle}/entry+{dispatch.EntryCompletedCycle - dispatch.CpuVisibleCycle}/" +
				$"sample+{firstRead.SampleCycle - dispatch.CpuVisibleCycle}:" + FormatCpuBusPhases(phases);
		}));
	}

	private static string FormatBeam(long cycle)
	{
		var frameCycle = Math.Max(0, cycle % AmigaConstants.A500PalCpuCyclesPerFrame);
		var line = frameCycle / AmigaConstants.A500PalCpuCyclesPerRasterLine;
		var lineCycle = frameCycle % AmigaConstants.A500PalCpuCyclesPerRasterLine;
		var hpos = lineCycle / AmigaConstants.A500PalCpuCyclesPerColorClock;
		return $"{cycle}@v{line:D3}h{hpos:D3}";
	}

	private static long GetPalBeamLine(long cycle)
	{
		var frameCycle = Math.Max(0, cycle % AmigaConstants.A500PalCpuCyclesPerFrame);
		return frameCycle / AmigaConstants.A500PalCpuCyclesPerRasterLine;
	}

	private static long GetPalBeamHorizontal(long cycle)
	{
		var frameCycle = Math.Max(0, cycle % AmigaConstants.A500PalCpuCyclesPerFrame);
		var lineCycle = frameCycle % AmigaConstants.A500PalCpuCyclesPerRasterLine;
		return lineCycle / AmigaConstants.A500PalCpuCyclesPerColorClock;
	}

	private static void AddAdfsFromDirectory(string root, string directory, List<string> cases)
	{
		foreach (var path in Directory.EnumerateFiles(directory, "*.adf", SearchOption.AllDirectories))
		{
			cases.Add(NormalizeCasePath(Path.GetRelativePath(root, path)));
		}
	}

	private static string? ResolveVAmigaTsRoot()
	{
		var configured = Environment.GetEnvironmentVariable(RootEnvironmentVariable);
		if (!string.IsNullOrWhiteSpace(configured))
		{
			var fullPath = Path.GetFullPath(configured);
			Assert.True(Directory.Exists(fullPath), $"{RootEnvironmentVariable} does not exist: {fullPath}");
			return fullPath;
		}

		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory != null)
		{
			var candidate = Path.Combine(directory.FullName, "third_party", "vAmigaTS");
			if (Directory.Exists(candidate))
			{
				return candidate;
			}

			directory = directory.Parent;
		}

		return null;
	}

	private static bool IsFatalBootStatus(string status)
		=> status.Contains("AMIGA_BOOT_UNSUPPORTED_OPCODE", StringComparison.Ordinal) ||
			status.Contains("AMIGA_BOOT_FAULT", StringComparison.Ordinal) ||
			status.Contains("AMIGA_BOOT_PROTECTED_DISK_UNSUPPORTED", StringComparison.Ordinal) ||
			status.Contains("AMIGA_BOOT_NULL_PC", StringComparison.Ordinal) ||
			status.Contains("AMIGA_BOOT_DOS_WORKBENCH_MEDIA_INCOMPLETE", StringComparison.Ordinal);

	private static int CountNonBlackPixels(ReadOnlySpan<int> framebuffer)
	{
		var count = 0;
		for (var i = 0; i < framebuffer.Length; i++)
		{
			if ((framebuffer[i] & 0x00FF_FFFF) != 0)
			{
				count++;
			}
		}

		return count;
	}

	private static int CountDistinctColors(ReadOnlySpan<int> framebuffer, int stopAt)
	{
		var colors = new HashSet<int>();
		for (var i = 0; i < framebuffer.Length; i++)
		{
			colors.Add(framebuffer[i]);
			if (colors.Count >= stopAt)
			{
				return colors.Count;
			}
		}

		return colors.Count;
	}

	private static string NormalizeRelativePath(string path)
		=> path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

	private static string NormalizeCasePath(string path)
		=> path.Replace('\\', '/').TrimStart('/');

	private static bool IsEnabled(string? value)
		=> value != null &&
			(value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
				value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
				value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
				value.Equals("on", StringComparison.OrdinalIgnoreCase));

	private sealed class Cycle01vBootProgressTrace
	{
		private const int PayloadStart = 0x00070000;
		private const int PayloadProbeLength = 256;
		private readonly Machine _machine;
		private readonly List<string> _transitions = [];
		private string? _lastRegion;
		private bool _payloadObserved;

		public Cycle01vBootProgressTrace(Machine machine)
		{
			_machine = machine;
		}

		public void Capture(int frame)
		{
			var pc = _machine.Cpu.State.ProgramCounter & 0x00FF_FFFF;
			var region = ClassifyProgramCounter(pc);
			var payloadNonZero = CountPayloadNonZeroBytes();
			var payloadLoaded = payloadNonZero > 0;
			if (_transitions.Count == 0 ||
				!string.Equals(region, _lastRegion, StringComparison.Ordinal) ||
				(payloadLoaded && !_payloadObserved))
			{
				_transitions.Add(FormatState(frame, region, payloadNonZero));
			}

			_lastRegion = region;
			_payloadObserved |= payloadLoaded;
		}

		public string Format()
		{
			var disk = _machine.Bus.Disk.CaptureSnapshot();
			return $"transitions=[{string.Join(" | ", _transitions)}]," +
				$"disk=xfer{disk.TransferCount}/lastWords{disk.LastTransferWords}/" +
				$"lastAddr0x{disk.LastTransferAddress:X6}/cyl{disk.Cylinder}.{disk.Head}/" +
				$"motor{disk.MotorOn}/selected{disk.Selected}/dma{disk.ActiveDma}," +
				$"vectors=resetSp0x{ReadChipLong(0):X8}/resetPc0x{ReadChipLong(4):X8}/" +
				$"irq3=0x{ReadChipLong(0x6C):X8}";
		}

		private string FormatState(int frame, string region, int payloadNonZero)
		{
			var state = _machine.Cpu.State;
			var disk = _machine.Bus.Disk.CaptureSnapshot();
			return $"f{frame}:{region}/pc0x{state.ProgramCounter & 0x00FF_FFFF:X6}/" +
				$"last0x{state.LastInstructionProgramCounter & 0x00FF_FFFF:X6}/" +
				$"sr0x{state.StatusRegister:X4}/sp0x{state.A[7]:X8}/cy{state.Cycles}/" +
				$"payloadNz{payloadNonZero}/xfer{disk.TransferCount}/" +
				$"lastAddr0x{disk.LastTransferAddress:X6}/cyl{disk.Cylinder}.{disk.Head}";
		}

		private string ClassifyProgramCounter(uint pc)
		{
			if (pc < _machine.Bus.ChipRam.Length)
			{
				return "chip";
			}

			return pc >= 0x00F80000 ? "rom" : "unmapped";
		}

		private int CountPayloadNonZeroBytes()
		{
			var chipRam = _machine.Bus.ChipRam;
			if (PayloadStart >= chipRam.Length)
			{
				return 0;
			}

			var stop = Math.Min(chipRam.Length, PayloadStart + PayloadProbeLength);
			var count = 0;
			for (var address = PayloadStart; address < stop; address++)
			{
				if (chipRam[address] != 0)
				{
					count++;
				}
			}

			return count;
		}

		private uint ReadChipLong(int address)
		{
			var chipRam = _machine.Bus.ChipRam;
			if (address < 0 || address + 3 >= chipRam.Length)
			{
				return 0;
			}

			return ((uint)chipRam[address] << 24) |
				((uint)chipRam[address + 1] << 16) |
				((uint)chipRam[address + 2] << 8) |
				chipRam[address + 3];
		}
	}

	private sealed class Cycle01vDelayedFetchSlotTrace
	{
		private const uint TargetFetchAddress = 0x0007035C;
		private const int MaxWindows = 16;
		private readonly AmigaBus _bus;
		private readonly Dictionary<long, int> _waitHistogram = [];
		private readonly Queue<string> _windows = new(MaxWindows);
		private int _targetFetches;
		private int _delayedFetches;

		public Cycle01vDelayedFetchSlotTrace(AmigaBus bus)
		{
			_bus = bus;
		}

		public void Capture(AgnusSlotScheduleAuditEntry entry)
		{
			if (entry.Requester != AmigaBusRequester.Cpu ||
				entry.Kind != AmigaBusAccessKind.CpuInstructionFetch ||
				(entry.Address & 0x00FF_FFFF) != TargetFetchAddress)
			{
				return;
			}

			_targetFetches++;
			var wait = entry.SlotCycle - entry.RequestedCycle;
			_waitHistogram[wait] = _waitHistogram.GetValueOrDefault(wait) + 1;
			if (wait <= 0)
			{
				return;
			}

			_delayedFetches++;
			var slots = new List<string>();
			var firstSlot = AgnusChipSlotScheduler.AlignToSlot(entry.RequestedCycle);
			for (var cycle = firstSlot;
				cycle <= entry.SlotCycle;
				cycle += AgnusChipSlotScheduler.SlotCycles)
			{
				_bus.TryGetCommittedAgnusSlotOwner(cycle, out var owner);
				var horizontal = AgnusHrmOcsSlotTable.GetHorizontal(cycle);
				var cpuCandidate = AgnusHrmOcsSlotTable.IsCpuAccessibleSlot(cycle) ? "cpu" : "dma";
				slots.Add($"+{cycle - entry.RequestedCycle}/h{horizontal:D3}:{cpuCandidate}/{owner}");
			}

			var requestedBeam = _bus.GetBeamPosition(entry.RequestedCycle);
			var grantedBeam = _bus.GetBeamPosition(entry.SlotCycle);
			var window =
				$"req={entry.RequestedCycle}@f{requestedBeam.FrameNumber}v{requestedBeam.BeamLine:D3}h{requestedBeam.BeamHorizontal:D3}," +
				$"grant={entry.SlotCycle}@f{grantedBeam.FrameNumber}v{grantedBeam.BeamLine:D3}h{grantedBeam.BeamHorizontal:D3}," +
				$"wait={wait},slots=[{string.Join(',', slots)}]";
			if (_windows.Count == MaxWindows)
			{
				_windows.Dequeue();
			}

			_windows.Enqueue(window);
		}

		public string Format()
		{
			var histogram = string.Join(",", _waitHistogram
				.OrderBy(pair => pair.Key)
				.Select(pair => $"{pair.Key}:{pair.Value}"));
			return $"targetFetches={_targetFetches},delayed={_delayedFetches}," +
				$"waitHistogram=[{histogram}],last=[{string.Join(" | ", _windows)}]";
		}
	}

	private readonly record struct RawComparisonResult(string? Failure, IReadOnlyList<string> Diagnostics)
	{
		public static RawComparisonResult Pass { get; } = new(null, []);
	}

	private sealed record VAmigaTsCaseResult(
		string RelativePath,
		int Frames,
		string Status,
		int BestNonBlack,
		int BestDistinctColors,
		string? Failure,
		IReadOnlyList<string> Diagnostics)
	{
		public string Summary =>
			$"{(Failure == null ? "PASS" : "FAIL")} {RelativePath}: frames={Frames}, status='{Status}', " +
			$"nonBlack={BestNonBlack}, colors={BestDistinctColors}" +
			(Failure == null ? string.Empty : $", failure='{Failure}'");

		public static VAmigaTsCaseResult Pass(
			string relativePath,
			int frames,
			string status,
			int bestNonBlack,
			int bestDistinctColors,
			IReadOnlyList<string> diagnostics)
			=> new(relativePath, frames, status, bestNonBlack, bestDistinctColors, null, diagnostics);

		public static VAmigaTsCaseResult Fail(
			string relativePath,
			int frames,
			string status,
			int bestNonBlack,
			int bestDistinctColors,
			string failure,
			IReadOnlyList<string> diagnostics)
			=> new(relativePath, frames, status, bestNonBlack, bestDistinctColors, failure, diagnostics);
	}
}
