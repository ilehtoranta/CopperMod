using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using CopperMod.Sid;

namespace CopperMod.Tools;

internal static class SidTest5ComparisonCommand
{
	public const string CommandName = "compare-sidtest5";
	private const int TestCount = 14;
	private const int DefaultSampleRate = 48000;
	private const double DefaultSeconds = 3.0;
	private const int SidPlayFpTimeoutMilliseconds = 60000;
	private const double ReferenceSilenceAcThreshold = 0.005;

	private static readonly string[] TestNames =
	{
		"BASIC WAVEFORMS",
		"ADSR ORGAN",
		"ADSR PIANO",
		"RING MODULATION",
		"SYNC",
		"LOW-PASS FILTER",
		"HIGH-PASS FILTER",
		"BAND-PASS FILTER",
		"FREQUENCY MODULATION",
		"COMBINED WAVES",
		"PULSE WIDTH SWEEP",
		"MASTER VOLUME MODULATION",
		"RING MOD ALL COMBINATIONS",
		"SYNC + RING MOD"
	};

	public static bool IsCommand(string[] args)
	{
		return args.Length > 0 && string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase);
	}

	public static void Run(string[] args, TextWriter output)
	{
		var options = SidTest5ComparisonOptions.Parse(args);
		var prgDirectory = Path.GetFullPath(options.PrgDirectory);
		if (!Directory.Exists(prgDirectory))
		{
			throw new CommandLineException("sidtest5 PRG directory does not exist: " + prgDirectory);
		}

		var outputDirectory = Path.GetFullPath(options.OutputDirectory);
		var referenceDirectory = Path.GetFullPath(options.ReferenceDirectory ?? Path.Combine(outputDirectory, "sidplayfp"));
		var candidateDirectory = Path.GetFullPath(options.CandidateDirectory ?? Path.Combine(outputDirectory, "coppermod"));
		Directory.CreateDirectory(outputDirectory);
		Directory.CreateDirectory(referenceDirectory);
		Directory.CreateDirectory(candidateDirectory);

		var c64RomPath = string.IsNullOrWhiteSpace(options.C64RomPath)
			? null
			: Path.GetFullPath(options.C64RomPath);
		if (c64RomPath != null && !File.Exists(c64RomPath))
		{
			throw new CommandLineException("C64 ROM file does not exist: " + c64RomPath);
		}

		output.WriteLine("test,name,ref_ac,cand_ac,ac_ratio,diff,corr");
		var results = new List<SidTest5ComparisonResult>(TestCount);
		for (var test = 1; test <= TestCount; test++)
		{
			var prgPath = ResolvePrgPath(prgDirectory, test);
			var referencePath = Path.Combine(referenceDirectory, GetReferenceFileName(test));
			var candidatePath = Path.Combine(candidateDirectory, GetCandidateFileName(test));

			EnsureReferenceWav(options, c64RomPath, prgPath, referenceDirectory, referencePath, test);
			EnsureCandidateWav(options, c64RomPath, prgPath, candidatePath);

			var result = Compare(test, TestNames[test - 1], referencePath, candidatePath);
			results.Add(result);
			output.WriteLine(result.ToCsvLine());
		}

		var csvPath = Path.Combine(outputDirectory, "sidtest5-comparison.csv");
		WriteCsv(csvPath, results);
		WriteSummary(output, results, csvPath);
	}

	private static string ResolvePrgPath(string prgDirectory, int test)
	{
		var path = Path.Combine(prgDirectory, "sidtest5-test" + test.ToString("00", CultureInfo.InvariantCulture) + ".prg");
		if (!File.Exists(path))
		{
			throw new CommandLineException("Missing sidtest5 split PRG: " + path);
		}

		return path;
	}

	private static string GetReferenceFileName(int test)
	{
		return "test" + test.ToString("00", CultureInfo.InvariantCulture) + "-ref.wav";
	}

	private static string GetCandidateFileName(int test)
	{
		return "test" + test.ToString("00", CultureInfo.InvariantCulture) + "-player.wav";
	}

	private static void EnsureReferenceWav(
		SidTest5ComparisonOptions options,
		string? c64RomPath,
		string prgPath,
		string referenceDirectory,
		string referencePath,
		int test)
	{
		if (File.Exists(referencePath) && !options.OverwriteReference)
		{
			return;
		}

		if (string.IsNullOrWhiteSpace(options.SidPlayFpPath))
		{
			throw new CommandLineException("Missing reference WAV " + referencePath + ". Provide --sidplayfp to render it or --reference-dir with existing references.");
		}

		if (string.IsNullOrWhiteSpace(c64RomPath))
		{
			throw new CommandLineException("--c64-rom is required when rendering sidplayfp PRG references.");
		}

		RenderSidPlayFpReference(options, c64RomPath, prgPath, referenceDirectory, test);
		if (!File.Exists(referencePath))
		{
			throw new CommandLineException("sidplayfp did not create the expected reference WAV: " + referencePath);
		}
	}

	private static void EnsureCandidateWav(
		SidTest5ComparisonOptions options,
		string? c64RomPath,
		string prgPath,
		string candidatePath)
	{
		if (File.Exists(candidatePath) && !options.OverwriteCandidate)
		{
			return;
		}

		if (string.IsNullOrWhiteSpace(c64RomPath))
		{
			throw new CommandLineException("--c64-rom is required when rendering CopperMod PRG candidates.");
		}

		var renderArgs = new List<string>
		{
			"render",
			prgPath,
			"--out",
			candidatePath,
			"--seconds",
			FormatInvariant(options.Seconds),
			"--sample-rate",
			options.SampleRate.ToString(CultureInfo.InvariantCulture),
			"--channels",
			"1",
			"--sid-profile",
			FormatSidProfile(options.SidProfile),
			"--c64-rom",
			c64RomPath,
			"--output",
			"player",
			"--c64-profile",
			"c64"
		};
		if (options.OverwriteCandidate)
		{
			renderArgs.Add("--overwrite");
		}

		CopperModTools.Render(RenderCommandOptions.Parse(renderArgs.ToArray()), TextWriter.Null);
	}

	private static void RenderSidPlayFpReference(
		SidTest5ComparisonOptions options,
		string c64RomPath,
		string prgPath,
		string referenceDirectory,
		int test)
	{
		var sidPlayFpPath = Path.GetFullPath(options.SidPlayFpPath!);
		if (!File.Exists(sidPlayFpPath))
		{
			throw new CommandLineException("sidplayfp executable does not exist: " + sidPlayFpPath);
		}

		var appDataRoot = Path.Combine(referenceDirectory, "sidplayfp-appdata");
		PrepareSidPlayFpRomConfig(c64RomPath, appDataRoot);
		var outputBaseName = Path.GetFileNameWithoutExtension(GetReferenceFileName(test));
		var startInfo = new ProcessStartInfo
		{
			FileName = sidPlayFpPath,
			WorkingDirectory = referenceDirectory,
			UseShellExecute = false,
			RedirectStandardError = true,
			RedirectStandardOutput = true
		};
		startInfo.Environment["APPDATA"] = appDataRoot;
		startInfo.ArgumentList.Add("--residfp");
		startInfo.ArgumentList.Add("-cwa");
		startInfo.ArgumentList.Add("-vpf");
		startInfo.ArgumentList.Add("-mof");
		startInfo.ArgumentList.Add("-q");
		startInfo.ArgumentList.Add("-f" + options.SampleRate.ToString(CultureInfo.InvariantCulture));
		startInfo.ArgumentList.Add("-p32");
		startInfo.ArgumentList.Add("-t" + FormatSidPlayFpDuration(options.Seconds));
		startInfo.ArgumentList.Add("-w" + outputBaseName);
		startInfo.ArgumentList.Add(prgPath);

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

			throw new CommandLineException("sidplayfp timed out while rendering " + Path.GetFileName(prgPath) + ".");
		}

		if (process.ExitCode != 0)
		{
			throw new CommandLineException(
				"sidplayfp failed while rendering " + Path.GetFileName(prgPath) + ":" +
				Environment.NewLine +
				stdout +
				stderr);
		}
	}

	private static void PrepareSidPlayFpRomConfig(string c64RomPath, string appDataRoot)
	{
		var rom = File.ReadAllBytes(c64RomPath);
		if (rom.Length != 16 * 1024 && rom.Length != 20 * 1024)
		{
			throw new CommandLineException("--c64-rom must be a 16 KiB BASIC+KERNAL ROM or a 20 KiB combo ROM.");
		}

		var sidPlayFpConfigDirectory = Path.Combine(appDataRoot, "sidplayfp");
		Directory.CreateDirectory(sidPlayFpConfigDirectory);
		var basicPath = Path.Combine(sidPlayFpConfigDirectory, "basic");
		var kernalPath = Path.Combine(sidPlayFpConfigDirectory, "kernal");
		File.WriteAllBytes(basicPath, rom.AsSpan(0, 8192).ToArray());
		File.WriteAllBytes(kernalPath, rom.AsSpan(8192, 8192).ToArray());
		File.WriteAllText(
			Path.Combine(sidPlayFpConfigDirectory, "sidplayfp.ini"),
			"[SIDPlayfp]" + Environment.NewLine +
			"Version=1" + Environment.NewLine +
			"Kernal Rom=" + kernalPath + Environment.NewLine +
			"Basic Rom=" + basicPath + Environment.NewLine +
			Environment.NewLine +
			"[Console]" + Environment.NewLine +
			"Ansi=false" + Environment.NewLine +
			"ASCII=true" + Environment.NewLine);
	}

	private static SidTest5ComparisonResult Compare(int test, string name, string referencePath, string candidatePath)
	{
		var reference = WavData.Read(referencePath);
		var candidate = WavData.Read(candidatePath);
		var length = Math.Min(reference.Samples.Length, candidate.Samples.Length);
		if (length <= 0)
		{
			throw new CommandLineException("Cannot compare empty WAVs for sidtest5 test " + test.ToString(CultureInfo.InvariantCulture) + ".");
		}

		var referenceAc = AcRms(reference.Samples, 0, length);
		if (referenceAc < ReferenceSilenceAcThreshold)
		{
			throw new CommandLineException(
				"SidPlayFP reference for sidtest5 test " + test.ToString(CultureInfo.InvariantCulture) + " appears silent " +
				"(AC RMS " + FormatInvariant(referenceAc) + "). Check the reference render before using this report.");
		}

		var candidateAc = AcRms(candidate.Samples, 0, length);
		return new SidTest5ComparisonResult(
			test,
			name,
			referenceAc,
			candidateAc,
			candidateAc / Math.Max(1.0e-12, referenceAc),
			Diff(reference.Samples, candidate.Samples, 0, 0, length),
			Correlation(reference.Samples, candidate.Samples, 0, 0, length));
	}

	private static void WriteCsv(string path, IReadOnlyList<SidTest5ComparisonResult> results)
	{
		using var writer = new StreamWriter(path);
		writer.WriteLine("test,name,ref_ac,cand_ac,ac_ratio,diff,corr");
		foreach (var result in results)
		{
			writer.WriteLine(result.ToCsvLine());
		}
	}

	private static void WriteSummary(TextWriter output, IReadOnlyList<SidTest5ComparisonResult> results, string csvPath)
	{
		var ratios = results.Select(result => result.AcRatio).OrderBy(value => value).ToArray();
		var median = ratios.Length % 2 == 0
			? (ratios[(ratios.Length / 2) - 1] + ratios[ratios.Length / 2]) * 0.5
			: ratios[ratios.Length / 2];
		output.WriteLine(
			"summary,median_ratio={0},mean_ratio={1},min_ratio={2},max_ratio={3}",
			FormatInvariant(median),
			FormatInvariant(results.Average(result => result.AcRatio)),
			FormatInvariant(ratios[0]),
			FormatInvariant(ratios[^1]));
		output.WriteLine("Wrote " + csvPath);
	}

	private static double Mean(float[] samples, int start, int length)
	{
		var sum = 0.0;
		for (var i = 0; i < length; i++)
		{
			sum += samples[start + i];
		}

		return sum / length;
	}

	private static double AcRms(float[] samples, int start, int length)
	{
		var mean = Mean(samples, start, length);
		var energy = 0.0;
		for (var i = 0; i < length; i++)
		{
			var value = samples[start + i] - mean;
			energy += value * value;
		}

		return Math.Sqrt(energy / length);
	}

	private static double Diff(float[] left, float[] right, int leftStart, int rightStart, int length)
	{
		var energy = 0.0;
		for (var i = 0; i < length; i++)
		{
			var value = left[leftStart + i] - right[rightStart + i];
			energy += value * value;
		}

		return Math.Sqrt(energy / length);
	}

	private static double Correlation(float[] left, float[] right, int leftStart, int rightStart, int length)
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

	private static string FormatSidProfile(SidEmulationProfile sidProfile)
	{
		return sidProfile == SidEmulationProfile.ReferenceMeasured ? "reference" : "balanced";
	}

	private static string FormatSidPlayFpDuration(double seconds)
	{
		return Math.Abs(seconds - Math.Round(seconds)) < 1.0e-9
			? ((int)Math.Round(seconds)).ToString(CultureInfo.InvariantCulture)
			: seconds.ToString("0.000", CultureInfo.InvariantCulture);
	}

	private static string FormatInvariant(double value)
	{
		return value.ToString("0.######", CultureInfo.InvariantCulture);
	}

	private sealed record SidTest5ComparisonResult(
		int Test,
		string Name,
		double ReferenceAc,
		double CandidateAc,
		double AcRatio,
		double Diff,
		double Correlation)
	{
		public string ToCsvLine()
		{
			return string.Join(
				',',
				Test.ToString(CultureInfo.InvariantCulture),
				Name,
				FormatInvariant(ReferenceAc),
				FormatInvariant(CandidateAc),
				FormatInvariant(AcRatio),
				FormatInvariant(Diff),
				Correlation.ToString("0.####", CultureInfo.InvariantCulture));
		}
	}

	private sealed class SidTest5ComparisonOptions
	{
		private SidTest5ComparisonOptions(
			string prgDirectory,
			string outputDirectory,
			string? referenceDirectory,
			string? candidateDirectory,
			string? sidPlayFpPath,
			string? c64RomPath,
			double seconds,
			int sampleRate,
			SidEmulationProfile sidProfile,
			bool overwriteReference,
			bool overwriteCandidate)
		{
			PrgDirectory = prgDirectory;
			OutputDirectory = outputDirectory;
			ReferenceDirectory = referenceDirectory;
			CandidateDirectory = candidateDirectory;
			SidPlayFpPath = sidPlayFpPath;
			C64RomPath = c64RomPath;
			Seconds = seconds;
			SampleRate = sampleRate;
			SidProfile = sidProfile;
			OverwriteReference = overwriteReference;
			OverwriteCandidate = overwriteCandidate;
		}

		public string PrgDirectory { get; }

		public string OutputDirectory { get; }

		public string? ReferenceDirectory { get; }

		public string? CandidateDirectory { get; }

		public string? SidPlayFpPath { get; }

		public string? C64RomPath { get; }

		public double Seconds { get; }

		public int SampleRate { get; }

		public SidEmulationProfile SidProfile { get; }

		public bool OverwriteReference { get; }

		public bool OverwriteCandidate { get; }

		public static SidTest5ComparisonOptions Parse(string[] args)
		{
			if (args.Length == 0 || IsHelp(args[0]))
			{
				throw new CommandLineException(Usage.Text);
			}

			if (!IsCommand(args))
			{
				throw new CommandLineException("Unknown command. Expected: " + CommandName + ".");
			}

			var positional = new List<string>();
			string? outputDirectory = null;
			string? referenceDirectory = null;
			string? candidateDirectory = null;
			string? sidPlayFpPath = null;
			string? c64RomPath = null;
			var seconds = DefaultSeconds;
			var sampleRate = DefaultSampleRate;
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
					case "--c64-rom":
						c64RomPath = RequireValue(args, ref i, arg);
						break;
					case "--seconds":
						seconds = ParsePositiveDouble(RequireValue(args, ref i, arg), arg);
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
				throw new CommandLineException("compare-sidtest5 requires exactly one sidtest5 split PRG directory.");
			}

			if (string.IsNullOrWhiteSpace(outputDirectory))
			{
				throw new CommandLineException("Missing required option: --out");
			}

			return new SidTest5ComparisonOptions(
				positional[0],
				outputDirectory,
				referenceDirectory,
				candidateDirectory,
				sidPlayFpPath,
				c64RomPath,
				seconds,
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

		private static double ParsePositiveDouble(string value, string option)
		{
			if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) || result <= 0.0)
			{
				throw new CommandLineException(option + " must be a positive number.");
			}

			return result;
		}

		private static SidEmulationProfile ParseSidProfile(string value)
		{
			return value.ToLowerInvariant() switch
			{
				"balanced" => SidEmulationProfile.Balanced,
				"reference" => SidEmulationProfile.ReferenceMeasured,
				"reference-measured" => SidEmulationProfile.ReferenceMeasured,
				"measured" => SidEmulationProfile.ReferenceMeasured,
				_ => throw new CommandLineException("Unsupported SID emulation profile: " + value)
			};
		}
	}

	private sealed record WavData(int SampleRate, float[] Samples)
	{
		public static WavData Read(string path)
		{
			using var stream = File.OpenRead(path);
			using var reader = new BinaryReader(stream);
			static string FourCc(BinaryReader reader) => new(reader.ReadChars(4));
			if (FourCc(reader) != "RIFF")
			{
				throw new InvalidDataException("Not a RIFF WAV: " + path);
			}

			_ = reader.ReadUInt32();
			if (FourCc(reader) != "WAVE")
			{
				throw new InvalidDataException("Not a WAVE file: " + path);
			}

			var audioFormat = 0;
			var channels = 0;
			var sampleRate = 0;
			var blockAlign = 0;
			var bitsPerSample = 0;
			byte[]? data = null;
			while (stream.Position + 8 <= stream.Length)
			{
				var id = FourCc(reader);
				var size = reader.ReadUInt32();
				var start = stream.Position;
				if (id == "fmt ")
				{
					var bytes = reader.ReadBytes(checked((int)size));
					audioFormat = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0, 2));
					channels = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2, 2));
					sampleRate = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4));
					blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(12, 2));
					bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(14, 2));
				}
				else if (id == "data")
				{
					data = reader.ReadBytes(checked((int)size));
				}

				stream.Position = start + size + (size & 1);
			}

			if (channels <= 0 || blockAlign <= 0 || data == null)
			{
				throw new InvalidDataException("WAV format or data chunk missing: " + path);
			}

			var frames = data.Length / blockAlign;
			var samples = new float[frames];
			var bytesPerSample = bitsPerSample / 8;
			for (var frame = 0; frame < frames; frame++)
			{
				var sum = 0.0;
				for (var channel = 0; channel < channels; channel++)
				{
					var offset = (frame * blockAlign) + (channel * bytesPerSample);
					sum += DecodeSample(data.AsSpan(offset, bytesPerSample), audioFormat, bitsPerSample);
				}

				samples[frame] = (float)(sum / channels);
			}

			return new WavData(sampleRate, samples);
		}

		private static double DecodeSample(ReadOnlySpan<byte> bytes, int audioFormat, int bitsPerSample)
		{
			if (audioFormat == 3 && bitsPerSample == 32)
			{
				return BinaryPrimitives.ReadSingleLittleEndian(bytes);
			}

			if (audioFormat != 1)
			{
				throw new InvalidDataException("Unsupported WAV format: " + audioFormat.ToString(CultureInfo.InvariantCulture));
			}

			return bitsPerSample switch
			{
				16 => BinaryPrimitives.ReadInt16LittleEndian(bytes) / 32768.0,
				32 => BinaryPrimitives.ReadInt32LittleEndian(bytes) / 2147483648.0,
				_ => throw new InvalidDataException("Unsupported PCM bit depth: " + bitsPerSample.ToString(CultureInfo.InvariantCulture))
			};
		}
	}
}
