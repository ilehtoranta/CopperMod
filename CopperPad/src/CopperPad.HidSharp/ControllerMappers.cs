/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

namespace CopperPad;

internal interface IControllerMapper
{
	string Name { get; }
	ControllerMappingInfo MappingInfo { get; }

	CopperControllerSnapshot Map(RawControllerInput input);
}

internal static class ControllerMapperFactory
{
	internal static IControllerMapper Create(HidDeviceDescriptor device, ControllerProfileSet profiles)
	{
		var info = ToInfo(device, true, device.Diagnostic);
		var profile = profiles.FindMatch(info);
		if (profile != null)
		{
			return new ProfileControllerMapper(profile);
		}

		if (SdlGameControllerDatabase.TryFindMapping(device, out var sdlMapping))
		{
			return new SdlControllerMapper(sdlMapping, device);
		}

		if (KnownControllerMapper.TryCreate(device, out var known))
		{
			return known;
		}

		if (device.IsGameControllerUsage)
		{
			return new GenericConventionControllerMapper();
		}

		return new DiagnosticControllerMapper("No HID gamepad usage or matching profile was found.");
	}

	internal static ControllerMappingInfo Describe(HidDeviceDescriptor device, ControllerProfileSet profiles)
		=> Create(device, profiles).MappingInfo;

	internal static bool IsCandidate(HidDeviceDescriptor device, ControllerProfileSet profiles, bool requireGameControllerUsage = false)
	{
		if (requireGameControllerUsage && !device.IsGameControllerUsage)
		{
			return false;
		}

		if (profiles.FindMatch(ToInfo(device, true, device.Diagnostic)) != null)
		{
			return true;
		}

		return device.IsGameControllerUsage ||
			SdlGameControllerDatabase.TryFindMapping(device, out _) ||
			KnownControllerMapper.IsKnownController(device) ||
			device.ProductName.Contains("gamepad", StringComparison.OrdinalIgnoreCase) ||
			device.ProductName.Contains("controller", StringComparison.OrdinalIgnoreCase) ||
			device.ProductName.Contains("joystick", StringComparison.OrdinalIgnoreCase);
	}

	private static CopperControllerInfo ToInfo(HidDeviceDescriptor device, bool connected, string? diagnostic)
		=> new(
			device.Id,
			device.ProductName,
			device.VendorId,
			device.ProductId,
			device.Transport,
			connected,
			new HashSet<ControllerProfileKind> { ControllerProfileKind.RawInput },
			ControllerMappingSource.None,
			null,
			diagnostic);
}

internal sealed class DiagnosticControllerMapper(string diagnostic) : IControllerMapper
{
	public string Name => "diagnostic";
	public ControllerMappingInfo MappingInfo { get; } = new("Diagnostic", diagnostic);

	public CopperControllerSnapshot Map(RawControllerInput input)
	{
		var builder = new CopperControllerSnapshotBuilder { Diagnostic = diagnostic };
		return builder.Build(input.Device, input.Timestamp, MappingInfo);
	}
}

internal sealed class GenericConventionControllerMapper : IControllerMapper
{
	public string Name => "generic-convention";
	public ControllerMappingInfo MappingInfo { get; } = new("Fallback", "Generic HID convention");

	public CopperControllerSnapshot Map(RawControllerInput input)
	{
		var report = input.Report;
		var offset = input.Device.ReportsUseId && input.Length > 8 ? 1 : 0;
		var builder = new CopperControllerSnapshotBuilder();
		if (input.Length - offset < 8)
		{
			builder.Diagnostic = "Generic HID report is too short for the convention mapper.";
			return builder.Build(input.Device, input.Timestamp, MappingInfo);
		}

		builder.LeftX = InputNormalization.NormalizeAxis(report[offset], 0, 255);
		builder.LeftY = InputNormalization.NormalizeAxis(report[offset + 1], 0, 255, invert: true);
		builder.RightX = InputNormalization.NormalizeAxis(report[offset + 2], 0, 255);
		builder.RightY = InputNormalization.NormalizeAxis(report[offset + 3], 0, 255, invert: true);
		builder.LeftTrigger = InputNormalization.NormalizeTrigger(report[offset + 4], 0, 255);
		builder.RightTrigger = InputNormalization.NormalizeTrigger(report[offset + 5], 0, 255);
		ApplyButtonByte(builder, report[offset + 6]);
		ApplyHat(builder, report[offset + 7] & 0x0F);
		return builder.Build(input.Device, input.Timestamp, MappingInfo);
	}

