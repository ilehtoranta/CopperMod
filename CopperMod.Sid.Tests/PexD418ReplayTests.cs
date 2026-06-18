using System;
using Xunit;

namespace CopperMod.Sid.Tests;

public sealed class PexD418ReplayTests
{
	private const int SampleRate = 96000;
	private const int WarmupSampleIndex = 800;
	private const int FromSampleIndex = 1000;
	private const int PexZeroToFromCycles = 247;
	private const int PexFromToCycles = 127;
	private const int PexToZeroCycles = 136;
	private const int PexSampleOffset = 13;
	private const int PexZeroRightSampleOffset = 26;
	private const ushort SidBase = SidConstants.DefaultSidBaseAddress;

	[Theory]
	[InlineData((int)SidChipModel.Mos6581, 0x00, 0x0F, 0x9F)]
	[InlineData((int)SidChipModel.Mos8580, 0x00, 0x0F, 0x9F)]
	public void ReferenceMeasuredPexReplayPreservesTransitionContextForSameTarget(
		int modelValue,
		int lowPreviousRegisterValue,
		int nextRegisterValue,
		int highPreviousRegisterValue)
	{
		var model = (SidChipModel)modelValue;
		var lowPrevious = ReplayPexTransition(
			lowPreviousRegisterValue,
			nextRegisterValue,
			model,
			SidEmulationProfile.ReferenceMeasured);
		var highPrevious = ReplayPexTransition(
			highPreviousRegisterValue,
			nextRegisterValue,
			model,
			SidEmulationProfile.ReferenceMeasured);

		var lowNormalizedPostWrite = NormalizeD418Value(lowPrevious.PostWrite, model);
		var highNormalizedPostWrite = NormalizeD418Value(highPrevious.PostWrite, model);
		var expectedLowPostWrite = TransitionPostWriteAmplitude(lowPreviousRegisterValue, nextRegisterValue, model);
		var expectedHighPostWrite = TransitionPostWriteAmplitude(highPreviousRegisterValue, nextRegisterValue, model);

		// Same-target comparisons keep the post-write voice/filter state comparable while still
		// proving the previous full $D418 byte changes the replayed output.
		if (model == SidChipModel.Mos8580)
		{
			Assert.Equal(
				Math.Sign(expectedLowPostWrite - expectedHighPostWrite),
				Math.Sign(lowNormalizedPostWrite - highNormalizedPostWrite));
		}

		Assert.True(
			Math.Abs(lowNormalizedPostWrite - highNormalizedPostWrite) > 0.01,
			$"Expected Pex replay to expose context-sensitive post-write response for {model}.");
	}

	[Fact]
	public void BalancedPexReplayDoesNotApplyMeasuredFullByteTransitionContext()
	{
		var balancedLowPrevious = ReplayPexTransition(
			0x00,
			0x0F,
			SidChipModel.Mos6581,
			SidEmulationProfile.Balanced);
		var balancedHighPrevious = ReplayPexTransition(
			0x9F,
			0x0F,
			SidChipModel.Mos6581,
			SidEmulationProfile.Balanced);
		var referenceLowPrevious = ReplayPexTransition(
			0x00,
			0x0F,
			SidChipModel.Mos6581,
			SidEmulationProfile.ReferenceMeasured);
		var referenceHighPrevious = ReplayPexTransition(
			0x9F,
			0x0F,
			SidChipModel.Mos6581,
			SidEmulationProfile.ReferenceMeasured);

		var balancedSpread = Math.Abs(balancedLowPrevious.PostWrite - balancedHighPrevious.PostWrite);
		var referenceSpread = Math.Abs(referenceLowPrevious.PostWrite - referenceHighPrevious.PostWrite);

		Assert.True(
			referenceSpread > balancedSpread * 1.20,
			$"Expected measured profile context spread {referenceSpread:0.000000} to exceed balanced spread {balancedSpread:0.000000}.");
	}

