using System.Globalization;
using System.Text;
using CopperMod.Abstractions;
using CopperMod.Sid;

namespace CopperMod.Tools;

internal static partial class SidConformance
{
	private const double DiagnosticTraceStepSeconds = 0.004;
	private const double DiagnosticTraceWindowSeconds = 0.012;
	private const double NearDcReferenceAcThreshold = 0.012;
	private const double NearDcAbsoluteLimit = 0.0025;
	private const double NearDcRelativeLimit = 3.0;

	private static void CollectFixtureDiagnostics(
		SidConformanceFixture fixture,
		int sampleRate,
		float[] referenceSamples,
		float[] playerSamples,
		float[] rawSamples,
		IReadOnlyList<SidConformanceSegmentComparisonResult> segmentResults,
		SidConformanceDiagnostics diagnostics)
	{
		if (IsAdsrDiagnosticsFixture(fixture.Spec))
		{
			AddAdsrDiagnostics(fixture, sampleRate, referenceSamples, playerSamples, rawSamples, diagnostics);
		}

		if (IsFilterDiagnosticsFixture(fixture.Spec))
		{
			AddFilterDiagnostics(fixture, sampleRate, referenceSamples, playerSamples, rawSamples, segmentResults, diagnostics);
		}

		if (IsWaveformEdgeDiagnosticsFixture(fixture.Spec, segmentResults))
		{
			AddWaveformEdgeDiagnostics(fixture, sampleRate, referenceSamples, playerSamples, rawSamples, segmentResults, diagnostics);
		}
	}

	private static void WriteDiagnosticCsvs(string outputDirectory, SidConformanceDiagnostics diagnostics)
	{
		if (diagnostics.AdsrTraceRows.Count > 0)
		{
			WriteAdsrTraceCsv(Path.Combine(outputDirectory, "conformance-adsr-trace.csv"), diagnostics.AdsrTraceRows);
		}

		if (diagnostics.AdsrPulseRows.Count > 0)
		{
			WriteAdsrPulseCsv(Path.Combine(outputDirectory, "conformance-adsr-pulses.csv"), diagnostics.AdsrPulseRows);
		}

		if (diagnostics.FilterRows.Count > 0)
		{
			WriteFilterBandCsv(Path.Combine(outputDirectory, "conformance-filter-bands.csv"), diagnostics.FilterRows);
		}

		if (diagnostics.WaveformRows.Count > 0)
		{
			WriteWaveformEdgeCsv(Path.Combine(outputDirectory, "conformance-waveform-edges.csv"), diagnostics.WaveformRows);
		}
	}

	private static void AppendDiagnosticIndexSummary(StringBuilder builder, SidConformanceDiagnostics diagnostics)
	{
		if (!diagnostics.HasRows)
		{
			return;
		}

		builder.Append("<div class=\"summary\">");
		if (diagnostics.AdsrTraceRows.Count > 0)
		{
			builder.Append("<span><a href=\"conformance-adsr-trace.csv\">ADSR trace</a></span>");
		}

		if (diagnostics.AdsrPulseRows.Count > 0)
		{
			builder.Append("<span><a href=\"conformance-adsr-pulses.csv\">ADSR pulses</a></span>");
		}

		if (diagnostics.FilterRows.Count > 0)
		{
			builder.Append("<span><a href=\"conformance-filter-bands.csv\">Filter bands</a></span>");
		}

		if (diagnostics.WaveformRows.Count > 0)
		{
			builder.Append("<span><a href=\"conformance-waveform-edges.csv\">Waveform edges</a></span>");
		}

		builder.Append("</div>");
		AppendWorstDiagnosticRows(builder, diagnostics);
	}

	private static void AppendWorstDiagnosticRows(StringBuilder builder, SidConformanceDiagnostics diagnostics)
	{
		builder.Append("<div class=\"summary\">");
		var wrote = false;
		if (diagnostics.AdsrPulseRows.Count > 0)
		{
			var worst = diagnostics.AdsrPulseRows.OrderByDescending(row => Math.Abs(Math.Log(Math.Max(1.0e-12, row.PeakRatio), 2.0))).First();
			builder
				.Append("<span>Worst ADSR pulse: ")
				.Append(Html(worst.Id))
				.Append(' ')
				.Append(worst.Pulse.ToString(CultureInfo.InvariantCulture))
				.Append(" peak ")
				.Append(FormatInvariant(worst.PeakRatio))
				.Append("x</span>");
			wrote = true;
		}

		if (diagnostics.FilterRows.Count > 0)
		{
			var worst = diagnostics.FilterRows.OrderByDescending(row => row.Score).First();
			builder
				.Append("<span>Worst filter band: ")
				.Append(Html(worst.Id))
				.Append(' ')
				.Append(Html(worst.Segment))
				.Append(" score ")
				.Append(FormatInvariant(worst.Score))
				.Append("</span>");
			wrote = true;
		}

		if (diagnostics.WaveformRows.Count > 0)
		{
			var worst = diagnostics.WaveformRows.OrderByDescending(row => row.Score).First();
			builder
				.Append("<span>Worst waveform edge: ")
				.Append(Html(worst.Id))
				.Append(' ')
				.Append(Html(worst.Segment))
				.Append(" ratio ")
				.Append(FormatInvariant(worst.PlayerRatio))
				.Append(" corr ")
				.Append(worst.PlayerCorrelation.ToString("0.####", CultureInfo.InvariantCulture))
				.Append("</span>");
			wrote = true;
		}

		if (!wrote)
		{
			builder.Append("<span>No specialized diagnostics emitted.</span>");
		}

		builder.Append("</div>");
	}

