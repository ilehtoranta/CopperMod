using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CopperMod.Abstractions;
using CopperMod.Rendering;
using CopperMod.Sid;

namespace CopperMod.Tools;

internal static partial class SidConformance
{
	public const string CommandName = "compare-sid-conformance";
	public const int DefaultSampleRate = 48000;
	public const int DefaultSegmentRate = 50;
	public const ushort ProgramBase = 0x1000;
	private const byte FrameCounterAddress = 0x02;
	private const ushort SidBase = 0xD400;
	private const int SidPlayFpTimeoutMilliseconds = 60000;
	private const double ReferenceSilenceAcThreshold = 0.005;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		ReadCommentHandling = JsonCommentHandling.Skip,
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	public static bool IsCommand(string[] args)
	{
		return args.Length > 0 && string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase);
	}

	public static void Run(string[] args, TextWriter output)
	{
		var options = SidConformanceOptions.Parse(args);
		var root = Path.GetFullPath(options.FixtureDirectory);
		var suite = LoadSuite(root);
		var outputDirectory = Path.GetFullPath(options.OutputDirectory);
		var referenceDirectory = Path.GetFullPath(options.ReferenceDirectory ?? Path.Combine(outputDirectory, "sidplayfp"));
		var candidateDirectory = Path.GetFullPath(options.CandidateDirectory ?? Path.Combine(outputDirectory, "coppermod"));
		var bitmapDirectory = Path.Combine(outputDirectory, "waveforms");
		Directory.CreateDirectory(outputDirectory);
		Directory.CreateDirectory(referenceDirectory);
		Directory.CreateDirectory(candidateDirectory);
		Directory.CreateDirectory(bitmapDirectory);

		output.WriteLine("id,category,name,output,ref_ac,cand_ac,ac_ratio,diff,corr");
		var results = new List<SidConformanceComparisonResult>();
		var segmentResults = new List<SidConformanceSegmentComparisonResult>();
		var diagnostics = new SidConformanceDiagnostics();
		foreach (var fixture in suite.Fixtures)
		{
			var seconds = fixture.Spec.Seconds;
			var sampleRate = options.SampleRate ?? fixture.Spec.SampleRate ?? suite.Manifest.SampleRate ?? DefaultSampleRate;
			var playerPath = Path.Combine(candidateDirectory, fixture.Id + "-player.wav");
			if (!File.Exists(playerPath) || options.OverwriteCandidate)
			{
				var samples = RenderCopperMod(fixture.BinaryPath, seconds, sampleRate, options.SidProfile, playerOutput: true);
				WriteFloatWav(playerPath, sampleRate, samples);
			}

			var rawPath = Path.Combine(candidateDirectory, fixture.Id + "-raw.wav");
			if (!File.Exists(rawPath) || options.OverwriteCandidate)
			{
				var samples = RenderCopperMod(fixture.BinaryPath, seconds, sampleRate, options.SidProfile, playerOutput: false);
				WriteFloatWav(rawPath, sampleRate, samples);
			}

			var targetSamples = SecondsToSamples(seconds, sampleRate);
			var player = ReadFloatWav(playerPath);
			var playerSamples = TrimSamples(player.Samples, targetSamples);
			WriteWaveformPng(Path.Combine(bitmapDirectory, fixture.Id + "-candidate.png"), playerSamples, sampleRate);
			var raw = ReadFloatWav(rawPath);
			var rawSamples = TrimSamples(raw.Samples, targetSamples);
			WriteWaveformPng(Path.Combine(bitmapDirectory, fixture.Id + "-raw.png"), rawSamples, sampleRate);

			var referencePath = Path.Combine(referenceDirectory, fixture.Id + "-ref.wav");
			if ((!File.Exists(referencePath) || options.OverwriteReference) && !string.IsNullOrWhiteSpace(options.SidPlayFpPath))
			{
				RunSidPlayFp(
					options.SidPlayFpPath!,
					fixture.BinaryPath,
					referenceDirectory,
					fixture.Id + "-ref",
					Math.Ceiling(seconds),
					sampleRate,
					fixture.Spec.Clock,
					fixture.Spec.ChipModel);
			}

			if (!File.Exists(referencePath))
			{
				output.WriteLine(string.Join(
					',',
					fixture.Id,
					fixture.Spec.SidtestCategory.ToString(CultureInfo.InvariantCulture),
					EscapeCsv(fixture.Spec.Name),
					"player",
					"",
					FormatInvariant(AcRms(playerSamples, 0, playerSamples.Length)),
					"",
					"",
					""));
				continue;
			}

			var reference = ReadFloatWav(referencePath);
			var referenceSamples = TrimSamples(reference.Samples, targetSamples);
			var length = Math.Min(referenceSamples.Length, playerSamples.Length);
			var referenceAc = AcRms(referenceSamples, 0, length);
			ValidateReferenceNotSilent(fixture, referenceAc);
			var result = new SidConformanceComparisonResult(
				fixture.Id,
				fixture.Spec.SidtestCategory,
				fixture.Spec.Name,
				"player",
				referenceAc,
				AcRms(playerSamples, 0, length),
				Diff(referenceSamples, playerSamples, 0, 0, length),
				Correlation(referenceSamples, playerSamples, 0, 0, length));
			results.Add(result);
			var fixtureSegments = CompareSegments(fixture, sampleRate, referenceSamples, playerSamples, rawSamples);
			segmentResults.AddRange(fixtureSegments);
			CollectFixtureDiagnostics(
				fixture,
				sampleRate,
				referenceSamples,
				playerSamples,
				rawSamples,
				fixtureSegments,
				diagnostics);
			output.WriteLine(result.ToCsvLine());
			WriteWaveformPng(Path.Combine(bitmapDirectory, fixture.Id + "-reference.png"), referenceSamples, sampleRate);
		}

		var csvPath = Path.Combine(outputDirectory, "conformance-comparison.csv");
		var segmentCsvPath = Path.Combine(outputDirectory, "conformance-segments.csv");
		WriteComparisonCsv(csvPath, results);
		WriteSegmentComparisonCsv(segmentCsvPath, segmentResults);
		WriteDiagnosticCsvs(outputDirectory, diagnostics);
		WriteIndexHtml(Path.Combine(outputDirectory, "index.html"), suite.Fixtures, results, segmentResults, diagnostics);
		output.WriteLine("Wrote " + csvPath);
		output.WriteLine("Wrote " + segmentCsvPath);
	}

	public static SidConformanceSuite LoadSuite(string root)
	{
		var manifestPath = Path.Combine(root, "manifest.json");
		var manifest = ReadJson<SidConformanceManifest>(manifestPath);
		if (manifest.Fixtures.Count == 0)
		{
			throw new InvalidDataException("SID conformance manifest does not contain fixtures: " + manifestPath);
		}

		var fixtures = new List<SidConformanceFixture>();
		foreach (var row in manifest.Fixtures)
		{
			var specPath = Path.GetFullPath(Path.Combine(root, row.Spec));
			var spec = ReadJson<SidConformanceFixtureSpec>(specPath);
			if (!string.Equals(row.Id, spec.Id, StringComparison.Ordinal))
			{
				throw new InvalidDataException("Fixture manifest id does not match spec id: " + row.Id + " vs " + spec.Id);
			}

			var binaryPath = Path.GetFullPath(Path.Combine(root, row.Binary));
			fixtures.Add(new SidConformanceFixture(row.Id, row, spec, specPath, binaryPath));
		}

		return new SidConformanceSuite(root, manifest, fixtures);
	}

	public static IReadOnlyList<string> ValidateAccuracyReport(
		SidConformanceSuite suite,
		string comparisonCsvPath)
	{
		ArgumentNullException.ThrowIfNull(suite);
		var errors = new List<string>();
		if (suite.Manifest.Schema < 2)
		{
			errors.Add("Conformance manifest schema must be at least 2 for external accuracy evidence.");
			return errors;
		}

		if (suite.Manifest.Evidence == null)
		{
			errors.Add("Conformance manifest is missing external evidence metadata.");
			return errors;
		}

		if (!File.Exists(comparisonCsvPath))
		{
			errors.Add("Conformance comparison CSV does not exist: " + comparisonCsvPath);
			return errors;
		}

		var lines = File.ReadAllLines(comparisonCsvPath);
		if (lines.Length < 2)
		{
			errors.Add("Conformance comparison CSV is empty: " + comparisonCsvPath);
			return errors;
		}

		var header = ParseCsvLine(lines[0]);
		var idIndex = Array.IndexOf(header, "id");
		var ratioIndex = Array.IndexOf(header, "ac_ratio");
		if (idIndex < 0 || ratioIndex < 0)
		{
			errors.Add("Conformance comparison CSV is missing id or ac_ratio columns.");
			return errors;
		}

		var ratios = new Dictionary<string, double>(StringComparer.Ordinal);
		for (var i = 1; i < lines.Length; i++)
		{
			var fields = ParseCsvLine(lines[i]);
			if (fields.Length <= Math.Max(idIndex, ratioIndex) ||
				!double.TryParse(fields[ratioIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio))
			{
				continue;
			}

			ratios[fields[idIndex]] = ratio;
		}

		foreach (var fixture in suite.Fixtures)
		{
			var accuracy = fixture.Manifest.Accuracy;
			if (accuracy == null)
			{
				errors.Add(fixture.Id + " is missing accuracy thresholds.");
				continue;
			}

			if (!string.Equals(fixture.Manifest.ChipModel, fixture.Spec.ChipModel, StringComparison.OrdinalIgnoreCase) ||
				!string.Equals(fixture.Manifest.Clock, fixture.Spec.Clock, StringComparison.OrdinalIgnoreCase))
			{
				errors.Add(fixture.Id + " manifest chip/clock does not match its fixture spec.");
			}

			if (!ratios.TryGetValue(fixture.Id, out var ratio))
			{
				errors.Add(fixture.Id + " is missing from the conformance comparison report.");
				continue;
			}

			if (ratio < accuracy.MinimumAcRatio || ratio > accuracy.MaximumAcRatio)
			{
				errors.Add(
					$"{fixture.Id} AC ratio {ratio:0.000000} is outside " +
					$"{accuracy.MinimumAcRatio:0.000000}..{accuracy.MaximumAcRatio:0.000000}.");
			}
		}

		ValidateSegmentAccuracyReport(
			suite,
			Path.Combine(Path.GetDirectoryName(comparisonCsvPath) ?? ".", "conformance-segments.csv"),
			errors);

		return errors;
	}

	private static void ValidateSegmentAccuracyReport(
		SidConformanceSuite suite,
		string segmentCsvPath,
		List<string> errors)
	{
		var measuredFixtures = suite.Fixtures
			.Where(fixture => fixture.Manifest.Accuracy is
				{
					MaximumSegmentRmsErrorDb: > 0.0
				} || fixture.Manifest.Accuracy is
				{
					MaximumCutoffLocationErrorFraction: > 0.0
				})
			.ToDictionary(fixture => fixture.Id, StringComparer.Ordinal);
		if (measuredFixtures.Count == 0)
		{
			return;
		}

		if (!File.Exists(segmentCsvPath))
		{
			errors.Add("Conformance segment CSV does not exist: " + segmentCsvPath);
			return;
		}

		var lines = File.ReadAllLines(segmentCsvPath);
		if (lines.Length < 2)
		{
			errors.Add("Conformance segment CSV is empty: " + segmentCsvPath);
			return;
		}

		var header = ParseCsvLine(lines[0]);
		var idIndex = Array.IndexOf(header, "id");
		var segmentIndex = Array.IndexOf(header, "segment");
		var referenceAcIndex = Array.IndexOf(header, "ref_ac");
		var candidateAcIndex = Array.IndexOf(header, "cand_player_ac");
		if (idIndex < 0 || segmentIndex < 0 || referenceAcIndex < 0 || candidateAcIndex < 0)
		{
			errors.Add("Conformance segment CSV is missing required accuracy columns.");
			return;
		}

		var rowsByFixture = new Dictionary<string, List<SidSegmentAccuracyRow>>(StringComparer.Ordinal);
		for (var lineIndex = 1; lineIndex < lines.Length; lineIndex++)
		{
			var fields = ParseCsvLine(lines[lineIndex]);
			var requiredIndex = Math.Max(Math.Max(idIndex, segmentIndex), Math.Max(referenceAcIndex, candidateAcIndex));
			if (fields.Length <= requiredIndex || !measuredFixtures.ContainsKey(fields[idIndex]) ||
				!double.TryParse(fields[referenceAcIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var referenceAc) ||
				!double.TryParse(fields[candidateAcIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var candidateAc))
			{
				continue;
			}

			if (!rowsByFixture.TryGetValue(fields[idIndex], out var rows))
			{
				rows = new List<SidSegmentAccuracyRow>();
				rowsByFixture.Add(fields[idIndex], rows);
			}

			rows.Add(new SidSegmentAccuracyRow(fields[segmentIndex], referenceAc, candidateAc));
		}

		foreach (var (fixtureId, fixture) in measuredFixtures)
		{
			if (!rowsByFixture.TryGetValue(fixtureId, out var rows) || rows.Count == 0)
			{
				errors.Add(fixtureId + " is missing from the conformance segment report.");
				continue;
			}

			var accuracy = fixture.Manifest.Accuracy!;
			if (accuracy.MaximumSegmentRmsErrorDb > 0.0)
			{
				var squaredErrors = rows
					.Where(row => row.ReferenceAc >= accuracy.MinimumSegmentReferenceAc && row.CandidateAc > 0.0)
					.Select(row => Math.Pow(20.0 * Math.Log10(row.CandidateAc / row.ReferenceAc), 2.0))
					.ToArray();
				if (squaredErrors.Length == 0)
				{
					errors.Add(fixtureId + " has no segments above its reference AC evidence floor.");
				}
				else
				{
					var rmsErrorDb = Math.Sqrt(squaredErrors.Average());
					if (rmsErrorDb > accuracy.MaximumSegmentRmsErrorDb)
					{
						errors.Add(
							$"{fixtureId} segment RMS response error {rmsErrorDb:0.000000} dB exceeds " +
							$"{accuracy.MaximumSegmentRmsErrorDb:0.000000} dB.");
					}
				}
			}

			if (accuracy.MaximumCutoffLocationErrorFraction > 0.0)
			{
				var cutoffGroups = new Dictionary<string, List<SidCutoffAccuracyPoint>>(StringComparer.Ordinal);
				foreach (var row in rows)
				{
					if (!TryParseCutoffPoint(row, out var group, out var point))
					{
						continue;
					}

					if (!cutoffGroups.TryGetValue(group, out var points))
					{
						points = new List<SidCutoffAccuracyPoint>();
						cutoffGroups.Add(group, points);
					}

					points.Add(point);
				}

				foreach (var (group, points) in cutoffGroups)
				{
					if (points.Count < 2)
					{
						continue;
					}

					var referenceWeight = points.Sum(point => point.ReferenceAc * point.ReferenceAc);
					var candidateWeight = points.Sum(point => point.CandidateAc * point.CandidateAc);
					if (referenceWeight <= 0.0 || candidateWeight <= 0.0)
					{
						continue;
					}

					var referenceLocation = points.Sum(point => point.Index * point.ReferenceAc * point.ReferenceAc) / referenceWeight;
					var candidateLocation = points.Sum(point => point.Index * point.CandidateAc * point.CandidateAc) / candidateWeight;
					var locationError = Math.Abs(referenceLocation - candidateLocation) / 4.0;
					if (locationError > accuracy.MaximumCutoffLocationErrorFraction)
					{
						errors.Add(
							$"{fixtureId} {group} cutoff-location error {locationError:0.000000} exceeds " +
							$"{accuracy.MaximumCutoffLocationErrorFraction:0.000000}.");
					}
				}
			}
		}
	}

	private static bool TryParseCutoffPoint(
		SidSegmentAccuracyRow row,
		out string group,
		out SidCutoffAccuracyPoint point)
	{
		var parts = row.Name.Split('-');
		if (parts.Length != 3 || parts[0] is not ("lp" or "bp" or "hp") || !parts[1].StartsWith("res", StringComparison.Ordinal))
		{
			group = "";
			point = default;
			return false;
		}

		var index = parts[2] switch
		{
			"closed" => 0,
			"low" => 1,
			"mid" => 2,
			"high" => 3,
			"open" => 4,
			_ => -1
		};
		if (index < 0)
		{
			group = "";
			point = default;
			return false;
		}

		group = parts[0] + "-" + parts[1];
		point = new SidCutoffAccuracyPoint(index, row.ReferenceAc, row.CandidateAc);
		return true;
	}

	public static SidConformanceBaseline LoadBaseline(string path)
	{
		return ReadJson<SidConformanceBaseline>(path);
	}

	public static byte[] BuildFixtureBinary(SidConformanceFixtureSpec spec)
	{
		if (!string.Equals(spec.Format, "psid", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("Only PSID conformance fixtures are supported in this pass: " + spec.Id);
		}

		var program = BuildPsidProgram(spec, out var playAddress);
		return CreatePsid(
			program,
			ProgramBase,
			ProgramBase,
			playAddress,
			spec.Name,
			"CopperMod",
			"2026",
			spec.ChipModel);
	}

	public static string Sha256Hex(byte[] data)
	{
		return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
	}

	public static SidConformanceFixtureMetrics MeasureFixture(
		SidConformanceFixture fixture,
		int sampleRate,
		SidEmulationProfile profile)
	{
		var raw = RenderCopperMod(fixture.BinaryPath, fixture.Spec.Seconds, sampleRate, profile, playerOutput: false);
		var player = RenderCopperMod(fixture.BinaryPath, fixture.Spec.Seconds, sampleRate, profile, playerOutput: true);
		return new SidConformanceFixtureMetrics
		{
			Id = fixture.Id,
			Outputs =
			{
				["raw"] = MeasureOutput(fixture.Spec, raw, sampleRate),
				["player"] = MeasureOutput(fixture.Spec, player, sampleRate)
			}
		};
	}

	public static SidConformanceOutputMetrics MeasureOutput(SidConformanceFixtureSpec spec, float[] samples, int sampleRate)
	{
		var result = new SidConformanceOutputMetrics
		{
			Mean = Mean(samples, 0, samples.Length),
			AcRms = AcRms(samples, 0, samples.Length),
			Peak = Peak(samples, 0, samples.Length)
		};

		var segmentStartFrame = 0;
		foreach (var segment in spec.Segments)
		{
			var start = SecondsToSamples(segmentStartFrame / (double)(spec.SegmentRate ?? DefaultSegmentRate), sampleRate);
			var length = Math.Min(
				samples.Length - start,
				SecondsToSamples(segment.Frames / (double)(spec.SegmentRate ?? DefaultSegmentRate), sampleRate));
			if (length <= 0)
			{
				continue;
			}

			result.Segments.Add(new SidConformanceSegmentMetrics
			{
				Name = segment.Name,
				Mean = Mean(samples, start, length),
				AcRms = AcRms(samples, start, length),
				Peak = Peak(samples, start, length)
			});
			segmentStartFrame += segment.Frames;
		}

		return result;
	}

	private static IReadOnlyList<SidConformanceSegmentComparisonResult> CompareSegments(
		SidConformanceFixture fixture,
		int sampleRate,
		float[] referenceSamples,
		float[] playerSamples,
		float[] rawSamples)
	{
		var results = new List<SidConformanceSegmentComparisonResult>();
		var spec = fixture.Spec;
		var segmentRate = spec.SegmentRate ?? DefaultSegmentRate;
		var segmentStartFrame = 0;
		for (var i = 0; i < spec.Segments.Count; i++)
		{
			var segment = spec.Segments[i];
			var start = SecondsToSamples(segmentStartFrame / (double)segmentRate, sampleRate);
			var length = SecondsToSamples(segment.Frames / (double)segmentRate, sampleRate);
			var available = Math.Min(
				referenceSamples.Length,
				Math.Min(playerSamples.Length, rawSamples.Length)) - start;
			length = Math.Min(length, available);
			if (length <= 0)
			{
				segmentStartFrame += segment.Frames;
				continue;
			}

			var reference = MeasureSamples(referenceSamples, start, length);
			var player = MeasureSamples(playerSamples, start, length);
			var raw = MeasureSamples(rawSamples, start, length);
			results.Add(new SidConformanceSegmentComparisonResult(
				fixture.Id,
				spec.SidtestCategory,
				spec.Name,
				i,
				segment.Name,
				(start * 1000.0) / sampleRate,
				((start + length) * 1000.0) / sampleRate,
				reference.Mean,
				reference.AcRms,
				reference.Peak,
				player.Mean,
				player.AcRms,
				player.Peak,
				player.AcRms / Math.Max(1.0e-12, reference.AcRms),
				Diff(referenceSamples, playerSamples, start, start, length),
				Correlation(referenceSamples, playerSamples, start, start, length),
				raw.Mean,
				raw.AcRms,
				raw.Peak));
			segmentStartFrame += segment.Frames;
		}

		return results;
	}

	public static void WriteManifest(string path, SidConformanceManifest manifest)
	{
		WriteJson(path, manifest);
	}

	public static void WriteBaseline(string path, SidConformanceBaseline baseline)
	{
		WriteJson(path, baseline);
	}

	public static float[] RenderCopperMod(
		string inputPath,
		double seconds,
		int sampleRate,
		SidEmulationProfile profile,
		bool playerOutput)
	{
		using var song = new SidFormat().Load(new ModuleLoadContext(File.ReadAllBytes(inputPath), inputPath));
		if (song is ISidEmulationProfileController profileController)
		{
			profileController.SidEmulationProfile = profile;
		}

		var settings = new ModuleRenderSettings(
			sampleRate,
			channelCount: 1,
			playerOutput ? ModuleRenderOutputMode.Player : ModuleRenderOutputMode.Raw,
			AmigaOutputProfile.A500,
			C64OutputProfile.C64,
			profile);
		using var renderer = new ModulePcmRenderer(song, settings);
		var targetSamples = SecondsToSamples(seconds, sampleRate);
		var samples = new float[targetSamples];
		var offset = 0;
		while (offset < samples.Length)
		{
			var written = renderer.Read(samples.AsSpan(offset));
			if (written <= 0)
			{
				break;
			}

			offset += written;
		}

		return samples;
	}

	public static double Mean(float[] samples, int start, int length)
	{
		var sum = 0.0;
		for (var i = 0; i < length; i++)
		{
			sum += samples[start + i];
		}

		return sum / Math.Max(1, length);
	}

	public static double AcRms(float[] samples, int start, int length)
	{
		var mean = Mean(samples, start, length);
		var energy = 0.0;
		for (var i = 0; i < length; i++)
		{
			var value = samples[start + i] - mean;
			energy += value * value;
		}

		return Math.Sqrt(energy / Math.Max(1, length));
	}

	public static double Diff(float[] left, float[] right, int leftStart, int rightStart, int length)
	{
		var energy = 0.0;
		for (var i = 0; i < length; i++)
		{
			var value = left[leftStart + i] - right[rightStart + i];
			energy += value * value;
		}

		return Math.Sqrt(energy / Math.Max(1, length));
	}

	public static double Correlation(float[] left, float[] right, int leftStart, int rightStart, int length)
	{
		var leftMean = Mean(left, leftStart, length);
		var rightMean = Mean(right, rightStart, length);
		var product = 0.0;
		var leftEnergy = 0.0;
		var rightEnergy = 0.0;
		for (var i = 0; i < length; i++)
		{
			var leftValue = left[leftStart + i] - leftMean;
			var rightValue = right[rightStart + i] - rightMean;
			product += leftValue * rightValue;
			leftEnergy += leftValue * leftValue;
			rightEnergy += rightValue * rightValue;
		}

		return product / Math.Sqrt(Math.Max(1.0e-20, leftEnergy * rightEnergy));
	}

	private static void ValidateReferenceNotSilent(SidConformanceFixture fixture, double referenceAc)
	{
		if (IsExpectedSilentFixture(fixture.Spec) || referenceAc >= ReferenceSilenceAcThreshold)
		{
			return;
		}

		throw new CommandLineException(
			"SidPlayFP reference for fixture " + fixture.Id + " appears silent " +
			"(AC RMS " + FormatInvariant(referenceAc) + "). Check the reference render before using this report.");
	}

	private static bool IsExpectedSilentFixture(SidConformanceFixtureSpec spec)
	{
		return spec.Tags.Any(tag => string.Equals(tag, "silent", StringComparison.OrdinalIgnoreCase)) ||
			string.Equals(spec.ReferenceAuthority, "coppermod-baseline", StringComparison.OrdinalIgnoreCase);
	}

	private static byte[] BuildPsidProgram(SidConformanceFixtureSpec spec, out ushort playAddress)
	{
		var init = new Mos6510Emitter(ProgramBase);
		init.LdxImmediate(0x18);
		init.LdaImmediate(0x00);
		init.Label("clear");
		init.StaAbsoluteX(SidBase);
		init.Dex();
		init.Bpl("clear");
		init.LdaImmediate(0x00);
		init.StaZeroPage(FrameCounterAddress);
		foreach (var write in spec.CommonWrites)
		{
			init.LdaImmediate((byte)ParseHex(write.Value, max: 0xFF));
			init.StaAbsolute(ParseAddress(write.Address));
		}

		init.Rts();
		var initBytes = init.ToArray();

		var playBase = checked((ushort)(ProgramBase + initBytes.Length));
		playAddress = playBase;
		var play = new Mos6510Emitter(playBase);
		play.LdaZeroPage(FrameCounterAddress);
		var cumulativeFrames = 0;
		for (var i = 0; i < spec.Segments.Count; i++)
		{
			if ((uint)cumulativeFrames > byte.MaxValue)
			{
				throw new InvalidDataException("Fixture has more than 255 play frames: " + spec.Id);
			}

			play.CmpImmediate((byte)cumulativeFrames);
			play.Bne("not-segment-" + i.ToString(CultureInfo.InvariantCulture));
			play.Jmp("segment-" + i.ToString(CultureInfo.InvariantCulture));
			play.Label("not-segment-" + i.ToString(CultureInfo.InvariantCulture));
			cumulativeFrames += spec.Segments[i].Frames;
		}

		play.Jmp("tick");
		for (var i = 0; i < spec.Segments.Count; i++)
		{
			play.Label("segment-" + i.ToString(CultureInfo.InvariantCulture));
			foreach (var write in spec.Segments[i].Writes)
			{
				play.LdaImmediate((byte)ParseHex(write.Value, max: 0xFF));
				play.StaAbsolute(ParseAddress(write.Address));
			}

			play.Jmp("tick");
		}

		play.Label("tick");
		play.IncZeroPage(FrameCounterAddress);
		play.Rts();
		return initBytes.Concat(play.ToArray()).ToArray();
	}

	private static byte[] CreatePsid(
		byte[] program,
		ushort loadAddress,
		ushort initAddress,
		ushort playAddress,
		string title,
		string author,
		string released,
		string chipModel)
	{
		var data = new byte[0x7C + program.Length];
		var sidModelBits = string.Equals(chipModel, "mos8580", StringComparison.OrdinalIgnoreCase)
			? 2
			: 1;
		WriteAscii(data, 0, "PSID");
		WriteBigEndian(data, 4, (ushort)2);
		WriteBigEndian(data, 6, (ushort)0x7C);
		WriteBigEndian(data, 8, loadAddress);
		WriteBigEndian(data, 0x0A, initAddress);
		WriteBigEndian(data, 0x0C, playAddress);
		WriteBigEndian(data, 0x0E, (ushort)1);
		WriteBigEndian(data, 0x10, (ushort)1);
		WriteBigEndian(data, 0x12, 0U);
		WriteFixed(data, 0x16, title);
		WriteFixed(data, 0x36, author);
		WriteFixed(data, 0x56, released);
		WriteBigEndian(data, 0x76, (ushort)((1 << 2) | (sidModelBits << 4)));
		program.CopyTo(data, 0x7C);
		return data;
	}

	private static void RunSidPlayFp(
		string sidPlayFpPath,
		string inputPath,
		string outputDirectory,
		string outputBaseName,
		double seconds,
		int sampleRate,
		string clock,
		string chipModel)
	{
		var sidPlayFp = Path.GetFullPath(sidPlayFpPath);
		if (!File.Exists(sidPlayFp))
		{
			throw new CommandLineException("sidplayfp executable does not exist: " + sidPlayFp);
		}

		var startInfo = new ProcessStartInfo
		{
			FileName = sidPlayFp,
			WorkingDirectory = outputDirectory,
			UseShellExecute = false,
			RedirectStandardError = true,
			RedirectStandardOutput = true
		};
		startInfo.ArgumentList.Add("--residfp");
		startInfo.ArgumentList.Add("--delay=0");
		startInfo.ArgumentList.Add("-cwa");
		startInfo.ArgumentList.Add(string.Equals(clock, "ntsc", StringComparison.OrdinalIgnoreCase) ? "-vnf" : "-vpf");
		startInfo.ArgumentList.Add(string.Equals(chipModel, "mos8580", StringComparison.OrdinalIgnoreCase) ? "-mnf" : "-mof");
		startInfo.ArgumentList.Add("-q");
		startInfo.ArgumentList.Add("-f" + sampleRate.ToString(CultureInfo.InvariantCulture));
		startInfo.ArgumentList.Add("-p32");
		startInfo.ArgumentList.Add("-t" + FormatSidPlayFpDuration(seconds));
		startInfo.ArgumentList.Add("-w" + outputBaseName);
		startInfo.ArgumentList.Add(inputPath);

		using var process = Process.Start(startInfo) ?? throw new CommandLineException("Failed to start sidplayfp.");
		var stdout = process.StandardOutput.ReadToEnd();
		var stderr = process.StandardError.ReadToEnd();
		if (!process.WaitForExit(SidPlayFpTimeoutMilliseconds))
		{
			try
			{
				process.Kill(entireProcessTree: true);
			}
			catch (InvalidOperationException)
			{
			}

			throw new CommandLineException("sidplayfp timed out while rendering " + Path.GetFileName(inputPath) + ".");
		}

		if (process.ExitCode != 0)
		{
			throw new CommandLineException("sidplayfp failed while rendering " + Path.GetFileName(inputPath) + ":" + Environment.NewLine + stdout + stderr);
		}
	}

	private static SidConformanceOutputMetrics MeasureOutput(SidConformanceFixtureSpec spec, IReadOnlyList<float> samples, int sampleRate)
	{
		return MeasureOutput(spec, samples.ToArray(), sampleRate);
	}

	private static SampleStats MeasureSamples(float[] samples, int start, int length)
	{
		return new SampleStats(
			Mean(samples, start, length),
			AcRms(samples, start, length),
			Peak(samples, start, length));
	}

	private static double Peak(float[] samples, int start, int length)
	{
		var peak = 0.0;
		for (var i = 0; i < length; i++)
		{
			peak = Math.Max(peak, Math.Abs(samples[start + i]));
		}

		return peak;
	}

	private static float[] TrimSamples(float[] samples, int length)
	{
		if (samples.Length == length)
		{
			return samples;
		}

		var trimmed = new float[length];
		Array.Copy(samples, trimmed, Math.Min(samples.Length, trimmed.Length));
		return trimmed;
	}

	private static int SecondsToSamples(double seconds, int sampleRate)
	{
		return checked((int)Math.Ceiling(seconds * sampleRate));
	}

	private static ushort ParseAddress(string value)
	{
		var address = ParseHex(value, max: 0xFFFF);
		if (address < SidBase || address > SidBase + 0x1F)
		{
			throw new InvalidDataException("SID conformance writes must target SID registers: " + value);
		}

		return (ushort)address;
	}

	private static int ParseHex(string value, int max)
	{
		var text = value.Trim();
		if (text.StartsWith("$", StringComparison.Ordinal))
		{
			text = text[1..];
		}
		else if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			text = text[2..];
		}

		if (!int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed) ||
			(uint)parsed > (uint)max)
		{
			throw new InvalidDataException("Invalid hexadecimal value: " + value);
		}

		return parsed;
	}

	private static T ReadJson<T>(string path)
		where T : class
	{
		return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) ??
			throw new InvalidDataException("Could not parse JSON: " + path);
	}

	private static void WriteJson<T>(string path, T value)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
		File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine);
	}

	private static void WriteComparisonCsv(string path, IReadOnlyList<SidConformanceComparisonResult> results)
	{
		using var writer = new StreamWriter(path);
		writer.WriteLine("id,category,name,output,ref_ac,cand_ac,ac_ratio,diff,corr");
		foreach (var result in results)
		{
			writer.WriteLine(result.ToCsvLine());
		}
	}

	private static void WriteSegmentComparisonCsv(string path, IReadOnlyList<SidConformanceSegmentComparisonResult> results)
	{
		using var writer = new StreamWriter(path);
		writer.WriteLine("id,category,name,segment_index,segment,start_ms,end_ms,ref_mean,ref_ac,ref_peak,cand_player_mean,cand_player_ac,cand_player_peak,player_ratio,player_diff,player_corr,cand_raw_mean,cand_raw_ac,cand_raw_peak");
		foreach (var result in results)
		{
			writer.WriteLine(result.ToCsvLine());
		}
	}

	private static void WriteIndexHtml(
		string path,
		IReadOnlyList<SidConformanceFixture> fixtures,
		IReadOnlyList<SidConformanceComparisonResult> results,
		IReadOnlyList<SidConformanceSegmentComparisonResult> segmentResults,
		SidConformanceDiagnostics diagnostics)
	{
		var byId = results.ToDictionary(result => result.Id, StringComparer.Ordinal);
		var segmentsById = segmentResults
			.GroupBy(result => result.Id, StringComparer.Ordinal)
			.ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
		var builder = new StringBuilder();
		builder.AppendLine("<!doctype html><meta charset=\"utf-8\"><title>SID Conformance</title>");
		builder.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;background:#111;color:#eee}a{color:#8ab4f8}table{border-collapse:collapse;width:100%}td,th{padding:4px 8px;border-bottom:1px solid #333;vertical-align:top}th{text-align:left;color:#bbb}.num{text-align:right;font-variant-numeric:tabular-nums}.meta{color:#bbb;font-size:12px}.thumb img{width:220px;image-rendering:auto}.heat{display:flex;flex-wrap:wrap;gap:2px;max-width:360px}.seg{width:9px;height:18px;border-radius:2px;background:#2d7d46}.seg.warn{background:#b8860b}.seg.bad{background:#b13d3d}.rowwarn{background:#1c1b10}.rowbad{background:#241515}.missing{color:#777}.summary{margin:8px 0 16px;color:#ccc}.summary span{margin-right:16px}</style>");
		builder.AppendLine("<h1>SID Conformance</h1>");
		AppendDiagnosticIndexSummary(builder, diagnostics);
		builder.AppendLine("<table><tr><th>Category</th><th>Fixture</th><th>Tags</th><th>Layer</th><th>Authority</th><th>Ratio</th><th>Corr</th><th>Worst segment</th><th>Segments</th><th>Player</th><th>Raw</th><th>Reference</th></tr>");
		foreach (var fixture in fixtures)
		{
			byId.TryGetValue(fixture.Id, out var result);
			segmentsById.TryGetValue(fixture.Id, out var segments);
			var worst = segments == null || segments.Length == 0 ? null : segments.OrderByDescending(SegmentScore).First();
			builder.Append("<tr class=\"")
				.Append(RowClass(result, worst))
				.Append("\"><td>")
				.Append(fixture.Spec.SidtestCategory.ToString(CultureInfo.InvariantCulture))
				.Append("</td><td>")
				.Append(Html(fixture.Spec.Name))
				.Append("<div class=\"meta\">")
				.Append(Html(fixture.Id))
				.Append("</div></td><td class=\"meta\">")
				.Append(Html(FormatTags(fixture.Spec)))
				.Append("</td><td class=\"meta\">")
				.Append(Html(fixture.Spec.TargetLayer ?? ""))
				.Append("</td><td class=\"meta\">")
				.Append(Html(fixture.Spec.ReferenceAuthority))
				.Append("</td><td>")
				.Append(result == null ? "" : FormatInvariant(result.AcRatio))
				.Append("</td><td>")
				.Append(result == null ? "" : result.Correlation.ToString("0.####", CultureInfo.InvariantCulture))
				.Append("</td><td>")
				.Append(FormatWorstSegment(worst))
				.Append("</td><td>")
				.Append(FormatSegmentHeatmap(segments))
				.Append("</td><td class=\"thumb\"><img src=\"waveforms/")
				.Append(fixture.Id)
				.Append("-candidate.png\"></td><td class=\"thumb\"><img src=\"waveforms/")
				.Append(fixture.Id)
				.Append("-raw.png\"></td><td class=\"thumb\">");
			if (result != null)
			{
				builder.Append("<img src=\"waveforms/")
					.Append(fixture.Id)
					.Append("-reference.png\">");
			}
			else
			{
				builder.Append("<span class=\"missing\">no reference</span>");
			}

			builder.Append("</td></tr>");
		}

		builder.AppendLine("</table>");
		File.WriteAllText(path, builder.ToString());
	}

	private static string FormatTags(SidConformanceFixtureSpec spec)
	{
		if (spec.Tags.Count > 0)
		{
			return string.Join(' ', spec.Tags);
		}

		return spec.Id.StartsWith("sidtest5-", StringComparison.Ordinal)
			? "sidtest5-parity"
			: "stress";
	}

	private static string FormatWorstSegment(SidConformanceSegmentComparisonResult? segment)
	{
		if (segment == null)
		{
			return "";
		}

		return Html(segment.Segment) +
			"<div class=\"meta\">" +
			FormatInvariant(segment.PlayerRatio) +
			"x corr " +
			segment.PlayerCorrelation.ToString("0.####", CultureInfo.InvariantCulture) +
			"</div>";
	}

	private static string FormatSegmentHeatmap(IReadOnlyList<SidConformanceSegmentComparisonResult>? segments)
	{
		if (segments == null || segments.Count == 0)
		{
			return "";
		}

		var builder = new StringBuilder();
		builder.Append("<div class=\"heat\">");
		foreach (var segment in segments)
		{
			builder.Append("<span class=\"seg ")
				.Append(SegmentClass(segment))
				.Append("\" title=\"")
				.Append(Html(segment.Segment))
				.Append(": ")
				.Append(FormatInvariant(segment.PlayerRatio))
				.Append("x, corr ")
				.Append(segment.PlayerCorrelation.ToString("0.####", CultureInfo.InvariantCulture))
				.Append("\"></span>");
		}

		builder.Append("</div>");
		return builder.ToString();
	}

	private static string RowClass(SidConformanceComparisonResult? result, SidConformanceSegmentComparisonResult? worst)
	{
		if (result == null)
		{
			return "";
		}

		var bad = result.AcRatio > 2.5 || result.AcRatio < 0.4 || result.Correlation < -0.05 ||
			(worst != null && SegmentClass(worst) == "bad");
		if (bad)
		{
			return "rowbad";
		}

		var warn = result.AcRatio > 1.35 || result.AcRatio < 0.75 || result.Correlation < 0.35 ||
			(worst != null && SegmentClass(worst) == "warn");
		return warn ? "rowwarn" : "";
	}

	private static string SegmentClass(SidConformanceSegmentComparisonResult segment)
	{
		if (segment.PlayerRatio > 2.5 || segment.PlayerRatio < 0.4 || segment.PlayerCorrelation < -0.05)
		{
			return "bad";
		}

		if (segment.PlayerRatio > 1.35 || segment.PlayerRatio < 0.75 || segment.PlayerCorrelation < 0.35)
		{
			return "warn";
		}

		return "";
	}

	private static double SegmentScore(SidConformanceSegmentComparisonResult segment)
	{
		var ratioScore = Math.Abs(Math.Log(Math.Max(1.0e-12, segment.PlayerRatio), 2.0));
		var correlationScore = Math.Max(0.0, 0.75 - segment.PlayerCorrelation);
		return ratioScore + correlationScore;
	}

	private static string Html(string value)
	{
		return value.Replace("&", "&amp;", StringComparison.Ordinal)
			.Replace("<", "&lt;", StringComparison.Ordinal)
			.Replace(">", "&gt;", StringComparison.Ordinal)
			.Replace("\"", "&quot;", StringComparison.Ordinal);
	}

	private static string EscapeCsv(string value)
	{
		if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
		{
			return value;
		}

		return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
	}

	private static string[] ParseCsvLine(string line)
	{
		var fields = new List<string>();
		var value = new StringBuilder();
		var quoted = false;
		for (var i = 0; i < line.Length; i++)
		{
			var character = line[i];
			if (character == '"')
			{
				if (quoted && i + 1 < line.Length && line[i + 1] == '"')
				{
					value.Append('"');
					i++;
				}
				else
				{
					quoted = !quoted;
				}
			}
			else if (character == ',' && !quoted)
			{
				fields.Add(value.ToString());
				value.Clear();
			}
			else
			{
				value.Append(character);
			}
		}

		fields.Add(value.ToString());
		return fields.ToArray();
	}

	private static string FormatInvariant(double value)
	{
		return value.ToString("0.######", CultureInfo.InvariantCulture);
	}

	private static string FormatSidPlayFpDuration(double seconds)
	{
		return Math.Abs(seconds - Math.Round(seconds)) < 1.0e-9
			? ((int)Math.Round(seconds)).ToString(CultureInfo.InvariantCulture)
			: seconds.ToString("0.000", CultureInfo.InvariantCulture);
	}

	private static void WriteFloatWav(string path, int sampleRate, IReadOnlyList<float> samples)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
		using var stream = File.Create(path);
		using var writer = new BinaryWriter(stream);
		var dataBytes = checked(samples.Count * sizeof(float));
		writer.Write("RIFF"u8);
		writer.Write(36 + dataBytes);
		writer.Write("WAVE"u8);
		writer.Write("fmt "u8);
		writer.Write(16);
		writer.Write((short)3);
		writer.Write((short)1);
		writer.Write(sampleRate);
		writer.Write(sampleRate * sizeof(float));
		writer.Write((short)sizeof(float));
		writer.Write((short)32);
		writer.Write("data"u8);
		writer.Write(dataBytes);
		var sampleBytes = new byte[sizeof(float)];
		foreach (var sample in samples)
		{
			BinaryPrimitives.WriteSingleLittleEndian(sampleBytes, sample);
			writer.Write(sampleBytes);
		}
	}

	private static FloatWav ReadFloatWav(string path)
	{
		using var stream = File.OpenRead(path);
		using var reader = new BinaryReader(stream);
		if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF")
		{
			throw new InvalidDataException("WAV file does not start with RIFF: " + path);
		}

		reader.ReadInt32();
		if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE")
		{
			throw new InvalidDataException("WAV file is not WAVE: " + path);
		}

		int? sampleRate = null;
		short? channels = null;
		short? bitsPerSample = null;
		short? formatTag = null;
		float[]? samples = null;
		while (stream.Position + 8 <= stream.Length)
		{
			var chunkId = Encoding.ASCII.GetString(reader.ReadBytes(4));
			var chunkSize = reader.ReadInt32();
			var chunkStart = stream.Position;
			if (chunkId == "fmt ")
			{
				formatTag = reader.ReadInt16();
				channels = reader.ReadInt16();
				sampleRate = reader.ReadInt32();
				reader.ReadInt32();
				reader.ReadInt16();
				bitsPerSample = reader.ReadInt16();
			}
			else if (chunkId == "data")
			{
				if (formatTag != 3 || channels != 1 || bitsPerSample != 32)
				{
					throw new InvalidDataException("Only mono 32-bit float WAV files are supported: " + path);
				}

				var count = chunkSize / sizeof(float);
				samples = new float[count];
				var sampleBytes = new byte[sizeof(float)];
				for (var i = 0; i < samples.Length; i++)
				{
					reader.ReadExactly(sampleBytes);
					samples[i] = BinaryPrimitives.ReadSingleLittleEndian(sampleBytes);
				}
			}

			stream.Position = chunkStart + chunkSize + (chunkSize & 1);
		}

		if (!sampleRate.HasValue || samples == null)
		{
			throw new InvalidDataException("WAV file is missing fmt or data chunk: " + path);
		}

		return new FloatWav(sampleRate.Value, samples);
	}

	private static void WriteWaveformPng(string path, float[] samples, int sampleRate)
	{
		var sampler = new WaveformBitmapSampler(channelCount: 1, sampleRate, maximumBins: 1024, targetFrameCount: samples.Length);
		sampler.AddSamples(samples, samples.Length);
		var image = WaveformBitmapRenderer.Render(sampler.CreateSnapshot(), width: 1024, height: 180);
		using var stream = File.Create(path);
		WaveformPngWriter.Write(stream, image);
	}

	private static void WriteAscii(byte[] data, int offset, string text)
	{
		Encoding.ASCII.GetBytes(text, data.AsSpan(offset, text.Length));
	}

	private static void WriteFixed(byte[] data, int offset, string text)
	{
		var ascii = Encoding.ASCII.GetBytes(text);
		ascii.AsSpan(0, Math.Min(32, ascii.Length)).CopyTo(data.AsSpan(offset));
	}

	private static void WriteBigEndian(byte[] data, int offset, ushort value)
	{
		BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset, sizeof(ushort)), value);
	}

	private static void WriteBigEndian(byte[] data, int offset, uint value)
	{
		BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset, sizeof(uint)), value);
	}

	private sealed record FloatWav(int SampleRate, float[] Samples);

	private readonly record struct SampleStats(double Mean, double AcRms, double Peak);

	private readonly record struct SidSegmentAccuracyRow(string Name, double ReferenceAc, double CandidateAc);

	private readonly record struct SidCutoffAccuracyPoint(int Index, double ReferenceAc, double CandidateAc);

	private sealed record SidConformanceComparisonResult(
		string Id,
		int Category,
		string Name,
		string Output,
		double ReferenceAc,
		double CandidateAc,
		double Diff,
		double Correlation)
	{
		public double AcRatio => CandidateAc / Math.Max(1.0e-12, ReferenceAc);

		public string ToCsvLine()
		{
			return string.Join(
				',',
				Id,
				Category.ToString(CultureInfo.InvariantCulture),
				EscapeCsv(Name),
				Output,
				FormatInvariant(ReferenceAc),
				FormatInvariant(CandidateAc),
				FormatInvariant(AcRatio),
				FormatInvariant(Diff),
				Correlation.ToString("0.####", CultureInfo.InvariantCulture));
		}
	}

	private sealed record SidConformanceSegmentComparisonResult(
		string Id,
		int Category,
		string Name,
		int SegmentIndex,
		string Segment,
		double StartMs,
		double EndMs,
		double ReferenceMean,
		double ReferenceAc,
		double ReferencePeak,
		double PlayerMean,
		double PlayerAc,
		double PlayerPeak,
		double PlayerRatio,
		double PlayerDiff,
		double PlayerCorrelation,
		double RawMean,
		double RawAc,
		double RawPeak)
	{
		public string ToCsvLine()
		{
			return string.Join(
				',',
				Id,
				Category.ToString(CultureInfo.InvariantCulture),
				EscapeCsv(Name),
				SegmentIndex.ToString(CultureInfo.InvariantCulture),
				EscapeCsv(Segment),
				FormatInvariant(StartMs),
				FormatInvariant(EndMs),
				FormatInvariant(ReferenceMean),
				FormatInvariant(ReferenceAc),
				FormatInvariant(ReferencePeak),
				FormatInvariant(PlayerMean),
				FormatInvariant(PlayerAc),
				FormatInvariant(PlayerPeak),
				FormatInvariant(PlayerRatio),
				FormatInvariant(PlayerDiff),
				PlayerCorrelation.ToString("0.####", CultureInfo.InvariantCulture),
				FormatInvariant(RawMean),
				FormatInvariant(RawAc),
				FormatInvariant(RawPeak));
		}
	}

	private sealed class Mos6510Emitter
	{
		private readonly ushort _baseAddress;
		private readonly List<byte> _bytes = new();
		private readonly Dictionary<string, ushort> _labels = new(StringComparer.Ordinal);
		private readonly List<Patch> _patches = new();

		public Mos6510Emitter(ushort baseAddress)
		{
			_baseAddress = baseAddress;
		}

		public void Label(string name)
		{
			_labels.Add(name, CurrentAddress);
		}

		public void LdaImmediate(byte value)
		{
			_bytes.Add(0xA9);
			_bytes.Add(value);
		}

		public void LdxImmediate(byte value)
		{
			_bytes.Add(0xA2);
			_bytes.Add(value);
		}

		public void LdaZeroPage(byte address)
		{
			_bytes.Add(0xA5);
			_bytes.Add(address);
		}

		public void CmpImmediate(byte value)
		{
			_bytes.Add(0xC9);
			_bytes.Add(value);
		}

		public void StaAbsolute(ushort address)
		{
			_bytes.Add(0x8D);
			AddWord(address);
		}

		public void StaAbsoluteX(ushort address)
		{
			_bytes.Add(0x9D);
			AddWord(address);
		}

		public void StaZeroPage(byte address)
		{
			_bytes.Add(0x85);
			_bytes.Add(address);
		}

		public void IncZeroPage(byte address)
		{
			_bytes.Add(0xE6);
			_bytes.Add(address);
		}

		public void Dex()
		{
			_bytes.Add(0xCA);
		}

		public void Bpl(string label)
		{
			AddRelativeBranch(0x10, label);
		}

		public void Bcc(string label)
		{
			AddRelativeBranch(0x90, label);
		}

		public void Bne(string label)
		{
			AddRelativeBranch(0xD0, label);
		}

		public void Jmp(string label)
		{
			_bytes.Add(0x4C);
			_patches.Add(new Patch(_bytes.Count, label, false));
			AddWord(0);
		}

		public void Rts()
		{
			_bytes.Add(0x60);
		}

		public byte[] ToArray()
		{
			foreach (var patch in _patches)
			{
				if (!_labels.TryGetValue(patch.Label, out var address))
				{
					throw new InvalidDataException("Undefined assembler label: " + patch.Label);
				}

				if (patch.Relative)
				{
					var offset = address - (_baseAddress + patch.Offset + 1);
					if (offset < sbyte.MinValue || offset > sbyte.MaxValue)
					{
						throw new InvalidDataException("Branch target is out of range: " + patch.Label);
					}

					_bytes[patch.Offset] = unchecked((byte)(sbyte)offset);
				}
				else
				{
					_bytes[patch.Offset] = (byte)address;
					_bytes[patch.Offset + 1] = (byte)(address >> 8);
				}
			}

			return _bytes.ToArray();
		}

		private ushort CurrentAddress => checked((ushort)(_baseAddress + _bytes.Count));

		private void AddRelativeBranch(byte opcode, string label)
		{
			_bytes.Add(opcode);
			_patches.Add(new Patch(_bytes.Count, label, true));
			_bytes.Add(0);
		}

		private void AddWord(ushort value)
		{
			_bytes.Add((byte)value);
			_bytes.Add((byte)(value >> 8));
		}

		private readonly record struct Patch(int Offset, string Label, bool Relative);
	}
}

