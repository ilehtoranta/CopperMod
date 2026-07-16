using System.Text.Json;
using System.Text.Json.Serialization;
using CopperMod.Amiga;

namespace CopperScreen;

internal sealed record CopperScreenMediaDriveSettings(int Index, string? DiskPath, bool? WriteProtected);

internal sealed class CopperScreenSettingsDraft
{
	private const int Kilobyte = 1024;

	public string Id { get; set; } = CopperScreenProfile.DefaultProfileId;

	public string DisplayName { get; set; } = "Expanded A500 + CopperStart";

	public string Description { get; set; } = string.Empty;

	public int ChipRamKb { get; set; } = AmigaConstants.A500BootChipRamSize / Kilobyte;

	public int PseudoFastRamKb { get; set; } = AmigaConstants.A500BootPseudoFastRamSize / Kilobyte;

	public string PseudoFastBase { get; set; } = "$C00000";

	public int RealFastRamKb { get; set; }

	public string RealFastBase { get; set; } = "$200000";

	public bool RtcEnabled { get; set; } = true;

	public int FloppyDriveCount { get; set; } = 2;

	public M68kBackendKind CpuBackend { get; set; } = M68kBackendKind.AccurateM68000;

	public CopperScreenKickstartSource KickstartSource { get; set; } = CopperScreenKickstartSource.CopperStart;

	public string? KickstartRomPath { get; set; }

	public FloppyDriveAudioOptions FloppyDriveAudio { get; set; } = FloppyDriveAudioOptions.Default;

	public CopperScreenInputOptions Input { get; set; } = CopperScreenInputOptions.Default;

	public CopperScreenPresentationOptions PresentationOptions { get; set; } = CopperScreenPresentationOptions.Default;

	public string?[] DriveDiskPaths { get; } = new string?[4];

	public bool?[] DriveWriteProtected { get; } = new bool?[4];

	public List<CopperScreenHardfileSettings> HardDrives { get; } = new List<CopperScreenHardfileSettings>();

	public bool RequiresRestart { get; private set; }

	public static CopperScreenSettingsDraft FromStartupOptions(CopperScreenStartupOptions options)
	{
		var draft = FromProfile(options.Profile);
		draft.KickstartRomPath = options.KickstartRomPath;
		draft.CpuBackend = options.CpuBackendOverride ?? options.Profile.CpuBackend;
		draft.FloppyDriveAudio = options.FloppyDriveAudio;
		draft.Input = options.Input;
		for (var i = 0; i < draft.DriveDiskPaths.Length; i++)
		{
			draft.DriveDiskPaths[i] = i < options.DriveDiskPaths.Length ? options.DriveDiskPaths[i] : null;
			draft.DriveWriteProtected[i] = i < options.DriveWriteProtected.Length ? options.DriveWriteProtected[i] : null;
		}

		draft.HardDrives.AddRange(options.HardDrives);

		draft.RequiresRestart = false;
		return draft;
	}

	public static CopperScreenSettingsDraft FromProfile(CopperScreenProfile profile)
	{
		var draft = new CopperScreenSettingsDraft
		{
			Id = profile.Id,
			DisplayName = profile.DisplayName,
			Description = profile.Description,
			ChipRamKb = profile.ChipRamSize / Kilobyte,
			PseudoFastRamKb = profile.ExpansionRamSize / Kilobyte,
			PseudoFastBase = FormatAddress(profile.ExpansionRamBase),
			RealFastRamKb = profile.RealFastRamSize / Kilobyte,
			RealFastBase = FormatAddress(profile.RealFastRamBase),
			RtcEnabled = profile.RtcEnabled,
			FloppyDriveCount = profile.FloppyDriveCount,
			CpuBackend = profile.CpuBackend,
			KickstartSource = profile.KickstartSource,
			KickstartRomPath = profile.KickstartRomPath,
			FloppyDriveAudio = profile.FloppyDriveAudio,
			Input = profile.Input,
			PresentationOptions = profile.PresentationOptions
		};
		foreach (var drive in profile.MediaDrives)
		{
			if ((uint)drive.Index < (uint)draft.DriveDiskPaths.Length)
			{
				draft.DriveDiskPaths[drive.Index] = drive.DiskPath;
				draft.DriveWriteProtected[drive.Index] = drive.WriteProtected;
			}
		}

		draft.HardDrives.AddRange(profile.HardDrives);

		return draft;
	}

