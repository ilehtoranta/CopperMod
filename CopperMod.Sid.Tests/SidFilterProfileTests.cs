namespace CopperMod.Sid.Tests;

public sealed class SidFilterProfileTests
{
	[Fact]
	public void AutoProfileSelectsBalanced6581ByDefault()
	{
		var chip = CreateFilteredPulse(SidChipModel.Mos6581, SidFilterProfileId.Auto);
		chip.Render(1);

		Assert.Equal(SidFilterProfileId.Mos6581Balanced, chip.DebugState.FilterProfile);
	}

	[Fact]
	public void AutoProfileSelectsReferenceMeasured6581WhenRequested()
	{
		var chip = new SidChip(
			SidChipModel.Mos6581,
			SidConstants.DefaultSidBaseAddress,
			filterProfile: SidFilterProfileId.Auto,
			sidEmulationProfile: SidEmulationProfile.ReferenceMeasured);
		chip.Write(0x15, 0x00);
		chip.Write(0x16, 0x80);
		chip.Write(0x17, 0x01);
		chip.Write(0x18, 0x1F);
		chip.Render(1);

		Assert.Equal(SidFilterProfileId.Mos6581ReferenceMeasured, chip.DebugState.FilterProfile);
	}

	[Fact]
	public void AutoProfileSelectsLinear8580For8580Model()
	{
		var chip = CreateFilteredPulse(SidChipModel.Mos8580, SidFilterProfileId.Auto);
		chip.Render(1);

		Assert.Equal(SidFilterProfileId.Mos8580Linear, chip.DebugState.FilterProfile);
	}

	[Fact]
	public void DataSheetProfileMapsCutoffRegisterToDocumentedRange()
	{
		var chip = new SidChip(SidChipModel.Mos6581, SidConstants.DefaultSidBaseAddress, filterProfile: SidFilterProfileId.Mos6581DataSheet);
		chip.Write(0x15, 0xF8);
		chip.Write(0x16, 0x00);
		chip.Write(0x17, 0x01);
		chip.Write(0x18, 0x1F);
		chip.Render(1);

		Assert.Equal(0, chip.DebugState.FilterCutoffRegister);
		Assert.InRange(chip.DebugState.FilterCutoffHz, 180.0, 260.0);

		chip.Write(0x15, 0xFF);
		chip.Write(0x16, 0xFF);
		chip.Render(1);

		Assert.Equal(2047, chip.DebugState.FilterCutoffRegister);
		Assert.InRange(chip.DebugState.FilterCutoffHz, 9999.9, 10000.1);
	}

	[Fact]
	public void FilterProfilesProduceDistinctCutoffCurves()
	{
		var dataSheet = ReadCutoff(SidChipModel.Mos6581, SidFilterProfileId.Mos6581DataSheet, cutoffHigh: 0x80);
		var balanced = ReadCutoff(SidChipModel.Mos6581, SidFilterProfileId.Mos6581Balanced, cutoffHigh: 0x80);
		var darkR3 = ReadCutoff(SidChipModel.Mos6581, SidFilterProfileId.Mos6581DarkR3, cutoffHigh: 0x80);
		var linear8580 = ReadCutoff(SidChipModel.Mos8580, SidFilterProfileId.Mos8580Linear, cutoffHigh: 0x80);

		Assert.True(linear8580 > balanced, $"Expected linear 8580 midpoint to be brighter than balanced 6581, 8580 {linear8580:0.0}, balanced {balanced:0.0}.");
		Assert.True(balanced > dataSheet, $"Expected balanced 6581 midpoint to be brighter than 6581 datasheet, balanced {balanced:0.0}, data {dataSheet:0.0}.");
		Assert.True(dataSheet > darkR3, $"Expected datasheet midpoint to be brighter than dark R3, data {dataSheet:0.0}, dark {darkR3:0.0}.");
	}

	[Fact]
	public void Balanced6581LowCutoffKeepsResonantPulseAudible()
	{
		var chip = CreateFilteredPulse(
			SidChipModel.Mos6581,
			SidFilterProfileId.Mos6581Balanced,
			cutoffHigh: 0x00,
			resonance: 0xF0,
			mode: 0x10,
			frequency: 0x0D0A);
		chip.Write(0x02, 0x00);
		chip.Write(0x03, 0x05);

		var samples = CollectSamples(chip, warmupCycles: 48000, measuredCycles: 48000);
		var acRms = AcRms(samples);

		Assert.InRange(chip.DebugState.FilterCutoffHz, 150.0, 220.0);
		Assert.InRange(acRms, 0.050, 0.350);
	}

	[Fact]
	public void Balanced6581FilteredVoiceLeakageHasLowCutoffFloor()
	{
		var profile = SidFilterProfileDefinition.Resolve(SidChipModel.Mos6581, SidFilterProfileId.Mos6581Balanced);
		var closedLeakage = profile.MapFilterVoiceLeakageGain(0);
		var openLeakage = profile.MapFilterVoiceLeakageGain(2047);

		Assert.InRange(closedLeakage, 0.001, openLeakage);
		Assert.True(openLeakage > closedLeakage);
	}

	[Fact]
	public void Measured6581ProfilesKeepIndependentModeGainCalibration()
	{
		var balanced = SidFilterProfileDefinition.Resolve(SidChipModel.Mos6581, SidFilterProfileId.Mos6581Balanced);
		var reference = SidFilterProfileDefinition.Resolve(SidChipModel.Mos6581, SidFilterProfileId.Mos6581ReferenceMeasured);

		Assert.True(balanced.UsesAnalog6581Filter);
		Assert.True(reference.UsesAnalog6581Filter);
		Assert.InRange(balanced.LowPassGain, 1.48, 1.50);
		Assert.Equal(1.20, reference.LowPassGain, precision: 12);
		Assert.Equal(0.70, balanced.BandPassGain, precision: 12);
		Assert.Equal(0.84, reference.BandPassGain, precision: 12);
		Assert.Equal(1.0, balanced.HighPassGain, precision: 12);
		Assert.Equal(0.45, reference.HighPassGain, precision: 12);
	}

