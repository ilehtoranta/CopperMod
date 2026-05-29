using CopperMod.Amiga;
using CopperScreen;

namespace CopperScreen.Tests;

public sealed class CopperScreenArchitectureTests
{
    [Fact]
    public void CopperScreenReferencesOnlyTheSharedAmigaCoreFromCopperMod()
    {
        var references = typeof(CopperScreenEmulator).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToHashSet();

        Assert.Contains("CopperMod.Amiga", references);
        Assert.DoesNotContain("CopperMod.Cust", references);
        Assert.DoesNotContain("CopperMod", references);
        Assert.DoesNotContain("CopperMod.Abstractions", references);
    }

    [Fact]
    public void CopperScreenProjectDoesNotReferencePlayerProjects()
    {
        var root = FindWorkspaceDirectory();
        var projectText = File.ReadAllText(Path.Combine(root, "CopperScreen", "CopperScreen.csproj"));

        Assert.Contains("CopperMod.Amiga", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("CopperMod.Cust", projectText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CopperMod.Abstractions", projectText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("..\\CopperMod\\", projectText, StringComparison.OrdinalIgnoreCase);
    }

	[Fact]
	public void StartupProfilesExposeVanillaAndExpandedCopperStartAndKickstartCombinations()
	{
		AssertProfile("vanilla-copperstart", AmigaMachineProfile.A500Pal512KChipOnlyBoot, CopperScreenKickstartSource.CopperStart, 0, 1);
		AssertProfile("expanded-copperstart", AmigaMachineProfile.A500Pal512KBoot, CopperScreenKickstartSource.CopperStart, 512 * 1024, 2);
		AssertProfile("vanilla-kickstart13", AmigaMachineProfile.A500Pal512KChipOnlyBoot, CopperScreenKickstartSource.Kickstart13Rom, 0, 1);
		AssertProfile("expanded-kickstart13", AmigaMachineProfile.A500Pal512KBoot, CopperScreenKickstartSource.Kickstart13Rom, 512 * 1024, 2);
	}

	[Fact]
	public void StartupProfileCanBeLoadedFromExplicitJsonPath()
	{
		var profilePath = Path.Combine(Path.GetTempPath(), "copperscreen-custom-profile.json");
		File.WriteAllText(
			profilePath,
			"""
			{
			  "id": "custom-vanilla",
			  "displayName": "Custom Vanilla",
			  "machine": {
			    "model": "A500PAL",
			    "chipRamKb": 512,
			    "pseudoFastRamKb": 0
			  },
			  "kickstart": {
			    "source": "CopperStart",
			    "version": "1.3"
			  }
			}
			""");
		try
		{
			Assert.True(CopperScreenProfile.TryLoad(profilePath, AppContext.BaseDirectory, out var profile, out var error), error);
			Assert.Equal("custom-vanilla", profile.Id);
			Assert.Equal("Custom Vanilla", profile.DisplayName);
			Assert.Equal(512 * 1024, profile.ChipRamSize);
			Assert.Equal(0, profile.ExpansionRamSize);
		}
		finally
		{
			File.Delete(profilePath);
		}
	}

	[Fact]
	public void StartupArgumentParserKeepsDiskPathSeparateFromProfileOptions()
	{
		var diskPath = Path.GetTempFileName();
		try
		{
			var options = CopperScreenStartupOptions.Parse(
				new[] { "--profile", "vanilla-copperstart", diskPath },
				AppContext.BaseDirectory);

			Assert.Equal("vanilla-copperstart", options.Profile.Id);
			Assert.Equal(Path.GetFullPath(diskPath), options.DiskPath);
			Assert.Null(options.Error);
		}
		finally
		{
			File.Delete(diskPath);
		}
	}

	[Fact]
	public void KickstartRomArgumentSelectsExpandedRomProfileWhenNoProfileWasExplicit()
	{
		var options = CopperScreenStartupOptions.Parse(
			new[] { "--kickstart-rom", "ROM\\Kickstart_13.rom" },
			AppContext.BaseDirectory);

		Assert.Equal("expanded-kickstart13", options.Profile.Id);
		Assert.True(options.Profile.UsesKickstartRom);
		Assert.EndsWith(Path.Combine("ROM", "Kickstart_13.rom"), options.KickstartRomPath);
		Assert.Null(options.Error);
	}

	[Fact]
	public void EmulatorDefaultsToExpandedCopperStartProfile()
	{
		var emulator = CopperScreenEmulator.CreateWithoutDisk();

		Assert.Equal("Expanded A500 + CopperStart", emulator.ProfileName);
	}

    private static string FindWorkspaceDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CopperMod.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the CopperMod workspace.");
    }

	private static void AssertProfile(
		string id,
		AmigaMachineProfile expectedMachineProfile,
		CopperScreenKickstartSource expectedKickstartSource,
		int expectedExpansionRamSize)
		=> AssertProfile(id, expectedMachineProfile, expectedKickstartSource, expectedExpansionRamSize, expectedExpansionRamSize > 0 ? 2 : 1);

	private static void AssertProfile(
		string id,
		AmigaMachineProfile expectedMachineProfile,
		CopperScreenKickstartSource expectedKickstartSource,
		int expectedExpansionRamSize,
		int expectedFloppyDriveCount)
	{
		Assert.True(CopperScreenProfile.TryLoad(id, AppContext.BaseDirectory, out var profile, out var error), error);
		Assert.Equal(expectedMachineProfile, profile.MachineProfile);
		Assert.Equal(expectedKickstartSource, profile.KickstartSource);
		Assert.Equal(512 * 1024, profile.ChipRamSize);
		Assert.Equal(expectedExpansionRamSize, profile.ExpansionRamSize);
		Assert.Equal(expectedFloppyDriveCount, profile.FloppyDriveCount);
		Assert.Equal(expectedFloppyDriveCount, profile.CreateMachineOptions().FloppyDriveCount);
	}
}
