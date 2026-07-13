namespace CopperMod.Sid.Tests;

public sealed class SidMultiChipTests
{
	[Fact]
	public void SidSystemUsesEachPlacementChipModel()
	{
		var sid = new SidSystem(
			new[]
			{
				new SidChipPlacement(0, 0xD400, SidChipModel.Mos6581),
				new SidChipPlacement(1, 0xD420, SidChipModel.Mos8580)
			},
			SidChipModel.Mos6581);

		Assert.Equal(SidChipModel.Mos6581, sid.Chips[0].Model);
		Assert.Equal(SidChipModel.Mos8580, sid.Chips[1].Model);
	}

	[Fact]
	public void UnknownExtraChipModelInheritsPrimaryModel()
	{
		var sid = new SidSystem(
			new[]
			{
				new SidChipPlacement(0, 0xD400, SidChipModel.Mos8580),
				new SidChipPlacement(1, 0xD420)
			},
			SidChipModel.Mos8580);

		Assert.All(sid.Chips, chip => Assert.Equal(SidChipModel.Mos8580, chip.Model));
	}

	[Fact]
	public void SilentExtraSidDoesNotAttenuatePrimarySidAcOutput()
	{
		var single = CreateSystem(extraChip: false);
		var dual = CreateSystem(extraChip: true);
		ConfigureQuietSaw(single);
		ConfigureQuietSaw(dual);

		var singleSamples = RenderSamples(single, 256, 64);
		var dualSamples = RenderSamples(dual, 256, 64);
		var singleRange = singleSamples.Max() - singleSamples.Min();
		var dualRange = dualSamples.Max() - dualSamples.Min();

		Assert.True(singleRange > 0.001, $"Primary SID fixture was unexpectedly quiet: {singleRange:0.000000}.");
		Assert.InRange(dualRange / singleRange, 0.99, 1.01);
	}

	private static SidSystem CreateSystem(bool extraChip)
	{
		var placements = extraChip
			? new[]
			{
				new SidChipPlacement(0, 0xD400, SidChipModel.Mos6581),
				new SidChipPlacement(1, 0xD420, SidChipModel.Mos6581)
			}
			: new[] { new SidChipPlacement(0, 0xD400, SidChipModel.Mos6581) };
		return new SidSystem(placements, SidChipModel.Mos6581);
	}

	private static void ConfigureQuietSaw(SidSystem sid)
	{
		Assert.True(sid.TryWrite(0xD400, 0x00, 0));
		Assert.True(sid.TryWrite(0xD401, 0x08, 0));
		Assert.True(sid.TryWrite(0xD405, 0x00, 0));
		Assert.True(sid.TryWrite(0xD406, 0xF0, 0));
		Assert.True(sid.TryWrite(0xD404, 0x21, 0));
		Assert.True(sid.TryWrite(0xD418, 0x02, 0));
	}

	private static double[] RenderSamples(SidSystem sid, int warmupSamples, int measuredSamples)
	{
		const int cyclesPerSample = 32;
		for (var i = 1; i <= warmupSamples; i++)
		{
			sid.RenderSample(i * cyclesPerSample);
		}

		var samples = new double[measuredSamples];
		for (var i = 0; i < samples.Length; i++)
		{
			samples[i] = sid.RenderSample((warmupSamples + i + 1) * cyclesPerSample);
		}

		return samples;
	}
}
