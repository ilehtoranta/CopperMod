using CopperPad;

public sealed class ControllerMapperTests
{
	[Fact]
	public void PlayStationMapper_UsesPositionalFaceButtons()
	{
		var device = Device(0x054C, 0x05C4, "DualShock 4", maxInputLength: 16);
		var mapper = new PlayStationControllerMapper();
		var report = new byte[] { 0x01, 128, 0, 255, 128, 0x22, 0x31, 0x01, 128, 255 };

		var state = mapper.Map(new RawControllerInput(device, report, report.Length, DateTimeOffset.UtcNow));

		Assert.True(state.IsPressed(ControllerElement.A));
		Assert.False(state.IsPressed(ControllerElement.B));
		Assert.False(state.IsPressed(ControllerElement.X));
		Assert.False(state.IsPressed(ControllerElement.Y));
		Assert.True(state.IsPressed(ControllerElement.DPadRight));
		Assert.True(state.IsPressed(ControllerElement.LeftShoulder));
		Assert.True(state.IsPressed(ControllerElement.Select));
		Assert.InRange(state.GetAxis(ControllerElement.LeftStickY), 0.99, 1.0);
		Assert.InRange(state.GetAxis(ControllerElement.RightStickX), 0.99, 1.0);
		Assert.InRange(state.GetAxis(ControllerElement.RightTrigger), 0.99, 1.0);
	}

	[Fact]
	public void XboxMapper_DecodesButtonsHatTriggersAndSignedAxes()
	{
		var device = Device(0x045E, 0x02EA, "Xbox Wireless Controller", maxInputLength: 16);
		var mapper = new XboxControllerMapper();
		var report = new byte[14];
		report[0] = 0x01;
		report[1] = 0x21;
		report[2] = 0x04;
		report[3] = 6;
		report[4] = 128;
		report[5] = 255;
		BitConverter.GetBytes((short)short.MaxValue).CopyTo(report, 6);
		BitConverter.GetBytes((short)short.MinValue).CopyTo(report, 8);
		BitConverter.GetBytes((short)0).CopyTo(report, 10);
		BitConverter.GetBytes((short)short.MaxValue).CopyTo(report, 12);

		var state = mapper.Map(new RawControllerInput(device, report, report.Length, DateTimeOffset.UtcNow));

		Assert.True(state.IsPressed(ControllerElement.A));
		Assert.True(state.IsPressed(ControllerElement.RightShoulder));
		Assert.True(state.IsPressed(ControllerElement.Menu));
		Assert.True(state.IsPressed(ControllerElement.DPadLeft));
		Assert.InRange(state.GetAxis(ControllerElement.LeftTrigger), 0.49, 0.51);
		Assert.InRange(state.GetAxis(ControllerElement.RightTrigger), 0.99, 1.0);
		Assert.InRange(state.GetAxis(ControllerElement.LeftStickX), 0.99, 1.0);
		Assert.InRange(state.GetAxis(ControllerElement.LeftStickY), 0.99, 1.0);
		Assert.Equal(0, state.GetAxis(ControllerElement.RightStickX), precision: 3);
		Assert.InRange(state.GetAxis(ControllerElement.RightStickY), -1.0, -0.99);
	}

	[Fact]
	public void GenericMapper_DecodesConventionReport()
	{
		var device = Device(0x1111, 0x2222, "Generic HID Gamepad", isGameControllerUsage: true);
		var mapper = ControllerMapperFactory.Create(device, ControllerProfileSet.Empty);
		var report = new byte[] { 255, 0, 128, 255, 64, 255, 0b1000_0101, 1 };

		var state = mapper.Map(new RawControllerInput(device, report, report.Length, DateTimeOffset.UtcNow));

		Assert.True(state.IsPressed(ControllerElement.A));
		Assert.True(state.IsPressed(ControllerElement.X));
		Assert.True(state.IsPressed(ControllerElement.Start));
		Assert.True(state.IsPressed(ControllerElement.DPadUp));
		Assert.True(state.IsPressed(ControllerElement.DPadRight));
		Assert.InRange(state.GetAxis(ControllerElement.LeftStickX), 0.99, 1.0);
		Assert.InRange(state.GetAxis(ControllerElement.LeftStickY), 0.99, 1.0);
		Assert.InRange(state.GetAxis(ControllerElement.RightTrigger), 0.99, 1.0);
	}

