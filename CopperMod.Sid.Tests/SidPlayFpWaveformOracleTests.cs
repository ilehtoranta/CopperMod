using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using CopperMod.Abstractions;

namespace CopperMod.Sid.Tests;

public sealed class SidPlayFpWaveformOracleTests
{
	private const int SampleRate = 96000;
	private const int FramesPerSegment = 18;
	private const int SegmentRate = 50;
	private const byte FrameCounterAddress = 0x02;
	private const ushort SidBase = 0xD400;
	private const ushort ProgramBase = 0x1000;
	private const double CombinedHarmonicRelativeTolerance = 0.50;
	private const double CombinedHarmonicAbsoluteTolerance = 0.055;
	private const double CombinedNearNullRatioLimit = 0.055;

	private static readonly OracleSegment[] Segments =
	{
		new("triangle", OracleSegmentKind.Correlation, 0x1000, 0.95),
		new("saw", OracleSegmentKind.Correlation, 0x1000, 0.95),
		new("pulse", OracleSegmentKind.Correlation, 0x1000, 0.95),
		new("noise", OracleSegmentKind.Noise, 0x4000, 0.0),
		new("sync-fm", OracleSegmentKind.Correlation, 0x1800, 0.85),
		new("ring", OracleSegmentKind.Correlation, 0x1300, 0.85),
		new("triangle-saw", OracleSegmentKind.Harmonics, 0x1000, 0.0),
		new("triangle-pulse", OracleSegmentKind.Harmonics, 0x1000, 0.0),
		new("saw-pulse", OracleSegmentKind.Harmonics, 0x1000, 0.0),
		new("triangle-saw-pulse", OracleSegmentKind.Harmonics, 0x1000, 0.0),
		new("noise-saw", OracleSegmentKind.NoiseCombined, 0x4000, 0.0)
	};

	private static readonly double RenderSeconds = (double)(Segments.Length * FramesPerSegment) / SegmentRate;

