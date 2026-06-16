namespace CopperMod.Sid.Tests;

public sealed class SidAnalogTests
{
	[Fact]
	public void Mos6581D418MeasuredAmplitudeTableHasExpectedReferenceValues()
	{
		Assert.Equal(256, SidAnalog.Mos6581D418AmplitudeTableLength);
		Assert.InRange(SidAnalog.Mos6581D418MeasuredAmplitude(0x00), 0.0, 0.001);
		Assert.Equal(1.0, SidAnalog.Mos6581D418MeasuredAmplitude(0x0F), precision: 12);
		Assert.Equal(-0.243400, SidAnalog.Mos6581D418MeasuredAmplitude(0x1F), precision: 6);
		Assert.Equal(-0.869965, SidAnalog.Mos6581D418MeasuredAmplitude(0x9F), precision: 6);
		Assert.Equal(-0.635317, SidAnalog.Mos6581D418MeasuredAmplitude(0xFF), precision: 6);
	}

	[Fact]
	public void Mos6581D418VolumeOffsetUsesFullRegisterByte()
	{
		var volume0f = SidAnalog.VolumeOffset(0x0F, SidChipModel.Mos6581);
		var volume1f = SidAnalog.VolumeOffset(0x1F, SidChipModel.Mos6581);
		var volume9f = SidAnalog.VolumeOffset(0x9F, SidChipModel.Mos6581);
		var volumeff = SidAnalog.VolumeOffset(0xFF, SidChipModel.Mos6581);

		Assert.True(volume0f > volume1f + 0.30);
		Assert.True(volume1f > volume9f + 0.15);
		Assert.True(volumeff > volume9f + 0.05);
		Assert.Equal(
			SidAnalog.VolumeOffset(0x0F, SidChipModel.Mos8580),
			SidAnalog.VolumeOffset(0xFF, SidChipModel.Mos8580),
			precision: 12);
	}

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

		Assert.True(mos6581Range > 0.28, $"Expected strong 6581 D418 digi range, got {mos6581Range:0.000}.");
		Assert.True(nightdawnStyleRange > 0.20, $"Expected offset-biased 4-bit digis to stay audible, got {nightdawnStyleRange:0.000}.");
		Assert.True(mos8580Range < mos6581Range * 0.10, $"Expected 8580 volume digis to remain much weaker, 8580 {mos8580Range:0.000}, 6581 {mos6581Range:0.000}.");
	}

	[Fact]
	public void Mos6581VolumeZeroKeepsNonZeroRestVoltage()
	{
		Assert.Equal(0.0, SidAnalog.ConvertVolume(0, SidChipModel.Mos6581), precision: 12);
		Assert.True(
			Math.Abs(SidAnalog.VolumeOffset(0, SidChipModel.Mos6581)) > 0.02,
			$"Expected 6581 volume zero to retain a rest voltage, got {SidAnalog.VolumeOffset(0, SidChipModel.Mos6581):0.000}.");
		Assert.True(
			Math.Abs(SidAnalog.VolumeOffset(0, SidChipModel.Mos8580)) < Math.Abs(SidAnalog.VolumeOffset(0, SidChipModel.Mos6581)) * 0.10,
			"Expected 8580 volume-zero rest voltage to stay far below the 6581 profile.");
	}

	[Fact]
	public void Mos6581VolumeGainAndDcAreSeparateProfileCurves()
	{
		Assert.Equal(0.0, SidAnalog.ConvertVolume(0, SidChipModel.Mos6581), precision: 12);
		Assert.Equal(1.0, SidAnalog.ConvertVolume(15, SidChipModel.Mos6581), precision: 12);
		for (var volume = 1; volume < 16; volume++)
		{
			Assert.True(
				SidAnalog.ConvertVolume(volume, SidChipModel.Mos6581) > SidAnalog.ConvertVolume(volume - 1, SidChipModel.Mos6581),
				$"Expected 6581 volume gain {volume} to exceed {volume - 1}.");
			Assert.True(
				SidAnalog.VolumeOffset(volume, SidChipModel.Mos6581) > SidAnalog.VolumeOffset(volume - 1, SidChipModel.Mos6581),
				$"Expected 6581 volume DC {volume} to exceed {volume - 1}.");
		}
	}
}
