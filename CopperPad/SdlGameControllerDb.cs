using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using HidSharp.Reports;
using HidSharp.Reports.Input;

namespace CopperPad;

public sealed record ControllerMappingInfo(string Source, string Name)
{
	public override string ToString()
		=> string.IsNullOrWhiteSpace(Name) ? Source : $"{Source}: {Name}";
}

internal static class SdlGameControllerDatabase
{
	private const string ResourceName = "CopperPad.ThirdParty.SDL_GameControllerDB.gamecontrollerdb.txt";
	private static readonly Lazy<IReadOnlyList<SdlControllerMapping>> BuiltInMappings = new(LoadBuiltInMappings);

	public static bool TryFindMapping(HidDeviceDescriptor device, out SdlControllerMapping mapping)
	{
		var currentPlatform = GetCurrentPlatform();
		var candidate = BuiltInMappings.Value
			.Where(item => item.VendorId == device.VendorId && item.ProductId == device.ProductId)
			.Select(item => new { Mapping = item, Score = Score(item, device, currentPlatform) })
			.OrderByDescending(item => item.Score)
			.FirstOrDefault(item => item.Score > int.MinValue);
		if (candidate != null)
		{
			mapping = candidate.Mapping;
			return true;
		}

		mapping = null!;
		return false;
	}

	internal static IReadOnlyList<SdlControllerMapping> Parse(string text)
	{
		var mappings = new List<SdlControllerMapping>();
		using var reader = new StringReader(text);
		while (reader.ReadLine() is { } line)
		{
			var mapping = SdlControllerMapping.TryParse(line);
			if (mapping != null)
			{
				mappings.Add(mapping);
			}
		}

		return mappings;
	}

	private static IReadOnlyList<SdlControllerMapping> LoadBuiltInMappings()
	{
		var assembly = typeof(SdlGameControllerDatabase).Assembly;
		using var stream = assembly.GetManifestResourceStream(ResourceName);
		if (stream == null)
		{
			return Array.Empty<SdlControllerMapping>();
		}

		using var reader = new StreamReader(stream);
		return Parse(reader.ReadToEnd());
	}

	private static int Score(SdlControllerMapping mapping, HidDeviceDescriptor device, string currentPlatform)
	{
		var platformScore = mapping.Platform == null
			? 600
			: string.Equals(mapping.Platform, currentPlatform, StringComparison.OrdinalIgnoreCase)
				? 900
				: int.MinValue;
		if (platformScore == int.MinValue)
		{
			return int.MinValue;
		}

		return platformScore + NameScore(mapping.Name, device.ProductName);
	}

	private static int NameScore(string mappingName, string productName)
	{
		if (string.IsNullOrWhiteSpace(mappingName) || string.IsNullOrWhiteSpace(productName))
		{
			return 0;
		}

		if (string.Equals(mappingName, productName, StringComparison.OrdinalIgnoreCase))
		{
			return 200;
		}

		if (mappingName.Contains(productName, StringComparison.OrdinalIgnoreCase) ||
			productName.Contains(mappingName, StringComparison.OrdinalIgnoreCase))
		{
			return 120;
		}

		var mappingTokens = TokenizeName(mappingName);
		var productTokens = TokenizeName(productName);
		return mappingTokens.Intersect(productTokens, StringComparer.OrdinalIgnoreCase).Count() * 40;
	}

	private static HashSet<string> TokenizeName(string value)
	{
		var tokens = value
			.Split([' ', '-', '_', '/', '\\', '.', ',', ':', ';', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries)
			.Select(NormalizeToken)
			.Where(token => token.Length > 0)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		if (tokens.Contains("gamepad") || tokens.Contains("joystick") || tokens.Contains("controller") || tokens.Contains("pad"))
		{
			tokens.Add("controller");
		}

		return tokens;
	}

	private static string NormalizeToken(string token)
		=> token.Equals("gamepad", StringComparison.OrdinalIgnoreCase) ||
			token.Equals("joystick", StringComparison.OrdinalIgnoreCase) ||
			token.Equals("pad", StringComparison.OrdinalIgnoreCase)
				? "controller"
				: token;

	private static string GetCurrentPlatform()
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			return "Windows";
		}

		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
		{
			return "Mac OS X";
		}

		return "Linux";
	}
}

internal sealed class SdlControllerMapper(SdlControllerMapping mapping, HidDeviceDescriptor device) : IControllerMapper
{
	private readonly SdlHidInputDecoder _decoder = new(device);

