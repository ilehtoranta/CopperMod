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
	public void Mos8580D418MeasuredAmplitudeTableHasExpectedReferenceValues()
	{
		Assert.Equal(256, SidAnalog.Mos8580D418AmplitudeTableLength);
		Assert.Equal(0.296841, SidAnalog.Mos8580D418MeasuredAmplitude(0x00), precision: 6);
		Assert.Equal(1.0, SidAnalog.Mos8580D418MeasuredAmplitude(0x0F), precision: 12);
		Assert.Equal(1.009703, SidAnalog.Mos8580D418MeasuredAmplitude(0x4F), precision: 6);
		Assert.Equal(-1.0, SidAnalog.Mos8580D418MeasuredAmplitude(0x9F), precision: 12);
		Assert.Equal(-0.964929, SidAnalog.Mos8580D418MeasuredAmplitude(0xFF), precision: 6);
	}

	[Fact]
	public void MeasuredD418TransitionMatricesHaveExpectedShape()
	{
		Assert.Equal(256 * 256, SidAnalog.D418TransitionMatrixLength);
		AssertFiniteMatrix(SidD418TransitionMatrices.Mos6581PreWrite, "6581 pre-write");
		AssertFiniteMatrix(SidD418TransitionMatrices.Mos6581PostWrite, "6581 post-write");
		AssertFiniteMatrix(SidD418TransitionMatrices.Mos8580PreWrite, "8580 pre-write");
		AssertFiniteMatrix(SidD418TransitionMatrices.Mos8580PostWrite, "8580 post-write");
	}

	[Fact]
	public void MeasuredD418TransitionMatricesHaveExpectedReferenceValues()
	{
		AssertTransitionValues(0x00, 0x0F, 0.004160145, 1.03355706, 0.30113918, 0.99477065);
		AssertTransitionValues(0x0F, 0x00, 1.01067090, -0.003847202, 1.00021756, 0.29528311);
		AssertTransitionValues(0x00, 0x9F, -0.002876993, -0.85220689, 0.29199123, -1.00677383);
		AssertTransitionValues(0x9F, 0x0F, -0.86797822, 0.96767813, -0.99111611, 1.00570190);
		AssertTransitionValues(0xFF, 0x00, -0.63494444, 0.003721667, -0.96524543, 0.30268008);
	}

	[Fact]
	public void ReferenceMeasuredD418TransientEnvelopeUsesMeasuredConstants()
	{
		Assert.Equal(0.00130, SidAnalog.VolumeRegisterTransientAttackSeconds(SidChipModel.Mos6581, SidEmulationProfile.Balanced), precision: 12);
		Assert.Equal(0.0042, SidAnalog.VolumeRegisterTransientDecaySeconds(SidChipModel.Mos6581, SidEmulationProfile.Balanced), precision: 12);
		Assert.Equal(3.40, SidAnalog.VolumeRegisterTransientGain(SidChipModel.Mos6581, SidEmulationProfile.Balanced), precision: 12);
		Assert.Equal(0.0, SidAnalog.VolumeRegisterTransientGain(SidChipModel.Mos6581, SidEmulationProfile.ReferenceMeasured), precision: 12);
		Assert.Equal(0.0, SidAnalog.VolumeRegisterTransientGain(SidChipModel.Mos8580, SidEmulationProfile.ReferenceMeasured), precision: 12);

		Assert.Equal(SidD418TransitionMatrices.Mos6581TransientAttackSeconds, SidAnalog.VolumeRegisterTransientAttackSeconds(SidChipModel.Mos6581, SidEmulationProfile.ReferenceMeasured), precision: 15);
		Assert.Equal(SidD418TransitionMatrices.Mos6581TransientDecaySeconds, SidAnalog.VolumeRegisterTransientDecaySeconds(SidChipModel.Mos6581, SidEmulationProfile.ReferenceMeasured), precision: 15);
		Assert.Equal(SidD418TransitionMatrices.Mos8580TransientAttackSeconds, SidAnalog.VolumeRegisterTransientAttackSeconds(SidChipModel.Mos8580, SidEmulationProfile.ReferenceMeasured), precision: 15);
		Assert.Equal(SidD418TransitionMatrices.Mos8580TransientDecaySeconds, SidAnalog.VolumeRegisterTransientDecaySeconds(SidChipModel.Mos8580, SidEmulationProfile.ReferenceMeasured), precision: 15);

		Assert.InRange(SidD418TransitionMatrices.Mos6581TransientAttackSeconds, 0.000010, 0.000011);
		Assert.InRange(SidD418TransitionMatrices.Mos6581TransientDecaySeconds, 0.0010, 0.0012);
		Assert.InRange(SidD418TransitionMatrices.Mos8580TransientDecaySeconds, 0.0004, 0.0005);
		Assert.True(
			SidAnalog.VolumeRegisterTransientSlew(SidChipModel.Mos6581, SidConstants.PalCpuCyclesPerSecond, SidEmulationProfile.ReferenceMeasured) >
				SidAnalog.VolumeRegisterTransientSlew(SidChipModel.Mos6581, SidConstants.PalCpuCyclesPerSecond, SidEmulationProfile.Balanced),
			"Expected measured reference profile to use faster D418 transient attack.");
	}

	[Fact]
	public void Mos8580OutputLowPassCutoffIsProfileSpecific()
	{
		Assert.Equal(
			14_000.0,
			SidAnalog.OutputLowPassCutoffHz(SidChipModel.Mos8580, SidEmulationProfile.Balanced),
			precision: 12);
		Assert.Equal(
			22_000.0,
			SidAnalog.OutputLowPassCutoffHz(SidChipModel.Mos8580, SidEmulationProfile.ReferenceMeasured),
			precision: 12);
	}

	[Fact]
	public void ReferenceCalibrationOverrideIsScopedToMeasuredProfile()
	{
		var defaultLimit = SidAnalog.VolumeRegisterTransientLimit(SidChipModel.Mos6581, SidEmulationProfile.ReferenceMeasured);
		var defaultAttack = SidAnalog.VolumeRegisterTransientAttackSeconds(SidChipModel.Mos6581, SidEmulationProfile.ReferenceMeasured);
		var defaultSlew = SidAnalog.VolumeRegisterTransientSlew(SidChipModel.Mos6581, SidConstants.PalCpuCyclesPerSecond, SidEmulationProfile.ReferenceMeasured);
		var defaultCutoff = SidAnalog.OutputLowPassCutoffHz(SidChipModel.Mos8580, SidEmulationProfile.ReferenceMeasured);
		var defaultTransition = SidAnalog.D418TransitionTransient(0x00, 0x0F, SidChipModel.Mos6581, SidEmulationProfile.ReferenceMeasured);

		using (SidAnalog.PushReferenceCalibration(new SidAnalogReferenceCalibration
		{
			Mos6581TransientLimitScale = 0.50,
			Mos6581TransientAttackScale = 2.00,
			Mos6581TransitionScale = 0.25,
			Mos8580OutputLowPassCutoffHz = 18_000.0
		}))
		{
			Assert.Equal(defaultLimit * 0.50, SidAnalog.VolumeRegisterTransientLimit(SidChipModel.Mos6581, SidEmulationProfile.ReferenceMeasured), precision: 12);
			Assert.Equal(defaultAttack * 2.00, SidAnalog.VolumeRegisterTransientAttackSeconds(SidChipModel.Mos6581, SidEmulationProfile.ReferenceMeasured), precision: 15);
			Assert.True(
				SidAnalog.VolumeRegisterTransientSlew(SidChipModel.Mos6581, SidConstants.PalCpuCyclesPerSecond, SidEmulationProfile.ReferenceMeasured) < defaultSlew,
				"Expected a slower calibrated attack to reduce the per-cycle slew.");
			Assert.Equal(18_000.0, SidAnalog.OutputLowPassCutoffHz(SidChipModel.Mos8580, SidEmulationProfile.ReferenceMeasured), precision: 12);
			Assert.Equal(defaultTransition * 0.25, SidAnalog.D418TransitionTransient(0x00, 0x0F, SidChipModel.Mos6581, SidEmulationProfile.ReferenceMeasured), precision: 12);
			Assert.Equal(0.70, SidAnalog.VolumeRegisterTransientLimit(SidChipModel.Mos6581, SidEmulationProfile.Balanced), precision: 12);
		}

		Assert.Equal(defaultLimit, SidAnalog.VolumeRegisterTransientLimit(SidChipModel.Mos6581, SidEmulationProfile.ReferenceMeasured), precision: 12);
		Assert.Equal(defaultAttack, SidAnalog.VolumeRegisterTransientAttackSeconds(SidChipModel.Mos6581, SidEmulationProfile.ReferenceMeasured), precision: 15);
		Assert.Equal(defaultCutoff, SidAnalog.OutputLowPassCutoffHz(SidChipModel.Mos8580, SidEmulationProfile.ReferenceMeasured), precision: 12);
		Assert.Equal(defaultTransition, SidAnalog.D418TransitionTransient(0x00, 0x0F, SidChipModel.Mos6581, SidEmulationProfile.ReferenceMeasured), precision: 12);
	}

	[Fact]
	public void D418VolumeOffsetUsesMeasuredFullRegisterByte()
	{
		var volume0f = SidAnalog.VolumeOffset(0x0F, SidChipModel.Mos6581);
		var volume1f = SidAnalog.VolumeOffset(0x1F, SidChipModel.Mos6581);
		var volume9f = SidAnalog.VolumeOffset(0x9F, SidChipModel.Mos6581);
		var volumeff = SidAnalog.VolumeOffset(0xFF, SidChipModel.Mos6581);

		Assert.True(volume0f > volume1f + 0.30);
		Assert.True(volume1f > volume9f + 0.15);
		Assert.True(volumeff > volume9f + 0.05);

		var mos8580Volume00 = SidAnalog.VolumeOffset(0x00, SidChipModel.Mos8580);
		var mos8580Volume0f = SidAnalog.VolumeOffset(0x0F, SidChipModel.Mos8580);
		var mos8580Volume9f = SidAnalog.VolumeOffset(0x9F, SidChipModel.Mos8580);
		var mos8580Volumeff = SidAnalog.VolumeOffset(0xFF, SidChipModel.Mos8580);

		Assert.Equal(0.017, mos8580Volume0f - mos8580Volume00, precision: 12);
		Assert.True(mos8580Volume0f > mos8580Volume9f + 0.04);
		Assert.True(mos8580Volume00 > mos8580Volumeff + 0.02);
	}

	[Fact]
	public void ReferenceMeasuredD418TransitionsAreProfileGated()
	{
		var balanced6581 = SidAnalog.D418TransitionTransient(0xD8, 0x0E, SidChipModel.Mos6581, SidEmulationProfile.Balanced);
		var reference6581 = SidAnalog.D418TransitionTransient(0xD8, 0x0E, SidChipModel.Mos6581, SidEmulationProfile.ReferenceMeasured);
		var reference8580 = SidAnalog.D418TransitionTransient(0x00, 0x02, SidChipModel.Mos8580, SidEmulationProfile.ReferenceMeasured);

		Assert.Equal(0.0, balanced6581, precision: 12);
		Assert.True(Math.Abs(reference6581) > 0.005, $"Expected measured 6581 transition correction, got {reference6581:0.000000}.");
		Assert.InRange(Math.Abs(reference8580), 0.0002, Math.Abs(reference6581) * 0.25);
	}

	[Fact]
	public void ReferenceMeasuredD418TransitionUsesMeasuredPostWriteMatrix()
	{
		AssertMeasuredTransitionFormula(0xD8, 0x0E, SidChipModel.Mos6581);
		AssertMeasuredTransitionFormula(0x00, 0x02, SidChipModel.Mos8580);
	}

	[Fact]
	public void ReferenceMeasuredFilterRegisterTransientsAreProfileGated()
	{
		Assert.Equal(0.0, SidAnalog.FilterRoutingTransient(0x00, 0x03, 0x10, SidChipModel.Mos6581, SidEmulationProfile.Balanced), precision: 12);
		Assert.Equal(0.0, SidAnalog.FilterModeTransient(0x0F, 0x1F, SidChipModel.Mos6581, SidEmulationProfile.Balanced), precision: 12);

		var routingImpulse = SidAnalog.FilterRoutingTransient(0x00, 0x03, 0x10, SidChipModel.Mos6581, SidEmulationProfile.ReferenceMeasured);
		var modeImpulse = SidAnalog.FilterModeTransient(0x0F, 0x1F, SidChipModel.Mos6581, SidEmulationProfile.ReferenceMeasured);
		var measuredD418ModeImpulse = SidAnalog.D418TransitionTransient(0x0F, 0x1F, SidChipModel.Mos6581, SidEmulationProfile.ReferenceMeasured);

		Assert.InRange(Math.Abs(routingImpulse), 0.020, 0.030);
		Assert.True(routingImpulse < 0.0, $"Expected routing-in transient to use the SID output polarity, got {routingImpulse:0.000000}.");
		Assert.Equal(0.0, modeImpulse, precision: 12);
		Assert.True(Math.Abs(measuredD418ModeImpulse) > 0.001, $"Expected D418 mode-bit transition to come from the measured matrix, got {measuredD418ModeImpulse:0.000000}.");
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
	public void DacCalibrationIsExplicitlyScopedByModelAndProfile()
	{
		var balanced6581Wave = SidAnalog.ConvertWaveformDac12(0x800, SidChipModel.Mos6581, SidEmulationProfile.Balanced);
		var reference6581Wave = SidAnalog.ConvertWaveformDac12(0x800, SidChipModel.Mos6581, SidEmulationProfile.ReferenceMeasured);
		var balanced6581Envelope = SidAnalog.ConvertEnvelope(128, SidChipModel.Mos6581, SidEmulationProfile.Balanced);
		var reference6581Envelope = SidAnalog.ConvertEnvelope(128, SidChipModel.Mos6581, SidEmulationProfile.ReferenceMeasured);

		Assert.NotEqual(balanced6581Wave, reference6581Wave);
		Assert.NotEqual(balanced6581Envelope, reference6581Envelope);
		Assert.Equal(
			SidAnalog.ConvertWaveformDac12(0x800, SidChipModel.Mos8580, SidEmulationProfile.Balanced),
			SidAnalog.ConvertWaveformDac12(0x800, SidChipModel.Mos8580, SidEmulationProfile.ReferenceMeasured),
			precision: 12);
		Assert.Equal(
			SidAnalog.ConvertEnvelope(128, SidChipModel.Mos8580, SidEmulationProfile.Balanced),
			SidAnalog.ConvertEnvelope(128, SidChipModel.Mos8580, SidEmulationProfile.ReferenceMeasured),
			precision: 12);
	}

	[Fact]
	public void ReferenceCombinedWaveformsUsePinnedPerModelCalibration()
	{
		Assert.Equal("sidplayfp-emulator-derived", SidReferenceCombinedWaveformData.Authority);
		Assert.Equal(64, SidReferenceCombinedWaveformData.SourceSha256.Length);

		Assert.True(SidReferenceCombinedWaveformData.TryGet(SidChipModel.Mos6581, 0x70, out var mos6581));
		Assert.Equal(3, mos6581.ActiveWaveforms);
		Assert.Equal(0.105800, mos6581.Gain, precision: 6);
		Assert.Equal(0x0F1F, mos6581.RetentionMask);

		Assert.True(SidReferenceCombinedWaveformData.TryGet(SidChipModel.Mos8580, 0x70, out var mos8580));
		Assert.Equal(3, mos8580.ActiveWaveforms);
		Assert.Equal(1.000000, mos8580.Gain, precision: 6);
		Assert.Equal(0x0FFF, mos8580.RetentionMask);
		Assert.True(SidReferenceCombinedWaveformData.TryGet(SidChipModel.Mos8580, 0xA0, out var mos8580NoiseSaw));
		Assert.Equal(0.250000, mos8580NoiseSaw.Gain, precision: 6);

		var balanced = SidAnalog.ConvertCombinedWaveformDac12(0x080, 0x70, SidChipModel.Mos6581, SidEmulationProfile.Balanced);
		var reference = SidAnalog.ConvertCombinedWaveformDac12(0x080, 0x70, SidChipModel.Mos6581, SidEmulationProfile.ReferenceMeasured);
		Assert.NotEqual(balanced, reference);
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
	public void Mos6581PurePulseTinyWidthsSettleTowardHighRail()
	{
		var narrowLow = SidAnalog.ScalePulseWidthEdgeOutput(-1.0, 0x40, 0x001, SidChipModel.Mos6581);
		var narrowHigh = SidAnalog.ScalePulseWidthEdgeOutput(1.0, 0x40, 0x001, SidChipModel.Mos6581);
		var tinyLow = SidAnalog.ScalePulseWidthEdgeOutput(-1.0, 0x40, 0x010, SidChipModel.Mos6581);
		var quarter = SidAnalog.ScalePulseWidthEdgeOutput(0.50, 0x40, 0x400, SidChipModel.Mos6581);
		var square = SidAnalog.ScalePulseWidthEdgeOutput(0.50, 0x40, 0x800, SidChipModel.Mos6581);
		var mos8580 = SidAnalog.ScalePulseWidthEdgeOutput(-1.0, 0x40, 0x001, SidChipModel.Mos8580);
		var combined = SidAnalog.ScalePulseWidthEdgeOutput(-1.0, 0x50, 0x001, SidChipModel.Mos6581);

		Assert.InRange(narrowLow, 0.71, 0.73);
		Assert.Equal(1.0, narrowHigh, precision: 12);
		Assert.InRange(tinyLow, 0.67, 0.69);
		Assert.InRange(quarter, 0.46, 0.48);
		Assert.Equal(0.50, square, precision: 12);
		Assert.Equal(-1.0, mos8580, precision: 12);
		Assert.Equal(-1.0, combined, precision: 12);
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

	private static void AssertFiniteMatrix(ReadOnlySpan<float> values, string name)
	{
		Assert.Equal(SidAnalog.D418TransitionMatrixLength, values.Length);
		for (var i = 0; i < values.Length; i++)
		{
			Assert.True(float.IsFinite(values[i]), $"{name} matrix contains a non-finite value at {i}.");
		}
	}

	private static void AssertTransitionValues(
		int previousRegisterValue,
		int nextRegisterValue,
		double mos6581PreWrite,
		double mos6581PostWrite,
		double mos8580PreWrite,
		double mos8580PostWrite)
	{
		Assert.Equal(mos6581PreWrite, SidAnalog.Mos6581D418TransitionPreWriteAmplitude(previousRegisterValue, nextRegisterValue), precision: 6);
		Assert.Equal(mos6581PostWrite, SidAnalog.Mos6581D418TransitionPostWriteAmplitude(previousRegisterValue, nextRegisterValue), precision: 6);
		Assert.Equal(mos8580PreWrite, SidAnalog.Mos8580D418TransitionPreWriteAmplitude(previousRegisterValue, nextRegisterValue), precision: 6);
		Assert.Equal(mos8580PostWrite, SidAnalog.Mos8580D418TransitionPostWriteAmplitude(previousRegisterValue, nextRegisterValue), precision: 6);
	}

	private static void AssertMeasuredTransitionFormula(int previousRegisterValue, int nextRegisterValue, SidChipModel model)
	{
		var postWriteAmplitude = model == SidChipModel.Mos8580
			? SidAnalog.Mos8580D418TransitionPostWriteAmplitude(previousRegisterValue, nextRegisterValue)
			: SidAnalog.Mos6581D418TransitionPostWriteAmplitude(previousRegisterValue, nextRegisterValue);
		var settledAmplitude = model == SidChipModel.Mos8580
			? SidAnalog.Mos8580D418MeasuredAmplitude(nextRegisterValue)
			: SidAnalog.Mos6581D418MeasuredAmplitude(nextRegisterValue);
		var scale =
			(SidAnalog.VolumeOffset(0x0F, model, SidEmulationProfile.ReferenceMeasured) -
				SidAnalog.VolumeOffset(0x00, model, SidEmulationProfile.ReferenceMeasured)) /
			((model == SidChipModel.Mos8580 ? SidAnalog.Mos8580D418MeasuredAmplitude(0x0F) : SidAnalog.Mos6581D418MeasuredAmplitude(0x0F)) -
				(model == SidChipModel.Mos8580 ? SidAnalog.Mos8580D418MeasuredAmplitude(0x00) : SidAnalog.Mos6581D418MeasuredAmplitude(0x00)));
		if (model == SidChipModel.Mos6581)
		{
			scale = -scale;
		}

		var limit = SidAnalog.VolumeRegisterTransientLimit(model, SidEmulationProfile.ReferenceMeasured);
		var expected = Math.Clamp((postWriteAmplitude - settledAmplitude) * scale, -limit, limit);

		Assert.Equal(expected, SidAnalog.D418TransitionTransient(previousRegisterValue, nextRegisterValue, model, SidEmulationProfile.ReferenceMeasured), precision: 12);
	}
}