	[Theory]
	[InlineData((int)SidFilterProfileId.Mos6581DataSheet)]
	[InlineData((int)SidFilterProfileId.Mos6581Balanced)]
	[InlineData((int)SidFilterProfileId.Mos6581ReferenceMeasured)]
	[InlineData((int)SidFilterProfileId.Mos6581DarkR3)]
	public void Mos6581ProfilesUseFullCutoffAndResonanceTables(int profileValue)
	{
		var profileId = (SidFilterProfileId)profileValue;
		var profile = SidFilterProfileDefinition.Resolve(SidChipModel.Mos6581, profileId);

		Assert.True(profile.UsesNonlinearFilter);
		Assert.True(profile.UsesAnalog6581Filter);
		Assert.Equal(2048, profile.CutoffTableLength);
		Assert.Equal(16, profile.ResonanceTableLength);
		Assert.True(profile.FilterDrive > 1.0);
		Assert.True(profile.CutoffSignalModulation > 0.0);

		var previousCutoff = profile.MapCutoff(0);
		Assert.True(double.IsFinite(previousCutoff));
		for (var register = 1; register < profile.CutoffTableLength; register++)
		{
			var cutoff = profile.MapCutoff(register);
			Assert.True(double.IsFinite(cutoff));
			Assert.True(cutoff >= previousCutoff, $"{profileId} cutoff table reg {register} moved backward from {previousCutoff:0.000} to {cutoff:0.000}.");
			previousCutoff = cutoff;
		}

		var previousDamping = profile.MapDamping(0);
		for (var resonance = 1; resonance < profile.ResonanceTableLength; resonance++)
		{
			var damping = profile.MapDamping(resonance);
			Assert.True(double.IsFinite(damping));
			Assert.True(damping <= previousDamping, $"{profileId} damping table resonance {resonance} moved upward from {previousDamping:0.000} to {damping:0.000}.");
			previousDamping = damping;
		}
	}

	[Fact]
	public void Mos6581AnalogTablesAreFiniteBoundedAndKinked()
	{
		var profile = SidFilterProfileDefinition.Resolve(SidChipModel.Mos6581, SidFilterProfileId.Mos6581Balanced);
		var model = Assert.IsType<SidMos6581AnalogModel>(profile.Analog6581Model);
		var network = model.ResistorNetwork;
		var cutoffCircuit = model.CutoffCircuit;
		var outputCircuit = model.OutputCircuit;

		Assert.Equal(2048, model.CutoffDacTableLength);
		Assert.Equal(4096, model.OpAmpTableLength);
		Assert.Equal(4, model.MixerGainTableLength);
		Assert.Equal(16, model.ResonanceFeedbackTableLength);
		Assert.Equal(2048, model.ResonanceFeedbackCutoffTableLength);
		Assert.Equal(128, model.SummerGainTableLength);
		Assert.Equal(128, model.OutputGainTableLength);
		Assert.InRange(network.R1Ohms, 63_900.0, 64_100.0);
		Assert.InRange(network.R2Ohms / network.R1Ohms, 1.99, 2.01);
		Assert.InRange(network.R6Ohms / network.R1Ohms, 5.99, 6.01);
		Assert.InRange(network.R8Ohms / network.R1Ohms, 7.99, 8.01);
		Assert.InRange(network.R24Ohms / network.R1Ohms, 23.0, 24.0);
		Assert.InRange(network.OutputExternalOhms, 999.0, 1001.0);
		Assert.InRange(cutoffCircuit.MinimumCutoffHz, 209.9, 210.1);
		Assert.InRange(cutoffCircuit.FullScaleCutoffHz, 10999.0, 11001.0);
		Assert.InRange(cutoffCircuit.CapacitorFarads, 469e-12, 471e-12);
		Assert.InRange(cutoffCircuit.DacTwoRDivR, 1.878, 1.880);
		Assert.False(cutoffCircuit.DacTerminated);
		Assert.InRange(cutoffCircuit.ThresholdVoltage, 1.30, 1.32);
		Assert.InRange(cutoffCircuit.MobilityCox, 19e-6, 21e-6);
		Assert.InRange(cutoffCircuit.VcrWidthLength, 8.9, 9.1);
		Assert.InRange(outputCircuit.WorkingPointVoltage, 4.53, 4.55);
		Assert.InRange(outputCircuit.OutputSignalGain, 0.41, 0.43);
		Assert.InRange(outputCircuit.OutputSoftClipAmount, 0.19, 0.21);
		Assert.InRange(outputCircuit.OutputLowPassCutoffHz, 11_999.0, 12_001.0);
		Assert.InRange(model.CutoffHz[0], 209.9, 210.1);
		Assert.InRange(model.CutoffHz[^1], 10999.0, 11001.0);
		Assert.InRange(model.MapOpAmp(0.0), -0.001, 0.001);
		Assert.InRange(model.MapReverseOpAmp(0.0), -0.05, 0.05);
		Assert.True(model.MapOpAmp(-0.5) < model.MapOpAmp(0.0));
		Assert.True(model.MapOpAmp(0.5) > model.MapOpAmp(0.0));
		Assert.All(
			new[]
			{
				model.MapMixerGain(0),
				model.MapMixerGain(3),
				model.MapResonanceFeedbackScale(0),
				model.MapResonanceFeedbackScale(15),
				model.MapResonanceFeedbackScale(15, 0),
				model.MapResonanceFeedbackScale(15, 2047),
				model.MapResonanceOutputScale(15, 2047),
				model.MapSummerGain(0x70),
				model.MapOutputGain(0x70)
			},
			value => Assert.True(double.IsFinite(value)));
		Assert.True(model.MapMixerGain(3) < model.MapMixerGain(1), $"Expected resistor loading to reduce three-routed-voice mixer gain, one {model.MapMixerGain(1):0.000}, three {model.MapMixerGain(3):0.000}.");
		Assert.True(model.MapSummerGain(0x70) < model.MapSummerGain(0x10), $"Expected multi-mode summer loading to reduce gain, one {model.MapSummerGain(0x10):0.000}, three {model.MapSummerGain(0x70):0.000}.");
		Assert.True(model.MapOutputGain(0x70) < model.MapOutputGain(0x10), $"Expected output loading to reduce multi-mode output gain, one {model.MapOutputGain(0x10):0.000}, three {model.MapOutputGain(0x70):0.000}.");
		Assert.True(model.MapResonanceOutputScale(15, 1024) > model.MapResonanceOutputScale(0, 1024));
		Assert.NotEqual(model.MapResonanceFeedbackScale(15, 0), model.MapResonanceFeedbackScale(15, 2047), precision: 4);

		var lowControl = model.MapCutoffControlVoltage(0);
		var midControl = model.MapCutoffControlVoltage(1024);
		var highControl = model.MapCutoffControlVoltage(2047);
		Assert.All(new[] { lowControl, midControl, highControl }, value => Assert.True(double.IsFinite(value)));
		Assert.True(lowControl < midControl);
		Assert.True(midControl < highControl);

		var linearMidpoint = lowControl + ((highControl - lowControl) * 1024.0 / 2047.0);
		Assert.True(Math.Abs(midControl - linearMidpoint) > 0.005, $"Expected 6581 DAC control voltage to show a non-linear kink, midpoint delta {midControl - linearMidpoint:0.000}.");
		var normalizedMid = (midControl - lowControl) / (highControl - lowControl);
		var forcedMsbMid = NormalizedForcedMsbDacCode(1024, cutoffCircuit.DacTwoRDivR, cutoffCircuit.DacTerminated);
		Assert.True(
			Math.Abs(normalizedMid - forcedMsbMid) < 0.000001,
			$"Expected cutoff DAC midpoint {normalizedMid:0.000000} to follow forced-MSB physical shape {forcedMsbMid:0.000000}.");

		var linearCutoffMidpoint = model.CutoffHz[0] + ((model.CutoffHz[^1] - model.CutoffHz[0]) * 1024.0 / 2047.0);
		Assert.True(Math.Abs(model.CutoffHz[1024] - linearCutoffMidpoint) > 1000.0, $"Expected VCR-derived cutoff to be strongly non-linear, midpoint delta {model.CutoffHz[1024] - linearCutoffMidpoint:0.0} Hz.");
	}