	[Fact]
	public void OptionalGeneratedWaveformFixtureMatchesSidPlayFpOracle()
	{
		if (Environment.GetEnvironmentVariable("SIDPLAYFP_ORACLE_TESTS") != "1")
		{
			return;
		}

		var sidPlayFp = Environment.GetEnvironmentVariable("SIDPLAYFP_EXE");
		if (string.IsNullOrWhiteSpace(sidPlayFp))
		{
			sidPlayFp = @"D:\Models\sidplayfp-3.0.2-ucrt64\sidplayfp.exe";
		}

		Assert.True(File.Exists(sidPlayFp), "SidPlayFP executable was not found: " + sidPlayFp);

		var root = Path.Combine(Path.GetTempPath(), "coppermod-sidplayfp-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			var sidPath = Path.Combine(root, "input.sid");
			var wavPath = Path.Combine(root, "input.wav");
			File.WriteAllBytes(sidPath, CreateOracleSid());

			RunSidPlayFp(sidPlayFp, root);
			Assert.True(File.Exists(wavPath), "SidPlayFP did not create the expected oracle WAV: " + wavPath);

			var reference = MeasurementWav.Read(wavPath);
			var candidate = RenderCopperMod(sidPath, RenderSeconds);
			Assert.Equal(SampleRate, reference.SampleRate);
			Assert.True(reference.Samples.Length >= SecondsToSamples(RenderSeconds) - SampleRate / 20);
			Assert.True(candidate.Length >= SecondsToSamples(RenderSeconds) - SampleRate / 20);

			for (var i = 0; i < Segments.Length; i++)
			{
				AssertSegmentMatches(Segments[i], i, reference.Samples, candidate);
			}
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	private static void AssertSegmentMatches(OracleSegment segment, int segmentIndex, float[] reference, float[] candidate)
	{
		var start = SecondsToSamples(((segmentIndex * FramesPerSegment) / (double)SegmentRate) + 0.10);
		var length = SecondsToSamples(0.18);
		Assert.True(reference.Length > start + length, segment.Name + " reference window is outside the SidPlayFP capture.");
		Assert.True(candidate.Length > start + length, segment.Name + " candidate window is outside the CopperMod render.");

		var offset = FindBestCandidateOffset(reference, candidate, start, length, maxOffset: SampleRate / 40);
		switch (segment.Kind)
		{
			case OracleSegmentKind.Correlation:
				var correlation = Correlation(reference, candidate, start, start + offset, length);
				Assert.True(
					correlation >= segment.MinimumCorrelation,
					$"{segment.Name} correlation {correlation:0.000} was below {segment.MinimumCorrelation:0.000} at candidate offset {offset}.");
				break;
			case OracleSegmentKind.Harmonics:
				AssertHarmonicRatios(segment, reference, candidate, start, start + offset, length);
				break;
			case OracleSegmentKind.Noise:
				AssertNoiseShape(segment, reference, candidate, start, start + offset, length);
				break;
			case OracleSegmentKind.NoiseCombined:
				AssertNoiseCombinedShape(segment, reference, candidate, start, start + offset, length);
				break;
		}
	}

	private static byte[] CreateOracleSid()
	{
		var asm = new Mos6510Emitter(ProgramBase);
		asm.Label("init");
		EmitClearSid(asm);
		asm.LdaImm(0);
		asm.StaZp(FrameCounterAddress);
		asm.Rts();

		asm.Label("play");
		asm.LdaZp(FrameCounterAddress);
		for (var i = 0; i < Segments.Length - 1; i++)
		{
			asm.CmpImm((byte)((i + 1) * FramesPerSegment));
			asm.Bcc("route" + i.ToString(CultureInfo.InvariantCulture));
		}

		asm.Jmp("route" + (Segments.Length - 1).ToString(CultureInfo.InvariantCulture));
		for (var i = 0; i < Segments.Length; i++)
		{
			asm.Label("route" + i.ToString(CultureInfo.InvariantCulture));
			asm.Jmp("segment" + i.ToString(CultureInfo.InvariantCulture));
		}

		for (var i = 0; i < Segments.Length; i++)
		{
			asm.Label("segment" + i.ToString(CultureInfo.InvariantCulture));
			EmitSegment(asm, i);
			asm.Jmp("done");
		}

		asm.Label("done");
		asm.IncZp(FrameCounterAddress);
		asm.Rts();

		return SidFixtureBuilder.CreatePsid(
			asm.ToArray(),
			loadAddress: ProgramBase,
			initAddress: ProgramBase,
			playAddress: asm.AddressOf("play"),
			songs: 1,
			startSong: 1,
			speed: 0,
			flags: (1 << 2) | (1 << 4),
			title: "CopperMod SID Waveform Oracle",
			author: "CopperMod",
			released: "2026");
	}

	private static void EmitSegment(Mos6510Emitter asm, int segmentIndex)
	{
		EmitSegmentResetIfFirstFrame(asm, segmentIndex);
		EmitCommonSidSetup(asm);
		switch (segmentIndex)
		{
			case 0:
				EmitSingleVoice(asm, voice: 0, frequency: 0x1000, pulseWidth: 0x0800, control: 0x11);
				break;
			case 1:
				EmitSingleVoice(asm, voice: 0, frequency: 0x1000, pulseWidth: 0x0800, control: 0x21);
				break;
			case 2:
				EmitSingleVoice(asm, voice: 0, frequency: 0x1000, pulseWidth: 0x0800, control: 0x41);
				break;
			case 3:
				EmitSingleVoice(asm, voice: 0, frequency: 0x4000, pulseWidth: 0x0800, control: 0x81);
				break;
			case 4:
				EmitHardSyncFrequencyModulation(asm);
				break;
			case 5:
				EmitRingModulation(asm);
				break;
			case 6:
				EmitSingleVoice(asm, voice: 0, frequency: 0x1000, pulseWidth: 0x0800, control: 0x31);
				break;
			case 7:
				EmitSingleVoice(asm, voice: 0, frequency: 0x1000, pulseWidth: 0x0800, control: 0x51);
				break;
			case 8:
				EmitSingleVoice(asm, voice: 0, frequency: 0x1000, pulseWidth: 0x0800, control: 0x61);
				break;
			case 9:
				EmitSingleVoice(asm, voice: 0, frequency: 0x1000, pulseWidth: 0x0800, control: 0x71);
				break;
			default:
				EmitSingleVoice(asm, voice: 0, frequency: 0x4000, pulseWidth: 0x0800, control: 0xA1);
				break;
		}
	}

	private static void EmitSegmentResetIfFirstFrame(Mos6510Emitter asm, int segmentIndex)
	{
		var skipLabel = "segment-reset-skip" + segmentIndex.ToString(CultureInfo.InvariantCulture);
		asm.LdaZp(FrameCounterAddress);
		asm.CmpImm((byte)(segmentIndex * FramesPerSegment));
		asm.Bne(skipLabel);
		asm.LdaImm(0x08);
		asm.StaAbs(SidBase + 0x04);
		asm.StaAbs(SidBase + 0x0B);
		asm.StaAbs(SidBase + 0x12);
		asm.LdaImm(0);
		asm.StaAbs(SidBase + 0x04);
		asm.StaAbs(SidBase + 0x0B);
		asm.StaAbs(SidBase + 0x12);
		asm.Label(skipLabel);
	}

	private static void EmitClearSid(Mos6510Emitter asm)
	{
		asm.LdxImm(0x18);
		asm.LdaImm(0);
		asm.Label("clear-sid");
		asm.StaAbsX(SidBase);
		asm.Dex();
		asm.Bpl("clear-sid");
		asm.LdaImm(0x08);
		asm.StaAbs(SidBase + 0x04);
		asm.StaAbs(SidBase + 0x0B);
		asm.StaAbs(SidBase + 0x12);
		asm.LdaImm(0);
		asm.StaAbs(SidBase + 0x04);
		asm.StaAbs(SidBase + 0x0B);
		asm.StaAbs(SidBase + 0x12);
	}

	private static void EmitCommonSidSetup(Mos6510Emitter asm)
	{
		asm.LdaImm(0);
		asm.StaAbs(SidBase + 0x04);
		asm.StaAbs(SidBase + 0x0B);
		asm.StaAbs(SidBase + 0x12);
		asm.StaAbs(SidBase + 0x15);
		asm.StaAbs(SidBase + 0x16);
		asm.StaAbs(SidBase + 0x17);
		for (var voice = 0; voice < 3; voice++)
		{
			var offset = voice * 7;
			asm.StaAbs((ushort)(SidBase + offset + 5));
			asm.LdaImm(0xF0);
			asm.StaAbs((ushort)(SidBase + offset + 6));
			asm.LdaImm(0);
		}

		asm.LdaImm(0x0F);
		asm.StaAbs(SidBase + 0x18);
	}

	private static void EmitSingleVoice(Mos6510Emitter asm, int voice, ushort frequency, ushort pulseWidth, byte control)
	{
		for (var other = 0; other < 3; other++)
		{
			if (other != voice)
			{
				asm.LdaImm(0);
				asm.StaAbs((ushort)(SidBase + (other * 7) + 4));
			}
		}

		EmitVoiceRegisters(asm, voice, frequency, pulseWidth, control);
	}

	private static void EmitHardSyncFrequencyModulation(Mos6510Emitter asm)
	{
		asm.LdaImm(0);
		asm.StaAbs(SidBase + 0x04);
		asm.StaAbs(SidBase + 0x12);
		EmitVoiceRegisters(asm, voice: 0, frequency: 0x0800, pulseWidth: 0x0800, control: 0x00);
		EmitVoiceRegisters(asm, voice: 1, frequency: 0x1800, pulseWidth: 0x0800, control: 0x23);
		asm.LdaZp(FrameCounterAddress);
		asm.AslA();
		asm.StaAbs(SidBase + 0x07);
		asm.LdaImm(0x18);
		asm.StaAbs(SidBase + 0x08);
	}

	private static void EmitRingModulation(Mos6510Emitter asm)
	{
		asm.LdaImm(0);
		asm.StaAbs(SidBase + 0x04);
		asm.StaAbs(SidBase + 0x12);
		EmitVoiceRegisters(asm, voice: 0, frequency: 0x0900, pulseWidth: 0x0800, control: 0x00);
		EmitVoiceRegisters(asm, voice: 1, frequency: 0x1300, pulseWidth: 0x0800, control: 0x15);
	}

	private static void EmitVoiceRegisters(Mos6510Emitter asm, int voice, ushort frequency, ushort pulseWidth, byte control)
	{
		var offset = voice * 7;
		asm.LdaImm((byte)frequency);
		asm.StaAbs((ushort)(SidBase + offset));
		asm.LdaImm((byte)(frequency >> 8));
		asm.StaAbs((ushort)(SidBase + offset + 1));
		asm.LdaImm((byte)pulseWidth);
		asm.StaAbs((ushort)(SidBase + offset + 2));
		asm.LdaImm((byte)(pulseWidth >> 8));
		asm.StaAbs((ushort)(SidBase + offset + 3));
		asm.LdaImm(control);
		asm.StaAbs((ushort)(SidBase + offset + 4));
	}

	private static void RunSidPlayFp(string sidPlayFp, string workingDirectory)
	{
		var startInfo = new ProcessStartInfo
		{
			FileName = sidPlayFp,
			WorkingDirectory = workingDirectory,
			UseShellExecute = false,
			RedirectStandardError = true,
			RedirectStandardOutput = true
		};
		startInfo.ArgumentList.Add("--residfp");
		startInfo.ArgumentList.Add("-cwa");
		startInfo.ArgumentList.Add("-vpf");
		startInfo.ArgumentList.Add("-mof");
		startInfo.ArgumentList.Add("-m");
		startInfo.ArgumentList.Add("-f" + SampleRate.ToString(CultureInfo.InvariantCulture));
		startInfo.ArgumentList.Add("-p32");
		startInfo.ArgumentList.Add("-t" + FormatSidPlayFpDuration(RenderSeconds));
		startInfo.ArgumentList.Add("-w");
		startInfo.ArgumentList.Add("input.sid");

		using var process = Process.Start(startInfo);
		Assert.NotNull(process);
		var stdout = process.StandardOutput.ReadToEnd();
		var stderr = process.StandardError.ReadToEnd();
		Assert.True(process.WaitForExit(30_000), "SidPlayFP did not exit within 30 seconds.");
		Assert.True(
			process.ExitCode == 0,
			"SidPlayFP failed.\nSTDOUT:\n" + stdout + "\nSTDERR:\n" + stderr);
	}

	private static float[] RenderCopperMod(string sidPath, double seconds)
	{
		using var song = (SidSong)new SidFormat().Load(File.ReadAllBytes(sidPath));
		((ISidEmulationProfileController)song).SidEmulationProfile = SidEmulationProfile.Balanced;
		var options = new AudioRenderOptions(SampleRate, channelCount: 1);
		var targetFrames = SecondsToSamples(seconds);
		var samples = new List<float>(targetFrames + SampleRate);
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

	private static void AssertHarmonicRatios(
		OracleSegment segment,
		float[] reference,
		float[] candidate,
		int referenceStart,
		int candidateStart,
		int length)
	{
		var fundamental = SidFrequencyToHz(segment.Frequency);
		var referenceMagnitudes = new double[8];
		var candidateMagnitudes = new double[8];
		var referenceTotal = 0.0;
		var candidateTotal = 0.0;
		for (var harmonic = 1; harmonic <= 8; harmonic++)
		{
			referenceMagnitudes[harmonic - 1] = HarmonicMagnitude(reference, referenceStart, length, fundamental * harmonic);
			candidateMagnitudes[harmonic - 1] = HarmonicMagnitude(candidate, candidateStart, length, fundamental * harmonic);
			referenceTotal += referenceMagnitudes[harmonic - 1];
			candidateTotal += candidateMagnitudes[harmonic - 1];
		}

		Assert.True(referenceTotal > 1.0e-7, segment.Name + " SidPlayFP harmonic energy was too small.");
		Assert.True(candidateTotal > 1.0e-7, segment.Name + " CopperMod harmonic energy was too small.");
		for (var harmonic = 1; harmonic <= 8; harmonic++)
		{
			var referenceRatio = referenceMagnitudes[harmonic - 1] / referenceTotal;
			var candidateRatio = candidateMagnitudes[harmonic - 1] / candidateTotal;
			if (referenceRatio < 0.02)
			{
				Assert.True(
					candidateRatio < CombinedNearNullRatioLimit,
					$"{segment.Name} harmonic {harmonic} should stay near-null: reference {referenceRatio:0.0000}, candidate {candidateRatio:0.0000}. " +
						"Reference [" + FormatRatios(referenceMagnitudes, referenceTotal) + "], candidate [" + FormatRatios(candidateMagnitudes, candidateTotal) + "].");
				continue;
			}

			if (referenceRatio < 1.0e-4 && candidateRatio < 1.0e-4)
			{
				continue;
			}

			var relativeError = Math.Abs(candidateRatio - referenceRatio) / Math.Max(1.0e-4, referenceRatio);
			var absoluteError = Math.Abs(candidateRatio - referenceRatio);
			Assert.True(
				relativeError <= CombinedHarmonicRelativeTolerance || absoluteError <= CombinedHarmonicAbsoluteTolerance,
				$"{segment.Name} harmonic {harmonic} ratio mismatch: reference {referenceRatio:0.0000}, candidate {candidateRatio:0.0000}, relative error {relativeError:0.000}, absolute error {absoluteError:0.0000}. " +
					"Reference [" + FormatRatios(referenceMagnitudes, referenceTotal) + "], candidate [" + FormatRatios(candidateMagnitudes, candidateTotal) + "].");
		}
	}

	private static string FormatRatios(double[] magnitudes, double total)
	{
		return string.Join(
			", ",
			magnitudes.Select(value => (value / total).ToString("0.0000", CultureInfo.InvariantCulture)));
	}

	private static void AssertNoiseShape(
		OracleSegment segment,
		float[] reference,
		float[] candidate,
		int referenceStart,
		int candidateStart,
		int length)
	{
		var referenceFlatness = SpectralFlatness(reference, referenceStart, length);
		var candidateFlatness = SpectralFlatness(candidate, candidateStart, length);
		var minimumFlatness = referenceFlatness * 0.65;
		var maximumFlatness = referenceFlatness * 1.35;
		Assert.True(
			candidateFlatness >= minimumFlatness && candidateFlatness <= maximumFlatness,
			$"{segment.Name} spectral flatness {candidateFlatness:0.0000} was outside {minimumFlatness:0.0000}..{maximumFlatness:0.0000}.");

		ReadOnlySpan<double> bands = stackalloc[] { 1000.0, 2000.0, 4000.0, 8000.0, 12000.0, 16000.0 };
		var referenceEnergy = 0.0;
		var candidateEnergy = 0.0;
		for (var i = 0; i < bands.Length; i++)
		{
			referenceEnergy += HarmonicMagnitude(reference, referenceStart, length, bands[i]);
			candidateEnergy += HarmonicMagnitude(candidate, candidateStart, length, bands[i]);
		}

		Assert.True(referenceEnergy > 1.0e-7, segment.Name + " SidPlayFP noise band energy was too small.");
		Assert.True(candidateEnergy > referenceEnergy * 0.35, segment.Name + " CopperMod noise band energy was too small.");
	}

	private static void AssertNoiseCombinedShape(
		OracleSegment segment,
		float[] reference,
		float[] candidate,
		int referenceStart,
		int candidateStart,
		int length)
	{
		var candidateFlatness = SpectralFlatness(candidate, candidateStart, length);
		Assert.InRange(candidateFlatness, 0.03, 0.85);

		ReadOnlySpan<double> bands = stackalloc[] { 1000.0, 2000.0, 4000.0, 8000.0, 12000.0, 16000.0 };
		var referenceEnergy = 0.0;
		var candidateEnergy = 0.0;
		for (var i = 0; i < bands.Length; i++)
		{
			referenceEnergy += HarmonicMagnitude(reference, referenceStart, length, bands[i]);
			candidateEnergy += HarmonicMagnitude(candidate, candidateStart, length, bands[i]);
		}

		Assert.True(referenceEnergy > 1.0e-7, segment.Name + " SidPlayFP noise band energy was too small.");
		Assert.True(candidateEnergy > referenceEnergy * 0.20, segment.Name + " CopperMod noise-combined band energy was too small.");
	}

	private static int FindBestCandidateOffset(float[] reference, float[] candidate, int start, int length, int maxOffset)
	{
		var bestOffset = 0;
		var bestCorrelation = double.NegativeInfinity;
		for (var offset = -maxOffset; offset <= maxOffset; offset += 8)
		{
			if (start + offset < 0 || start + offset + length >= candidate.Length)
			{
				continue;
			}

			var correlation = Correlation(reference, candidate, start, start + offset, length);
			if (correlation > bestCorrelation)
			{
				bestCorrelation = correlation;
				bestOffset = offset;
			}
		}

		var refineStart = Math.Max(-maxOffset, bestOffset - 24);
		var refineEnd = Math.Min(maxOffset, bestOffset + 24);
		for (var offset = refineStart; offset <= refineEnd; offset++)
		{
			if (start + offset < 0 || start + offset + length >= candidate.Length)
			{
				continue;
			}

			var correlation = Correlation(reference, candidate, start, start + offset, length);
			if (correlation > bestCorrelation)
			{
				bestCorrelation = correlation;
				bestOffset = offset;
			}
		}

		return bestOffset;
	}

	private static double Correlation(float[] reference, float[] candidate, int referenceStart, int candidateStart, int length)
	{
		var referenceMean = Mean(reference, referenceStart, length);
		var candidateMean = Mean(candidate, candidateStart, length);
		var numerator = 0.0;
		var referenceEnergy = 0.0;
		var candidateEnergy = 0.0;
		for (var i = 0; i < length; i++)
		{
			var left = reference[referenceStart + i] - referenceMean;
			var right = candidate[candidateStart + i] - candidateMean;
			numerator += left * right;
			referenceEnergy += left * left;
			candidateEnergy += right * right;
		}

		return numerator / Math.Sqrt(Math.Max(1.0e-18, referenceEnergy * candidateEnergy));
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

	private static double HarmonicMagnitude(float[] samples, int start, int length, double frequency)
	{
		var mean = Mean(samples, start, length);
		var real = 0.0;
		var imaginary = 0.0;
		for (var i = 0; i < length; i++)
		{
			var window = 0.5 - (0.5 * Math.Cos((2.0 * Math.PI * i) / (length - 1)));
			var phase = (2.0 * Math.PI * frequency * i) / SampleRate;
			var sample = (samples[start + i] - mean) * window;
			real += sample * Math.Cos(phase);
			imaginary -= sample * Math.Sin(phase);
		}

		return Math.Sqrt((real * real) + (imaginary * imaginary)) / length;
	}

	private static double SpectralFlatness(float[] samples, int start, int length)
	{
		ReadOnlySpan<double> bands = stackalloc[] { 700.0, 1100.0, 1700.0, 2600.0, 3900.0, 5800.0, 8700.0, 13000.0, 18000.0 };
		var logSum = 0.0;
		var sum = 0.0;
		for (var i = 0; i < bands.Length; i++)
		{
			var magnitude = Math.Max(1.0e-12, HarmonicMagnitude(samples, start, length, bands[i]));
			logSum += Math.Log(magnitude);
			sum += magnitude;
		}

		return Math.Exp(logSum / bands.Length) / (sum / bands.Length);
	}

	private static double SidFrequencyToHz(ushort frequency)
		=> frequency * (double)SidConstants.PalCpuCyclesPerSecond / 16_777_216.0;

	private static int SecondsToSamples(double seconds)
		=> (int)Math.Round(seconds * SampleRate);

	private static string FormatSidPlayFpDuration(double seconds)
	{
		var wholeSeconds = (int)Math.Floor(seconds);
		var milliseconds = (int)Math.Round((seconds - wholeSeconds) * 1000.0);
		if (milliseconds >= 1000)
		{
			wholeSeconds++;
			milliseconds -= 1000;
		}

		return "00:" +
			wholeSeconds.ToString("00", CultureInfo.InvariantCulture) +
			"." +
			milliseconds.ToString("000", CultureInfo.InvariantCulture);
	}

	private sealed record OracleSegment(
		string Name,
		OracleSegmentKind Kind,
		ushort Frequency,
		double MinimumCorrelation);

	private enum OracleSegmentKind
	{
		Correlation,
		Harmonics,
		Noise,
		NoiseCombined
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
			=> _labels[name] = CurrentAddress;

		public ushort AddressOf(string name)
			=> _labels[name];

		public void LdaImm(byte value)
		{
			Emit(0xA9);
			Emit(value);
		}

		public void LdxImm(byte value)
		{
			Emit(0xA2);
			Emit(value);
		}

		public void LdaZp(byte address)
		{
			Emit(0xA5);
			Emit(address);
		}

		public void StaZp(byte address)
		{
			Emit(0x85);
			Emit(address);
		}

		public void IncZp(byte address)
		{
			Emit(0xE6);
			Emit(address);
		}

		public void CmpImm(byte value)
		{
			Emit(0xC9);
			Emit(value);
		}

		public void AslA()
			=> Emit(0x0A);

		public void Dex()
			=> Emit(0xCA);

		public void Rts()
			=> Emit(0x60);

		public void StaAbs(int address)
		{
			Emit(0x8D);
			EmitWord((ushort)address);
		}

		public void StaAbsX(ushort address)
		{
			Emit(0x9D);
			EmitWord(address);
		}

		public void Bcc(string label)
		{
			Emit(0x90);
			AddRelativePatch(label);
		}

		public void Bne(string label)
		{
			Emit(0xD0);
			AddRelativePatch(label);
		}

		public void Bpl(string label)
		{
			Emit(0x10);
			AddRelativePatch(label);
		}

		public void Jmp(string label)
		{
			Emit(0x4C);
			_patches.Add(new Patch(_bytes.Count, label, false, CurrentAddress));
			EmitWord(0);
		}

		public byte[] ToArray()
		{
			foreach (var patch in _patches)
			{
				var target = AddressOf(patch.Label);
				if (patch.Relative)
				{
					var delta = target - patch.NextInstructionAddress;
					if (delta < sbyte.MinValue || delta > sbyte.MaxValue)
					{
						throw new InvalidOperationException("Branch to " + patch.Label + " is outside 6502 relative range.");
					}

					_bytes[patch.Offset] = unchecked((byte)(sbyte)delta);
				}
				else
				{
					_bytes[patch.Offset] = (byte)target;
					_bytes[patch.Offset + 1] = (byte)(target >> 8);
				}
			}

			return _bytes.ToArray();
		}

		private ushort CurrentAddress => (ushort)(_baseAddress + _bytes.Count);

		private void AddRelativePatch(string label)
		{
			_patches.Add(new Patch(_bytes.Count, label, true, (ushort)(CurrentAddress + 1)));
			Emit(0);
		}

		private void Emit(byte value)
			=> _bytes.Add(value);

		private void EmitWord(ushort value)
		{
			Emit((byte)value);
			Emit((byte)(value >> 8));
		}

		private readonly record struct Patch(int Offset, string Label, bool Relative, ushort NextInstructionAddress);
	}

	private sealed record MeasurementWav(int SampleRate, float[] Samples)
	{
		public static MeasurementWav Read(string path)
		{
			using var stream = File.OpenRead(path);
			using var reader = new BinaryReader(stream);
			if (new string(reader.ReadChars(4)) != "RIFF")
			{
				throw new InvalidDataException("Only RIFF WAV files are supported.");
			}

			_ = reader.ReadUInt32();
			if (new string(reader.ReadChars(4)) != "WAVE")
			{
				throw new InvalidDataException("Only WAVE files are supported.");
			}

			WavFormat? format = null;
			byte[]? data = null;
			while (stream.Position + 8 <= stream.Length)
			{
				var id = new string(reader.ReadChars(4));
				var size = reader.ReadUInt32();
				var chunkStart = stream.Position;
				if (id == "fmt ")
				{
					format = WavFormat.Parse(reader.ReadBytes(checked((int)size)));
				}
				else if (id == "data")
				{
					data = reader.ReadBytes(checked((int)size));
				}

				stream.Position = chunkStart + size + (size & 1);
			}

			if (format == null || data == null)
			{
				throw new InvalidDataException("WAV file is missing fmt or data chunk.");
			}

			return new MeasurementWav(format.SampleRate, DecodeMono(format, data));
		}

		private static float[] DecodeMono(WavFormat format, byte[] data)
		{
			var frameCount = data.Length / format.BlockAlign;
			var samples = new float[frameCount];
			for (var frame = 0; frame < frameCount; frame++)
			{
				var frameOffset = frame * format.BlockAlign;
				var sum = 0.0;
				for (var channel = 0; channel < format.Channels; channel++)
				{
					var sampleOffset = frameOffset + (channel * format.BytesPerSample);
					sum += DecodeSample(format, data.AsSpan(sampleOffset, format.BytesPerSample));
				}

				samples[frame] = (float)(sum / format.Channels);
			}

			return samples;
		}

		private static double DecodeSample(WavFormat format, ReadOnlySpan<byte> bytes)
		{
			if (format.AudioFormat == 3 && format.BitsPerSample == 32)
			{
				return BinaryPrimitives.ReadSingleLittleEndian(bytes);
			}

			if (format.AudioFormat != 1)
			{
				throw new InvalidDataException("Unsupported WAV format tag: " + format.AudioFormat.ToString(CultureInfo.InvariantCulture));
			}

			return format.BitsPerSample switch
			{
				16 => BinaryPrimitives.ReadInt16LittleEndian(bytes) / 32768.0,
				24 => DecodeInt24(bytes) / 8388608.0,
				32 => BinaryPrimitives.ReadInt32LittleEndian(bytes) / 2147483648.0,
				_ => throw new InvalidDataException("Unsupported PCM bit depth: " + format.BitsPerSample.ToString(CultureInfo.InvariantCulture))
			};
		}

		private static int DecodeInt24(ReadOnlySpan<byte> bytes)
		{
			var value = bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);
			if ((value & 0x800000) != 0)
			{
				value |= unchecked((int)0xFF000000);
			}

			return value;
		}
	}

	private sealed record WavFormat(int AudioFormat, int Channels, int SampleRate, int BlockAlign, int BitsPerSample)
	{
		public int BytesPerSample => BitsPerSample / 8;

		public static WavFormat Parse(byte[] bytes)
		{
			if (bytes.Length < 16)
			{
				throw new InvalidDataException("WAV fmt chunk is too short.");
			}

			var audioFormat = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0, 2));
			if (audioFormat == 0xFFFE && bytes.Length >= 40)
			{
				audioFormat = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(24, 2));
			}

			var channels = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2, 2));
			var sampleRate = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4));
			var blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(12, 2));
			var bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(14, 2));
			return new WavFormat(audioFormat, channels, sampleRate, blockAlign, bitsPerSample);
		}
	}
}
