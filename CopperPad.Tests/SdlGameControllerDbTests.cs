using CopperPad;

public sealed class SdlGameControllerDbTests
{
	private const string UsbGamepadLine =
		"03000000790000000600000007010000,USB Gamepad,a:b2,b:b1,back:b8,dpdown:h0.4,dpleft:h0.8,dpright:h0.2,dpup:h0.1,leftshoulder:b4,leftstick:b10,lefttrigger:b6,leftx:a0,lefty:a1,rightshoulder:b5,rightstick:b11,righttrigger:b7,rightx:a3,righty:a4,start:b9,x:b3,y:b0,platform:Linux,";

	[Fact]
	public void Parser_ReadsGuidPlatformButtonsAxesHatsAndHalfAxes()
	{
		var mapping = SdlControllerMapping.TryParse(
			"03000000a306000022f6000000000000,Cyborg V.3 Rumble,a:b1,dpup:h0.1,lefttrigger:+a3,righttrigger:-a3,lefty:~a1,platform:Windows,");

		Assert.NotNull(mapping);
		Assert.Equal(0x06A3, mapping.VendorId);
		Assert.Equal(0xF622, mapping.ProductId);
		Assert.Equal("Windows", mapping.Platform);
		Assert.Contains(mapping.Bindings, binding => binding.Target == VirtualXboxControl.A && binding.Source.Kind == SdlInputSourceKind.Button);
		Assert.Contains(mapping.Bindings, binding => binding.Target == VirtualXboxControl.DPadUp && binding.Source.Kind == SdlInputSourceKind.Hat);
		Assert.Contains(mapping.Bindings, binding => binding.Target == VirtualXboxControl.LeftTrigger && binding.Source.Polarity == SdlPolarity.Positive);
		Assert.Contains(mapping.Bindings, binding => binding.Target == VirtualXboxControl.RightTrigger && binding.Source.Polarity == SdlPolarity.Negative);
		Assert.Contains(mapping.Bindings, binding => binding.Target == VirtualXboxControl.LeftY && binding.Source.Invert);
	}

	[Fact]
	public void Parser_IgnoresUnknownTargetsAndSources()
	{
		var mapping = SdlControllerMapping.TryParse("03000000010000000200000000000000,Partial Pad,a:b0,crc:abcd,leftx:weird,platform:Windows,");

		Assert.NotNull(mapping);
		var binding = Assert.Single(mapping.Bindings);
		Assert.Equal(VirtualXboxControl.A, binding.Target);
	}

	[Fact]
	public void IndexByVidPid_GroupsMappingsByHardwareIdentity()
	{
		var mappings = SdlGameControllerDatabase.Parse(
			"""
			03000000010000000200000000000000,First Pad,a:b0,platform:Windows,
			03000000010000000200000001000000,Second Pad,a:b1,platform:Linux,
			03000000030000000400000000000000,Other Pad,a:b2,platform:Windows,
			""");

		var index = SdlGameControllerDatabase.IndexByVidPid(mappings);

		Assert.Equal(2, index.Count);
		Assert.Equal(2, index[(0x0001, 0x0002)].Count);
		Assert.Single(index[(0x0003, 0x0004)]);
	}

	[Fact]
	public void Mapping_UsesSdlButtonAxisHatAndTriggerSources()
	{
		var mapping = SdlControllerMapping.TryParse(
			"03000000010000000200000000000000,Full Pad,a:b2,dpdown:h0.4,lefttrigger:+a2,righttrigger:-a2,leftx:a0,lefty:a1,rightx:~a3,righty:a4,platform:Windows,")!;
		var snapshot = new SdlInputSnapshot(
			[
				new SdlAxisValue(255, 0, 255),
				new SdlAxisValue(0, 0, 255),
				new SdlAxisValue(255, 0, 255),
				new SdlAxisValue(0, 0, 255),
				new SdlAxisValue(0, 0, 255)
			],
			new Dictionary<int, bool> { [2] = true },
			[4],
			null);

		var state = mapping.Map(ControllerMapperTests.Device(1, 2, "Full Pad"), DateTimeOffset.UtcNow, snapshot);

		Assert.True(state.A);
		Assert.True(state.DPadDown);
		Assert.InRange(state.LeftTrigger, 0.99, 1.0);
		Assert.Equal(0, state.RightTrigger, precision: 3);
		Assert.InRange(state.LeftX, 0.99, 1.0);
		Assert.InRange(state.LeftY, 0.99, 1.0);
		Assert.InRange(state.RightX, 0.99, 1.0);
		Assert.InRange(state.RightY, 0.99, 1.0);
	}

	[Fact]
	public void Mapping_UsesUsbGamepadSdlRightStickAndFaceButtons()
	{
		var mapping = SdlControllerMapping.TryParse(UsbGamepadLine)!;
		var snapshot = new SdlInputSnapshot(
			[
				new SdlAxisValue(128, 0, 255),
				new SdlAxisValue(128, 0, 255),
				new SdlAxisValue(128, 0, 255),
				new SdlAxisValue(255, 0, 255),
				new SdlAxisValue(0, 0, 255)
			],
			new Dictionary<int, bool> { [0] = true, [2] = true },
			[0],
			null);

		var state = mapping.Map(ControllerMapperTests.Device(0x0079, 0x0006, "Generic USB Joystick"), DateTimeOffset.UtcNow, snapshot);

		Assert.True(state.A);
		Assert.True(state.Y);
		Assert.InRange(state.RightX, 0.99, 1.0);
		Assert.InRange(state.RightY, 0.99, 1.0);
	}

	[Fact]
	public void Factory_UserProfileOverridesSdlDatabase()
	{
		var device = ControllerMapperTests.Device(0x0079, 0x0006, "Generic USB Joystick", isGameControllerUsage: true);
		var profiles = new ControllerProfileSet
		{
			Profiles =
			[
				new ControllerProfile
				{
					Name = "User fix",
					VendorId = 0x0079,
					ProductId = 0x0006,
					Bindings =
					[
						new ControllerBinding
						{
							Target = VirtualXboxControl.B,
							Source = new ControllerBindingSource { Kind = ControllerBindingSourceKind.ReportBit, Offset = 0, Bit = 0 }
						}
					]
				}
			]
		};

		var mapper = ControllerMapperFactory.Create(device, profiles);
		var state = mapper.Map(new RawControllerInput(device, [0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00], 7, DateTimeOffset.UtcNow));

		Assert.Equal("User profile", mapper.MappingInfo.Source);
		Assert.True(state.B);
		Assert.False(state.A);
	}

	[Fact]
	public void Factory_SdlDatabaseOverridesGenericFallbackForCurrentPlatformEntry()
	{
		var device = ControllerMapperTests.Device(0x2DC8, 0x0651, "8BitDo M30", isGameControllerUsage: true);
		var mapper = ControllerMapperFactory.Create(device, ControllerProfileSet.Empty);

		Assert.Equal("SDL DB", mapper.MappingInfo.Source);
	}

	[Fact]
	public void Factory_DoesNotUseMismatchedPlatformSdlEntryForUsbGamepad()
	{
		var device = ControllerMapperTests.Device(0x0079, 0x0006, "Generic USB Joystick", isGameControllerUsage: true);
		var mapper = ControllerMapperFactory.Create(device, ControllerProfileSet.Empty);

		Assert.NotEqual("SDL DB", mapper.MappingInfo.Source);
	}
}
