namespace CopperMod.Sid.Tests;

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

	[Fact]
	public void Mos6581VolumeOffsetKeepsDigiPlaybackProminent()
	{
		var mos6581Range = SidAnalog.VolumeOffset(15, SidChipModel.Mos6581) -
			SidAnalog.VolumeOffset(0, SidChipModel.Mos6581);
		var mos8580Range = SidAnalog.VolumeOffset(15, SidChipModel.Mos8580) -
			SidAnalog.VolumeOffset(0, SidChipModel.Mos8580);
		var nightdawnStyleRange = SidAnalog.VolumeOffset(15, SidChipModel.Mos6581) -
			SidAnalog.VolumeOffset(4, SidChipModel.Mos6581);

		Assert.True(mos6581Range > 0.32, $"Expected strong 6581 D418 digi range, got {mos6581Range:0.000}.");
		Assert.True(nightdawnStyleRange > 0.26, $"Expected offset-biased 4-bit digis to stay audible, got {nightdawnStyleRange:0.000}.");
		Assert.True(mos8580Range < mos6581Range * 0.10, $"Expected 8580 volume digis to remain much weaker, 8580 {mos8580Range:0.000}, 6581 {mos6581Range:0.000}.");
	}
}
