public sealed class PlayerWindowWaveformTests
{
	[Theory]
	[InlineData(false, true)]
	[InlineData(true, false)]
	public void WaveformRenderingContinuesOnlyUntilCurrentFrameSettles(bool settled, bool expected)
	{
		Assert.Equal(expected, CopperMod.PlayerWindow.ShouldContinueWaveformRendering(settled));
	}
}