	[Fact]
	public void ReferenceMeasured6581OutputLowPassKeepsMeasuredD418TransitionsFast()
	{
		var balanced = SidFilterProfileDefinition.Resolve(SidChipModel.Mos6581, SidFilterProfileId.Mos6581Balanced);
		var reference = SidFilterProfileDefinition.Resolve(SidChipModel.Mos6581, SidFilterProfileId.Mos6581ReferenceMeasured);
		var balancedModel = Assert.IsType<SidMos6581AnalogModel>(balanced.Analog6581Model);
		var referenceModel = Assert.IsType<SidMos6581AnalogModel>(reference.Analog6581Model);

		Assert.InRange(balancedModel.OutputCircuit.OutputLowPassCutoffHz, 11_999.0, 12_001.0);
		Assert.InRange(referenceModel.OutputCircuit.OutputLowPassCutoffHz, 27_999.0, 28_001.0);
	}

	[Fact]
	public void Mos6581VoltageDomainNodesStayWithinOpAmpRails()
	{
		var profile = SidFilterProfileDefinition.Resolve(SidChipModel.Mos6581, SidFilterProfileId.Mos6581Balanced);
		var model = Assert.IsType<SidMos6581AnalogModel>(profile.Analog6581Model);
		var filter = new SidMos6581AnalogFilter(profile, SidConstants.PalCpuCyclesPerSecond);
		foreach (var cutoff in new[] { 0x000, 0x180, 0x780, 0xD00, 0x7FF })
		{
			foreach (var resonance in new[] { 0, 8, 15 })
			{
				foreach (var mode in new[] { 0x10, 0x20, 0x40, 0x70 })
				{
					filter.Reset();
					var outputVoltage = model.WorkingPointVoltage;
					for (var cycle = 0; cycle < 2048; cycle++)
					{
						var drive = (cycle & 0x20) == 0 ? 1.15 : -1.05;
						outputVoltage = filter.Process(
							drive,
							-drive * 0.65,
							drive * 0.40,
							filterRouting: 0x07,
							filterMode: mode,
							voice3Muted: false,
							cutoffRegister: cutoff,
							resonanceNibble: resonance);
					}

					var finalVoltage = filter.ApplyOutputStageVoltage(
						outputVoltage,
						volumeGain: 1.0,
						volumeOffsetSample: SidAnalog.VolumeOffset(15, SidChipModel.Mos6581),
						volumeTransientSample: 0.0);
					AssertVoltageInRails(model, filter.LastLowPassVoltage);
					AssertVoltageInRails(model, filter.LastBandPassVoltage);
					AssertVoltageInRails(model, filter.LastHighPassVoltage);
					AssertVoltageInRails(model, outputVoltage);
					AssertVoltageInRails(model, finalVoltage);
					Assert.InRange(filter.LastLowPass, -1.8, 1.8);
					Assert.InRange(filter.LastBandPass, -1.8, 1.8);
					Assert.InRange(filter.LastHighPass, -1.8, 1.8);
					Assert.InRange(filter.OutputVoltageToSample(finalVoltage), -0.999, 0.999);
				}
			}
		}
	}

