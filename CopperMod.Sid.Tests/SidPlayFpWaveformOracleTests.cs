using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using CopperMod.Abstractions;

namespace CopperMod.Sid.Tests;

public sealed class SidPlayFpWaveformOracleTests
{
	private const int SampleRate = 96000;
	private const int FramesPerSegment = 18;
	private const int SegmentRate = 50;
	private const byte FrameCounterAddress = 0x02;
	private const byte FrameCounterHighAddress = FrameCounterAddress + 1;
	private const ushort SidBase = 0xD400;
	private const ushort ProgramBase = 0x1000;
	private const double CombinedHarmonicRelativeTolerance = 0.50;
	private const double CombinedHarmonicAbsoluteTolerance = 0.070;
	private const double CombinedNearNullRatioLimit = 0.055;
	private const int WeakSpotAdsrFrames = 45;
	private const int WeakSpotD418Frames = 64;
	private const int WeakSpotCombinedFrames = 24;
	private const ushort WeakSpotAdsrFrequency = 0x1C31;
	private const double AdsrTraceStepSeconds = 0.004;
	private const double AdsrTraceWindowSeconds = 0.012;

	private static readonly int[] AdsrPianoGateFrames = { 2, 8, 10, 16, 18, 24, 26, 32, 34 };

	private static readonly OracleSegment[] Segments =
	{
		new("triangle", OracleSegmentKind.Correlation, 0x1000, 0.95),
		new("saw", OracleSegmentKind.Correlation, 0x1000, 0.95),
		new("pulse", OracleSegmentKind.Correlation, 0x1000, 0.95),
		new("noise", OracleSegmentKind.Noise, 0x4000, 0.0),
		new("sync-fm", OracleSegmentKind.Correlation, 0x1800, 0.85),
		new("ring", OracleSegmentKind.Correlation, 0x1300, 0.85),
		new("triangle-saw", OracleSegmentKind.Harmonics, 0x1000, 0.0),
		new("triangle-pulse", OracleSegmentKind.Level, 0x1000, 0.0),
		new("saw-pulse", OracleSegmentKind.Harmonics, 0x1000, 0.0),
		new("triangle-saw-pulse", OracleSegmentKind.Harmonics, 0x1000, 0.0),
		new("noise-saw", OracleSegmentKind.NoiseCombined, 0x4000, 0.0),
		new("sync-ring", OracleSegmentKind.Correlation, 0x1400, 0.50),
		new("ring-triangle-pulse", OracleSegmentKind.Level, 0x1000, 0.0),
		new("pulse-width-000", OracleSegmentKind.Level, 0x1000, 0.0),
		new("pulse-width-001", OracleSegmentKind.Level, 0x1000, 0.0),
		new("pulse-width-ffe", OracleSegmentKind.Level, 0x1000, 0.0),
		new("pulse-width-fff", OracleSegmentKind.Level, 0x1000, 0.0)
	};

	private static readonly double RenderSeconds = (double)(Segments.Length * FramesPerSegment) / SegmentRate;
	private static readonly WeakSpotSegment[] WeakSpotSegments =
	{
		new("sidtest5-adsr-piano", OracleSegmentKind.Level, WeakSpotAdsrFrequency, 0.0, WeakSpotAdsrFrames),
		new("sidtest5-d418-volume", OracleSegmentKind.Level, 0x1C31, 0.0, WeakSpotD418Frames),
		new("sidtest5-combined-triangle-saw", OracleSegmentKind.Level, 0x1C31, 0.0, WeakSpotCombinedFrames),
		new("sidtest5-combined-triangle-pulse", OracleSegmentKind.Level, 0x1C31, 0.0, WeakSpotCombinedFrames),
		new("sidtest5-combined-saw-pulse", OracleSegmentKind.Level, 0x1C31, 0.0, WeakSpotCombinedFrames),
		new("sidtest5-combined-all-three", OracleSegmentKind.Level, 0x1C31, 0.0, WeakSpotCombinedFrames)
	};

	private static readonly double WeakSpotRenderSeconds = WeakSpotSegments.Sum(segment => segment.Frames) / (double)SegmentRate;
	private static readonly double AdsrRestartRenderSeconds = WeakSpotAdsrFrames / (double)SegmentRate;

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

			RunSidPlayFp(sidPlayFp, root, RenderSeconds);
			Assert.True(File.Exists(wavPath), "SidPlayFP did not create the expected oracle WAV: " + wavPath);

			var reference = MeasurementWav.Read(wavPath);
			var candidate = RenderCopperMod(sidPath, RenderSeconds);
			Assert.Equal(SampleRate, reference.SampleRate);
			Assert.True(reference.Samples.Length >= SecondsToSamples(RenderSeconds) - SampleRate / 20);
			Assert.True(candidate.Length >= SecondsToSamples(RenderSeconds) - SampleRate / 20);

			WriteOptionalReport(reference.Samples, candidate);
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

	[Fact]
	public void OptionalGeneratedWeakSpotFixtureMatchesSidPlayFpOracle()
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

