using System.Diagnostics;
using System.Text;
using CopperMod.Abstractions;
using CopperMod.Cust;
using Xunit.Abstractions;

namespace CopperMod.Cust.Tests;

public sealed class CustCorpusSmokeTests
{
	private const string CorpusRootEnvironmentVariable = "COPPERMOD_CUST_CORPUS_ROOT";
	private const int SampleRate = 44100;
	private const int ChannelCount = 2;
	private const int TargetFrames = SampleRate;
	private const float SilenceThreshold = 0.0001f;
	private static readonly TimeSpan PerCaseTimeout = TimeSpan.FromSeconds(15);
	private static readonly string[] CorpusFiles =
	{
		"cust.Intact",
		@"cadaver\Cadaver.CUST",
		@"beneathasteelsky\SteelSky2.CUST",
		@"fa-18interceptor\FA-18_Interceptor.CUST",
		@"Frontier\Frontier.CUS",
		@"Populous\Populous.CUST",
		@"Starglider\CUST.Starglider",
		@"jpond2robocod\JamesPond2.CUST",
		@"RickDangerousplus\CUST.RickDangerousplus",
		@"alteredbeast\AlteredBeast.CUST",
		@"batmanthemovie\Batman_The_Movie-Title.CUST",
		@"batmanthemovie\Batman_The_Movie-Lvl_2_4.CUST",
		@"elite\Elite.CUST",
		@"MidnightResistance\CUST.MidnightResistance",
		@"greatgiannasisters\GreatGianaSisters_Intro.CUST",
		@"greatgiannasisters\GreatGianaSisters_Ingame.CUST",
		@"bioniccommando\BionicCommando.CUS",
		"CUST.BattleSquadron",
		@"hybris\Cust.hybris",
		@"Lemmings\CUST.LEMMINGS"
	};

	private readonly ITestOutputHelper _output;

	public CustCorpusSmokeTests(ITestOutputHelper output)
	{
		_output = output;
	}

	[Fact(Timeout = 180_000)]
	public void ExternalCorpusRendersFirstSubTuneForOneSecond()
	{
		var root = Environment.GetEnvironmentVariable(CorpusRootEnvironmentVariable);
		if (string.IsNullOrWhiteSpace(root))
		{
			_output.WriteLine($"CUST corpus smoke test is disabled. Set {CorpusRootEnvironmentVariable} to enable it.");
			return;
		}

		root = Environment.ExpandEnvironmentVariables(root.Trim().Trim('"'));
		if (!Directory.Exists(root))
		{
			_output.WriteLine($"CUST corpus smoke test is disabled because {CorpusRootEnvironmentVariable} does not exist: {root}");
			return;
		}

		var failures = new List<string>();
		foreach (var relativePath in CorpusFiles)
		{
			var fullPath = ResolveCorpusPath(root, relativePath);
			if (!File.Exists(fullPath))
			{
				failures.Add($"{relativePath}: missing at {fullPath}");
				continue;
			}

			var result = RunCaseWithTimeout(relativePath, fullPath);
			_output.WriteLine(result.Summary);
			if (!result.Passed)
			{
				failures.Add(result.Details);
			}
		}

		Assert.True(failures.Count == 0, "CUST corpus smoke failures:" + Environment.NewLine + string.Join(Environment.NewLine + Environment.NewLine, failures));
	}

	private static CorpusCaseResult RunCaseWithTimeout(string relativePath, string fullPath)
	{
		CorpusCaseResult? result = null;
		Exception? exception = null;
		var thread = new Thread(() =>
		{
			try
			{
				result = RunCase(relativePath, fullPath);
			}
			catch (Exception ex)
			{
				exception = ex;
			}
		})
		{
			IsBackground = true,
			Name = "CUST corpus smoke: " + Path.GetFileName(relativePath)
		};

		thread.Start();
		if (!thread.Join(PerCaseTimeout))
		{
			return CorpusCaseResult.Fail(
				relativePath,
				$"{relativePath}: timed out after {PerCaseTimeout.TotalSeconds:0}s at {fullPath}",
				$"{relativePath}: timeout after {PerCaseTimeout.TotalSeconds:0}s");
		}

		if (exception != null)
		{
			return CorpusCaseResult.Fail(
				relativePath,
				$"{relativePath}: threw {exception.GetType().Name}: {exception.Message}{Environment.NewLine}{exception}",
				$"{relativePath}: exception {exception.GetType().Name}");
		}

		return result ?? CorpusCaseResult.Fail(relativePath, $"{relativePath}: did not produce a result.", $"{relativePath}: no result");
	}