	internal static PexReplayMeasurement ReplayPexTransition(
		int previousRegisterValue,
		int nextRegisterValue,
		SidChipModel model,
		SidEmulationProfile sidEmulationProfile)
	{
		var sid = new SidSystem(
			new[] { new SidChipPlacement(0, SidBase) },
			model,
			SidConstants.PalCpuCyclesPerSecond,
			SidFilterProfileId.Auto,
			sidEmulationProfile);

		ApplyPexSidSetup(sid);
		sid.RenderSample(CycleForSample(WarmupSampleIndex));

		var fromWriteCycle = CycleForSample(FromSampleIndex);
		WriteD418(sid, 0x00, fromWriteCycle - PexZeroToFromCycles);
		WriteD418(sid, previousRegisterValue, fromWriteCycle);
		WriteD418(sid, nextRegisterValue, fromWriteCycle + PexFromToCycles);
		WriteD418(sid, 0x00, fromWriteCycle + PexFromToCycles + PexToZeroCycles);

		var firstSampleIndex = FromSampleIndex - PexSampleOffset - 1;
		var lastSampleIndex = FromSampleIndex + PexZeroRightSampleOffset + 1;
		var samples = new double[lastSampleIndex - firstSampleIndex + 1];
		for (var sampleIndex = firstSampleIndex; sampleIndex <= lastSampleIndex; sampleIndex++)
		{
			samples[sampleIndex - firstSampleIndex] = sid.RenderSample(CycleForSample(sampleIndex));
		}

		var zeroLeftIndex = FromSampleIndex - PexSampleOffset;
		var toSampleIndex = FromSampleIndex + PexSampleOffset;
		var zeroRightIndex = FromSampleIndex + PexZeroRightSampleOffset;
		var zeroLeft = AverageAt(samples, zeroLeftIndex, firstSampleIndex);
		var preWrite = AverageAt(samples, FromSampleIndex, firstSampleIndex);
		var postWrite = AverageAt(samples, toSampleIndex, firstSampleIndex);
		var zeroRight = AverageAt(samples, zeroRightIndex, firstSampleIndex);
		var preWriteZero = Interpolate(zeroLeft, zeroRight, zeroLeftIndex, zeroRightIndex, FromSampleIndex);
		var postWriteZero = Interpolate(zeroLeft, zeroRight, zeroLeftIndex, zeroRightIndex, toSampleIndex);

		return new PexReplayMeasurement(
			preWrite - preWriteZero,
			postWrite - postWriteZero);
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

	private static void WriteD418(SidSystem sid, int value, long cycle)
		=> WriteSid(sid, 0x18, value, cycle);

	private static void WriteSid(SidSystem sid, int register, int value, long cycle)
		=> Assert.True(sid.TryWrite((ushort)(SidBase + register), (byte)value, cycle));

	private static long CycleForSample(int sampleIndex)
		=> SidIntegerMath.MulDivRoundNearest(sampleIndex, SidConstants.PalCpuCyclesPerSecond, SampleRate);

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

	internal static double NormalizeD418Value(double value, SidChipModel model)
	{
		var zeroAmplitude = MeasuredAmplitude(0x00, model);
		var scale =
			(SidAnalog.VolumeOffset(0x0F, model, SidEmulationProfile.ReferenceMeasured) -
				SidAnalog.VolumeOffset(0x00, model, SidEmulationProfile.ReferenceMeasured)) /
			(MeasuredAmplitude(0x0F, model) - zeroAmplitude);

		return zeroAmplitude + (value / scale);
	}

	private static double MeasuredAmplitude(int registerValue, SidChipModel model)
		=> model == SidChipModel.Mos8580
			? SidAnalog.Mos8580D418MeasuredAmplitude(registerValue)
			: SidAnalog.Mos6581D418MeasuredAmplitude(registerValue);

	internal static double TransitionPostWriteAmplitude(
		int previousRegisterValue,
		int nextRegisterValue,
		SidChipModel model)
		=> model == SidChipModel.Mos8580
			? SidAnalog.Mos8580D418TransitionPostWriteAmplitude(previousRegisterValue, nextRegisterValue)
			: SidAnalog.Mos6581D418TransitionPostWriteAmplitude(previousRegisterValue, nextRegisterValue);

	internal readonly record struct PexReplayMeasurement(double PreWrite, double PostWrite);
}
