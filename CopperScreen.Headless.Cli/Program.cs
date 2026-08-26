using System.Globalization;
using System.Text.Json;
using CopperScreen.Headless;

return Run(args);

static int Run(string[] args)
{
	if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase))
	{
		PrintUsage();
		return args.Length == 0 ? 1 : 0;
	}

	try
	{
		var values = Parse(args);
		var hunkPath = Required(values, "hunk");
		var expectedD0 = OptionalUInt(values, "expect-d0");
		var runnerOptions = new CopperScreenTestOptions
		{
			MachineProfile = OptionalEnum(values, "profile", CopperScreenTestMachineProfile.A500PalExpanded),
			CpuBackend = OptionalEnum(values, "cpu", CopperScreenTestCpuBackend.AccurateM68000),
			MaxInstructionsPerFrame = OptionalInt(values, "instructions-per-frame", 1_000_000)
		};
		var limits = new CopperScreenTestRunLimits(
			OptionalLong(values, "max-frames", 300),
			OptionalNullableLong(values, "max-cycles"),
			OptionalNullableLong(values, "max-instructions"));

		using var runner = CopperScreenTestRunner.Create(runnerOptions);
		var launch = runner.LaunchHunk(
			File.ReadAllBytes(hunkPath),
			new CopperScreenProgramLaunchOptions
			{
				DisplayName = Path.GetFileName(hunkPath),
				Arguments = values.GetValueOrDefault("arguments") ?? string.Empty,
				StackSize = OptionalInt(values, "stack-size", 8 * 1024),
				EnableInterrupts = values.ContainsKey("enable-interrupts")
			});
		var result = runner.RunUntilProgramReturn(limits);
		var success = result.StopReason == CopperScreenTestStopReason.ProgramReturned &&
			(!expectedD0.HasValue || result.ReturnValue == expectedD0.Value);

		if (values.ContainsKey("json"))
		{
			Console.WriteLine(JsonSerializer.Serialize(new { success, launch, result }, new JsonSerializerOptions { WriteIndented = true }));
		}
		else
		{
			Console.WriteLine($"{result.StopReason}: D0=0x{result.ReturnValue:X8}, frames={result.Snapshot.FramesExecuted}, cycles={result.Snapshot.Cpu.Cycles}, instructions={result.Snapshot.InstructionsExecuted}");
		}

		return success ? 0 : result.StopReason == CopperScreenTestStopReason.ProgramReturned ? 3 : 2;
	}
	catch (Exception exception)
	{
		Console.Error.WriteLine(exception.Message);
		return 1;
	}
}

static Dictionary<string, string?> Parse(string[] args)
{
	var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
	for (var index = 0; index < args.Length; index++)
	{
		var token = args[index];
		if (!token.StartsWith("--", StringComparison.Ordinal))
			throw new ArgumentException($"Unexpected argument '{token}'.");
		var key = token[2..];
		values[key] = index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
			? args[++index]
			: null;
	}
	return values;
}

static string Required(Dictionary<string, string?> values, string key) =>
	values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
		? value
		: throw new ArgumentException($"Missing required --{key} option.");

static int OptionalInt(Dictionary<string, string?> values, string key, int fallback) =>
	values.TryGetValue(key, out var value) ? int.Parse(value!, CultureInfo.InvariantCulture) : fallback;

static long OptionalLong(Dictionary<string, string?> values, string key, long fallback) =>
	values.TryGetValue(key, out var value) ? long.Parse(value!, CultureInfo.InvariantCulture) : fallback;

static long? OptionalNullableLong(Dictionary<string, string?> values, string key) =>
	values.TryGetValue(key, out var value) ? long.Parse(value!, CultureInfo.InvariantCulture) : null;

static uint? OptionalUInt(Dictionary<string, string?> values, string key) =>
	values.TryGetValue(key, out var value)
		? value!.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
			? uint.Parse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
			: uint.Parse(value, CultureInfo.InvariantCulture)
		: null;

static T OptionalEnum<T>(Dictionary<string, string?> values, string key, T fallback) where T : struct, Enum =>
	values.TryGetValue(key, out var value) ? Enum.Parse<T>(value!, ignoreCase: true) : fallback;

static void PrintUsage() => Console.WriteLine(
	"CopperScreen.Headless.Cli --hunk <file> [--expect-d0 <value>] [--max-frames <n>]\n" +
	"  [--max-cycles <n>] [--max-instructions <n>] [--profile <name>] [--cpu <name>]\n" +
	"  [--arguments <text>] [--stack-size <bytes>] [--enable-interrupts] [--json]");
