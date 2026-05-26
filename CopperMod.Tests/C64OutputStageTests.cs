namespace CopperMod.Tests;

public sealed class C64OutputStageTests
{
	[Fact]
	public void C64ProfileKeepsHeadroomInsteadOfAddingDrive()
	{
		var stage = new C64OutputStage(C64OutputProfile.C64);
		var samples = new[] { 1.0f, 0.0f, 0.0f, 0.0f };

		stage.Process(samples, channels: 1, sampleRate: 44100);

		Assert.InRange(samples[0], 0.5f, 0.75f);
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
		Assert.True(largestJump < 1.5f, $"Expected C64 output profile to smooth hard SID edges, got jump {largestJump:0.000}.");
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
}