	[Fact]
	public void Mos6581DryOutputMixDoesNotClampAtFilterNodeBeforeVolume()
	{
		var profile = SidFilterProfileDefinition.Resolve(SidChipModel.Mos6581, SidFilterProfileId.Mos6581Balanced);
		var filter = new SidMos6581AnalogFilter(profile, SidConstants.PalCpuCyclesPerSecond);

		var singleVoice = RenderDryOutput(filter, voice1: 1.0, voice2: 0.0, voice3: 0.0);
		filter.Reset();
		var threeVoices = RenderDryOutput(filter, voice1: 1.0, voice2: 1.0, voice3: 1.0);

		Assert.True(threeVoices > singleVoice * 1.8, $"Expected dry voice summing to reach the output stage before clipping, single {singleVoice:0.000}, three {threeVoices:0.000}.");
		Assert.InRange(threeVoices, 0.90, 0.999);
	}

	[Fact]
	public void Mos8580ProfileKeepsLinearCutoffAndDampingFormula()
	{
		var profile = SidFilterProfileDefinition.Resolve(SidChipModel.Mos8580, SidFilterProfileId.Mos8580Linear);

		Assert.False(profile.UsesNonlinearFilter);
		Assert.Equal(2048, profile.CutoffTableLength);
		Assert.Equal(16, profile.ResonanceTableLength);
		foreach (var register in new[] { 0, 1, 64, 512, 1024, 1536, 2047 })
		{
			var expected = 35.0 + (Math.Pow(register / 2047.0, 1.10) * (14500.0 - 35.0));
			Assert.Equal(expected, profile.MapCutoff(register), precision: 12);
		}

		foreach (var resonance in new[] { 0, 7, 15 })
		{
			var expected = Math.Clamp(1.62 - ((resonance / 15.0) * 1.10), 0.42, 1.95);
			Assert.Equal(expected, profile.MapDamping(resonance), precision: 12);
		}
	}

	[Fact]
	public void PalAndNtscClockedFiltersStayFinite()
	{
		var pal = CreateFilteredPulse(SidChipModel.Mos6581, SidFilterProfileId.Mos6581Balanced, SidConstants.PalCpuCyclesPerSecond);
		var ntsc = CreateFilteredPulse(SidChipModel.Mos6581, SidFilterProfileId.Mos6581Balanced, SidConstants.NtscCpuCyclesPerSecond);

		var palSamples = CollectSamples(pal, warmupCycles: 4000, measuredCycles: 2000);
		var ntscSamples = CollectSamples(ntsc, warmupCycles: 4000, measuredCycles: 2000);

		Assert.All(palSamples.Concat(ntscSamples), sample => Assert.True(double.IsFinite(sample)));
		Assert.True(MeasureRange(palSamples) > 0.01);
		Assert.True(MeasureRange(ntscSamples) > 0.01);
	}

	[Fact]
	public void ResonanceNibbleChangesDampingMonotonically()
	{
		var lowResonance = ReadDamping(resonance: 0x00);
		var midResonance = ReadDamping(resonance: 0x80);
		var highResonance = ReadDamping(resonance: 0xF0);

		Assert.True(lowResonance > midResonance);
		Assert.True(midResonance > highResonance);
	}

	[Fact]
	public void FilterModeBitsAreAdditive()
	{
		var chip = CreateFilteredPulse(SidChipModel.Mos6581, SidFilterProfileId.Mos6581Balanced);
		chip.Write(0x18, 0x7F);
		chip.Render(1);

		Assert.Equal(0x70, chip.DebugState.FilterMode);
	}

	[Fact]
	public void MixerFilterPipelineIsDeterministicAcrossDynamicRegisterChanges()
	{
		var expected = CreateDynamicFilterPipelineChip();
		var actual = CreateDynamicFilterPipelineChip();
		var checkpoints = new[]
		{
			0, 1, 2, 3, 4, 5, 31, 63, 127, 255, 383, 384, 511, 767, 768,
			895, 1023, 1151, 1152, 1279, 1535, 1536, 1791, 1792, 2047,
			2303, 2559, 3071, 4095
		};
		var checkpointIndex = 0;
		var minimum = double.MaxValue;
		var maximum = double.MinValue;
		for (var cycle = 0; cycle <= 4095; cycle++)
		{
			if (cycle == 384)
			{
				expected.Write(0x17, 0xF5);
				actual.Write(0x17, 0xF5);
			}

			if (cycle == 768)
			{
				expected.Write(0x18, 0x9F);
				actual.Write(0x18, 0x9F);
			}

			if (cycle == 1152)
			{
				expected.Write(0x16, 0x20);
				actual.Write(0x16, 0x20);
			}

			if (cycle == 1536)
			{
				expected.Write(0x18, 0x0F);
				actual.Write(0x18, 0x0F);
			}

			if (cycle == 1792)
			{
				expected.Write(0x18, 0x5F);
				actual.Write(0x18, 0x5F);
			}

			var expectedSample = expected.Render(1);
			var actualSample = actual.Render(1);
			Assert.True(double.IsFinite(actualSample));
			Assert.InRange(actualSample, -0.999, 0.999);
			minimum = Math.Min(minimum, actualSample);
			maximum = Math.Max(maximum, actualSample);
			if (checkpointIndex < checkpoints.Length && cycle == checkpoints[checkpointIndex])
			{
				var delta = Math.Abs(actualSample - expectedSample);
				Assert.True(delta <= 1e-12, $"Cycle {cycle} expected {expectedSample:R}, got {actualSample:R}, delta {delta:R}.");
				checkpointIndex++;
			}
		}

		Assert.Equal(checkpoints.Length, checkpointIndex);
		Assert.True(maximum - minimum > 0.25, $"Expected dynamic filtered pipeline to stay audibly active, range {maximum - minimum:0.000}.");
	}