internal sealed class SidConformanceSuite
{
	public SidConformanceSuite(
		string root,
		SidConformanceManifest manifest,
		IReadOnlyList<SidConformanceFixture> fixtures)
	{
		Root = root;
		Manifest = manifest;
		Fixtures = fixtures;
	}

	public string Root { get; }

	public SidConformanceManifest Manifest { get; }

	public IReadOnlyList<SidConformanceFixture> Fixtures { get; }
}

internal sealed class SidConformanceFixture
{
	public SidConformanceFixture(
		string id,
		SidConformanceManifestFixture manifest,
		SidConformanceFixtureSpec spec,
		string specPath,
		string binaryPath)
	{
		Id = id;
		Manifest = manifest;
		Spec = spec;
		SpecPath = specPath;
		BinaryPath = binaryPath;
	}

	public string Id { get; }

	public SidConformanceManifestFixture Manifest { get; }

	public SidConformanceFixtureSpec Spec { get; }

	public string SpecPath { get; }

	public string BinaryPath { get; }
}

internal sealed class SidConformanceManifest
{
	public int Schema { get; set; } = 1;

	public int? SampleRate { get; set; }

	public SidConformanceEvidence? Evidence { get; set; }

	public List<SidConformanceManifestFixture> Fixtures { get; set; } = new();
}