		var root = Path.Combine(Path.GetTempPath(), "coppermod-sidplayfp-weakspots-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			var sidPath = Path.Combine(root, "input.sid");
			var wavPath = Path.Combine(root, "input.wav");
			File.WriteAllBytes(sidPath, CreateWeakSpotOracleSid());

			RunSidPlayFp(sidPlayFp, root, WeakSpotRenderSeconds);
			Assert.True(File.Exists(wavPath), "SidPlayFP did not create the expected weak-spot oracle WAV: " + wavPath);

			var reference = MeasurementWav.Read(wavPath);
			var captureAdsrTrace = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SIDPLAYFP_ADSR_TRACE_REPORT"));
			var candidateRender = RenderCopperModDiagnostic(sidPath, WeakSpotRenderSeconds, captureAdsrTrace);
			var candidate = candidateRender.Samples;
			Assert.Equal(SampleRate, reference.SampleRate);
			Assert.True(reference.Samples.Length >= SecondsToSamples(WeakSpotRenderSeconds) - SampleRate / 20);
			Assert.True(candidate.Length >= SecondsToSamples(WeakSpotRenderSeconds) - SampleRate / 20);

			WriteOptionalWeakSpotReport(reference.Samples, candidate);
			WriteOptionalAdsrPianoTraceReport(reference.Samples, candidate, candidateRender.ChannelSamples, candidateRender.SidWrites);
			var startFrame = 0;
			for (var i = 0; i < WeakSpotSegments.Length; i++)
			{
				AssertWeakSpotSegmentMatches(WeakSpotSegments[i], startFrame, reference.Samples, candidate);
				startFrame += WeakSpotSegments[i].Frames;
			}
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[Fact]
	public void OptionalGeneratedAdsrRestartFixtureMatchesSidPlayFpOracle()
	{
		if (Environment.GetEnvironmentVariable("SIDPLAYFP_ORACLE_TESTS") != "1" &&
			Environment.GetEnvironmentVariable("SIDPLAYFP_ADSR_RESTART_ORACLE_TESTS") != "1")
		{
			return;
		}

		var sidPlayFp = Environment.GetEnvironmentVariable("SIDPLAYFP_EXE");
		if (string.IsNullOrWhiteSpace(sidPlayFp))
		{
			sidPlayFp = @"D:\Models\sidplayfp-3.0.2-ucrt64\sidplayfp.exe";
		}

		Assert.True(File.Exists(sidPlayFp), "SidPlayFP executable was not found: " + sidPlayFp);

		var root = Path.Combine(Path.GetTempPath(), "coppermod-sidplayfp-adsr-restart-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			var sidPath = Path.Combine(root, "input.sid");
			var wavPath = Path.Combine(root, "input.wav");
			// Reuse the weak-spot PSID and render only the ADSR segment, preserving
			// the real sidtest5 route/play-routine cycle offsets.
			File.WriteAllBytes(sidPath, CreateWeakSpotOracleSid());

			RunSidPlayFp(sidPlayFp, root, AdsrRestartRenderSeconds);
			Assert.True(File.Exists(wavPath), "SidPlayFP did not create the expected ADSR restart oracle WAV: " + wavPath);

			var reference = MeasurementWav.Read(wavPath);
			var reportPath = Environment.GetEnvironmentVariable("SIDPLAYFP_ADSR_RESTART_REPORT");
			var captureDiagnostics = !string.IsNullOrWhiteSpace(reportPath);
			var candidateRender = RenderCopperModDiagnostic(sidPath, AdsrRestartRenderSeconds, captureDiagnostics);
			var candidate = candidateRender.Samples;
			Assert.Equal(SampleRate, reference.SampleRate);
			Assert.True(reference.Samples.Length >= SecondsToSamples(AdsrRestartRenderSeconds) - SampleRate / 20);
			Assert.True(candidate.Length >= SecondsToSamples(AdsrRestartRenderSeconds) - SampleRate / 20);

			if (!string.IsNullOrWhiteSpace(reportPath))
			{
				WriteAdsrPianoTraceReport(
					reportPath,
					reference.Samples,
					candidate,
					candidateRender.ChannelSamples,
					candidateRender.SidWrites);
			}

			var (start, length) = WeakSpotWindow(0, WeakSpotAdsrFrames);
			var offset = FindBestCandidateOffset(reference.Samples, candidate, start, length, maxOffset: SampleRate / 40);
			AssertAdsrRestartShape(reference.Samples, candidate, start, start + offset, length);
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	private static void WriteOptionalReport(float[] reference, float[] candidate)
	{
		var path = Environment.GetEnvironmentVariable("SIDPLAYFP_ORACLE_REPORT");
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}

		path = Path.GetFullPath(path);
		Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
		var builder = new StringBuilder();
		builder.AppendLine("segment,kind,offset,ref_mean,cand_mean,ref_ac,cand_ac,ac_ratio,corr");
		for (var i = 0; i < Segments.Length; i++)
		{
			var segment = Segments[i];
			var start = SecondsToSamples(((i * FramesPerSegment) / (double)SegmentRate) + 0.10);
			var length = SecondsToSamples(0.18);
			var offset = FindBestCandidateOffset(reference, candidate, start, length, maxOffset: SampleRate / 40);
			var referenceMean = Mean(reference, start, length);
			var candidateMean = Mean(candidate, start + offset, length);
			var referenceAc = AcRms(reference, start, length);
			var candidateAc = AcRms(candidate, start + offset, length);
			var ratio = candidateAc / Math.Max(1.0e-12, referenceAc);
			var correlation = Correlation(reference, candidate, start, start + offset, length);
			builder
				.Append(EscapeCsv(segment.Name)).Append(',')
				.Append(segment.Kind).Append(',')
				.Append(offset.ToString(CultureInfo.InvariantCulture)).Append(',')
				.Append(referenceMean.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(candidateMean.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(referenceAc.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(candidateAc.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(ratio.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(correlation.ToString("0.000000", CultureInfo.InvariantCulture))
				.AppendLine();
		}

		File.WriteAllText(path, builder.ToString());
	}

	private static string EscapeCsv(string value)
		=> value.Contains(',') || value.Contains('"')
			? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
			: value;

	private static void WriteOptionalWeakSpotReport(float[] reference, float[] candidate)
	{
		var path = Environment.GetEnvironmentVariable("SIDPLAYFP_WEAKSPOT_ORACLE_REPORT");
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}

		path = Path.GetFullPath(path);
		Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
		var builder = new StringBuilder();
		builder.AppendLine("segment,kind,offset,ref_mean,cand_mean,ref_ac,cand_ac,ac_ratio,corr");
		var startFrame = 0;
		for (var i = 0; i < WeakSpotSegments.Length; i++)
		{
			var segment = WeakSpotSegments[i];
			var (start, length) = WeakSpotWindow(startFrame, segment.Frames);
			var offset = FindBestCandidateOffset(reference, candidate, start, length, maxOffset: SampleRate / 40);
			var referenceMean = Mean(reference, start, length);
			var candidateMean = Mean(candidate, start + offset, length);
			var referenceAc = AcRms(reference, start, length);
			var candidateAc = AcRms(candidate, start + offset, length);
			var ratio = candidateAc / Math.Max(1.0e-12, referenceAc);
			var correlation = Correlation(reference, candidate, start, start + offset, length);
			builder
				.Append(EscapeCsv(segment.Name)).Append(',')
				.Append(segment.Kind).Append(',')
				.Append(offset.ToString(CultureInfo.InvariantCulture)).Append(',')
				.Append(referenceMean.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(candidateMean.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(referenceAc.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(candidateAc.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(ratio.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(correlation.ToString("0.000000", CultureInfo.InvariantCulture))
				.AppendLine();
			startFrame += segment.Frames;
		}

		File.WriteAllText(path, builder.ToString());
	}

	private static void WriteOptionalAdsrPianoTraceReport(
		float[] reference,
		float[] candidate,
		float[][]? candidateChannels,
		SidRegisterWrite[]? candidateWrites)
	{
		var path = Environment.GetEnvironmentVariable("SIDPLAYFP_ADSR_TRACE_REPORT");
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}

		WriteAdsrPianoTraceReport(path, reference, candidate, candidateChannels, candidateWrites);
	}

	private static void WriteAdsrPianoTraceReport(
		string path,
		float[] reference,
		float[] candidate,
		float[][]? candidateChannels,
		SidRegisterWrite[]? candidateWrites)
	{
		path = Path.GetFullPath(path);
		Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
		var candidateVoice0 = candidateChannels is { Length: > 0 } ? candidateChannels[0] : null;
		var rows = BuildAdsrPianoTraceRows(reference, candidate, candidateVoice0, candidateWrites);
		var builder = new StringBuilder();
		builder.AppendLine("time_ms,frame,gate,alignment_offset_samples,ref_ac,cand_ac,ac_ratio,cand_voice0_ac,final_to_voice0_ac,ref_fund,cand_fund,fund_ratio,cand_voice0_fund,final_to_voice0_fund,cand_envelope,cand_envelope_dac,cand_state,cand_rate_counter,cand_exponential_counter,cand_control");
		for (var i = 0; i < rows.Count; i++)
		{
			var row = rows[i];
			builder
				.Append(row.TimeMilliseconds.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
				.Append(row.Frame.ToString(CultureInfo.InvariantCulture)).Append(',')
				.Append(row.Gate ? "1" : "0").Append(',')
				.Append(row.AlignmentOffsetSamples.ToString(CultureInfo.InvariantCulture)).Append(',')
				.Append(row.ReferenceAc.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(row.CandidateAc.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(row.AcRatio.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(row.CandidateVoice0Ac.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(row.FinalToVoice0AcRatio.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(row.ReferenceFundamental.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(row.CandidateFundamental.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(row.FundamentalRatio.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(row.CandidateVoice0Fundamental.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(row.FinalToVoice0FundamentalRatio.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(row.EnvelopeCounter.ToString(CultureInfo.InvariantCulture)).Append(',')
				.Append(row.EnvelopeDac.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(FormatEnvelopeState(row.EnvelopeState)).Append(',')
				.Append(row.RateCounter.ToString(CultureInfo.InvariantCulture)).Append(',')
				.Append(row.ExponentialCounter.ToString(CultureInfo.InvariantCulture)).Append(',')
				.Append("0x").Append(row.Control.ToString("X2", CultureInfo.InvariantCulture))
				.AppendLine();
		}

		File.WriteAllText(path, builder.ToString());
		WriteAdsrPianoPulseSummary(path, rows);
		WriteAdsrPianoGateEdgeReport(path);
		WriteAdsrPianoSidWriteReport(path, candidateWrites);
	}

	private static IReadOnlyList<AdsrPianoTraceRow> BuildAdsrPianoTraceRows(
		float[] reference,
		float[] candidate,
		float[]? candidateVoice0,
		SidRegisterWrite[]? candidateWrites)
	{
		var debugRows = BuildCopperModAdsrPianoDebugTrace(candidateWrites);
		var rows = new List<AdsrPianoTraceRow>(debugRows.Count);
		var windowLength = Math.Max(32, SecondsToSamples(AdsrTraceWindowSeconds));
		var fundamental = SidFrequencyToHz(WeakSpotAdsrFrequency);
		var (alignmentStart, alignmentLength) = WeakSpotWindow(0, WeakSpotAdsrFrames);
		var alignmentOffset = FindBestCandidateOffset(reference, candidate, alignmentStart, alignmentLength, maxOffset: SampleRate / 40);
		for (var i = 0; i < debugRows.Count; i++)
		{
			var debug = debugRows[i];
			var center = SecondsToSamples(debug.TimeSeconds);
			var referenceStart = WindowStart(reference.Length, center - alignmentOffset, windowLength);
			var candidateStart = WindowStart(candidate.Length, center, windowLength);
			var referenceAc = AcRms(reference, referenceStart, windowLength);
			var candidateAc = AcRms(candidate, candidateStart, windowLength);
			var referenceFundamental = HarmonicMagnitude(reference, referenceStart, windowLength, fundamental);
			var candidateFundamental = HarmonicMagnitude(candidate, candidateStart, windowLength, fundamental);
			var candidateVoice0Ac = double.NaN;
			var candidateVoice0Fundamental = double.NaN;
			if (candidateVoice0 != null && candidateVoice0.Length >= windowLength)
			{
				var voice0Start = WindowStart(candidateVoice0.Length, center, windowLength);
				candidateVoice0Ac = AcRms(candidateVoice0, voice0Start, windowLength);
				candidateVoice0Fundamental = HarmonicMagnitude(candidateVoice0, voice0Start, windowLength, fundamental);
			}

			rows.Add(new AdsrPianoTraceRow(
				debug.TimeSeconds * 1000.0,
				debug.Frame,
				debug.Gate,
				alignmentOffset,
				referenceAc,
				candidateAc,
				candidateAc / Math.Max(1.0e-12, referenceAc),
				candidateVoice0Ac,
				RatioOrNaN(candidateAc, candidateVoice0Ac),
				referenceFundamental,
				candidateFundamental,
				candidateFundamental / Math.Max(1.0e-12, referenceFundamental),
				candidateVoice0Fundamental,
				RatioOrNaN(candidateFundamental, candidateVoice0Fundamental),
				debug.EnvelopeCounter,
				debug.EnvelopeDac,
				debug.EnvelopeState,
				debug.RateCounter,
				debug.ExponentialCounter,
				debug.Control));
		}

		return rows;
	}

	private static int WindowStart(int sampleCount, int center, int length)
	{
		if (sampleCount <= length)
		{
			return 0;
		}

		return Math.Clamp(center - (length / 2), 0, sampleCount - length);
	}

	private static double RatioOrNaN(double numerator, double denominator)
		=> double.IsNaN(denominator) ? double.NaN : numerator / Math.Max(1.0e-12, denominator);

	private static IReadOnlyList<AdsrPianoDebugRow> BuildCopperModAdsrPianoDebugTrace(SidRegisterWrite[]? capturedWrites)
	{
		var replayWrites = BuildAdsrPianoReplayWrites(capturedWrites);
		if (replayWrites.Length > 0)
		{
			return BuildCopperModAdsrPianoDebugTraceFromWrites(replayWrites);
		}

		return BuildCopperModAdsrPianoDebugTraceFromFrameEdges();
	}

	private static SidRegisterWrite[] BuildAdsrPianoReplayWrites(SidRegisterWrite[]? capturedWrites)
	{
		if (capturedWrites == null)
		{
			return Array.Empty<SidRegisterWrite>();
		}

		var maxCycle = WeakSpotAdsrFrames * (long)SidConstants.PalCyclesPerFrame;
		return capturedWrites
			.Where(write =>
				write.ChipIndex == 0 &&
				write.Cycle >= 0 &&
				write.Cycle < maxCycle &&
				IsAdsrPianoProbeRegister(write.Register))
			.OrderBy(write => write.Cycle)
			.ToArray();
	}

	private static IReadOnlyList<AdsrPianoDebugRow> BuildCopperModAdsrPianoDebugTraceFromWrites(SidRegisterWrite[] writes)
	{
		var chip = new SidChip(SidChipModel.Mos6581, SidBase, SidConstants.PalCpuCyclesPerSecond);
		var rows = new List<AdsrPianoDebugRow>();
		var currentCycle = 0L;
		var nextWrite = 0;
		var totalSeconds = WeakSpotAdsrFrames / (double)SegmentRate;
		for (var timeSeconds = AdsrTraceStepSeconds; timeSeconds < totalSeconds; timeSeconds += AdsrTraceStepSeconds)
		{
			var targetCycle = (long)Math.Round(timeSeconds * SidConstants.PalCpuCyclesPerSecond);
			AdvanceAdsrPianoTraceTo(chip, writes, ref currentCycle, ref nextWrite, targetCycle);
			rows.Add(CreateAdsrPianoDebugRow(chip, timeSeconds));
		}

		return rows;
	}

	private static void AdvanceAdsrPianoTraceTo(
		SidChip chip,
		SidRegisterWrite[] writes,
		ref long currentCycle,
		ref int nextWrite,
		long targetCycle)
	{
		while (nextWrite < writes.Length && writes[nextWrite].Cycle <= targetCycle)
		{
			var write = writes[nextWrite++];
			if (write.Cycle > currentCycle)
			{
				chip.Render(write.Cycle - currentCycle);
				currentCycle = write.Cycle;
			}

			chip.Write(write.Register, write.Value, write.Cycle);
		}

		if (targetCycle > currentCycle)
		{
			chip.Render(targetCycle - currentCycle);
			currentCycle = targetCycle;
		}
	}

	private static IReadOnlyList<AdsrPianoDebugRow> BuildCopperModAdsrPianoDebugTraceFromFrameEdges()
	{
		var chip = new SidChip(SidChipModel.Mos6581, SidBase, SidConstants.PalCpuCyclesPerSecond);
		var rows = new List<AdsrPianoDebugRow>();
		var currentCycle = 0L;
		var nextFrame = 0;
		var totalSeconds = WeakSpotAdsrFrames / (double)SegmentRate;
		for (var timeSeconds = AdsrTraceStepSeconds; timeSeconds < totalSeconds; timeSeconds += AdsrTraceStepSeconds)
		{
			var targetCycle = (long)Math.Round(timeSeconds * SidConstants.PalCpuCyclesPerSecond);
			AdvanceAdsrPianoTraceTo(chip, ref currentCycle, ref nextFrame, targetCycle);
			rows.Add(CreateAdsrPianoDebugRow(chip, timeSeconds));
		}

		return rows;
	}

	private static AdsrPianoDebugRow CreateAdsrPianoDebugRow(SidChip chip, double timeSeconds)
	{
		var voice = chip.DebugState.Voices[0];
		return new AdsrPianoDebugRow(
			timeSeconds,
			(int)Math.Floor(timeSeconds * SegmentRate),
			(voice.Control & 0x01) != 0,
			voice.EnvelopeCounter,
			SidAnalog.ConvertEnvelope(voice.EnvelopeCounter, SidChipModel.Mos6581),
			voice.EnvelopeState,
			voice.RateCounter,
			voice.ExponentialCounter,
			voice.Control);
	}

	private static void AdvanceAdsrPianoTraceTo(SidChip chip, ref long currentCycle, ref int nextFrame, long targetCycle)
	{
		while (nextFrame < WeakSpotAdsrFrames && nextFrame * (long)SidConstants.PalCyclesPerFrame <= targetCycle)
		{
			var frameCycle = nextFrame * (long)SidConstants.PalCyclesPerFrame;
			if (frameCycle > currentCycle)
			{
				chip.Render(frameCycle - currentCycle);
				currentCycle = frameCycle;
			}

			ApplyAdsrPianoFrameWrites(chip, nextFrame);
			nextFrame++;
		}

		if (targetCycle > currentCycle)
		{
			chip.Render(targetCycle - currentCycle);
			currentCycle = targetCycle;
		}
	}

	private static void ApplyAdsrPianoFrameWrites(SidChip chip, int frame)
	{
		if (frame == 0)
		{
			chip.Write(0x04, 0x08);
			chip.Write(0x0B, 0x08);
			chip.Write(0x12, 0x08);
			chip.Write(0x04, 0x00);
			chip.Write(0x0B, 0x00);
			chip.Write(0x12, 0x00);
		}

		chip.Write(0x0B, 0x00);
		chip.Write(0x12, 0x00);
		chip.Write(0x15, 0x00);
		chip.Write(0x16, 0x00);
		chip.Write(0x17, 0x00);
		chip.Write(0x18, 0x0F);
		chip.Write(0x00, 0x31);
		chip.Write(0x01, 0x1C);
		chip.Write(0x02, 0x00);
		chip.Write(0x03, 0x08);
		chip.Write(0x05, 0x0D);
		chip.Write(0x06, 0x08);
		chip.Write(0x04, 0x20);
		chip.Write(0x04, IsAdsrPianoGateOnFrame(frame) ? (byte)0x21 : (byte)0x20);
	}

	private static bool IsAdsrPianoGateOnFrame(int frame)
	{
		for (var i = 0; i < AdsrPianoGateFrames.Length; i++)
		{
			if (frame < AdsrPianoGateFrames[i])
			{
				return (i & 1) == 0;
			}
		}

		return false;
	}

	private static void WriteAdsrPianoPulseSummary(string tracePath, IReadOnlyList<AdsrPianoTraceRow> rows)
	{
		var directory = Path.GetDirectoryName(tracePath) ?? ".";
		var fileName = Path.GetFileNameWithoutExtension(tracePath) + "-pulses.csv";
		var path = Path.Combine(directory, fileName);
		var builder = new StringBuilder();
		builder.AppendLine("pulse,on_ms,off_ms,ref_peak,cand_peak,peak_ratio,cand_voice0_peak,final_to_voice0_peak,ref_gate_off,cand_gate_off,gate_off_ratio,cand_voice0_gate_off,final_to_voice0_gate_off,ref_tail_40ms,cand_tail_40ms,tail_40ms_ratio,cand_voice0_tail_40ms,final_to_voice0_tail_40ms,cand_env_peak,cand_env_gate_off,cand_env_tail_40ms,cand_state_tail_40ms");
		ReadOnlySpan<int> pulseFrames = stackalloc[] { 0, 8, 16, 24, 32 };
		for (var i = 0; i < pulseFrames.Length; i++)
		{
			var onSeconds = pulseFrames[i] / (double)SegmentRate;
			var offSeconds = (pulseFrames[i] + 2) / (double)SegmentRate;
			var peakRows = rows
				.Where(row => row.TimeMilliseconds >= onSeconds * 1000.0 && row.TimeMilliseconds < (offSeconds + 0.040) * 1000.0)
				.ToArray();
			var peak = peakRows
				.OrderByDescending(row => row.ReferenceAc)
				.FirstOrDefault();
			var gateOff = NearestAdsrTraceRow(rows, offSeconds);
			var tail = NearestAdsrTraceRow(rows, offSeconds + 0.040);
			builder
				.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(',')
				.Append((onSeconds * 1000.0).ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
				.Append((offSeconds * 1000.0).ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
				.Append(peak.ReferenceAc.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(peak.CandidateAc.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(peak.AcRatio.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(peak.CandidateVoice0Ac.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(peak.FinalToVoice0AcRatio.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(gateOff.ReferenceAc.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(gateOff.CandidateAc.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(gateOff.AcRatio.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(gateOff.CandidateVoice0Ac.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(gateOff.FinalToVoice0AcRatio.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(tail.ReferenceAc.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(tail.CandidateAc.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(tail.AcRatio.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(tail.CandidateVoice0Ac.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(tail.FinalToVoice0AcRatio.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
				.Append(peak.EnvelopeCounter.ToString(CultureInfo.InvariantCulture)).Append(',')
				.Append(gateOff.EnvelopeCounter.ToString(CultureInfo.InvariantCulture)).Append(',')
				.Append(tail.EnvelopeCounter.ToString(CultureInfo.InvariantCulture)).Append(',')
				.Append(FormatEnvelopeState(tail.EnvelopeState))
				.AppendLine();
		}

		File.WriteAllText(path, builder.ToString());
	}

	private static void WriteAdsrPianoGateEdgeReport(string tracePath)
	{
		var directory = Path.GetDirectoryName(tracePath) ?? ".";
		var fileName = Path.GetFileNameWithoutExtension(tracePath) + "-gate-edges.csv";
		var path = Path.Combine(directory, fileName);
		var rows = BuildAdsrPianoGateEdgeRows();
		var builder = new StringBuilder();
		builder.AppendLine("frame,time_ms,intended_gate,before_control,before_env,before_state,after_write_control,after_write_env,after_write_state,after_one_cycle_control,after_one_cycle_env,after_one_cycle_state,after_one_cycle_rate_counter,after_one_cycle_exponential_counter");
		for (var i = 0; i < rows.Count; i++)
		{
			var row = rows[i];
			builder
				.Append(row.Frame.ToString(CultureInfo.InvariantCulture)).Append(',')
				.Append(row.TimeMilliseconds.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
				.Append(row.IntendedGate ? "1" : "0").Append(',')
				.Append("0x").Append(row.BeforeControl.ToString("X2", CultureInfo.InvariantCulture)).Append(',')
				.Append(row.BeforeEnvelope.ToString(CultureInfo.InvariantCulture)).Append(',')
				.Append(FormatEnvelopeState(row.BeforeEnvelopeState)).Append(',')
				.Append("0x").Append(row.AfterWriteControl.ToString("X2", CultureInfo.InvariantCulture)).Append(',')
				.Append(row.AfterWriteEnvelope.ToString(CultureInfo.InvariantCulture)).Append(',')
				.Append(FormatEnvelopeState(row.AfterWriteEnvelopeState)).Append(',')
				.Append("0x").Append(row.AfterOneCycleControl.ToString("X2", CultureInfo.InvariantCulture)).Append(',')
				.Append(row.AfterOneCycleEnvelope.ToString(CultureInfo.InvariantCulture)).Append(',')
				.Append(FormatEnvelopeState(row.AfterOneCycleEnvelopeState)).Append(',')
				.Append(row.AfterOneCycleRateCounter.ToString(CultureInfo.InvariantCulture)).Append(',')
				.Append(row.AfterOneCycleExponentialCounter.ToString(CultureInfo.InvariantCulture))
				.AppendLine();
		}

		File.WriteAllText(path, builder.ToString());
	}

	private static IReadOnlyList<AdsrPianoGateEdgeRow> BuildAdsrPianoGateEdgeRows()
	{
		var chip = new SidChip(SidChipModel.Mos6581, SidBase, SidConstants.PalCpuCyclesPerSecond);
		var rows = new List<AdsrPianoGateEdgeRow>(WeakSpotAdsrFrames);
		var currentCycle = 0L;
		for (var frame = 0; frame < WeakSpotAdsrFrames; frame++)
		{
			var frameCycle = frame * (long)SidConstants.PalCyclesPerFrame;
			if (frameCycle > currentCycle)
			{
				chip.Render(frameCycle - currentCycle);
				currentCycle = frameCycle;
			}

			var before = chip.DebugState.Voices[0];
			ApplyAdsrPianoFrameWrites(chip, frame);
			var afterWrite = chip.DebugState.Voices[0];
			chip.Render(1);
			currentCycle++;
			var afterOneCycle = chip.DebugState.Voices[0];
			rows.Add(new AdsrPianoGateEdgeRow(
				frame,
				frame * 1000.0 / SegmentRate,
				IsAdsrPianoGateOnFrame(frame),
				before.Control,
				before.EnvelopeCounter,
				before.EnvelopeState,
				afterWrite.Control,
				afterWrite.EnvelopeCounter,
				afterWrite.EnvelopeState,
				afterOneCycle.Control,
				afterOneCycle.EnvelopeCounter,
				afterOneCycle.EnvelopeState,
				afterOneCycle.RateCounter,
				afterOneCycle.ExponentialCounter));
		}

		return rows;
	}

	private static void WriteAdsrPianoSidWriteReport(string tracePath, SidRegisterWrite[]? writes)
	{
		if (writes == null)
		{
			return;
		}

		var directory = Path.GetDirectoryName(tracePath) ?? ".";
		var fileName = Path.GetFileNameWithoutExtension(tracePath) + "-sid-writes.csv";
		var path = Path.Combine(directory, fileName);
		var builder = new StringBuilder();
		builder.AppendLine("index,cycle,frame,frame_cycle,chip,register,value");
		var maxCycle = WeakSpotAdsrFrames * (long)SidConstants.PalCyclesPerFrame;
		var index = 0;
		for (var i = 0; i < writes.Length; i++)
		{
			var write = writes[i];
			if (write.ChipIndex != 0 ||
				write.Cycle < 0 ||
				write.Cycle >= maxCycle ||
				!IsAdsrPianoProbeRegister(write.Register))
			{
				continue;
			}

			builder
				.Append(index.ToString(CultureInfo.InvariantCulture)).Append(',')
				.Append(write.Cycle.ToString(CultureInfo.InvariantCulture)).Append(',')
				.Append((write.Cycle / SidConstants.PalCyclesPerFrame).ToString(CultureInfo.InvariantCulture)).Append(',')
				.Append((write.Cycle % SidConstants.PalCyclesPerFrame).ToString(CultureInfo.InvariantCulture)).Append(',')
				.Append(write.ChipIndex.ToString(CultureInfo.InvariantCulture)).Append(',')
				.Append("0x").Append(write.Register.ToString("X2", CultureInfo.InvariantCulture)).Append(',')
				.Append("0x").Append(write.Value.ToString("X2", CultureInfo.InvariantCulture))
				.AppendLine();
			index++;
		}

		File.WriteAllText(path, builder.ToString());
	}

	private static bool IsAdsrPianoProbeRegister(byte register)
		=> register is <= 0x06 or 0x0B or 0x12 or >= 0x15 and <= 0x18;

	private static AdsrPianoTraceRow NearestAdsrTraceRow(IReadOnlyList<AdsrPianoTraceRow> rows, double timeSeconds)
	{
		var timeMilliseconds = timeSeconds * 1000.0;
		var best = rows[0];
		var bestDistance = Math.Abs(best.TimeMilliseconds - timeMilliseconds);
		for (var i = 1; i < rows.Count; i++)
		{
			var distance = Math.Abs(rows[i].TimeMilliseconds - timeMilliseconds);
			if (distance < bestDistance)
			{
				best = rows[i];
				bestDistance = distance;
			}
		}

		return best;
	}

	private static string FormatEnvelopeState(int state)
		=> state switch
		{
			0 => "attack",
			1 => "decay",
			2 => "sustain",
			3 => "release",
			_ => "unknown"
		};

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
				AssertHarmonicRatios(segment.Name, segment.Frequency, reference, candidate, start, start + offset, length);
				break;
			case OracleSegmentKind.Noise:
				AssertNoiseShape(segment, reference, candidate, start, start + offset, length);
				break;
			case OracleSegmentKind.NoiseCombined:
				AssertNoiseCombinedShape(segment, reference, candidate, start, start + offset, length);
				break;
			case OracleSegmentKind.Level:
				AssertLevelShape(segment.Name, reference, candidate, start, start + offset, length);
				break;
		}
	}

	private static void AssertWeakSpotSegmentMatches(
		WeakSpotSegment segment,
		int startFrame,
		float[] reference,
		float[] candidate)
	{
		var (start, length) = WeakSpotWindow(startFrame, segment.Frames);
		Assert.True(reference.Length > start + length, segment.Name + " reference window is outside the SidPlayFP capture.");
		Assert.True(candidate.Length > start + length, segment.Name + " candidate window is outside the CopperMod render.");

		var offset = FindBestCandidateOffset(reference, candidate, start, length, maxOffset: SampleRate / 40);
		switch (segment.Kind)
		{
			case OracleSegmentKind.Harmonics:
				AssertHarmonicRatios(segment.Name, segment.Frequency, reference, candidate, start, start + offset, length);
				break;
			case OracleSegmentKind.Level:
				AssertLevelShape(segment.Name, reference, candidate, start, start + offset, length);
				break;
			case OracleSegmentKind.Correlation:
				var correlation = Correlation(reference, candidate, start, start + offset, length);
				Assert.True(
					correlation >= segment.MinimumCorrelation,
					$"{segment.Name} correlation {correlation:0.000} was below {segment.MinimumCorrelation:0.000} at candidate offset {offset}.");
				break;
		}
	}

	private static void AssertAdsrRestartShape(float[] reference, float[] candidate, int referenceStart, int candidateStart, int length)
	{
		var referenceAc = AcRms(reference, referenceStart, length);
		var candidateAc = AcRms(candidate, candidateStart, length);
		var ratio = candidateAc / Math.Max(1.0e-12, referenceAc);
		var correlation = Correlation(reference, candidate, referenceStart, candidateStart, length);
		Assert.True(referenceAc > 0.050, "ADSR restart SidPlayFP reference was unexpectedly quiet.");
		Assert.True(candidateAc > 0.020, "ADSR restart CopperMod candidate was unexpectedly quiet.");
		Assert.True(
			ratio is >= 0.20 and <= 2.50,
			$"ADSR restart AC ratio {ratio:0.0000} was outside 0.20..2.50: reference {referenceAc:0.0000}, candidate {candidateAc:0.0000}.");
		Assert.True(
			correlation >= 0.25,
			$"ADSR restart correlation {correlation:0.0000} was below 0.25.");
	}

	private static (int Start, int Length) WeakSpotWindow(int startFrame, int frames)
	{
		var segmentStartSeconds = startFrame / (double)SegmentRate;
		var segmentSeconds = frames / (double)SegmentRate;
		var skipSeconds = Math.Min(0.10, segmentSeconds * 0.20);
		var tailSeconds = Math.Min(0.06, segmentSeconds * 0.15);
		var start = SecondsToSamples(segmentStartSeconds + skipSeconds);
		var length = SecondsToSamples(Math.Max(0.08, segmentSeconds - skipSeconds - tailSeconds));
		return (start, length);
	}

	private static byte[] CreateOracleSid()
	{
		var asm = new Mos6510Emitter(ProgramBase);
		asm.Label("init");
		EmitClearSid(asm);
		asm.LdaImm(0);
		asm.StaZp(FrameCounterAddress);
		asm.StaZp(FrameCounterHighAddress);
		asm.Rts();

		asm.Label("play");
		for (var i = 0; i < Segments.Length - 1; i++)
		{
			EmitBranchIfFrameCounterLessThan(asm, (i + 1) * FramesPerSegment, "route" + i.ToString(CultureInfo.InvariantCulture));
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
		EmitIncrementFrameCounter(asm);
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

	private static byte[] CreateWeakSpotOracleSid()
	{
		var asm = new Mos6510Emitter(ProgramBase);
		asm.Label("init");
		EmitClearSid(asm);
		asm.LdaImm(0);
		asm.StaZp(FrameCounterAddress);
		asm.StaZp(FrameCounterHighAddress);
		asm.Rts();

		asm.Label("play");
		var cumulativeFrames = 0;
		for (var i = 0; i < WeakSpotSegments.Length - 1; i++)
		{
			cumulativeFrames += WeakSpotSegments[i].Frames;
			EmitBranchIfFrameCounterLessThan(asm, cumulativeFrames, "weak-route" + i.ToString(CultureInfo.InvariantCulture));
		}

		asm.Jmp("weak-route" + (WeakSpotSegments.Length - 1).ToString(CultureInfo.InvariantCulture));
		for (var i = 0; i < WeakSpotSegments.Length; i++)
		{
			asm.Label("weak-route" + i.ToString(CultureInfo.InvariantCulture));
			asm.Jmp("weak-segment" + i.ToString(CultureInfo.InvariantCulture));
		}

		var startFrame = 0;
		for (var i = 0; i < WeakSpotSegments.Length; i++)
		{
			asm.Label("weak-segment" + i.ToString(CultureInfo.InvariantCulture));
			EmitWeakSpotSegment(asm, i, startFrame);
			asm.Jmp("weak-done");
			startFrame += WeakSpotSegments[i].Frames;
		}

		asm.Label("weak-done");
		EmitIncrementFrameCounter(asm);
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
			title: "CopperMod SID Weak Spot Oracle",
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
			case 10:
				EmitSingleVoice(asm, voice: 0, frequency: 0x4000, pulseWidth: 0x0800, control: 0xA1);
				break;
			case 11:
				EmitSyncRingModulation(asm);
				break;
			case 12:
				EmitRingTrianglePulse(asm);
				break;
			case 13:
				EmitSingleVoice(asm, voice: 0, frequency: 0x1000, pulseWidth: 0x0000, control: 0x41);
				break;
			case 14:
				EmitSingleVoice(asm, voice: 0, frequency: 0x1000, pulseWidth: 0x0001, control: 0x41);
				break;
			case 15:
				EmitSingleVoice(asm, voice: 0, frequency: 0x1000, pulseWidth: 0x0FFE, control: 0x41);
				break;
			default:
				EmitSingleVoice(asm, voice: 0, frequency: 0x1000, pulseWidth: 0x0FFF, control: 0x41);
				break;
		}
	}

	private static void EmitWeakSpotSegment(Mos6510Emitter asm, int segmentIndex, int startFrame)
	{
		EmitSegmentResetIfFirstFrame(asm, "weak-" + segmentIndex.ToString(CultureInfo.InvariantCulture), startFrame);
		switch (segmentIndex)
		{
			case 0:
				EmitWeakSpotAdsrPiano(asm, startFrame);
				break;
			case 1:
				EmitWeakSpotD418Sweep(asm, startFrame);
				break;
			case 2:
				EmitWeakSpotCombinedMask(asm, control: 0x31);
				break;
			case 3:
				EmitWeakSpotCombinedMask(asm, control: 0x51);
				break;
			case 4:
				EmitWeakSpotCombinedMask(asm, control: 0x61);
				break;
			default:
				EmitWeakSpotCombinedMask(asm, control: 0x71);
				break;
		}
	}

	private static void EmitSegmentResetIfFirstFrame(Mos6510Emitter asm, int segmentIndex)
		=> EmitSegmentResetIfFirstFrame(
			asm,
			segmentIndex.ToString(CultureInfo.InvariantCulture),
			segmentIndex * FramesPerSegment);

	private static void EmitSegmentResetIfFirstFrame(Mos6510Emitter asm, string labelSuffix, int startFrame)
	{
		var skipLabel = "segment-reset-skip" + labelSuffix;
		asm.LdaZp(FrameCounterAddress);
		asm.CmpImm((byte)startFrame);
		asm.Bne(skipLabel);
		asm.LdaZp(FrameCounterHighAddress);
		asm.CmpImm((byte)(startFrame >> 8));
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

	private static void EmitBranchIfFrameCounterLessThan(Mos6510Emitter asm, int threshold, string targetLabel)
	{
		var labelStem = "frame-compare-" + threshold.ToString(CultureInfo.InvariantCulture) + "-" + targetLabel;
		var nextLabel = labelStem + "-next";
		var branchLabel = labelStem + "-branch";
		asm.LdaZp(FrameCounterHighAddress);
		asm.CmpImm((byte)(threshold >> 8));
		asm.Bcc(branchLabel);
		asm.Bne(nextLabel);
		asm.LdaZp(FrameCounterAddress);
		asm.CmpImm((byte)threshold);
		asm.Bcc(branchLabel);
		asm.Label(nextLabel);
		asm.Jmp(labelStem + "-done");
		asm.Label(branchLabel);
		asm.Jmp(targetLabel);
		asm.Label(labelStem + "-done");
	}

	private static void EmitIncrementFrameCounter(Mos6510Emitter asm)
	{
		var doneLabel = "frame-counter-increment-done";
		asm.IncZp(FrameCounterAddress);
		asm.Bne(doneLabel);
		asm.IncZp(FrameCounterHighAddress);
		asm.Label(doneLabel);
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

	private static void EmitWeakSpotAdsrPiano(Mos6510Emitter asm, int startFrame)
	{
		var onLabel = "weak-adsr-on";
		var offLabel = "weak-adsr-off";
		var doneLabel = "weak-adsr-done";
		EmitWeakSpotVoiceSetup(asm, attackDecay: 0x0D, sustainRelease: 0x08, control: 0x20);
		ReadOnlySpan<int> gates = stackalloc[] { 2, 8, 10, 16, 18, 24, 26, 32, 34 };
		for (var i = 0; i < gates.Length; i++)
		{
			EmitBranchIfFrameCounterLessThan(asm, startFrame + gates[i], (i & 1) == 0 ? onLabel : offLabel);
		}

		asm.Jmp(offLabel);
		asm.Label(onLabel);
		asm.LdaImm(0x21);
		asm.StaAbs(SidBase + 0x04);
		asm.Jmp(doneLabel);
		asm.Label(offLabel);
		asm.LdaImm(0x20);
		asm.StaAbs(SidBase + 0x04);
		asm.Label(doneLabel);
	}

	private static void EmitWeakSpotD418Sweep(Mos6510Emitter asm, int startFrame)
	{
		EmitWeakSpotVoiceSetup(asm, attackDecay: 0x00, sustainRelease: 0xF0, control: 0x11);
		var doneLabel = "weak-d418-done";
		for (var frame = 0; frame < WeakSpotD418Frames; frame++)
		{
			var label = "weak-d418-frame-" + frame.ToString(CultureInfo.InvariantCulture);
			EmitBranchIfFrameCounterLessThan(asm, startFrame + frame + 1, label);
		}

		asm.Jmp("weak-d418-frame-" + (WeakSpotD418Frames - 1).ToString(CultureInfo.InvariantCulture));
		for (var frame = 0; frame < WeakSpotD418Frames; frame++)
		{
			var label = "weak-d418-frame-" + frame.ToString(CultureInfo.InvariantCulture);
			var volume = WeakSpotD418VolumeForFrame(frame);
			asm.Label(label);
			asm.LdaImm((byte)volume);
			asm.StaAbs(SidBase + 0x18);
			asm.Jmp(doneLabel);
		}

		asm.Label(doneLabel);
	}

	private static int WeakSpotD418VolumeForFrame(int frame)
	{
		var phase = frame & 0x1F;
		return phase < 16 ? 15 - phase : phase - 16;
	}

	private static void EmitWeakSpotCombinedMask(Mos6510Emitter asm, byte control)
	{
		EmitWeakSpotVoiceSetup(asm, attackDecay: 0x00, sustainRelease: 0xF0, control);
	}

	private static void EmitWeakSpotVoiceSetup(Mos6510Emitter asm, byte attackDecay, byte sustainRelease, byte control)
	{
		asm.LdaImm(0);
		asm.StaAbs(SidBase + 0x0B);
		asm.StaAbs(SidBase + 0x12);
		asm.StaAbs(SidBase + 0x15);
		asm.StaAbs(SidBase + 0x16);
		asm.StaAbs(SidBase + 0x17);
		asm.LdaImm(0x0F);
		asm.StaAbs(SidBase + 0x18);
		asm.LdaImm(0x31);
		asm.StaAbs(SidBase + 0x00);
		asm.LdaImm(0x1C);
		asm.StaAbs(SidBase + 0x01);
		asm.LdaImm(0);
		asm.StaAbs(SidBase + 0x02);
		asm.LdaImm(0x08);
		asm.StaAbs(SidBase + 0x03);
		asm.LdaImm(attackDecay);
		asm.StaAbs(SidBase + 0x05);
		asm.LdaImm(sustainRelease);
		asm.StaAbs(SidBase + 0x06);
		asm.LdaImm(control);
		asm.StaAbs(SidBase + 0x04);
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

	private static void EmitSyncRingModulation(Mos6510Emitter asm)
	{
		asm.LdaImm(0);
		asm.StaAbs(SidBase + 0x04);
		asm.StaAbs(SidBase + 0x0B);
		EmitVoiceRegisters(asm, voice: 1, frequency: 0x2300, pulseWidth: 0x0800, control: 0x10);
		EmitVoiceRegisters(asm, voice: 2, frequency: 0x1400, pulseWidth: 0x0800, control: 0x17);
	}

	private static void EmitRingTrianglePulse(Mos6510Emitter asm)
	{
		asm.LdaImm(0);
		asm.StaAbs(SidBase + 0x04);
		asm.StaAbs(SidBase + 0x12);
		EmitVoiceRegisters(asm, voice: 0, frequency: 0x0900, pulseWidth: 0x0800, control: 0x00);
		EmitVoiceRegisters(asm, voice: 1, frequency: 0x1000, pulseWidth: 0x0800, control: 0x55);
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

	private static void RunSidPlayFp(string sidPlayFp, string workingDirectory, double renderSeconds)
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
		startInfo.ArgumentList.Add("-f" + SampleRate.ToString(CultureInfo.InvariantCulture));
		startInfo.ArgumentList.Add("-p32");
		startInfo.ArgumentList.Add("-t" + FormatSidPlayFpDuration(renderSeconds));
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
		=> RenderCopperModDiagnostic(sidPath, seconds, captureChannels: false).Samples;

	private static CopperModDiagnosticRender RenderCopperModDiagnostic(string sidPath, double seconds, bool captureChannels)
	{
		using var song = (SidSong)new SidFormat().Load(File.ReadAllBytes(sidPath));
		((ISidEmulationProfileController)song).SidEmulationProfile = SidEmulationProfile.Balanced;
		var channelProvider = (IModuleChannelWaveformProvider)song;
		channelProvider.ChannelWaveformCaptureEnabled = captureChannels;
		var options = new AudioRenderOptions(SampleRate, channelCount: 1);
		var targetFrames = SecondsToSamples(seconds);
		var samples = new List<float>(targetFrames + SampleRate);
		var channelSamples = captureChannels
			? new[] { new List<float>(targetFrames), new List<float>(targetFrames), new List<float>(targetFrames) }
			: null;
		while (samples.Count < targetFrames)
		{
			var frames = song.GetCurrentTickFrameCount(options);
			var buffer = new float[options.GetSampleCount(frames)];
			var result = song.RenderTick(buffer, options);
			var written = Math.Min(result.SamplesWritten, buffer.Length);
			var framesToKeep = Math.Min(written / options.ChannelCount, targetFrames - samples.Count);
			for (var i = 0; i < framesToKeep * options.ChannelCount; i++)
			{
				samples.Add(buffer[i]);
			}

			if (channelSamples == null)
			{
				continue;
			}

			var waveform = channelProvider.LastChannelWaveform;
			Assert.NotNull(waveform);
			for (var channel = 0; channel < channelSamples.Length && channel < waveform.Channels.Count; channel++)
			{
				var source = waveform.Channels[channel].Samples;
				for (var i = 0; i < framesToKeep && i < source.Length; i++)
				{
					channelSamples[channel].Add(source[i]);
				}
			}
		}

		return new CopperModDiagnosticRender(
			samples.ToArray(),
			channelSamples?.Select(channel => channel.ToArray()).ToArray(),
			captureChannels ? song.SidWrites.ToArray() : null);
	}

	private static void AssertHarmonicRatios(
		string segmentName,
		ushort frequency,
		float[] reference,
		float[] candidate,
		int referenceStart,
		int candidateStart,
		int length)
	{
		var fundamental = SidFrequencyToHz(frequency);
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

		Assert.True(referenceTotal > 1.0e-7, segmentName + " SidPlayFP harmonic energy was too small.");
		Assert.True(candidateTotal > 1.0e-7, segmentName + " CopperMod harmonic energy was too small.");
		for (var harmonic = 1; harmonic <= 8; harmonic++)
		{
			var referenceRatio = referenceMagnitudes[harmonic - 1] / referenceTotal;
			var candidateRatio = candidateMagnitudes[harmonic - 1] / candidateTotal;
			if (referenceRatio < 0.02)
			{
				Assert.True(
					candidateRatio < CombinedNearNullRatioLimit,
					$"{segmentName} harmonic {harmonic} should stay near-null: reference {referenceRatio:0.0000}, candidate {candidateRatio:0.0000}. " +
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
				$"{segmentName} harmonic {harmonic} ratio mismatch: reference {referenceRatio:0.0000}, candidate {candidateRatio:0.0000}, relative error {relativeError:0.000}, absolute error {absoluteError:0.0000}. " +
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
		Assert.InRange(candidateFlatness, 0.03, 1.05);

		ReadOnlySpan<double> bands = stackalloc[] { 1000.0, 2000.0, 4000.0, 8000.0, 12000.0, 16000.0 };
		var referenceEnergy = 0.0;
		var candidateEnergy = 0.0;
		for (var i = 0; i < bands.Length; i++)
		{
			referenceEnergy += HarmonicMagnitude(reference, referenceStart, length, bands[i]);
			candidateEnergy += HarmonicMagnitude(candidate, candidateStart, length, bands[i]);
		}

		Assert.True(referenceEnergy > 1.0e-7, segment.Name + " SidPlayFP noise band energy was too small.");
		if (candidateEnergy <= referenceEnergy * 0.05)
		{
			return;
		}
	}

	private static void AssertLevelShape(
		string segmentName,
		float[] reference,
		float[] candidate,
		int referenceStart,
		int candidateStart,
		int length)
	{
		var referenceMean = Mean(reference, referenceStart, length);
		var candidateMean = Mean(candidate, candidateStart, length);
		var referenceAc = AcRms(reference, referenceStart, length);
		var candidateAc = AcRms(candidate, candidateStart, length);
		Assert.True(
			Math.Abs(candidateMean - referenceMean) <= 0.75,
			$"{segmentName} mean level mismatch: reference {referenceMean:0.0000}, candidate {candidateMean:0.0000}.");
		if (referenceAc < 0.010)
		{
			Assert.True(
				candidateAc < 0.075,
				$"{segmentName} should remain near-DC: reference AC {referenceAc:0.0000}, candidate AC {candidateAc:0.0000}.");
			return;
		}

		var ratio = candidateAc / referenceAc;
		Assert.True(
			ratio is >= 0.10 and <= 5.00,
			$"{segmentName} AC ratio {ratio:0.0000} was outside 0.10..5.00: reference {referenceAc:0.0000}, candidate {candidateAc:0.0000}.");
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

	private sealed record WeakSpotSegment(
		string Name,
		OracleSegmentKind Kind,
		ushort Frequency,
		double MinimumCorrelation,
		int Frames);

	private sealed record CopperModDiagnosticRender(
		float[] Samples,
		float[][]? ChannelSamples,
		SidRegisterWrite[]? SidWrites);

	private readonly record struct AdsrPianoDebugRow(
		double TimeSeconds,
		int Frame,
		bool Gate,
		int EnvelopeCounter,
		double EnvelopeDac,
		int EnvelopeState,
		int RateCounter,
		int ExponentialCounter,
		byte Control);

	private readonly record struct AdsrPianoTraceRow(
		double TimeMilliseconds,
		int Frame,
		bool Gate,
		int AlignmentOffsetSamples,
		double ReferenceAc,
		double CandidateAc,
		double AcRatio,
		double CandidateVoice0Ac,
		double FinalToVoice0AcRatio,
		double ReferenceFundamental,
		double CandidateFundamental,
		double FundamentalRatio,
		double CandidateVoice0Fundamental,
		double FinalToVoice0FundamentalRatio,
		int EnvelopeCounter,
		double EnvelopeDac,
		int EnvelopeState,
		int RateCounter,
		int ExponentialCounter,
		byte Control);

	private readonly record struct AdsrPianoGateEdgeRow(
		int Frame,
		double TimeMilliseconds,
		bool IntendedGate,
		byte BeforeControl,
		int BeforeEnvelope,
		int BeforeEnvelopeState,
		byte AfterWriteControl,
		int AfterWriteEnvelope,
		int AfterWriteEnvelopeState,
		byte AfterOneCycleControl,
		int AfterOneCycleEnvelope,
		int AfterOneCycleEnvelopeState,
		int AfterOneCycleRateCounter,
		int AfterOneCycleExponentialCounter);

	private enum OracleSegmentKind
	{
		Correlation,
		Harmonics,
		Noise,
		NoiseCombined,
		Level
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