	private static bool IsAdsrDiagnosticsFixture(SidConformanceFixtureSpec spec)
		=> HasTag(spec, "adsr") || string.Equals(spec.TargetLayer, "digital", StringComparison.OrdinalIgnoreCase) &&
			spec.Id.Contains("adsr", StringComparison.OrdinalIgnoreCase);

	private static bool IsFilterDiagnosticsFixture(SidConformanceFixtureSpec spec)
		=> HasTag(spec, "filter") || string.Equals(spec.TargetLayer, "filter", StringComparison.OrdinalIgnoreCase);

	private static bool IsWaveformEdgeDiagnosticsFixture(
		SidConformanceFixtureSpec spec,
		IReadOnlyList<SidConformanceSegmentComparisonResult> segments)
	{
		if (HasTag(spec, "noise") || HasTag(spec, "combined-wave") || HasTag(spec, "waveform-edge"))
		{
			return true;
		}

		return segments.Any(segment => IsCombinedOrNoiseSegmentName(segment.Segment));
	}

	private static bool HasTag(SidConformanceFixtureSpec spec, string tag)
		=> spec.Tags.Any(value => string.Equals(value, tag, StringComparison.OrdinalIgnoreCase));

	private static void AddAdsrDiagnostics(
		SidConformanceFixture fixture,
		int sampleRate,
		float[] referenceSamples,
		float[] playerSamples,
		float[] rawSamples,
		SidConformanceDiagnostics diagnostics)
	{
		var render = RenderCopperModDiagnostic(fixture, sampleRate, captureChannels: true);
		var voice0 = render.ChannelSamples is { Length: > 0 } ? render.ChannelSamples[0] : null;
		var traceRows = BuildAdsrTraceRows(
			fixture,
			sampleRate,
			referenceSamples,
			playerSamples,
			rawSamples,
			voice0,
			render.SidWrites);
		diagnostics.AdsrTraceRows.AddRange(traceRows);
		diagnostics.AdsrPulseRows.AddRange(BuildAdsrPulseRows(fixture, traceRows));
	}

	private static IReadOnlyList<AdsrTraceDiagnosticRow> BuildAdsrTraceRows(
		SidConformanceFixture fixture,
		int sampleRate,
		float[] referenceSamples,
		float[] playerSamples,
		float[] rawSamples,
		float[]? voice0Samples,
		SidRegisterWrite[] writes)
	{
		var debugRows = BuildAdsrDebugRows(fixture.Spec, writes);
		var rows = new List<AdsrTraceDiagnosticRow>(debugRows.Count);
		var windowLength = Math.Max(32, SecondsToSamples(DiagnosticTraceWindowSeconds, sampleRate));
		var alignmentLength = Math.Min(
			referenceSamples.Length,
			Math.Min(playerSamples.Length, SecondsToSamples(fixture.Spec.Seconds, sampleRate)));
		var alignmentOffset = alignmentLength > windowLength
			? FindBestCandidateOffset(referenceSamples, playerSamples, 0, alignmentLength, maxOffset: sampleRate / 40)
			: 0;
		var fundamental = SidFrequencyToHz(FindVoiceFrequency(fixture.Spec, voiceOffset: 0));

		foreach (var debug in debugRows)
		{
			var center = SecondsToSamples(debug.TimeSeconds, sampleRate);
			var referenceStart = WindowStart(referenceSamples.Length, center - alignmentOffset, windowLength);
			var playerStart = WindowStart(playerSamples.Length, center, windowLength);
			var rawStart = WindowStart(rawSamples.Length, center, windowLength);
			var voice0Ac = double.NaN;
			var voice0Fundamental = double.NaN;
			if (voice0Samples != null && voice0Samples.Length >= windowLength)
			{
				var voiceStart = WindowStart(voice0Samples.Length, center, windowLength);
				voice0Ac = AcRms(voice0Samples, voiceStart, windowLength);
				voice0Fundamental = HarmonicMagnitude(voice0Samples, voiceStart, windowLength, fundamental, sampleRate);
			}

			var referenceAc = AcRms(referenceSamples, referenceStart, windowLength);
			var playerAc = AcRms(playerSamples, playerStart, windowLength);
			var rawAc = AcRms(rawSamples, rawStart, windowLength);
			var referenceFundamental = HarmonicMagnitude(referenceSamples, referenceStart, windowLength, fundamental, sampleRate);
			var playerFundamental = HarmonicMagnitude(playerSamples, playerStart, windowLength, fundamental, sampleRate);
			rows.Add(new AdsrTraceDiagnosticRow(
				fixture.Id,
				fixture.Spec.SidtestCategory,
				fixture.Spec.Name,
				debug.TimeSeconds * 1000.0,
				debug.Frame,
				debug.Gate,
				alignmentOffset,
				referenceAc,
				playerAc,
				playerAc / Math.Max(1.0e-12, referenceAc),
				rawAc,
				voice0Ac,
				RatioOrNaN(playerAc, voice0Ac),
				referenceFundamental,
				playerFundamental,
				playerFundamental / Math.Max(1.0e-12, referenceFundamental),
				voice0Fundamental,
				RatioOrNaN(playerFundamental, voice0Fundamental),
				debug.EnvelopeCounter,
				SidAnalog.ConvertEnvelope(debug.EnvelopeCounter, SidChipModel.Mos6581),
				debug.EnvelopeState,
				debug.RateCounter,
				debug.ExponentialCounter,
				debug.Control));
		}

		return rows;
	}