	[Fact]
	public void Mos6581FilterResponseChangesWithSignalLevel()
	{
		var singleVoice = CreateRoutedPulseVoiceCount(voiceCount: 1);
		var doubledVoice = CreateRoutedPulseVoiceCount(voiceCount: 2);

		var singlePeak = MeasureLowPassPeak(singleVoice, warmupCycles: 12000, measuredCycles: 16000);
		var doubledPeak = MeasureLowPassPeak(doubledVoice, warmupCycles: 12000, measuredCycles: 16000);

		Assert.True(doubledPeak > singlePeak * 1.10, $"Expected louder routed input to increase filter state, single {singlePeak:0.000}, doubled {doubledPeak:0.000}.");
		Assert.True(doubledPeak < singlePeak * 2.0, $"Expected nonlinear 6581 filter drive to compress doubled input, single {singlePeak:0.000}, doubled {doubledPeak:0.000}.");
	}

	[Fact]
	public void Mos8580RenderStaysLinearWhen6581ProfileIsRequested()
	{
		var linear = CreateFilteredPulse(SidChipModel.Mos8580, SidFilterProfileId.Mos8580Linear);
		var requested6581 = CreateFilteredPulse(SidChipModel.Mos8580, SidFilterProfileId.Mos6581DarkR3);
		var linearSamples = CollectSamples(linear, warmupCycles: 4000, measuredCycles: 4096);
		var requestedSamples = CollectSamples(requested6581, warmupCycles: 4000, measuredCycles: 4096);

		Assert.Equal(SidFilterProfileId.Mos8580Linear, linear.DebugState.FilterProfile);
		Assert.Equal(SidFilterProfileId.Mos8580Linear, requested6581.DebugState.FilterProfile);
		for (var i = 0; i < linearSamples.Length; i++)
		{
			Assert.Equal(linearSamples[i], requestedSamples[i], precision: 12);
		}
	}

	[Fact]
	public void FilterRoutingWorksIndependentlyPerVoice()
	{
		var voice1 = MeasureRange(CollectSamples(CreateSingleRoutedVoice(voice: 0), warmupCycles: 10000, measuredCycles: 12000));
		var voice2 = MeasureRange(CollectSamples(CreateSingleRoutedVoice(voice: 1), warmupCycles: 10000, measuredCycles: 12000));
		var voice3 = MeasureRange(CollectSamples(CreateSingleRoutedVoice(voice: 2), warmupCycles: 10000, measuredCycles: 12000));

		Assert.True(voice1 > 0.02, $"Expected routed voice 1 to be audible, range {voice1:0.000}.");
		Assert.True(voice2 > 0.02, $"Expected routed voice 2 to be audible, range {voice2:0.000}.");
		Assert.True(voice3 > 0.02, $"Expected routed voice 3 to be audible, range {voice3:0.000}.");
	}

	[Fact]
	public void RoutedVoiceWithNoFilterOutputModeIsSilentExceptVolumeDac()
	{
		var routed = CreateFilteredPulse(SidChipModel.Mos6581, SidFilterProfileId.Mos6581Balanced);
		routed.Write(0x18, 0x0F);
		var unrouted = CreateFilteredPulse(SidChipModel.Mos6581, SidFilterProfileId.Mos6581Balanced);
		unrouted.Write(0x17, 0x00);
		unrouted.Write(0x18, 0x0F);

		var routedRange = MeasureRange(CollectSamples(routed, warmupCycles: 10000, measuredCycles: 12000));
		var unroutedRange = MeasureRange(CollectSamples(unrouted, warmupCycles: 10000, measuredCycles: 12000));

		Assert.True(routedRange < unroutedRange * 0.25, $"Expected routed/no-mode voice to disappear from audio output, routed {routedRange:0.000}, unrouted {unroutedRange:0.000}.");
	}