internal sealed class SidConformanceManifestFixture
{
	public string Id { get; set; } = "";

	public int Category { get; set; }

	public string Name { get; set; } = "";

	public string Spec { get; set; } = "";

	public string Binary { get; set; } = "";

	public string Sha256 { get; set; } = "";

	public string ChipModel { get; set; } = "";

	public string Clock { get; set; } = "";

	public SidConformanceAccuracyThreshold? Accuracy { get; set; }
}

internal sealed class SidConformanceEvidence
{
	public string Authority { get; set; } = "sidplayfp";

	public string ReferenceVersion { get; set; } = "";

	public string ReferenceSha256 { get; set; } = "";

	public string Profile { get; set; } = "reference";

	public int SampleRate { get; set; } = SidConformance.DefaultSampleRate;

	public List<string> Metrics { get; set; } = new();
}

internal sealed class SidConformanceAccuracyThreshold
{
	public double MinimumAcRatio { get; set; }

	public double MaximumAcRatio { get; set; }

	public double MinimumSegmentReferenceAc { get; set; }

	public double MaximumSegmentRmsErrorDb { get; set; }

	public double MaximumCutoffLocationErrorFraction { get; set; }

	public string Authority { get; set; } = "sidplayfp-provisional";
}

internal sealed class SidConformanceFixtureSpec
{
	public int Schema { get; set; } = 1;

