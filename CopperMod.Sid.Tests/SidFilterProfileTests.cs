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
		Assert.InRange(chip.DebugState.FilterCutoffHz, 29.9, 30.1);

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

		Assert.True(dataSheet > balanced, $"Expected datasheet midpoint to be brighter than balanced, data {dataSheet:0.0}, balanced {balanced:0.0}.");
		Assert.True(balanced > darkR3, $"Expected balanced midpoint to be brighter than dark R3, balanced {balanced:0.0}, dark {darkR3:0.0}.");
		Assert.True(linear8580 > dataSheet, $"Expected 8580 midpoint to be brighter than 6581 datasheet, 8580 {linear8580:0.0}, data {dataSheet:0.0}.");
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
	public void MixerFilterPipelineMatchesPinnedCurrentOutput()
	{
		var chip = new SidChip(SidChipModel.Mos6581, SidConstants.DefaultSidBaseAddress);
		WriteVoice(chip, voice: 0, frequency: 0x1234, control: 0x41, pulseWidth: 0x0830, attackDecay: 0x00, sustainRelease: 0xF0);
		WriteVoice(chip, voice: 1, frequency: 0x0711, control: 0x21, pulseWidth: 0x0000, attackDecay: 0x00, sustainRelease: 0xF0);
		WriteVoice(chip, voice: 2, frequency: 0x0D55, control: 0x11, pulseWidth: 0x0000, attackDecay: 0x00, sustainRelease: 0xF0);
		chip.Write(0x15, 0x03);
		chip.Write(0x16, 0x60);
		chip.Write(0x17, 0xF3);
		chip.Write(0x18, 0x7F);
		var checkpoints = new[]
		{
			0, 1, 2, 3, 4, 5, 31, 63, 127, 255, 383, 384, 511, 767, 768,
			895, 1023, 1151, 1152, 1279, 1535, 1536, 1791, 1792, 2047,
			2303, 2559, 3071, 4095
		};
		var expected = new[]
		{
			0.017298175441452127,
			0.0323319792231036,
			0.045397822896049086,
			0.05675331704545432,
			0.0666223504278281,
			0.07519950423733535,
			0.12492814676847076,
			0.11724430094994959,
			0.09825887427718794,
			0.06475300746591524,
			0.03721252381840548,
			0.0365815906026363,
			0.007285910767749346,
			-0.040962353300401086,
			-0.04066838230439219,
			-0.05646858834318402,
			-0.07190295420889999,
			-0.08533071286395066,
			-0.10161137102641625,
			-0.21798602155958965,
			-0.2291155604587863,
			-0.19923589031104413,
			-0.010130804993097662,
			-0.03416532733853054,
			0.41672193340966746,
			0.43773843383793354,
			0.5159890924615952,
			0.7648882786571707,
			-0.01729801729840872
		};

		var checkpointIndex = 0;
		for (var cycle = 0; cycle <= 4095; cycle++)
		{
			if (cycle == 384)
			{
				chip.Write(0x17, 0xF5);
			}

			if (cycle == 768)
			{
				chip.Write(0x18, 0x9F);
			}

			if (cycle == 1152)
			{
				chip.Write(0x16, 0x20);
			}

			if (cycle == 1536)
			{
				chip.Write(0x18, 0x0F);
			}

			if (cycle == 1792)
			{
				chip.Write(0x18, 0x5F);
			}

			var sample = chip.Render(1);
			if (checkpointIndex < checkpoints.Length && cycle == checkpoints[checkpointIndex])
			{
				var delta = Math.Abs(sample - expected[checkpointIndex]);
				Assert.True(delta <= 1e-12, $"Cycle {cycle} expected {expected[checkpointIndex]:R}, got {sample:R}, delta {delta:R}.");
				checkpointIndex++;
			}
		}

		Assert.Equal(expected.Length, checkpointIndex);
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

	private static double ReadDamping(byte resonance)
	{
		var chip = CreateFilteredPulse(SidChipModel.Mos6581, SidFilterProfileId.Mos6581Balanced, resonance: resonance);
		chip.Render(1);
		return chip.DebugState.FilterDamping;
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

	private static double Rms(IReadOnlyList<double> samples)
	{
		var sum = 0.0;
		for (var i = 0; i < samples.Count; i++)
		{
			sum += samples[i] * samples[i];
		}

		return Math.Sqrt(sum / Math.Max(1, samples.Count));
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
