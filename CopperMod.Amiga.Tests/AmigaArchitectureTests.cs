using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class AmigaArchitectureTests
{
	[Fact]
	public void AmigaCoreDoesNotReferencePlayerOrCustAssemblies()
	{
		var references = typeof(Machine).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToHashSet();

		Assert.DoesNotContain("CopperMod", references);
		Assert.DoesNotContain("CopperMod.Abstractions", references);
		Assert.DoesNotContain("CopperMod.Cust", references);
		Assert.DoesNotContain("CopperDisk", references);
	}

	[Fact]
	public void MachineProfilesCreateA500PalZeroWaitHardwareSkeletons()
	{
		var custMachine = new Machine(MachineOptions.ForProfile(MachineProfile.A500PalCustPlayback));
		var emulatorSkeleton = new Machine(MachineOptions.ForProfile(MachineProfile.A500PalFullEmulationSkeleton));

		Assert.Equal(MachineProfile.A500PalCustPlayback, custMachine.Profile);
		Assert.Equal(MachineProfile.A500PalFullEmulationSkeleton, emulatorSkeleton.Profile);
		Assert.IsType<ZeroWaitBusArbiter>(custMachine.Bus.Arbiter);
		Assert.IsType<ZeroWaitBusArbiter>(emulatorSkeleton.Bus.Arbiter);
		Assert.True(custMachine.Options.LiveAgnusDma);
		Assert.True(custMachine.Bus.LiveAgnusDmaEnabled);
		Assert.False(custMachine.Options.LiveDisplayDma);
		Assert.False(custMachine.Bus.LiveDisplayDmaEnabled);
		Assert.Equal(0x0001_0000, custMachine.Bus.ExpansionRam.Length);
		Assert.Equal(AmigaConstants.A500PalMinimumAudioDmaPeriod, custMachine.Options.AudioDmaMinimumPeriod);
		Assert.Equal(AmigaConstants.A500PalMinimumAudioDmaPeriod, custMachine.Bus.AudioDmaMinimumPeriod);
		Assert.True(emulatorSkeleton.Options.LiveDisplayDma);
		Assert.True(emulatorSkeleton.Bus.LiveDisplayDmaEnabled);
		Assert.Equal(AmigaConstants.A500PalMinimumAudioDmaPeriod, emulatorSkeleton.Options.AudioDmaMinimumPeriod);
		Assert.Equal(AmigaConstants.A500PalMinimumAudioDmaPeriod, emulatorSkeleton.Bus.AudioDmaMinimumPeriod);
		Assert.Equal(KickstartConfiguration.HostShim13.Description, custMachine.Kickstart.Configuration.Description);
	}

	[Fact]
	public void MachineProfilesDefaultToOcsPalChipset()
	{
		foreach (var profile in Enum.GetValues<MachineProfile>())
		{
			var options = MachineOptions.ForProfile(profile);

			Assert.Equal(AmigaChipset.OcsPal, options.Chipset);
		}
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(true, false)]
	[InlineData(false, true)]
	[InlineData(true, true)]
	public void AgnusAndDeniseModelsCanBeSelectedIndependently(
		bool ecsAgnus,
		bool ecsDenise)
	{
		var agnus = ecsAgnus ? AgnusModel.Ecs : AgnusModel.Ocs;
		var denise = ecsDenise ? DeniseModel.Ecs : DeniseModel.Ocs;
		var chipset = new AmigaChipset(agnus, denise, VideoStandard.Pal);
		var options = MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithChipset(chipset);

		Assert.Equal(agnus, options.Chipset.Agnus);
		Assert.Equal(denise, options.Chipset.Denise);
		Assert.Equal(VideoStandard.Pal, options.Chipset.VideoStandard);
	}

	[Fact]
	public void ChipsetPresetsSelectExpectedModelsAndVideoStandards()
	{
		Assert.Equal(
			new AmigaChipset(AgnusModel.Ocs, DeniseModel.Ocs, VideoStandard.Pal),
			AmigaChipset.OcsPal);
		Assert.Equal(
			new AmigaChipset(AgnusModel.Ocs, DeniseModel.Ocs, VideoStandard.Ntsc),
			AmigaChipset.OcsNtsc);
		Assert.Equal(
			new AmigaChipset(AgnusModel.Ecs, DeniseModel.Ecs, VideoStandard.Pal),
			AmigaChipset.EcsPal);
		Assert.Equal(
			new AmigaChipset(AgnusModel.Ecs, DeniseModel.Ecs, VideoStandard.Ntsc),
			AmigaChipset.EcsNtsc);
	}

	[Fact]
	public void BootProfilesExposeChipOnlyAndDefaultPseudoFastMemoryLayouts()
	{
		var chipOnly = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KChipOnlyBoot));
		var defaultBoot = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot));

		Assert.Equal(AmigaConstants.A500BootChipRamSize, chipOnly.Bus.ChipRam.Length);
		Assert.Empty(chipOnly.Bus.ExpansionRam);
		Assert.False(chipOnly.Options.RealTimeClockEnabled);
		Assert.False(chipOnly.Bus.RealTimeClockEnabled);
		Assert.Equal(AmigaConstants.A500BootChipRamSize, defaultBoot.Bus.ChipRam.Length);
		Assert.Equal(AmigaConstants.A500BootPseudoFastRamSize, defaultBoot.Bus.ExpansionRam.Length);
		Assert.Equal(AmigaConstants.A500BootPseudoFastRamBase, defaultBoot.Bus.ExpansionRamBase);
		Assert.Empty(defaultBoot.Bus.RealFastRam);
		Assert.True(defaultBoot.Options.RealTimeClockEnabled);
		Assert.True(defaultBoot.Bus.RealTimeClockEnabled);
	}

	[Fact]
	public void MachineOptionsCanDisableDefaultBootRealTimeClock()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithRealTimeClock(false));

		Assert.False(machine.Options.RealTimeClockEnabled);
		Assert.False(machine.Bus.RealTimeClockEnabled);
	}

	[Fact]
	public void MachineOptionsCanAddSeparateRealFastRam()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithRealFastRam(AmigaConstants.A500JitRealFastRamSize));

		Assert.Equal(AmigaConstants.A500JitRealFastRamSize, machine.Bus.RealFastRam.Length);
		Assert.False(machine.Bus.AutoconfigFastRam!.IsConfigured);
		machine.Bus.ConfigureAutoconfigFastRamForHost();
		Assert.Equal(AmigaConstants.A500RealFastRamBase, machine.Bus.RealFastRamBase);
	}

	[Fact]
	public void MachineProfilesEnableLiveAgnusAndDisplayDmaByDefault()
	{
		var machine = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot));

		Assert.True(machine.Options.LiveAgnusDma);
		Assert.True(machine.Bus.LiveAgnusDmaEnabled);
		Assert.True(machine.Options.LiveDisplayDma);
		Assert.True(machine.Bus.LiveDisplayDmaEnabled);
	}
}
