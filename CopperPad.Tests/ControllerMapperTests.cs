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

		Assert.True(state.A);
		Assert.False(state.B);
		Assert.False(state.X);
		Assert.False(state.Y);
		Assert.True(state.DPadRight);
		Assert.True(state.LeftShoulder);
		Assert.True(state.Back);
		Assert.InRange(state.LeftY, 0.99, 1.0);
		Assert.InRange(state.RightX, 0.99, 1.0);
		Assert.InRange(state.RightTrigger, 0.99, 1.0);
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

		Assert.True(state.A);
		Assert.True(state.RightShoulder);
		Assert.True(state.Guide);
		Assert.True(state.DPadLeft);
		Assert.InRange(state.LeftTrigger, 0.49, 0.51);
		Assert.InRange(state.RightTrigger, 0.99, 1.0);
		Assert.InRange(state.LeftX, 0.99, 1.0);
		Assert.InRange(state.LeftY, 0.99, 1.0);
		Assert.Equal(0, state.RightX, precision: 3);
		Assert.InRange(state.RightY, -1.0, -0.99);
	}

	[Fact]
	public void GenericMapper_DecodesConventionReport()
	{
		var device = Device(0x1111, 0x2222, "Generic HID Gamepad", isGameControllerUsage: true);
		var mapper = ControllerMapperFactory.Create(device, ControllerProfileSet.Empty);
		var report = new byte[] { 255, 0, 128, 255, 64, 255, 0b1000_0101, 1 };

		var state = mapper.Map(new RawControllerInput(device, report, report.Length, DateTimeOffset.UtcNow));

		Assert.True(state.A);
		Assert.True(state.X);
		Assert.True(state.Start);
		Assert.True(state.DPadUp);
		Assert.True(state.DPadRight);
		Assert.InRange(state.LeftX, 0.99, 1.0);
		Assert.InRange(state.LeftY, 0.99, 1.0);
		Assert.InRange(state.RightTrigger, 0.99, 1.0);
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
							Target = VirtualXboxControl.B,
							Source = new ControllerBindingSource { Kind = ControllerBindingSourceKind.ReportBit, Offset = 0, Bit = 0 }
						},
						new ControllerBinding
						{
							Target = VirtualXboxControl.LeftX,
							Source = new ControllerBindingSource { Kind = ControllerBindingSourceKind.ReportByte, Offset = 1 },
							Axis = new AxisCalibration { Minimum = 0, Maximum = 255, Deadzone = 0 }
						}
					]
				}
			]
		};

		var mapper = ControllerMapperFactory.Create(device, profiles);
		var state = mapper.Map(new RawControllerInput(device, [0x01, 255], 2, DateTimeOffset.UtcNow));

		Assert.True(state.B);
		Assert.False(state.A);
		Assert.InRange(state.LeftX, 0.99, 1.0);
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
