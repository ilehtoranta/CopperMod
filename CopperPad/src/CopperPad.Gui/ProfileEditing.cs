using CopperPad;

namespace CopperPad.Gui;

internal static class CopperPadProfilePaths
{
	public static string GetDefaultProfilePath()
		=> Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			"CopperMod",
			"CopperPad",
			"profiles.json");
}

internal sealed class FileControllerProfileStore(string path) : IControllerProfileStore
{
	public string Path { get; } = path;

	public async ValueTask<ControllerProfileSet> LoadAsync(CancellationToken cancellationToken = default)
	{
		if (!File.Exists(Path))
		{
			return ControllerProfileSet.Empty;
		}

		await using var stream = File.OpenRead(Path);
		return await JsonControllerProfileSerializer.LoadAsync(stream, cancellationToken).ConfigureAwait(false);
	}

	public async ValueTask SaveAsync(ControllerProfileSet profiles, CancellationToken cancellationToken = default)
	{
		var directory = System.IO.Path.GetDirectoryName(Path);
		if (!string.IsNullOrWhiteSpace(directory))
		{
			Directory.CreateDirectory(directory);
		}

		await using var stream = File.Create(Path);
		await JsonControllerProfileSerializer.SaveAsync(stream, profiles, cancellationToken).ConfigureAwait(false);
	}
}

internal static class MappingDisplay
{
	public static string Format(ControllerMappingInfo? mapping)
		=> mapping == null ? "Mapping: unknown" : "Mapping: " + mapping;
}

internal static class ProfileDocumentDisplay
{
	public static string Format(string path, bool hasSavedProfile, ControllerProfile? draftProfile)
	{
		var profileState = hasSavedProfile ? "saved override" : "not found; new override draft";
		var profileName = string.IsNullOrWhiteSpace(draftProfile?.Name) ? "unsaved profile" : draftProfile.Name;
		return $"Profile: {profileState} ({profileName})\nDocument: {path}";
	}
}

internal static class DeviceDisplay
{
	public static bool IsLikelyGameController(HidDeviceInfo device)
		=> device.IsGameControllerUsage ||
			ContainsAny(device.ProductName, "gamepad", "controller", "joystick", "arcade", "fightstick", "pad") ||
			(device.VendorId == 0x0079 && device.ProductId == 0x0006);

	private static bool ContainsAny(string text, params string[] values)
		=> values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
}

internal sealed record ReportChange(
	int Offset,
	byte Baseline,
	byte Current,
	byte Delta,
	int ChangedBitCount,
	ControllerBindingSource SuggestedSource);

internal static class ReportAnalyzer
{
	public static IReadOnlyList<ReportChange> DetectChanges(byte[]? baseline, byte[] current)
	{
		if (current.Length == 0)
		{
			return Array.Empty<ReportChange>();
		}

		baseline ??= Array.Empty<byte>();
		var length = Math.Max(baseline.Length, current.Length);
		var changes = new List<ReportChange>();
		for (var offset = 0; offset < length; offset++)
		{
			var before = offset < baseline.Length ? baseline[offset] : (byte)0;
			var after = offset < current.Length ? current[offset] : (byte)0;
			if (before == after)
			{
				continue;
			}

			var delta = (byte)(before ^ after);
			changes.Add(new ReportChange(offset, before, after, delta, CountBits(delta), SuggestSource(offset, before, after, delta)));
		}

		return changes
			.OrderByDescending(change => change.ChangedBitCount)
			.ThenBy(change => change.Offset)
			.ToArray();
	}

	public static ReportChange? GetBestChange(byte[]? baseline, byte[] current)
		=> DetectChanges(baseline, current).FirstOrDefault();

	public static int ReadSourceValue(ControllerBindingSource source, byte[] report)
	{
		if (source.Offset < 0 || source.Offset >= report.Length)
		{
			return 0;
		}

		if (source.Kind == ControllerBindingSourceKind.ReportInt16LittleEndian && source.Offset + 1 < report.Length)
		{
			return BitConverter.ToInt16(report, source.Offset);
		}

		if (source.Kind == ControllerBindingSourceKind.ReportBit && source.Bit is >= 0 and < 8)
		{
			return (report[source.Offset] & (1 << source.Bit)) != 0 ? 1 : 0;
		}

		if (source.Kind == ControllerBindingSourceKind.Hat)
		{
			return report[source.Offset] & 0x0F;
		}

		return report[source.Offset];
	}

