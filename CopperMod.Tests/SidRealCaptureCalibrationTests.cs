using System.Diagnostics;
using System.Globalization;
using CopperMod.Abstractions;
using CopperMod.Rendering;
using CopperMod.Sid;

namespace CopperMod.Tests;

public sealed class SidRealCaptureCalibrationTests
{
	private const int SampleRate = 44100;
	private const double RenderSeconds = 45.0;
	private const double PreDrumStartSeconds = 30.0;
	private const double PreDrumDurationSeconds = 8.0;
	private const double DrumStartSeconds = 39.5;
	private const double DrumDurationSeconds = 2.0;

	[Fact]
	public void OptionalGreatGianaSistersSubtuneOneD418DrumMatchesRealC64Capture()
	{
		if (Environment.GetEnvironmentVariable("SID_REAL_CAPTURE_TESTS") != "1")
		{
			return;
		}

		var sidPath = FindWorkspaceFile("TestTunes", "SID", "Tough", "Great_Giana_Sisters.sid");
		var capturePath = FindWorkspaceFile("TestTunes", "SID", "Great Giana Sisters", "new19ck_giana1.flac");
		if (!File.Exists(sidPath) || !File.Exists(capturePath))
		{
			return;
		}

		var capture = TryDecodeFlacToMonoFloat(capturePath, SampleRate);
		if (capture.Length == 0)
		{
			return;
		}

		var rawRender = RenderGianaSubtuneOneRawMono(sidPath, SampleRate, RenderSeconds);
		var current = rawRender.ToArray();
		new C64OutputStage(C64OutputProfile.C64).Process(current, channels: 1, sampleRate: SampleRate);
		var legacy = rawRender.ToArray();
		LegacyC64OutputShape(legacy, SampleRate);

		var preStart = SecondsToFrames(PreDrumStartSeconds);
		var preLength = SecondsToFrames(PreDrumDurationSeconds);
		var drumStart = SecondsToFrames(DrumStartSeconds);
		var drumLength = SecondsToFrames(DrumDurationSeconds);
		Assert.True(capture.Length > drumStart + drumLength, "Real C64 capture is too short for the Giana drum window.");
		Assert.True(current.Length > drumStart + drumLength, "Rendered Giana output is too short for the drum window.");

		var currentOffset = FindBestCandidateOffset(capture, current, preStart, preLength, maxOffset: SampleRate * 3);
		var legacyOffset = FindBestCandidateOffset(capture, legacy, preStart, preLength, maxOffset: SampleRate * 3);
		var currentScale = Rms(capture, preStart, preLength) / Math.Max(1e-9, Rms(current, preStart + currentOffset, preLength));
		var legacyScale = Rms(capture, preStart, preLength) / Math.Max(1e-9, Rms(legacy, preStart + legacyOffset, preLength));

		var captureEnergy = LowBandTransientRms(capture, drumStart, drumLength, scale: 1.0);
		var currentEnergy = LowBandTransientRms(current, drumStart + currentOffset, drumLength, currentScale);
		var legacyEnergy = LowBandTransientRms(legacy, drumStart + legacyOffset, drumLength, legacyScale);
		var currentPreDistance = BroadbandDistance(capture, current, preStart, preStart + currentOffset, preLength, currentScale);
		var legacyPreDistance = BroadbandDistance(capture, legacy, preStart, preStart + legacyOffset, preLength, legacyScale);

		Assert.True(
			currentEnergy >= legacyEnergy * 1.25,
			FormatMetric("Expected calibrated D418/output profile to improve Giana drum low-band transient energy", currentEnergy, legacyEnergy));
		Assert.True(
			currentEnergy >= captureEnergy * 0.60,
			FormatMetric("Expected rendered Giana drum low-band transient energy to approach the real C64 capture", currentEnergy, captureEnergy));
		Assert.True(
			currentPreDistance <= legacyPreDistance * 1.05,
			FormatMetric("Expected pre-drum broadband distance not to regress materially", currentPreDistance, legacyPreDistance));
	}

	private static float[] RenderGianaSubtuneOneRawMono(string sidPath, int sampleRate, double seconds)
	{
		using var song = new SidFormat().Load(File.ReadAllBytes(sidPath));
		var options = new AudioRenderOptions(sampleRate, channelCount: 1);
		var targetFrames = SecondsToFrames(seconds);
		var samples = new List<float>(targetFrames + sampleRate);
		while (samples.Count < targetFrames)
		{
			var frames = song.GetCurrentTickFrameCount(options);
			var buffer = new float[options.GetSampleCount(frames)];
			var result = song.RenderTick(buffer, options);
			var written = Math.Min(result.SamplesWritten, buffer.Length);
			for (var i = 0; i < written && samples.Count < targetFrames; i++)
			{
				samples.Add(buffer[i]);
			}
		}

		return samples.ToArray();
	}