	internal static void ApplyButtonByte(CopperControllerSnapshotBuilder builder, int buttons)
	{
		builder.A = (buttons & (1 << 0)) != 0;
		builder.B = (buttons & (1 << 1)) != 0;
		builder.X = (buttons & (1 << 2)) != 0;
		builder.Y = (buttons & (1 << 3)) != 0;
		builder.LeftShoulder = (buttons & (1 << 4)) != 0;
		builder.RightShoulder = (buttons & (1 << 5)) != 0;
		builder.Back = (buttons & (1 << 6)) != 0;
		builder.Start = (buttons & (1 << 7)) != 0;
	}

	internal static void ApplyHat(CopperControllerSnapshotBuilder builder, int hat)
	{
		var directions = InputNormalization.HatToDirections(hat);
		builder.DPadUp = directions.Up;
		builder.DPadDown = directions.Down;
		builder.DPadLeft = directions.Left;
		builder.DPadRight = directions.Right;
	}
}

internal sealed class ProfileControllerMapper(ControllerProfile profile) : IControllerMapper
{
	public string Name => "profile:" + profile.Name;
	public ControllerMappingInfo MappingInfo { get; } = new("User profile", profile.Name);

	public CopperControllerSnapshot Map(RawControllerInput input)
	{
		var builder = new CopperControllerSnapshotBuilder();
		foreach (var binding in profile.Bindings)
		{
			ApplyBinding(builder, binding, input.Report, input.Length);
		}

		return builder.Build(input.Device, input.Timestamp, MappingInfo);
	}

	private static void ApplyBinding(CopperControllerSnapshotBuilder builder, ControllerBinding binding, byte[] report, int length)
	{
		var source = binding.Source;
		if (source.Offset < 0 || source.Offset >= length)
		{
			return;
		}

		switch (binding.Target)
		{
			case ControllerElement.LeftStickX:
			case ControllerElement.LeftStickY:
			case ControllerElement.RightStickX:
			case ControllerElement.RightStickY:
				SetAxis(builder, binding.Target, ReadAxisSource(source, report, length), binding.Axis);
				break;
			case ControllerElement.LeftTrigger:
			case ControllerElement.RightTrigger:
				if (source.Kind == ControllerBindingSourceKind.ReportBit || source.Kind == ControllerBindingSourceKind.Hat)
				{
					SetDigitalTrigger(builder, binding.Target, ReadButtonSource(source, report, length));
				}
				else
				{
					SetTrigger(builder, binding.Target, ReadAxisSource(source, report, length), binding.Axis);
				}
				break;
			default:
				SetButton(builder, binding.Target, ReadButtonSource(source, report, length));
				break;
		}
	}

	private static int ReadAxisSource(ControllerBindingSource source, byte[] report, int length)
	{
		if (source.Kind == ControllerBindingSourceKind.ReportInt16LittleEndian && source.Offset + 1 < length)
		{
			return BitConverter.ToInt16(report, source.Offset);
		}

		return report[source.Offset];
	}

