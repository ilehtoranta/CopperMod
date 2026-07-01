/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

namespace CopperPad;

internal static class InputNormalization
{
	public static double NormalizeAxis(
		int raw,
		int minimum,
		int maximum,
		int? center = null,
		bool invert = false,
		double deadzone = 0.1,
		double saturation = 1.0)
	{
		if (maximum <= minimum)
		{
			return 0;
		}

		var midpoint = center ?? (minimum + ((maximum - minimum) / 2));
		var value = raw >= midpoint
			? (double)(raw - midpoint) / Math.Max(1, maximum - midpoint)
			: (double)(raw - midpoint) / Math.Max(1, midpoint - minimum);
		value = Math.Clamp(value, -1.0, 1.0);
		if (invert)
		{
			value = -value;
		}

		return ApplyDeadzone(value, deadzone, saturation);
	}

	public static double NormalizeTrigger(int raw, int minimum, int maximum, double deadzone = 0.0, double saturation = 1.0)
	{
		if (maximum <= minimum)
		{
			return 0;
		}

		var value = Math.Clamp((double)(raw - minimum) / (maximum - minimum), 0.0, 1.0);
		if (value <= Math.Clamp(deadzone, 0, 0.95))
		{
			return 0;
		}

		return Math.Clamp(value / Math.Max(0.01, saturation), 0.0, 1.0);
	}

	public static (bool Up, bool Down, bool Left, bool Right) HatToDirections(int value)
		=> value switch
		{
			0 => (true, false, false, false),
			1 => (true, false, false, true),
			2 => (false, false, false, true),
			3 => (false, true, false, true),
			4 => (false, true, false, false),
			5 => (false, true, true, false),
			6 => (false, false, true, false),
			7 => (true, false, true, false),
			_ => (false, false, false, false)
		};

	private static double ApplyDeadzone(double value, double deadzone, double saturation)
	{
		var abs = Math.Abs(value);
		var clampedDeadzone = Math.Clamp(deadzone, 0, 0.95);
		if (abs <= clampedDeadzone)
		{
			return 0;
		}

		var usableRange = Math.Max(0.01, 1.0 - clampedDeadzone);
		var scaled = (abs - clampedDeadzone) / usableRange;
		scaled = Math.Clamp(scaled / Math.Max(0.01, saturation), 0.0, 1.0);
		return Math.CopySign(scaled, value);
	}
}

internal sealed class CopperControllerSnapshotBuilder
{
	private static readonly IReadOnlySet<ControllerProfileKind> MappedProfiles = new HashSet<ControllerProfileKind>
	{
		ControllerProfileKind.StandardGamepad,
		ControllerProfileKind.ExtendedGamepad,
		ControllerProfileKind.RawInput
	};

	private static readonly IReadOnlySet<ControllerProfileKind> RawOnlyProfiles = new HashSet<ControllerProfileKind>
	{
		ControllerProfileKind.RawInput
	};

	public bool A;
	public bool B;
	public bool X;
	public bool Y;
	public bool LeftShoulder;
	public bool RightShoulder;
	public bool Back;
	public bool Start;
	public bool Guide;
	public bool LeftStick;
	public bool RightStick;
	public bool DPadUp;
	public bool DPadDown;
	public bool DPadLeft;
	public bool DPadRight;
	public double LeftX;
	public double LeftY;
	public double RightX;
	public double RightY;
	public double LeftTrigger;
	public double RightTrigger;
	public string? Diagnostic;

	public CopperControllerSnapshot Build(HidDeviceDescriptor device, DateTimeOffset timestamp, ControllerMappingInfo mapping)
	{
		var elements = new Dictionary<ControllerElement, ControllerElementValue>
		{
			[ControllerElement.South] = ControllerElementValue.Button(A),
			[ControllerElement.East] = ControllerElementValue.Button(B),
			[ControllerElement.West] = ControllerElementValue.Button(X),
			[ControllerElement.North] = ControllerElementValue.Button(Y),
			[ControllerElement.LeftShoulder] = ControllerElementValue.Button(LeftShoulder),
			[ControllerElement.RightShoulder] = ControllerElementValue.Button(RightShoulder),
			[ControllerElement.Select] = ControllerElementValue.Button(Back),
			[ControllerElement.Start] = ControllerElementValue.Button(Start),
			[ControllerElement.Menu] = ControllerElementValue.Button(Guide),
			[ControllerElement.LeftStickButton] = ControllerElementValue.Button(LeftStick),
			[ControllerElement.RightStickButton] = ControllerElementValue.Button(RightStick),
			[ControllerElement.DPadUp] = ControllerElementValue.Button(DPadUp),
			[ControllerElement.DPadDown] = ControllerElementValue.Button(DPadDown),
			[ControllerElement.DPadLeft] = ControllerElementValue.Button(DPadLeft),
			[ControllerElement.DPadRight] = ControllerElementValue.Button(DPadRight),
			[ControllerElement.LeftStickX] = ControllerElementValue.Axis(LeftX),
			[ControllerElement.LeftStickY] = ControllerElementValue.Axis(LeftY),
			[ControllerElement.RightStickX] = ControllerElementValue.Axis(RightX),
			[ControllerElement.RightStickY] = ControllerElementValue.Axis(RightY),
			[ControllerElement.LeftTrigger] = ControllerElementValue.Trigger(LeftTrigger),
			[ControllerElement.RightTrigger] = ControllerElementValue.Trigger(RightTrigger)
		};

		return new CopperControllerSnapshot(
			device.Id,
			timestamp,
			true,
			device.ProductName,
			device.VendorId,
			device.ProductId,
			device.Transport,
			elements,
			MappedProfiles,
			ResolveMappingSource(mapping),
			mapping.Name,
			Diagnostic);
	}

	public static CopperControllerSnapshot Disconnected(HidDeviceDescriptor device, DateTimeOffset timestamp, ControllerMappingInfo? mapping, string? diagnostic)
		=> new(
			device.Id,
			timestamp,
			false,
			device.ProductName,
			device.VendorId,
			device.ProductId,
			device.Transport,
			new Dictionary<ControllerElement, ControllerElementValue>(),
			mapping == null ? RawOnlyProfiles : MappedProfiles,
			mapping == null ? ControllerMappingSource.None : ResolveMappingSource(mapping),
			mapping?.Name,
			diagnostic);

	public static CopperControllerInfo ToInfo(HidDeviceDescriptor device, bool connected, ControllerMappingInfo? mapping, string? diagnostic)
		=> new(
			device.Id,
			device.ProductName,
			device.VendorId,
			device.ProductId,
			device.Transport,
			connected,
			mapping == null ? RawOnlyProfiles : MappedProfiles,
			mapping == null ? ControllerMappingSource.None : ResolveMappingSource(mapping),
			mapping?.Name,
			diagnostic);

	private static ControllerMappingSource ResolveMappingSource(ControllerMappingInfo mapping)
		=> mapping.Source switch
		{
			"User profile" => ControllerMappingSource.UserProfile,
			"SDL DB" => ControllerMappingSource.SdlGameControllerDb,
			"Provider native" => ControllerMappingSource.ProviderNative,
			"Fallback" => ControllerMappingSource.Fallback,
			_ => ControllerMappingSource.None
		};
}
