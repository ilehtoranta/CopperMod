using CopperScreen;

namespace CopperScreen.Tests;

public sealed class MainWindowMouseInputTests
{
	[Fact]
	public void FractionalFramebufferMouseDeltasAccumulateIntoWholeCounts()
	{
		var remainder = 0.0;

		Assert.Equal(0, MainWindow.ConsumeWholeMouseDelta(ref remainder, 0.375));
		Assert.Equal(0.375, remainder, precision: 6);
		Assert.Equal(0, MainWindow.ConsumeWholeMouseDelta(ref remainder, 0.375));
		Assert.Equal(0.75, remainder, precision: 6);
		Assert.Equal(1, MainWindow.ConsumeWholeMouseDelta(ref remainder, 0.375));
		Assert.Equal(0.125, remainder, precision: 6);
	}

	[Fact]
	public void NegativeFractionalFramebufferMouseDeltasAccumulateIntoWholeCounts()
	{
		var remainder = 0.0;

		Assert.Equal(0, MainWindow.ConsumeWholeMouseDelta(ref remainder, -0.375));
		Assert.Equal(-0.375, remainder, precision: 6);
		Assert.Equal(0, MainWindow.ConsumeWholeMouseDelta(ref remainder, -0.375));
		Assert.Equal(-0.75, remainder, precision: 6);
		Assert.Equal(-1, MainWindow.ConsumeWholeMouseDelta(ref remainder, -0.375));
		Assert.Equal(-0.125, remainder, precision: 6);
	}
}