	private static CorpusCaseResult RunCase(string relativePath, string fullPath)
	{
		var stopwatch = Stopwatch.StartNew();
		var data = File.ReadAllBytes(fullPath);
		var format = new CustFormat();
		var loadContext = new ModuleLoadContext(data, fullPath);
		if (!format.CanLoad(loadContext))
		{
			return CorpusCaseResult.Fail(relativePath, $"{relativePath}: CustFormat.CanLoad returned false.", $"{relativePath}: cannot load");
		}

		using var song = format.Load(loadContext);
		if (song is not CustSong custSong)
		{
			return CorpusCaseResult.Fail(relativePath, $"{relativePath}: loaded song was {song.GetType().FullName}, not CustSong.", $"{relativePath}: wrong song type");
		}

		var options = new AudioRenderOptions(SampleRate, ChannelCount);
		var renderedFrames = 0;
		var peak = 0.0f;
		var nonFiniteSamples = 0;
		var ticks = 0;
		while (renderedFrames < TargetFrames)
		{
			var frames = custSong.GetCurrentTickFrameCount(options);
			var buffer = new float[options.GetSampleCount(frames)];
			var renderResult = custSong.RenderTick(buffer, options);
			renderedFrames += renderResult.FramesWritten;
			ticks++;

			var samplesWritten = Math.Min(renderResult.SamplesWritten, buffer.Length);
			for (var i = 0; i < samplesWritten; i++)
			{
				var sample = buffer[i];
				if (!float.IsFinite(sample))
				{
					nonFiniteSamples++;
					continue;
				}

				peak = Math.Max(peak, Math.Abs(sample));
			}

			if (renderResult.EndOfSong)
			{
				break;
			}
		}

		var failingDiagnostics = custSong.Diagnostics
			.Where(diagnostic => diagnostic.Code is "CUST_UNSUPPORTED_OPCODE" or "CUST_CPU_FAULT")
			.ToArray();
		var details = BuildDetails(relativePath, fullPath, stopwatch.Elapsed, renderedFrames, ticks, peak, nonFiniteSamples, custSong);
		var failureReasons = new List<string>();
		if (nonFiniteSamples > 0)
		{
			failureReasons.Add($"contains {nonFiniteSamples} non-finite samples");
		}

		if (peak <= SilenceThreshold)
		{
			failureReasons.Add($"peak {peak:R} is below {SilenceThreshold:R}");
		}

		if (failingDiagnostics.Length > 0)
		{
			failureReasons.Add("has failing diagnostics: " + FormatDiagnosticCodeSummary(failingDiagnostics));
		}

		if (failureReasons.Count > 0)
		{
			return CorpusCaseResult.Fail(relativePath, details + Environment.NewLine + "Failure: " + string.Join("; ", failureReasons), $"{relativePath}: failed peak={peak:R} frames={renderedFrames} diagnostics={FormatDiagnosticCodeSummary(custSong.Diagnostics)}");
		}

		return CorpusCaseResult.Pass(relativePath, $"{relativePath}: ok peak={peak:R} frames={renderedFrames} ticks={ticks} elapsed={stopwatch.Elapsed.TotalMilliseconds:0}ms diagnostics={FormatDiagnosticCodeSummary(custSong.Diagnostics)}", details);
	}

	private static string BuildDetails(
		string relativePath,
		string fullPath,
		TimeSpan elapsed,
		int renderedFrames,
		int ticks,
		float peak,
		int nonFiniteSamples,
		CustSong song)
	{
		var builder = new StringBuilder();
		builder.AppendLine(relativePath);
		builder.AppendLine($"  path: {fullPath}");
		builder.AppendLine($"  renderedFrames: {renderedFrames}");
		builder.AppendLine($"  ticks: {ticks}");
		builder.AppendLine($"  elapsed: {elapsed.TotalMilliseconds:0} ms");
		builder.AppendLine($"  peak: {peak:R}");
		builder.AppendLine($"  nonFiniteSamples: {nonFiniteSamples}");
		builder.AppendLine("  diagnostics: " + FormatDiagnostics(song.Diagnostics));
		builder.AppendLine("  writes: " + SummarizeWrites(song.CustomRegisterWrites));
		return builder.ToString().TrimEnd();
	}

	private static string FormatDiagnostics(IReadOnlyList<ModuleDiagnostic> diagnostics)
	{
		if (diagnostics.Count == 0)
		{
			return "(none)";
		}

		return string.Join(", ", diagnostics
			.GroupBy(diagnostic => diagnostic.Code + ": " + diagnostic.Message)
			.Select(group => group.Key + (group.Count() > 1 ? $" (x{group.Count()})" : string.Empty)));
	}

	private static string FormatDiagnosticCodeSummary(IReadOnlyList<ModuleDiagnostic> diagnostics)
	{
		if (diagnostics.Count == 0)
		{
			return "(none)";
		}

		return string.Join(", ", diagnostics
			.GroupBy(diagnostic => diagnostic.Code)
			.Select(group => group.Key + (group.Count() > 1 ? $"x{group.Count()}" : string.Empty)));
	}

	private static string SummarizeWrites(IReadOnlyList<CustomRegisterWrite> writes)
	{
		if (writes.Count <= 24)
		{
			return "count=" + writes.Count + " " + string.Join(", ", writes.Select(write => $"@{write.Cycle}:{write.Address:X3}={write.Value:X4}"));
		}

		var first = string.Join(", ", writes.Take(12).Select(write => $"@{write.Cycle}:{write.Address:X3}={write.Value:X4}"));
		var last = string.Join(", ", writes.TakeLast(12).Select(write => $"@{write.Cycle}:{write.Address:X3}={write.Value:X4}"));
		return $"count={writes.Count} first=[{first}] last=[{last}]";
	}

	private static string ResolveCorpusPath(string root, string relativePath)
	{
		var normalized = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
		return Path.GetFullPath(Path.Combine(root, normalized));
	}

	private sealed class CorpusCaseResult
	{
		private CorpusCaseResult(bool passed, string summary, string details)
		{
			Passed = passed;
			Summary = summary;
			Details = details;
		}

		public bool Passed { get; }

		public string Summary { get; }

		public string Details { get; }

		public static CorpusCaseResult Pass(string relativePath, string summary, string details)
		{
			_ = relativePath;
			return new CorpusCaseResult(true, summary, details);
		}

		public static CorpusCaseResult Fail(string relativePath, string details, string summary)
		{
			_ = relativePath;
			return new CorpusCaseResult(false, summary, details);
		}
	}
}
