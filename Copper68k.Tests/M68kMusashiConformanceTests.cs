using System.Text;
using Copper68k;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Copper68k.Tests;

public sealed class M68kMusashiConformanceTests
{
	private const string RunVariable = "COPPER68K_RUN_MUSASHI_M68000";
	private const string PathVariable = "COPPER68K_MUSASHI_M68000_PATH";
	private const string FilterVariable = "COPPER68K_MUSASHI_M68000_FILTER";
	private const string LimitVariable = "COPPER68K_MUSASHI_M68000_LIMIT";
	private const string MaxInstructionsVariable = "COPPER68K_MUSASHI_M68000_MAX_INSTRUCTIONS";
	private const string CpuModelVariable = "COPPER68K_MUSASHI_M68000_CPU_MODEL";
	private const string BackendVariable = "COPPER68K_MUSASHI_M68000_BACKEND";
	private const string IncludeKnownIncompatibleVariable = "COPPER68K_MUSASHI_M68000_INCLUDE_KNOWN_INCOMPATIBLE";
	private const string DefaultCorpusRelativePath = "third_party/Musashi/test/mc68000";
	private const string RunVariable40 = "COPPER68K_RUN_MUSASHI_M68040";
	private const string PathVariable40 = "COPPER68K_MUSASHI_M68040_PATH";
	private const string FilterVariable40 = "COPPER68K_MUSASHI_M68040_FILTER";
	private const string LimitVariable40 = "COPPER68K_MUSASHI_M68040_LIMIT";
	private const string MaxInstructionsVariable40 = "COPPER68K_MUSASHI_M68040_MAX_INSTRUCTIONS";
	private const string IncludeKnownFailingVariable40 = "COPPER68K_MUSASHI_M68040_INCLUDE_KNOWN_FAILING";
	private const string DefaultCorpusRelativePath40 = "third_party/Musashi/test/mc68040";
	private const string RunVariableM68kRsExtra = "COPPER68K_RUN_M68KRS_EXTRA";
	private const string PathVariableM68kRsExtra = "COPPER68K_M68KRS_EXTRA_PATH";
	private const string FilterVariableM68kRsExtra = "COPPER68K_M68KRS_EXTRA_FILTER";
	private const string LimitVariableM68kRsExtra = "COPPER68K_M68KRS_EXTRA_LIMIT";
	private const string MaxInstructionsVariableM68kRsExtra = "COPPER68K_M68KRS_EXTRA_MAX_INSTRUCTIONS";
	private const string IncludeKnownFailingVariableM68kRsExtra = "COPPER68K_M68KRS_EXTRA_INCLUDE_KNOWN_FAILING";
	private const string DefaultCorpusRelativePathM68kRsExtra = "third_party/m68k-rs/tests/fixtures/extra";
	private const int DefaultMaxInstructions = 1_000_000;
	private static readonly IReadOnlyDictionary<string, string> KnownIncompatiblePrograms =
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["abcd.bin"] = "uses invalid packed-BCD operands whose results conflict with SingleStepTests/m68000",
			["sbcd.bin"] = "uses invalid packed-BCD operands whose results conflict with SingleStepTests/m68000",
			["divs.bin"] = "asserts DIVS overflow N/Z flag behavior that conflicts with SingleStepTests/m68000",
			["divu.bin"] = "asserts DIVU overflow N/Z flag behavior that conflicts with SingleStepTests/m68000"
		};
	private static readonly IReadOnlyDictionary<string, string> KnownFailingM68040Programs =
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["cmp2.bin"] = "CMP2 byte carry expectation conflicts with the test source author's documented expectation"
		};
	private static readonly IReadOnlyDictionary<string, string> KnownFailingM68kRsExtraPrograms =
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["coverage/bin/swap_ext.bin"] = "exposes missing LEA address-register effective-address handling",
			["exceptions/bin/double_exception.bin"] = "exception frame and nested exception behavior still diverges from this fixture",
			["exceptions/bin/exception_priority.bin"] = "exception priority behavior still diverges from this fixture",
			["exceptions/bin/rte_validation.bin"] = "RTE validation and exception-return behavior still diverges from this fixture",
			["m68020/bin/boundary_edge.bin"] = "exposes unsupported MC68020 boundary or bus-error edge behavior",
			["m68020/bin/callm_020.bin"] = "CALLM is not currently implemented",
			["m68020/bin/cas.bin"] = "exposes unsupported exact MC68020 compare/addressing forms",
			["m68020/bin/ec020_mmu_trap.bin"] = "EC020 MMU trap behavior is not currently modeled",
			["m68020/bin/ec030_fpu.bin"] = "EC030 FPU behavior is not currently modeled",
			["m68020/bin/ec030_mmu_trap.bin"] = "EC030 MMU trap behavior is not currently modeled",
			["m68020/bin/msp_test.bin"] = "master stack pointer behavior is not currently modeled",
			["m68020/bin/trace_t0.bin"] = "MC68020 trace mode behavior still diverges from this fixture",
			["m68030/bin/cache_030.bin"] = "MC68030 cache-control behavior is not currently modeled",
			["m68030/bin/mmu_030_tc.bin"] = "MC68030 MMU control behavior is not currently modeled",
			["m68030/bin/move16_030.bin"] = "MOVE16 is not currently implemented",
			["m68040/bin/32bit_disp.bin"] = "full-extension indexed addressing is not fully implemented",
			["m68040/bin/arch_unaligned.bin"] = "unaligned access and address-error completion semantics still diverge from this fixture",
			["m68040/bin/bkpt.bin"] = "BKPT handler setup or related addressing forms are not currently implemented",
			["m68040/bin/callm_rtm.bin"] = "CALLM/RTM behavior is not currently implemented",
			["m68040/bin/ec040_positive.bin"] = "EC040 control/MMU behavior is not currently modeled",
			["m68040/bin/fpu_arith.bin"] = "MC68040 FPU arithmetic behavior is not currently modeled",
			["m68040/bin/fpu_basic_ops.bin"] = "MC68040 FPU basic operations are not currently modeled",
			["m68040/bin/fpu_branch.bin"] = "MC68040 FPU branch behavior is not currently modeled",
			["m68040/bin/fpu_constants.bin"] = "MC68040 FPU constant loading is not currently modeled",
			["m68040/bin/fpu_ctrl.bin"] = "MC68040 FPU control behavior is not currently modeled",
			["m68040/bin/fpu_double.bin"] = "MC68040 FPU double-precision behavior is not currently modeled",
			["m68040/bin/fpu_except.bin"] = "MC68040 FPU exception behavior is not currently modeled",
			["m68040/bin/fpu_exp_log.bin"] = "MC68040 FPU exp/log behavior is not currently modeled",
			["m68040/bin/fpu_move.bin"] = "MC68040 FPU move behavior is not currently modeled",
			["m68040/bin/fpu_remainder.bin"] = "MC68040 FPU remainder behavior is not currently modeled",
			["m68040/bin/fpu_rounding.bin"] = "MC68040 FPU rounding behavior is not currently modeled",
			["m68040/bin/fpu_scale.bin"] = "MC68040 FPU scale behavior is not currently modeled",
			["m68040/bin/fpu_sincos.bin"] = "MC68040 FPU sine/cosine behavior is not currently modeled",
			["m68040/bin/fpu_trans.bin"] = "MC68040 FPU transcendental behavior is not currently modeled",
			["m68040/bin/fpu_transcendental.bin"] = "MC68040 FPU transcendental behavior is not currently modeled",
			["m68040/bin/fpu_transcendental2.bin"] = "MC68040 FPU transcendental behavior is not currently modeled",
			["m68040/bin/fpu_unimp.bin"] = "MC68040 unimplemented-FPU-instruction behavior is not currently modeled",
			["m68040/bin/interrupts.bin"] = "MC68040 interrupt frame behavior still diverges from this fixture",
			["m68040/bin/lc040_mmu_positive.bin"] = "LC040 MMU behavior is not currently modeled",
			["m68040/bin/mem_indirect.bin"] = "memory-indirect full-extension addressing is not fully implemented",
			["m68040/bin/mmu_ptest.bin"] = "MC68040 MMU PTEST behavior is not currently modeled",
			["m68040/bin/moves.bin"] = "MOVES is not currently implemented",
			["m68040/bin/pack.bin"] = "PACK behavior is not currently implemented",
			["m68040/bin/pc_indirect.bin"] = "PC memory-indirect full-extension addressing is not fully implemented",
			["m68040/bin/trace_modes.bin"] = "MC68040 trace mode behavior still diverges from this fixture",
			["m68040/bin/unpk.bin"] = "UNPK behavior is not currently implemented",
			["privilege/bin/exception_privilege.bin"] = "privilege exception behavior still diverges from this fixture",
			["privilege/bin/trap_privilege.bin"] = "TRAP privilege behavior still diverges from this fixture",
			["privilege/bin/user_movec.bin"] = "user-mode MOVEC privilege trap behavior still diverges from this fixture",
			["privilege/bin/usp_ssp_switch.bin"] = "USP/SSP switching behavior still diverges from this fixture"
		};
	private readonly ITestOutputHelper _output;

	public M68kMusashiConformanceTests(ITestOutputHelper output)
	{
		_output = output;
	}

	[Fact]
	public void MusashiM68000ProgramsPassInterpreterWhenEnabled()
	{
		RunMusashiCorpus(new MusashiCorpusConfig(
			"mc68000",
			RunVariable,
			PathVariable,
			FilterVariable,
			LimitVariable,
			MaxInstructionsVariable,
			DefaultCorpusRelativePath,
			M68kCpuModel.M68000,
			CpuModelVariable,
			MusashiBackend.Interpreter,
			BackendVariable,
			IncludeKnownIncompatibleVariable,
			KnownIncompatiblePrograms,
			"known incompatible"));
	}

	[Fact]
	public void MusashiM68040ProgramsPassInterpreterWhenEnabled()
	{
		RunMusashiCorpus(new MusashiCorpusConfig(
			"mc68040",
			RunVariable40,
			PathVariable40,
			FilterVariable40,
			LimitVariable40,
			MaxInstructionsVariable40,
			DefaultCorpusRelativePath40,
			M68kCpuModel.M68040,
			null,
			MusashiBackend.Interpreter,
			null,
			IncludeKnownFailingVariable40,
			KnownFailingM68040Programs,
			"known failing"));
	}

	[Fact]
	public void M68kRsExtraFixturesPassInterpreterWhenEnabled()
	{
		RunM68kRsExtraCorpus();
	}

	private void RunMusashiCorpus(MusashiCorpusConfig config)
	{
		if (!IsTruthy(Environment.GetEnvironmentVariable(config.RunVariable)))
		{
			_output.WriteLine($"Set {config.RunVariable}=1 to run the Musashi {config.Label} program corpus.");
			return;
		}

		var corpusPath = ResolveCorpusPath(config.PathVariable, config.DefaultCorpusRelativePath, config.Label);
		if (corpusPath is null)
		{
			throw new XunitException(
				$"Musashi {config.Label} corpus not found. Set {config.PathVariable} to the Musashi repo root or test/{config.Label} folder, " +
				$"or clone it to {config.DefaultCorpusRelativePath}.");
		}

		var filter = Environment.GetEnvironmentVariable(config.FilterVariable);
		var limit = ParseLimit(Environment.GetEnvironmentVariable(config.LimitVariable));
		var maxInstructions = ParseMaxInstructions(Environment.GetEnvironmentVariable(config.MaxInstructionsVariable));
		var cpuModel = config.CpuModelVariable is null
			? config.DefaultCpuModel
			: ParseCpuModel(Environment.GetEnvironmentVariable(config.CpuModelVariable));
		var backend = config.BackendVariable is null
			? config.DefaultBackend
			: ParseBackend(Environment.GetEnvironmentVariable(config.BackendVariable));
		var includeKnownExcluded = IsTruthy(Environment.GetEnvironmentVariable(config.IncludeKnownExcludedVariable));
		var files = Directory.EnumerateFiles(corpusPath, "*.bin", SearchOption.TopDirectoryOnly)
			.Where(path => string.IsNullOrWhiteSpace(filter) ||
				Path.GetFileName(path).Contains(filter, StringComparison.OrdinalIgnoreCase))
			.Where(path => includeKnownExcluded || !config.KnownExcludedPrograms.ContainsKey(Path.GetFileName(path)))
			.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		if (files.Length == 0)
		{
			throw new XunitException($"No Musashi {config.Label} binaries matched path '{corpusPath}' and filter '{filter}'.");
		}

		var executed = 0;
		var failures = new List<string>();
		foreach (var file in files)
		{
			try
			{
				RunProgram(file, maxInstructions, cpuModel, backend);
			}
			catch (XunitException ex)
			{
				failures.Add(ex.Message);
			}

			executed++;
			if (limit is > 0 && executed >= limit)
			{
				break;
			}
		}

		if (failures.Count > 0)
		{
			var shownFailures = string.Join(Environment.NewLine, failures.Take(20).Select(failure => $"- {failure}"));
			var suffix = failures.Count > 20
				? $"{Environment.NewLine}- ... {failures.Count - 20} additional failure(s) omitted."
				: string.Empty;
			throw new XunitException(
				$"Musashi {config.Label} reported {failures.Count} failure(s) across {executed} program(s):" +
				$"{Environment.NewLine}{shownFailures}{suffix}");
		}

		var excludedCount = includeKnownExcluded
			? 0
			: config.KnownExcludedPrograms.Count(entry => string.IsNullOrWhiteSpace(filter) ||
				entry.Key.Contains(filter, StringComparison.OrdinalIgnoreCase));
		_output.WriteLine(
			$"Executed {executed} Musashi {config.Label} program(s) with {cpuModel} {backend}; " +
			$"excluded {excludedCount} {config.KnownExcludedLabel} program(s).");
	}

	private void RunM68kRsExtraCorpus()
	{
		if (!IsTruthy(Environment.GetEnvironmentVariable(RunVariableM68kRsExtra)))
		{
			_output.WriteLine($"Set {RunVariableM68kRsExtra}=1 to run the m68k-rs extra fixture corpus.");
			return;
		}

		var corpusPath = ResolveM68kRsExtraPath(PathVariableM68kRsExtra, DefaultCorpusRelativePathM68kRsExtra);
		if (corpusPath is null)
		{
			throw new XunitException(
				$"m68k-rs extra fixture corpus not found. Set {PathVariableM68kRsExtra} to the m68k-rs repo root, " +
				$"tests/fixtures/extra folder, or clone it to {DefaultCorpusRelativePathM68kRsExtra}.");
		}

		var filter = Environment.GetEnvironmentVariable(FilterVariableM68kRsExtra);
		var limit = ParseLimit(Environment.GetEnvironmentVariable(LimitVariableM68kRsExtra));
		var maxInstructions = ParseMaxInstructions(Environment.GetEnvironmentVariable(MaxInstructionsVariableM68kRsExtra));
		var includeKnownFailing = IsTruthy(Environment.GetEnvironmentVariable(IncludeKnownFailingVariableM68kRsExtra));
		var files = Directory.EnumerateFiles(corpusPath, "*.bin", SearchOption.AllDirectories)
			.Select(path => new M68kRsExtraFixture(path, ToRelativeCorpusPath(corpusPath, path)))
			.Where(fixture => string.IsNullOrWhiteSpace(filter) ||
				fixture.RelativePath.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
				Path.GetFileName(fixture.Path).Contains(filter, StringComparison.OrdinalIgnoreCase))
			.Where(fixture => includeKnownFailing || !KnownFailingM68kRsExtraPrograms.ContainsKey(fixture.RelativePath))
			.OrderBy(fixture => fixture.RelativePath, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		if (files.Length == 0)
		{
			throw new XunitException($"No m68k-rs extra fixture binaries matched path '{corpusPath}' and filter '{filter}'.");
		}

		var executed = 0;
		var failures = new List<string>();
		foreach (var fixture in files)
		{
			try
			{
				RunProgram(fixture.Path, maxInstructions, GetM68kRsExtraCpuModel(fixture.RelativePath), MusashiBackend.Interpreter);
			}
			catch (XunitException ex)
			{
				failures.Add($"{fixture.RelativePath}: {ex.Message}");
			}

			executed++;
			if (limit is > 0 && executed >= limit)
			{
				break;
			}
		}

		if (failures.Count > 0)
		{
			const int maxShownFailures = 100;
			var shownFailures = string.Join(Environment.NewLine, failures.Take(maxShownFailures).Select(failure => $"- {failure}"));
			var suffix = failures.Count > maxShownFailures
				? $"{Environment.NewLine}- ... {failures.Count - maxShownFailures} additional failure(s) omitted."
				: string.Empty;
			throw new XunitException(
				$"m68k-rs extra fixtures reported {failures.Count} failure(s) across {executed} program(s):" +
				$"{Environment.NewLine}{shownFailures}{suffix}");
		}

		var excludedCount = includeKnownFailing
			? 0
			: KnownFailingM68kRsExtraPrograms.Count(entry => string.IsNullOrWhiteSpace(filter) ||
				entry.Key.Contains(filter, StringComparison.OrdinalIgnoreCase));
		_output.WriteLine(
			$"Executed {executed} m68k-rs extra fixture program(s); excluded {excludedCount} known failing program(s).");
	}

	private static bool IsTruthy(string? value)
		=> value is not null &&
			(value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
			 value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
			 value.Equals("yes", StringComparison.OrdinalIgnoreCase));

	private static int? ParseLimit(string? value)
	{
		return int.TryParse(value, out var limit) && limit > 0
			? limit
			: null;
	}

	private static int ParseMaxInstructions(string? value)
	{
		return int.TryParse(value, out var limit) && limit > 0
			? limit
			: DefaultMaxInstructions;
	}

	private static M68kCpuModel ParseCpuModel(string? value)
	{
		if (string.IsNullOrWhiteSpace(value) ||
			value.Equals("m68000", StringComparison.OrdinalIgnoreCase) ||
			value.Equals("68000", StringComparison.OrdinalIgnoreCase))
		{
			return M68kCpuModel.M68000;
		}

		if (value.Equals("m68040", StringComparison.OrdinalIgnoreCase) ||
			value.Equals("68040", StringComparison.OrdinalIgnoreCase))
		{
			return M68kCpuModel.M68040;
		}

		throw new XunitException(
			$"Invalid {CpuModelVariable} value '{value}'. Expected 'm68000' or 'm68040'.");
	}

	private static MusashiBackend ParseBackend(string? value)
	{
		if (string.IsNullOrWhiteSpace(value) ||
			value.Equals("interpreter", StringComparison.OrdinalIgnoreCase))
		{
			return MusashiBackend.Interpreter;
		}

		if (value.Equals("jit", StringComparison.OrdinalIgnoreCase))
		{
			return MusashiBackend.Jit;
		}

		throw new XunitException(
			$"Invalid {BackendVariable} value '{value}'. Expected 'interpreter' or 'jit'.");
	}

	private static string? ResolveCorpusPath(string pathVariable, string defaultCorpusRelativePath, string corpusDirectory)
	{
		var configuredPath = Environment.GetEnvironmentVariable(pathVariable);
		if (!string.IsNullOrWhiteSpace(configuredPath))
		{
			return ResolveConfiguredCorpusPath(configuredPath, corpusDirectory);
		}

		var root = FindRepositoryRoot();
		return root is null
			? null
			: ResolveMusashiCorpusPath(
				Path.Combine(root, defaultCorpusRelativePath.Replace('/', Path.DirectorySeparatorChar)),
				corpusDirectory);
	}

	private static string? ResolveM68kRsExtraPath(string pathVariable, string defaultCorpusRelativePath)
	{
		var configuredPath = Environment.GetEnvironmentVariable(pathVariable);
		if (!string.IsNullOrWhiteSpace(configuredPath))
		{
			return ResolveConfiguredM68kRsExtraPath(configuredPath);
		}

		var root = FindRepositoryRoot();
		return root is null
			? null
			: ResolveM68kRsExtraCorpusPath(
				Path.Combine(root, defaultCorpusRelativePath.Replace('/', Path.DirectorySeparatorChar)));
	}

	private static string? ResolveConfiguredM68kRsExtraPath(string path)
	{
		var resolvedPath = ResolveM68kRsExtraCorpusPath(path);
		if (resolvedPath is not null || Path.IsPathRooted(path))
		{
			return resolvedPath;
		}

		var root = FindRepositoryRoot();
		return root is null
			? null
			: ResolveM68kRsExtraCorpusPath(Path.Combine(root, path));
	}

	private static string? ResolveM68kRsExtraCorpusPath(string path)
	{
		var fullPath = Path.GetFullPath(path);
		if (ContainsRecursiveBinaries(fullPath))
		{
			return fullPath;
		}

		var corpusPath = Path.Combine(fullPath, "tests", "fixtures", "extra");
		return ContainsRecursiveBinaries(corpusPath)
			? corpusPath
			: null;
	}

	private static string? ResolveConfiguredCorpusPath(string path, string corpusDirectory)
	{
		var resolvedPath = ResolveMusashiCorpusPath(path, corpusDirectory);
		if (resolvedPath is not null || Path.IsPathRooted(path))
		{
			return resolvedPath;
		}

		var root = FindRepositoryRoot();
		return root is null
			? null
			: ResolveMusashiCorpusPath(Path.Combine(root, path), corpusDirectory);
	}

	private static string? ResolveMusashiCorpusPath(string path, string corpusDirectory)
	{
		var fullPath = Path.GetFullPath(path);
		if (ContainsMusashiBinaries(fullPath))
		{
			return fullPath;
		}

		var corpusPath = Path.Combine(fullPath, "test", corpusDirectory);
		return ContainsMusashiBinaries(corpusPath)
			? corpusPath
			: null;
	}

	private static bool ContainsMusashiBinaries(string path)
		=> Directory.Exists(path) &&
			Directory.EnumerateFiles(path, "*.bin", SearchOption.TopDirectoryOnly).Any();

	private static bool ContainsRecursiveBinaries(string path)
		=> Directory.Exists(path) &&
			Directory.EnumerateFiles(path, "*.bin", SearchOption.AllDirectories).Any();

	private static string ToRelativeCorpusPath(string corpusPath, string file)
		=> Path.GetRelativePath(corpusPath, file)
			.Replace(Path.DirectorySeparatorChar, '/')
			.Replace(Path.AltDirectorySeparatorChar, '/');

	private static M68kCpuModel GetM68kRsExtraCpuModel(string relativePath)
	{
		if (relativePath.StartsWith("m68010/", StringComparison.OrdinalIgnoreCase))
		{
			return M68kCpuModel.M68010;
		}

		if (relativePath.StartsWith("m68020/", StringComparison.OrdinalIgnoreCase))
		{
			return M68kCpuModel.M68020;
		}

		if (relativePath.StartsWith("m68030/", StringComparison.OrdinalIgnoreCase))
		{
			return M68kCpuModel.M68030;
		}

		if (relativePath.StartsWith("m68040/", StringComparison.OrdinalIgnoreCase))
		{
			return M68kCpuModel.M68040;
		}

		return M68kCpuModel.M68000;
	}

	private static string? FindRepositoryRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null)
		{
			if (File.Exists(Path.Combine(directory.FullName, "CopperMod.sln")))
			{
				return directory.FullName;
			}

			directory = directory.Parent;
		}

		return null;
	}

	private void RunProgram(string file, int maxInstructions, M68kCpuModel cpuModel, MusashiBackend backend)
	{
		var bus = new MusashiBus();
		bus.LoadRom(File.ReadAllBytes(file));
		bus.WriteLong(0, 0x0000_03F0);
		bus.WriteLong(4, MusashiBus.RomBase);

		using var cpu = CreateCore(cpuModel, backend, bus);
		cpu.Reset(MusashiBus.RomBase, 0x0000_03F0);

		for (var instruction = 0; instruction < maxInstructions; instruction++)
		{
			try
			{
				cpu.ExecuteInstruction();
			}
			catch (Exception ex)
			{
				throw new XunitException(
					$"{Path.GetFileName(file)} threw {ex.GetType().Name} at PC 0x{cpu.State.ProgramCounter:X8}: {ex.Message}; " +
					$"SR=0x{cpu.State.StatusRegister:X4}, last PC=0x{cpu.State.LastInstructionProgramCounter:X8}, " +
					$"opcode=0x{cpu.State.LastOpcode:X4}, {FormatDataRegisters(cpu.State)}, " +
					$"{FormatAddressRegisters(cpu.State)}, {FormatLastException(cpu.State)}");
			}

			if (bus.PendingInterruptLevel is { } level)
			{
				bus.ClearPendingInterrupt();
				cpu.RequestInterrupt(level, (uint)(24 + level) * 4);
			}

			if (bus.FailCount > 0)
			{
				throw new XunitException(
					$"{Path.GetFileName(file)} reported {bus.FailCount} failure(s) after instruction {instruction + 1}; " +
					$"PC=0x{cpu.State.ProgramCounter:X8}, SR=0x{cpu.State.StatusRegister:X4}, " +
					$"last PC=0x{cpu.State.LastInstructionProgramCounter:X8}, opcode=0x{cpu.State.LastOpcode:X4}, " +
					FormatDataRegisters(cpu.State) +
					$", {FormatAddressRegisters(cpu.State)}, {FormatLastException(cpu.State)}" +
					$".{FormatStdout(bus)}");
			}

			if (bus.PassCount > 0)
			{
				return;
			}

			if (cpu.State.Stopped || cpu.State.Halted)
			{
				break;
			}
		}

		throw new XunitException(
			$"{Path.GetFileName(file)} did not report a pass within {maxInstructions} instruction(s); " +
			$"pass={bus.PassCount}, fail={bus.FailCount}, PC=0x{cpu.State.ProgramCounter:X8}, SR=0x{cpu.State.StatusRegister:X4}, " +
			$"{FormatAddressRegisters(cpu.State)}, {FormatLastException(cpu.State)}.{FormatStdout(bus)}");
	}

	private static string FormatStdout(MusashiBus bus)
	{
		var text = bus.ReadStdout();
		return text.Length == 0
			? string.Empty
			: $"{Environment.NewLine}Musashi stdout:{Environment.NewLine}{text}";
	}

	private static string FormatDataRegisters(M68kCpuState state)
	{
		var registers = Enumerable.Range(0, 8)
			.Select(index => $"D{index}=0x{state.D[index]:X8}");
		return string.Join(", ", registers);
	}

	private static string FormatAddressRegisters(M68kCpuState state)
	{
		var registers = Enumerable.Range(0, 8)
			.Select(index => $"A{index}=0x{state.A[index]:X8}");
		return string.Join(", ", registers);
	}

	private static string FormatLastException(M68kCpuState state)
		=> state.LastExceptionVector < 0
			? "last exception=<none>"
			: $"last exception=vector {state.LastExceptionVector}, stacked PC=0x{state.LastExceptionStackedProgramCounter:X8}, " +
				$"saved SR=0x{state.LastExceptionStatusRegister:X4}, exception A7=0x{state.LastExceptionA7:X8}";

	private static IM68kCore CreateCore(M68kCpuModel cpuModel, MusashiBackend backend, IM68kBus bus)
		=> backend switch
		{
			MusashiBackend.Interpreter => M68kCoreFactory.Default.Create(cpuModel, bus),
			MusashiBackend.Jit when cpuModel == M68kCpuModel.M68000 => M68kJitCore.CreateM68000(bus),
			MusashiBackend.Jit => throw new XunitException(
				$"{BackendVariable}=jit is supported by this Musashi runner only with {CpuModelVariable}=m68000."),
			_ => throw new InvalidOperationException($"Invalid Musashi backend: {backend}.")
		};

	private enum MusashiBackend
	{
		Interpreter,
		Jit
	}

	private sealed record MusashiCorpusConfig(
		string Label,
		string RunVariable,
		string PathVariable,
		string FilterVariable,
		string LimitVariable,
		string MaxInstructionsVariable,
		string DefaultCorpusRelativePath,
		M68kCpuModel DefaultCpuModel,
		string? CpuModelVariable,
		MusashiBackend DefaultBackend,
		string? BackendVariable,
		string IncludeKnownExcludedVariable,
		IReadOnlyDictionary<string, string> KnownExcludedPrograms,
		string KnownExcludedLabel);

	private sealed record M68kRsExtraFixture(string Path, string RelativePath);

	private sealed class MusashiBus : IM68kBus, IM68kCodeReader
	{
		public const uint RomBase = 0x0001_0000;
		private const uint StackRamBase = 0x0000_0000;
		private const uint TestDeviceBase = 0x0010_0000;
		private const uint ExtraRamBase = 0x0030_0000;
		private const int SlotSize = 0x0001_0000;
		private const int RomSlotCount = 4;
		private readonly byte[] _stackRam = new byte[SlotSize];
		private readonly byte[] _rom = new byte[SlotSize * RomSlotCount];
		private readonly byte[] _extraRam = new byte[SlotSize];
		private readonly StringBuilder _stdout = new();

		public int PassCount { get; private set; }

		public int FailCount { get; private set; }

		public int? PendingInterruptLevel { get; private set; }

		public void LoadRom(byte[] content)
		{
			if (content.Length > _rom.Length)
			{
				throw new XunitException($"Musashi ROM is {content.Length} byte(s), but the dummy machine maps only {_rom.Length} byte(s).");
			}

			content.CopyTo(_rom, 0);
		}

		public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = cycle;
			_ = accessKind;
			return ReadByte(address);
		}

		public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = cycle;
			_ = accessKind;
			return ReadWord(address);
		}

		public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = cycle;
			_ = accessKind;
			return ReadLong(address);
		}

		public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = cycle;
			_ = accessKind;
			WriteByte(address, value);
		}

		public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = cycle;
			_ = accessKind;
			WriteWord(address, value);
		}

		public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = cycle;
			_ = accessKind;
			WriteLong(address, value);
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
		{
			_ = cycle;
		}

		public ushort ReadHostWord(uint address)
			=> ReadWord(address);

		public string ReadStdout()
			=> _stdout.ToString();

		public void ClearPendingInterrupt()
			=> PendingInterruptLevel = null;

		private byte ReadByte(uint address)
		{
			if (TryMapRam(address, StackRamBase, _stackRam, out var stackOffset))
			{
				return _stackRam[stackOffset];
			}

			if (TryMapRam(address, RomBase, _rom, out var romOffset))
			{
				return _rom[romOffset];
			}

			if (TryMapExtraRam(address, out var extraOffset))
			{
				return _extraRam[extraOffset];
			}

			if (TryMapTestDevice(address, out _))
			{
				return 0;
			}

			throw new InvalidOperationException($"Read from unmapped Musashi address 0x{address:X8}.");
		}

		private ushort ReadWord(uint address)
			=> (ushort)((ReadByte(address) << 8) | ReadByte(address + 1));

		private uint ReadLong(uint address)
			=> ((uint)ReadWord(address) << 16) | ReadWord(address + 2);

		private void WriteByte(uint address, byte value)
		{
			if (TryMapRam(address, StackRamBase, _stackRam, out var stackOffset))
			{
				_stackRam[stackOffset] = value;
				return;
			}

			if (TryMapExtraRam(address, out var extraOffset))
			{
				_extraRam[extraOffset] = value;
				return;
			}

			if (TryMapRam(address, RomBase, _rom, out _))
			{
				throw new InvalidOperationException($"Write to Musashi ROM address 0x{address:X8}.");
			}

			if (TryMapTestDevice(address, out var testOffset))
			{
				if (testOffset == 0x14)
				{
					_stdout.Append((char)value);
				}

				return;
			}

			throw new InvalidOperationException($"Write to unmapped Musashi address 0x{address:X8}.");
		}

		private void WriteWord(uint address, ushort value)
		{
			WriteByte(address, (byte)(value >> 8));
			WriteByte(address + 1, (byte)value);
		}

		public void WriteLong(uint address, uint value)
		{
			if (TryMapTestDevice(address, out var testOffset))
			{
				if (testOffset == 0x0)
				{
					FailCount++;
				}
				else if (testOffset == 0x4)
				{
					PassCount++;
				}
				else if (testOffset == 0xC)
				{
					PendingInterruptLevel = (int)(value & 0x7);
				}

				return;
			}

			WriteWord(address, (ushort)(value >> 16));
			WriteWord(address + 2, (ushort)value);
		}

		private static bool TryMapRam(uint address, uint baseAddress, byte[] memory, out int offset)
		{
			if (address >= baseAddress && address < baseAddress + memory.Length)
			{
				offset = (int)(address - baseAddress);
				return true;
			}

			offset = 0;
			return false;
		}

		private static bool TryMapTestDevice(uint address, out uint offset)
		{
			if (address >= TestDeviceBase && address < TestDeviceBase + SlotSize)
			{
				offset = address - TestDeviceBase;
				return true;
			}

			offset = 0;
			return false;
		}

		private static bool TryMapExtraRam(uint address, out int offset)
		{
			var slot = address / SlotSize;
			if (slot is >= 0x30 and <= 0x3F)
			{
				offset = (int)(address & (SlotSize - 1));
				return true;
			}

			offset = 0;
			return false;
		}
	}
}
