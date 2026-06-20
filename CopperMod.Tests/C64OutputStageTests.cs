using CopperMod.Rendering;

namespace CopperMod.Tests;

public sealed class C64OutputStageTests
{
	[Fact]
	public void C64ProfileKeepsHeadroomInsteadOfAddingDrive()
	{
		var stage = new C64OutputStage(C64OutputProfile.C64);
		var samples = new[] { 1.0f, 0.0f, 0.0f, 0.0f };

		stage.Process(samples, channels: 1, sampleRate: 44100);

		Assert.InRange(samples[0], 0.35f, 0.55f);
		Assert.All(samples, sample => Assert.InRange(sample, -1.0f, 1.0f));
	}

	[Fact]
	public void C64ProfileSoftensSharpSidEdges()
	{
		var stage = new C64OutputStage(C64OutputProfile.C64);
		var samples = Enumerable.Range(0, 256)
			.Select(index => index % 2 == 0 ? 1.0f : -1.0f)
			.ToArray();

		stage.Process(samples, channels: 1, sampleRate: 44100);

		var largestJump = samples.Zip(samples.Skip(1), (previous, current) => Math.Abs(current - previous)).Max();
		Assert.True(largestJump < 1.95f, $"Expected C64 output profile to keep hard SID edges bounded, got jump {largestJump:0.000}.");
	}

	[Fact]
	public void C64ProfileKeepsUpperTrebleOpen()
	{
		var stage = new C64OutputStage(C64OutputProfile.C64);
		const int sampleRate = 44100;
		const double frequency = 16000.0;
		var samples = Enumerable.Range(0, sampleRate)
			.Select(index => (float)(Math.Sin(2.0 * Math.PI * frequency * index / sampleRate) * 0.5))
			.ToArray();
		var inputRms = Rms(samples, sampleRate / 4, sampleRate / 2);

		stage.Process(samples, channels: 1, sampleRate);

		var outputRms = Rms(samples, sampleRate / 4, sampleRate / 2);
		Assert.True(
			outputRms / inputRms > 0.50,
			$"Expected C64 output profile to preserve upper treble with headroom, ratio was {outputRms / inputRms:0.000}.");
	}

	[Fact]
	public void C64ProfileCouplesDcStepsIntoDecayingTransients()
	{
		var stage = new C64OutputStage(C64OutputProfile.C64);
		var samples = new float[44100];
		for (var i = 2205; i < samples.Length; i++)
		{
			samples[i] = 0.5f;
		}

		stage.Process(samples, channels: 1, sampleRate: 44100);

		var earlyPeak = samples.Skip(2205).Take(512).Max(Math.Abs);
		var latePeak = samples.Skip(samples.Length - 4096).Max(Math.Abs);
		Assert.True(earlyPeak > 0.20f, $"Expected DC step to produce an audible transient, got {earlyPeak:0.000}.");
		Assert.True(latePeak < earlyPeak * 0.10f, $"Expected C64 coupling to decay steady DC, early {earlyPeak:0.000}, late {latePeak:0.000}.");
	}

	[Fact]
	public void C64ProfileKeepsDcCouplingDroopVisibleAcrossFirstNote()
	{
		var stage = new C64OutputStage(C64OutputProfile.C64);
		const int sampleRate = 44100;
		var samples = Enumerable.Repeat(0.5f, sampleRate).ToArray();

		stage.Process(samples, channels: 1, sampleRate);

		var initial = Math.Abs(samples[0]);
		var after50Milliseconds = Math.Abs(samples[sampleRate / 20]);
		var after250Milliseconds = Math.Abs(samples[sampleRate / 4]);
		Assert.True(
			after50Milliseconds > initial * 0.40f,
			$"Expected C64 output coupling to retain visible first-note droop, initial {initial:0.000}, 50 ms {after50Milliseconds:0.000}.");
		Assert.True(
			after250Milliseconds < initial * 0.20f,
			$"Expected C64 output coupling to settle after the note attack window, initial {initial:0.000}, 250 ms {after250Milliseconds:0.000}.");
	}

	[Fact]
	public void CleanProfileBypassesC64PostOutputShaping()
	{
		var stage = new C64OutputStage(C64OutputProfile.Clean);
		var samples = new[] { -0.75f, 0.25f, 0.5f, 0.9f };
		var original = samples.ToArray();

		stage.Process(samples, channels: 1, sampleRate: 44100);

		Assert.Equal(original, samples);
	}

	[Fact]
	public void C64ProfileDecaysConstantSidDcWhileCleanProfilePreservesIt()
	{
		const int sampleRate = 44100;
		var c64Samples = Enumerable.Repeat(0.35f, sampleRate).ToArray();
		var cleanSamples = c64Samples.ToArray();

		new C64OutputStage(C64OutputProfile.C64).Process(c64Samples, channels: 1, sampleRate);
		new C64OutputStage(C64OutputProfile.Clean).Process(cleanSamples, channels: 1, sampleRate);

		var c64Initial = Math.Abs(c64Samples[0]);
		var c64Late = c64Samples.Skip(sampleRate - 4096).Max(Math.Abs);
		Assert.True(c64Initial > 0.15f, $"Expected C64 profile to pass the initial SID DC edge, got {c64Initial:0.000}.");
		Assert.True(c64Late < c64Initial * 0.05f, $"Expected C64 profile to decay steady SID DC, initial {c64Initial:0.000}, late {c64Late:0.000}.");
		Assert.All(cleanSamples, sample => Assert.Equal(0.35f, sample));
	}

	private static double Rms(float[] samples, int start, int count)
	{
		var sumSquares = 0.0;
		for (var i = 0; i < count; i++)
		{
			var sample = samples[start + i];
			sumSquares += sample * sample;
		}

		return Math.Sqrt(sumSquares / count);
	}
}
