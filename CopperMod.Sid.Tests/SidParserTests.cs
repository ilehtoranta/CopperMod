using CopperMod.Abstractions;

namespace CopperMod.Sid.Tests;

public sealed class SidParserTests
{
	[Fact]
	public void CanLoadRecognizesPsidAndRsid()
	{
		var format = new SidFormat();
		var psid = SidFixtureBuilder.CreatePsid(SidFixtureBuilder.SimpleToneProgram());
		var rsid = SidFixtureBuilder.CreateRsid(SidFixtureBuilder.SimpleToneProgram());

		Assert.True(format.CanLoad(psid));
		Assert.True(format.CanLoad(rsid));
		Assert.False(format.CanLoad(new byte[] { (byte)'P', (byte)'A', (byte)'C', (byte)'K' }));
	}

	[Fact]
	public void ParseReadsHeaderMetadataAndFlags()
	{
		var data = SidFixtureBuilder.CreatePsid(SidFixtureBuilder.SimpleToneProgram());

		var module = SidParser.Parse(data);

		Assert.Equal(SidFileKind.Psid, module.Kind);
		Assert.Equal(2, module.Version);
		Assert.Equal(0x1000, module.EffectiveLoadAddress);
		Assert.Equal(0x1000, module.InitAddress);
		Assert.Equal(0x1004, module.PlayAddress);
		Assert.Equal(2, module.SubSongCount);
		Assert.Equal(1, module.DefaultSubSongIndex);
		Assert.Equal(SidClock.Pal, module.Clock);
		Assert.Equal(SidChipModel.Mos8580, module.ChipModel);
		Assert.Equal("Generated SID", module.Title);
		Assert.Equal("CopperMod", module.Author);
		Assert.Single(module.Chips);
		Assert.Equal(SidChipModel.Mos8580, module.Chips[0].Model);
	}

	[Fact]
	public void ParseReadsStandardV4ExtraSidAddressesAndPerChipModels()
	{
		var flags = (ushort)((1 << 2) | (1 << 4) | (2 << 6) | (1 << 8));
		var data = SidFixtureBuilder.CreatePsid(
			SidFixtureBuilder.SimpleToneProgram(),
			flags: flags,
			version: 4,
			secondSidAddress: 0x42,
			thirdSidAddress: 0x50);

		var module = SidParser.Parse(data);

		Assert.Collection(
			module.Chips,
			chip =>
			{
				Assert.Equal(0xD400, chip.BaseAddress);
				Assert.Equal(SidChipModel.Mos6581, chip.Model);
			},
			chip =>
			{
				Assert.Equal(0xD420, chip.BaseAddress);
				Assert.Equal(SidChipModel.Mos8580, chip.Model);
			},
			chip =>
			{
				Assert.Equal(0xD500, chip.BaseAddress);
				Assert.Equal(SidChipModel.Mos6581, chip.Model);
			});
	}

	[Fact]
	public void ParseV3UsesSecondSidAndIgnoresReservedThirdSidField()
	{
		var flags = (ushort)((1 << 2) | (2 << 4) | (1 << 6) | (1 << 8));
		var data = SidFixtureBuilder.CreatePsid(
			SidFixtureBuilder.SimpleToneProgram(),
			flags: flags,
			version: 3,
			secondSidAddress: 0x42,
			thirdSidAddress: 0x50);

		var module = SidParser.Parse(data);

		Assert.Equal(2, module.Chips.Count);
		Assert.Equal(0xD420, module.Chips[1].BaseAddress);
		Assert.Equal(SidChipModel.Mos6581, module.Chips[1].Model);
	}

	[Fact]
	public void ParseIgnoresDuplicateExtraSidAddress()
	{
		var data = SidFixtureBuilder.CreatePsid(
			SidFixtureBuilder.SimpleToneProgram(),
			version: 4,
			secondSidAddress: 0x42,
			thirdSidAddress: 0x42);

		var module = SidParser.Parse(data);

		Assert.Equal(2, module.Chips.Count);
		Assert.Contains(module.Diagnostics, diagnostic => diagnostic.Code == "SID_EXTRA_DUPLICATE_BASE");
	}

	[Fact]
	public void ParseUsesEmbeddedLoadAddressWhenHeaderLoadAddressIsZero()
	{
		var data = SidFixtureBuilder.CreateRsid(SidFixtureBuilder.SimpleToneProgram(), loadAddress: 0x2000, initAddress: 0x2000);

		var module = SidParser.Parse(data);

		Assert.Equal(SidFileKind.Rsid, module.Kind);
		Assert.Equal(0, module.LoadAddress);
		Assert.Equal(0x2000, module.EffectiveLoadAddress);
		Assert.Equal(SidFixtureBuilder.SimpleToneProgram().Length, module.Payload.Length);
	}

	[Fact]
	public void ParseDetectsBasicRsidAndKeepsDeclaredInitAddress()
	{
		var data = SidFixtureBuilder.CreateBasicRsid((10, new byte[] { 0x80 }));

		var module = SidParser.Parse(data);

		Assert.True(module.IsBasicRsid);
		Assert.Equal(0x0801, module.EffectiveLoadAddress);
		Assert.Equal(0, module.InitAddress);
		Assert.Equal(0, module.PlayAddress);
		Assert.Contains(module.Diagnostics, diagnostic => diagnostic.Code == "SID_RSID_BASIC_NATIVE_RUNNER");
	}

	[Fact]
	public void ParseRejectsInvalidRsidRestrictions()
	{
		var data = SidFixtureBuilder.CreateRsid(SidFixtureBuilder.SimpleToneProgram());
		data[0x0C] = 0x10;

		Assert.Throws<UnsupportedModuleFormatException>(() => SidParser.Parse(data));
	}

	[Fact]
	public void LoadExposesSubSongSelectorAndOutputFamily()
	{
		var song = new SidFormat().Load(SidFixtureBuilder.CreatePsid(SidFixtureBuilder.SimpleToneProgram()));

		var selector = Assert.IsAssignableFrom<IModuleSubSongSelector>(song);
		var outputFamily = Assert.IsAssignableFrom<IModuleOutputFamilyProvider>(song);
		Assert.Equal(2, selector.SubSongCount);
		Assert.Equal(1, selector.DefaultSubSongIndex);
		Assert.Equal(ModuleOutputFamily.Commodore64, outputFamily.OutputFamily);
		Assert.Equal("SID", song.Metadata.FormatName);
		Assert.Equal("Mos8580", song.Metadata.Tags["ChipModel"]);
	}
}