	private static bool ReadButtonSource(ControllerBindingSource source, byte[] report, int length)
		=> source.Kind switch
		{
			ControllerBindingSourceKind.ReportBit => source.Bit is >= 0 and < 8 && ReadBitSource(source, report),
			ControllerBindingSourceKind.Hat => source.HatValue.HasValue && MatchesHatDirection(report[source.Offset] & 0x0F, source.HatValue.Value),
			ControllerBindingSourceKind.ReportByte => report[source.Offset] != 0,
			ControllerBindingSourceKind.ReportInt16LittleEndian => source.Offset + 1 < length && BitConverter.ToInt16(report, source.Offset) != 0,
			_ => false
		};

	private static bool ReadBitSource(ControllerBindingSource source, byte[] report)
	{
		var isSet = (report[source.Offset] & (1 << source.Bit)) != 0;
		return source.Invert ? !isSet : isSet;
	}

	private static bool MatchesHatDirection(int actual, int expected)
	{
		var actualDirections = InputNormalization.HatToDirections(actual);
		var expectedDirections = InputNormalization.HatToDirections(expected);
		return (!expectedDirections.Up || actualDirections.Up) &&
			(!expectedDirections.Down || actualDirections.Down) &&
			(!expectedDirections.Left || actualDirections.Left) &&
			(!expectedDirections.Right || actualDirections.Right);
	}

	private static void SetAxis(CopperControllerSnapshotBuilder builder, ControllerElement control, int raw, AxisCalibration? calibration)
	{
		calibration ??= new AxisCalibration();
		var value = InputNormalization.NormalizeAxis(
			raw,
			calibration.Minimum,
			calibration.Maximum,
			calibration.Center,
			calibration.Invert,
			calibration.Deadzone,
			calibration.Saturation);
		switch (control)
		{
			case ControllerElement.LeftStickX: builder.LeftX = value; break;
			case ControllerElement.LeftStickY: builder.LeftY = value; break;
			case ControllerElement.RightStickX: builder.RightX = value; break;
			case ControllerElement.RightStickY: builder.RightY = value; break;
		}
	}

	private static void SetTrigger(CopperControllerSnapshotBuilder builder, ControllerElement control, int raw, AxisCalibration? calibration)
	{
		calibration ??= new AxisCalibration();
		var value = InputNormalization.NormalizeTrigger(raw, calibration.Minimum, calibration.Maximum, calibration.Deadzone, calibration.Saturation);
		if (control == ControllerElement.LeftTrigger)
		{
			builder.LeftTrigger = value;
		}
		else
		{
			builder.RightTrigger = value;
		}
	}

	private static void SetDigitalTrigger(CopperControllerSnapshotBuilder builder, ControllerElement control, bool pressed)
	{
		if (control == ControllerElement.LeftTrigger)
		{
			builder.LeftTrigger = pressed ? 1 : 0;
		}
		else
		{
			builder.RightTrigger = pressed ? 1 : 0;
		}
	}

	private static void SetButton(CopperControllerSnapshotBuilder builder, ControllerElement control, bool pressed)
	{
		switch (control)
		{
			case ControllerElement.A: builder.A = pressed; break;
			case ControllerElement.B: builder.B = pressed; break;
			case ControllerElement.X: builder.X = pressed; break;
			case ControllerElement.Y: builder.Y = pressed; break;
			case ControllerElement.LeftShoulder: builder.LeftShoulder = pressed; break;
			case ControllerElement.RightShoulder: builder.RightShoulder = pressed; break;
			case ControllerElement.Select: builder.Back = pressed; break;
			case ControllerElement.Start: builder.Start = pressed; break;
			case ControllerElement.Menu: builder.Guide = pressed; break;
			case ControllerElement.LeftStickButton: builder.LeftStick = pressed; break;
			case ControllerElement.RightStickButton: builder.RightStick = pressed; break;
			case ControllerElement.DPadUp: builder.DPadUp = pressed; break;
			case ControllerElement.DPadDown: builder.DPadDown = pressed; break;
			case ControllerElement.DPadLeft: builder.DPadLeft = pressed; break;
			case ControllerElement.DPadRight: builder.DPadRight = pressed; break;
		}
	}
}
