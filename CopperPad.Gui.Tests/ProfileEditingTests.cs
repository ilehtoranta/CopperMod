using CopperPad;
using CopperPad.Gui;

public sealed class ProfileEditingTests
{
	[Fact]
	public void ReportAnalyzer_SuggestsBitSourceForSingleBitChange()
	{
		var changes = ReportAnalyzer.DetectChanges([0x00], [0x04]);

		var change = Assert.Single(changes);
		Assert.Equal(0, change.Offset);
		Assert.Equal(ControllerBindingSourceKind.ReportBit, change.SuggestedSource.Kind);
		Assert.Equal(2, change.SuggestedSource.Bit);
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
					Target = VirtualXboxControl.A,
					Source = new ControllerBindingSource { Kind = ControllerBindingSourceKind.ReportBit, Offset = 2, Bit = 0 }
				},
				new ControllerBinding
				{
					Target = VirtualXboxControl.A,
					Source = new ControllerBindingSource { Kind = ControllerBindingSourceKind.ReportBit, Offset = 8, Bit = 9 }
				}
			]
		};

		var issues = ProfileEditor.ValidateProfile(profile, maxInputReportLength: 4);

		Assert.Contains(issues, issue => issue.Message.Contains("Duplicate", StringComparison.Ordinal));
		Assert.Contains(issues, issue => issue.Message.Contains("outside", StringComparison.Ordinal));
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
					Target = VirtualXboxControl.A,
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
					Target = VirtualXboxControl.B,
					Source = new ControllerBindingSource { Kind = ControllerBindingSourceKind.ReportBit, Offset = 1, Bit = 0 }
				}
			]
		};

		var merged = ProfileEditor.MergeProfile(new ControllerProfileSet { Profiles = [first] }, replacement);

		var profile = Assert.Single(merged.Profiles);
		Assert.Equal(VirtualXboxControl.B, Assert.Single(profile.Bindings).Target);
	}

	[Fact]
	public void DefaultProfilePath_UsesUserAppDataCopperPadLocation()
	{
		var path = CopperPadProfilePaths.GetDefaultProfilePath();

		Assert.EndsWith(Path.Combine("CopperMod", "CopperPad", "profiles.json"), path, StringComparison.Ordinal);
		Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), path, StringComparison.OrdinalIgnoreCase);
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
							Target = VirtualXboxControl.LeftX,
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
			Assert.Equal(VirtualXboxControl.LeftX, binding.Target);
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
}