	[Fact]
	public void ProfileMapper_OverridesBuiltInMappings()
	{
		var device = Device(0x054C, 0x05C4, "DualShock 4");
		var profiles = new ControllerProfileSet
		{
			Profiles =
			[
				new ControllerProfile
				{
					Name = "test",
					VendorId = 0x054C,
					ProductId = 0x05C4,
					Bindings =
					[
						new ControllerBinding
						{
							Target = ControllerElement.B,
							Source = new ControllerBindingSource { Kind = ControllerBindingSourceKind.ReportBit, Offset = 0, Bit = 0 }
						},
						new ControllerBinding
						{
							Target = ControllerElement.LeftStickX,
							Source = new ControllerBindingSource { Kind = ControllerBindingSourceKind.ReportByte, Offset = 1 },
							Axis = new AxisCalibration { Minimum = 0, Maximum = 255, Deadzone = 0 }
						}
					]
				}
			]
		};

		var mapper = ControllerMapperFactory.Create(device, profiles);
		var state = mapper.Map(new RawControllerInput(device, [0x01, 255], 2, DateTimeOffset.UtcNow));

		Assert.True(state.IsPressed(ControllerElement.B));
		Assert.False(state.IsPressed(ControllerElement.A));
		Assert.InRange(state.GetAxis(ControllerElement.LeftStickX), 0.99, 1.0);
	}

	[Fact]
	public void ProfileMapper_SupportsActiveLowReportBits()
	{
		var device = Device(0x0079, 0x0006, "Generic USB Joystick");
		var profiles = new ControllerProfileSet
		{
			Profiles =
			[
				new ControllerProfile
				{
					Name = "active-low dpad",
					VendorId = 0x0079,
					ProductId = 0x0006,
					Bindings =
					[
						new ControllerBinding
						{
							Target = ControllerElement.DPadUp,
							Source = new ControllerBindingSource
							{
								Kind = ControllerBindingSourceKind.ReportBit,
								Offset = 6,
								Bit = 0,
								Invert = true
							}
						}
					]
				}
			]
		};

		var mapper = ControllerMapperFactory.Create(device, profiles);
		var neutral = mapper.Map(new RawControllerInput(device, [0, 0, 0, 0, 0, 0, 0x0F], 7, DateTimeOffset.UtcNow));
		var pressed = mapper.Map(new RawControllerInput(device, [0, 0, 0, 0, 0, 0, 0x0E], 7, DateTimeOffset.UtcNow));

		Assert.False(neutral.IsPressed(ControllerElement.DPadUp));
		Assert.True(pressed.IsPressed(ControllerElement.DPadUp));
	}

	[Fact]
	public void ProfileMapper_MapsDigitalTriggerBitsToTriggerAxes()
	{
		var device = Device(0x0079, 0x0006, "Generic USB Joystick");
		var profiles = new ControllerProfileSet
		{
			Profiles =
			[
				new ControllerProfile
				{
					Name = "digital triggers",
					VendorId = 0x0079,
					ProductId = 0x0006,
					Bindings =
					[
						new ControllerBinding
						{
							Target = ControllerElement.LeftTrigger,
							Source = new ControllerBindingSource
							{
								Kind = ControllerBindingSourceKind.ReportBit,
								Offset = 7,
								Bit = 0
							}
						}
					]
				}
			]
		};

		var mapper = ControllerMapperFactory.Create(device, profiles);
		var neutral = mapper.Map(new RawControllerInput(device, [0, 0, 0, 0, 0, 0, 0, 0], 8, DateTimeOffset.UtcNow));
		var pressed = mapper.Map(new RawControllerInput(device, [0, 0, 0, 0, 0, 0, 0, 1], 8, DateTimeOffset.UtcNow));

		Assert.Equal(0, neutral.GetAxis(ControllerElement.LeftTrigger), precision: 3);
		Assert.Equal(1, pressed.GetAxis(ControllerElement.LeftTrigger), precision: 3);
	}

	[Fact]
	public void UnknownDevice_ProducesDiagnosticState()
	{
		var device = Device(0x9999, 0x8888, "Mystery Device", isGameControllerUsage: false);
		var mapper = ControllerMapperFactory.Create(device, ControllerProfileSet.Empty);

		var state = mapper.Map(new RawControllerInput(device, [0], 1, DateTimeOffset.UtcNow));

		Assert.False(string.IsNullOrWhiteSpace(state.Diagnostic));
	}

	internal static HidDeviceDescriptor Device(
		int vendorId,
		int productId,
		string name,
		bool isGameControllerUsage = false,
		bool reportsUseId = false,
		int maxInputLength = 8)
		=> new(
			$"hid://{vendorId:X4}/{productId:X4}/{name}",
			name,
			vendorId,
			productId,
			ControllerTransport.Usb,
			maxInputLength,
			Array.Empty<byte>(),
			isGameControllerUsage,
			reportsUseId,
			null);
}