	public static string FormatSource(ControllerBindingSource source)
		=> source.Kind switch
		{
			ControllerBindingSourceKind.ReportBit => $"bit {source.Offset}.{source.Bit}",
			ControllerBindingSourceKind.ReportByte => $"byte {source.Offset}",
			ControllerBindingSourceKind.ReportInt16LittleEndian => $"i16le {source.Offset}",
			ControllerBindingSourceKind.Hat => $"hat {source.Offset}={source.HatValue}",
			_ => $"offset {source.Offset}"
		};

	private static ControllerBindingSource SuggestSource(int offset, byte before, byte after, byte delta)
	{
		var beforeHat = before & 0x0F;
		var afterHat = after & 0x0F;
		if ((before & 0xF0) == (after & 0xF0) && beforeHat == 8 && afterHat is >= 0 and <= 7)
		{
			return new ControllerBindingSource
			{
				Kind = ControllerBindingSourceKind.Hat,
				Offset = offset,
				HatValue = afterHat
			};
		}

		if (CountBits(delta) == 1)
		{
			return new ControllerBindingSource
			{
				Kind = ControllerBindingSourceKind.ReportBit,
				Offset = offset,
				Bit = LowestSetBit(delta)
			};
		}

		return new ControllerBindingSource
		{
			Kind = ControllerBindingSourceKind.ReportByte,
			Offset = offset
		};
	}

	private static int CountBits(byte value)
	{
		var count = 0;
		while (value != 0)
		{
			count += value & 1;
			value >>= 1;
		}

		return count;
	}

	private static int LowestSetBit(byte value)
	{
		for (var bit = 0; bit < 8; bit++)
		{
			if ((value & (1 << bit)) != 0)
			{
				return bit;
			}
		}

		return 0;
	}
}

internal sealed class AxisCalibrationCapture
{
	public int? Minimum { get; private set; }
	public int? Maximum { get; private set; }
	public int? Center { get; private set; }
	public int? LastRaw { get; private set; }

	public void Observe(int raw)
	{
		LastRaw = raw;
		Minimum = Minimum.HasValue ? Math.Min(Minimum.Value, raw) : raw;
		Maximum = Maximum.HasValue ? Math.Max(Maximum.Value, raw) : raw;
	}

	public void CaptureCenter(int raw)
	{
		LastRaw = raw;
		Center = raw;
		Observe(raw);
	}

	public AxisCalibration ToCalibration(bool invert, double deadzone, double saturation)
	{
		var minimum = Minimum ?? 0;
		var maximum = Maximum ?? 255;
		if (maximum <= minimum)
		{
			maximum = minimum + 1;
		}

		return new AxisCalibration
		{
			Minimum = minimum,
			Maximum = maximum,
			Center = Center,
			Invert = invert,
			Deadzone = Math.Clamp(deadzone, 0, 0.95),
			Saturation = Math.Clamp(saturation, 0.01, 1.0)
		};
	}

	public static AxisCalibrationCapture From(AxisCalibration? calibration)
	{
		var capture = new AxisCalibrationCapture();
		if (calibration == null)
		{
			return capture;
		}

		capture.Minimum = calibration.Minimum;
		capture.Maximum = calibration.Maximum;
		capture.Center = calibration.Center;
		return capture;
	}
}

internal sealed record ProfileValidationIssue(string Message);

internal static class ProfileEditor
{
	private static readonly HashSet<ControllerElement> AxisTargets =
	[
		ControllerElement.LeftStickX,
		ControllerElement.LeftStickY,
		ControllerElement.RightStickX,
		ControllerElement.RightStickY,
		ControllerElement.LeftTrigger,
		ControllerElement.RightTrigger
	];

	public static bool IsAxisTarget(ControllerElement control)
		=> AxisTargets.Contains(control);

