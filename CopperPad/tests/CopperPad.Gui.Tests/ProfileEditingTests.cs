using CopperPad;
using CopperPad.Gui;

public sealed class ProfileEditingTests
{
	[Fact]
	public void StartupOptions_DetectsSmokeTestArgument()
	{
		var options = GuiStartupOptions.Parse(["--smoke-test"]);

		Assert.True(options.SmokeTest);
	}

	[Fact]
	public void ReportAnalyzer_SuggestsBitSourceForSingleBitChange()
	{
		var changes = ReportAnalyzer.DetectChanges([0x00], [0x04]);

		var change = Assert.Single(changes);
		Assert.Equal(0, change.Offset);
		Assert.Equal(ControllerBindingSourceKind.ReportBit, change.SuggestedSource.Kind);
		Assert.Equal(2, change.SuggestedSource.Bit);
		Assert.False(change.SuggestedSource.Invert);
	}

	[Fact]
	public void ReportAnalyzer_SuggestsActiveLowBitSourceForClearedBit()
	{
		var changes = ReportAnalyzer.DetectChanges([0x0F], [0x0E]);

		var change = Assert.Single(changes);
		Assert.Equal(ControllerBindingSourceKind.ReportBit, change.SuggestedSource.Kind);
		Assert.Equal(0, change.SuggestedSource.Bit);
		Assert.True(change.SuggestedSource.Invert);
		Assert.Equal("bit 0.0 active-low", ReportAnalyzer.FormatSource(change.SuggestedSource));
	}

	[Fact]
	public void ReportAnalyzer_SuggestsHatSourceForNeutralHatMovement()
	{
		var changes = ReportAnalyzer.DetectChanges([0x08], [0x00]);

		var change = Assert.Single(changes);
		Assert.Equal(ControllerBindingSourceKind.Hat, change.SuggestedSource.Kind);
		Assert.Equal(0, change.SuggestedSource.HatValue);
	}

	[Fact]
	public void ReportAnalyzer_SuggestsHatSourceForFifteenNeutralHatMovement()
	{
		var changes = ReportAnalyzer.DetectChanges([0x0F], [0x02]);

		var change = Assert.Single(changes);
		Assert.Equal(ControllerBindingSourceKind.Hat, change.SuggestedSource.Kind);
		Assert.Equal(2, change.SuggestedSource.HatValue);
	}

