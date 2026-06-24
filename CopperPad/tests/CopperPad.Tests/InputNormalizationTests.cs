using CopperPad;

public sealed class InputNormalizationTests
{
	[Fact]
	public void NormalizeAxis_MapsCenteredByteAxisToSignedRange()
	{
		Assert.Equal(0, InputNormalization.NormalizeAxis(128, 0, 255), precision: 2);
		Assert.True(InputNormalization.NormalizeAxis(255, 0, 255) > 0.99);
		Assert.True(InputNormalization.NormalizeAxis(0, 0, 255) < -0.99);
	}

	[Fact]
	public void NormalizeAxis_AppliesDeadzoneAndInversion()
	{
		Assert.Equal(0, InputNormalization.NormalizeAxis(134, 0, 255, deadzone: 0.1), precision: 3);
		Assert.True(InputNormalization.NormalizeAxis(255, 0, 255, invert: true) < -0.99);
	}

	[Fact]
	public void NormalizeTrigger_MapsUnsignedRangeToZeroToOne()
	{
		Assert.Equal(0, InputNormalization.NormalizeTrigger(0, 0, 255), precision: 3);
		Assert.Equal(1, InputNormalization.NormalizeTrigger(255, 0, 255), precision: 3);
		Assert.InRange(InputNormalization.NormalizeTrigger(128, 0, 255), 0.49, 0.51);
	}

	[Theory]
	[InlineData(0, true, false, false, false)]
	[InlineData(1, true, false, false, true)]
	[InlineData(4, false, true, false, false)]
	[InlineData(6, false, false, true, false)]
	[InlineData(8, false, false, false, false)]
	public void HatToDirections_DecodesStandardEightWayHat(int value, bool up, bool down, bool left, bool right)
	{
		var directions = InputNormalization.HatToDirections(value);
		Assert.Equal(up, directions.Up);
		Assert.Equal(down, directions.Down);
		Assert.Equal(left, directions.Left);
		Assert.Equal(right, directions.Right);
	}
}