	public string Id { get; set; } = "";

	public int SidtestCategory { get; set; }

	public string Name { get; set; } = "";

	public string Format { get; set; } = "psid";

	public string Clock { get; set; } = "pal";

	public string ChipModel { get; set; } = "mos6581";

	public int? SampleRate { get; set; }

	public int? SegmentRate { get; set; }

	public double Seconds { get; set; }

	public string Binary { get; set; } = "";

	public List<string> Tags { get; set; } = new();

	public string? TargetLayer { get; set; }

	public string ReferenceAuthority { get; set; } = "sidplayfp";

	public List<SidConformanceWriteSpec> CommonWrites { get; set; } = new();

	public List<SidConformanceSegmentSpec> Segments { get; set; } = new();
}

internal sealed class SidConformanceSegmentSpec
{
	public string Name { get; set; } = "";

	public int Frames { get; set; }

	public List<SidConformanceWriteSpec> Writes { get; set; } = new();
}

internal sealed class SidConformanceWriteSpec
{
	public string Address { get; set; } = "";

	public string Value { get; set; } = "";
}

internal sealed class SidConformanceBaseline
{
	public int Schema { get; set; } = 1;

	public string Profile { get; set; } = "balanced";

	public int SampleRate { get; set; } = SidConformance.DefaultSampleRate;

