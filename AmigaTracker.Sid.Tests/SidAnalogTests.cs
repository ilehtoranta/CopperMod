namespace AmigaTracker.Sid.Tests;

public sealed class SidAnalogTests
{
	[Fact]
	public void Mos6581WaveformDacIsMeasurablyNonLinear()
	{
		var lowerHalf = SidAnalog.ConvertWaveformDac12(0x800, SidChipModel.Mos6581) -
			SidAnalog.ConvertWaveformDac12(0x000, SidChipModel.Mos6581);
		var upperHalf = SidAnalog.ConvertWaveformDac12(0xFFF, SidChipModel.Mos6581) -
			SidAnalog.ConvertWaveformDac12(0x800, SidChipModel.Mos6581);

		Assert.True(Math.Abs(lowerHalf - upperHalf) > 0.05, $"Expected 6581 DAC curve to be non-linear, lower {lowerHalf:0.000}, upper {upperHalf:0.000}.");
	}

	[Fact]
	public void Mos8580WaveformDacIsNearLinear()
	{
		var lowerHalf = SidAnalog.ConvertWaveformDac12(0x800, SidChipModel.Mos8580) -
			SidAnalog.ConvertWaveformDac12(0x000, SidChipModel.Mos8580);
		var upperHalf = SidAnalog.ConvertWaveformDac12(0xFFF, SidChipModel.Mos8580) -
			SidAnalog.ConvertWaveformDac12(0x800, SidChipModel.Mos8580);

		Assert.True(Math.Abs(lowerHalf - upperHalf) < 0.002, $"Expected 8580 DAC curve to stay near-linear, lower {lowerHalf:0.000}, upper {upperHalf:0.000}.");
	}

	[Fact]
	public void Mos6581EnvelopeMultiplierIsSlightlyNonLinear()
	{
		var linearMidpoint = SidAnalog.ConvertEnvelope(128, SidChipModel.Mos8580);
		var mos6581Midpoint = SidAnalog.ConvertEnvelope(128, SidChipModel.Mos6581);

		Assert.True(mos6581Midpoint < linearMidpoint);
		Assert.InRange(mos6581Midpoint, 0.45, 0.52);
	}
}