	private static IReadOnlyList<AdsrDebugRow> BuildAdsrDebugRows(SidConformanceFixtureSpec spec, SidRegisterWrite[] writes)
	{
		var chip = new SidChip(
			SidChipModel.Mos6581,
			SidBase,
			SidConstants.PalCpuCyclesPerSecond,
			SidFilterProfileId.Auto,
			SidEmulationProfile.Balanced);
		var replayWrites = writes
			.Where(write => write.ChipIndex == 0 && write.Cycle >= 0 && IsAdsrProbeRegister(write.Register))
			.OrderBy(write => write.Cycle)
			.ToArray();
		var rows = new List<AdsrDebugRow>();
		var currentCycle = 0L;
		var nextWrite = 0;
		var totalSeconds = spec.Seconds;
		for (var timeSeconds = DiagnosticTraceStepSeconds; timeSeconds < totalSeconds; timeSeconds += DiagnosticTraceStepSeconds)
		{
			var targetCycle = (long)Math.Round(timeSeconds * SidConstants.PalCpuCyclesPerSecond);
			while (nextWrite < replayWrites.Length && replayWrites[nextWrite].Cycle <= targetCycle)
			{
				var write = replayWrites[nextWrite++];
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

			var voice = chip.DebugState.Voices[0];
			rows.Add(new AdsrDebugRow(
				timeSeconds,
				(int)Math.Floor(timeSeconds * (spec.SegmentRate ?? DefaultSegmentRate)),
				(voice.Control & 0x01) != 0,
				voice.EnvelopeCounter,
				voice.EnvelopeState,
				voice.RateCounter,
				voice.ExponentialCounter,
				voice.Control));
		}

		return rows;
	}

	private static IEnumerable<AdsrPulseDiagnosticRow> BuildAdsrPulseRows(
		SidConformanceFixture fixture,
		IReadOnlyList<AdsrTraceDiagnosticRow> traceRows)
	{
		if (traceRows.Count == 0)
		{
			yield break;
		}

		var pulses = FindGatePulses(fixture.Spec).ToArray();
		for (var i = 0; i < pulses.Length; i++)
		{
			var pulse = pulses[i];
			var onMs = pulse.OnFrame * 1000.0 / (fixture.Spec.SegmentRate ?? DefaultSegmentRate);
			var offMs = pulse.OffFrame * 1000.0 / (fixture.Spec.SegmentRate ?? DefaultSegmentRate);
			var peakRows = traceRows
				.Where(row => row.TimeMs >= onMs && row.TimeMs < offMs + 40.0)
				.ToArray();
			if (peakRows.Length == 0)
			{
				continue;
			}

			var peak = peakRows.OrderByDescending(row => row.RefAc).First();
			var gateOff = NearestAdsrTraceRow(traceRows, offMs);
			var tail = NearestAdsrTraceRow(traceRows, offMs + 40.0);
			yield return new AdsrPulseDiagnosticRow(
				fixture.Id,
				fixture.Spec.SidtestCategory,
				fixture.Spec.Name,
				i + 1,
				onMs,
				offMs,
				peak.RefAc,
				peak.CandPlayerAc,
				peak.PlayerRatio,
				peak.CandVoice0Ac,
				peak.FinalToVoice0Ac,
				gateOff.RefAc,
				gateOff.CandPlayerAc,
				gateOff.PlayerRatio,
				gateOff.CandVoice0Ac,
				gateOff.FinalToVoice0Ac,
				tail.RefAc,
				tail.CandPlayerAc,
				tail.PlayerRatio,
				tail.CandVoice0Ac,
				tail.FinalToVoice0Ac,
				peak.EnvelopeCounter,
				gateOff.EnvelopeCounter,
				tail.EnvelopeCounter,
				EnvelopeSlope(peak.EnvelopeCounter, gateOff.EnvelopeCounter, Math.Max(1.0, gateOff.TimeMs - peak.TimeMs)),
				EnvelopeSlope(gateOff.EnvelopeCounter, tail.EnvelopeCounter, Math.Max(1.0, tail.TimeMs - gateOff.TimeMs)),
				FormatEnvelopeState(tail.EnvelopeState));
		}
	}

	private static IEnumerable<GatePulse> FindGatePulses(SidConformanceFixtureSpec spec)
	{
		var segmentRate = spec.SegmentRate ?? DefaultSegmentRate;
		_ = segmentRate;
		var frame = 0;
		var gate = FindCommonControlGate(spec);
		int? pulseStart = gate ? 0 : null;
		foreach (var segment in spec.Segments)
		{
			var nextGate = gate;
			foreach (var write in segment.Writes)
			{
				if (ParseAddress(write.Address) == SidBase + 0x04)
				{
					nextGate = (ParseHex(write.Value, max: 0xFF) & 0x01) != 0;
				}
			}

			if (!gate && nextGate)
			{
				pulseStart = frame;
			}
			else if (gate && !nextGate && pulseStart.HasValue)
			{
				yield return new GatePulse(pulseStart.Value, frame);
				pulseStart = null;
			}

			gate = nextGate;
			frame += segment.Frames;
		}

		if (pulseStart.HasValue)
		{
			yield return new GatePulse(pulseStart.Value, frame);
		}
	}

	private static bool FindCommonControlGate(SidConformanceFixtureSpec spec)
	{
		foreach (var write in spec.CommonWrites)
		{
			if (ParseAddress(write.Address) == SidBase + 0x04)
			{
				return (ParseHex(write.Value, max: 0xFF) & 0x01) != 0;
			}
		}

		return false;
	}

	private static void AddFilterDiagnostics(
		SidConformanceFixture fixture,
		int sampleRate,
		float[] referenceSamples,
		float[] playerSamples,
		float[] rawSamples,
		IReadOnlyList<SidConformanceSegmentComparisonResult> segmentResults,
		SidConformanceDiagnostics diagnostics)
	{
		foreach (var segment in segmentResults)
		{
			var (start, length) = SegmentWindow(segment, sampleRate, referenceSamples, playerSamples, rawSamples);
			if (length <= 0)
			{
				continue;
			}

			var reference = MeasureBands(referenceSamples, start, length, sampleRate);
			var player = MeasureBands(playerSamples, start, length, sampleRate);
			var raw = MeasureBands(rawSamples, start, length, sampleRate);
			diagnostics.FilterRows.Add(new FilterBandDiagnosticRow(
				fixture.Id,
				fixture.Spec.SidtestCategory,
				fixture.Spec.Name,
				segment.SegmentIndex,
				segment.Segment,
				segment.StartMs,
				segment.EndMs,
				reference.Low,
				reference.Mid,
				reference.High,
				player.Low,
				player.Mid,
				player.High,
				raw.Low,
				raw.Mid,
				raw.High));
		}
	}

	private static void AddWaveformEdgeDiagnostics(
		SidConformanceFixture fixture,
		int sampleRate,
		float[] referenceSamples,
		float[] playerSamples,
		float[] rawSamples,
		IReadOnlyList<SidConformanceSegmentComparisonResult> segmentResults,
		SidConformanceDiagnostics diagnostics)
	{
		foreach (var segment in segmentResults)
		{
			if (!IsCombinedOrNoiseSegmentName(segment.Segment) && !HasTag(fixture.Spec, "noise") && !HasTag(fixture.Spec, "combined-wave"))
			{
				continue;
			}

			var (start, length) = SegmentWindow(segment, sampleRate, referenceSamples, playerSamples, rawSamples);
			if (length <= 0)
			{
				continue;
			}

			var referenceFlatness = SpectralFlatness(referenceSamples, start, length, sampleRate);
			var playerFlatness = SpectralFlatness(playerSamples, start, length, sampleRate);
			var rawFlatness = SpectralFlatness(rawSamples, start, length, sampleRate);
			var referenceNoiseBand = NoiseBandEnergy(referenceSamples, start, length, sampleRate);
			var playerNoiseBand = NoiseBandEnergy(playerSamples, start, length, sampleRate);
			var rawNoiseBand = NoiseBandEnergy(rawSamples, start, length, sampleRate);
			var nearDcExpected = IsNearDcSegmentName(segment.Segment);
			var nearDcLimit = Math.Max(NearDcAbsoluteLimit, segment.ReferenceAc * NearDcRelativeLimit);
			diagnostics.WaveformRows.Add(new WaveformEdgeDiagnosticRow(
				fixture.Id,
				fixture.Spec.SidtestCategory,
				fixture.Spec.Name,
				segment.SegmentIndex,
				segment.Segment,
				segment.StartMs,
				segment.EndMs,
				segment.ReferenceAc,
				segment.PlayerAc,
				segment.PlayerRatio,
				segment.PlayerCorrelation,
				referenceFlatness,
				playerFlatness,
				rawFlatness,
				referenceNoiseBand,
				playerNoiseBand,
				rawNoiseBand,
				nearDcExpected,
				nearDcExpected && segment.ReferenceAc < NearDcReferenceAcThreshold ? nearDcLimit : double.NaN,
				!nearDcExpected || segment.ReferenceAc >= NearDcReferenceAcThreshold || segment.PlayerAc <= nearDcLimit));
		}
	}

	private static CopperModDiagnosticRender RenderCopperModDiagnostic(
		SidConformanceFixture fixture,
		int sampleRate,
		bool captureChannels)
	{
		using var song = (SidSong)new SidFormat().Load(new ModuleLoadContext(File.ReadAllBytes(fixture.BinaryPath), fixture.BinaryPath));
		((ISidEmulationProfileController)song).SidEmulationProfile = SidEmulationProfile.Balanced;
		var channelProvider = (IModuleChannelWaveformProvider)song;
		channelProvider.ChannelWaveformCaptureEnabled = captureChannels;
		var options = new AudioRenderOptions(sampleRate, channelCount: 1);
		var targetFrames = SecondsToSamples(fixture.Spec.Seconds, sampleRate);
		var samples = new List<float>(targetFrames + sampleRate);
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
			if (waveform == null)
			{
				throw new InvalidOperationException("SID channel waveform capture was enabled but no waveform was produced.");
			}

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
			captureChannels ? song.SidWrites.ToArray() : Array.Empty<SidRegisterWrite>());
	}