	[Fact]
	public void MappableTargets_UseCanonicalNamesWithoutXboxAliasDuplicates()
	{
		Assert.Equal(ProfileEditor.MappableTargets.Count, ProfileEditor.MappableTargets.Distinct().Count());
		var labels = MappingTargetItem.All.Select(item => item.ToString()).ToArray();
		Assert.DoesNotContain(labels, label => label is "A" or "B" or "X" or "Y");
		Assert.Contains(labels, label => label.Contains("South", StringComparison.Ordinal) && label.Contains("bottom face", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void GuidedMappingCapture_LocksStableChangedSourceAfterTimeout()
	{
		var capture = new GuidedMappingCapture(TimeSpan.FromMilliseconds(500));
		byte[] baseline = [0x00];
		byte[] current = [0x01];
		var change = ReportAnalyzer.GetBestChange(baseline, current);
		Assert.NotNull(change);
		var start = DateTimeOffset.UtcNow;

		Assert.False(capture.Observe(ControllerElement.South, change, baseline, current, start));
		Assert.False(capture.Observe(ControllerElement.South, change, baseline, current, start.AddMilliseconds(499)));
		Assert.True(capture.Observe(ControllerElement.South, change, baseline, current, start.AddMilliseconds(500)));

		Assert.NotNull(capture.LockedSource);
		Assert.Equal(ControllerBindingSourceKind.ReportBit, capture.LockedSource.Kind);
		Assert.Equal(0, capture.LockedSource.Bit);
	}

	[Fact]
	public void GuidedMappingCapture_LocksOneShotReportWhenSourceStillActive()
	{
		var capture = new GuidedMappingCapture(TimeSpan.FromMilliseconds(500));
		byte[] baseline = [0x00];
		byte[] current = [0x20];
		var change = ReportAnalyzer.GetBestChange(baseline, current);
		Assert.NotNull(change);
		var start = DateTimeOffset.UtcNow;

		Assert.False(capture.Observe(ControllerElement.East, change, baseline, current, start));
		Assert.True(capture.TryLockCandidate(baseline, current, start.AddMilliseconds(500)));

		Assert.NotNull(capture.LockedSource);
		Assert.Equal(5, capture.LockedSource.Bit);
	}

	[Fact]
	public void GuidedMappingCapture_DoesNotLockOneShotReportAfterRelease()
	{
		var capture = new GuidedMappingCapture(TimeSpan.FromMilliseconds(500));
		byte[] baseline = [0x00];
		byte[] pressed = [0x20];
		byte[] released = [0x00];
		var change = ReportAnalyzer.GetBestChange(baseline, pressed);
		Assert.NotNull(change);
		var start = DateTimeOffset.UtcNow;

		Assert.False(capture.Observe(ControllerElement.East, change, baseline, pressed, start));
		Assert.False(capture.TryLockCandidate(baseline, released, start.AddMilliseconds(500)));

		Assert.False(capture.IsLocked);
	}

	[Fact]
	public void GuidedMappingCapture_DoesNotLockByteCounterAsButton()
	{
		var capture = new GuidedMappingCapture(TimeSpan.Zero);
		byte[] baseline = [0x00];
		byte[] current = [0x03];
		var change = ReportAnalyzer.GetBestChange(baseline, current);
		Assert.NotNull(change);

		Assert.False(capture.Observe(ControllerElement.South, change, baseline, current, DateTimeOffset.UtcNow));

		Assert.False(capture.IsLocked);
	}

	[Fact]
	public void GuidedMappingCapture_DoesNotLockSmallAxisNoise()
	{
		var capture = new GuidedMappingCapture(TimeSpan.Zero);
		byte[] baseline = [0x80];
		byte[] current = [0x84];
		var change = ReportAnalyzer.GetBestChange(baseline, current);
		Assert.NotNull(change);

		Assert.False(capture.Observe(ControllerElement.LeftStickX, change, baseline, current, DateTimeOffset.UtcNow));

		Assert.False(capture.IsLocked);
	}

	[Fact]
	public void GuidedMappingCapture_TreatsLargeSingleBitAxisChangeAsReportByte()
	{
		var capture = new GuidedMappingCapture(TimeSpan.Zero);
		byte[] baseline = [0x80];
		byte[] current = [0x00];
		var change = ReportAnalyzer.GetBestChange(baseline, current);
		Assert.NotNull(change);
		Assert.Equal(ControllerBindingSourceKind.ReportBit, change.SuggestedSource.Kind);

		var normalized = GuidedMappingCapture.NormalizeForTarget(ControllerElement.LeftStickY, change, baseline, current);
		Assert.Equal(ControllerBindingSourceKind.ReportByte, normalized.SuggestedSource.Kind);
		Assert.Equal(0, normalized.SuggestedSource.Offset);

		var now = DateTimeOffset.UtcNow;
		Assert.False(capture.Observe(ControllerElement.LeftStickY, change, baseline, current, now));
		Assert.True(capture.Observe(ControllerElement.LeftStickY, change, baseline, current, now));

		Assert.NotNull(capture.LockedSource);
		Assert.Equal(ControllerBindingSourceKind.ReportByte, capture.LockedSource.Kind);
		Assert.Equal(0, capture.LockedSource.Offset);
	}

	[Fact]
	public void GuidedMappingCapture_AllowsDigitalTriggerBits()
	{
		var capture = new GuidedMappingCapture(TimeSpan.Zero);
		byte[] baseline = [0x00];
		byte[] current = [0x04];
		var change = ReportAnalyzer.GetBestChange(baseline, current);
		Assert.NotNull(change);

		var now = DateTimeOffset.UtcNow;
		Assert.False(capture.Observe(ControllerElement.LeftTrigger, change, baseline, current, now));
		Assert.True(capture.Observe(ControllerElement.LeftTrigger, change, baseline, current, now));

		Assert.NotNull(capture.LockedSource);
		Assert.Equal(ControllerBindingSourceKind.ReportBit, capture.LockedSource.Kind);
		Assert.Equal(2, capture.LockedSource.Bit);
	}

	[Fact]
	public void GuidedMappingCapture_CanSkipNoisySourceAndLockDPadCandidate()
	{
		byte[] baseline = [0x00, 0x00];
		byte[] current = [0x01, 0x10];
		var changes = ReportAnalyzer.DetectChanges(baseline, current);
		var ignored = ProfileEditor.GetSourceKey(changes[0].SuggestedSource);
		var candidate = changes.First(change =>
			!string.Equals(ProfileEditor.GetSourceKey(change.SuggestedSource), ignored, StringComparison.Ordinal) &&
			GuidedMappingCapture.IsPlausibleForTarget(ControllerElement.DPadUp, change, baseline, current));
		var capture = new GuidedMappingCapture(TimeSpan.Zero);

		var now = DateTimeOffset.UtcNow;
		Assert.False(capture.Observe(ControllerElement.DPadUp, candidate, baseline, current, now));
		Assert.True(capture.Observe(ControllerElement.DPadUp, candidate, baseline, current, now));

		Assert.NotNull(capture.LockedSource);
		Assert.Equal(1, capture.LockedSource.Offset);
		Assert.Equal(4, capture.LockedSource.Bit);
	}

	[Fact]
	public void GuidedMappingCapture_PrefersHatCandidateForDPad()
	{
		byte[] baseline = [0x00, 0x08];
		byte[] current = [0x80, 0x04];
		var changes = ReportAnalyzer.DetectChanges(baseline, current);
		var bitChange = changes.Single(change => change.SuggestedSource.Kind == ControllerBindingSourceKind.ReportBit);
		var hatChange = changes.Single(change => change.SuggestedSource.Kind == ControllerBindingSourceKind.Hat);

		Assert.True(
			GuidedMappingCapture.GetCandidateScore(ControllerElement.DPadDown, hatChange, baseline, current) >
			GuidedMappingCapture.GetCandidateScore(ControllerElement.DPadDown, bitChange, baseline, current));
	}

	[Fact]
	public void GuidedMappingCapture_MatchesDPadHatDirection()
	{
		byte[] baseline = [0x08];
		byte[] current = [0x04];
		var change = Assert.Single(ReportAnalyzer.DetectChanges(baseline, current));

		Assert.True(GuidedMappingCapture.IsPlausibleForTarget(ControllerElement.DPadDown, change, baseline, current));
		Assert.False(GuidedMappingCapture.IsPlausibleForTarget(ControllerElement.DPadUp, change, baseline, current));
	}

	[Fact]
	public void GuidedMappingCapture_DoesNotUseHatForFaceButton()
	{
		byte[] baseline = [0x08];
		byte[] current = [0x04];
		var change = Assert.Single(ReportAnalyzer.DetectChanges(baseline, current));

		Assert.False(GuidedMappingCapture.IsPlausibleForTarget(ControllerElement.South, change, baseline, current));
	}

	[Fact]
	public void GuidedMappingCapture_DoesNotAutoLockActiveLowFaceButton()
	{
		byte[] baseline = [0x01];
		byte[] current = [0x00];
		var change = Assert.Single(ReportAnalyzer.DetectChanges(baseline, current));
		Assert.True(change.SuggestedSource.Invert);

		Assert.False(GuidedMappingCapture.IsPlausibleForTarget(ControllerElement.East, change, baseline, current));
	}

	[Fact]
	public void GuidedMappingCapture_AllowsActiveLowDPadNibbleMask()
	{
		byte[] baseline = [0x0F];
		byte[] current = [0x0D];
		var change = Assert.Single(ReportAnalyzer.DetectChanges(baseline, current));
		Assert.True(change.SuggestedSource.Invert);

		Assert.True(GuidedMappingCapture.IsPlausibleForTarget(ControllerElement.DPadRight, change, baseline, current));
	}

	[Fact]
	public void ProfileEditor_PrefersExistingFaceButtonByteOverActiveLowNoise()
	{
		var profile = new ControllerProfile
		{
			Name = "draft",
			Bindings =
			[
				new ControllerBinding
				{
					Target = ControllerElement.South,
					Source = new ControllerBindingSource { Kind = ControllerBindingSourceKind.ReportBit, Offset = 6, Bit = 6 }
				},
				new ControllerBinding
				{
					Target = ControllerElement.East,
					Source = new ControllerBindingSource { Kind = ControllerBindingSourceKind.ReportBit, Offset = 3, Bit = 0, Invert = true }
				}
			]
		};
		var realButton = new ControllerBindingSource { Kind = ControllerBindingSourceKind.ReportBit, Offset = 6, Bit = 7 };
		var noisyButton = new ControllerBindingSource { Kind = ControllerBindingSourceKind.ReportBit, Offset = 3, Bit = 1, Invert = true };

		Assert.True(
			ProfileEditor.GetSourcePreferenceScore(profile, ControllerElement.West, realButton) >
			ProfileEditor.GetSourcePreferenceScore(profile, ControllerElement.West, noisyButton));
	}

	[Fact]
	public void GuidedMappingCapture_DetectsBitSourceReleased()
	{
		var source = new ControllerBindingSource { Kind = ControllerBindingSourceKind.ReportBit, Offset = 0, Bit = 7 };

		Assert.False(GuidedMappingCapture.IsSourceReleased(source, [0x00], [0x80]));
		Assert.True(GuidedMappingCapture.IsSourceReleased(source, [0x00], [0x00]));
	}

	[Fact]
	public void GuidedMappingCapture_DetectsActiveLowBitSourceReleased()
	{
		var source = new ControllerBindingSource { Kind = ControllerBindingSourceKind.ReportBit, Offset = 0, Bit = 0, Invert = true };

		Assert.False(GuidedMappingCapture.IsSourceReleased(source, [0x0F], [0x0E]));
		Assert.True(GuidedMappingCapture.IsSourceReleased(source, [0x0F], [0x0F]));
	}

	[Fact]
	public void GuidedMappingCapture_DetectsByteAxisReleasedWithJitter()
	{
		var source = new ControllerBindingSource { Kind = ControllerBindingSourceKind.ReportByte, Offset = 0 };

		Assert.False(GuidedMappingCapture.IsSourceReleased(source, [0x80], [0x40]));
		Assert.True(GuidedMappingCapture.IsSourceReleased(source, [0x80], [0x86]));
	}

	[Fact]
	public void AxisCalibrationCapture_TracksRangeCenterAndOptions()
	{
		var capture = new AxisCalibrationCapture();

		capture.Observe(128);
		capture.Observe(0);
		capture.Observe(255);
		capture.CaptureCenter(127);
		var calibration = capture.ToCalibration(invert: true, deadzone: 0.2, saturation: 0.75);

		Assert.Equal(0, calibration.Minimum);
		Assert.Equal(255, calibration.Maximum);
		Assert.Equal(127, calibration.Center);
		Assert.True(calibration.Invert);
		Assert.Equal(0.2, calibration.Deadzone);
		Assert.Equal(0.75, calibration.Saturation);
	}

	[Fact]
	public void ProfileValidator_BlocksDuplicateTargetsAndInvalidOffsets()
	{
		var profile = new ControllerProfile
		{
			Name = "broken",
			Bindings =
			[
				new ControllerBinding
				{
					Target = ControllerElement.A,
					Source = new ControllerBindingSource { Kind = ControllerBindingSourceKind.ReportBit, Offset = 2, Bit = 0 }
				},
				new ControllerBinding
				{
					Target = ControllerElement.A,
					Source = new ControllerBindingSource { Kind = ControllerBindingSourceKind.ReportBit, Offset = 8, Bit = 9 }
				}
			]
		};

		var issues = ProfileEditor.ValidateProfile(profile, maxInputReportLength: 4);

		Assert.Contains(issues, issue => issue.Message.Contains("Duplicate", StringComparison.Ordinal));
		Assert.Contains(issues, issue => issue.Message.Contains("outside", StringComparison.Ordinal));
	}

	[Fact]
	public void ProfileValidator_BlocksDuplicatePhysicalSources()
	{
		var profile = new ControllerProfile
		{
			Name = "duplicate sources",
			Bindings =
			[
				new ControllerBinding
				{
					Target = ControllerElement.South,
					Source = new ControllerBindingSource { Kind = ControllerBindingSourceKind.ReportBit, Offset = 3, Bit = 0 }
				},
				new ControllerBinding
				{
					Target = ControllerElement.East,
					Source = new ControllerBindingSource { Kind = ControllerBindingSourceKind.ReportBit, Offset = 3, Bit = 0 }
				}
			]
		};

		var issues = ProfileEditor.ValidateProfile(profile, maxInputReportLength: 8);

		Assert.Contains(issues, issue => issue.Message.Contains("Duplicate physical source", StringComparison.Ordinal));
	}

	[Fact]
	public void ProfileEditor_MergesProfilesByDeviceIdentity()
	{
		var first = new ControllerProfile
		{
			Name = "Pad profile",
			VendorId = 1,
			ProductId = 2,
			ProductNameContains = "Pad",
			Bindings =
			[
				new ControllerBinding
				{
					Target = ControllerElement.A,
					Source = new ControllerBindingSource { Kind = ControllerBindingSourceKind.ReportBit, Offset = 0, Bit = 0 }
				}
			]
		};
		var replacement = first with
		{
			Bindings =
			[
				new ControllerBinding
				{
					Target = ControllerElement.B,
					Source = new ControllerBindingSource { Kind = ControllerBindingSourceKind.ReportBit, Offset = 1, Bit = 0 }
				}
			]
		};

		var merged = ProfileEditor.MergeProfile(new ControllerProfileSet { Profiles = [first] }, replacement);

		var profile = Assert.Single(merged.Profiles);
		Assert.Equal(ControllerElement.B, Assert.Single(profile.Bindings).Target);
	}

	[Fact]
	public void DefaultProfilePath_UsesUserAppDataCopperPadLocation()
	{
		var path = CopperPadProfilePaths.GetDefaultProfilePath();

		Assert.EndsWith(Path.Combine("CopperMod", "CopperPad", "profiles.json"), path, StringComparison.Ordinal);
		Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), path, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void MappingDisplay_SeparatesPhysicalDeviceNameFromMappingName()
	{
		var text = MappingDisplay.Format(new ControllerMappingInfo("SDL DB", "PC Controller"));

		Assert.Equal("Mapping: SDL DB: PC Controller", text);
		Assert.DoesNotContain("Dragon", text, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void ProfileDocumentDisplay_ShowsSeparateOverrideDocumentAndState()
	{
		var profile = new ControllerProfile { Name = "Esperanza override" };
		var newText = ProfileDocumentDisplay.Format(@"C:\Users\me\AppData\Roaming\CopperMod\CopperPad\profiles.json", false, profile);
		var savedText = ProfileDocumentDisplay.Format(@"C:\Users\me\AppData\Roaming\CopperMod\CopperPad\profiles.json", true, profile);

		Assert.Contains("not found", newText, StringComparison.Ordinal);
		Assert.Contains("new override draft", newText, StringComparison.Ordinal);
		Assert.Contains("saved override", savedText, StringComparison.Ordinal);
		Assert.Contains("profiles.json", savedText, StringComparison.Ordinal);
	}

	[Fact]
	public void DeviceDisplay_KeepsLikelyGameControllersAndHidesGenericHid()
	{
		var controller = Device("Generic USB Joystick", 0x0079, 0x0006, isGameControllerUsage: false);
		var usageController = Device("HID input", 0x1234, 0x5678, isGameControllerUsage: true);
		var mouse = Device("Trust Wireless Mouse", 0x145F, 0x02D0, isGameControllerUsage: false);
		var unknownHid = Device("Unknown HID controller", 0xFFFF, 0xBACE, isGameControllerUsage: false);

		Assert.True(DeviceDisplay.IsLikelyGameController(controller));
		Assert.True(DeviceDisplay.IsLikelyGameController(usageController));
		Assert.False(DeviceDisplay.IsLikelyGameController(mouse));
		Assert.False(DeviceDisplay.IsLikelyGameController(unknownHid));
	}

	[Fact]
	public async Task FileProfileStore_RoundTripsProfiles()
	{
		var directory = Path.Combine(Path.GetTempPath(), "CopperPad.Gui.Tests", Guid.NewGuid().ToString("N"));
		var path = Path.Combine(directory, "profiles.json");
		var store = new FileControllerProfileStore(path);
		var profiles = new ControllerProfileSet
		{
			Profiles =
			[
				new ControllerProfile
				{
					Name = "Roundtrip",
					VendorId = 0x1111,
					ProductId = 0x2222,
					Bindings =
					[
						new ControllerBinding
						{
							Target = ControllerElement.LeftStickX,
							Source = new ControllerBindingSource { Kind = ControllerBindingSourceKind.ReportByte, Offset = 1 },
							Axis = new AxisCalibration { Minimum = 0, Maximum = 255, Center = 128 }
						}
					]
				}
			]
		};

		try
		{
			await store.SaveAsync(profiles);
			var loaded = await store.LoadAsync();

			var profile = Assert.Single(loaded.Profiles);
			Assert.Equal("Roundtrip", profile.Name);
			var binding = Assert.Single(profile.Bindings);
			Assert.Equal(ControllerElement.LeftStickX, binding.Target);
			Assert.Equal(128, binding.Axis?.Center);
		}
		finally
		{
			if (Directory.Exists(directory))
			{
				Directory.Delete(directory, recursive: true);
			}
		}
	}

	private static HidDeviceInfo Device(string name, int vendorId, int productId, bool isGameControllerUsage)
		=> new(
			$"hid://{vendorId:X4}/{productId:X4}/{name}",
			name,
			vendorId,
			productId,
			ControllerTransport.Usb,
			8,
			[],
			isGameControllerUsage,
			false,
			null);
}
