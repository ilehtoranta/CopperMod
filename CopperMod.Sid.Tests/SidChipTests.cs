namespace CopperMod.Sid.Tests;

public sealed class SidChipTests
{
	[Fact]
	public void FrequencyRegisterUsesSidPhaseAccumulatorScale()
	{
		var chip = CreateSawVoice();
		var range = MeasureRange(chip, warmupCycles: 3000, measuredCycles: 1024);

		Assert.True(range > 0.5, $"Expected SID oscillator to cover a broad sawtooth range, got {range:0.000}.");
	}

	[Fact]
	public void FilterModeDoesNotMuteUnroutedVoices()
	{
		var chip = CreateSawVoice();
		chip.Write(0x17, 0x00);
		chip.Write(0x18, 0x1F);

		var range = MeasureRange(chip, warmupCycles: 3000, measuredCycles: 1024);

		Assert.True(range > 0.3, $"Expected unfiltered voice to bypass active filter mode, got {range:0.000}.");
	}

	[Fact]
	public void PulseWidthUsesTopTwelvePhaseBits()
	{
		var chip = CreatePulseVoice();
		var range = MeasureRange(chip, warmupCycles: 10000, measuredCycles: 24000);

		Assert.True(range > 0.5, $"Expected nonzero pulse width to produce a toggling waveform, got {range:0.000}.");
	}

	[Fact]
	public void PulseComparatorUsesSidPolarity()
	{
		var zeroWidth = CreatePulseVoice(attackDecay: 0x00, sustainRelease: 0xF0, pulseWidth: 0x000);
		var maxWidth = CreatePulseVoice(attackDecay: 0x00, sustainRelease: 0xF0, pulseWidth: 0xFFF);

		var zeroSamples = CollectSamples(zeroWidth, warmupCycles: 4000, measuredCycles: 2048);
		var maxSamples = CollectSamples(maxWidth, warmupCycles: 4000, measuredCycles: 2048);

		Assert.True(zeroSamples.Average() > maxSamples.Average() + 0.1);
	}

	[Fact]
	public void TriangleWaveformDoesNotJumpAtHalfCycle()
	{
		var samples = CollectSamples(CreateTriangleVoice(), warmupCycles: 3000, measuredCycles: 2048);
		var largestJump = LargestAdjacentJump(samples);

		Assert.True(largestJump < 0.05, $"Expected triangle waveform to be continuous, got adjacent jump {largestJump:0.000}.");
	}

	[Fact]
	public void SawtoothWaveformRisesBetweenWraps()
	{
		var samples = CollectSamples(CreateSawVoice(), warmupCycles: 3000, measuredCycles: 2048);
		var positiveJumps = samples.Zip(samples.Skip(1), (previous, current) => current - previous)
			.Where(delta => delta > 0)
			.ToArray();
		var negativeWraps = samples.Zip(samples.Skip(1), (previous, current) => current - previous)
			.Count(delta => delta < -0.03);

		Assert.True(positiveJumps.Length > 0, "Expected sawtooth to rise between wraps.");
		Assert.True(positiveJumps.Max() < 0.05, $"Expected sawtooth rise to be smooth between wraps, got jump {positiveJumps.Max():0.000}.");
		Assert.True(negativeWraps > 0, "Expected sawtooth to wrap downward.");
	}

	[Fact]
	public void ArkanoidPulseRegisterSetTogglesAtAudioCadence()
	{
		var chip = CreateArkanoidPulseChord();
		var min = double.MaxValue;
		var max = double.MinValue;
		for (var i = 0; i < 880; i++)
		{
			var sample = chip.Render(22);
			min = Math.Min(min, sample);
			max = Math.Max(max, sample);
		}

		Assert.True(max - min > 0.5, $"Expected Arkanoid pulse register set to toggle over one frame, got {max - min:0.000}.");
	}

	[Theory]
	[InlineData("unfiltered")]
	[InlineData("filtered")]
	[InlineData("muted")]
	public void BatchRenderMatchesOneCyclePath(string scenario)
	{
		var expected = CreateBatchEquivalenceChip(scenario);
		var actual = CreateBatchEquivalenceChip(scenario);

		var expectedSum = RenderOneCycleSum(expected, firstCycle: 1, cycles: 1024);
		var actualSum = actual.RenderAndSumFast(firstCycle: 1, cycles: 1024);

		AssertClose(expectedSum, actualSum);
		AssertDebugStateEqual(expected.DebugState, actual.DebugState);
	}

