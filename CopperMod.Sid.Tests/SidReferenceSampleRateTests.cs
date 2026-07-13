namespace CopperMod.Sid.Tests;

public sealed class SidReferenceSampleRateTests
{
	[Theory]
	[InlineData(1, SidConstants.PalCpuCyclesPerSecond)]
	[InlineData(1, SidConstants.NtscCpuCyclesPerSecond)]
	[InlineData(2, SidConstants.PalCpuCyclesPerSecond)]
	[InlineData(2, SidConstants.NtscCpuCyclesPerSecond)]
	public void ReferenceMeasuredLevelIsStableAcrossOutputSampleRates(int modelValue, int cpuCyclesPerSecond)
	{
		var at44100 = RenderReferenceSaw((SidChipModel)modelValue, cpuCyclesPerSecond, 44100);
		var at48000 = RenderReferenceSaw((SidChipModel)modelValue, cpuCyclesPerSecond, 48000);
		var at96000 = RenderReferenceSaw((SidChipModel)modelValue, cpuCyclesPerSecond, 96000);
		var referenceRms = AcRms(at48000);

		Assert.InRange(AcRms(at44100) / referenceRms, 0.98, 1.02);
		Assert.InRange(AcRms(at96000) / referenceRms, 0.98, 1.02);
		Assert.All(at44100.Concat(at48000).Concat(at96000), sample =>
		{
			Assert.True(float.IsFinite(sample));
			Assert.InRange(sample, -0.999f, 0.999f);
		});
	}

	private static float[] RenderReferenceSaw(SidChipModel model, int cpuCyclesPerSecond, int sampleRate)
	{
		var sid = new SidSystem(
			new[] { new SidChipPlacement(0, SidConstants.DefaultSidBaseAddress, model) },
			model,
			cpuCyclesPerSecond,
			sidEmulationProfile: SidEmulationProfile.ReferenceMeasured);
		Assert.True(sid.TryWrite(0xD400, 0x00, 0));
		Assert.True(sid.TryWrite(0xD401, 0x20, 0));
		Assert.True(sid.TryWrite(0xD405, 0x00, 0));
		Assert.True(sid.TryWrite(0xD406, 0xF0, 0));
		Assert.True(sid.TryWrite(0xD404, 0x21, 0));
		Assert.True(sid.TryWrite(0xD418, 0x0F, 0));

		var samples = new float[sampleRate / 4];
		for (var i = 0; i < samples.Length; i++)
		{
			var cycle = SidIntegerMath.MulDivRoundNearest(i + 1, cpuCyclesPerSecond, sampleRate);
			samples[i] = sid.RenderSample(cycle);
		}

		return samples[(sampleRate / 20)..];
	}

	private static double AcRms(IReadOnlyList<float> samples)
	{
		var mean = samples.Average(sample => (double)sample);
		return Math.Sqrt(samples.Average(sample =>
		{
			var centered = sample - mean;
			return centered * centered;
		}));
	}
}
