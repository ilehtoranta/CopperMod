using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace CopperMod.Sid.Tests;

public sealed class PexD418ReplayCalibrationTests
{
	private const int SampleRate = 96000;
	private const int FirstFromSampleIndex = 2000;
	private const int PexZeroToFromCycles = 247;
	private const int PexFromToCycles = 127;
	private const int PexToZeroCycles = 136;
	private const int PexTransitionCycles = PexZeroToFromCycles + PexFromToCycles + PexToZeroCycles;
	private const int PexSampleOffset = 13;
	private const int PexZeroRightSampleOffset = 26;
	private const int MinPhaseOffset = -8;
	private const int MaxPhaseOffset = 8;
	private const int StageCount = (int)CalibrationStage.Count;
	private const ushort SidBase = SidConstants.DefaultSidBaseAddress;
	private static readonly object ReportFileLock = new();

	private static readonly CalibrationStage[] ContextFitStages =
	{
		CalibrationStage.D418MatrixImpulse,
		CalibrationStage.D418GenericStepImpulse,
		CalibrationStage.FilterModeImpulse,
		CalibrationStage.VolumeTransientTarget,
		CalibrationStage.VolumeTransientCurrent,
		CalibrationStage.VolumeOffset,
		CalibrationStage.PreSoftClipSample,
		CalibrationStage.PostSoftClipSample,
		CalibrationStage.AnalogOutputVoltage,
		CalibrationStage.AnalogLowPassVoltage,
		CalibrationStage.FinalSample
	};

	private static readonly (int Previous, int Next)[] RepresentativeTransitions =
	{
		(0x00, 0x00),
		(0x00, 0x0F),
		(0x0F, 0x00),
		(0x00, 0x9F),
		(0x9F, 0x0F),
		(0xFF, 0x00),
		(0x00, 0x80),
		(0x80, 0x0F),
		(0x1F, 0x0F),
		(0x20, 0x2F),
		(0x60, 0x2F),
		(0x40, 0x4F),
		(0xC0, 0x4F),
		(0x70, 0x7F),
		(0xF0, 0x7F),
		(0x90, 0x9F),
		(0x10, 0x9F),
		(0x7F, 0xFF),
		(0xFF, 0x7F),
		(0x55, 0xAA),
		(0xAA, 0x55)
	};

	private static readonly ContextDelta[] ContextDeltas =
	{
		new(0x00, 0x9F, 0x0F),
		new(0x0F, 0xFF, 0x00),
		new(0x00, 0x80, 0x0F),
		new(0x80, 0x1F, 0x0F),
		new(0x20, 0x60, 0x2F),
		new(0x40, 0xC0, 0x4F),
		new(0x70, 0xF0, 0x7F),
		new(0x90, 0x10, 0x9F)
	};

	private readonly ITestOutputHelper _output;

	public PexD418ReplayCalibrationTests(ITestOutputHelper output)
	{
		_output = output;
	}

	[Theory]
	[InlineData((int)SidChipModel.Mos6581)]
	[InlineData((int)SidChipModel.Mos8580)]
	public void OptionalContinuousPexReplayCalibrationProducesPhasePolarityFitReport(int modelValue)
	{
		if (!ContinuousCalibrationEnabled())
		{
			return;
		}

		var model = (SidChipModel)modelValue;
		var transitionIndices = BuildTransitionSet();
		var reference = BuildCalibrationReport(model, SidEmulationProfile.ReferenceMeasured, transitionIndices);
		var balanced = BuildCalibrationReport(model, SidEmulationProfile.Balanced, transitionIndices);

		WriteReport(reference);
		WriteReport(balanced);
		if (CalibrationSweepEnabled())
		{
			WriteSweepReport(model, transitionIndices, reference);
		}

		AssertFinite(reference);
		AssertFinite(balanced);
		AssertReferenceMeasuredStageFits(reference);
		Assert.True(
			Math.Abs(reference.ContextFit.Correlation) > 0.50,
			$"Expected {model} reference final context fit to retain useful correlation: {reference.ContextFit}.");
		Assert.True(
			reference.ContextFit.NormalizedRootMeanSquareError < 0.90,
			$"Expected {model} reference final context fit to stay within calibrated error bounds: {reference.ContextFit}.");
		Assert.True(
			reference.ContextFit.NormalizedRootMeanSquareError < balanced.ContextFit.NormalizedRootMeanSquareError * 0.95,
			$"Expected {model} reference final context fit to beat balanced. Reference {reference.ContextFit}; balanced {balanced.ContextFit}.");
	}

