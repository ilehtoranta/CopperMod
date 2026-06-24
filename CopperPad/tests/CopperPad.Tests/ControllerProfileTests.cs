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
							Target = ControllerElement.A,
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

		Assert.Contains("\"schemaVersion\": 2", json);
		var profile = Assert.Single(roundTripped.Profiles);
		Assert.Equal("Arcade stick", profile.Name);
		var binding = Assert.Single(profile.Bindings);
		Assert.Equal(ControllerElement.A, binding.Target);
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
		var info = new CopperControllerInfo(
			"device-1",
			"Excellent Pad",
			0x1234,
			0x5678,
			ControllerTransport.Usb,
			true,
			new HashSet<ControllerProfileKind> { ControllerProfileKind.RawInput },
			ControllerMappingSource.None,
			null,
			null);

		Assert.True(profile.Matches(info));
		Assert.False((profile with { ProductNameContains = "Wheel" }).Matches(info));
		Assert.False((profile with { VendorId = 0x9999 }).Matches(info));
	}

	[Fact]
	public void JsonSerializer_UpgradesSchemaV1ProfilesToV2()
	{
		var json =
			"""
			{
			  "schemaVersion": 1,
			  "profiles": [
			    {
			      "name": "Legacy Pad",
			      "vendorId": 121,
			      "productId": 6,
			      "productNameContains": "Joystick",
			      "bindings": [
			        {
			          "target": "a",
			          "source": { "kind": "reportBit", "offset": 0, "bit": 1 }
			        }
			      ]
			    }
			  ]
			}
			""";

		var profiles = JsonControllerProfileSerializer.Deserialize(json);

		Assert.Equal(2, profiles.SchemaVersion);
		var profile = Assert.Single(profiles.Profiles);
		Assert.Equal("Legacy Pad", profile.Name);
		Assert.Equal(0x0079, profile.VendorId);
		Assert.Equal(0x0006, profile.ProductId);
		Assert.Equal(ControllerElement.A, Assert.Single(profile.Bindings).Target);
	}
}
