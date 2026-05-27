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
		Assert.Equal(AmigaKickstartConfiguration.HostShim13.Description, custMachine.Kickstart.Configuration.Description);
	}
}
