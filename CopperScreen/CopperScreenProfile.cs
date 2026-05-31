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
		int floppyDriveCount,
		CopperScreenKickstartSource kickstartSource,
		string? configPath)
	{
		Id = id;
		DisplayName = displayName;
		Description = description;
		ChipRamSize = chipRamSize;
		ExpansionRamSize = expansionRamSize;
		ExpansionRamBase = expansionRamBase;
		FloppyDriveCount = floppyDriveCount;
		KickstartSource = kickstartSource;
		ConfigPath = configPath;
	}

	public string Id { get; }

	public string DisplayName { get; }

	public string Description { get; }

	public int ChipRamSize { get; }

	public int ExpansionRamSize { get; }

	public uint ExpansionRamBase { get; }

	public int FloppyDriveCount { get; }

	public CopperScreenKickstartSource KickstartSource { get; }

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
			.WithFloppyDriveCount(FloppyDriveCount)
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
		var floppyDriveCount = machine.FloppyDriveCount ?? (expansionRamSize > 0 ? 2 : 1);
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
			floppyDriveCount,
			kickstartSource,
			Path.GetFullPath(path));
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

	private static CopperScreenProfile CreateFallbackDefault()
	{
		return new CopperScreenProfile(
			DefaultProfileId,
			"Expanded A500 + CopperStart",
			"A500 PAL, 512 KB chip RAM, 512 KB pseudo-fast RAM at $C00000, CopperStart Kickstart 1.3 shim.",
			AmigaConstants.A500BootChipRamSize,
			AmigaConstants.A500BootPseudoFastRamSize,
			AmigaConstants.A500BootPseudoFastRamBase,
			2,
			CopperScreenKickstartSource.CopperStart,
			null);
	}

	private sealed class ProfileFile
	{
		public string? Id { get; set; }

		public string? DisplayName { get; set; }

		public string? Description { get; set; }

		public MachineFile? Machine { get; set; }

		public KickstartFile? Kickstart { get; set; }
	}

	private sealed class MachineFile
	{
		public string? Model { get; set; }

		public int ChipRamKb { get; set; }

		public int PseudoFastRamKb { get; set; }

		public string? PseudoFastBase { get; set; }

		public int? FloppyDriveCount { get; set; }
	}

	private sealed class KickstartFile
	{
		public string? Source { get; set; }

		public string? Version { get; set; }
	}
}
