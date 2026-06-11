using CopperPad;

public sealed class ControllerProfileTests
{
	[Fact]
	public void JsonSerializer_RoundTripsProfileSet()
	{
		var profiles = new ControllerProfileSet
		{
			Profiles =
			[
				new ControllerProfile
				{
					Name = "Arcade stick",
					VendorId = 1,
					ProductId = 2,
					ProductNameContains = "Arcade",
					Bindings =
					[
						new ControllerBinding
						{
							Target = VirtualXboxControl.A,
							Source = new ControllerBindingSource
							{
								Kind = ControllerBindingSourceKind.ReportBit,
								Offset = 4,
								Bit = 2
							}
						}
					]
				}
			]
		};

		var json = JsonControllerProfileSerializer.Serialize(profiles);
		var roundTripped = JsonControllerProfileSerializer.Deserialize(json);

		Assert.Contains("\"schemaVersion\": 1", json);
		var profile = Assert.Single(roundTripped.Profiles);
		Assert.Equal("Arcade stick", profile.Name);
		var binding = Assert.Single(profile.Bindings);
		Assert.Equal(VirtualXboxControl.A, binding.Target);
		Assert.Equal(ControllerBindingSourceKind.ReportBit, binding.Source.Kind);
	}

	[Fact]
	public void ProfileMatch_UsesVidPidNameAndOptionalDeviceId()
	{
		var profile = new ControllerProfile
		{
			VendorId = 0x1234,
			ProductId = 0x5678,
			ProductNameContains = "Pad"
		};
		var info = new ControllerInfo("device-1", "Excellent Pad", 0x1234, 0x5678, ControllerTransport.Usb, true, null);

		Assert.True(profile.Matches(info));
		Assert.False((profile with { ProductNameContains = "Wheel" }).Matches(info));
		Assert.False((profile with { VendorId = 0x9999 }).Matches(info));
	}
}