	public void MarkRequiresRestart()
	{
		RequiresRestart = true;
	}

	public void ClearRestartRequired()
	{
		RequiresRestart = false;
	}

	public CopperScreenStartupOptions ToStartupOptions(string baseDirectory)
	{
		var profile = ToProfile();
		return CopperScreenStartupOptions.FromSettings(
			profile,
			DriveDiskPaths,
			DriveWriteProtected,
			KickstartRomPath,
			null,
			FloppyDriveAudio,
			Input,
			baseDirectory);
	}

	public CopperScreenProfile ToProfile(string? configPath = null)
	{
		var realFastRamSize = checked(RealFastRamKb * Kilobyte);
		var realFastRamBase = string.IsNullOrWhiteSpace(RealFastBase) && realFastRamSize > 0
			? AutoconfigFastRamBoard.GetDefaultBase(realFastRamSize)
			: ParseAddress(RealFastBase, AmigaConstants.A500RealFastRamBase);
		return CopperScreenProfile.Create(
			Id,
			DisplayName,
			Description,
			checked(ChipRamKb * Kilobyte),
			checked(PseudoFastRamKb * Kilobyte),
			ParseAddress(PseudoFastBase, AmigaConstants.A500BootPseudoFastRamBase),
			realFastRamSize,
			realFastRamBase,
			RtcEnabled,
			FloppyDriveCount,
			CpuBackend,
			FloppyDriveAudio,
			KickstartSource,
			CreateMediaDrives(),
			HardDrives.ToArray(),
			Input,
			configPath,
			PresentationOptions,
			KickstartRomPath);
	}

	public IReadOnlyList<CopperScreenMediaDriveSettings> CreateMediaDrives()
	{
		var drives = new List<CopperScreenMediaDriveSettings>();
		for (var i = 0; i < DriveDiskPaths.Length; i++)
		{
			if (!string.IsNullOrWhiteSpace(DriveDiskPaths[i]) || DriveWriteProtected[i].HasValue)
			{
				drives.Add(new CopperScreenMediaDriveSettings(i, DriveDiskPaths[i], DriveWriteProtected[i]));
			}
		}

		return drives;
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
			return Convert.ToUInt32(trimmed[1..], 16);
		}

		if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			return Convert.ToUInt32(trimmed[2..], 16);
		}

		return Convert.ToUInt32(trimmed, 10);
	}

	private static string FormatAddress(uint address)
		=> "$" + address.ToString("X6", System.Globalization.CultureInfo.InvariantCulture);
}

