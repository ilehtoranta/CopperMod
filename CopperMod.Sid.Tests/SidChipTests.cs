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
			.Count(delta => delta < -0.25);

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
			chip.Write(0x18, (byte)volume);
			samples[volume] = chip.Render(1);
		}

		for (var i = 1; i < samples.Length; i++)
		{
			Assert.True(samples[i] > samples[i - 1], $"Expected D418 volume step {i} to be greater than {i - 1}.");
		}

		Assert.True(samples[^1] - samples[0] > 0.12, $"Expected audible 6581 volume DAC range, got {samples[^1] - samples[0]:0.000}.");
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
	public void NoiseDacUsesDocumentedOutputBits()
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);

		Assert.Equal(ExpectedNoiseDac(0x7FFFF8), chip.DebugState.Voices[0].NoiseDac);
	}

	[Fact]
	public void ReleaseNibbleAStaysAudiblePastHalfSecond()
	{
		var chip = CreatePulseVoice(attackDecay: 0x00, sustainRelease: 0xFA);
		RenderCycles(chip, 10000);
		chip.Write(0x04, 0x40);
		RenderCycles(chip, (int)(SidConstants.PalCpuClock * 0.75));

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

	private static SidChip CreatePulseVoice(byte attackDecay, byte sustainRelease)
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);
		chip.Write(0x00, 0xE8);
		chip.Write(0x01, 0x05);
		chip.Write(0x02, 0x32);
		chip.Write(0x03, 0x08);
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
		dac |= ((value >> 20) & 1u) << 11;
		dac |= ((value >> 18) & 1u) << 10;
		dac |= ((value >> 14) & 1u) << 9;
		dac |= ((value >> 11) & 1u) << 8;
		dac |= ((value >> 9) & 1u) << 7;
		dac |= ((value >> 5) & 1u) << 6;
		dac |= ((value >> 2) & 1u) << 5;
		dac |= (value & 1u) << 4;
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