	private static (int Start, int Length) SegmentWindow(
		SidConformanceSegmentComparisonResult segment,
		int sampleRate,
		params float[][] sampleSets)
	{
		var start = SecondsToSamples(segment.StartMs / 1000.0, sampleRate);
		var end = SecondsToSamples(segment.EndMs / 1000.0, sampleRate);
		var available = sampleSets.Min(samples => samples.Length) - start;
		return (start, Math.Min(end - start, available));
	}

	private static BandEnergies MeasureBands(float[] samples, int start, int length, int sampleRate)
	{
		ReadOnlySpan<double> low = stackalloc[] { 180.0, 320.0, 640.0 };
		ReadOnlySpan<double> mid = stackalloc[] { 1000.0, 1800.0, 3200.0 };
		ReadOnlySpan<double> high = stackalloc[] { 5000.0, 8000.0, 12000.0 };
		return new BandEnergies(
			BandMagnitude(samples, start, length, low, sampleRate),
			BandMagnitude(samples, start, length, mid, sampleRate),
			BandMagnitude(samples, start, length, high, sampleRate));
	}

	private static double NoiseBandEnergy(float[] samples, int start, int length, int sampleRate)
	{
		ReadOnlySpan<double> bands = stackalloc[] { 1000.0, 2000.0, 4000.0, 8000.0, 12000.0, 16000.0 };
		return BandMagnitude(samples, start, length, bands, sampleRate);
	}