	[Fact]
	public void BatchRenderMatchesOneCyclePathAcrossRegisterBoundary()
	{
		var expected = CreateBatchEquivalenceChip("filtered");
		var actual = CreateBatchEquivalenceChip("filtered");
		var expectedSum = RenderOneCycleSum(expected, firstCycle: 1, cycles: 128);
		var actualSum = actual.RenderAndSumFast(firstCycle: 1, cycles: 128);

		expected.Write(0x16, 0x20);
		actual.Write(0x16, 0x20);
		expected.Write(0x04, 0x40);
		actual.Write(0x04, 0x40);

		expectedSum += RenderOneCycleSum(expected, firstCycle: 129, cycles: 384);
		actualSum += actual.RenderAndSumFast(firstCycle: 129, cycles: 384);

		AssertClose(expectedSum, actualSum);
		AssertDebugStateEqual(expected.DebugState, actual.DebugState);
	}

	[Fact]
	public void SidSystemBatchPathMatchesTracedSampleBoundaries()
	{
		var fast = CreateBatchEquivalenceSid(trace: false);
		var traced = CreateBatchEquivalenceSid(trace: true);
		Assert.True(fast.TryWrite(0xD416, 0x20, 100));
		Assert.True(traced.TryWrite(0xD416, 0x20, 100));

		var fastAtBoundary = fast.RenderSample(100);
		var tracedAtBoundary = traced.RenderSample(100);
		var fastAfterBoundary = fast.RenderSample(101);
		var tracedAfterBoundary = traced.RenderSample(101);
		var fastLater = fast.RenderSample(240);
		var tracedLater = traced.RenderSample(240);

		Assert.Equal(tracedAtBoundary, fastAtBoundary);
		Assert.Equal(tracedAfterBoundary, fastAfterBoundary);
		Assert.Equal(tracedLater, fastLater);
		AssertDebugStateEqual(traced.Chips[0].DebugState, fast.Chips[0].DebugState);
	}

	[Fact]
	public void ResonantFilteredPulseDoesNotPlateauAtInternalClamp()
	{
		var chip = CreateGreenBeretFilteredPulse();
		var samples = CollectSamples(chip, warmupCycles: 10000, measuredCycles: 12000);
		var plateauCount = samples.Count(sample =>
			Math.Abs(sample - 0.37333333333333335) < 0.000001 ||
			Math.Abs(sample + 0.29333333333333333) < 0.000001);

		Assert.True(plateauCount < samples.Length / 20, $"Expected filter output to avoid hard internal clamp plateaus, got {plateauCount} plateau samples.");
	}

