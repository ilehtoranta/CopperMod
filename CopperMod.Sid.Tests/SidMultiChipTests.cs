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
		var single = CreateSystem(extraChip: false, SidEmulationProfile.ReferenceMeasured);
		var dual = CreateSystem(extraChip: true, SidEmulationProfile.ReferenceMeasured);
		ConfigureQuietSaw(single);
		ConfigureQuietSaw(dual);

		var singleSamples = RenderSamples(single, 256, 64);
		var dualSamples = RenderSamples(dual, 256, 64);
		var singleRange = singleSamples.Max() - singleSamples.Min();
		var dualRange = dualSamples.Max() - dualSamples.Min();

		Assert.True(singleRange > 0.001, $"Primary SID fixture was unexpectedly quiet: {singleRange:0.000000}.");
		Assert.InRange(dualRange / singleRange, 0.99, 1.01);
	}

	[Fact]
	public void RegisterWritesRouteOnlyToAddressedSid()
	{
		var sid = CreateMixedModelSystem(SidEmulationProfile.ReferenceMeasured);

		Assert.True(sid.TryWrite(0xD404, 0x21, 0));
		Assert.True(sid.TryWrite(0xD438, 0x0A, 0));
		Assert.True(sid.TryWrite(0xD424, 0x41, 0));

		Assert.Equal(0x21, sid.Chips[0].Registers[0x04]);
		Assert.Equal(0x00, sid.Chips[0].Registers[0x18]);
		Assert.Equal(0x41, sid.Chips[1].Registers[0x04]);
		Assert.Equal(0x0A, sid.Chips[1].Registers[0x18]);
	}

	[Theory]
	[InlineData((int)SidEmulationProfile.Balanced)]
	[InlineData((int)SidEmulationProfile.ReferenceMeasured)]
	public void MixedModelOutputIsUnnormalizedSumOfIndependentChips(int profileValue)
	{
		var profile = (SidEmulationProfile)profileValue;
		var mos6581 = CreateSingleModelSystem(SidChipModel.Mos6581, profile);
		var mos8580 = CreateSingleModelSystem(SidChipModel.Mos8580, profile);
		var mixed = CreateMixedModelSystem(profile);
		ConfigureQuietSaw(mos6581, 0xD400, 0x1100);
		ConfigureQuietSaw(mos8580, 0xD400, 0x1900);
		ConfigureQuietSaw(mixed, 0xD400, 0x1100);
		ConfigureQuietSaw(mixed, 0xD420, 0x1900);

		const int cyclesPerSample = 24;
		for (var sample = 1; sample <= 320; sample++)
		{
			var cycle = sample * cyclesPerSample;
			var expected = mos6581.RenderSample(cycle) + mos8580.RenderSample(cycle);
			var actual = mixed.RenderSample(cycle);
			Assert.InRange(Math.Abs(expected), 0.0, 0.95);
			Assert.InRange(actual - expected, -2.0e-6, 2.0e-6);
		}
	}

	private static SidSystem CreateSystem(bool extraChip, SidEmulationProfile profile)
	{
		var placements = extraChip
			? new[]
			{
				new SidChipPlacement(0, 0xD400, SidChipModel.Mos6581),
				new SidChipPlacement(1, 0xD420, SidChipModel.Mos6581)
			}
			: new[] { new SidChipPlacement(0, 0xD400, SidChipModel.Mos6581) };
		return new SidSystem(placements, SidChipModel.Mos6581, sidEmulationProfile: profile);
	}

	private static SidSystem CreateSingleModelSystem(SidChipModel model, SidEmulationProfile profile)
		=> new SidSystem(
			new[] { new SidChipPlacement(0, 0xD400, model) },
			model,
			sidEmulationProfile: profile);

	private static SidSystem CreateMixedModelSystem(SidEmulationProfile profile)
		=> new SidSystem(
			new[]
			{
				new SidChipPlacement(0, 0xD400, SidChipModel.Mos6581),
				new SidChipPlacement(1, 0xD420, SidChipModel.Mos8580)
			},
			SidChipModel.Mos6581,
			sidEmulationProfile: profile);

	private static void ConfigureQuietSaw(SidSystem sid, ushort sidBase = 0xD400, ushort frequency = 0x0800)
	{
		Assert.True(sid.TryWrite(sidBase, (byte)frequency, 0));
		Assert.True(sid.TryWrite((ushort)(sidBase + 1), (byte)(frequency >> 8), 0));
		Assert.True(sid.TryWrite((ushort)(sidBase + 5), 0x00, 0));
		Assert.True(sid.TryWrite((ushort)(sidBase + 6), 0xF0, 0));
		Assert.True(sid.TryWrite((ushort)(sidBase + 4), 0x21, 0));
		Assert.True(sid.TryWrite((ushort)(sidBase + 0x18), 0x02, 0));
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
