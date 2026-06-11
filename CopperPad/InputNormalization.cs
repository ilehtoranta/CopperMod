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

internal sealed class VirtualXboxStateBuilder
{
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

	public VirtualXboxControllerState Build(HidDeviceDescriptor device, DateTimeOffset timestamp)
		=> new(
			device.Id,
			timestamp,
			true,
			device.ProductName,
			device.VendorId,
			device.ProductId,
			device.Transport,
			A,
			B,
			X,
			Y,
			LeftShoulder,
			RightShoulder,
			Back,
			Start,
			Guide,
			LeftStick,
			RightStick,
			DPadUp,
			DPadDown,
			DPadLeft,
			DPadRight,
			LeftX,
			LeftY,
			RightX,
			RightY,
			LeftTrigger,
			RightTrigger,
			Diagnostic);
}
