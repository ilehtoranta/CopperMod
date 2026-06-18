using System;
using Xunit;

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
	private const ushort SidBase = SidConstants.DefaultSidBaseAddress;

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

	[Theory]
	[InlineData((int)SidChipModel.Mos6581)]
	[InlineData((int)SidChipModel.Mos8580)]
	public void OptionalContinuousPexReplayCalibrationProducesFiniteFitReport(int modelValue)
	{
		if (!ContinuousCalibrationEnabled())
		{
			return;
		}

		var model = (SidChipModel)modelValue;
		var reference = MeasureContextFit(model, SidEmulationProfile.ReferenceMeasured);
		var balanced = MeasureContextFit(model, SidEmulationProfile.Balanced);

		AssertFinite(model, SidEmulationProfile.ReferenceMeasured, reference);
		AssertFinite(model, SidEmulationProfile.Balanced, balanced);
	}

	private static void AssertFinite(SidChipModel model, SidEmulationProfile sidEmulationProfile, ContextFit fit)
	{
		Assert.True(double.IsFinite(fit.Scale), $"{model} {sidEmulationProfile} invalid scale: {fit}.");
		Assert.True(double.IsFinite(fit.Correlation), $"{model} {sidEmulationProfile} invalid correlation: {fit}.");
		Assert.True(double.IsFinite(fit.RootMeanSquareError), $"{model} {sidEmulationProfile} invalid RMSE: {fit}.");
		Assert.True(double.IsFinite(fit.NormalizedRootMeanSquareError), $"{model} {sidEmulationProfile} invalid normalized RMSE: {fit}.");
		Assert.True(double.IsFinite(fit.MaxAbsoluteError), $"{model} {sidEmulationProfile} invalid max error: {fit}.");
	}

	private static ContextFit MeasureContextFit(SidChipModel model, SidEmulationProfile sidEmulationProfile)
	{
		var transitions = new (int Previous, int Next)[ContextDeltas.Length * 2];
		for (var i = 0; i < ContextDeltas.Length; i++)
		{
			var sample = ContextDeltas[i];
			transitions[i * 2] = (sample.PreviousA, sample.Next);
			transitions[(i * 2) + 1] = (sample.PreviousB, sample.Next);
		}

		var replay = ReplayContinuousPexTransitions(model, sidEmulationProfile, transitions);
		var observed = new double[ContextDeltas.Length];
		var expected = new double[ContextDeltas.Length];
		for (var i = 0; i < ContextDeltas.Length; i++)
		{
			var sample = ContextDeltas[i];
			var lowPrevious = replay[(sample.PreviousA << 8) | sample.Next];
			var highPrevious = replay[(sample.PreviousB << 8) | sample.Next];

			observed[i] = lowPrevious.PostWrite - highPrevious.PostWrite;
			expected[i] =
				PexD418ReplayTests.TransitionPostWriteAmplitude(sample.PreviousA, sample.Next, model) -
				PexD418ReplayTests.TransitionPostWriteAmplitude(sample.PreviousB, sample.Next, model);
		}

		var scale = FitScaleThroughOrigin(observed, expected);
		var sumSquaredError = 0.0;
		var maxAbsoluteError = 0.0;
		for (var i = 0; i < observed.Length; i++)
		{
			var error = (observed[i] * scale) - expected[i];
			sumSquaredError += error * error;
			maxAbsoluteError = Math.Max(maxAbsoluteError, Math.Abs(error));
		}

		var rootMeanSquareError = Math.Sqrt(sumSquaredError / observed.Length);
		var expectedSpread = StandardDeviation(expected);
		var normalizedRootMeanSquareError = expectedSpread <= 0.0
			? rootMeanSquareError
			: rootMeanSquareError / expectedSpread;
		return new ContextFit(
			scale,
			Correlation(observed, expected),
			rootMeanSquareError,
			normalizedRootMeanSquareError,
			maxAbsoluteError,
			DescribeSamples(observed, expected, scale));
	}

	private static PexD418ReplayTests.PexReplayMeasurement[] ReplayContinuousPexTransitions(
		SidChipModel model,
		SidEmulationProfile sidEmulationProfile,
		(int Previous, int Next)[] transitions)
	{
		var maxTransitionIndex = 0;
		for (var i = 0; i < transitions.Length; i++)
		{
			maxTransitionIndex = Math.Max(maxTransitionIndex, (transitions[i].Previous << 8) | transitions[i].Next);
		}

		var sid = new SidSystem(
			new[] { new SidChipPlacement(0, SidBase) },
			model,
			SidConstants.PalCpuCyclesPerSecond,
			SidFilterProfileId.Auto,
			sidEmulationProfile);
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

		var result = new PexD418ReplayTests.PexReplayMeasurement[256 * 256];
		var sortedTransitions = new int[transitions.Length];
		for (var i = 0; i < transitions.Length; i++)
		{
			sortedTransitions[i] = (transitions[i].Previous << 8) | transitions[i].Next;
		}

		Array.Sort(sortedTransitions);
		for (var i = 0; i < sortedTransitions.Length; i++)
		{
			var transitionIndex = sortedTransitions[i];
			var measurement = MeasureContinuousTransition(sid, transitionIndex);
			result[transitionIndex] = measurement;
		}

		return result;
	}

	private static bool ContinuousCalibrationEnabled()
		=> string.Equals(
			Environment.GetEnvironmentVariable("SID_D418_REPLAY_CALIBRATION"),
			"1",
			StringComparison.Ordinal);

	private static PexD418ReplayTests.PexReplayMeasurement MeasureContinuousTransition(
		SidSystem sid,
		int transitionIndex)
	{
		var centerIndex = FirstFromSampleIndex +
			(transitionIndex * PexTransitionCycles * (double)SampleRate / SidConstants.PalCpuCyclesPerSecond);
		var zeroLeftIndex = RoundMatlab(centerIndex - PexSampleOffset);
		var fromIndex = RoundMatlab(centerIndex);
		var toIndex = RoundMatlab(centerIndex + PexSampleOffset);
		var zeroRightIndex = RoundMatlab(centerIndex + PexZeroRightSampleOffset);
		var firstSampleIndex = zeroLeftIndex - 1;
		var lastSampleIndex = zeroRightIndex + 1;
		var samples = new double[lastSampleIndex - firstSampleIndex + 1];
		for (var sampleIndex = firstSampleIndex; sampleIndex <= lastSampleIndex; sampleIndex++)
		{
			samples[sampleIndex - firstSampleIndex] = RenderSingleSampleAt(sid, sampleIndex);
		}

		var zeroLeft = AverageAt(samples, zeroLeftIndex, firstSampleIndex);
		var preWrite = AverageAt(samples, fromIndex, firstSampleIndex);
		var postWrite = AverageAt(samples, toIndex, firstSampleIndex);
		var zeroRight = AverageAt(samples, zeroRightIndex, firstSampleIndex);
		var preWriteZero = Interpolate(zeroLeft, zeroRight, zeroLeftIndex, zeroRightIndex, fromIndex);
		var postWriteZero = Interpolate(zeroLeft, zeroRight, zeroLeftIndex, zeroRightIndex, toIndex);

		return new PexD418ReplayTests.PexReplayMeasurement(
			preWrite - preWriteZero,
			postWrite - postWriteZero);
	}

	private static float RenderSingleSampleAt(SidSystem sid, int sampleIndex)
	{
		sid.AdvanceTo(CycleForSample(sampleIndex - 1));
		sid.DiscardAccumulatedOutput();
		return sid.RenderSample(CycleForSample(sampleIndex));
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

	private static double AverageAt(double[] samples, int sampleIndex, int firstSampleIndex)
	{
		var offset = sampleIndex - firstSampleIndex;
		return (samples[offset - 1] + samples[offset] + samples[offset + 1]) / 3.0;
	}

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

	private static double FitScaleThroughOrigin(double[] observed, double[] expected)
	{
		var numerator = 0.0;
		var denominator = 0.0;
		for (var i = 0; i < observed.Length; i++)
		{
			numerator += observed[i] * expected[i];
			denominator += observed[i] * observed[i];
		}

		return denominator == 0.0 ? 0.0 : numerator / denominator;
	}

	private static double Correlation(double[] left, double[] right)
	{
		var leftMean = Mean(left);
		var rightMean = Mean(right);
		var numerator = 0.0;
		var leftSquared = 0.0;
		var rightSquared = 0.0;
		for (var i = 0; i < left.Length; i++)
		{
			var leftDelta = left[i] - leftMean;
			var rightDelta = right[i] - rightMean;
			numerator += leftDelta * rightDelta;
			leftSquared += leftDelta * leftDelta;
			rightSquared += rightDelta * rightDelta;
		}

		var denominator = Math.Sqrt(leftSquared * rightSquared);
		return denominator == 0.0 ? 0.0 : numerator / denominator;
	}

	private static double StandardDeviation(double[] values)
	{
		var mean = Mean(values);
		var sumSquared = 0.0;
		for (var i = 0; i < values.Length; i++)
		{
			var delta = values[i] - mean;
			sumSquared += delta * delta;
		}

		return Math.Sqrt(sumSquared / values.Length);
	}

	private static double Mean(double[] values)
	{
		var sum = 0.0;
		for (var i = 0; i < values.Length; i++)
		{
			sum += values[i];
		}

		return sum / values.Length;
	}

	private static string DescribeSamples(double[] observed, double[] expected, double scale)
	{
		var parts = new string[observed.Length];
		for (var i = 0; i < observed.Length; i++)
		{
			var sample = ContextDeltas[i];
			parts[i] = $"{sample.PreviousA:X2}/{sample.PreviousB:X2}->{sample.Next:X2}: observed {observed[i]:0.000000}, scaled {observed[i] * scale:0.000000}, expected {expected[i]:0.000000}";
		}

		return string.Join("; ", parts);
	}

	private readonly record struct ContextDelta(int PreviousA, int PreviousB, int Next);

	private readonly record struct ContextFit(
		double Scale,
		double Correlation,
		double RootMeanSquareError,
		double NormalizedRootMeanSquareError,
		double MaxAbsoluteError,
		string Details)
	{
		public override string ToString()
			=> $"scale {Scale:0.000000}, corr {Correlation:0.000}, nrmse {NormalizedRootMeanSquareError:0.000}, rmse {RootMeanSquareError:0.000000}, max {MaxAbsoluteError:0.000000}; {Details}";
	}
}