	private static double BandMagnitude(float[] samples, int start, int length, ReadOnlySpan<double> frequencies, int sampleRate)
	{
		var sum = 0.0;
		foreach (var frequency in frequencies)
		{
			sum += HarmonicMagnitude(samples, start, length, frequency, sampleRate);
		}

		return sum;
	}

	private static double SpectralFlatness(float[] samples, int start, int length, int sampleRate)
	{
		ReadOnlySpan<double> bands = stackalloc[] { 700.0, 1100.0, 1700.0, 2600.0, 3900.0, 5800.0, 8700.0, 13000.0, 18000.0 };
		var logSum = 0.0;
		var sum = 0.0;
		for (var i = 0; i < bands.Length; i++)
		{
			var magnitude = Math.Max(1.0e-12, HarmonicMagnitude(samples, start, length, bands[i], sampleRate));
			logSum += Math.Log(magnitude);
			sum += magnitude;
		}

		return Math.Exp(logSum / bands.Length) / (sum / bands.Length);
	}

	private static double HarmonicMagnitude(float[] samples, int start, int length, double frequency, int sampleRate)
	{
		if (length <= 1)
		{
			return 0.0;
		}

		var mean = Mean(samples, start, length);
		var real = 0.0;
		var imaginary = 0.0;
		for (var i = 0; i < length; i++)
		{
			var window = 0.5 - (0.5 * Math.Cos((2.0 * Math.PI * i) / (length - 1)));
			var phase = (2.0 * Math.PI * frequency * i) / sampleRate;
			var sample = (samples[start + i] - mean) * window;
			real += sample * Math.Cos(phase);
			imaginary -= sample * Math.Sin(phase);
		}

		return Math.Sqrt((real * real) + (imaginary * imaginary)) / length;
	}

