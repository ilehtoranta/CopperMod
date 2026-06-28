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
	{
		if (device.IsGameControllerUsage ||
			(device.VendorId == 0x0079 && device.ProductId == 0x0006))
		{
			return true;
		}

		if (IsUnknownHidPlaceholder(device.ProductName))
		{
			return false;
		}

		return ContainsAny(device.ProductName, "gamepad", "controller", "joystick", "arcade", "fightstick", "pad");
	}

	private static bool ContainsAny(string text, params string[] values)
		=> values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));

	private static bool IsUnknownHidPlaceholder(string productName)
		=> productName.StartsWith("Unknown HID", StringComparison.OrdinalIgnoreCase);
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
			var isSet = (report[source.Offset] & (1 << source.Bit)) != 0;
			return source.Invert ? isSet ? 0 : 1 : isSet ? 1 : 0;
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
			ControllerBindingSourceKind.ReportBit => source.Invert ? $"bit {source.Offset}.{source.Bit} active-low" : $"bit {source.Offset}.{source.Bit}",
			ControllerBindingSourceKind.ReportByte => $"byte {source.Offset}",
			ControllerBindingSourceKind.ReportInt16LittleEndian => $"i16le {source.Offset}",
			ControllerBindingSourceKind.Hat => $"hat {source.Offset}={source.HatValue}",
			_ => $"offset {source.Offset}"
		};

	private static ControllerBindingSource SuggestSource(int offset, byte before, byte after, byte delta)
	{
		var beforeHat = before & 0x0F;
		var afterHat = after & 0x0F;
		if (beforeHat == 0x0F &&
			(before & 0xF0) == (after & 0xF0) &&
			CountBits(delta) == 1 &&
			(before & delta) != 0 &&
			(after & delta) == 0)
		{
			return new ControllerBindingSource
			{
				Kind = ControllerBindingSourceKind.ReportBit,
				Offset = offset,
				Bit = LowestSetBit(delta),
				Invert = (before & delta) != 0 && (after & delta) == 0
			};
		}

		if ((before & 0xF0) == (after & 0xF0) && beforeHat is 8 or 0x0F && afterHat is >= 0 and <= 7)
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
				Bit = LowestSetBit(delta),
				Invert = (before & delta) != 0 && (after & delta) == 0
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
	public static readonly IReadOnlyList<ControllerElement> MappableTargets =
	[
		ControllerElement.South,
		ControllerElement.East,
		ControllerElement.West,
		ControllerElement.North,
		ControllerElement.DPadUp,
		ControllerElement.DPadDown,
		ControllerElement.DPadLeft,
		ControllerElement.DPadRight,
		ControllerElement.LeftShoulder,
		ControllerElement.RightShoulder,
		ControllerElement.Select,
		ControllerElement.Start,
		ControllerElement.Menu,
		ControllerElement.LeftStickButton,
		ControllerElement.RightStickButton,
		ControllerElement.LeftStickX,
		ControllerElement.LeftStickY,
		ControllerElement.RightStickX,
		ControllerElement.RightStickY,
		ControllerElement.LeftTrigger,
		ControllerElement.RightTrigger
	];

	private static readonly HashSet<ControllerElement> AxisTargets =
	[
		ControllerElement.LeftStickX,
		ControllerElement.LeftStickY,
		ControllerElement.RightStickX,
		ControllerElement.RightStickY,
		ControllerElement.LeftTrigger,
		ControllerElement.RightTrigger
	];

	private static readonly HashSet<ControllerElement> FaceButtonTargets =
	[
		ControllerElement.South,
		ControllerElement.East,
		ControllerElement.West,
		ControllerElement.North
	];

	public static bool IsAxisTarget(ControllerElement control)
		=> AxisTargets.Contains(control);

	public static bool IsDPadTarget(ControllerElement control)
		=> control is
			ControllerElement.DPadUp or
			ControllerElement.DPadDown or
			ControllerElement.DPadLeft or
			ControllerElement.DPadRight;

	public static bool IsTriggerTarget(ControllerElement control)
		=> control is ControllerElement.LeftTrigger or ControllerElement.RightTrigger;

	public static bool IsFaceButtonTarget(ControllerElement control)
		=> FaceButtonTargets.Contains(control);

	public static int GetSourcePreferenceScore(ControllerProfile? profile, ControllerElement target, ControllerBindingSource source)
	{
		if (ProfileEditor.IsAxisTarget(target) || profile == null)
		{
			return 0;
		}

		var score = 0;
		if (source.Kind == ControllerBindingSourceKind.ReportBit &&
			source.Invert &&
			!ProfileEditor.IsDPadTarget(target))
		{
			score -= 40;
		}

		if (ProfileEditor.IsFaceButtonTarget(target) && source.Kind == ControllerBindingSourceKind.ReportBit)
		{
			var matchingFaceOffsetCount = profile.Bindings.Count(binding =>
				ProfileEditor.IsFaceButtonTarget(binding.Target) &&
				binding.Source.Kind == ControllerBindingSourceKind.ReportBit &&
				binding.Source.Offset == source.Offset &&
				binding.Target != target);
			score += matchingFaceOffsetCount * 90;
		}

		return score;
	}

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

		foreach (var duplicate in profile.Bindings.GroupBy(binding => GetSourceKey(binding.Source)).Where(group => group.Count() > 1))
		{
			issues.Add(new ProfileValidationIssue($"Duplicate physical source {duplicate.Key} is mapped to {string.Join(", ", duplicate.Select(binding => binding.Target))}."));
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

	public static string GetSourceKey(ControllerBindingSource source)
		=> $"{source.Kind}:{source.Offset}:{source.Bit}:{source.HatValue?.ToString() ?? ""}:{source.Invert}";

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

internal sealed record MappingTargetItem(ControllerElement Element)
{
	public static IReadOnlyList<MappingTargetItem> All { get; } = ProfileEditor.MappableTargets.Select(target => new MappingTargetItem(target)).ToArray();

	public override string ToString()
		=> Element switch
		{
			ControllerElement.South => "South (A / bottom face)",
			ControllerElement.East => "East (B / right face)",
			ControllerElement.West => "West (X / left face)",
			ControllerElement.North => "North (Y / top face)",
			ControllerElement.LeftStickX => "Left stick X",
			ControllerElement.LeftStickY => "Left stick Y",
			ControllerElement.RightStickX => "Right stick X",
			ControllerElement.RightStickY => "Right stick Y",
			ControllerElement.LeftTrigger => "Left trigger",
			ControllerElement.RightTrigger => "Right trigger",
			_ => Element.ToString()
		};
}

internal sealed class GuidedMappingCapture(TimeSpan lockDelay)
{
	private const int ByteAxisThreshold = 16;
	private const int Int16AxisThreshold = 1024;
	private string? _candidateKey;
	private DateTimeOffset _candidateSince;

	public ControllerBindingSource? CandidateSource { get; private set; }
	public ControllerBindingSource? LockedSource { get; private set; }

	public bool IsLocked => LockedSource != null;

	public void Reset()
	{
		_candidateKey = null;
		_candidateSince = default;
		CandidateSource = null;
		LockedSource = null;
	}

	public bool Observe(ControllerElement target, ReportChange? change, byte[] baseline, byte[] current, DateTimeOffset timestamp)
	{
		if (LockedSource != null || change == null)
		{
			return false;
		}

		var normalized = NormalizeForTarget(target, change, baseline, current);
		if (!IsPlausibleForTarget(target, normalized, baseline, current))
		{
			return false;
		}

		var key = ProfileEditor.GetSourceKey(normalized.SuggestedSource);
		if (!string.Equals(key, _candidateKey, StringComparison.Ordinal))
		{
			_candidateKey = key;
			_candidateSince = timestamp;
			CandidateSource = normalized.SuggestedSource;
			return false;
		}

		return TryLockCandidate(baseline, current, timestamp);
	}

	public bool TryLockCandidate(byte[] baseline, byte[] current, DateTimeOffset timestamp)
	{
		if (LockedSource != null)
		{
			return true;
		}

		if (CandidateSource == null ||
			timestamp - _candidateSince < lockDelay ||
			IsSourceReleased(CandidateSource, baseline, current))
		{
			return false;
		}

		LockedSource = CandidateSource;
		return true;
	}

	public static bool IsPlausibleForTarget(ControllerElement target, ReportChange change, byte[] baseline, byte[] current)
		=> GetCandidateScore(target, change, baseline, current) >= 0;

	public static int GetCandidateScore(ControllerElement target, ReportChange change, byte[] baseline, byte[] current)
	{
		change = NormalizeForTarget(target, change, baseline, current);
		var source = change.SuggestedSource;
		if (ProfileEditor.IsDPadTarget(target))
		{
			return source.Kind switch
			{
				ControllerBindingSourceKind.Hat when HatMatchesTarget(target, source.HatValue) => 300,
				ControllerBindingSourceKind.ReportBit when IsActiveLowNibbleMask(change, baseline, current) => 250,
				ControllerBindingSourceKind.ReportBit => 50,
				_ => -1
			};
		}

		if (!ProfileEditor.IsAxisTarget(target))
		{
			if (source.Kind == ControllerBindingSourceKind.ReportBit && source.Invert)
			{
				return -1;
			}

			return source.Kind == ControllerBindingSourceKind.ReportBit ? 100 : -1;
		}

		if (ProfileEditor.IsTriggerTarget(target) &&
			source.Kind is ControllerBindingSourceKind.ReportBit or ControllerBindingSourceKind.Hat)
		{
			return source.Kind == ControllerBindingSourceKind.ReportBit && source.Invert ? -1 : 100;
		}

		return source.Kind switch
		{
			ControllerBindingSourceKind.ReportByte => Math.Abs(GetByte(baseline, source.Offset) - GetByte(current, source.Offset)) >= ByteAxisThreshold ? 100 : -1,
			ControllerBindingSourceKind.ReportInt16LittleEndian => Math.Abs(GetInt16(baseline, source.Offset) - GetInt16(current, source.Offset)) >= Int16AxisThreshold ? 100 : -1,
			_ => -1
		};
	}

	public static ReportChange NormalizeForTarget(ControllerElement target, ReportChange change, byte[] baseline, byte[] current)
	{
		var source = change.SuggestedSource;
		if (ProfileEditor.IsAxisTarget(target) &&
			source.Kind == ControllerBindingSourceKind.ReportBit &&
			Math.Abs(GetByte(baseline, source.Offset) - GetByte(current, source.Offset)) >= ByteAxisThreshold)
		{
			return change with
			{
				SuggestedSource = new ControllerBindingSource
				{
					Kind = ControllerBindingSourceKind.ReportByte,
					Offset = source.Offset
				}
			};
		}

		return change;
	}

	private static bool HatMatchesTarget(ControllerElement target, int? hatValue)
	{
		if (!hatValue.HasValue)
		{
			return false;
		}

		return target switch
		{
			ControllerElement.DPadUp => hatValue.Value is 0 or 1 or 7,
			ControllerElement.DPadDown => hatValue.Value is 3 or 4 or 5,
			ControllerElement.DPadLeft => hatValue.Value is 5 or 6 or 7,
			ControllerElement.DPadRight => hatValue.Value is 1 or 2 or 3,
			_ => false
		};
	}

	private static bool IsActiveLowNibbleMask(ReportChange change, byte[] baseline, byte[] current)
	{
		var source = change.SuggestedSource;
		if (source.Kind != ControllerBindingSourceKind.ReportBit ||
			!source.Invert ||
			source.Bit is < 0 or > 3 ||
			source.Offset < 0 ||
			source.Offset >= baseline.Length ||
			source.Offset >= current.Length)
		{
			return false;
		}

		return (baseline[source.Offset] & 0x0F) == 0x0F &&
			(current[source.Offset] & 0x0F) == (0x0F & ~(1 << source.Bit));
	}

	public static bool IsSourceReleased(ControllerBindingSource source, byte[] baseline, byte[] current)
		=> source.Kind switch
		{
			ControllerBindingSourceKind.ReportBit => ReportAnalyzer.ReadSourceValue(source, current) == ReportAnalyzer.ReadSourceValue(source, baseline),
			ControllerBindingSourceKind.Hat => ReportAnalyzer.ReadSourceValue(source, current) != source.HatValue,
			ControllerBindingSourceKind.ReportByte => Math.Abs(ReportAnalyzer.ReadSourceValue(source, current) - ReportAnalyzer.ReadSourceValue(source, baseline)) < ByteAxisThreshold,
			ControllerBindingSourceKind.ReportInt16LittleEndian => Math.Abs(ReportAnalyzer.ReadSourceValue(source, current) - ReportAnalyzer.ReadSourceValue(source, baseline)) < Int16AxisThreshold,
			_ => true
		};

	private static int GetByte(byte[] report, int offset)
		=> offset >= 0 && offset < report.Length ? report[offset] : 0;

	private static int GetInt16(byte[] report, int offset)
		=> offset >= 0 && offset + 1 < report.Length ? BitConverter.ToInt16(report, offset) : 0;
}
