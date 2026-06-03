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
		AssertProfile(
			"expanded-jit-realfast-copperstart",
			AmigaMachineProfile.A500Pal512KBoot,
			CopperScreenKickstartSource.CopperStart,
			512 * 1024,
			2,
			AgnusTimingMode.SlotEngine,
			M68kBackendKind.JitM68000,
			2 * 1024 * 1024);
		AssertProfile(
			"expanded-jit-realfast-kickstart13",
			AmigaMachineProfile.A500Pal512KBoot,
			CopperScreenKickstartSource.Kickstart13Rom,
			512 * 1024,
			2,
			AgnusTimingMode.SlotEngine,
			M68kBackendKind.JitM68000,
			2 * 1024 * 1024);
		AssertProfile("diagnostic-slotengine-copperstart", AmigaMachineProfile.A500Pal512KBoot, CopperScreenKickstartSource.CopperStart, 512 * 1024, 2, AgnusTimingMode.SlotEngine);
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
			Assert.Equal(0, profile.RealFastRamSize);
			Assert.Equal(M68kBackendKind.AccurateM68000, profile.CpuBackend);
			Assert.Equal(AgnusTimingMode.SlotEngine, profile.AgnusTimingMode);
			Assert.False(profile.FloppyDriveAudio.Enabled);
			Assert.Equal(FloppyDriveAudioOptions.DefaultSoundPack, profile.FloppyDriveAudio.SoundPack);
			Assert.Equal(FloppyDriveAudioOptions.DefaultVolume, profile.FloppyDriveAudio.Volume);
		}
		finally
		{
			File.Delete(profilePath);
		}
	}

	[Fact]
	public void StartupProfileCanEnableFloppyDriveAudio()
	{
		var profilePath = Path.Combine(Path.GetTempPath(), "copperscreen-audio-profile.json");
		File.WriteAllText(
			profilePath,
			"""
			{
			  "id": "custom-audio",
			  "displayName": "Custom Audio",
			  "machine": {
			    "model": "A500PAL",
			    "chipRamKb": 512,
			    "pseudoFastRamKb": 512
			  },
			  "audio": {
			    "floppyDriveSounds": {
			      "enabled": true,
			      "soundPack": "bench-pack",
			      "volume": 0.6
			    }
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

			Assert.True(profile.FloppyDriveAudio.Enabled);
			Assert.Equal("bench-pack", profile.FloppyDriveAudio.SoundPack);
			Assert.Equal(0.6f, profile.FloppyDriveAudio.Volume);
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
	public void StartupArgumentParserCanOverrideAgnusTimingMode()
	{
		var options = CopperScreenStartupOptions.Parse(
			new[] { "--profile", "expanded-copperstart", "--agnus-timing", "legacy" },
			AppContext.BaseDirectory);

		Assert.Null(options.Error);
		Assert.Equal(AgnusTimingMode.SlotEngine, options.Profile.AgnusTimingMode);
		Assert.Equal(AgnusTimingMode.LegacyReservation, options.AgnusTimingModeOverride);
	}

	[Fact]
	public void StartupArgumentParserCanOverrideCpuBackend()
	{
		var options = CopperScreenStartupOptions.Parse(
			new[] { "--profile", "expanded-copperstart", "--jit" },
			AppContext.BaseDirectory);

		Assert.Null(options.Error);
		Assert.Equal(M68kBackendKind.AccurateM68000, options.Profile.CpuBackend);
		Assert.Equal(M68kBackendKind.JitM68000, options.CpuBackendOverride);

		var explicitOptions = CopperScreenStartupOptions.Parse(
			new[] { "--cpu", "interpreter" },
			AppContext.BaseDirectory);
		Assert.Equal(M68kBackendKind.AccurateM68000, explicitOptions.CpuBackendOverride);
	}

	[Fact]
	public void StartupArgumentParserCanOverrideFloppyDriveAudio()
	{
		var options = CopperScreenStartupOptions.Parse(
			new[]
			{
				"--profile",
				"expanded-copperstart",
				"--floppy-sounds",
				"on",
				"--floppy-sound-pack",
				".\\Sounds\\CustomFloppy",
				"--floppy-sound-volume",
				"0.75"
			},
			AppContext.BaseDirectory);

		Assert.Null(options.Error);
		Assert.True(options.FloppyDriveAudio.Enabled);
		Assert.Equal(".\\Sounds\\CustomFloppy", options.FloppyDriveAudio.SoundPack);
		Assert.Equal(0.75f, options.FloppyDriveAudio.Volume);

		var disabled = CopperScreenStartupOptions.Parse(
			new[] { "--profile", "expanded-copperstart", "--floppy-sounds=off", "--floppy-sound-volume", "5" },
			AppContext.BaseDirectory);
		Assert.Null(disabled.Error);
		Assert.False(disabled.FloppyDriveAudio.Enabled);
		Assert.Equal(1f, disabled.FloppyDriveAudio.Volume);
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

	[Fact]
	public void CrashLogUsesStableTimestampedFileNames()
	{
		var fileName = CopperScreenCrashLog.CreateLogFileName(new DateTimeOffset(2026, 5, 29, 13, 45, 12, TimeSpan.Zero), 1234);

		Assert.Equal("CopperScreen-20260529-134512-1234.log", fileName);
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
		=> AssertProfile(id, expectedMachineProfile, expectedKickstartSource, expectedExpansionRamSize, expectedFloppyDriveCount, AgnusTimingMode.SlotEngine);

	private static void AssertProfile(
		string id,
		AmigaMachineProfile expectedMachineProfile,
		CopperScreenKickstartSource expectedKickstartSource,
		int expectedExpansionRamSize,
		int expectedFloppyDriveCount,
		AgnusTimingMode expectedAgnusTimingMode)
		=> AssertProfile(
			id,
			expectedMachineProfile,
			expectedKickstartSource,
			expectedExpansionRamSize,
			expectedFloppyDriveCount,
			expectedAgnusTimingMode,
			M68kBackendKind.AccurateM68000,
			0);

	private static void AssertProfile(
		string id,
		AmigaMachineProfile expectedMachineProfile,
		CopperScreenKickstartSource expectedKickstartSource,
		int expectedExpansionRamSize,
		int expectedFloppyDriveCount,
		AgnusTimingMode expectedAgnusTimingMode,
		M68kBackendKind expectedCpuBackend,
		int expectedRealFastRamSize)
	{
		Assert.True(CopperScreenProfile.TryLoad(id, AppContext.BaseDirectory, out var profile, out var error), error);
		Assert.Equal(expectedMachineProfile, profile.MachineProfile);
		Assert.Equal(expectedKickstartSource, profile.KickstartSource);
		Assert.Equal(512 * 1024, profile.ChipRamSize);
		Assert.Equal(expectedExpansionRamSize, profile.ExpansionRamSize);
		Assert.Equal(expectedRealFastRamSize, profile.RealFastRamSize);
		Assert.Equal(expectedCpuBackend, profile.CpuBackend);
		Assert.Equal(expectedFloppyDriveCount, profile.FloppyDriveCount);
		Assert.Equal(expectedAgnusTimingMode, profile.AgnusTimingMode);
		Assert.False(profile.FloppyDriveAudio.Enabled);
		Assert.Equal(FloppyDriveAudioOptions.DefaultSoundPack, profile.FloppyDriveAudio.SoundPack);
		Assert.Equal(FloppyDriveAudioOptions.DefaultVolume, profile.FloppyDriveAudio.Volume);
		Assert.Equal(expectedFloppyDriveCount, profile.CreateMachineOptions().FloppyDriveCount);
		Assert.Equal(expectedAgnusTimingMode, profile.CreateMachineOptions().AgnusTimingMode);
		Assert.Equal(expectedCpuBackend, profile.CreateMachineOptions().CpuBackend);
		Assert.Equal(expectedRealFastRamSize, profile.CreateMachineOptions().RealFastRamSize);
	}
}
