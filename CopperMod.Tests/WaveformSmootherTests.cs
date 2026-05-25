namespace CopperMod.Tests;

public sealed class WaveformSmootherTests
{
	[Fact]
	public void MoveTowardsInterpolatesMatchingSnapshots()
	{
		var current = new WaveformSnapshot(
			new[] { -1.0f, 0.0f },
			new[] { 0.0f, 1.0f },
			sourceFrameCount: 2,
			sampleRate: 44100);
		var target = new WaveformSnapshot(
			new[] { 0.0f, -1.0f },
			new[] { 1.0f, 0.0f },
			sourceFrameCount: 2,
			sampleRate: 44100);

		var smoothed = WaveformSmoother.MoveTowards(current, target, amount: 0.5f, out var settled);

		Assert.False(settled);
		Assert.Equal(new[] { -0.5f, -0.5f }, smoothed.Minimums);
		Assert.Equal(new[] { 0.5f, 0.5f }, smoothed.Maximums);
	}

	[Fact]
	public void MoveTowardsSnapsWhenShapesDiffer()
	{
		var current = new WaveformSnapshot(new[] { -1.0f }, new[] { 1.0f }, sourceFrameCount: 1, sampleRate: 44100);
		var target = new WaveformSnapshot(new[] { -0.5f, -0.25f }, new[] { 0.5f, 0.25f }, sourceFrameCount: 2, sampleRate: 44100);

		var smoothed = WaveformSmoother.MoveTowards(current, target, amount: 0.5f, out var settled);

		Assert.True(settled);
		Assert.Same(target, smoothed);
	}
}
