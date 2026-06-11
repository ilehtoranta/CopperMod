namespace CopperMod.Sid.Tests;

public sealed class SidFilterCharacterizationTests
{
	[Fact]
	public void Mos6581CutoffSweepCharacterizationIsFiniteAndMonotonic()
	{
		var points = SidFilterCharacterizer.SweepCutoff(
			SidChipModel.Mos6581,
			SidFilterProfileId.Mos6581Balanced,
			new[] { 0, 128, 512, 1024, 1536, 2047 },
			resonanceNibble: 8,
			filterMode: 0x10,
			frequency: 0x1800);

		var previousCutoff = 0.0;
		foreach (var point in points)
		{
			Assert.True(double.IsFinite(point.FilterCutoffHz));
			Assert.True(double.IsFinite(point.FilterDamping));
			Assert.True(double.IsFinite(point.Dc));
			Assert.True(double.IsFinite(point.Rms));
			Assert.True(double.IsFinite(point.AcRms));
			Assert.True(double.IsFinite(point.Peak));
			Assert.True(double.IsFinite(point.PeakToPeak));
			Assert.True(double.IsFinite(point.ZeroCrossingRate));
			Assert.True(double.IsFinite(point.MeanAbsDelta));
			Assert.True(double.IsFinite(point.Brightness));
			Assert.True(double.IsFinite(point.LowPassPeak));
			Assert.True(double.IsFinite(point.BandPassPeak));
			Assert.True(double.IsFinite(point.HighPassPeak));
			Assert.True(double.IsFinite(point.EstimatedRingingFrequencyHz));
			Assert.True(double.IsFinite(point.RingDecayRatio));
			Assert.True(double.IsFinite(point.LowToBandPeakRatio));
			Assert.True(double.IsFinite(point.HighToBandPeakRatio));
			Assert.InRange(point.SaturationOrPlateauCount, 0, point.Request.MeasuredCycles / 8);
			Assert.True(point.FilterCutoffHz >= previousCutoff);
			Assert.InRange(point.Peak, 0.0, 0.999);
			previousCutoff = point.FilterCutoffHz;
		}

		Assert.InRange(points[0].FilterCutoffHz, 180.0, 260.0);
		Assert.True(points[^1].FilterCutoffHz > 8000.0);
		Assert.True(points[^1].MeanAbsDelta > points[0].MeanAbsDelta, $"Expected open low-pass sweep to have more absolute motion, closed {points[0].MeanAbsDelta:0.000000}, open {points[^1].MeanAbsDelta:0.000000}.");
	}

	[Fact]
	public void CharacterizationIsDeterministicForSameRegisterScript()
	{
		var request = new SidFilterCharacterizationRequest(
			SidChipModel.Mos6581,
			SidFilterProfileId.Mos6581Balanced,
			cutoffRegister: 0x560,
			resonanceNibble: 12,
			filterMode: 0x70,
			routedVoiceCount: 2,
			inputLevel: 0.85,
			frequency: 0x0C00);

		var first = SidFilterCharacterizer.Measure(request);
		var second = SidFilterCharacterizer.Measure(request);

		Assert.Equal(first.FilterCutoffHz, second.FilterCutoffHz, precision: 12);
		Assert.Equal(first.FilterDamping, second.FilterDamping, precision: 12);
		Assert.Equal(first.Dc, second.Dc, precision: 12);
		Assert.Equal(first.Rms, second.Rms, precision: 12);
		Assert.Equal(first.AcRms, second.AcRms, precision: 12);
		Assert.Equal(first.Peak, second.Peak, precision: 12);
		Assert.Equal(first.PeakToPeak, second.PeakToPeak, precision: 12);
		Assert.Equal(first.ZeroCrossingRate, second.ZeroCrossingRate, precision: 12);
		Assert.Equal(first.MeanAbsDelta, second.MeanAbsDelta, precision: 12);
		Assert.Equal(first.Brightness, second.Brightness, precision: 12);
		Assert.Equal(first.LowPassPeak, second.LowPassPeak, precision: 12);
		Assert.Equal(first.BandPassPeak, second.BandPassPeak, precision: 12);
		Assert.Equal(first.HighPassPeak, second.HighPassPeak, precision: 12);
		Assert.Equal(first.EstimatedRingingFrequencyHz, second.EstimatedRingingFrequencyHz, precision: 12);
		Assert.Equal(first.RingDecayRatio, second.RingDecayRatio, precision: 12);
		Assert.Equal(first.SaturationOrPlateauCount, second.SaturationOrPlateauCount);
		Assert.Equal(first.LowToBandPeakRatio, second.LowToBandPeakRatio, precision: 12);
		Assert.Equal(first.HighToBandPeakRatio, second.HighToBandPeakRatio, precision: 12);
	}

