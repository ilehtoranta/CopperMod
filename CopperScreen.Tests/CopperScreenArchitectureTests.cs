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
		AssertProfile("expanded-diagrom", AmigaMachineProfile.A500Pal512KBoot, CopperScreenKickstartSource.DiagRom, 512 * 1024, 2);
		AssertProfile(
			"expanded-jit-realfast-copperstart",
			AmigaMachineProfile.A500Pal512KBoot,
			CopperScreenKickstartSource.CopperStart,
			512 * 1024,
			2,
			M68kBackendKind.JitM68000,
			2 * 1024 * 1024);
		AssertProfile(
			"expanded-jit-realfast-kickstart13",
			AmigaMachineProfile.A500Pal512KBoot,
			CopperScreenKickstartSource.Kickstart13Rom,
			512 * 1024,
			2,
			M68kBackendKind.JitM68000,
			2 * 1024 * 1024);
		AssertProfile("diagnostic-hrm-copperstart", AmigaMachineProfile.A500Pal512KBoot, CopperScreenKickstartSource.CopperStart, 512 * 1024, 2);

		Assert.True(CopperScreenProfile.TryLoad("diagrom", AppContext.BaseDirectory, out var diagRom, out var diagRomError), diagRomError);
		Assert.Equal("expanded-diagrom", diagRom.Id);
		Assert.Equal("ROM/DiagROM/diagrom-a500.rom", diagRom.KickstartRomPath);
		Assert.True(diagRom.BootsWithoutDisk);
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
		AmigaMachineProfile expectedMachineProfile,
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
}