	private CalibrationReport BuildCalibrationReport(
		SidChipModel model,
		SidEmulationProfile sidEmulationProfile,
		int[] transitionIndices,
		SidAnalogReferenceCalibration? calibration = null)
	{
		IDisposable? calibrationScope = null;
		try
		{
			if (calibration != null)
			{
				calibrationScope = SidAnalog.PushReferenceCalibration(calibration);
			}

			var replay = ReplayContinuousPexTransitions(
				model,
				sidEmulationProfile,
				transitionIndices,
				MinPhaseOffset,
				MaxPhaseOffset);
			var bestPostFit = FindBestPostWriteFit(model, replay, transitionIndices);
			var bestContextFit = FindBestContextFit(model, replay, CalibrationStage.FinalSample);
			var stageFits = FindBestStageContextFits(model, replay);
			return new CalibrationReport(model, sidEmulationProfile, bestPostFit, bestContextFit, stageFits);
		}
		finally
		{
			calibrationScope?.Dispose();
		}
	}

	private static void AssertFinite(CalibrationReport report)
	{
		AssertFinite(report, "post-write", report.PostWriteFit);
		AssertFinite(report, "context", report.ContextFit);
		for (var i = 0; i < report.StageContextFits.Length; i++)
		{
			AssertFinite(report, "stage context", report.StageContextFits[i]);
		}
	}

	private static void AssertFinite(CalibrationReport report, string name, CalibrationFit fit)
	{
		Assert.True(double.IsFinite(fit.Scale), $"{report.Model} {report.SidEmulationProfile} {name} invalid scale: {fit}.");
		Assert.True(double.IsFinite(fit.Bias), $"{report.Model} {report.SidEmulationProfile} {name} invalid bias: {fit}.");
		Assert.True(double.IsFinite(fit.Correlation), $"{report.Model} {report.SidEmulationProfile} {name} invalid correlation: {fit}.");
		Assert.True(double.IsFinite(fit.RootMeanSquareError), $"{report.Model} {report.SidEmulationProfile} {name} invalid RMSE: {fit}.");
		Assert.True(double.IsFinite(fit.NormalizedRootMeanSquareError), $"{report.Model} {report.SidEmulationProfile} {name} invalid normalized RMSE: {fit}.");
		Assert.True(double.IsFinite(fit.MaxAbsoluteError), $"{report.Model} {report.SidEmulationProfile} {name} invalid max error: {fit}.");
	}

	private static void AssertReferenceMeasuredStageFits(CalibrationReport report)
	{
		Assert.Equal(SidEmulationProfile.ReferenceMeasured, report.SidEmulationProfile);
		var matrix = FindStageContextFit(report, "matrix-impulse");
		Assert.True(
			Math.Abs(matrix.Correlation) > 0.999999,
			$"Expected {report.Model} measured matrix stage to match the Pex oracle correlation: {matrix}.");
		Assert.True(
			matrix.NormalizedRootMeanSquareError < 0.000001,
			$"Expected {report.Model} measured matrix stage to fit the Pex oracle exactly: {matrix}.");
		Assert.True(
			matrix.MaxAbsoluteError < 0.000001,
			$"Expected {report.Model} measured matrix stage to have no representative transition error: {matrix}.");

		if (report.Model != SidChipModel.Mos6581)
		{
			return;
		}

		var transient = FindStageContextFit(report, "transient-current");
		Assert.True(
			Math.Abs(transient.Correlation) > 0.60,
			$"Expected 6581 measured transient stage to keep useful Pex context correlation: {transient}.");
		Assert.True(
			transient.NormalizedRootMeanSquareError < 0.80,
			$"Expected 6581 measured transient stage to stay within calibrated Pex context error bounds: {transient}.");
	}

	private static CalibrationFit FindStageContextFit(CalibrationReport report, string name)
	{
		for (var i = 0; i < report.StageContextFits.Length; i++)
		{
			if (string.Equals(report.StageContextFits[i].Name, name, StringComparison.Ordinal))
			{
				return report.StageContextFits[i];
			}
		}

		throw new InvalidOperationException($"Calibration stage '{name}' was not reported for {report.Model} {report.SidEmulationProfile}.");
	}

	private void WriteReport(CalibrationReport report)
		=> WriteText(report.ToString());

