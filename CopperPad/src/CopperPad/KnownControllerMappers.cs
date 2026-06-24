/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

namespace CopperPad;

internal static class KnownControllerMapper
{
	public static bool TryCreate(HidDeviceDescriptor device, out IControllerMapper mapper)
	{
		if (IsPlayStation(device))
		{
			mapper = new PlayStationControllerMapper();
			return true;
		}

		if (IsSwitch(device))
		{
			mapper = new SwitchProControllerMapper();
			return true;
		}

		if (IsXbox(device))
		{
			mapper = new XboxControllerMapper();
			return true;
		}

		mapper = null!;
		return false;
	}

	public static bool IsKnownController(HidDeviceDescriptor device)
		=> IsPlayStation(device) || IsSwitch(device) || IsXbox(device);

	private static bool IsPlayStation(HidDeviceDescriptor device)
		=> device.VendorId == 0x054C ||
			device.ProductName.Contains("dualshock", StringComparison.OrdinalIgnoreCase) ||
			device.ProductName.Contains("dualsense", StringComparison.OrdinalIgnoreCase);

	private static bool IsSwitch(HidDeviceDescriptor device)
		=> device.VendorId == 0x057E ||
			device.ProductName.Contains("switch pro", StringComparison.OrdinalIgnoreCase) ||
			device.ProductName.Contains("pro controller", StringComparison.OrdinalIgnoreCase);

	private static bool IsXbox(HidDeviceDescriptor device)
		=> device.VendorId == 0x045E ||
			device.ProductName.Contains("xbox", StringComparison.OrdinalIgnoreCase);
}

internal sealed class PlayStationControllerMapper : IControllerMapper
{
	public string Name => "known-playstation";
	public ControllerMappingInfo MappingInfo { get; } = new("Fallback", "Known PlayStation HID");

	public VirtualXboxControllerState Map(RawControllerInput input)
	{
		var report = input.Report;
		var offset = input.Length > 6 && report[0] is 0x01 or 0x11 or 0x31 ? 1 : 0;
		var builder = new VirtualXboxStateBuilder();
		if (input.Length - offset < 6)
		{
			builder.Diagnostic = "PlayStation HID report is too short.";
			return builder.Build(input.Device, input.Timestamp);
		}

		builder.LeftX = InputNormalization.NormalizeAxis(report[offset], 0, 255);
		builder.LeftY = InputNormalization.NormalizeAxis(report[offset + 1], 0, 255, invert: true);
		builder.RightX = InputNormalization.NormalizeAxis(report[offset + 2], 0, 255);
		builder.RightY = InputNormalization.NormalizeAxis(report[offset + 3], 0, 255, invert: true);

		var faceAndHat = report[offset + 4];
		GenericConventionControllerMapper.ApplyHat(builder, faceAndHat & 0x0F);
		builder.X = (faceAndHat & 0x10) != 0; // Square, west
		builder.A = (faceAndHat & 0x20) != 0; // Cross, south
		builder.B = (faceAndHat & 0x40) != 0; // Circle, east
		builder.Y = (faceAndHat & 0x80) != 0; // Triangle, north

		var buttons = report[offset + 5];
		builder.LeftShoulder = (buttons & 0x01) != 0;
		builder.RightShoulder = (buttons & 0x02) != 0;
		builder.LeftTrigger = (buttons & 0x04) != 0 ? 1 : 0;
		builder.RightTrigger = (buttons & 0x08) != 0 ? 1 : 0;
		builder.Back = (buttons & 0x10) != 0;
		builder.Start = (buttons & 0x20) != 0;
		builder.LeftStick = (buttons & 0x40) != 0;
		builder.RightStick = (buttons & 0x80) != 0;
		if (input.Length - offset > 8)
		{
			builder.LeftTrigger = InputNormalization.NormalizeTrigger(report[offset + 7], 0, 255);
			builder.RightTrigger = InputNormalization.NormalizeTrigger(report[offset + 8], 0, 255);
		}

		if (input.Length - offset > 6)
		{
			builder.Guide = (report[offset + 6] & 0x01) != 0;
		}

		return builder.Build(input.Device, input.Timestamp);
	}
}

internal sealed class SwitchProControllerMapper : IControllerMapper
{
	public string Name => "known-switch-pro";
	public ControllerMappingInfo MappingInfo { get; } = new("Fallback", "Known Switch Pro HID");