	private static int FindBestCandidateOffset(float[] reference, float[] candidate, int start, int length, int maxOffset)
	{
		var bestOffset = 0;
		var bestCorrelation = double.NegativeInfinity;
		for (var offset = -maxOffset; offset <= maxOffset; offset += 8)
		{
			if (start + offset < 0 || start + offset + length >= candidate.Length || start + length >= reference.Length)
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
			if (start + offset < 0 || start + offset + length >= candidate.Length || start + length >= reference.Length)
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

	private static int WindowStart(int sampleCount, int center, int length)
	{
		if (sampleCount <= length)
		{
			return 0;
		}

		return Math.Clamp(center - (length / 2), 0, sampleCount - length);
	}

	private static ushort FindVoiceFrequency(SidConformanceFixtureSpec spec, int voiceOffset)
	{
		var low = FindCommonWriteByte(spec, SidBase + voiceOffset);
		var high = FindCommonWriteByte(spec, SidBase + voiceOffset + 1);
		return (ushort)(low | (high << 8));
	}

	private static int FindCommonWriteByte(SidConformanceFixtureSpec spec, int address)
	{
		for (var i = spec.CommonWrites.Count - 1; i >= 0; i--)
		{
			var write = spec.CommonWrites[i];
			if (ParseAddress(write.Address) == address)
			{
				return ParseHex(write.Value, max: 0xFF);
			}
		}

		return 0;
	}

	private static bool IsAdsrProbeRegister(byte register)
		=> register is <= 0x06 or 0x0B or 0x12 or >= 0x15 and <= 0x18;

	private static bool IsCombinedOrNoiseSegmentName(string segment)
		=> segment.Contains("noise", StringComparison.OrdinalIgnoreCase) ||
			segment.Contains("triangle-saw", StringComparison.OrdinalIgnoreCase) ||
			segment.Contains("triangle-pulse", StringComparison.OrdinalIgnoreCase) ||
			segment.Contains("saw-pulse", StringComparison.OrdinalIgnoreCase) ||
			segment.Contains("all-three", StringComparison.OrdinalIgnoreCase) ||
			segment.Contains("combined", StringComparison.OrdinalIgnoreCase);

	private static bool IsNearDcSegmentName(string segment)
		=> segment.Contains("saw-pulse", StringComparison.OrdinalIgnoreCase) ||
			segment.Contains("triangle-saw-pulse", StringComparison.OrdinalIgnoreCase) ||
			segment.Contains("all-three", StringComparison.OrdinalIgnoreCase) ||
			segment.Contains("noise-all-waveforms", StringComparison.OrdinalIgnoreCase);

	private static AdsrTraceDiagnosticRow NearestAdsrTraceRow(IReadOnlyList<AdsrTraceDiagnosticRow> rows, double timeMilliseconds)
	{
		var best = rows[0];
		var bestDistance = Math.Abs(best.TimeMs - timeMilliseconds);
		for (var i = 1; i < rows.Count; i++)
		{
			var distance = Math.Abs(rows[i].TimeMs - timeMilliseconds);
			if (distance < bestDistance)
			{
				best = rows[i];
				bestDistance = distance;
			}
		}

		return best;
	}

	private static double RatioOrNaN(double numerator, double denominator)
		=> double.IsNaN(denominator) ? double.NaN : numerator / Math.Max(1.0e-12, denominator);

	private static double EnvelopeSlope(int from, int to, double milliseconds)
		=> (to - from) / milliseconds;

	private static double SidFrequencyToHz(ushort frequency)
		=> frequency * (double)SidConstants.PalCpuCyclesPerSecond / 16_777_216.0;

	private static string FormatEnvelopeState(int state)
		=> state switch
		{
			0 => "attack",
			1 => "decay-sustain",
			2 => "release",
			_ => state.ToString(CultureInfo.InvariantCulture)
		};

	private static string CsvDouble(double value)
		=> double.IsFinite(value) ? value.ToString("0.000000", CultureInfo.InvariantCulture) : "NaN";

	private static void WriteAdsrTraceCsv(string path, IReadOnlyList<AdsrTraceDiagnosticRow> rows)
	{
		using var writer = new StreamWriter(path);
		writer.WriteLine("id,category,name,time_ms,frame,gate,alignment_offset_samples,ref_ac,cand_player_ac,player_ratio,cand_raw_ac,cand_voice0_ac,final_to_voice0_ac,ref_fund,cand_player_fund,fund_ratio,cand_voice0_fund,final_to_voice0_fund,cand_envelope,cand_envelope_dac,cand_state,cand_rate_counter,cand_exponential_counter,cand_control");
		foreach (var row in rows)
		{
			writer.WriteLine(row.ToCsvLine());
		}
	}

	private static void WriteAdsrPulseCsv(string path, IReadOnlyList<AdsrPulseDiagnosticRow> rows)
	{
		using var writer = new StreamWriter(path);
		writer.WriteLine("id,category,name,pulse,on_ms,off_ms,ref_peak,cand_peak,peak_ratio,cand_voice0_peak,final_to_voice0_peak,ref_gate_off,cand_gate_off,gate_off_ratio,cand_voice0_gate_off,final_to_voice0_gate_off,ref_tail_40ms,cand_tail_40ms,tail_40ms_ratio,cand_voice0_tail_40ms,final_to_voice0_tail_40ms,cand_env_peak,cand_env_gate_off,cand_env_tail_40ms,cand_decay_slope_per_ms,cand_release_slope_per_ms,cand_tail_state");
		foreach (var row in rows)
		{
			writer.WriteLine(row.ToCsvLine());
		}
	}

	private static void WriteFilterBandCsv(string path, IReadOnlyList<FilterBandDiagnosticRow> rows)
	{
		using var writer = new StreamWriter(path);
		writer.WriteLine("id,category,name,segment_index,segment,start_ms,end_ms,ref_low,ref_mid,ref_high,cand_player_low,cand_player_mid,cand_player_high,cand_raw_low,cand_raw_mid,cand_raw_high,player_low_ratio,player_mid_ratio,player_high_ratio,raw_low_ratio,raw_mid_ratio,raw_high_ratio");
		foreach (var row in rows)
		{
			writer.WriteLine(row.ToCsvLine());
		}
	}

	private static void WriteWaveformEdgeCsv(string path, IReadOnlyList<WaveformEdgeDiagnosticRow> rows)
	{
		using var writer = new StreamWriter(path);
		writer.WriteLine("id,category,name,segment_index,segment,start_ms,end_ms,ref_ac,cand_player_ac,player_ratio,player_corr,ref_flatness,cand_player_flatness,cand_raw_flatness,ref_noise_band,cand_player_noise_band,cand_raw_noise_band,near_dc_expected,near_dc_limit,near_dc_ok");
		foreach (var row in rows)
		{
			writer.WriteLine(row.ToCsvLine());
		}
	}

	private sealed class SidConformanceDiagnostics
	{
		public List<AdsrTraceDiagnosticRow> AdsrTraceRows { get; } = new();

		public List<AdsrPulseDiagnosticRow> AdsrPulseRows { get; } = new();

		public List<FilterBandDiagnosticRow> FilterRows { get; } = new();

		public List<WaveformEdgeDiagnosticRow> WaveformRows { get; } = new();

		public bool HasRows => AdsrTraceRows.Count > 0 || AdsrPulseRows.Count > 0 || FilterRows.Count > 0 || WaveformRows.Count > 0;
	}

	private sealed record CopperModDiagnosticRender(
		float[] Samples,
		float[][]? ChannelSamples,
		SidRegisterWrite[] SidWrites);

	private readonly record struct BandEnergies(double Low, double Mid, double High);

	private readonly record struct GatePulse(int OnFrame, int OffFrame);

	private readonly record struct AdsrDebugRow(
		double TimeSeconds,
		int Frame,
		bool Gate,
		int EnvelopeCounter,
		int EnvelopeState,
		int RateCounter,
		int ExponentialCounter,
		byte Control);

	private sealed record AdsrTraceDiagnosticRow(
		string Id,
		int Category,
		string Name,
		double TimeMs,
		int Frame,
		bool Gate,
		int AlignmentOffsetSamples,
		double RefAc,
		double CandPlayerAc,
		double PlayerRatio,
		double CandRawAc,
		double CandVoice0Ac,
		double FinalToVoice0Ac,
		double RefFundamental,
		double CandPlayerFundamental,
		double FundamentalRatio,
		double CandVoice0Fundamental,
		double FinalToVoice0Fundamental,
		int EnvelopeCounter,
		double EnvelopeDac,
		int EnvelopeState,
		int RateCounter,
		int ExponentialCounter,
		byte Control)
	{
		public string ToCsvLine()
			=> string.Join(
				',',
				Id,
				Category.ToString(CultureInfo.InvariantCulture),
				EscapeCsv(Name),
				TimeMs.ToString("0.000", CultureInfo.InvariantCulture),
				Frame.ToString(CultureInfo.InvariantCulture),
				Gate ? "1" : "0",
				AlignmentOffsetSamples.ToString(CultureInfo.InvariantCulture),
				CsvDouble(RefAc),
				CsvDouble(CandPlayerAc),
				CsvDouble(PlayerRatio),
				CsvDouble(CandRawAc),
				CsvDouble(CandVoice0Ac),
				CsvDouble(FinalToVoice0Ac),
				CsvDouble(RefFundamental),
				CsvDouble(CandPlayerFundamental),
				CsvDouble(FundamentalRatio),
				CsvDouble(CandVoice0Fundamental),
				CsvDouble(FinalToVoice0Fundamental),
				EnvelopeCounter.ToString(CultureInfo.InvariantCulture),
				CsvDouble(EnvelopeDac),
				FormatEnvelopeState(EnvelopeState),
				RateCounter.ToString(CultureInfo.InvariantCulture),
				ExponentialCounter.ToString(CultureInfo.InvariantCulture),
				"0x" + Control.ToString("X2", CultureInfo.InvariantCulture));
	}

	private sealed record AdsrPulseDiagnosticRow(
		string Id,
		int Category,
		string Name,
		int Pulse,
		double OnMs,
		double OffMs,
		double RefPeak,
		double CandPeak,
		double PeakRatio,
		double CandVoice0Peak,
		double FinalToVoice0Peak,
		double RefGateOff,
		double CandGateOff,
		double GateOffRatio,
		double CandVoice0GateOff,
		double FinalToVoice0GateOff,
		double RefTail40Ms,
		double CandTail40Ms,
		double Tail40MsRatio,
		double CandVoice0Tail40Ms,
		double FinalToVoice0Tail40Ms,
		int CandEnvelopePeak,
		int CandEnvelopeGateOff,
		int CandEnvelopeTail40Ms,
		double CandDecaySlopePerMs,
		double CandReleaseSlopePerMs,
		string CandTailState)
	{
		public string ToCsvLine()
			=> string.Join(
				',',
				Id,
				Category.ToString(CultureInfo.InvariantCulture),
				EscapeCsv(Name),
				Pulse.ToString(CultureInfo.InvariantCulture),
				OnMs.ToString("0.000", CultureInfo.InvariantCulture),
				OffMs.ToString("0.000", CultureInfo.InvariantCulture),
				CsvDouble(RefPeak),
				CsvDouble(CandPeak),
				CsvDouble(PeakRatio),
				CsvDouble(CandVoice0Peak),
				CsvDouble(FinalToVoice0Peak),
				CsvDouble(RefGateOff),
				CsvDouble(CandGateOff),
				CsvDouble(GateOffRatio),
				CsvDouble(CandVoice0GateOff),
				CsvDouble(FinalToVoice0GateOff),
				CsvDouble(RefTail40Ms),
				CsvDouble(CandTail40Ms),
				CsvDouble(Tail40MsRatio),
				CsvDouble(CandVoice0Tail40Ms),
				CsvDouble(FinalToVoice0Tail40Ms),
				CandEnvelopePeak.ToString(CultureInfo.InvariantCulture),
				CandEnvelopeGateOff.ToString(CultureInfo.InvariantCulture),
				CandEnvelopeTail40Ms.ToString(CultureInfo.InvariantCulture),
				CsvDouble(CandDecaySlopePerMs),
				CsvDouble(CandReleaseSlopePerMs),
				CandTailState);
	}

	private sealed record FilterBandDiagnosticRow(
		string Id,
		int Category,
		string Name,
		int SegmentIndex,
		string Segment,
		double StartMs,
		double EndMs,
		double RefLow,
		double RefMid,
		double RefHigh,
		double CandPlayerLow,
		double CandPlayerMid,
		double CandPlayerHigh,
		double CandRawLow,
		double CandRawMid,
		double CandRawHigh)
	{
		public double PlayerLowRatio => CandPlayerLow / Math.Max(1.0e-12, RefLow);

		public double PlayerMidRatio => CandPlayerMid / Math.Max(1.0e-12, RefMid);

		public double PlayerHighRatio => CandPlayerHigh / Math.Max(1.0e-12, RefHigh);

		public double RawLowRatio => CandRawLow / Math.Max(1.0e-12, RefLow);

		public double RawMidRatio => CandRawMid / Math.Max(1.0e-12, RefMid);

		public double RawHighRatio => CandRawHigh / Math.Max(1.0e-12, RefHigh);

		public double Score =>
			Math.Abs(Math.Log(Math.Max(1.0e-12, PlayerLowRatio), 2.0)) +
			Math.Abs(Math.Log(Math.Max(1.0e-12, PlayerMidRatio), 2.0)) +
			Math.Abs(Math.Log(Math.Max(1.0e-12, PlayerHighRatio), 2.0));

		public string ToCsvLine()
			=> string.Join(
				',',
				Id,
				Category.ToString(CultureInfo.InvariantCulture),
				EscapeCsv(Name),
				SegmentIndex.ToString(CultureInfo.InvariantCulture),
				EscapeCsv(Segment),
				StartMs.ToString("0.000", CultureInfo.InvariantCulture),
				EndMs.ToString("0.000", CultureInfo.InvariantCulture),
				CsvDouble(RefLow),
				CsvDouble(RefMid),
				CsvDouble(RefHigh),
				CsvDouble(CandPlayerLow),
				CsvDouble(CandPlayerMid),
				CsvDouble(CandPlayerHigh),
				CsvDouble(CandRawLow),
				CsvDouble(CandRawMid),
				CsvDouble(CandRawHigh),
				CsvDouble(PlayerLowRatio),
				CsvDouble(PlayerMidRatio),
				CsvDouble(PlayerHighRatio),
				CsvDouble(RawLowRatio),
				CsvDouble(RawMidRatio),
				CsvDouble(RawHighRatio));
	}

	private sealed record WaveformEdgeDiagnosticRow(
		string Id,
		int Category,
		string Name,
		int SegmentIndex,
		string Segment,
		double StartMs,
		double EndMs,
		double RefAc,
		double CandPlayerAc,
		double PlayerRatio,
		double PlayerCorrelation,
		double RefFlatness,
		double CandPlayerFlatness,
		double CandRawFlatness,
		double RefNoiseBand,
		double CandPlayerNoiseBand,
		double CandRawNoiseBand,
		bool NearDcExpected,
		double NearDcLimit,
		bool NearDcOk)
	{
		public double Score =>
			Math.Abs(Math.Log(Math.Max(1.0e-12, PlayerRatio), 2.0)) +
			Math.Max(0.0, 0.75 - PlayerCorrelation) +
			(NearDcExpected && !NearDcOk ? 4.0 : 0.0);

		public string ToCsvLine()
			=> string.Join(
				',',
				Id,
				Category.ToString(CultureInfo.InvariantCulture),
				EscapeCsv(Name),
				SegmentIndex.ToString(CultureInfo.InvariantCulture),
				EscapeCsv(Segment),
				StartMs.ToString("0.000", CultureInfo.InvariantCulture),
				EndMs.ToString("0.000", CultureInfo.InvariantCulture),
				CsvDouble(RefAc),
				CsvDouble(CandPlayerAc),
				CsvDouble(PlayerRatio),
				PlayerCorrelation.ToString("0.000000", CultureInfo.InvariantCulture),
				CsvDouble(RefFlatness),
				CsvDouble(CandPlayerFlatness),
				CsvDouble(CandRawFlatness),
				CsvDouble(RefNoiseBand),
				CsvDouble(CandPlayerNoiseBand),
				CsvDouble(CandRawNoiseBand),
				NearDcExpected ? "1" : "0",
				CsvDouble(NearDcLimit),
				NearDcOk ? "1" : "0");
	}
}