internal static class CopperScreenProfileStore
{
	private static readonly JsonSerializerOptions WriteJsonOptions = new()
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		WriteIndented = true
	};

	public static IReadOnlyList<CopperScreenProfileSummary> ListProfiles(string baseDirectory)
	{
		var profilesDirectory = FindProfilesDirectory(baseDirectory);
		if (!Directory.Exists(profilesDirectory))
		{
			return CreateFallbackProfileList(baseDirectory);
		}

		var profiles = new List<CopperScreenProfileSummary>();
		foreach (var path in Directory.EnumerateFiles(profilesDirectory, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
		{
			if (CopperScreenProfile.TryLoad(path, baseDirectory, out var profile, out _))
			{
				profiles.Add(new CopperScreenProfileSummary(profile.Id, profile.DisplayName, path));
			}
		}

		AddFallbackProfileIfEmpty(profiles, baseDirectory);

		return profiles;
	}

	private static IReadOnlyList<CopperScreenProfileSummary> CreateFallbackProfileList(string baseDirectory)
	{
		var profiles = new List<CopperScreenProfileSummary>();
		AddFallbackProfileIfEmpty(profiles, baseDirectory);
		return profiles;
	}

	private static void AddFallbackProfileIfEmpty(List<CopperScreenProfileSummary> profiles, string baseDirectory)
	{
		if (profiles.Count != 0)
		{
			return;
		}

		var fallback = CopperScreenProfile.LoadDefault(baseDirectory, out _);
		profiles.Add(new CopperScreenProfileSummary(fallback.Id, fallback.DisplayName, fallback.ConfigPath ?? "Fallback default"));
	}

	public static string Save(CopperScreenSettingsDraft draft, string baseDirectory)
	{
		var profilesDirectory = FindProfilesDirectory(baseDirectory);
		Directory.CreateDirectory(profilesDirectory);
		var id = CopperScreenProfile.NormalizeProfileId(draft.Id);
		if (string.IsNullOrWhiteSpace(id))
		{
			throw new InvalidOperationException("Profile id is required.");
		}

		draft.Id = id;
		var path = Path.Combine(profilesDirectory, id + ".json");
		var file = ProfileFile.FromDraft(draft);
		File.WriteAllText(path, JsonSerializer.Serialize(file, WriteJsonOptions));
		return path;
	}

	public static string SaveAs(CopperScreenSettingsDraft draft, string baseDirectory)
	{
		var profilesDirectory = FindProfilesDirectory(baseDirectory);
		Directory.CreateDirectory(profilesDirectory);
		var baseId = CopperScreenProfile.NormalizeProfileId(draft.Id);
		if (string.IsNullOrWhiteSpace(baseId))
		{
			baseId = CopperScreenProfile.DefaultProfileId;
		}

		var candidateId = baseId.EndsWith("-copy", StringComparison.OrdinalIgnoreCase)
			? baseId
			: baseId + "-copy";
		var suffix = 2;
		while (File.Exists(Path.Combine(profilesDirectory, candidateId + ".json")))
		{
			candidateId = baseId + "-copy-" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
			suffix++;
		}

		draft.Id = candidateId;
		if (!draft.DisplayName.EndsWith(" Copy", StringComparison.OrdinalIgnoreCase))
		{
			draft.DisplayName += " Copy";
		}

		return Save(draft, baseDirectory);
	}

	public static string FindProfilesDirectory(string baseDirectory)
	{
		var directory = new DirectoryInfo(baseDirectory);
		while (directory != null)
		{
			var direct = Path.Combine(directory.FullName, "Profiles");
			if (Directory.Exists(direct))
			{
				return direct;
			}

			var project = Path.Combine(directory.FullName, "CopperScreen", "Profiles");
			if (Directory.Exists(project))
			{
				return project;
			}

			directory = directory.Parent;
		}

		return Path.Combine(baseDirectory, "Profiles");
	}

	private sealed class ProfileFile
	{
		public string Id { get; set; } = string.Empty;

		public string DisplayName { get; set; } = string.Empty;

		public string? Description { get; set; }

		public MachineFile Machine { get; set; } = new();

		public CpuFile? Cpu { get; set; }

		public AudioFile? Audio { get; set; }

		public KickstartFile Kickstart { get; set; } = new();

		public MediaFile? Media { get; set; }

		public HardDriveFile[]? HardDrives { get; set; }

		public InputFile? Input { get; set; }

		public PresentationFile? Presentation { get; set; }

		public static ProfileFile FromDraft(CopperScreenSettingsDraft draft)
		{
			return new ProfileFile
			{
				Id = CopperScreenProfile.NormalizeProfileId(draft.Id),
				DisplayName = draft.DisplayName.Trim(),
				Description = string.IsNullOrWhiteSpace(draft.Description) ? null : draft.Description.Trim(),
				Machine = new MachineFile
				{
					Model = "A500PAL",
					ChipRamKb = draft.ChipRamKb,
					PseudoFastRamKb = draft.PseudoFastRamKb,
					PseudoFastBase = draft.PseudoFastBase,
					RealFastRamKb = draft.RealFastRamKb == 0 ? null : draft.RealFastRamKb,
					RealFastBase = draft.RealFastRamKb == 0 ? null : draft.RealFastBase,
					RtcEnabled = draft.RtcEnabled,
					FloppyDriveCount = draft.FloppyDriveCount
				},
				Cpu = draft.CpuBackend == M68kBackendKind.AccurateM68000 ? null : new CpuFile { Backend = draft.CpuBackend.ToString() },
				Audio = draft.FloppyDriveAudio == FloppyDriveAudioOptions.Default
					? null
					: new AudioFile
					{
						FloppyDriveSounds = new FloppyDriveSoundFile
						{
							Enabled = draft.FloppyDriveAudio.Enabled,
							Mode = draft.FloppyDriveAudio.Mode.ToString(),
							SoundPack = draft.FloppyDriveAudio.SoundPack,
							Volume = draft.FloppyDriveAudio.Volume
						}
					},
				Kickstart = new KickstartFile
				{
					Source = draft.KickstartSource.ToString(),
					Version = draft.KickstartSource == CopperScreenKickstartSource.Kickstart13Rom
						? "1.3"
						: draft.KickstartSource == CopperScreenKickstartSource.DiagRom ? "2.0" : null,
					Path = string.IsNullOrWhiteSpace(draft.KickstartRomPath) ? null : draft.KickstartRomPath.Trim()
				},
				Media = draft.CreateMediaDrives().Count == 0
					? null
					: new MediaFile
					{
						Drives = draft.CreateMediaDrives()
							.Select(drive => new MediaDriveFile
							{
								Index = drive.Index,
								DiskPath = drive.DiskPath,
								WriteProtected = drive.WriteProtected
							})
							.ToArray()
					},
				HardDrives = draft.HardDrives.Count == 0
					? null
					: draft.HardDrives
						.Select(drive => new HardDriveFile
						{
							Unit = drive.Unit,
							Path = drive.Path,
							ReadOnly = drive.ReadOnly,
							SizeMb = drive.CreateSizeBytes == 0 ? null : checked((int)(drive.CreateSizeBytes / (1024 * 1024))),
							Mode = drive.Mode == AmigaHardfileMountMode.Auto ? null : FormatHardfileMountMode(drive.Mode),
							Partition = HardDrivePartitionFile.FromMetadata(drive.Partition)
						})
						.ToArray(),
				Input = IsDefaultInput(draft.Input)
					? null
					: new InputFile
					{
						Ports = new InputPortsFile
						{
							Port1 = new InputPortFile { ProfileId = draft.Input.Port1ProfileId },
							Port2 = new InputPortFile { ProfileId = draft.Input.Port2ProfileId }
						},
						ControllerProfiles = draft.Input.ControllerProfiles
							.Where(profile => profile.Kind != CopperScreenControllerKind.None)
							.Select(profile => new ControllerProfileFile
							{
								Id = profile.Id,
								DisplayName = profile.DisplayName,
								Kind = profile.Kind.ToString(),
								JoystickKeys = profile.Kind == CopperScreenControllerKind.KeyboardJoystick
									? new JoystickKeysFile
									{
										Up = profile.JoystickKeys.Up,
										Down = profile.JoystickKeys.Down,
										Left = profile.JoystickKeys.Left,
										Right = profile.JoystickKeys.Right,
										Fire = profile.JoystickKeys.Fire,
										SecondFire = profile.JoystickKeys.SecondFire
									}
									: null
							})
							.ToArray()
					},
				Presentation = new PresentationFile
				{
					LacedMode = draft.PresentationOptions.LacedMode.ToString()
				}
			};
		}

		private static bool IsDefaultInput(CopperScreenInputOptions input)
		{
			return string.Equals(input.Port1ProfileId, CopperScreenInputOptions.Default.Port1ProfileId, StringComparison.OrdinalIgnoreCase) &&
				string.Equals(input.Port2ProfileId, CopperScreenInputOptions.Default.Port2ProfileId, StringComparison.OrdinalIgnoreCase) &&
				input.ControllerProfiles.Count == CopperScreenInputOptions.Default.ControllerProfiles.Count &&
				input.ControllerProfiles.All(profile => CopperScreenInputOptions.Default.ControllerProfiles.Any(defaultProfile =>
					string.Equals(defaultProfile.Id, profile.Id, StringComparison.OrdinalIgnoreCase) &&
					defaultProfile.Kind == profile.Kind &&
					SameKeys(defaultProfile.JoystickKeys, profile.JoystickKeys)));
		}

		private static bool SameKeys(CopperScreenJoystickKeyMap left, CopperScreenJoystickKeyMap right)
			=> left.Up.SequenceEqual(right.Up, StringComparer.OrdinalIgnoreCase) &&
				left.Down.SequenceEqual(right.Down, StringComparer.OrdinalIgnoreCase) &&
				left.Left.SequenceEqual(right.Left, StringComparer.OrdinalIgnoreCase) &&
				left.Right.SequenceEqual(right.Right, StringComparer.OrdinalIgnoreCase) &&
				left.Fire.SequenceEqual(right.Fire, StringComparer.OrdinalIgnoreCase) &&
				left.SecondFire.SequenceEqual(right.SecondFire, StringComparer.OrdinalIgnoreCase);

		private static string FormatHardfileMountMode(AmigaHardfileMountMode mode)
			=> mode switch
			{
				AmigaHardfileMountMode.RigidDiskBlock => "rdb",
				AmigaHardfileMountMode.Partition => "partition",
				_ => "auto"
			};
	}

	private sealed class MachineFile
	{
		public string Model { get; set; } = "A500PAL";

		public int ChipRamKb { get; set; }

		public int PseudoFastRamKb { get; set; }

		public string? PseudoFastBase { get; set; }

		public int? RealFastRamKb { get; set; }

		public string? RealFastBase { get; set; }

		public bool? RtcEnabled { get; set; }

		public int FloppyDriveCount { get; set; }
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
		public string Source { get; set; } = "CopperStart";

		public string? Version { get; set; } = "1.3";

		public string? Path { get; set; }
	}

	private sealed class MediaFile
	{
		public MediaDriveFile[]? Drives { get; set; }
	}

	private sealed class MediaDriveFile
	{
		public int Index { get; set; }

		public string? DiskPath { get; set; }

		public bool? WriteProtected { get; set; }
	}

	private sealed class HardDriveFile
	{
		public int Unit { get; set; }

		public string Path { get; set; } = string.Empty;

		public bool ReadOnly { get; set; }

		public int? SizeMb { get; set; }

		public string? Mode { get; set; }

		public HardDrivePartitionFile? Partition { get; set; }
	}

	private sealed class HardDrivePartitionFile
	{
		public string? DeviceName { get; set; }

		public uint? TableSize { get; set; }

		public uint? SizeBlockLongs { get; set; }

		public uint? SectorOrigin { get; set; }

		public uint? Surfaces { get; set; }

		public uint? SectorsPerBlock { get; set; }

		public uint? BlocksPerTrack { get; set; }

		public uint? ReservedBlocks { get; set; }

		public uint? PreAllocBlocks { get; set; }

		public uint? Interleave { get; set; }

		public uint? LowCylinder { get; set; }

		public uint? HighCylinder { get; set; }

		public uint? NumBuffers { get; set; }

		public uint? BufferMemoryType { get; set; }

		public string? MaxTransfer { get; set; }

		public string? Mask { get; set; }

		public int? BootPriority { get; set; }

		public string? DosType { get; set; }

		public static HardDrivePartitionFile? FromMetadata(AmigaHardfilePartitionMetadata? metadata)
		{
			if (metadata == null)
			{
				return null;
			}

			return new HardDrivePartitionFile
			{
				DeviceName = metadata.DeviceName,
				TableSize = metadata.TableSize,
				SizeBlockLongs = metadata.SizeBlockLongs,
				SectorOrigin = metadata.SectorOrigin,
				Surfaces = metadata.Surfaces,
				SectorsPerBlock = metadata.SectorsPerBlock,
				BlocksPerTrack = metadata.BlocksPerTrack,
				ReservedBlocks = metadata.ReservedBlocks,
				PreAllocBlocks = metadata.PreAllocBlocks,
				Interleave = metadata.Interleave,
				LowCylinder = metadata.LowCylinder,
				HighCylinder = metadata.HighCylinder,
				NumBuffers = metadata.NumBuffers,
				BufferMemoryType = metadata.BufferMemoryType,
				MaxTransfer = FormatOptionalHex(metadata.MaxTransfer),
				Mask = FormatOptionalHex(metadata.Mask),
				BootPriority = metadata.BootPriority,
				DosType = FormatOptionalHex(metadata.DosType)
			};
		}

		private static string? FormatOptionalHex(uint? value)
			=> value.HasValue ? "$" + value.Value.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) : null;
	}

	private sealed class InputFile
	{
		public InputPortsFile? Ports { get; set; }

		public ControllerProfileFile[]? ControllerProfiles { get; set; }
	}

	private sealed class PresentationFile
	{
		public string LacedMode { get; set; } = CopperScreenLacedPresentationMode.CrtFlicker.ToString();
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

internal sealed record CopperScreenProfileSummary(string Id, string DisplayName, string Path)
{
	public override string ToString()
		=> DisplayName + " (" + Id + ") - " + Path;
}