	public VirtualXboxControllerState Map(RawControllerInput input)
	{
		var report = input.Report;
		var offset = input.Length > 12 && report[0] == 0x30 ? 3 : 0;
		var builder = new VirtualXboxStateBuilder();
		if (input.Length - offset < 7)
		{
			builder.Diagnostic = "Switch Pro HID report is too short.";
			return builder.Build(input.Device, input.Timestamp);
		}

		var rightButtons = report[offset];
		builder.Y = (rightButtons & 0x01) != 0;
		builder.X = (rightButtons & 0x02) != 0;
		builder.B = (rightButtons & 0x04) != 0;
		builder.A = (rightButtons & 0x08) != 0;
		builder.RightShoulder = (rightButtons & 0x40) != 0;
		builder.RightTrigger = (rightButtons & 0x80) != 0 ? 1 : 0;

		var shared = report[offset + 1];
		builder.Back = (shared & 0x01) != 0;
		builder.Start = (shared & 0x02) != 0;
		builder.LeftStick = (shared & 0x04) != 0;
		builder.RightStick = (shared & 0x08) != 0;
		builder.Guide = (shared & 0x10) != 0;

		var leftButtons = report[offset + 2];
		builder.DPadDown = (leftButtons & 0x01) != 0;
		builder.DPadUp = (leftButtons & 0x02) != 0;
		builder.DPadRight = (leftButtons & 0x04) != 0;
		builder.DPadLeft = (leftButtons & 0x08) != 0;
		builder.LeftShoulder = (leftButtons & 0x40) != 0;
		builder.LeftTrigger = (leftButtons & 0x80) != 0 ? 1 : 0;

		builder.LeftX = InputNormalization.NormalizeAxis(Read12(report[offset + 3], report[offset + 4]), 0, 4095);
		builder.LeftY = InputNormalization.NormalizeAxis(Read12(report[offset + 4] >> 4, report[offset + 5]), 0, 4095, invert: true);
		if (input.Length - offset >= 9)
		{
			builder.RightX = InputNormalization.NormalizeAxis(Read12(report[offset + 6], report[offset + 7]), 0, 4095);
			builder.RightY = InputNormalization.NormalizeAxis(Read12(report[offset + 7] >> 4, report[offset + 8]), 0, 4095, invert: true);
		}

		return builder.Build(input.Device, input.Timestamp);
	}

	private static int Read12(int lowByte, int highNibbleByte)
		=> lowByte | ((highNibbleByte & 0x0F) << 8);
}

internal sealed class XboxControllerMapper : IControllerMapper
{
	public string Name => "known-xbox-hid";
	public ControllerMappingInfo MappingInfo { get; } = new("Fallback", "Known Xbox HID");

	public VirtualXboxControllerState Map(RawControllerInput input)
	{
		var report = input.Report;
		var offset = input.Length > 13 && report[0] is 0x01 or 0x20 ? 1 : 0;
		var builder = new VirtualXboxStateBuilder();
		if (input.Length - offset < 12)
		{
			builder.Diagnostic = "Xbox HID report is too short or exposed in a non-HID driver mode.";
			return builder.Build(input.Device, input.Timestamp);
		}

		var buttons = report[offset];
		builder.A = (buttons & 0x01) != 0;
		builder.B = (buttons & 0x02) != 0;
		builder.X = (buttons & 0x04) != 0;
		builder.Y = (buttons & 0x08) != 0;
		builder.LeftShoulder = (buttons & 0x10) != 0;
		builder.RightShoulder = (buttons & 0x20) != 0;
		builder.Back = (buttons & 0x40) != 0;
		builder.Start = (buttons & 0x80) != 0;
		var buttons2 = report[offset + 1];
		builder.LeftStick = (buttons2 & 0x01) != 0;
		builder.RightStick = (buttons2 & 0x02) != 0;
		builder.Guide = (buttons2 & 0x04) != 0;
		GenericConventionControllerMapper.ApplyHat(builder, report[offset + 2] & 0x0F);
		builder.LeftTrigger = InputNormalization.NormalizeTrigger(report[offset + 3], 0, 255);
		builder.RightTrigger = InputNormalization.NormalizeTrigger(report[offset + 4], 0, 255);
		builder.LeftX = InputNormalization.NormalizeAxis(BitConverter.ToInt16(report, offset + 5), short.MinValue, short.MaxValue, 0);
		builder.LeftY = InputNormalization.NormalizeAxis(BitConverter.ToInt16(report, offset + 7), short.MinValue, short.MaxValue, 0, invert: true);
		builder.RightX = InputNormalization.NormalizeAxis(BitConverter.ToInt16(report, offset + 9), short.MinValue, short.MaxValue, 0);
		if (input.Length - offset >= 13)
		{
			builder.RightY = InputNormalization.NormalizeAxis(BitConverter.ToInt16(report, offset + 11), short.MinValue, short.MaxValue, 0, invert: true);
		}

		return builder.Build(input.Device, input.Timestamp);
	}
}