	[Fact]
	public void CharacterizationCapturesNonlinearSignalLevelResponse()
	{
		var quiet = SidFilterCharacterizer.Measure(new SidFilterCharacterizationRequest(
			SidChipModel.Mos6581,
			SidFilterProfileId.Mos6581Balanced,
			cutoffRegister: 0x420,
			resonanceNibble: 10,
			filterMode: 0x10,
			inputLevel: 0.35,
			frequency: 0x1000));
		var loud = SidFilterCharacterizer.Measure(new SidFilterCharacterizationRequest(
			SidChipModel.Mos6581,
			SidFilterProfileId.Mos6581Balanced,
			cutoffRegister: 0x420,
			resonanceNibble: 10,
			filterMode: 0x10,
			inputLevel: 1.0,
			frequency: 0x1000));

		Assert.True(loud.AcRms > quiet.AcRms, $"Expected louder input to increase AC RMS, quiet {quiet.AcRms:0.000}, loud {loud.AcRms:0.000}.");
		Assert.True(loud.LowPassPeak < quiet.LowPassPeak * 4.0, $"Expected analog path to compress louder low-pass state, quiet {quiet.LowPassPeak:0.000}, loud {loud.LowPassPeak:0.000}.");
	}

	[Fact]
	public void ResonanceSweepRaisesBandPassEnergyWithoutRunawayPlateaus()
	{
		var points = SidFilterCharacterizer.SweepResonance(
			SidChipModel.Mos6581,
			SidFilterProfileId.Mos6581Balanced,
			cutoffRegister: 0x700,
			new[] { 0, 4, 8, 12, 15 },
			filterMode: 0x20,
			frequency: 0x0800);

		for (var i = 1; i < points.Length; i++)
		{
			Assert.True(points[i].BandPassPeak >= points[i - 1].BandPassPeak * 0.92, $"Expected resonance sweep to preserve/increase band-pass peak at index {i}, previous {points[i - 1].BandPassPeak:0.000}, current {points[i].BandPassPeak:0.000}.");
		}

		Assert.True(points[^1].BandPassPeak > points[0].BandPassPeak * 1.08, $"Expected high resonance to raise band-pass energy, low {points[0].BandPassPeak:0.000}, high {points[^1].BandPassPeak:0.000}.");
		Assert.InRange(points[^1].SaturationOrPlateauCount, 0, points[^1].Request.MeasuredCycles / 10);
		Assert.InRange(points[^1].RingDecayRatio, 0.05, 8.0);
	}

	[Fact]
	public void HighResonanceRingingEstimateTracksCutoff()
	{
		var low = SidFilterCharacterizer.Measure(new SidFilterCharacterizationRequest(
			SidChipModel.Mos6581,
			SidFilterProfileId.Mos6581Balanced,
			cutoffRegister: 0x300,
			resonanceNibble: 15,
			filterMode: 0x20,
			frequency: 0x0200));
		var mid = SidFilterCharacterizer.Measure(new SidFilterCharacterizationRequest(
			SidChipModel.Mos6581,
			SidFilterProfileId.Mos6581Balanced,
			cutoffRegister: 0x800,
			resonanceNibble: 15,
			filterMode: 0x20,
			frequency: 0x0200));
		var high = SidFilterCharacterizer.Measure(new SidFilterCharacterizationRequest(
			SidChipModel.Mos6581,
			SidFilterProfileId.Mos6581Balanced,
			cutoffRegister: 0xD00,
			resonanceNibble: 15,
			filterMode: 0x20,
			frequency: 0x0200));

		Assert.True(mid.EstimatedRingingFrequencyHz >= low.EstimatedRingingFrequencyHz, $"Expected ringing estimate to rise from low to mid cutoff, low {low.EstimatedRingingFrequencyHz:0.0}, mid {mid.EstimatedRingingFrequencyHz:0.0}.");
		Assert.True(high.EstimatedRingingFrequencyHz >= mid.EstimatedRingingFrequencyHz, $"Expected ringing estimate to rise from mid to high cutoff, mid {mid.EstimatedRingingFrequencyHz:0.0}, high {high.EstimatedRingingFrequencyHz:0.0}.");
	}

	[Fact]
	public void HighResonanceModesRemainBalancedAndDistinct()
	{
		var lowPass = SidFilterCharacterizer.Measure(new SidFilterCharacterizationRequest(
			SidChipModel.Mos6581,
			SidFilterProfileId.Mos6581Balanced,
			cutoffRegister: 0x780,
			resonanceNibble: 15,
			filterMode: 0x10,
			frequency: 0x0800));
		var bandPass = SidFilterCharacterizer.Measure(new SidFilterCharacterizationRequest(
			SidChipModel.Mos6581,
			SidFilterProfileId.Mos6581Balanced,
			cutoffRegister: 0x780,
			resonanceNibble: 15,
			filterMode: 0x20,
			frequency: 0x0800));
		var highPass = SidFilterCharacterizer.Measure(new SidFilterCharacterizationRequest(
			SidChipModel.Mos6581,
			SidFilterProfileId.Mos6581Balanced,
			cutoffRegister: 0x780,
			resonanceNibble: 15,
			filterMode: 0x40,
			frequency: 0x0800));

		Assert.True(Math.Abs(lowPass.AcRms - bandPass.AcRms) > 0.001 || Math.Abs(bandPass.AcRms - highPass.AcRms) > 0.001);
		Assert.InRange(bandPass.LowToBandPeakRatio, 0.01, 20.0);
		Assert.InRange(bandPass.HighToBandPeakRatio, 0.01, 20.0);
		Assert.InRange(lowPass.SaturationOrPlateauCount + bandPass.SaturationOrPlateauCount + highPass.SaturationOrPlateauCount, 0, lowPass.Request.MeasuredCycles / 4);
	}
}