	public string Name => "sdl:" + mapping.Name;
	public ControllerMappingInfo MappingInfo { get; } = new("SDL DB", mapping.Name);

	public VirtualXboxControllerState Map(RawControllerInput input)
	{
		var snapshot = _decoder.Decode(input);
		var state = mapping.Map(input.Device, input.Timestamp, snapshot);
		if (snapshot.Diagnostic != null && state.Diagnostic == null)
		{
			return state with { Diagnostic = snapshot.Diagnostic };
		}

		return state;
	}
}

internal sealed record SdlControllerMapping(
	string Guid,
	string Name,
	int VendorId,
	int ProductId,
	string? Platform,
	IReadOnlyList<SdlControllerBinding> Bindings)
{
	private static readonly Dictionary<string, VirtualXboxControl> Targets = new(StringComparer.OrdinalIgnoreCase)
	{
		["a"] = VirtualXboxControl.A,
		["b"] = VirtualXboxControl.B,
		["x"] = VirtualXboxControl.X,
		["y"] = VirtualXboxControl.Y,
		["back"] = VirtualXboxControl.Back,
		["start"] = VirtualXboxControl.Start,
		["guide"] = VirtualXboxControl.Guide,
		["leftshoulder"] = VirtualXboxControl.LeftShoulder,
		["rightshoulder"] = VirtualXboxControl.RightShoulder,
		["leftstick"] = VirtualXboxControl.LeftStick,
		["rightstick"] = VirtualXboxControl.RightStick,
		["dpup"] = VirtualXboxControl.DPadUp,
		["dpdown"] = VirtualXboxControl.DPadDown,
		["dpleft"] = VirtualXboxControl.DPadLeft,
		["dpright"] = VirtualXboxControl.DPadRight,
		["leftx"] = VirtualXboxControl.LeftX,
		["lefty"] = VirtualXboxControl.LeftY,
		["rightx"] = VirtualXboxControl.RightX,
		["righty"] = VirtualXboxControl.RightY,
		["lefttrigger"] = VirtualXboxControl.LeftTrigger,
		["righttrigger"] = VirtualXboxControl.RightTrigger
	};

	public ControllerMappingInfo MappingInfo { get; } = new("SDL DB", Name);

	public VirtualXboxControllerState Map(HidDeviceDescriptor device, DateTimeOffset timestamp, SdlInputSnapshot snapshot)
	{
		var builder = new VirtualXboxStateBuilder();
		foreach (var binding in Bindings)
		{
			ApplyBinding(builder, binding, snapshot);
		}

		return builder.Build(device, timestamp);
	}

	public static SdlControllerMapping? TryParse(string line)
	{
		if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
		{
			return null;
		}

		var parts = line.Split(',', StringSplitOptions.TrimEntries);
		if (parts.Length < 2 || !TryParseGuid(parts[0], out var vendorId, out var productId))
		{
			return null;
		}

		var platform = default(string);
		var bindings = new List<SdlControllerBinding>();
		for (var i = 2; i < parts.Length; i++)
		{
			var part = parts[i];
			if (part.Length == 0)
			{
				continue;
			}

			var separator = part.IndexOf(':', StringComparison.Ordinal);
			if (separator <= 0 || separator == part.Length - 1)
			{
				continue;
			}

			var key = part[..separator];
			var value = part[(separator + 1)..];
			if (string.Equals(key, "platform", StringComparison.OrdinalIgnoreCase))
			{
				platform = value;
				continue;
			}

			var targetSign = SdlPolarity.None;
			if (key[0] == '+' || key[0] == '-')
			{
				targetSign = key[0] == '+' ? SdlPolarity.Positive : SdlPolarity.Negative;
				key = key[1..];
			}

			if (Targets.TryGetValue(key, out var target) && SdlInputSource.TryParse(value, out var source))
			{
				bindings.Add(new SdlControllerBinding(target, targetSign, source));
			}
		}

		return new SdlControllerMapping(parts[0], parts[1], vendorId, productId, platform, bindings);
	}

	private static bool TryParseGuid(string guid, out int vendorId, out int productId)
	{
		vendorId = 0;
		productId = 0;
		if (guid.Length != 32)
		{
			return false;
		}

		Span<byte> bytes = stackalloc byte[16];
		for (var i = 0; i < bytes.Length; i++)
		{
			if (!byte.TryParse(guid.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
			{
				return false;
			}
		}

		vendorId = bytes[4] | (bytes[5] << 8);
		productId = bytes[8] | (bytes[9] << 8);
		return true;
	}

	private static void ApplyBinding(VirtualXboxStateBuilder builder, SdlControllerBinding binding, SdlInputSnapshot snapshot)
	{
		if (IsAxis(binding.Target))
		{
			SetAxis(builder, binding.Target, ReadAxis(binding, snapshot));
			return;
		}

		if (IsTrigger(binding.Target))
		{
			SetTrigger(builder, binding.Target, ReadTrigger(binding.Source, snapshot));
			return;
		}

		SetButton(builder, binding.Target, ReadButton(binding.Source, snapshot));
	}

	private static bool ReadButton(SdlInputSource source, SdlInputSnapshot snapshot)
		=> source.Kind switch
		{
			SdlInputSourceKind.Button => snapshot.GetButton(source.Index),
			SdlInputSourceKind.Hat => (snapshot.GetHat(source.Index) & source.HatMask) != 0,
			SdlInputSourceKind.Axis => Math.Abs(ReadAxisSource(source, snapshot)) >= 0.5,
			_ => false
		};

	private static double ReadAxis(SdlControllerBinding binding, SdlInputSnapshot snapshot)
	{
		var value = binding.Source.Kind == SdlInputSourceKind.Hat
			? ReadHatAxis(binding, snapshot)
			: ReadAxisSource(binding.Source, snapshot);
		if (binding.Target is VirtualXboxControl.LeftY or VirtualXboxControl.RightY)
		{
			value = -value;
		}

		return value;
	}

	private static double ReadHatAxis(SdlControllerBinding binding, SdlInputSnapshot snapshot)
	{
		var active = (snapshot.GetHat(binding.Source.Index) & binding.Source.HatMask) != 0;
		if (!active)
		{
			return 0;
		}

		return binding.TargetPolarity == SdlPolarity.Negative ? -1 : 1;
	}

	private static double ReadTrigger(SdlInputSource source, SdlInputSnapshot snapshot)
		=> source.Kind switch
		{
			SdlInputSourceKind.Button => snapshot.GetButton(source.Index) ? 1 : 0,
			SdlInputSourceKind.Axis => ReadTriggerAxis(source, snapshot),
			SdlInputSourceKind.Hat => (snapshot.GetHat(source.Index) & source.HatMask) != 0 ? 1 : 0,
			_ => 0
		};

	private static double ReadAxisSource(SdlInputSource source, SdlInputSnapshot snapshot)
	{
		var axis = snapshot.GetAxis(source.Index);
		if (axis == null)
		{
			return 0;
		}

		var value = InputNormalization.NormalizeAxis(axis.Value.Raw, axis.Value.Minimum, axis.Value.Maximum);
		if (source.Polarity == SdlPolarity.Positive)
		{
			value = Math.Max(0, value);
		}
		else if (source.Polarity == SdlPolarity.Negative)
		{
			value = Math.Max(0, -value);
		}

		return source.Invert ? -value : value;
	}

	private static double ReadTriggerAxis(SdlInputSource source, SdlInputSnapshot snapshot)
	{
		var axis = snapshot.GetAxis(source.Index);
		if (axis == null)
		{
			return 0;
		}

		if (source.Polarity == SdlPolarity.Positive)
		{
			var center = axis.Value.Center;
			return InputNormalization.NormalizeTrigger(axis.Value.Raw, center, axis.Value.Maximum);
		}

		if (source.Polarity == SdlPolarity.Negative)
		{
			var center = axis.Value.Center;
			return InputNormalization.NormalizeTrigger(center - axis.Value.Raw, 0, center - axis.Value.Minimum);
		}

		return InputNormalization.NormalizeTrigger(axis.Value.Raw, axis.Value.Minimum, axis.Value.Maximum);
	}

	private static bool IsAxis(VirtualXboxControl control)
		=> control is VirtualXboxControl.LeftX or VirtualXboxControl.LeftY or VirtualXboxControl.RightX or VirtualXboxControl.RightY;

	private static bool IsTrigger(VirtualXboxControl control)
		=> control is VirtualXboxControl.LeftTrigger or VirtualXboxControl.RightTrigger;

	private static void SetAxis(VirtualXboxStateBuilder builder, VirtualXboxControl control, double value)
	{
		switch (control)
		{
			case VirtualXboxControl.LeftX: builder.LeftX = value; break;
			case VirtualXboxControl.LeftY: builder.LeftY = value; break;
			case VirtualXboxControl.RightX: builder.RightX = value; break;
			case VirtualXboxControl.RightY: builder.RightY = value; break;
		}
	}

	private static void SetTrigger(VirtualXboxStateBuilder builder, VirtualXboxControl control, double value)
	{
		if (control == VirtualXboxControl.LeftTrigger)
		{
			builder.LeftTrigger = value;
		}
		else
		{
			builder.RightTrigger = value;
		}
	}

	private static void SetButton(VirtualXboxStateBuilder builder, VirtualXboxControl control, bool pressed)
	{
		switch (control)
		{
			case VirtualXboxControl.A: builder.A = pressed; break;
			case VirtualXboxControl.B: builder.B = pressed; break;
			case VirtualXboxControl.X: builder.X = pressed; break;
			case VirtualXboxControl.Y: builder.Y = pressed; break;
			case VirtualXboxControl.LeftShoulder: builder.LeftShoulder = pressed; break;
			case VirtualXboxControl.RightShoulder: builder.RightShoulder = pressed; break;
			case VirtualXboxControl.Back: builder.Back = pressed; break;
			case VirtualXboxControl.Start: builder.Start = pressed; break;
			case VirtualXboxControl.Guide: builder.Guide = pressed; break;
			case VirtualXboxControl.LeftStick: builder.LeftStick = pressed; break;
			case VirtualXboxControl.RightStick: builder.RightStick = pressed; break;
			case VirtualXboxControl.DPadUp: builder.DPadUp = pressed; break;
			case VirtualXboxControl.DPadDown: builder.DPadDown = pressed; break;
			case VirtualXboxControl.DPadLeft: builder.DPadLeft = pressed; break;
			case VirtualXboxControl.DPadRight: builder.DPadRight = pressed; break;
		}
	}
}

internal sealed record SdlControllerBinding(VirtualXboxControl Target, SdlPolarity TargetPolarity, SdlInputSource Source);

internal sealed record SdlInputSource(SdlInputSourceKind Kind, int Index, SdlPolarity Polarity, bool Invert, int HatMask)
{
	public static bool TryParse(string value, out SdlInputSource source)
	{
		source = null!;
		var polarity = SdlPolarity.None;
		var invert = false;
		while (value.Length > 0 && (value[0] == '+' || value[0] == '-' || value[0] == '~'))
		{
			if (value[0] == '+')
			{
				polarity = SdlPolarity.Positive;
			}
			else if (value[0] == '-')
			{
				polarity = SdlPolarity.Negative;
			}
			else
			{
				invert = !invert;
			}

			value = value[1..];
		}

		if (value.Length < 2)
		{
			return false;
		}

		if (value[0] == 'b' && int.TryParse(value.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var buttonIndex))
		{
			source = new SdlInputSource(SdlInputSourceKind.Button, buttonIndex, polarity, invert, 0);
			return true;
		}

		if (value[0] == 'a' && int.TryParse(value.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var axisIndex))
		{
			source = new SdlInputSource(SdlInputSourceKind.Axis, axisIndex, polarity, invert, 0);
			return true;
		}

		if (value[0] == 'h')
		{
			var dot = value.IndexOf('.', StringComparison.Ordinal);
			if (dot > 1 &&
				int.TryParse(value.AsSpan(1, dot - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var hatIndex) &&
				int.TryParse(value.AsSpan(dot + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var hatMask))
			{
				source = new SdlInputSource(SdlInputSourceKind.Hat, hatIndex, polarity, invert, hatMask);
				return true;
			}
		}

		return false;
	}
}

internal enum SdlInputSourceKind
{
	Button,
	Axis,
	Hat
}

internal enum SdlPolarity
{
	None,
	Positive,
	Negative
}

internal sealed class SdlHidInputDecoder
{
	private readonly ReportDescriptor? _descriptor;
	private readonly string? _diagnostic;

	public SdlHidInputDecoder(HidDeviceDescriptor device)
	{
		if (device.ReportDescriptor.Length == 0)
		{
			_diagnostic = "SDL mapping is using a raw fallback because the HID report descriptor is unavailable.";
			return;
		}

		try
		{
			_descriptor = new ReportDescriptor(device.ReportDescriptor);
		}
		catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
		{
			_diagnostic = "SDL mapping is using a raw fallback because the HID report descriptor could not be parsed: " + ex.Message;
		}
	}

	public SdlInputSnapshot Decode(RawControllerInput input)
	{
		if (_descriptor != null)
		{
			var parsed = TryDecodeParsed(input.Report);
			if (parsed != null)
			{
				return parsed;
			}
		}

		return DecodeRawFallback(input.Report, _diagnostic);
	}

	private SdlInputSnapshot? TryDecodeParsed(byte[] report)
	{
		foreach (var deviceItem in _descriptor!.DeviceItems)
		{
			var parser = deviceItem.CreateDeviceItemInputParser();
			if (!TryParseReport(parser, report))
			{
				continue;
			}

			var axes = new List<SdlAxisValue>();
			var buttons = new Dictionary<int, bool>();
			var hats = new List<int>();
			for (var index = 0; index < parser.ValueCount; index++)
			{
				var value = parser.GetValue(index);
				if (!value.IsValid || value.IsNull)
				{
					continue;
				}

				foreach (var usage in value.Usages)
				{
					if (IsButtonUsage(usage))
					{
						var buttonIndex = (int)(usage & 0xFFFF) - 1;
						if (buttonIndex >= 0)
						{
							buttons[buttonIndex] = value.GetLogicalValue() != 0;
						}
					}
					else if (usage == (uint)Usage.GenericDesktopHatSwitch)
					{
						hats.Add(HidHatToSdlMask(value.GetLogicalValue()));
					}
					else if (IsAxisUsage(usage))
					{
						axes.Add(new SdlAxisValue(
							value.GetLogicalValue(),
							value.DataItem.LogicalMinimum,
							value.DataItem.LogicalMaximum));
					}
				}
			}

			if (axes.Count > 0 || buttons.Count > 0 || hats.Count > 0)
			{
				return new SdlInputSnapshot(axes, buttons, hats, null);
			}
		}

		return null;
	}

	private static bool TryParseReport(DeviceItemInputParser parser, byte[] report)
	{
		try
		{
			return parser.TryParseReport(report, 0, null!);
		}
		catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IndexOutOfRangeException)
		{
			return false;
		}
	}

	private static SdlInputSnapshot DecodeRawFallback(byte[] report, string? diagnostic)
	{
		var axes = report.Take(Math.Min(6, report.Length))
			.Select(value => new SdlAxisValue(value, 0, 255))
			.ToArray();
		var buttons = new Dictionary<int, bool>();
		if (report.Length > 6)
		{
			var buttonByte = report[6];
			var startsAtHighNibble = (buttonByte & 0x0F) <= 8;
			for (var index = 0; index < 12; index++)
			{
				var bit = startsAtHighNibble ? index + 4 : index;
				var offset = 6 + (bit / 8);
				if (offset >= report.Length)
				{
					break;
				}

				buttons[index] = (report[offset] & (1 << (bit % 8))) != 0;
			}
		}

		var hats = report.Length > 6
			? new[] { HidHatToSdlMask(report[6] & 0x0F) }
			: Array.Empty<int>();
		return new SdlInputSnapshot(axes, buttons, hats, diagnostic);
	}

	private static bool IsButtonUsage(uint usage)
		=> (usage >> 16) == 0x0009;

	private static bool IsAxisUsage(uint usage)
		=> usage is
			(uint)Usage.GenericDesktopX or
			(uint)Usage.GenericDesktopY or
			(uint)Usage.GenericDesktopZ or
			(uint)Usage.GenericDesktopRx or
			(uint)Usage.GenericDesktopRy or
			(uint)Usage.GenericDesktopRz or
			(uint)Usage.GenericDesktopSlider or
			(uint)Usage.GenericDesktopDial or
			(uint)Usage.GenericDesktopWheel;

	private static int HidHatToSdlMask(int value)
	{
		var directions = InputNormalization.HatToDirections(value);
		var mask = 0;
		if (directions.Up)
		{
			mask |= 1;
		}

		if (directions.Right)
		{
			mask |= 2;
		}

		if (directions.Down)
		{
			mask |= 4;
		}

		if (directions.Left)
		{
			mask |= 8;
		}

		return mask;
	}
}

internal sealed record SdlInputSnapshot(
	IReadOnlyList<SdlAxisValue> Axes,
	IReadOnlyDictionary<int, bool> Buttons,
	IReadOnlyList<int> Hats,
	string? Diagnostic)
{
	public bool GetButton(int index)
		=> Buttons.TryGetValue(index, out var pressed) && pressed;

	public SdlAxisValue? GetAxis(int index)
		=> index >= 0 && index < Axes.Count ? Axes[index] : null;

	public int GetHat(int index)
		=> index >= 0 && index < Hats.Count ? Hats[index] : 0;
}

internal readonly record struct SdlAxisValue(int Raw, int Minimum, int Maximum)
{
	public int Center => Minimum + ((Maximum - Minimum) / 2);
}
