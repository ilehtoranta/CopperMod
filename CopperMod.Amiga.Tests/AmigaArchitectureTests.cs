using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class AmigaArchitectureTests
{
	[Fact]
	public void AmigaCoreDoesNotReferencePlayerOrCustAssemblies()
	{
		var references = typeof(AmigaMachine).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToHashSet();

		Assert.DoesNotContain("CopperMod", references);
		Assert.DoesNotContain("CopperMod.Abstractions", references);
		Assert.DoesNotContain("CopperMod.Cust", references);
	}

	[Fact]
	public void MachineProfilesCreateA500PalZeroWaitHardwareSkeletons()
	{
		var custMachine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500PalCustPlayback));
		var emulatorSkeleton = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500PalFullEmulationSkeleton));

		Assert.Equal(AmigaMachineProfile.A500PalCustPlayback, custMachine.Profile);
		Assert.Equal(AmigaMachineProfile.A500PalFullEmulationSkeleton, emulatorSkeleton.Profile);
		Assert.IsType<ZeroWaitBusArbiter>(custMachine.Bus.Arbiter);
		Assert.IsType<ZeroWaitBusArbiter>(emulatorSkeleton.Bus.Arbiter);
		Assert.Equal(AgnusTimingMode.SlotEngine, custMachine.Options.AgnusTimingMode);
		Assert.Equal(AgnusTimingMode.SlotEngine, custMachine.Bus.AgnusTimingMode);
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
		Assert.Equal(AmigaKickstartConfiguration.HostShim13.Description, custMachine.Kickstart.Configuration.Description);
	}

	[Fact]
	public void BootProfilesExposeChipOnlyAndDefaultPseudoFastMemoryLayouts()
	{
		var chipOnly = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KChipOnlyBoot));
		var defaultBoot = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));

		Assert.Equal(AmigaConstants.A500BootChipRamSize, chipOnly.Bus.ChipRam.Length);
		Assert.Empty(chipOnly.Bus.ExpansionRam);
		Assert.Equal(AmigaConstants.A500BootChipRamSize, defaultBoot.Bus.ChipRam.Length);
		Assert.Equal(AmigaConstants.A500BootPseudoFastRamSize, defaultBoot.Bus.ExpansionRam.Length);
		Assert.Equal(AmigaConstants.A500BootPseudoFastRamBase, defaultBoot.Bus.ExpansionRamBase);
		Assert.Empty(defaultBoot.Bus.RealFastRam);
	}

	[Fact]
	public void MachineOptionsCanAddSeparateRealFastRam()
	{
		var machine = new AmigaMachine(AmigaMachineOptions
			.ForProfile(AmigaMachineProfile.A500Pal512KBoot)
			.WithRealFastRam(AmigaConstants.A500JitRealFastRamSize));

		Assert.Equal(AmigaConstants.A500JitRealFastRamSize, machine.Bus.RealFastRam.Length);
		Assert.Equal(AmigaConstants.A500RealFastRamBase, machine.Bus.RealFastRamBase);
	}

	[Fact]
	public void AgnusTimingModeDefaultsToSlotEngineAndCanSelectLegacyOptOut()
	{
		var slot = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));
		var legacy = new AmigaMachine(AmigaMachineOptions
			.ForProfile(AmigaMachineProfile.A500Pal512KBoot)
			.WithAgnusTimingMode(AgnusTimingMode.LegacyReservation)
			.WithLiveAgnusDma(false));

		Assert.Equal(AgnusTimingMode.SlotEngine, slot.Options.AgnusTimingMode);
		Assert.Equal(AgnusTimingMode.SlotEngine, slot.Bus.AgnusTimingMode);
		Assert.True(slot.Options.LiveAgnusDma);
		Assert.True(slot.Bus.LiveAgnusDmaEnabled);
		Assert.True(slot.Options.LiveDisplayDma);
		Assert.True(slot.Bus.LiveDisplayDmaEnabled);
		Assert.Equal(AgnusTimingMode.LegacyReservation, legacy.Options.AgnusTimingMode);
		Assert.Equal(AgnusTimingMode.LegacyReservation, legacy.Bus.AgnusTimingMode);
		Assert.False(legacy.Options.LiveAgnusDma);
		Assert.False(legacy.Bus.LiveAgnusDmaEnabled);
	}
}