	[Fact]
	public void VolumeRegisterProducesMonotonicAudibleDigiSteps()
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);
		var samples = new double[16];
		for (var volume = 0; volume < samples.Length; volume++)
		{
			chip = new SidChip(SidChipModel.Mos6581, 0xD400);
			chip.Write(0x18, (byte)volume);
			RenderCycles(chip, 256);
			samples[volume] = chip.Render(1);
		}

		for (var i = 1; i < samples.Length; i++)
		{
			Assert.True(samples[i] > samples[i - 1], $"Expected D418 volume step {i} to be greater than {i - 1}.");
		}

		Assert.True(samples[^1] - samples[0] > 0.28, $"Expected audible 6581 volume DAC range, got {samples[^1] - samples[0]:0.000}.");
		Assert.True(samples[^1] - samples[4] > 0.22, $"Expected offset-biased 4-bit digi playback to stay prominent, got {samples[^1] - samples[4]:0.000}.");
	}

	[Fact]
	public void Mos6581VolumeRegisterStepsAddSlewedTransientDigiEnergy()
	{
		var mos6581 = MeasureVolumeStepTransient(SidChipModel.Mos6581);
		var mos8580 = MeasureVolumeStepTransient(SidChipModel.Mos8580);

		Assert.True(mos6581.EarlyExcursion > 0.15, $"Expected 6581 D418 step to create strong transient digi energy, got {mos6581.EarlyExcursion:0.000}.");
		Assert.True(Math.Abs(mos6581.Settled - SidAnalog.VolumeOffset(3, SidChipModel.Mos6581)) < 0.01, $"Expected 6581 D418 transient to decay back to the volume rest offset, settled {mos6581.Settled:0.000}.");
		Assert.True(mos8580.EarlyExcursion < 0.02, $"Expected 8580 D418 transient to remain weak, got {mos8580.EarlyExcursion:0.000}.");
	}

	[Fact]
	public void MultipleVoicesSumLouderWithoutActiveChannelNormalization()
	{
		var oneVoice = CreateSawVoice();
		var twoVoices = CreateTwoSawVoices();

		var oneVoiceRange = MeasureRange(oneVoice, warmupCycles: 3000, measuredCycles: 4096);
		var twoVoiceRange = MeasureRange(twoVoices, warmupCycles: 3000, measuredCycles: 4096);
		var twoVoicePeak = MeasurePeak(twoVoices, warmupCycles: 0, measuredCycles: 4096);

		Assert.True(twoVoiceRange > oneVoiceRange * 1.25, $"Expected two voices to be louder than one voice, one {oneVoiceRange:0.000}, two {twoVoiceRange:0.000}.");
		Assert.True(twoVoicePeak < 0.999, $"Expected summed voices to retain output headroom, peak {twoVoicePeak:0.000}.");
	}

	[Fact]
	public void VoiceThreeMuteDoesNotMuteVoiceThreeWhenRoutedThroughFilter()
	{
		var direct = CreateVoiceThreePulse(filtered: false, muted: false);
		var muted = CreateVoiceThreePulse(filtered: false, muted: true);
		var filteredMuted = CreateVoiceThreePulse(filtered: true, muted: true);

		var directRange = MeasureRange(direct, warmupCycles: 10000, measuredCycles: 12000);
		var mutedRange = MeasureRange(muted, warmupCycles: 10000, measuredCycles: 12000);
		var filteredMutedRange = MeasureRange(filteredMuted, warmupCycles: 10000, measuredCycles: 12000);

		Assert.True(mutedRange < directRange * 0.25, $"Expected direct voice 3 output to be muted, direct {directRange:0.000}, muted {mutedRange:0.000}.");
		Assert.True(filteredMutedRange > mutedRange + 0.05, $"Expected filtered voice 3 to remain audible while muted from direct output, filtered {filteredMutedRange:0.000}, muted {mutedRange:0.000}.");
	}

	[Fact]
	public void FilterCutoffChangesFilteredPulseResponse()
	{
		var lowCutoff = CreateFilteredPulse(cutoffHighByte: 0x08, resonance: 0x00, mode: 0x10, frequency: 0x6000);
		var highCutoff = CreateFilteredPulse(cutoffHighByte: 0xF0, resonance: 0x00, mode: 0x10, frequency: 0x6000);

		var lowSamples = CollectSamples(lowCutoff, warmupCycles: 12000, measuredCycles: 16000);
		var highSamples = CollectSamples(highCutoff, warmupCycles: 12000, measuredCycles: 16000);
		var lowJump = LargestAdjacentJump(lowSamples);
		var highJump = LargestAdjacentJump(highSamples);

		Assert.True(highJump > lowJump * 1.5, $"Expected high cutoff to preserve sharper pulse edges, low jump {lowJump:0.000}, high jump {highJump:0.000}.");
		Assert.All(lowSamples.Concat(highSamples), sample => Assert.True(double.IsFinite(sample)));
	}

	[Fact]
	public void FilterModesProduceDistinctFiniteResponses()
	{
		var lowPass = CreateFilteredPulse(cutoffHighByte: 0x90, resonance: 0x80, mode: 0x10);
		var bandPass = CreateFilteredPulse(cutoffHighByte: 0x90, resonance: 0x80, mode: 0x20);
		var highPass = CreateFilteredPulse(cutoffHighByte: 0x90, resonance: 0x80, mode: 0x40);

		var lowRange = MeasureRange(lowPass, warmupCycles: 12000, measuredCycles: 16000);
		var bandRange = MeasureRange(bandPass, warmupCycles: 12000, measuredCycles: 16000);
		var highRange = MeasureRange(highPass, warmupCycles: 12000, measuredCycles: 16000);

		Assert.True(Math.Abs(lowRange - bandRange) > 0.01 || Math.Abs(lowRange - highRange) > 0.01 || Math.Abs(bandRange - highRange) > 0.01);
		Assert.True(double.IsFinite(lowRange));
		Assert.True(double.IsFinite(bandRange));
		Assert.True(double.IsFinite(highRange));
	}

	[Fact]
	public void SidWritesAreAppliedOnlyWhenAudioReachesTheirCycle()
	{
		var sid = new SidSystem(new[] { new SidChipPlacement(0, SidConstants.DefaultSidBaseAddress) }, SidChipModel.Mos6581);

		Assert.True(sid.TryWrite(0xD418, 0x0F, 100));
		_ = sid.RenderSample(50);

		Assert.Equal(0x00, sid.Chips[0].Registers[0x18]);

		_ = sid.RenderSample(100);

		Assert.Equal(0x0F, sid.Chips[0].Registers[0x18]);
	}

	[Fact]
	public void SidWriteCaptureKeepsRecentBoundedHistory()
	{
		var sid = new SidSystem(new[] { new SidChipPlacement(0, SidConstants.DefaultSidBaseAddress) }, SidChipModel.Mos6581);

		for (var i = 0; i < 70000; i++)
		{
			Assert.True(sid.TryWrite(0xD418, (byte)i, i));
		}

		Assert.Equal(65536, sid.Writes.Count);
		Assert.Equal(70000 - 65536, sid.Writes[0].Cycle);
		Assert.Equal(69999, sid.Writes[^1].Cycle);
		Assert.Equal(sid.Writes.OrderBy(write => write.Cycle).Select(write => write.Cycle), sid.Writes.Select(write => write.Cycle));
	}

	[Fact]
	public void BuiltInSidRegisterMirrorsMapThroughD400ToD7ff()
	{
		var sid = new SidSystem(new[] { new SidChipPlacement(0, SidConstants.DefaultSidBaseAddress) }, SidChipModel.Mos6581);

		Assert.True(sid.TryWrite(0xD424, 0x41, 0));

		Assert.Equal(0x41, sid.Chips[0].Registers[0x04]);
		Assert.Equal(0x04, sid.Writes[0].Register);
		Assert.True(sid.TryRead(0xD424, out var mirroredValue));
		Assert.Equal(0x41, mirroredValue);
	}

	[Fact]
	public void SidRegisterWritesAreForwardedOnNextSidCycle()
	{
		var sid = new SidSystem(new[] { new SidChipPlacement(0, SidConstants.DefaultSidBaseAddress) }, SidChipModel.Mos6581);

		Assert.True(sid.TryWrite(0xD418, 0x0F, 100));
		sid.AdvanceTo(100);

		Assert.Equal(0x0F, sid.Chips[0].Registers[0x18]);
		Assert.Equal(0x00, sid.Chips[0].DebugState.ForwardedRegisters[0x18]);

		sid.AdvanceTo(101);

		Assert.Equal(0x0F, sid.Chips[0].DebugState.ForwardedRegisters[0x18]);
	}

	[Fact]
	public void MultipleWritesBeforeForwardingKeepLastValue()
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);

		chip.Write(0x18, 0x01);
		chip.Write(0x18, 0x0F);

		Assert.Equal(0x0F, chip.Registers[0x18]);
		Assert.Equal(0x00, chip.DebugState.ForwardedRegisters[0x18]);

		chip.Render(1);

		Assert.Equal(0x0F, chip.DebugState.ForwardedRegisters[0x18]);
	}

	[Fact]
	public void GateOnStartsAttackOnlyAfterForwarding()
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);
		chip.Write(0x05, 0x00);
		chip.Write(0x06, 0xF0);
		chip.Write(0x04, 0x21);

		Assert.Equal(0x00, chip.DebugState.Voices[0].Control);

		chip.Render(1);

		Assert.Equal(0x21, chip.DebugState.Voices[0].Control);
		Assert.Equal(0, chip.DebugState.Voices[0].EnvelopeCounter);

		chip.Render(8);

		Assert.Equal(1, chip.DebugState.Voices[0].EnvelopeCounter);
	}

	[Fact]
	public void GateOffDoesNotResetEnvelopeRateCounter()
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);
		chip.Write(0x05, 0xF0);
		chip.Write(0x06, 0xFF);
		chip.Write(0x04, 0x41);
		chip.Render(10);
		var before = chip.DebugState.Voices[0].RateCounter;

		chip.Write(0x04, 0x40);
		chip.Render(1);

		Assert.Equal(before + 1, chip.DebugState.Voices[0].RateCounter);
	}

	[Fact]
	public void SustainReleaseWriteDoesNotOverwriteEnvelopeCounter()
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);
		chip.Write(0x05, 0x00);
		chip.Write(0x06, 0xF0);
		chip.Write(0x04, 0x41);
		chip.Render(2304);
		var before = chip.DebugState.Voices[0].EnvelopeCounter;

		chip.Write(0x06, 0x00);
		chip.Render(1);

		Assert.Equal(0xFF, before);
		Assert.Equal(before, chip.DebugState.Voices[0].EnvelopeCounter);
	}

	[Fact]
	public void OscillatorUsesTwentyFourBitAccumulator()
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);
		chip.Write(0x00, 0x01);

		chip.Render(1);

		Assert.Equal(1u, chip.DebugState.Voices[0].Accumulator);
	}

	[Fact]
	public void TestBitHoldsOscillatorAtZero()
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);
		chip.Write(0x00, 0xFF);
		chip.Write(0x01, 0xFF);
		chip.Write(0x04, 0x20);
		chip.Render(4);

		Assert.True(chip.DebugState.Voices[0].Accumulator > 0);

		chip.Write(0x04, 0x28);
		chip.Render(4);

		Assert.Equal(0u, chip.DebugState.Voices[0].Accumulator);
	}

	[Fact]
	public void SyncResetsVoicesFromSourceMsbRisingSimultaneously()
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);
		WriteVoice(chip, voice: 0, frequency: 0x8000, control: 0x20);
		WriteVoice(chip, voice: 1, frequency: 0x8000, control: 0x22);
		WriteVoice(chip, voice: 2, frequency: 0x8000, control: 0x22);

		chip.Render(256);

		Assert.Equal(0x800000u, chip.DebugState.Voices[0].Accumulator);
		Assert.Equal(0u, chip.DebugState.Voices[1].Accumulator);
		Assert.Equal(0u, chip.DebugState.Voices[2].Accumulator);
	}

	[Fact]
	public void NoiseShiftRegisterClocksOnlyOnOscillatorBit19Rising()
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);
		chip.Write(0x00, 0x00);
		chip.Write(0x01, 0x80);

		chip.Render(15);

		Assert.Equal(0x7FFFF8u, chip.DebugState.Voices[0].NoiseShiftRegister);

		chip.Render(1);

		Assert.Equal(NextNoise(0x7FFFF8), chip.DebugState.Voices[0].NoiseShiftRegister);
	}

	[Fact]
	public void TestBitResetsNoiseShiftRegister()
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);
		chip.Write(0x00, 0x00);
		chip.Write(0x01, 0x80);
		chip.Render(16);

		Assert.Equal(NextNoise(0x7FFFF8), chip.DebugState.Voices[0].NoiseShiftRegister);

		chip.Write(0x04, 0x08);
		chip.Render(1);

		Assert.Equal(0x7FFFF8u, chip.DebugState.Voices[0].NoiseShiftRegister);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Mos6581NoiseCombinedWithOtherWaveformsDegradesWithoutImmediateLfsrClear(bool traced)
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);
		if (traced)
		{
			chip.Trace = new SidCycleTrace();
		}

		WriteVoice(chip, voice: 0, frequency: 0x8000, control: 0xA0);

		chip.Render(1);

		Assert.Equal(0x2ED768u, chip.DebugState.Voices[0].NoiseShiftRegister);
		Assert.Equal(0u, chip.DebugState.Voices[0].NoiseDac);

		chip.Write(0x04, 0x80);
		chip.Render(15);

		Assert.NotEqual(0u, chip.DebugState.Voices[0].NoiseShiftRegister);

		chip.Render(1);

		Assert.Equal(NextNoise(0x2ED768), chip.DebugState.Voices[0].NoiseShiftRegister);
	}

	[Fact]
	public void Mos6581NoiseWritebackDuringTestAffectsReleaseLatch()
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);
		WriteVoice(chip, voice: 0, frequency: 0x8000, control: 0xA8);

		chip.Render(1);

		Assert.Equal(0x7FFFF8u, chip.DebugState.Voices[0].NoiseShiftRegister);

		chip.Write(0x04, 0x80);
		chip.Render(1);

		Assert.Equal(0x2ED768u, chip.DebugState.Voices[0].NoiseShiftRegister);
	}

	[Fact]
	public void Mos8580NoiseCombinedWithOtherWaveformsKeepsLegacyImmediateLock()
	{
		var chip = new SidChip(SidChipModel.Mos8580, 0xD400);
		WriteVoice(chip, voice: 0, frequency: 0x8000, control: 0xA0);

		chip.Render(1);

		Assert.Equal(0u, chip.DebugState.Voices[0].NoiseShiftRegister);

		chip.Write(0x04, 0x88);
		chip.Render(1);

		Assert.Equal(0x7FFFF8u, chip.DebugState.Voices[0].NoiseShiftRegister);
	}

	[Fact]
	public void Mos6581NoiseWritebackMatchesFastAndTracedPaths()
	{
		var fast = CreateNoiseWritebackChip(trace: false);
		var traced = CreateNoiseWritebackChip(trace: true);

		fast.Render(96);
		traced.Render(96);

		AssertDebugStateEqual(fast.DebugState, traced.DebugState);
	}

	[Fact]
	public void NoiseDacUsesDocumentedOutputBits()
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);

		Assert.Equal(ExpectedNoiseDac(0x7FFFF8), chip.DebugState.Voices[0].NoiseDac);
	}

	[Fact]
	public void Mos6581TrianglePulseCombinationAddsContentionEdges()
	{
		var triangleSamples = RenderAudioSamples(control: 0x11);
		var combinedSamples = RenderAudioSamples(control: 0x51);

		var triangleCrossingRate = ZeroCrossingRate(triangleSamples);
		var combinedCrossingRate = ZeroCrossingRate(combinedSamples);

		Assert.True(combinedCrossingRate > triangleCrossingRate * 2.5, $"Expected 6581 triangle+pulse contention to add edge detail, triangle {triangleCrossingRate:0.000}, combined {combinedCrossingRate:0.000}.");
		Assert.True(MeasureRange(combinedSamples) > 0.02, "Expected 6581 triangle+pulse combination to remain audible.");
	}

	[Fact]
	public void ReleaseNibbleAStaysAudiblePastHalfSecond()
	{
		var chip = CreatePulseVoice(attackDecay: 0x00, sustainRelease: 0xFA);
		RenderCycles(chip, 10000);
		chip.Write(0x04, 0x40);
		RenderCycles(chip, (SidConstants.PalCpuCyclesPerSecond * 3) / 4);

		var range = MeasureRange(chip, warmupCycles: 0, measuredCycles: 24000);

		Assert.True(range > 0.04, $"Expected release rate A to remain audible while following the SID exponential release counter, got range {range:0.000} after 0.75s.");
	}

	[Fact]
	public void ReleaseNibble9SurvivesHardRestartGap()
	{
		var chip = CreatePulseVoice(attackDecay: 0x05, sustainRelease: 0xF9);
		RenderCycles(chip, 10000);
		chip.Write(0x04, 0x40);
		RenderCycles(chip, SidConstants.PalCyclesPerFrame * 2);

		var range = MeasureRange(chip, warmupCycles: 0, measuredCycles: 24000);

		Assert.True(range > 0.5, $"Expected release rate 9 to keep a hard-restarted voice alive across two PAL frames, got range {range:0.000}.");
	}

	private static SidChip CreateSawVoice()
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);
		chip.Write(0x00, 0x00);
		chip.Write(0x01, 0x80);
		chip.Write(0x05, 0x00);
		chip.Write(0x06, 0xF0);
		chip.Write(0x04, 0x21);
		chip.Write(0x18, 0x0F);
		return chip;
	}

	private static SidChip CreateTriangleVoice()
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);
		chip.Write(0x00, 0x00);
		chip.Write(0x01, 0x40);
		chip.Write(0x05, 0x00);
		chip.Write(0x06, 0xF0);
		chip.Write(0x04, 0x11);
		chip.Write(0x18, 0x0F);
		return chip;
	}

	private static double[] RenderAudioSamples(byte control)
	{
		const int sampleRate = 48000;
		const int samples = sampleRate / 2;
		var sid = new SidSystem(new[] { new SidChipPlacement(0, SidConstants.DefaultSidBaseAddress) }, SidChipModel.Mos6581);
		Assert.True(sid.TryWrite(0xD400, 0x00, 0));
		Assert.True(sid.TryWrite(0xD401, 0x10, 0));
		Assert.True(sid.TryWrite(0xD402, 0x00, 0));
		Assert.True(sid.TryWrite(0xD403, 0x01, 0));
		Assert.True(sid.TryWrite(0xD405, 0x00, 0));
		Assert.True(sid.TryWrite(0xD406, 0xF0, 0));
		Assert.True(sid.TryWrite(0xD404, control, 0));
		Assert.True(sid.TryWrite(0xD418, 0x0F, 0));

		var rendered = new double[samples];
		for (var i = 0; i < rendered.Length; i++)
		{
			var cycle = SidIntegerMath.MulDivRoundNearest(i + 1, SidConstants.PalCpuCyclesPerSecond, sampleRate);
			rendered[i] = sid.RenderSample(cycle);
		}

		return rendered;
	}

	private static (double EarlyExcursion, double Settled) MeasureVolumeStepTransient(SidChipModel model)
	{
		var chip = new SidChip(model, 0xD400);
		chip.Write(0x18, 0x08);
		RenderCycles(chip, 12000);
		_ = chip.Render(1);
		chip.Write(0x18, 0x03);

		var minimum = double.MaxValue;
		var maximum = double.MinValue;
		for (var i = 0; i < 2048; i++)
		{
			var sample = chip.Render(1);
			minimum = Math.Min(minimum, sample);
			maximum = Math.Max(maximum, sample);
		}

		RenderCycles(chip, 16000);
		var settled = chip.Render(1);
		var earlyExcursion = Math.Max(Math.Abs(minimum - settled), Math.Abs(maximum - settled));
		return (earlyExcursion, settled);
	}

	private static SidChip CreateTwoSawVoices()
	{
		var chip = CreateSawVoice();
		chip.Write(7, 0x00);
		chip.Write(8, 0x80);
		chip.Write(12, 0x00);
		chip.Write(13, 0xF0);
		chip.Write(11, 0x21);
		return chip;
	}

	private static SidChip CreatePulseVoice()
	{
		return CreatePulseVoice(attackDecay: 0x00, sustainRelease: 0xF0);
	}

	private static SidChip CreatePulseVoice(byte attackDecay, byte sustainRelease, ushort pulseWidth = 0x0832)
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);
		chip.Write(0x00, 0xE8);
		chip.Write(0x01, 0x05);
		chip.Write(0x02, (byte)(pulseWidth & 0xFF));
		chip.Write(0x03, (byte)(pulseWidth >> 8));
		chip.Write(0x05, attackDecay);
		chip.Write(0x06, sustainRelease);
		chip.Write(0x04, 0x41);
		chip.Write(0x18, 0x0F);
		return chip;
	}

	private static SidChip CreateArkanoidPulseChord()
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);
		var registers = new byte[]
		{
			0xE8, 0x05, 0x32, 0x08, 0x41, 0x14, 0xC8,
			0xE8, 0x05, 0x32, 0x08, 0x41, 0x14, 0xC4,
			0xC1, 0x05, 0x96, 0x08, 0x41, 0x14, 0xC8,
			0x00, 0x00, 0xF0, 0x0F
		};
		for (var register = 0; register < registers.Length; register++)
		{
			chip.Write((byte)register, registers[register]);
		}

		for (var i = 0; i < 10000; i++)
		{
			chip.Render(1);
		}

		return chip;
	}

	private static SidChip CreateNoiseWritebackChip(bool trace)
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);
		if (trace)
		{
			chip.Trace = new SidCycleTrace();
		}

		chip.Write(0x00, 0x00);
		chip.Write(0x01, 0x80);
		chip.Write(0x02, 0x00);
		chip.Write(0x03, 0x08);
		chip.Write(0x05, 0x00);
		chip.Write(0x06, 0xF0);
		chip.Write(0x04, 0xE1);
		chip.Write(0x18, 0x0F);
		return chip;
	}

	private static void WriteVoice(SidChip chip, int voice, ushort frequency, byte control)
	{
		var offset = voice * 7;
		chip.Write((byte)(offset + 0), (byte)(frequency & 0xFF));
		chip.Write((byte)(offset + 1), (byte)(frequency >> 8));
		chip.Write((byte)(offset + 4), control);
	}

	private static SidChip CreateVoiceThreePulse(bool filtered, bool muted)
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);
		WriteVoice(chip, voice: 2, frequency: 0x1800, control: 0x41);
		chip.Write(0x10, 0x00);
		chip.Write(0x11, 0x08);
		chip.Write(0x13, 0x00);
		chip.Write(0x14, 0xF0);
		chip.Write(0x15, 0x00);
		chip.Write(0x16, 0xE0);
		chip.Write(0x17, filtered ? (byte)0x04 : (byte)0x00);
		chip.Write(0x18, (byte)((muted ? 0x80 : 0x00) | (filtered ? 0x10 : 0x00) | 0x0F));
		return chip;
	}

	private static SidChip CreateFilteredPulse(byte cutoffHighByte, byte resonance, byte mode, ushort frequency = 0x05E8)
	{
		var chip = CreatePulseVoice();
		chip.Write(0x00, (byte)(frequency & 0xFF));
		chip.Write(0x01, (byte)(frequency >> 8));
		chip.Write(0x15, 0x00);
		chip.Write(0x16, cutoffHighByte);
		chip.Write(0x17, (byte)(resonance | 0x01));
		chip.Write(0x18, (byte)(mode | 0x0F));
		return chip;
	}

	private static uint NextNoise(uint value)
	{
		var feedback = ((value >> 22) ^ (value >> 17)) & 1;
		return ((value << 1) | feedback) & 0x7FFFFF;
	}

	private static uint ExpectedNoiseDac(uint value)
	{
		var dac = 0u;
		dac |= ((value >> 22) & 1u) << 11;
		dac |= ((value >> 20) & 1u) << 10;
		dac |= ((value >> 16) & 1u) << 9;
		dac |= ((value >> 13) & 1u) << 8;
		dac |= ((value >> 11) & 1u) << 7;
		dac |= ((value >> 7) & 1u) << 6;
		dac |= ((value >> 4) & 1u) << 5;
		dac |= ((value >> 2) & 1u) << 4;
		return dac;
	}

	private static SidChip CreateGreenBeretFilteredPulse()
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);
		chip.Write(0x00, 0xB1);
		chip.Write(0x01, 0x19);
		chip.Write(0x02, 0x04);
		chip.Write(0x03, 0x07);
		chip.Write(0x05, 0x08);
		chip.Write(0x06, 0xCA);
		chip.Write(0x04, 0x41);
		chip.Write(0x15, 0x00);
		chip.Write(0x16, 0x64);
		chip.Write(0x17, 0x81);
		chip.Write(0x18, 0x1F);
		return chip;
	}

	private static SidChip CreateBatchEquivalenceChip(string scenario)
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);
		WriteVoice(chip, voice: 0, frequency: 0x1234, control: 0x41);
		chip.Write(0x02, 0x30);
		chip.Write(0x03, 0x08);
		chip.Write(0x05, 0x00);
		chip.Write(0x06, 0xF0);
		WriteVoice(chip, voice: 1, frequency: 0x0711, control: 0x21);
		chip.Write(0x0C, 0x00);
		chip.Write(0x0D, 0xF0);
		WriteVoice(chip, voice: 2, frequency: 0x0D55, control: 0x11);
		chip.Write(0x13, 0x00);
		chip.Write(0x14, 0xF0);
		chip.Write(0x15, 0x03);
		chip.Write(0x16, 0x60);
		chip.Write(0x17, scenario == "unfiltered" ? (byte)0x00 : (byte)0xF3);
		chip.Write(0x18, (byte)((scenario == "filtered" ? 0x70 : 0x10) | (scenario == "muted" ? 0x80 : 0x00) | 0x0F));
		if (scenario == "muted")
		{
			chip.MutedVoicesMask = 0x02;
		}

		return chip;
	}

	private static SidSystem CreateBatchEquivalenceSid(bool trace)
	{
		var sid = new SidSystem(new[] { new SidChipPlacement(0, SidConstants.DefaultSidBaseAddress) }, SidChipModel.Mos6581);
		Assert.True(sid.TryWrite(0xD400, 0x34, 0));
		Assert.True(sid.TryWrite(0xD401, 0x12, 0));
		Assert.True(sid.TryWrite(0xD402, 0x30, 0));
		Assert.True(sid.TryWrite(0xD403, 0x08, 0));
		Assert.True(sid.TryWrite(0xD405, 0x00, 0));
		Assert.True(sid.TryWrite(0xD406, 0xF0, 0));
		Assert.True(sid.TryWrite(0xD404, 0x41, 0));
		Assert.True(sid.TryWrite(0xD415, 0x03, 0));
		Assert.True(sid.TryWrite(0xD416, 0x60, 0));
		Assert.True(sid.TryWrite(0xD417, 0xF1, 0));
		Assert.True(sid.TryWrite(0xD418, 0x1F, 0));
		if (trace)
		{
			sid.Trace = new SidCycleTrace();
		}

		return sid;
	}

	private static double RenderOneCycleSum(SidChip chip, long firstCycle, long cycles)
	{
		var voiceOutputs = new double[3];
		var sum = 0.0;
		for (var i = 0L; i < cycles; i++)
		{
			sum += chip.RenderOneCycle(firstCycle + i, voiceOutputs);
		}

		return sum;
	}

	private static void AssertDebugStateEqual(SidChipDebugState expected, SidChipDebugState actual)
	{
		Assert.Equal(expected.ForwardedRegisters, actual.ForwardedRegisters);
		Assert.Equal(expected.FilterProfile, actual.FilterProfile);
		Assert.Equal(expected.FilterCutoffRegister, actual.FilterCutoffRegister);
		AssertClose(expected.FilterCutoffHz, actual.FilterCutoffHz);
		Assert.Equal(expected.FilterResonanceNibble, actual.FilterResonanceNibble);
		Assert.Equal(expected.FilterMode, actual.FilterMode);
		AssertClose(expected.FilterDamping, actual.FilterDamping);
		AssertClose(expected.LowPassOutput, actual.LowPassOutput);
		AssertClose(expected.BandPassOutput, actual.BandPassOutput);
		AssertClose(expected.HighPassOutput, actual.HighPassOutput);
		for (var i = 0; i < expected.Voices.Length; i++)
		{
			Assert.Equal(expected.Voices[i].Accumulator, actual.Voices[i].Accumulator);
			Assert.Equal(expected.Voices[i].NoiseShiftRegister, actual.Voices[i].NoiseShiftRegister);
			Assert.Equal(expected.Voices[i].NoiseDac, actual.Voices[i].NoiseDac);
			Assert.Equal(expected.Voices[i].EnvelopeCounter, actual.Voices[i].EnvelopeCounter);
			Assert.Equal(expected.Voices[i].RateCounter, actual.Voices[i].RateCounter);
			Assert.Equal(expected.Voices[i].ExponentialCounter, actual.Voices[i].ExponentialCounter);
			Assert.Equal(expected.Voices[i].EnvelopeState, actual.Voices[i].EnvelopeState);
			Assert.Equal(expected.Voices[i].Control, actual.Voices[i].Control);
		}
	}

	private static void AssertClose(double expected, double actual)
	{
		var delta = Math.Abs(expected - actual);
		Assert.True(delta <= 1e-12, $"Expected {expected:R}, got {actual:R}, delta {delta:R}.");
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

	private static double ZeroCrossingRate(IReadOnlyList<double> samples)
	{
		var mean = samples.Average();
		var crossings = 0;
		for (var i = 1; i < samples.Count; i++)
		{
			var previous = samples[i - 1] - mean;
			var current = samples[i] - mean;
			if ((previous < 0 && current >= 0) ||
				(previous >= 0 && current < 0))
			{
				crossings++;
			}
		}

		return crossings / (double)Math.Max(1, samples.Count - 1);
	}

	private static double MeasureRange(IReadOnlyList<double> samples)
	{
		return samples.Max() - samples.Min();
	}

	private static double MeasureRange(SidChip chip, int warmupCycles, int measuredCycles)
	{
		RenderCycles(chip, warmupCycles);

		var min = double.MaxValue;
		var max = double.MinValue;
		for (var i = 0; i < measuredCycles; i++)
		{
			var sample = chip.Render(1);
			min = Math.Min(min, sample);
			max = Math.Max(max, sample);
		}

		return max - min;
	}

	private static double MeasurePeak(SidChip chip, int warmupCycles, int measuredCycles)
	{
		RenderCycles(chip, warmupCycles);

		var peak = 0.0;
		for (var i = 0; i < measuredCycles; i++)
		{
			peak = Math.Max(peak, Math.Abs(chip.Render(1)));
		}

		return peak;
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