	[Theory]
	[InlineData(1, 0x10)]
	[InlineData(1, 0x20)]
	[InlineData(1, 0x30)]
	[InlineData(1, 0x40)]
	[InlineData(1, 0x50)]
	[InlineData(1, 0x60)]
	[InlineData(1, 0x70)]
	[InlineData(2, 0x10)]
	[InlineData(2, 0x20)]
	[InlineData(2, 0x30)]
	[InlineData(2, 0x40)]
	[InlineData(2, 0x50)]
	[InlineData(2, 0x60)]
	[InlineData(2, 0x70)]
	public void FilterStateAdvancesWhileOutputModesAreDisconnected(int modelValue, int audibleMode)
	{
		var model = (SidChipModel)modelValue;
		var hidden = CreateFilteredPulse(model, SidFilterProfileId.Auto, mode: 0x00, cutoffHigh: 0x88, resonance: 0xA0);
		var audible = CreateFilteredPulse(model, SidFilterProfileId.Auto, mode: (byte)audibleMode, cutoffHigh: 0x88, resonance: 0xA0);

		CollectSamples(hidden, warmupCycles: 0, measuredCycles: 4096);
		CollectSamples(audible, warmupCycles: 0, measuredCycles: 4096);

		AssertFilterStateClose(audible.DebugState, hidden.DebugState);
	}

	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	public void ResidualFilterStateDecaysAcrossRoutingAndModeChanges(int modelValue)
	{
		var model = (SidChipModel)modelValue;
		var hidden = CreateFilteredPulse(model, SidFilterProfileId.Auto, mode: 0x10, cutoffHigh: 0x78, resonance: 0xC0);
		var audible = CreateFilteredPulse(model, SidFilterProfileId.Auto, mode: 0x10, cutoffHigh: 0x78, resonance: 0xC0);
		CollectSamples(hidden, warmupCycles: 0, measuredCycles: 4096);
		CollectSamples(audible, warmupCycles: 0, measuredCycles: 4096);

		hidden.Write(0x17, 0xC0);
		hidden.Write(0x18, 0x0F);
		audible.Write(0x17, 0xC0);
		audible.Write(0x18, 0x1F);
		CollectSamples(hidden, warmupCycles: 0, measuredCycles: 512);
		CollectSamples(audible, warmupCycles: 0, measuredCycles: 512);

		AssertFilterStateClose(audible.DebugState, hidden.DebugState);
	}

	[Fact]
	public void ExternalInputRoutingIsZeroForNow()
	{
		var noExternal = new SidChip(SidChipModel.Mos6581, SidConstants.DefaultSidBaseAddress);
		noExternal.Write(0x17, 0x00);
		noExternal.Write(0x18, 0x1F);
		var external = new SidChip(SidChipModel.Mos6581, SidConstants.DefaultSidBaseAddress);
		external.Write(0x17, 0x08);
		external.Write(0x18, 0x1F);

		var noExternalSamples = CollectSamples(noExternal, warmupCycles: 1000, measuredCycles: 256);
		var externalSamples = CollectSamples(external, warmupCycles: 1000, measuredCycles: 256);

		for (var i = 0; i < noExternalSamples.Length; i++)
		{
			Assert.Equal(noExternalSamples[i], externalSamples[i], precision: 12);
		}

		Assert.Equal(0x08, external.DebugState.ForwardedRegisters[0x17] & 0x08);
	}

	[Fact]
	public void LowPassOpensAsCutoffIncreases()
	{
		var lowCutoff = CreateFilteredPulse(SidChipModel.Mos6581, SidFilterProfileId.Mos6581Balanced, cutoffHigh: 0x08, mode: 0x10, frequency: 0x6000);
		var highCutoff = CreateFilteredPulse(SidChipModel.Mos6581, SidFilterProfileId.Mos6581Balanced, cutoffHigh: 0xF0, mode: 0x10, frequency: 0x6000);
		var lowSamples = CollectSamples(lowCutoff, warmupCycles: 12000, measuredCycles: 16000);
		var highSamples = CollectSamples(highCutoff, warmupCycles: 12000, measuredCycles: 16000);

		Assert.True(LargestAdjacentJump(highSamples) > LargestAdjacentJump(lowSamples) * 1.5);
	}

	[Fact]
	public void HighPassReducesLowFrequencyPulseEnergy()
	{
		var direct = CreatePulseVoice(frequency: 0x0200);
		var highPass = CreateFilteredPulse(SidChipModel.Mos6581, SidFilterProfileId.Mos6581Balanced, cutoffHigh: 0x90, mode: 0x40, frequency: 0x0200);
		var directRms = Rms(CollectSamples(direct, warmupCycles: 12000, measuredCycles: 65536));
		var highPassRms = Rms(CollectSamples(highPass, warmupCycles: 12000, measuredCycles: 65536));

		Assert.True(highPassRms < directRms * 0.9, $"Expected high-pass to reduce low-frequency pulse RMS, direct {directRms:0.000}, high-pass {highPassRms:0.000}.");
	}

	[Fact]
	public void BandPassIsFiniteAndDistinctFromLowAndHighPass()
	{
		var lowPass = CreateFilteredPulse(SidChipModel.Mos6581, SidFilterProfileId.Mos6581Balanced, cutoffHigh: 0x90, resonance: 0x80, mode: 0x10);
		var bandPass = CreateFilteredPulse(SidChipModel.Mos6581, SidFilterProfileId.Mos6581Balanced, cutoffHigh: 0x90, resonance: 0x80, mode: 0x20);
		var highPass = CreateFilteredPulse(SidChipModel.Mos6581, SidFilterProfileId.Mos6581Balanced, cutoffHigh: 0x90, resonance: 0x80, mode: 0x40);
		var lowRange = MeasureRange(CollectSamples(lowPass, warmupCycles: 12000, measuredCycles: 16000));
		var bandRange = MeasureRange(CollectSamples(bandPass, warmupCycles: 12000, measuredCycles: 16000));
		var highRange = MeasureRange(CollectSamples(highPass, warmupCycles: 12000, measuredCycles: 16000));

		Assert.True(double.IsFinite(lowRange));
		Assert.True(double.IsFinite(bandRange));
		Assert.True(double.IsFinite(highRange));
		Assert.True(Math.Abs(bandRange - lowRange) > 0.005 || Math.Abs(bandRange - highRange) > 0.005);
	}

	[Fact]
	public void HighResonanceRemainsFiniteAndUnclipped()
	{
		var chip = CreateFilteredPulse(SidChipModel.Mos6581, SidFilterProfileId.Mos6581DarkR3, cutoffHigh: 0x64, resonance: 0xF0, mode: 0x70);
		var samples = CollectSamples(chip, warmupCycles: 12000, measuredCycles: 16000);

		Assert.All(samples, sample => Assert.True(double.IsFinite(sample)));
		Assert.All(samples, sample => Assert.InRange(sample, -0.999, 0.999));
	}