	private void WriteSweepReport(
		SidChipModel model,
		int[] transitionIndices,
		CalibrationReport currentReport)
	{
		var candidates = BuildSweepCandidates(model);
		var results = new List<CalibrationSweepResult>(candidates.Length + 1)
		{
			CalibrationSweepResult.FromReport("current", currentReport)
		};

		for (var i = 0; i < candidates.Length; i++)
		{
			var candidate = candidates[i];
			var report = BuildCalibrationReport(
				model,
				SidEmulationProfile.ReferenceMeasured,
				transitionIndices,
				candidate.Calibration);
			results.Add(CalibrationSweepResult.FromReport(candidate.Name, report));
		}

		results.Sort((left, right) => CompareSweepResults(left, right));
		WriteText(FormatSweepReport(model, results));
	}

	private static CalibrationCandidate[] BuildSweepCandidates(SidChipModel model)
	{
		if (model == SidChipModel.Mos8580)
		{
			return new[]
			{
				new CalibrationCandidate(
					"lp18k",
					new SidAnalogReferenceCalibration { Mos8580OutputLowPassCutoffHz = 18_000.0 }),
				new CalibrationCandidate(
					"lp22k",
					new SidAnalogReferenceCalibration { Mos8580OutputLowPassCutoffHz = 22_000.0 }),
				new CalibrationCandidate(
					"atk1.5",
					new SidAnalogReferenceCalibration { Mos8580TransientAttackScale = 1.50 }),
				new CalibrationCandidate(
					"dec1.25",
					new SidAnalogReferenceCalibration { Mos8580TransientDecayScale = 1.25 }),
				new CalibrationCandidate(
					"xition0.85",
					new SidAnalogReferenceCalibration { Mos8580TransitionScale = 0.85 })
			};
		}

		return new[]
		{
			new CalibrationCandidate(
				"lp20k",
				new SidAnalogReferenceCalibration { Mos6581OutputLowPassCutoffHz = 20_000.0 }),
			new CalibrationCandidate(
				"lp28k",
				new SidAnalogReferenceCalibration { Mos6581OutputLowPassCutoffHz = 28_000.0 }),
			new CalibrationCandidate(
				"atk1.5",
				new SidAnalogReferenceCalibration { Mos6581TransientAttackScale = 1.50 }),
			new CalibrationCandidate(
				"dec1.25",
				new SidAnalogReferenceCalibration { Mos6581TransientDecayScale = 1.25 }),
			new CalibrationCandidate(
				"atk1.5-dec1.25",
				new SidAnalogReferenceCalibration { Mos6581TransientAttackScale = 1.50, Mos6581TransientDecayScale = 1.25 }),
			new CalibrationCandidate(
				"xition0.85",
				new SidAnalogReferenceCalibration { Mos6581TransitionScale = 0.85 })
		};
	}

	private static int CompareSweepResults(CalibrationSweepResult left, CalibrationSweepResult right)
	{
		var errorDelta = left.ContextFit.NormalizedRootMeanSquareError - right.ContextFit.NormalizedRootMeanSquareError;
		if (Math.Abs(errorDelta) > 0.000001)
		{
			return errorDelta < 0.0 ? -1 : 1;
		}

		var correlationDelta = Math.Abs(right.ContextFit.Correlation) - Math.Abs(left.ContextFit.Correlation);
		if (Math.Abs(correlationDelta) > 0.000001)
		{
			return correlationDelta < 0.0 ? -1 : 1;
		}

		return string.CompareOrdinal(left.Name, right.Name);
	}

	private static string FormatSweepReport(SidChipModel model, List<CalibrationSweepResult> results)
	{
		var builder = new StringBuilder();
		builder.AppendFormat(CultureInfo.InvariantCulture, "{0} ReferenceMeasured sweep", model);
		builder.AppendLine();
		builder.AppendLine("  candidate          phase polarity    corr   nrmse        max  transient  postclip");
		for (var i = 0; i < results.Count; i++)
		{
			var result = results[i];
			builder.AppendFormat(
				CultureInfo.InvariantCulture,
				"  {0,-16} {1,5:+0;-0;0} {2,-8} {3,7:0.000} {4,7:0.000} {5,10:0.000000} {6,10:0.000} {7,9:0.000}",
				result.Name,
				result.ContextFit.PhaseOffsetSamples,
				result.ContextFit.Scale < 0.0 ? "inv" : "norm",
				result.ContextFit.Correlation,
				result.ContextFit.NormalizedRootMeanSquareError,
				result.ContextFit.MaxAbsoluteError,
				result.TransientCurrentFit.NormalizedRootMeanSquareError,
				result.PostSoftClipFit.NormalizedRootMeanSquareError);
			builder.AppendLine();
		}

		return builder.ToString();
	}

