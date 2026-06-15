using CopperMod.Abstractions;

namespace CopperMod.Sid.Tests;

public sealed class SidCommandoLikeFixtureTests
{
	private const int SampleRate = 44100;

	[Fact]
	public void CommandoLikeDryPulseMixWritesExpectedVoice3BassRegisters()
	{
		var song = LoadFixture();
		var options = new AudioRenderOptions(sampleRate: SampleRate, channelCount: 1);

		RenderTicks(song, options, ticks: 4);

		var writes = song.SidWrites;
		Assert.Contains(writes, write => write.Register == 0x17 && write.Value == 0x00);
		Assert.Contains(writes, write => write.Register == 0x18 && write.Value == 0x0F);
		Assert.Contains(writes, write => write.Register == 0x12 && write.Value == 0x41);

		var pulseWidthLowValues = writes
			.Where(write => write.Register == 0x10)
			.Select(write => write.Value)
			.Distinct()
			.ToArray();
		Assert.True(
			pulseWidthLowValues.Length >= 3,
			$"Expected voice 3 pulse width low byte to move across play calls, saw {pulseWidthLowValues.Length} distinct values.");
	}

	[Fact]
	public void CommandoLikeDryPulseMixKeepsVoice3AudibleInsideFullMix()
	{
		var fullMix = RenderMeasuredSamples(mutedVoicesMask: 0x00);
		var voice3Solo = RenderMeasuredSamples(mutedVoicesMask: 0x03);
		var withoutVoice3 = RenderMeasuredSamples(mutedVoicesMask: 0x04);

		AssertFinite(fullMix);
		AssertFinite(voice3Solo);
		AssertFinite(withoutVoice3);

		var fullRange = PeakToPeak(fullMix);
		var fullAcRms = AcRms(fullMix);
		var voice3AcRms = AcRms(voice3Solo);
		var withoutVoice3AcRms = AcRms(withoutVoice3);
		var voice3ContributionRms = DifferenceRms(fullMix, withoutVoice3);

		Assert.True(fullRange > 0.70, $"Expected full dry mix to retain strong output range, got {fullRange:0.000}.");
		Assert.True(fullAcRms > 0.10, $"Expected full dry mix AC RMS to remain audible, got {fullAcRms:0.000}.");
		Assert.True(voice3AcRms > 0.03, $"Expected low voice 3 pulse solo to remain audible, got {voice3AcRms:0.000}.");
		Assert.True(
			voice3ContributionRms > fullAcRms * 0.10,
			$"Expected voice 3 to materially affect the full dry mix, contribution {voice3ContributionRms:0.000}, full RMS {fullAcRms:0.000}.");
		Assert.True(
			fullAcRms > Math.Max(voice3AcRms, withoutVoice3AcRms) * 0.65,
			$"Expected the full mix not to collapse to a muted/solo level, full {fullAcRms:0.000}, voice3 {voice3AcRms:0.000}, without voice3 {withoutVoice3AcRms:0.000}.");
	}

	[Fact]
	public void CommandoLikeDryPulseMixOutputRemainsBoundedAndSmooth()
	{
		var samples = RenderMeasuredSamples(mutedVoicesMask: 0x00);
		var largestJump = LargestAdjacentJump(samples);

		AssertFinite(samples);
		Assert.All(samples, sample => Assert.InRange(sample, -0.999f, 0.999f));
		Assert.True(largestJump < 0.55, $"Expected output low-pass stage to avoid pathological sample jumps, largest jump {largestJump:0.000}.");
	}

	private static SidSong LoadFixture()
	{
		return (SidSong)new SidFormat().Load(SidFixtureBuilder.CreateCommandoLikeDryPulseMixPsid());
	}

	private static float[] RenderMeasuredSamples(int mutedVoicesMask)
	{
		var song = LoadFixture();
		song.MutedVoicesMask = mutedVoicesMask;
		var options = new AudioRenderOptions(sampleRate: SampleRate, channelCount: 1);
		RenderTicks(song, options, ticks: 2);

		var samples = new List<float>();
		for (var tick = 0; tick < 12; tick++)
		{
			var frames = song.GetCurrentTickFrameCount(options);
			var buffer = new float[options.GetSampleCount(frames)];
			song.RenderTick(buffer, options);
			samples.AddRange(buffer);
		}

		return samples.ToArray();
	}

	private static void RenderTicks(SidSong song, AudioRenderOptions options, int ticks)
	{
		for (var tick = 0; tick < ticks; tick++)
		{
			var frames = song.GetCurrentTickFrameCount(options);
			var buffer = new float[options.GetSampleCount(frames)];
			song.RenderTick(buffer, options);
		}
	}

	private static void AssertFinite(IEnumerable<float> samples)
	{
		Assert.All(samples, sample => Assert.True(float.IsFinite(sample)));
	}

	private static double PeakToPeak(IReadOnlyList<float> samples)
	{
		return samples.Max() - samples.Min();
	}

	private static double AcRms(IReadOnlyList<float> samples)
	{
		var mean = samples.Average();
		var sum = 0.0;
		for (var i = 0; i < samples.Count; i++)
		{
			var sample = samples[i] - mean;
			sum += sample * sample;
		}

		return Math.Sqrt(sum / Math.Max(1, samples.Count));
	}

	private static double DifferenceRms(IReadOnlyList<float> left, IReadOnlyList<float> right)
	{
		Assert.Equal(left.Count, right.Count);
		var sum = 0.0;
		for (var i = 0; i < left.Count; i++)
		{
			var delta = left[i] - right[i];
			sum += delta * delta;
		}

		return Math.Sqrt(sum / Math.Max(1, left.Count));
	}

	private static double LargestAdjacentJump(IReadOnlyList<float> samples)
	{
		var largest = 0.0;
		for (var i = 1; i < samples.Count; i++)
		{
			largest = Math.Max(largest, Math.Abs(samples[i] - samples[i - 1]));
		}

		return largest;
	}
}
