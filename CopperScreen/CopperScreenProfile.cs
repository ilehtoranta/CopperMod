using System.Globalization;
using System.Text.Json;
using CopperMod.Amiga;

namespace CopperScreen;

internal enum CopperScreenKickstartSource
{
	CopperStart,
	Kickstart13Rom
}

internal sealed class CopperScreenProfile
{
	public const string DefaultProfileId = "expanded-copperstart";
	private const int Kilobyte = 1024;
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		AllowTrailingCommas = true,
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip
	};

	private CopperScreenProfile(
		string id,
		string displayName,
		string description,
		int chipRamSize,
		int expansionRamSize,
		uint expansionRamBase,
		int realFastRamSize,
		uint realFastRamBase,
		bool rtcEnabled,
		int floppyDriveCount,
		M68kBackendKind cpuBackend,
		FloppyDriveAudioOptions floppyDriveAudio,
		CopperScreenKickstartSource kickstartSource,
		IReadOnlyList<CopperScreenMediaDriveSettings> mediaDrives,
		CopperScreenInputOptions input,
		CopperScreenPresentationOptions presentationOptions,
		string? configPath)
	{
		Id = id;
		DisplayName = displayName;
		Description = description;
		ChipRamSize = chipRamSize;
		ExpansionRamSize = expansionRamSize;
		ExpansionRamBase = expansionRamBase;
		RealFastRamSize = realFastRamSize;
		RealFastRamBase = realFastRamBase;
		RtcEnabled = rtcEnabled;
		FloppyDriveCount = floppyDriveCount;
		CpuBackend = cpuBackend;
		FloppyDriveAudio = floppyDriveAudio;
		KickstartSource = kickstartSource;
		MediaDrives = mediaDrives;
		Input = input;
		PresentationOptions = presentationOptions;
		ConfigPath = configPath;
	}

	public string Id { get; }

	public string DisplayName { get; }

	public string Description { get; }

	public int ChipRamSize { get; }

	public int ExpansionRamSize { get; }

	public uint ExpansionRamBase { get; }

	public int RealFastRamSize { get; }

	public uint RealFastRamBase { get; }

	public bool RtcEnabled { get; }

	public int FloppyDriveCount { get; }

	public M68kBackendKind CpuBackend { get; }

	public FloppyDriveAudioOptions FloppyDriveAudio { get; }

	public CopperScreenKickstartSource KickstartSource { get; }

	public IReadOnlyList<CopperScreenMediaDriveSettings> MediaDrives { get; }

	public CopperScreenInputOptions Input { get; }

	public CopperScreenPresentationOptions PresentationOptions { get; }

	public string? ConfigPath { get; }

	public bool UsesKickstartRom => KickstartSource == CopperScreenKickstartSource.Kickstart13Rom;

	public AmigaMachineProfile MachineProfile => ExpansionRamSize == 0
		? AmigaMachineProfile.A500Pal512KChipOnlyBoot
		: AmigaMachineProfile.A500Pal512KBoot;

	public AmigaMachineOptions CreateMachineOptions()
	{
		return AmigaMachineOptions
			.ForProfile(MachineProfile)
			.WithChipRam(ChipRamSize)
			.WithExpansionRam(ExpansionRamSize, ExpansionRamBase)
			.WithRealFastRam(RealFastRamSize, RealFastRamBase)
			.WithRealTimeClock(RtcEnabled)
			.WithFloppyDriveCount(FloppyDriveCount)
			.WithCpu(M68kCoreFactory.Default, CpuBackend)
			.WithLiveAgnusDma(true)
			.WithBusAccessLogging(false);
	}

	public static CopperScreenProfile LoadDefault(string baseDirectory, out string? error)
	{
		if (TryLoad(DefaultProfileId, baseDirectory, out var profile, out error))
		{
			return profile;
		}

		return CreateFallbackDefault();
	}

	public static bool TryLoad(string? specifier, string baseDirectory, out CopperScreenProfile profile, out string? error)
	{
		profile = CreateFallbackDefault();
		error = null;
		var resolvedSpecifier = string.IsNullOrWhiteSpace(specifier)
			? DefaultProfileId
			: NormalizeProfileId(specifier);
		var profilePath = ResolveProfilePath(resolvedSpecifier, baseDirectory);
		if (profilePath == null)
		{
			error = $"CopperScreen profile '{specifier}' was not found.";
			return false;
		}

		try
		{
			profile = FromJsonFile(profilePath);
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
		{
			error = $"Could not load CopperScreen profile '{specifier}': {ex.Message}";
			return false;
		}
	}

	public static bool TryLoadWithKickstartSource(
		CopperScreenProfile current,
		CopperScreenKickstartSource source,
		string baseDirectory,
		out CopperScreenProfile profile,
		out string? error)
	{
		var id = current.ExpansionRamSize == 0
			? source == CopperScreenKickstartSource.CopperStart ? "vanilla-copperstart" : "vanilla-kickstart13"
			: source == CopperScreenKickstartSource.CopperStart ? "expanded-copperstart" : "expanded-kickstart13";
		return TryLoad(id, baseDirectory, out profile, out error);
	}

	public static string NormalizeProfileId(string value)
	{
		var trimmed = value.Trim();
		if (trimmed.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
			trimmed.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
			trimmed.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
		{
			return trimmed;
		}

		var normalized = trimmed
			.ToLowerInvariant()
			.Replace('_', '-')
			.Replace(' ', '-');
		return normalized switch
		{
			"default" or "expanded" => "expanded-copperstart",
			"vanilla" => "vanilla-copperstart",
			"expanded-rom" or "expanded-kickstart" or "expanded-kickstart-13" => "expanded-kickstart13",
			"vanilla-rom" or "vanilla-kickstart" or "vanilla-kickstart-13" => "vanilla-kickstart13",
			_ => normalized
		};
	}

	private static CopperScreenProfile FromJsonFile(string path)
	{
		var json = File.ReadAllText(path);
		var config = JsonSerializer.Deserialize<ProfileFile>(json, JsonOptions) ??
			throw new InvalidOperationException("The profile file was empty.");
		return FromConfig(config, path);
	}

	private static CopperScreenProfile FromConfig(ProfileFile config, string path)
	{
		var id = Required(config.Id, "id");
		var displayName = Required(config.DisplayName, "displayName");
		var machine = config.Machine ?? throw new InvalidOperationException("The profile is missing machine settings.");
		var kickstart = config.Kickstart ?? throw new InvalidOperationException("The profile is missing kickstart settings.");
		if (!string.Equals(machine.Model, "A500PAL", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException($"Unsupported machine model '{machine.Model}'.");
		}

		var chipRamSize = CheckedKilobytes(machine.ChipRamKb, "machine.chipRamKb");
		var expansionRamSize = CheckedKilobytes(machine.PseudoFastRamKb, "machine.pseudoFastRamKb");
		var expansionRamBase = ParseAddress(machine.PseudoFastBase, AmigaConstants.A500BootPseudoFastRamBase);
		var realFastRamSize = CheckedKilobytes(machine.RealFastRamKb, "machine.realFastRamKb");
		var realFastRamBase = ParseAddress(machine.RealFastBase, AmigaConstants.A500RealFastRamBase);
		var rtcEnabled = machine.RtcEnabled ?? expansionRamSize > 0;
		var floppyDriveCount = machine.FloppyDriveCount ?? (expansionRamSize > 0 ? 2 : 1);
		var cpuBackend = ParseCpuBackend(config.Cpu?.Backend);
		var floppyDriveAudio = ParseFloppyDriveAudio(config.Audio?.FloppyDriveSounds);
		var mediaDrives = ParseMediaDrives(config.Media);
		var input = ParseInputOptions(config.Input);
		var presentationOptions = ParsePresentationOptions(config.Presentation);
		if (floppyDriveCount is < 1 or > 4)
		{
			throw new InvalidOperationException("machine.floppyDriveCount must be between 1 and 4.");
		}

		var kickstartSource = ParseKickstartSource(kickstart.Source);
		var description = string.IsNullOrWhiteSpace(config.Description)
			? displayName
			: config.Description.Trim();

		return new CopperScreenProfile(
			id,
			displayName,
			description,
			chipRamSize,
			expansionRamSize,
			expansionRamBase,
			realFastRamSize,
			realFastRamBase,
			rtcEnabled,
			floppyDriveCount,
			cpuBackend,
			floppyDriveAudio,
			kickstartSource,
			mediaDrives,
			input,
			presentationOptions,
			Path.GetFullPath(path));
	}

	internal static CopperScreenProfile Create(
		string id,
		string displayName,
		string description,
		int chipRamSize,
		int expansionRamSize,
		uint expansionRamBase,
		int realFastRamSize,
		uint realFastRamBase,
		bool rtcEnabled,
		int floppyDriveCount,
		M68kBackendKind cpuBackend,
		FloppyDriveAudioOptions floppyDriveAudio,
		CopperScreenKickstartSource kickstartSource,
		IReadOnlyList<CopperScreenMediaDriveSettings> mediaDrives,
		CopperScreenInputOptions input,
		string? configPath = null,
		CopperScreenPresentationOptions? presentationOptions = null)
	{
		if (floppyDriveCount is < 1 or > 4)
		{
			throw new InvalidOperationException("machine.floppyDriveCount must be between 1 and 4.");
		}

		return new CopperScreenProfile(
			Required(id, "id"),
			Required(displayName, "displayName"),
			string.IsNullOrWhiteSpace(description) ? Required(displayName, "displayName") : description.Trim(),
			chipRamSize,
			expansionRamSize,
			expansionRamBase,
			realFastRamSize,
			realFastRamBase,
			rtcEnabled,
			floppyDriveCount,
			cpuBackend,
			floppyDriveAudio,
			kickstartSource,
			mediaDrives,
			input,
			presentationOptions ?? CopperScreenPresentationOptions.Default,
			configPath);
	}

	private static string? ResolveProfilePath(string specifier, string baseDirectory)
	{
		foreach (var candidate in EnumerateProfilePathCandidates(specifier, baseDirectory))
		{
			if (File.Exists(candidate))
			{
				return Path.GetFullPath(candidate);
			}
		}

		return null;
	}

	private static IEnumerable<string> EnumerateProfilePathCandidates(string specifier, string baseDirectory)
	{
		var hasExtension = Path.HasExtension(specifier);
		var fileName = hasExtension ? specifier : specifier + ".json";
		if (Path.IsPathFullyQualified(specifier))
		{
			yield return specifier;
			if (!hasExtension)
			{
				yield return specifier + ".json";
			}

			yield break;
		}

		yield return Path.GetFullPath(specifier);
		if (!hasExtension)
		{
			yield return Path.GetFullPath(specifier + ".json");
		}

		var directory = new DirectoryInfo(baseDirectory);
		while (directory != null)
		{
			yield return Path.Combine(directory.FullName, "Profiles", fileName);
			yield return Path.Combine(directory.FullName, "CopperScreen", "Profiles", fileName);
			directory = directory.Parent;
		}
	}

	private static string Required(string? value, string name)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new InvalidOperationException($"The profile is missing {name}.");
		}

		return value.Trim();
	}

	private static int CheckedKilobytes(int value, string name)
	{
		if (value < 0)
		{
			throw new InvalidOperationException($"{name} cannot be negative.");
		}

		return checked(value * Kilobyte);
	}

	private static uint ParseAddress(string? value, uint defaultValue)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return defaultValue;
		}

		var trimmed = value.Trim();
		if (trimmed.StartsWith("$", StringComparison.Ordinal))
		{
			return uint.Parse(trimmed[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
		}

		if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			return uint.Parse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
		}

		return uint.Parse(trimmed, CultureInfo.InvariantCulture);
	}

	private static CopperScreenKickstartSource ParseKickstartSource(string? value)
	{
		var source = Required(value, "kickstart.source");
		return source.Trim().ToLowerInvariant().Replace("-", string.Empty) switch
		{
			"copperstart" => CopperScreenKickstartSource.CopperStart,
			"kickstart13rom" or "kickstart13" or "rom" => CopperScreenKickstartSource.Kickstart13Rom,
			_ => throw new InvalidOperationException($"Unsupported kickstart source '{source}'.")
		};
	}

	internal static M68kBackendKind ParseCpuBackend(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return M68kBackendKind.AccurateM68000;
		}

		var backend = value.Trim().ToLowerInvariant().Replace("-", string.Empty);
		return backend switch
		{
			"interpreter" or "accurate" or "accuratem68000" or "m68000" => M68kBackendKind.AccurateM68000,
			"jit" or "jitm68000" => M68kBackendKind.JitM68000,
			_ => throw new InvalidOperationException($"Unsupported CPU backend '{value}'.")
		};
	}

	private static FloppyDriveAudioOptions ParseFloppyDriveAudio(FloppyDriveSoundFile? config)
	{
		if (config == null)
		{
			return FloppyDriveAudioOptions.Default;
		}

		return new FloppyDriveAudioOptions(
			config.Enabled,
			ParseFloppyDriveAudioMode(config.Mode),
			string.IsNullOrWhiteSpace(config.SoundPack)
				? FloppyDriveAudioOptions.DefaultSoundPack
				: config.SoundPack.Trim(),
			FloppyDriveAudioOptions.ClampVolume(config.Volume ?? FloppyDriveAudioOptions.DefaultVolume));
	}

	private static IReadOnlyList<CopperScreenMediaDriveSettings> ParseMediaDrives(MediaFile? config)
	{
		if (config?.Drives == null || config.Drives.Length == 0)
		{
			return Array.Empty<CopperScreenMediaDriveSettings>();
		}

		var drives = new List<CopperScreenMediaDriveSettings>(config.Drives.Length);
		for (var i = 0; i < config.Drives.Length; i++)
		{
			var drive = config.Drives[i];
			var index = drive.Index ?? i;
			if (index is < 0 or > 3)
			{
				throw new InvalidOperationException("media.drives[].index must be between 0 and 3.");
			}

			drives.Add(new CopperScreenMediaDriveSettings(
				index,
				string.IsNullOrWhiteSpace(drive.DiskPath) ? null : drive.DiskPath.Trim(),
				drive.WriteProtected));
		}

		return drives;
	}

	private static CopperScreenInputOptions ParseInputOptions(InputFile? config)
	{
		if (config == null)
		{
			return CopperScreenInputOptions.Default;
		}

		if (config.Ports != null || config.ControllerProfiles is { Length: > 0 })
		{
			return CopperScreenInputOptions.Create(
				config.Ports?.Port1?.ProfileId,
				config.Ports?.Port2?.ProfileId,
				ParseControllerProfiles(config.ControllerProfiles));
		}

		return CopperScreenInputOptions.Create(
			config.MousePort ?? CopperScreenInputOptions.DefaultMousePort,
			ParseJoystickKeyMap(config.JoystickKeys));
	}

	private static CopperScreenPresentationOptions ParsePresentationOptions(PresentationFile? config)
	{
		return new CopperScreenPresentationOptions(
			CopperScreenPresentationOptions.ParseLacedMode(config?.LacedMode));
	}

	private static IReadOnlyList<CopperScreenControllerProfile> ParseControllerProfiles(ControllerProfileFile[]? profiles)
	{
		if (profiles == null || profiles.Length == 0)
		{
			return CopperScreenInputOptions.DefaultControllerProfiles;
		}

		var parsed = new List<CopperScreenControllerProfile>(profiles.Length);
		foreach (var profile in profiles)
		{
			var id = CopperScreenInputOptions.NormalizeProfileId(profile.Id);
			if (string.IsNullOrWhiteSpace(id))
			{
				continue;
			}

			parsed.Add(new CopperScreenControllerProfile(
				id,
				string.IsNullOrWhiteSpace(profile.DisplayName) ? id : profile.DisplayName.Trim(),
				ParseControllerKind(profile.Kind),
				ParseJoystickKeyMap(profile.JoystickKeys)));
		}

		return parsed;
	}

	private static CopperScreenControllerKind ParseControllerKind(string? value)
	{
		if (Enum.TryParse<CopperScreenControllerKind>(value, ignoreCase: true, out var kind))
		{
			return kind;
		}

		return CopperScreenControllerKind.None;
	}

	private static CopperScreenJoystickKeyMap ParseJoystickKeyMap(JoystickKeysFile? keys)
		=> CopperScreenJoystickKeyMap.Create(
			keys?.Up,
			keys?.Down,
			keys?.Left,
			keys?.Right,
			keys?.Fire,
			keys?.SecondFire);

	private static FloppyDriveAudioMode ParseFloppyDriveAudioMode(string? value)
	{
		if (FloppyDriveAudioOptions.TryParseMode(value, out var mode))
		{
			return mode;
		}

		throw new InvalidOperationException($"Unsupported floppy drive sound mode '{value}'.");
	}

	private static CopperScreenProfile CreateFallbackDefault()
	{
		return new CopperScreenProfile(
			DefaultProfileId,
			"Expanded A500 + CopperStart",
			"A500 PAL, 512 KB chip RAM, 512 KB pseudo-fast RAM at $C00000, CopperStart Kickstart 1.3 shim.",
			AmigaConstants.A500BootChipRamSize,
			AmigaConstants.A500BootPseudoFastRamSize,
			AmigaConstants.A500BootPseudoFastRamBase,
			0,
			AmigaConstants.A500RealFastRamBase,
			true,
			2,
			M68kBackendKind.AccurateM68000,
			FloppyDriveAudioOptions.Default,
			CopperScreenKickstartSource.CopperStart,
			Array.Empty<CopperScreenMediaDriveSettings>(),
			CopperScreenInputOptions.Default,
			CopperScreenPresentationOptions.Default,
			null);
	}

	private sealed class ProfileFile
	{
		public string? Id { get; set; }

		public string? DisplayName { get; set; }

		public string? Description { get; set; }

		public MachineFile? Machine { get; set; }

		public CpuFile? Cpu { get; set; }

		public AudioFile? Audio { get; set; }

		public KickstartFile? Kickstart { get; set; }

		public MediaFile? Media { get; set; }

		public InputFile? Input { get; set; }

		public PresentationFile? Presentation { get; set; }
	}

	private sealed class MachineFile
	{
		public string? Model { get; set; }

		public int ChipRamKb { get; set; }

		public int PseudoFastRamKb { get; set; }

		public string? PseudoFastBase { get; set; }

		public int RealFastRamKb { get; set; }

		public string? RealFastBase { get; set; }

		public bool? RtcEnabled { get; set; }

		public int? FloppyDriveCount { get; set; }
	}

	private sealed class CpuFile
	{
		public string? Backend { get; set; }
	}

	private sealed class AudioFile
	{
		public FloppyDriveSoundFile? FloppyDriveSounds { get; set; }
	}

	private sealed class FloppyDriveSoundFile
	{
		public bool Enabled { get; set; }

		public string? Mode { get; set; }

		public string? SoundPack { get; set; }

		public float? Volume { get; set; }
	}

	private sealed class KickstartFile
	{
		public string? Source { get; set; }

		public string? Version { get; set; }
	}

	private sealed class MediaFile
	{
		public MediaDriveFile[]? Drives { get; set; }
	}

	private sealed class MediaDriveFile
	{
		public int? Index { get; set; }

		public string? DiskPath { get; set; }

		public bool? WriteProtected { get; set; }
	}

	private sealed class InputFile
	{
		public int? MousePort { get; set; }

		public JoystickKeysFile? JoystickKeys { get; set; }

		public InputPortsFile? Ports { get; set; }

		public ControllerProfileFile[]? ControllerProfiles { get; set; }
	}

	private sealed class PresentationFile
	{
		public string? LacedMode { get; set; }
	}

	private sealed class InputPortsFile
	{
		public InputPortFile? Port1 { get; set; }

		public InputPortFile? Port2 { get; set; }
	}

	private sealed class InputPortFile
	{
		public string? ProfileId { get; set; }
	}

	private sealed class ControllerProfileFile
	{
		public string? Id { get; set; }

		public string? DisplayName { get; set; }

		public string? Kind { get; set; }

		public JoystickKeysFile? JoystickKeys { get; set; }
	}

	private sealed class JoystickKeysFile
	{
		public string[]? Up { get; set; }

		public string[]? Down { get; set; }

		public string[]? Left { get; set; }

		public string[]? Right { get; set; }

		public string[]? Fire { get; set; }

		public string[]? SecondFire { get; set; }
	}
}