	private void WriteText(string text)
	{
		_output.WriteLine(text);
		var path = Environment.GetEnvironmentVariable("SID_D418_REPLAY_CALIBRATION_REPORT");
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}

		var fullPath = Path.GetFullPath(path);
		var directory = Path.GetDirectoryName(fullPath);
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}

		lock (ReportFileLock)
		{
			File.AppendAllText(fullPath, text + Environment.NewLine + Environment.NewLine, Encoding.UTF8);
		}
	}

	private static int[] BuildTransitionSet()
	{
		var set = new SortedSet<int>();
		for (var i = 0; i < RepresentativeTransitions.Length; i++)
		{
			set.Add((RepresentativeTransitions[i].Previous << 8) | RepresentativeTransitions[i].Next);
		}

		for (var i = 0; i < ContextDeltas.Length; i++)
		{
			set.Add((ContextDeltas[i].PreviousA << 8) | ContextDeltas[i].Next);
			set.Add((ContextDeltas[i].PreviousB << 8) | ContextDeltas[i].Next);
		}

		var values = new int[set.Count];
		set.CopyTo(values);
		return values;
	}

	private static Dictionary<int, StageReplayMeasurement[]> ReplayContinuousPexTransitions(
		SidChipModel model,
		SidEmulationProfile sidEmulationProfile,
		int[] transitionIndices,
		int minPhaseOffset,
		int maxPhaseOffset)
	{
		var maxTransitionIndex = transitionIndices[^1];
		var sid = new SidSystem(
			new[] { new SidChipPlacement(0, SidBase) },
			model,
			SidConstants.PalCpuCyclesPerSecond,
			SidFilterProfileId.Auto,
			sidEmulationProfile);
		var outputStageTrace = new SidOutputStageTrace();
		sid.Chips[0].OutputStageTrace = outputStageTrace;
		ApplyPexSidSetup(sid);

		var firstFromCycle = CycleForSample(FirstFromSampleIndex);
		ApplyPexPreamble(sid, firstFromCycle - PexZeroToFromCycles - 64);
		for (var transitionIndex = 0; transitionIndex <= maxTransitionIndex; transitionIndex++)
		{
			var from = transitionIndex >> 8;
			var to = transitionIndex & 0xFF;
			var fromCycle = firstFromCycle + ((long)transitionIndex * PexTransitionCycles);
			WriteD418(sid, 0x00, fromCycle - PexZeroToFromCycles);
			WriteD418(sid, from, fromCycle);
			WriteD418(sid, to, fromCycle + PexFromToCycles);
		}

		var finalFromCycle = firstFromCycle + ((long)maxTransitionIndex * PexTransitionCycles);
		WriteD418(sid, 0x00, finalFromCycle + PexFromToCycles + PexToZeroCycles);

		var result = new Dictionary<int, StageReplayMeasurement[]>(transitionIndices.Length);
		for (var i = 0; i < transitionIndices.Length; i++)
		{
			var transitionIndex = transitionIndices[i];
			result[transitionIndex] = MeasureContinuousTransitionPhases(
				sid,
				outputStageTrace,
				transitionIndex,
				minPhaseOffset,
				maxPhaseOffset);
		}

		return result;
	}

	private static StageReplayMeasurement[] MeasureContinuousTransitionPhases(
		SidSystem sid,
		SidOutputStageTrace outputStageTrace,
		int transitionIndex,
		int minPhaseOffset,
		int maxPhaseOffset)
	{
		var phaseCount = maxPhaseOffset - minPhaseOffset + 1;
		var centerIndex = FirstFromSampleIndex +
			(transitionIndex * PexTransitionCycles * (double)SampleRate / SidConstants.PalCpuCyclesPerSecond);
		var firstSampleIndex = RoundMatlab(centerIndex + minPhaseOffset - PexSampleOffset) - 1;
		var lastSampleIndex = RoundMatlab(centerIndex + maxPhaseOffset + PexZeroRightSampleOffset) + 1;
		var samples = new CapturedSample[lastSampleIndex - firstSampleIndex + 1];
		for (var sampleIndex = firstSampleIndex; sampleIndex <= lastSampleIndex; sampleIndex++)
		{
			samples[sampleIndex - firstSampleIndex] = RenderSingleSampleAt(sid, outputStageTrace, sampleIndex);
		}

		var result = new StageReplayMeasurement[phaseCount];
		for (var phaseOffset = minPhaseOffset; phaseOffset <= maxPhaseOffset; phaseOffset++)
		{
			var shiftedCenterIndex = centerIndex + phaseOffset;
			var zeroLeftIndex = RoundMatlab(shiftedCenterIndex - PexSampleOffset);
			var fromIndex = RoundMatlab(shiftedCenterIndex);
			var toIndex = RoundMatlab(shiftedCenterIndex + PexSampleOffset);
			var zeroRightIndex = RoundMatlab(shiftedCenterIndex + PexZeroRightSampleOffset);
			var measurement = new StageReplayMeasurement();
			for (var stageIndex = 0; stageIndex < StageCount; stageIndex++)
			{
				var stage = (CalibrationStage)stageIndex;
				var zeroLeft = AverageAt(samples, zeroLeftIndex, firstSampleIndex, stage);
				var preWrite = AverageAt(samples, fromIndex, firstSampleIndex, stage);
				var postWrite = AverageAt(samples, toIndex, firstSampleIndex, stage);
				var zeroRight = AverageAt(samples, zeroRightIndex, firstSampleIndex, stage);
				var preWriteZero = Interpolate(zeroLeft, zeroRight, zeroLeftIndex, zeroRightIndex, fromIndex);
				var postWriteZero = Interpolate(zeroLeft, zeroRight, zeroLeftIndex, zeroRightIndex, toIndex);
				measurement.PreWrite[stageIndex] = preWrite - preWriteZero;
				measurement.PostWrite[stageIndex] = postWrite - postWriteZero;
			}

			result[phaseOffset - minPhaseOffset] = measurement;
		}

		return result;
	}

	private static CalibrationFit FindBestPostWriteFit(
		SidChipModel model,
		Dictionary<int, StageReplayMeasurement[]> replay,
		int[] transitionIndices)
	{
		CalibrationFit? best = null;
		for (var phaseOffset = MinPhaseOffset; phaseOffset <= MaxPhaseOffset; phaseOffset++)
		{
			var phaseIndex = phaseOffset - MinPhaseOffset;
			var points = new FitPoint[transitionIndices.Length];
			for (var i = 0; i < transitionIndices.Length; i++)
			{
				var transitionIndex = transitionIndices[i];
				var previous = transitionIndex >> 8;
				var next = transitionIndex & 0xFF;
				points[i] = new FitPoint(
					$"{previous:X2}->{next:X2}",
					replay[transitionIndex][phaseIndex].PostWrite[(int)CalibrationStage.FinalSample],
					PexD418ReplayTests.TransitionPostWriteAmplitude(previous, next, model));
			}

			var fit = FitAffine("post-write", phaseOffset, points);
			best = IsBetterFit(fit, best) ? fit : best;
		}

		return best ?? throw new InvalidOperationException("No post-write calibration fit was produced.");
	}

	private static CalibrationFit FindBestContextFit(
		SidChipModel model,
		Dictionary<int, StageReplayMeasurement[]> replay,
		CalibrationStage stage)
	{
		CalibrationFit? best = null;
		var stageIndex = (int)stage;
		for (var phaseOffset = MinPhaseOffset; phaseOffset <= MaxPhaseOffset; phaseOffset++)
		{
			var phaseIndex = phaseOffset - MinPhaseOffset;
			var points = new FitPoint[ContextDeltas.Length];
			for (var i = 0; i < ContextDeltas.Length; i++)
			{
				var sample = ContextDeltas[i];
				var leftIndex = (sample.PreviousA << 8) | sample.Next;
				var rightIndex = (sample.PreviousB << 8) | sample.Next;
				points[i] = new FitPoint(
					$"{sample.PreviousA:X2}/{sample.PreviousB:X2}->{sample.Next:X2}",
					replay[leftIndex][phaseIndex].PostWrite[stageIndex] - replay[rightIndex][phaseIndex].PostWrite[stageIndex],
					PexD418ReplayTests.TransitionPostWriteAmplitude(sample.PreviousA, sample.Next, model) -
						PexD418ReplayTests.TransitionPostWriteAmplitude(sample.PreviousB, sample.Next, model));
			}

			var fit = FitAffine(StageLabel(stage), phaseOffset, points);
			best = IsBetterFit(fit, best) ? fit : best;
		}

		return best ?? throw new InvalidOperationException("No context calibration fit was produced.");
	}

	private static CalibrationFit[] FindBestStageContextFits(
		SidChipModel model,
		Dictionary<int, StageReplayMeasurement[]> replay)
	{
		var fits = new CalibrationFit[ContextFitStages.Length];
		for (var i = 0; i < ContextFitStages.Length; i++)
		{
			fits[i] = FindBestContextFit(model, replay, ContextFitStages[i]);
		}

		return fits;
	}

	private static bool IsBetterFit(CalibrationFit fit, CalibrationFit? currentBest)
	{
		if (currentBest == null)
		{
			return true;
		}

		var errorDelta = fit.NormalizedRootMeanSquareError - currentBest.Value.NormalizedRootMeanSquareError;
		if (Math.Abs(errorDelta) > 0.000001)
		{
			return errorDelta < 0.0;
		}

		return Math.Abs(fit.Correlation) > Math.Abs(currentBest.Value.Correlation);
	}

	private static CalibrationFit FitAffine(string name, int phaseOffset, FitPoint[] points)
	{
		var observedMean = 0.0;
		var expectedMean = 0.0;
		for (var i = 0; i < points.Length; i++)
		{
			observedMean += points[i].Observed;
			expectedMean += points[i].Expected;
		}

		observedMean /= points.Length;
		expectedMean /= points.Length;

		var covariance = 0.0;
		var observedVariance = 0.0;
		var expectedVariance = 0.0;
		for (var i = 0; i < points.Length; i++)
		{
			var observedDelta = points[i].Observed - observedMean;
			var expectedDelta = points[i].Expected - expectedMean;
			covariance += observedDelta * expectedDelta;
			observedVariance += observedDelta * observedDelta;
			expectedVariance += expectedDelta * expectedDelta;
		}

		var scale = observedVariance <= 0.0 ? 0.0 : covariance / observedVariance;
		var bias = expectedMean - (scale * observedMean);
		var correlation = observedVariance <= 0.0 || expectedVariance <= 0.0
			? 0.0
			: covariance / Math.Sqrt(observedVariance * expectedVariance);
		var sumSquaredError = 0.0;
		var errors = new FitError[points.Length];
		for (var i = 0; i < points.Length; i++)
		{
			var fitted = (points[i].Observed * scale) + bias;
			var error = fitted - points[i].Expected;
			sumSquaredError += error * error;
			errors[i] = new FitError(points[i].Label, error, points[i].Observed, fitted, points[i].Expected);
		}

		Array.Sort(errors, (left, right) => Math.Abs(right.Error).CompareTo(Math.Abs(left.Error)));
		var rootMeanSquareError = Math.Sqrt(sumSquaredError / points.Length);
		var expectedSpread = Math.Sqrt(expectedVariance / points.Length);
		var normalizedRootMeanSquareError = expectedSpread <= 0.0
			? rootMeanSquareError
			: rootMeanSquareError / expectedSpread;
		return new CalibrationFit(
			name,
			phaseOffset,
			scale,
			bias,
			correlation,
			rootMeanSquareError,
			normalizedRootMeanSquareError,
			Math.Abs(errors[0].Error),
			DescribeWorst(errors));
	}

	private static string DescribeWorst(FitError[] errors)
	{
		var count = Math.Min(3, errors.Length);
		var parts = new string[count];
		for (var i = 0; i < count; i++)
		{
			parts[i] = string.Format(
				CultureInfo.InvariantCulture,
				"{0}: err {1:0.000000}, obs {2:0.000000}, fit {3:0.000000}, exp {4:0.000000}",
				errors[i].Label,
				errors[i].Error,
				errors[i].Observed,
				errors[i].Fitted,
				errors[i].Expected);
		}

		return string.Join("; ", parts);
	}

	private static bool ContinuousCalibrationEnabled()
		=> string.Equals(
			Environment.GetEnvironmentVariable("SID_D418_REPLAY_CALIBRATION"),
			"1",
			StringComparison.Ordinal);

	private static bool CalibrationSweepEnabled()
		=> string.Equals(
			Environment.GetEnvironmentVariable("SID_D418_REPLAY_SWEEP"),
			"1",
			StringComparison.Ordinal);

	private static CapturedSample RenderSingleSampleAt(
		SidSystem sid,
		SidOutputStageTrace outputStageTrace,
		int sampleIndex)
	{
		sid.AdvanceTo(CycleForSample(sampleIndex - 1));
		sid.DiscardAccumulatedOutput();
		outputStageTrace.BeginFrame();
		var sample = sid.RenderSample(CycleForSample(sampleIndex));
		return new CapturedSample(sample, outputStageTrace.EndFrame());
	}

	private static void ApplyPexSidSetup(SidSystem sid)
	{
		for (var register = 0; register <= 0x18; register++)
		{
			WriteSid(sid, register, 0x00, register);
		}

		WriteSid(sid, 0x04, 0x49, 0x20);
		WriteSid(sid, 0x0B, 0x49, 0x21);
		WriteSid(sid, 0x12, 0x49, 0x22);
		WriteSid(sid, 0x06, 0xFF, 0x23);
		WriteSid(sid, 0x0D, 0xFF, 0x24);
		WriteSid(sid, 0x14, 0xFF, 0x25);
		WriteSid(sid, 0x15, 0xFF, 0x26);
		WriteSid(sid, 0x16, 0xFF, 0x27);
		WriteSid(sid, 0x17, 0x03, 0x28);
		WriteSid(sid, 0x18, 0x00, 0x29);
	}

	private static void ApplyPexPreamble(SidSystem sid, long cycle)
	{
		WriteD418(sid, 0x0F, cycle);
		WriteD418(sid, 0x00, cycle + 4);
		WriteD418(sid, 0x8F, cycle + 8);
		WriteD418(sid, 0x00, cycle + 12);
		WriteD418(sid, 0x9F, cycle + 16);
		WriteD418(sid, 0x00, cycle + 20);
		WriteD418(sid, 0x0F, cycle + 24);
	}

	private static void WriteD418(SidSystem sid, int value, long cycle)
		=> WriteSid(sid, 0x18, value, cycle);

	private static void WriteSid(SidSystem sid, int register, int value, long cycle)
		=> Assert.True(sid.TryWrite((ushort)(SidBase + register), (byte)value, cycle));

	private static long CycleForSample(int sampleIndex)
		=> SidIntegerMath.MulDivRoundNearest(sampleIndex, SidConstants.PalCpuCyclesPerSecond, SampleRate);

	private static int RoundMatlab(double value)
		=> (int)Math.Floor(value + 0.5);

	private static double AverageAt(
		CapturedSample[] samples,
		int sampleIndex,
		int firstSampleIndex,
		CalibrationStage stage)
	{
		var offset = sampleIndex - firstSampleIndex;
		return (GetStageValue(samples[offset - 1], stage) +
			GetStageValue(samples[offset], stage) +
			GetStageValue(samples[offset + 1], stage)) / 3.0;
	}

	private static double GetStageValue(CapturedSample sample, CalibrationStage stage)
	{
		var frame = sample.Frame;
		return stage switch
		{
			CalibrationStage.FinalSample => frame.FinalSample,
			CalibrationStage.D418MatrixImpulse => frame.D418MatrixImpulse,
			CalibrationStage.D418GenericStepImpulse => frame.D418GenericStepImpulse,
			CalibrationStage.FilterModeImpulse => frame.FilterModeImpulse,
			CalibrationStage.VoiceSignal => frame.VoiceSignal,
			CalibrationStage.VolumeOffset => frame.VolumeOffset,
			CalibrationStage.VolumeTransientTarget => frame.VolumeTransientTarget,
			CalibrationStage.VolumeTransientCurrent => frame.VolumeTransientCurrent,
			CalibrationStage.PreSoftClipSample => frame.PreSoftClipSample,
			CalibrationStage.PostSoftClipSample => frame.PostSoftClipSample,
			CalibrationStage.AnalogMixedVoltage => frame.AnalogMixedVoltage,
			CalibrationStage.AnalogOutputVoltage => frame.AnalogOutputVoltage,
			CalibrationStage.AnalogLowPassVoltage => frame.AnalogLowPassVoltage,
			_ => sample.Output
		};
	}

	private static string StageLabel(CalibrationStage stage)
		=> stage switch
		{
			CalibrationStage.FinalSample => "final",
			CalibrationStage.D418MatrixImpulse => "matrix-impulse",
			CalibrationStage.D418GenericStepImpulse => "generic-step",
			CalibrationStage.FilterModeImpulse => "filter-mode",
			CalibrationStage.VoiceSignal => "voice-signal",
			CalibrationStage.VolumeOffset => "volume-offset",
			CalibrationStage.VolumeTransientTarget => "transient-target",
			CalibrationStage.VolumeTransientCurrent => "transient-current",
			CalibrationStage.PreSoftClipSample => "pre-softclip",
			CalibrationStage.PostSoftClipSample => "post-softclip",
			CalibrationStage.AnalogMixedVoltage => "analog-mixed-v",
			CalibrationStage.AnalogOutputVoltage => "analog-output-v",
			CalibrationStage.AnalogLowPassVoltage => "analog-lowpass-v",
			_ => stage.ToString()
		};

	private static double Interpolate(
		double left,
		double right,
		int leftIndex,
		int rightIndex,
		int sampleIndex)
	{
		var fraction = (double)(sampleIndex - leftIndex) / (rightIndex - leftIndex);
		return left + ((right - left) * fraction);
	}

	private enum CalibrationStage
	{
		FinalSample = 0,
		D418MatrixImpulse,
		D418GenericStepImpulse,
		FilterModeImpulse,
		VoiceSignal,
		VolumeOffset,
		VolumeTransientTarget,
		VolumeTransientCurrent,
		PreSoftClipSample,
		PostSoftClipSample,
		AnalogMixedVoltage,
		AnalogOutputVoltage,
		AnalogLowPassVoltage,
		Count
	}

	private sealed class StageReplayMeasurement
	{
		public double[] PreWrite { get; } = new double[StageCount];

		public double[] PostWrite { get; } = new double[StageCount];
	}

	private readonly record struct CapturedSample(double Output, SidOutputStageFrame Frame);

	private readonly record struct ContextDelta(int PreviousA, int PreviousB, int Next);

	private readonly record struct FitPoint(string Label, double Observed, double Expected);

	private readonly record struct FitError(
		string Label,
		double Error,
		double Observed,
		double Fitted,
		double Expected);

	private readonly record struct CalibrationCandidate(
		string Name,
		SidAnalogReferenceCalibration Calibration);

	private readonly record struct CalibrationSweepResult(
		string Name,
		CalibrationFit ContextFit,
		CalibrationFit TransientCurrentFit,
		CalibrationFit PostSoftClipFit)
	{
		public static CalibrationSweepResult FromReport(string name, CalibrationReport report)
			=> new CalibrationSweepResult(
				name,
				report.ContextFit,
				FindStageContextFit(report, "transient-current"),
				FindStageContextFit(report, "post-softclip"));
	}

	private readonly record struct CalibrationReport(
		SidChipModel Model,
		SidEmulationProfile SidEmulationProfile,
		CalibrationFit PostWriteFit,
		CalibrationFit ContextFit,
		CalibrationFit[] StageContextFits)
	{
		public override string ToString()
		{
			var builder = new StringBuilder();
			builder.AppendFormat(
				CultureInfo.InvariantCulture,
				"{0} {1}",
				Model,
				SidEmulationProfile);
			builder.AppendLine();
			builder.AppendLine("  summary:");
			builder.AppendLine("    stage                 phase polarity    corr   nrmse        max");
			AppendSummaryRow(builder, "post-write", PostWriteFit);
			AppendSummaryRow(builder, "context-final", ContextFit);
			for (var i = 0; i < StageContextFits.Length; i++)
			{
				AppendSummaryRow(builder, StageContextFits[i].Name, StageContextFits[i]);
			}

			builder.AppendLine("  details:");
			builder.Append("    ");
			builder.AppendLine(PostWriteFit.ToString());
			builder.Append("    context: ");
			builder.AppendLine(ContextFit.ToString());
			builder.AppendLine("    stage context:");
			for (var i = 0; i < StageContextFits.Length; i++)
			{
				builder.Append("      ");
				builder.Append(StageContextFits[i]);
				if (i < StageContextFits.Length - 1)
				{
					builder.AppendLine();
				}
			}

			return builder.ToString();
		}

		private static void AppendSummaryRow(StringBuilder builder, string name, CalibrationFit fit)
		{
			builder.AppendFormat(
				CultureInfo.InvariantCulture,
				"    {0,-20} {1,5:+0;-0;0} {2,-8} {3,7:0.000} {4,7:0.000} {5,10:0.000000}",
				name,
				fit.PhaseOffsetSamples,
				fit.Scale < 0.0 ? "inv" : "norm",
				fit.Correlation,
				fit.NormalizedRootMeanSquareError,
				fit.MaxAbsoluteError);
			builder.AppendLine();
		}
	}

	private readonly record struct CalibrationFit(
		string Name,
		int PhaseOffsetSamples,
		double Scale,
		double Bias,
		double Correlation,
		double RootMeanSquareError,
		double NormalizedRootMeanSquareError,
		double MaxAbsoluteError,
		string WorstTransitions)
	{
		public override string ToString()
			=> string.Format(
				CultureInfo.InvariantCulture,
				"{0}: phase {1:+0;-0;0}, polarity {2}, scale {3:0.000000}, bias {4:0.000000}, corr {5:0.000}, nrmse {6:0.000}, rmse {7:0.000000}, max {8:0.000000}; worst {9}",
				Name,
				PhaseOffsetSamples,
				Scale < 0.0 ? "inverted" : "normal",
				Scale,
				Bias,
				Correlation,
				NormalizedRootMeanSquareError,
				RootMeanSquareError,
				MaxAbsoluteError,
				WorstTransitions);
	}
}
