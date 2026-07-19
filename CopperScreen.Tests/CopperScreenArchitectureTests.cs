using System.Reflection;
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
	public void DiskPickerIncludesSupportedFloppyImageFormats()
	{
		Assert.Equal(["*.adf", "*.adz", "*.dms", "*.ipf", "*.zip"], MainWindow.AmigaDiskImagePickerPatterns);
	}

	[Fact]
	public void StartupProfilesExposeVanillaAndExpandedCopperStartAndKickstartCombinations()
	{
		AssertProfile("vanilla-copperstart", MachineProfile.A500Pal512KChipOnlyBoot, CopperScreenKickstartSource.CopperStart, 0, 1);
		AssertProfile("expanded-copperstart", MachineProfile.A500Pal512KBoot, CopperScreenKickstartSource.CopperStart, 512 * 1024, 2);
		AssertProfile("vanilla-kickstart13", MachineProfile.A500Pal512KChipOnlyBoot, CopperScreenKickstartSource.Kickstart13Rom, 0, 1);
		AssertProfile("expanded-kickstart13", MachineProfile.A500Pal512KBoot, CopperScreenKickstartSource.Kickstart13Rom, 512 * 1024, 2);
		AssertProfile("expanded-diagrom", MachineProfile.A500Pal512KBoot, CopperScreenKickstartSource.DiagRom, 512 * 1024, 2);
		AssertProfile(
			"expanded-jit-realfast-copperstart",
			MachineProfile.A500Pal512KBoot,
			CopperScreenKickstartSource.CopperStart,
			512 * 1024,
			2,
			M68kBackendKind.JitM68000,
			2 * 1024 * 1024);
		AssertProfile(
			"expanded-jit-realfast-kickstart13",
			MachineProfile.A500Pal512KBoot,
			CopperScreenKickstartSource.Kickstart13Rom,
			512 * 1024,
			2,
			M68kBackendKind.JitM68000,
			2 * 1024 * 1024);
		AssertProfile(
			"expanded-m68040-jit-kickstart-rom",
			MachineProfile.A500Pal512KBoot,
			CopperScreenKickstartSource.KickstartRom,
			512 * 1024,
			2,
			M68kBackendKind.JitM68040,
			8 * 1024 * 1024);
		AssertProfile("diagnostic-hrm-copperstart", MachineProfile.A500Pal512KBoot, CopperScreenKickstartSource.CopperStart, 512 * 1024, 2);

		Assert.True(CopperScreenProfile.TryLoad("a500-plus-ecs-pal", AppContext.BaseDirectory, out var a500Plus, out var a500PlusError), a500PlusError);
		Assert.Equal(MachineProfile.A500PlusEcsPal, a500Plus.MachineProfile);
		Assert.Equal(AmigaChipset.EcsPal, a500Plus.Chipset);
		Assert.Equal(1024 * 1024, a500Plus.ChipRamSize);
		Assert.Equal(0, a500Plus.ExpansionRamSize);
		Assert.True(a500Plus.RtcEnabled);
		Assert.Equal(2, a500Plus.FloppyDriveCount);
		Assert.Equal(KickstartVersion.Kickstart20, a500Plus.KickstartVersion);
		var a500PlusOptions = a500Plus.CreateMachineOptions();
		Assert.Equal(AmigaChipset.EcsPal, a500PlusOptions.Chipset);
		Assert.Same(KickstartConfiguration.HostShim20, a500PlusOptions.KickstartConfiguration);

		Assert.True(CopperScreenProfile.TryLoad("diagrom", AppContext.BaseDirectory, out var diagRom, out var diagRomError), diagRomError);
		Assert.Equal("expanded-diagrom", diagRom.Id);
		Assert.Equal("ROM/DiagROM/diagrom-a500.rom", diagRom.KickstartRomPath);
		Assert.True(diagRom.BootsWithoutDisk);
	}

	[Fact]
	public void MissingChipsetProfileFieldsRemainBackwardCompatibleOcsPal()
	{
		Assert.True(CopperScreenProfile.TryLoad("expanded-copperstart", AppContext.BaseDirectory, out var profile, out var error), error);

		Assert.Equal(AmigaChipset.OcsPal, profile.Chipset);
		Assert.Equal(AmigaChipset.OcsPal, profile.CreateMachineOptions().Chipset);
	}

	[Fact]
	public void ProfileAutoStartStartupSequenceDefaultsToTrueUnlessExplicitlyDisabled()
	{
		var directory = Path.Combine(Path.GetTempPath(), "copperscreen-autostart-default-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		try
		{
			Assert.True(LoadProfile("absent-workbench", null).AutoStartWorkbenchStartupSequence);
			Assert.True(LoadProfile("empty-workbench", "\"workbench\": {}").AutoStartWorkbenchStartupSequence);
			Assert.False(LoadProfile("disabled-workbench", "\"workbench\": { \"autoStartStartupSequence\": false }").AutoStartWorkbenchStartupSequence);
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}

		CopperScreenProfile LoadProfile(string id, string? workbenchJson)
		{
			var workbenchBlock = workbenchJson == null ? string.Empty : "," + Environment.NewLine + "  " + workbenchJson;
			var profilePath = Path.Combine(directory, id + ".json");
			File.WriteAllText(
				profilePath,
				$$"""
				{
				  "id": "{{id}}",
				  "displayName": "{{id}}",
				  "machine": {
				    "model": "A500PAL",
				    "chipRamKb": 512,
				    "pseudoFastRamKb": 512
				  },
				  "kickstart": {
				    "source": "CopperStart",
				    "version": "1.3"
				  }{{workbenchBlock}}
				}
				""");

			Assert.True(CopperScreenProfile.TryLoad(profilePath, AppContext.BaseDirectory, out var profile, out var error), error);
			return profile;
		}
	}

	[Fact]
	public void BundledKickstart31CopperHdfProfileResolvesHardDrive()
	{
		Assert.True(
			CopperScreenProfile.TryLoad(
				"copperhdf",
				AppContext.BaseDirectory,
				out var profile,
				out var error),
			error);

		Assert.Equal("expanded-m68040-jit-kickstart31-copperhdf", profile.Id);
		Assert.Equal(CopperScreenKickstartSource.KickstartRom, profile.KickstartSource);
		Assert.Equal("ROM/kickstart-3.1-a500.rom", profile.KickstartRomPath);
		Assert.Equal(M68kBackendKind.JitM68040, profile.CpuBackend);
		var hardDrive = Assert.Single(profile.HardDrives);
		Assert.Equal(0, hardDrive.Unit);
		Assert.Equal("TestImages/Hardfiles/copperhdf.hdf", hardDrive.Path);
		Assert.False(hardDrive.ReadOnly);
		Assert.Equal(32L * 1024L * 1024L, hardDrive.CreateSizeBytes);

		var options = CopperScreenStartupOptions.Parse(
			new[] { "--profile", "copperhdf" },
			AppContext.BaseDirectory);
		Assert.Null(options.Error);
		Assert.Null(options.DiskPath);
		var startupHardDrive = Assert.Single(options.HardDrives);
		Assert.EndsWith(Path.Combine("TestImages", "Hardfiles", "copperhdf.hdf"), startupHardDrive.Path);
		Assert.Equal(32L * 1024L * 1024L, startupHardDrive.CreateSizeBytes);
	}

	[Fact]
	public void M68040KickstartRomProfilesAutoStartStartupSequence()
	{
		Assert.True(CopperScreenProfile.TryLoad("expanded-m68040-kickstart-rom", AppContext.BaseDirectory, out var accurate, out var accurateError), accurateError);
		Assert.True(CopperScreenProfile.TryLoad("expanded-m68040-jit-kickstart-rom", AppContext.BaseDirectory, out var jit, out var jitError), jitError);

		Assert.True(accurate.AutoStartWorkbenchStartupSequence);
		Assert.True(jit.AutoStartWorkbenchStartupSequence);
	}

	[Fact]
	public void KickstartRomProfilesDoNotEnableSyntheticWorkbenchStartupRunner()
	{
		var directory = Path.Combine(Path.GetTempPath(), "copperscreen-rom-autostart-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		try
		{
			var romPath = Path.Combine(directory, "kick.rom");
			File.WriteAllBytes(romPath, new byte[8]);
			var copperStartProfile = WriteProfile("host-startup", "CopperStart");
			var kickstartRomProfile = WriteProfile("rom-startup", "KickstartRom");

			using var copperStart = CopperScreenEmulator.Create(["--profile", copperStartProfile], AppContext.BaseDirectory);
			var copperStartBoot = GetBoot(copperStart);
			Assert.True(copperStartBoot.AutoRunStartupSequence);
			Assert.True(copperStartBoot.AutoStartWorkbenchDefaultTool);

			using var kickstartRom = CopperScreenEmulator.Create(
				["--profile", kickstartRomProfile, "--kickstart-rom", romPath],
				AppContext.BaseDirectory);
			var kickstartRomBoot = GetBoot(kickstartRom);
			Assert.False(kickstartRomBoot.AutoRunStartupSequence);
			Assert.False(kickstartRomBoot.AutoStartWorkbenchDefaultTool);
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}

		string WriteProfile(string id, string kickstartSource)
		{
			var profilePath = Path.Combine(directory, id + ".json");
			File.WriteAllText(
				profilePath,
				$$"""
				{
				  "id": "{{id}}",
				  "displayName": "{{id}}",
				  "machine": {
				    "model": "A500PAL",
				    "chipRamKb": 512,
				    "pseudoFastRamKb": 512
				  },
				  "kickstart": {
				    "source": "{{kickstartSource}}"
				  },
				  "workbench": {
				    "autoStartStartupSequence": true
				  }
				}
				""");
			return profilePath;
		}
	}

	[Fact]
	public void StartupArgumentParserCanAttachHardfile()
	{
		var hdfPath = Path.Combine(Path.GetTempPath(), "startup-hardfile-" + Guid.NewGuid().ToString("N") + ".hdf");
		try
		{
			File.WriteAllBytes(hdfPath, new byte[1024]);
			var options = CopperScreenStartupOptions.Parse(
				new[] { "--profile", "expanded-m68040-jit-kickstart-rom", "--hdf-readonly", hdfPath },
				AppContext.BaseDirectory);

			Assert.Null(options.Error);
			var hardDrive = Assert.Single(options.HardDrives);
			Assert.Equal(0, hardDrive.Unit);
			Assert.Equal(Path.GetFullPath(hdfPath), hardDrive.Path);
			Assert.True(hardDrive.ReadOnly);
		}
		finally
		{
			File.Delete(hdfPath);
		}
	}

	[Fact]
	public void WorkbenchNamedExplicitDiskPathIsPreservedLikeAnyOtherDisk()
	{
		var directory = Path.Combine(Path.GetTempPath(), "copperscreen-wb31-literal-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		try
		{
			var installPath = Path.Combine(directory, "Workbench v3.1 rev 40.29 (1993)(Commodore)(beta)(Disk 1 of 6)(Install)[m].adf");
			var workbenchPath = Path.Combine(directory, "Workbench v3.1 rev 40.29 (1993)(Commodore)(beta)(Disk 2 of 6)(Workbench)[m].adf");
			File.WriteAllBytes(installPath, CreateMinimalAmigaDosDisk(includeWorkbenchDesktop: false));
			File.WriteAllBytes(workbenchPath, CreateMinimalAmigaDosDisk(includeWorkbenchDesktop: true));

			var options = CopperScreenStartupOptions.Parse(
				new[] { "--profile", "expanded-m68040-jit-kickstart-rom", installPath },
				AppContext.BaseDirectory);

			Assert.Null(options.Error);
			Assert.Equal(Path.GetFullPath(installPath), options.DiskPath);
			Assert.Equal(Path.GetFullPath(installPath), options.DriveDiskPaths[0]);
			Assert.Null(options.DriveDiskPaths[1]);
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
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
			Assert.False(profile.RtcEnabled);
			Assert.Equal(M68kBackendKind.AccurateM68000, profile.CpuBackend);
			Assert.False(profile.FloppyDriveAudio.Enabled);
			Assert.Equal(FloppyDriveAudioMode.Synthetic, profile.FloppyDriveAudio.Mode);
			Assert.Equal(FloppyDriveAudioOptions.DefaultSoundPack, profile.FloppyDriveAudio.SoundPack);
			Assert.Equal(FloppyDriveAudioOptions.DefaultVolume, profile.FloppyDriveAudio.Volume);
			Assert.Equal(CopperScreenLacedPresentationMode.CrtFlicker, profile.PresentationOptions.LacedMode);
		}
		finally
		{
			File.Delete(profilePath);
		}
	}

	[Fact]
	public void StartupProfileCanSelectCrtFlickerLacedPresentation()
	{
		var profilePath = Path.Combine(Path.GetTempPath(), "copperscreen-crt-flicker-profile.json");
		File.WriteAllText(
			profilePath,
			"""
			{
			  "id": "custom-crt-flicker",
			  "displayName": "Custom CRT Flicker",
			  "machine": {
			    "model": "A500PAL",
			    "chipRamKb": 512,
			    "pseudoFastRamKb": 512
			  },
			  "kickstart": {
			    "source": "CopperStart",
			    "version": "1.3"
			  },
			  "presentation": {
			    "lacedMode": "CrtFlicker"
			  }
			}
			""");
		try
		{
			Assert.True(CopperScreenProfile.TryLoad(profilePath, AppContext.BaseDirectory, out var profile, out var error), error);

			Assert.Equal(CopperScreenLacedPresentationMode.CrtFlicker, profile.PresentationOptions.LacedMode);
		}
		finally
		{
			File.Delete(profilePath);
		}
	}

	[Fact]
	public void StartupProfileCanSelectStableWeaveLacedPresentation()
	{
		var profilePath = Path.Combine(Path.GetTempPath(), "copperscreen-stable-weave-profile.json");
		File.WriteAllText(
			profilePath,
			"""
			{
			  "id": "custom-stable-weave",
			  "displayName": "Custom Stable Weave",
			  "machine": {
			    "model": "A500PAL",
			    "chipRamKb": 512,
			    "pseudoFastRamKb": 512
			  },
			  "kickstart": {
			    "source": "CopperStart",
			    "version": "1.3"
			  },
			  "presentation": {
			    "lacedMode": "StableWeave"
			  }
			}
			""");
		try
		{
			Assert.True(CopperScreenProfile.TryLoad(profilePath, AppContext.BaseDirectory, out var profile, out var error), error);

			Assert.Equal(CopperScreenLacedPresentationMode.StableWeave, profile.PresentationOptions.LacedMode);
		}
		finally
		{
			File.Delete(profilePath);
		}
	}

	[Fact]
	public void StartupProfileRejectsInvalidLacedPresentationMode()
	{
		var profilePath = Path.Combine(Path.GetTempPath(), "copperscreen-invalid-presentation-profile.json");
		File.WriteAllText(
			profilePath,
			"""
			{
			  "id": "invalid-presentation",
			  "displayName": "Invalid Presentation",
			  "machine": {
			    "model": "A500PAL",
			    "chipRamKb": 512,
			    "pseudoFastRamKb": 512
			  },
			  "kickstart": {
			    "source": "CopperStart",
			    "version": "1.3"
			  },
			  "presentation": {
			    "lacedMode": "sparkle"
			  }
			}
			""");
		try
		{
			Assert.False(CopperScreenProfile.TryLoad(profilePath, AppContext.BaseDirectory, out _, out var error));

			Assert.Contains("presentation.lacedMode", error);
		}
		finally
		{
			File.Delete(profilePath);
		}
	}

	[Fact]
	public void StartupProfileCanDisableExpandedRealTimeClock()
	{
		var profilePath = Path.Combine(Path.GetTempPath(), "copperscreen-no-rtc-profile.json");
		File.WriteAllText(
			profilePath,
			"""
			{
			  "id": "custom-no-rtc",
			  "displayName": "Custom No RTC",
			  "machine": {
			    "model": "A500PAL",
			    "chipRamKb": 512,
			    "pseudoFastRamKb": 512,
			    "rtcEnabled": false
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

			Assert.False(profile.RtcEnabled);
			Assert.False(profile.CreateMachineOptions().RealTimeClockEnabled);
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
			      "mode": "samples",
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

			Assert.True(profile.RtcEnabled);
			Assert.True(profile.FloppyDriveAudio.Enabled);
			Assert.Equal(FloppyDriveAudioMode.Samples, profile.FloppyDriveAudio.Mode);
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
	public void StartupArgumentParserReportsWhetherProfileWasExplicit()
	{
		var implicitOptions = CopperScreenStartupOptions.Parse(Array.Empty<string>(), AppContext.BaseDirectory);
		Assert.False(implicitOptions.HasExplicitProfile);

		var profileOptions = CopperScreenStartupOptions.Parse(
			new[] { "--profile", "vanilla-copperstart" },
			AppContext.BaseDirectory);
		Assert.True(profileOptions.HasExplicitProfile);

		var realKickstartOptions = CopperScreenStartupOptions.Parse(
			new[] { "--real-kickstart" },
			AppContext.BaseDirectory);
		Assert.True(realKickstartOptions.HasExplicitProfile);
	}

	[Fact]
	public void ProfileStoreRoundTripsMediaAndInputSettings()
	{
		var baseDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Path.Combine(baseDirectory, "Profiles"));
		var diskPath = Path.Combine(baseDirectory, "disk.adf");
		File.WriteAllBytes(diskPath, new byte[AmigaDiskImage.StandardAdfSize]);
		try
		{
			var draft = CopperScreenSettingsDraft.FromProfile(CopperScreenProfile.LoadDefault(AppContext.BaseDirectory, out _));
			draft.Id = "roundtrip-settings";
			draft.DisplayName = "Roundtrip Settings";
			draft.RtcEnabled = false;
			draft.RtgVramMb = 256;
			draft.FloppyDriveCount = 3;
			draft.PresentationOptions = new CopperScreenPresentationOptions(CopperScreenLacedPresentationMode.StableWeave);
			draft.DriveDiskPaths[1] = diskPath;
			draft.DriveWriteProtected[1] = false;
			draft.Input = CopperScreenInputOptions.Create(
				2,
				CopperScreenJoystickKeyMap.Create(
					["W"],
					["S"],
					["A"],
					["D"],
					["Space"],
					["LeftCtrl"]));

			var savedPath = CopperScreenProfileStore.Save(draft, baseDirectory);

			Assert.True(CopperScreenProfile.TryLoad(savedPath, baseDirectory, out var loaded, out var error), error);
			Assert.Equal("roundtrip-settings", loaded.Id);
			Assert.False(loaded.RtcEnabled);
			Assert.Equal(256L * 1024 * 1024, loaded.RtgVramSize);
			Assert.Equal(3, loaded.FloppyDriveCount);
			Assert.Equal(CopperScreenLacedPresentationMode.StableWeave, loaded.PresentationOptions.LacedMode);
			Assert.Equal(2, loaded.Input.MousePort);
			Assert.Equal("numpad-joystick", loaded.Input.Port1ProfileId);
			Assert.Equal("mouse", loaded.Input.Port2ProfileId);
			Assert.Equal(0, loaded.Input.JoystickPortIndex);
			Assert.Contains(loaded.Input.ControllerProfiles, profile =>
				profile.Id == "numpad-joystick" &&
				profile.JoystickKeys.GetActions(Avalonia.Input.Key.W, Avalonia.Input.PhysicalKey.W) == CopperScreenJoystickActions.Up);
			var drive = Assert.Single(loaded.MediaDrives);
			Assert.Equal(1, drive.Index);
			Assert.Equal(diskPath, drive.DiskPath);
			Assert.False(drive.WriteProtected);
			Assert.Contains(CopperScreenProfileStore.ListProfiles(baseDirectory), profile => profile.Id == "roundtrip-settings");
		}
		finally
		{
			try
			{
				Directory.Delete(baseDirectory, recursive: true);
			}
			catch (IOException)
			{
			}
		}
	}

	[Fact]
	public void ProfileStoreRoundTripsExplicitEmptyAndGamepadPorts()
	{
		var baseDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Path.Combine(baseDirectory, "Profiles"));
		try
		{
			var gamepad = new CopperScreenControllerProfile(
				"gamepad-test",
				"Test Gamepad",
				CopperScreenControllerKind.Gamepad,
				CopperScreenJoystickKeyMap.Empty);
			var draft = CopperScreenSettingsDraft.FromProfile(CopperScreenProfile.LoadDefault(AppContext.BaseDirectory, out _));
			draft.Id = "roundtrip-input";
			draft.DisplayName = "Roundtrip Input";
			draft.Input = CopperScreenInputOptions.Create(
				CopperScreenControllerProfile.None.Id,
				gamepad.Id,
				CopperScreenInputOptions.DefaultControllerProfiles.Append(gamepad));

			var savedPath = CopperScreenProfileStore.Save(draft, baseDirectory);

			Assert.True(CopperScreenProfile.TryLoad(savedPath, baseDirectory, out var loaded, out var error), error);
			Assert.Equal("none", loaded.Input.Port1ProfileId);
			Assert.Equal("gamepad-test", loaded.Input.Port2ProfileId);
			Assert.Equal(CopperScreenControllerKind.None, loaded.Input.GetProfileForPort(1).Kind);
			Assert.Equal(CopperScreenControllerKind.Gamepad, loaded.Input.GetProfileForPort(2).Kind);
			Assert.Contains(loaded.Input.ControllerProfiles, profile =>
				profile.Id == "gamepad-test" &&
				profile.DisplayName == "Test Gamepad" &&
				profile.Kind == CopperScreenControllerKind.Gamepad);
		}
		finally
		{
			try
			{
				Directory.Delete(baseDirectory, recursive: true);
			}
			catch (IOException)
			{
			}
		}
	}

	[Fact]
	public void ProfileStoreRoundTripsHardfilePartitionMetadata()
	{
		var baseDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Path.Combine(baseDirectory, "Profiles"));
		try
		{
			var profilePath = Path.Combine(baseDirectory, "Profiles", "hdf-metadata.json");
			File.WriteAllText(profilePath, """
			{
			  "id": "hdf-metadata",
			  "displayName": "HDF Metadata",
			  "machine": {
			    "model": "A500PAL",
			    "chipRamKb": 512,
			    "pseudoFastRamKb": 512
			  },
			  "kickstart": {
			    "source": "KickstartRom"
			  },
			  "hardDrives": [
			    {
			      "unit": 2,
			      "path": "disk.hdf",
			      "readOnly": true,
			      "mode": "partition",
			      "partition": {
			        "deviceName": "DH7",
			        "surfaces": 4,
			        "sectorsPerBlock": 1,
			        "blocksPerTrack": 63,
			        "reservedBlocks": 4,
			        "numBuffers": 80,
			        "maxTransfer": "$001FE000",
			        "mask": "$7FFFFFFC",
			        "bootPriority": 3,
			        "dosType": "DOS\\6"
			      }
			    }
			  ]
			}
			""");

			Assert.True(CopperScreenProfile.TryLoad(profilePath, baseDirectory, out var profile, out var error), error);
			var drive = Assert.Single(profile.HardDrives);
			Assert.Equal(2, drive.Unit);
			Assert.True(drive.ReadOnly);
			Assert.Equal(AmigaHardfileMountMode.Partition, drive.Mode);
			Assert.NotNull(drive.Partition);
			Assert.Equal("DH7", drive.Partition.DeviceName);
			Assert.Equal(4u, drive.Partition.Surfaces);
			Assert.Equal(63u, drive.Partition.BlocksPerTrack);
			Assert.Equal(0x001F_E000u, drive.Partition.MaxTransfer);
			Assert.Equal(0x7FFF_FFFCu, drive.Partition.Mask);
			Assert.Equal(3, drive.Partition.BootPriority);
			Assert.Equal(0x444F_5306u, drive.Partition.DosType);

			var draft = CopperScreenSettingsDraft.FromProfile(profile);
			var savedPath = CopperScreenProfileStore.Save(draft, baseDirectory);
			Assert.True(CopperScreenProfile.TryLoad(savedPath, baseDirectory, out var loaded, out error), error);
			var loadedDrive = Assert.Single(loaded.HardDrives);
			Assert.Equal(AmigaHardfileMountMode.Partition, loadedDrive.Mode);
			Assert.NotNull(loadedDrive.Partition);
			Assert.Equal("DH7", loadedDrive.Partition.DeviceName);
			Assert.Equal(0x444F_5306u, loadedDrive.Partition.DosType);
			Assert.Equal(0x001F_E000u, loadedDrive.Partition.MaxTransfer);
		}
		finally
		{
			try
			{
				Directory.Delete(baseDirectory, recursive: true);
			}
			catch (IOException)
			{
			}
		}
	}

	[Fact]
	public void ProfileStoreRoundTripsKickstartRomPath()
	{
		var baseDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Path.Combine(baseDirectory, "Profiles"));
		try
		{
			var draft = CopperScreenSettingsDraft.FromProfile(CopperScreenProfile.LoadDefault(AppContext.BaseDirectory, out _));
			draft.Id = "roundtrip-diagrom";
			draft.DisplayName = "Roundtrip DiagROM";
			draft.KickstartSource = CopperScreenKickstartSource.DiagRom;
			draft.KickstartRomPath = "ROM/DiagROM/diagrom.rom";

			var savedPath = CopperScreenProfileStore.Save(draft, baseDirectory);

			Assert.True(CopperScreenProfile.TryLoad(savedPath, baseDirectory, out var loaded, out var error), error);
			Assert.Equal(CopperScreenKickstartSource.DiagRom, loaded.KickstartSource);
			Assert.Equal("ROM/DiagROM/diagrom.rom", loaded.KickstartRomPath);
			Assert.True(loaded.BootsWithoutDisk);
		}
		finally
		{
			try
			{
				Directory.Delete(baseDirectory, recursive: true);
			}
			catch (IOException)
			{
			}
		}
	}

	[Fact]
	public void ProfileStoreRoundTripsGenericKickstartRomSource()
	{
		var baseDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Path.Combine(baseDirectory, "Profiles"));
		try
		{
			var draft = CopperScreenSettingsDraft.FromProfile(CopperScreenProfile.LoadDefault(AppContext.BaseDirectory, out _));
			draft.Id = "roundtrip-kickstart-rom";
			draft.DisplayName = "Roundtrip Kickstart ROM";
			draft.KickstartSource = CopperScreenKickstartSource.KickstartRom;
			draft.KickstartRomPath = "ROM/Kickstart.rom";
			draft.CpuBackend = M68kBackendKind.AccurateM68040;

			var savedPath = CopperScreenProfileStore.Save(draft, baseDirectory);

			Assert.True(CopperScreenProfile.TryLoad(savedPath, baseDirectory, out var loaded, out var error), error);
			Assert.Equal(CopperScreenKickstartSource.KickstartRom, loaded.KickstartSource);
			Assert.Equal("ROM/Kickstart.rom", loaded.KickstartRomPath);
			Assert.Equal(M68kBackendKind.AccurateM68040, loaded.CpuBackend);
			Assert.True(loaded.UsesKickstartRom);
			Assert.False(loaded.BootsWithoutDisk);
		}
		finally
		{
			try
			{
				Directory.Delete(baseDirectory, recursive: true);
			}
			catch (IOException)
			{
			}
		}
	}

	[Fact]
	public void ProfileStoreSaveAsCreatesNonOverwritingCopy()
	{
		var baseDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Path.Combine(baseDirectory, "Profiles"));
		try
		{
			var draft = CopperScreenSettingsDraft.FromProfile(CopperScreenProfile.LoadDefault(AppContext.BaseDirectory, out _));
			draft.Id = "copy-source";
			draft.DisplayName = "Copy Source";
			var first = CopperScreenProfileStore.Save(draft, baseDirectory);
			var copy = CopperScreenProfileStore.SaveAs(draft, baseDirectory);

			Assert.True(File.Exists(first));
			Assert.True(File.Exists(copy));
			Assert.NotEqual(first, copy);
			Assert.EndsWith("copy-source-copy.json", copy);
		}
		finally
		{
			try
			{
				Directory.Delete(baseDirectory, recursive: true);
			}
			catch (IOException)
			{
			}
		}
	}

	[Fact]
	public void ProfileSummaryDisplaysNameIdAndPath()
	{
		var summary = new CopperScreenProfileSummary("demo-profile", "Demo Profile", @"C:\Profiles\demo-profile.json");

		Assert.Equal(@"Demo Profile (demo-profile) - C:\Profiles\demo-profile.json", summary.ToString());
	}

	[Fact]
	public void ProfileStoreListsFallbackDefaultWhenNoProfilesExist()
	{
		var baseDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(baseDirectory);
		try
		{
			var profiles = CopperScreenProfileStore.ListProfiles(baseDirectory);

			var profile = Assert.Single(profiles);
			Assert.Equal(CopperScreenProfile.DefaultProfileId, profile.Id);
			Assert.Equal("Fallback default", profile.Path);
		}
		finally
		{
			Directory.Delete(baseDirectory, recursive: true);
		}
	}

	[Fact]
	public void JoystickKeyMapRejectsReservedHostShortcuts()
	{
		var map = CopperScreenJoystickKeyMap.Create(
			["F10", "W"],
			["NumLock", "S"],
			["Alt+Enter", "A"],
			["D"],
			["F11", "Space"],
			["F12", "LeftCtrl"]);

		Assert.Equal(["W"], map.Up);
		Assert.Equal(["S"], map.Down);
		Assert.Equal(["A"], map.Left);
		Assert.Equal(["D"], map.Right);
		Assert.Equal(["Space"], map.Fire);
		Assert.Equal(["LeftCtrl"], map.SecondFire);
	}

	[Fact]
	public void LegacyInputSettingsMapToControllerPortAssignments()
	{
		var profilePath = Path.Combine(Path.GetTempPath(), "copperscreen-legacy-input-profile.json");
		File.WriteAllText(
			profilePath,
			"""
			{
			  "id": "legacy-input",
			  "displayName": "Legacy Input",
			  "machine": {
			    "model": "A500PAL",
			    "chipRamKb": 512,
			    "pseudoFastRamKb": 512
			  },
			  "kickstart": {
			    "source": "CopperStart",
			    "version": "1.3"
			  },
			  "input": {
			    "mousePort": 2,
			    "joystickKeys": {
			      "up": [ "W" ],
			      "down": [ "S" ],
			      "left": [ "A" ],
			      "right": [ "D" ],
			      "fire": [ "Space" ],
			      "secondFire": [ "LeftCtrl" ]
			    }
			  }
			}
			""");
		try
		{
			Assert.True(CopperScreenProfile.TryLoad(profilePath, AppContext.BaseDirectory, out var profile, out var error), error);

			Assert.Equal("numpad-joystick", profile.Input.Port1ProfileId);
			Assert.Equal("mouse", profile.Input.Port2ProfileId);
			Assert.Equal(2, profile.Input.MousePort);
			Assert.True(profile.Input.TryGetKeyboardJoystickMap(0, out var keyMap));
			Assert.Equal(CopperScreenJoystickActions.Up, keyMap.GetActions(Avalonia.Input.Key.W, Avalonia.Input.PhysicalKey.W));
		}
		finally
		{
			File.Delete(profilePath);
		}
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
				"--floppy-sound-mode",
				"samples",
				"--floppy-sound-volume",
				"0.75"
			},
			AppContext.BaseDirectory);

		Assert.Null(options.Error);
		Assert.True(options.FloppyDriveAudio.Enabled);
		Assert.Equal(FloppyDriveAudioMode.Samples, options.FloppyDriveAudio.Mode);
		Assert.Equal(".\\Sounds\\CustomFloppy", options.FloppyDriveAudio.SoundPack);
		Assert.Equal(0.75f, options.FloppyDriveAudio.Volume);

		var disabled = CopperScreenStartupOptions.Parse(
			new[] { "--profile", "expanded-copperstart", "--floppy-sounds=off", "--floppy-sound-volume", "5" },
			AppContext.BaseDirectory);
		Assert.Null(disabled.Error);
		Assert.False(disabled.FloppyDriveAudio.Enabled);
		Assert.Equal(FloppyDriveAudioMode.Synthetic, disabled.FloppyDriveAudio.Mode);
		Assert.Equal(1f, disabled.FloppyDriveAudio.Volume);
	}

	[Fact]
	public void StartupArgumentParserRejectsUnknownFloppyDriveAudioMode()
	{
		var options = CopperScreenStartupOptions.Parse(
			new[] { "--profile", "expanded-copperstart", "--floppy-sound-mode", "vinyl" },
			AppContext.BaseDirectory);

		Assert.Contains("Unsupported floppy sound mode", options.Error);
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
	public void DiagRomProfileResolvesBundledRomAndDoesNotRequireDiskArgument()
	{
		var options = CopperScreenStartupOptions.Parse(
			new[] { "--profile", "diagrom" },
			AppContext.BaseDirectory);

		Assert.Equal("expanded-diagrom", options.Profile.Id);
		Assert.Equal(CopperScreenKickstartSource.DiagRom, options.Profile.KickstartSource);
		Assert.True(options.Profile.UsesKickstartRom);
		Assert.True(options.Profile.BootsWithoutDisk);
		Assert.Null(options.DiskPath);
		Assert.NotNull(options.KickstartRomPath);
		Assert.EndsWith(Path.Combine("ROM", "DiagROM", "diagrom-a500.rom"), options.KickstartRomPath);
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
		MachineProfile expectedMachineProfile,
		CopperScreenKickstartSource expectedKickstartSource,
		int expectedExpansionRamSize)
		=> AssertProfile(id, expectedMachineProfile, expectedKickstartSource, expectedExpansionRamSize, expectedExpansionRamSize > 0 ? 2 : 1);

	private static void AssertProfile(
		string id,
		MachineProfile expectedMachineProfile,
		CopperScreenKickstartSource expectedKickstartSource,
		int expectedExpansionRamSize,
		int expectedFloppyDriveCount)
		=> AssertProfile(
			id,
			expectedMachineProfile,
			expectedKickstartSource,
			expectedExpansionRamSize,
			expectedFloppyDriveCount,
			M68kBackendKind.AccurateM68000,
			0);

	private static void AssertProfile(
		string id,
		MachineProfile expectedMachineProfile,
		CopperScreenKickstartSource expectedKickstartSource,
		int expectedExpansionRamSize,
		int expectedFloppyDriveCount,
		M68kBackendKind expectedCpuBackend,
		int expectedRealFastRamSize)
	{
		Assert.True(CopperScreenProfile.TryLoad(id, AppContext.BaseDirectory, out var profile, out var error), error);
		Assert.Equal(expectedMachineProfile, profile.MachineProfile);
		Assert.Equal(expectedKickstartSource, profile.KickstartSource);
		Assert.Equal(512 * 1024, profile.ChipRamSize);
		Assert.Equal(expectedExpansionRamSize, profile.ExpansionRamSize);
		Assert.Equal(expectedRealFastRamSize, profile.RealFastRamSize);
		Assert.Equal(expectedExpansionRamSize > 0, profile.RtcEnabled);
		Assert.Equal(expectedCpuBackend, profile.CpuBackend);
		Assert.Equal(expectedFloppyDriveCount, profile.FloppyDriveCount);
		Assert.True(profile.FloppyDriveAudio.Enabled);
		Assert.Equal(FloppyDriveAudioMode.Synthetic, profile.FloppyDriveAudio.Mode);
		Assert.Equal(FloppyDriveAudioOptions.DefaultSoundPack, profile.FloppyDriveAudio.SoundPack);
		Assert.Equal(FloppyDriveAudioOptions.DefaultVolume, profile.FloppyDriveAudio.Volume);
		Assert.Equal(CopperScreenLacedPresentationMode.CrtFlicker, profile.PresentationOptions.LacedMode);
		Assert.Equal(expectedFloppyDriveCount, profile.CreateMachineOptions().FloppyDriveCount);
		Assert.Equal(expectedCpuBackend, profile.CreateMachineOptions().CpuBackend);
		Assert.Equal(expectedRealFastRamSize, profile.CreateMachineOptions().RealFastRamSize);
		Assert.Equal(expectedExpansionRamSize > 0, profile.CreateMachineOptions().RealTimeClockEnabled);
	}

	private static byte[] CreateMinimalAmigaDosDisk(bool includeWorkbenchDesktop)
	{
		var data = new byte[901_120];
		data[0] = (byte)'D';
		data[1] = (byte)'O';
		data[2] = (byte)'S';
		data[3] = 0;
		WriteDirectoryHeader(data, 880, 0, "Workbench", 1);
		WriteDirectoryHeader(data, 20, 880, "System", 2);
		if (includeWorkbenchDesktop)
		{
			WriteDirectoryHeader(data, 21, 20, "Workbench", unchecked((int)0xFFFF_FFFD), 64);
		}

		return data;
	}

	private static void WriteDirectoryHeader(byte[] data, int block, int parent, string name, int secondaryType, int size = 0)
	{
		var offset = block * 512;
		WriteUInt32(data, offset, 2);
		WriteUInt32(data, offset + 4, (uint)block);
		WriteUInt32(data, offset + 0x144, (uint)size);
		WriteUInt32(data, offset + 0x1F4, (uint)parent);
		WriteUInt32(data, offset + 0x1FC, unchecked((uint)secondaryType));
		var nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
		data[offset + 432] = (byte)nameBytes.Length;
		Array.Copy(nameBytes, 0, data, offset + 433, nameBytes.Length);
	}

	private static void WriteUInt32(byte[] data, int offset, uint value)
	{
		data[offset] = (byte)(value >> 24);
		data[offset + 1] = (byte)(value >> 16);
		data[offset + 2] = (byte)(value >> 8);
		data[offset + 3] = (byte)value;
	}

	private static AmigaBootController GetBoot(CopperScreenEmulator emulator)
	{
		return (AmigaBootController)typeof(CopperScreenEmulator)
			.GetField("_boot", BindingFlags.NonPublic | BindingFlags.Instance)!
			.GetValue(emulator)!;
	}
}