	public static ControllerProfile CreateDefaultProfile(HidDeviceInfo device, DateTimeOffset timestamp)
		=> new()
		{
			Name = string.IsNullOrWhiteSpace(device.ProductName) ? "Controller profile" : device.ProductName + " profile",
			VendorId = device.VendorId,
			ProductId = device.ProductId,
			ProductNameContains = device.ProductName,
			CreatedAt = timestamp,
			UpdatedAt = timestamp
		};

	public static ControllerProfile UpsertBinding(ControllerProfile profile, ControllerBinding binding)
		=> profile with
		{
			Bindings = profile.Bindings
				.Where(existing => existing.Target != binding.Target)
				.Append(binding)
				.OrderBy(binding => binding.Target)
				.ToArray()
		};

	public static ControllerProfile RemoveBinding(ControllerProfile profile, ControllerElement target)
		=> profile with
		{
			Bindings = profile.Bindings
				.Where(binding => binding.Target != target)
				.ToArray()
		};

	public static ControllerProfileSet MergeProfile(ControllerProfileSet profiles, ControllerProfile profile)
	{
		var merged = false;
		var list = new List<ControllerProfile>();
		foreach (var existing in profiles.Profiles)
		{
			if (!merged && IsSameProfile(existing, profile))
			{
				list.Add(profile);
				merged = true;
			}
			else
			{
				list.Add(existing);
			}
		}

		if (!merged)
		{
			list.Add(profile);
		}

		return profiles with { Profiles = list.ToArray() };
	}

	public static IReadOnlyList<ProfileValidationIssue> ValidateProfile(ControllerProfile profile, int maxInputReportLength)
	{
		var issues = new List<ProfileValidationIssue>();
		if (string.IsNullOrWhiteSpace(profile.Name))
		{
			issues.Add(new ProfileValidationIssue("Profile name is required."));
		}

		foreach (var duplicate in profile.Bindings.GroupBy(binding => binding.Target).Where(group => group.Count() > 1))
		{
			issues.Add(new ProfileValidationIssue($"Duplicate binding for {duplicate.Key}."));
		}

		foreach (var binding in profile.Bindings)
		{
			ValidateBinding(binding, maxInputReportLength, issues);
		}

		return issues;
	}

	public static ControllerBinding CreateBinding(
		ControllerElement target,
		ControllerBindingSource source,
		AxisCalibration? axisCalibration)
		=> new()
		{
			Target = target,
			Source = source,
			Axis = IsAxisTarget(target) ? axisCalibration ?? new AxisCalibration() : null
		};

	private static void ValidateBinding(ControllerBinding binding, int maxInputReportLength, List<ProfileValidationIssue> issues)
	{
		var source = binding.Source;
		if (source.Offset < 0 || source.Offset >= maxInputReportLength)
		{
			issues.Add(new ProfileValidationIssue($"{binding.Target} source offset is outside the input report."));
			return;
		}

		if (source.Kind == ControllerBindingSourceKind.ReportInt16LittleEndian && source.Offset + 1 >= maxInputReportLength)
		{
			issues.Add(new ProfileValidationIssue($"{binding.Target} 16-bit source needs two report bytes."));
		}

		if (source.Kind == ControllerBindingSourceKind.ReportBit && source.Bit is < 0 or > 7)
		{
			issues.Add(new ProfileValidationIssue($"{binding.Target} bit source must use bit 0-7."));
		}

		if (source.Kind == ControllerBindingSourceKind.Hat && !source.HatValue.HasValue)
		{
			issues.Add(new ProfileValidationIssue($"{binding.Target} hat source needs a hat value."));
		}
	}

	private static bool IsSameProfile(ControllerProfile left, ControllerProfile right)
	{
		if (!string.IsNullOrWhiteSpace(left.DeviceId) || !string.IsNullOrWhiteSpace(right.DeviceId))
		{
			return string.Equals(left.DeviceId, right.DeviceId, StringComparison.Ordinal) &&
				string.Equals(left.Name, right.Name, StringComparison.Ordinal);
		}

		return left.VendorId == right.VendorId &&
			left.ProductId == right.ProductId &&
			string.Equals(left.ProductNameContains, right.ProductNameContains, StringComparison.OrdinalIgnoreCase);
	}
}