	private static float[] TryDecodeFlacToMonoFloat(string path, int sampleRate)
	{
		var ffmpeg = Environment.GetEnvironmentVariable("FFMPEG_EXE");
		if (string.IsNullOrWhiteSpace(ffmpeg))
		{
			ffmpeg = "ffmpeg";
		}

		var temp = Path.Combine(Path.GetTempPath(), "coppermod-real-c64-" + Guid.NewGuid().ToString("N") + ".f32");
		try
		{
			var startInfo = new ProcessStartInfo
			{
				FileName = ffmpeg,
				UseShellExecute = false,
				RedirectStandardError = true,
				RedirectStandardOutput = true
			};
			startInfo.ArgumentList.Add("-hide_banner");
			startInfo.ArgumentList.Add("-loglevel");
			startInfo.ArgumentList.Add("error");
			startInfo.ArgumentList.Add("-y");
			startInfo.ArgumentList.Add("-i");
			startInfo.ArgumentList.Add(path);
			startInfo.ArgumentList.Add("-ac");
			startInfo.ArgumentList.Add("1");
			startInfo.ArgumentList.Add("-ar");
			startInfo.ArgumentList.Add(sampleRate.ToString(CultureInfo.InvariantCulture));
			startInfo.ArgumentList.Add("-f");
			startInfo.ArgumentList.Add("f32le");
			startInfo.ArgumentList.Add(temp);

			using var process = Process.Start(startInfo);
			if (process == null)
			{
				return Array.Empty<float>();
			}

			process.WaitForExit();
			if (process.ExitCode != 0 || !File.Exists(temp))
			{
				return Array.Empty<float>();
			}

			var bytes = File.ReadAllBytes(temp);
			var samples = new float[bytes.Length / sizeof(float)];
			Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * sizeof(float));
			return samples;
		}
		catch (Exception) when (Environment.GetEnvironmentVariable("SID_REAL_CAPTURE_TESTS_STRICT") != "1")
		{
			return Array.Empty<float>();
		}
		finally
		{
			if (File.Exists(temp))
			{
				File.Delete(temp);
			}
		}
	}

	private static int FindBestCandidateOffset(float[] reference, float[] candidate, int referenceStart, int length, int maxOffset)
	{
		var coarseOffset = FindBestCandidateOffset(reference, candidate, referenceStart, length, maxOffset, step: 32);
		var refineStart = Math.Max(-maxOffset, coarseOffset - 96);
		var refineEnd = Math.Min(maxOffset, coarseOffset + 96);
		return FindBestCandidateOffset(reference, candidate, referenceStart, length, refineStart, refineEnd, step: 1);
	}

	private static int FindBestCandidateOffset(
		float[] reference,
		float[] candidate,
		int referenceStart,
		int length,
		int maxOffset,
		int step)
	{
		return FindBestCandidateOffset(reference, candidate, referenceStart, length, -maxOffset, maxOffset, step);
	}

	private static int FindBestCandidateOffset(
		float[] reference,
		float[] candidate,
		int referenceStart,
		int length,
		int offsetStart,
		int offsetEnd,
		int step)
	{
		var bestOffset = 0;
		var bestCorrelation = double.NegativeInfinity;
		for (var offset = offsetStart; offset <= offsetEnd; offset += step)
		{
			if (referenceStart + offset < 0 || referenceStart + offset + length >= candidate.Length)
			{
				continue;
			}

			var correlation = NormalizedCorrelation(reference, candidate, referenceStart, referenceStart + offset, length, stride: 16);
			if (correlation > bestCorrelation)
			{
				bestCorrelation = correlation;
				bestOffset = offset;
			}
		}

		return bestOffset;
	}

	private static double NormalizedCorrelation(
		float[] reference,
		float[] candidate,
		int referenceStart,
		int candidateStart,
		int length,
		int stride)
	{
		var sumXY = 0.0;
		var sumX2 = 0.0;
		var sumY2 = 0.0;
		for (var i = 0; i < length; i += stride)
		{
			var x = reference[referenceStart + i];
			var y = candidate[candidateStart + i];
			sumXY += x * y;
			sumX2 += x * x;
			sumY2 += y * y;
		}

		return sumXY / Math.Sqrt(Math.Max(1e-18, sumX2 * sumY2));
	}

	private static double LowBandTransientRms(float[] samples, int start, int length, double scale)
	{
		var alpha = 1.0 - Math.Exp(-2.0 * Math.PI * 220.0 / SampleRate);
		var low = 0.0;
		var values = new double[length];
		var mean = 0.0;
		for (var i = 0; i < length; i++)
		{
			low += ((samples[start + i] * scale) - low) * alpha;
			values[i] = low;
			mean += low;
		}

		mean /= Math.Max(1, length);
		var sumSquares = 0.0;
		for (var i = 0; i < values.Length; i++)
		{
			var centered = values[i] - mean;
			sumSquares += centered * centered;
		}

		return Math.Sqrt(sumSquares / Math.Max(1, values.Length));
	}

	private static double BroadbandDistance(
		float[] reference,
		float[] candidate,
		int referenceStart,
		int candidateStart,
		int length,
		double candidateScale)
	{
		ReadOnlySpan<double> frequencies = stackalloc double[] { 80, 160, 320, 640, 1280, 2560, 5120, 10000 };
		var distance = 0.0;
		for (var i = 0; i < frequencies.Length; i++)
		{
			var refMagnitude = GoertzelMagnitude(reference, referenceStart, length, frequencies[i], scale: 1.0);
			var candidateMagnitude = GoertzelMagnitude(candidate, candidateStart, length, frequencies[i], candidateScale);
			distance += Math.Abs(Math.Log((candidateMagnitude + 1e-9) / (refMagnitude + 1e-9)));
		}

		return distance / frequencies.Length;
	}

	private static double GoertzelMagnitude(float[] samples, int start, int length, double frequency, double scale)
	{
		var omega = 2.0 * Math.PI * frequency / SampleRate;
		var coefficient = 2.0 * Math.Cos(omega);
		var s0 = 0.0;
		var s1 = 0.0;
		var s2 = 0.0;
		for (var i = 0; i < length; i++)
		{
			s0 = (samples[start + i] * scale) + (coefficient * s1) - s2;
			s2 = s1;
			s1 = s0;
		}

		return Math.Sqrt((s1 * s1) + (s2 * s2) - (coefficient * s1 * s2)) / Math.Max(1, length);
	}

	private static double Rms(float[] samples, int start, int length)
	{
		var sumSquares = 0.0;
		for (var i = 0; i < length; i++)
		{
			var sample = samples[start + i];
			sumSquares += sample * sample;
		}

		return Math.Sqrt(sumSquares / Math.Max(1, length));
	}

	private static void LegacyC64OutputShape(Span<float> samples, int sampleRate)
	{
		const double dcBlockCutoffHz = 1.0;
		const double outputLowPassCutoffHz = 18000.0;
		const float edgeEmphasis = 0.1f;
		const float outputHeadroom = 0.555f;
		var lowPassAlpha = 1.0 - Math.Exp(-2.0 * Math.PI * outputLowPassCutoffHz / sampleRate);
		var highPassAlpha = GetHighPassAlpha(dcBlockCutoffHz, sampleRate);
		var lowPass = 0.0f;
		var previousInput = 0.0f;
		var previousOutput = 0.0f;
		var previousEdgeInput = 0.0f;
		var edgeInitialized = false;
		for (var i = 0; i < samples.Length; i++)
		{
			var sample = lowPass + ((samples[i] - lowPass) * (float)lowPassAlpha);
			lowPass = sample;
			var highPassed = (float)(highPassAlpha * (previousOutput + sample - previousInput));
			previousInput = sample;
			previousOutput = highPassed;
			var edge = edgeInitialized ? highPassed - previousEdgeInput : 0.0f;
			edgeInitialized = true;
			previousEdgeInput = highPassed;
			samples[i] = Math.Clamp((highPassed + (edge * edgeEmphasis)) * outputHeadroom, -1.0f, 1.0f);
		}
	}

	private static double GetHighPassAlpha(double cutoffHz, int sampleRate)
	{
		var rc = 1.0 / (2.0 * Math.PI * cutoffHz);
		var dt = 1.0 / sampleRate;
		return rc / (rc + dt);
	}

	private static string FindWorkspaceFile(params string[] parts)
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory != null)
		{
			var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
			if (File.Exists(candidate))
			{
				return candidate;
			}

			if (parts.Length > 0)
			{
				var searchRoot = Path.Combine(new[] { directory.FullName }.Concat(parts.Take(parts.Length - 1)).ToArray());
				if (Directory.Exists(searchRoot))
				{
					var recursiveCandidate = Directory.EnumerateFiles(searchRoot, parts[^1], SearchOption.AllDirectories).FirstOrDefault();
					if (recursiveCandidate != null)
					{
						return recursiveCandidate;
					}
				}
			}

			directory = directory.Parent;
		}

		return string.Join(Path.DirectorySeparatorChar, parts);
	}

	private static int SecondsToFrames(double seconds)
	{
		return (int)Math.Round(seconds * SampleRate, MidpointRounding.AwayFromZero);
	}

	private static string FormatMetric(string message, double actual, double expected)
	{
		return $"{message}: actual {actual:0.000000}, reference {expected:0.000000}.";
	}
}