	private static double ReadCutoff(SidChipModel model, SidFilterProfileId profile, byte cutoffHigh)
	{
		var chip = CreateFilteredPulse(model, profile, cutoffHigh: cutoffHigh);
		chip.Render(1);
		return chip.DebugState.FilterCutoffHz;
	}

	private static void AssertVoltageInRails(SidMos6581AnalogModel model, double voltage)
	{
		Assert.True(double.IsFinite(voltage));
		Assert.InRange(voltage, model.MinimumOpAmpVoltage, model.MaximumOpAmpVoltage);
	}

	private static double RenderDryOutput(SidMos6581AnalogFilter filter, double voice1, double voice2, double voice3)
	{
		var mixedVoltage = filter.Process(
			voice1,
			voice2,
			voice3,
			filterRouting: 0x00,
			filterMode: 0x00,
			voice3Muted: false,
			cutoffRegister: 0,
			resonanceNibble: 0);
		var outputVoltage = filter.ApplyOutputStageVoltage(
			mixedVoltage,
			volumeGain: 1.0,
			volumeOffsetSample: 0.0,
			volumeTransientSample: 0.0);
		return filter.OutputVoltageToSample(outputVoltage);
	}

	private static double ReadDamping(byte resonance)
	{
		var chip = CreateFilteredPulse(SidChipModel.Mos6581, SidFilterProfileId.Mos6581Balanced, resonance: resonance);
		chip.Render(1);
		return chip.DebugState.FilterDamping;
	}

	private static SidChip CreateDynamicFilterPipelineChip()
	{
		var chip = new SidChip(SidChipModel.Mos6581, SidConstants.DefaultSidBaseAddress);
		WriteVoice(chip, voice: 0, frequency: 0x1234, control: 0x41, pulseWidth: 0x0830, attackDecay: 0x00, sustainRelease: 0xF0);
		WriteVoice(chip, voice: 1, frequency: 0x0711, control: 0x21, pulseWidth: 0x0000, attackDecay: 0x00, sustainRelease: 0xF0);
		WriteVoice(chip, voice: 2, frequency: 0x0D55, control: 0x11, pulseWidth: 0x0000, attackDecay: 0x00, sustainRelease: 0xF0);
		chip.Write(0x15, 0x03);
		chip.Write(0x16, 0x60);
		chip.Write(0x17, 0xF3);
		chip.Write(0x18, 0x7F);
		return chip;
	}

	private static SidChip CreateRoutedPulseVoiceCount(int voiceCount)
	{
		var chip = new SidChip(SidChipModel.Mos6581, SidConstants.DefaultSidBaseAddress, filterProfile: SidFilterProfileId.Mos6581Balanced);
		for (var voice = 0; voice < voiceCount; voice++)
		{
			WriteVoice(chip, voice, frequency: 0x1800, control: 0x41, pulseWidth: 0x0832, attackDecay: 0x00, sustainRelease: 0xF0);
		}

		chip.Write(0x15, 0x00);
		chip.Write(0x16, 0x64);
		chip.Write(0x17, (byte)((1 << voiceCount) - 1));
		chip.Write(0x18, 0x1F);
		return chip;
	}

	private static SidChip CreateSingleRoutedVoice(int voice)
	{
		var chip = new SidChip(SidChipModel.Mos6581, SidConstants.DefaultSidBaseAddress);
		WriteVoice(chip, voice, frequency: (ushort)(0x1800 + (voice * 0x400)), control: 0x41);
		var offset = voice * 7;
		chip.Write((byte)(offset + 2), 0x00);
		chip.Write((byte)(offset + 3), 0x08);
		chip.Write((byte)(offset + 5), 0x00);
		chip.Write((byte)(offset + 6), 0xF0);
		chip.Write(0x15, 0x00);
		chip.Write(0x16, 0xC0);
		chip.Write(0x17, (byte)(1 << voice));
		chip.Write(0x18, 0x1F);
		return chip;
	}

	private static SidChip CreatePulseVoice(ushort frequency = 0x05E8)
	{
		var chip = new SidChip(SidChipModel.Mos6581, SidConstants.DefaultSidBaseAddress);
		WriteVoice(chip, voice: 0, frequency, control: 0x41);
		chip.Write(0x02, 0x32);
		chip.Write(0x03, 0x08);
		chip.Write(0x05, 0x00);
		chip.Write(0x06, 0xF0);
		chip.Write(0x18, 0x0F);
		return chip;
	}

	private static SidChip CreateFilteredPulse(
		SidChipModel model,
		SidFilterProfileId profile,
		int cpuCyclesPerSecond = SidConstants.PalCpuCyclesPerSecond,
		byte cutoffHigh = 0x90,
		byte resonance = 0x80,
		byte mode = 0x10,
		ushort frequency = 0x05E8)
	{
		var chip = new SidChip(model, SidConstants.DefaultSidBaseAddress, cpuCyclesPerSecond, profile);
		WriteVoice(chip, voice: 0, frequency, control: 0x41);
		chip.Write(0x02, 0x32);
		chip.Write(0x03, 0x08);
		chip.Write(0x05, 0x00);
		chip.Write(0x06, 0xF0);
		chip.Write(0x15, 0x00);
		chip.Write(0x16, cutoffHigh);
		chip.Write(0x17, (byte)(resonance | 0x01));
		chip.Write(0x18, (byte)(mode | 0x0F));
		return chip;
	}

