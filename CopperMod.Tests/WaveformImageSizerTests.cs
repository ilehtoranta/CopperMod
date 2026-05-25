namespace CopperMod.Tests;

public sealed class WaveformImageSizerTests
{
	[Fact]
	public void ComputeTracksWideTerminalPixelAspect()
	{
		var (width, height) = WaveformImageSizer.Compute(columns: 120, rows: 16, cellWidth: 10, cellHeight: 20);

		Assert.True(width > height);
		Assert.InRange(width, WaveformImageRenderer.MinimumWidth, WaveformImageRenderer.MaximumWidth);
		Assert.InRange(height, WaveformImageRenderer.MinimumHeight, WaveformImageRenderer.MaximumHeight);
		Assert.InRange(width / (double)height, 3.0, 4.5);
	}

	[Fact]
	public void ComputeKeepsTallerViewsTallerWithoutExceedingBudget()
	{
		var (width, height) = WaveformImageSizer.Compute(columns: 80, rows: 30, cellWidth: 10, cellHeight: 20);

		Assert.True(height >= WaveformImageRenderer.MinimumHeight);
		Assert.InRange(width, WaveformImageRenderer.MinimumWidth, WaveformImageRenderer.MaximumWidth);
		Assert.InRange(height, WaveformImageRenderer.MinimumHeight, WaveformImageRenderer.MaximumHeight);
	}

	[Fact]
	public void ComputeKeepsImageNearFastSixelBudget()
	{
		var (width, height) = WaveformImageSizer.Compute(columns: 160, rows: 40, cellWidth: 10, cellHeight: 20);

		Assert.True(width * height <= WaveformImageSizer.MaximumPixels * 1.15);
	}
}