	public double Tolerance { get; set; } = 0.0001;

	public List<SidConformanceFixtureMetrics> Fixtures { get; set; } = new();
}

internal sealed class SidConformanceFixtureMetrics
{
	public string Id { get; set; } = "";

	public Dictionary<string, SidConformanceOutputMetrics> Outputs { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class SidConformanceOutputMetrics
{
	public double Mean { get; set; }

	public double AcRms { get; set; }

	public double Peak { get; set; }

	public List<SidConformanceSegmentMetrics> Segments { get; set; } = new();
}

internal sealed class SidConformanceSegmentMetrics
{
	public string Name { get; set; } = "";

	public double Mean { get; set; }

	public double AcRms { get; set; }

	public double Peak { get; set; }
}

internal sealed class SidConformanceOptions
{
	private SidConformanceOptions(
		string fixtureDirectory,
		string outputDirectory,
		string? referenceDirectory,
		string? candidateDirectory,
		string? sidPlayFpPath,
		int? sampleRate,
		SidEmulationProfile sidProfile,
		bool overwriteReference,
		bool overwriteCandidate)
	{
		FixtureDirectory = fixtureDirectory;
		OutputDirectory = outputDirectory;
		ReferenceDirectory = referenceDirectory;
		CandidateDirectory = candidateDirectory;
		SidPlayFpPath = sidPlayFpPath;
		SampleRate = sampleRate;
		SidProfile = sidProfile;
		OverwriteReference = overwriteReference;
		OverwriteCandidate = overwriteCandidate;
	}

	public string FixtureDirectory { get; }

	public string OutputDirectory { get; }

	public string? ReferenceDirectory { get; }

	public string? CandidateDirectory { get; }

	public string? SidPlayFpPath { get; }

	public int? SampleRate { get; }

	public SidEmulationProfile SidProfile { get; }

	public bool OverwriteReference { get; }

	public bool OverwriteCandidate { get; }

	public static SidConformanceOptions Parse(string[] args)
	{
		if (args.Length == 0 || IsHelp(args[0]))
		{
			throw new CommandLineException(Usage.Text);
		}

		if (!SidConformance.IsCommand(args))
		{
			throw new CommandLineException("Unknown command. Expected: " + SidConformance.CommandName + ".");
		}

		var positional = new List<string>();
		string? outputDirectory = null;
		string? referenceDirectory = null;
		string? candidateDirectory = null;
		string? sidPlayFpPath = null;
		int? sampleRate = null;
		var sidProfile = SidEmulationProfile.Balanced;
		var overwriteReference = false;
		var overwriteCandidate = false;

		for (var i = 1; i < args.Length; i++)
		{
			var arg = args[i];
			if (!arg.StartsWith("--", StringComparison.Ordinal))
			{
				positional.Add(arg);
				continue;
			}

			switch (arg)
			{
				case "--out":
					outputDirectory = RequireValue(args, ref i, arg);
					break;
				case "--reference-dir":
					referenceDirectory = RequireValue(args, ref i, arg);
					break;
				case "--candidate-dir":
					candidateDirectory = RequireValue(args, ref i, arg);
					break;
				case "--sidplayfp":
					sidPlayFpPath = RequireValue(args, ref i, arg);
					break;
				case "--sample-rate":
					sampleRate = ParsePositiveInt(RequireValue(args, ref i, arg), arg);
					break;
				case "--sid-profile":
					sidProfile = ParseSidProfile(RequireValue(args, ref i, arg));
					break;
				case "--overwrite":
					overwriteReference = true;
					overwriteCandidate = true;
					break;
				case "--overwrite-reference":
					overwriteReference = true;
					break;
				case "--overwrite-candidate":
					overwriteCandidate = true;
					break;
				case "--help":
					throw new CommandLineException(Usage.Text);
				default:
					throw new CommandLineException("Unknown option: " + arg);
			}
		}

		if (positional.Count != 1)
		{
			throw new CommandLineException("compare-sid-conformance requires exactly one fixture directory.");
		}

		if (string.IsNullOrWhiteSpace(outputDirectory))
		{
			throw new CommandLineException("Missing required option: --out");
		}

		return new SidConformanceOptions(
			positional[0],
			outputDirectory,
			referenceDirectory,
			candidateDirectory,
			sidPlayFpPath,
			sampleRate,
			sidProfile,
			overwriteReference,
			overwriteCandidate);
	}

	private static bool IsHelp(string value)
	{
		return string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase);
	}

	private static string RequireValue(string[] args, ref int index, string option)
	{
		if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
		{
			throw new CommandLineException("Missing value for " + option + ".");
		}

		index++;
		return args[index];
	}

	private static int ParsePositiveInt(string value, string option)
	{
		if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) || result <= 0)
		{
			throw new CommandLineException(option + " must be a positive integer.");
		}

		return result;
	}

	private static SidEmulationProfile ParseSidProfile(string value)
	{
		return value.ToLowerInvariant() switch
		{
			"balanced" => SidEmulationProfile.Balanced,
			"reference" or "referencemeasured" or "reference-measured" => SidEmulationProfile.ReferenceMeasured,
			_ => throw new CommandLineException("Unsupported SID profile: " + value + ". Expected balanced or reference.")
		};
	}
}