	private static void WriteVoice(SidChip chip, int voice, ushort frequency, byte control)
	{
		var offset = voice * 7;
		chip.Write((byte)(offset + 0), (byte)(frequency & 0xFF));
		chip.Write((byte)(offset + 1), (byte)(frequency >> 8));
		chip.Write((byte)(offset + 4), control);
	}

	private static void WriteVoice(
		SidChip chip,
		int voice,
		ushort frequency,
		byte control,
		ushort pulseWidth,
		byte attackDecay,
		byte sustainRelease)
	{
		var offset = voice * 7;
		chip.Write((byte)(offset + 0), (byte)(frequency & 0xFF));
		chip.Write((byte)(offset + 1), (byte)(frequency >> 8));
		chip.Write((byte)(offset + 2), (byte)(pulseWidth & 0xFF));
		chip.Write((byte)(offset + 3), (byte)(pulseWidth >> 8));
		chip.Write((byte)(offset + 5), attackDecay);
		chip.Write((byte)(offset + 6), sustainRelease);
		chip.Write((byte)(offset + 4), control);
	}

	private static double[] CollectSamples(SidChip chip, int warmupCycles, int measuredCycles)
	{
		RenderCycles(chip, warmupCycles);

		var samples = new double[measuredCycles];
		for (var i = 0; i < samples.Length; i++)
		{
			samples[i] = chip.Render(1);
		}

		return samples;
	}

	private static double LargestAdjacentJump(IReadOnlyList<double> samples)
	{
		var largest = 0.0;
		for (var i = 1; i < samples.Count; i++)
		{
			largest = Math.Max(largest, Math.Abs(samples[i] - samples[i - 1]));
		}

		return largest;
	}

	private static double MeasureRange(IReadOnlyList<double> samples)
	{
		return samples.Max() - samples.Min();
	}

	private static void AssertFilterStateClose(SidChipDebugState expected, SidChipDebugState actual)
	{
		Assert.Equal(expected.FilterCutoffRegister, actual.FilterCutoffRegister);
		Assert.Equal(expected.FilterResonanceNibble, actual.FilterResonanceNibble);
		Assert.InRange(Math.Abs(expected.LowPassOutput - actual.LowPassOutput), 0.0, 1.0e-12);
		Assert.InRange(Math.Abs(expected.BandPassOutput - actual.BandPassOutput), 0.0, 1.0e-12);
		Assert.InRange(Math.Abs(expected.HighPassOutput - actual.HighPassOutput), 0.0, 1.0e-12);
	}

	private static double MeasureLowPassPeak(SidChip chip, int warmupCycles, int measuredCycles)
	{
		RenderCycles(chip, warmupCycles);

		var peak = 0.0;
		for (var i = 0; i < measuredCycles; i++)
		{
			chip.Render(1);
			peak = Math.Max(peak, Math.Abs(chip.DebugState.LowPassOutput));
		}

		return peak;
	}

	private static double Rms(IReadOnlyList<double> samples)
	{
		var sum = 0.0;
		for (var i = 0; i < samples.Count; i++)
		{
			sum += samples[i] * samples[i];
		}

		return Math.Sqrt(sum / Math.Max(1, samples.Count));
	}

	private static double AcRms(IReadOnlyList<double> samples)
	{
		var mean = samples.Average();
		var sum = 0.0;
		for (var i = 0; i < samples.Count; i++)
		{
			var sample = samples[i] - mean;
			sum += sample * sample;
		}

		return Math.Sqrt(sum / Math.Max(1, samples.Count));
	}

	private static double NormalizedForcedMsbDacCode(int register, double twoRDivR, bool terminated)
	{
		var physicalDac = BuildNormalizedDacTable(bits: 12, twoRDivR, terminated);
		var low = physicalDac[0x800];
		var high = physicalDac[0xFFF];
		return (physicalDac[0x800 | (register & 0x7FF)] - low) / (high - low);
	}

	private static double[] BuildNormalizedDacTable(int bits, double twoRDivR, bool terminated)
	{
		var bitWeights = new double[bits];
		for (var setBit = 0; setBit < bits; setBit++)
		{
			var voltage = 1.0;
			var resistance = 1.0;
			var twoR = twoRDivR * resistance;
			var tail = terminated ? twoR : double.PositiveInfinity;
			for (var bit = 0; bit < setBit; bit++)
			{
				tail = double.IsInfinity(tail)
					? resistance + twoR
					: resistance + ((twoR * tail) / (twoR + tail));
			}

			if (double.IsInfinity(tail))
			{
				tail = twoR;
			}
			else
			{
				tail = (twoR * tail) / (twoR + tail);
				voltage *= tail / twoR;
			}

			for (var bit = setBit + 1; bit < bits; bit++)
			{
				tail += resistance;
				var current = voltage / tail;
				tail = (twoR * tail) / (twoR + tail);
				voltage = tail * current;
			}

			bitWeights[setBit] = voltage;
		}

		var table = new double[1 << bits];
		var maximum = 0.0;
		for (var code = 0; code < table.Length; code++)
		{
			var output = 0.0;
			for (var bit = 0; bit < bits; bit++)
			{
				if ((code & (1 << bit)) != 0)
				{
					output += bitWeights[bit];
				}
			}

			table[code] = output;
			maximum = Math.Max(maximum, output);
		}

		for (var code = 0; code < table.Length; code++)
		{
			table[code] /= maximum;
		}

		return table;
	}

	private static void RenderCycles(SidChip chip, int cycles)
	{
		const int chunk = 64;
		while (cycles >= chunk)
		{
			chip.Render(chunk);
			cycles -= chunk;
		}

		if (cycles > 0)
		{
			chip.Render(cycles);
		}
	}
}
