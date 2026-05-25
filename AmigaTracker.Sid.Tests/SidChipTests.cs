namespace AmigaTracker.Sid.Tests;

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

	private static SidChip CreatePulseVoice()
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);
		chip.Write(0x00, 0xE8);
		chip.Write(0x01, 0x05);
		chip.Write(0x02, 0x32);
		chip.Write(0x03, 0x08);
		chip.Write(0x05, 0x00);
		chip.Write(0x06, 0xF0);
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

	private static double[] CollectSamples(SidChip chip, int warmupCycles, int measuredCycles)
	{
		for (var i = 0; i < warmupCycles; i++)
		{
			chip.Render(1);
		}

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
		for (var i = 0; i < warmupCycles; i++)
		{
			chip.Render(1);
		}

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
}
