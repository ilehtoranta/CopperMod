using CopperMod.Amiga;

namespace CopperScreen;

internal sealed class CopperScreenStartupOptions
{
	private CopperScreenStartupOptions(
		CopperScreenProfile profile,
		string? diskPath,
		string?[] driveDiskPaths,
		bool?[] driveWriteProtected,
		IReadOnlyList<CopperScreenHardfileSettings> hardDrives,
		string? kickstartRomPath,
		M68kBackendKind? cpuBackendOverride,
		FloppyDriveAudioOptions floppyDriveAudio,
		CopperScreenInputOptions input,
		string baseDirectory,
		bool hasExplicitProfile,
		string? error)
	{
		Profile = profile;
		DiskPath = diskPath;
		DriveDiskPaths = driveDiskPaths;
		DriveWriteProtected = driveWriteProtected;
		HardDrives = hardDrives;
		KickstartRomPath = kickstartRomPath;
		CpuBackendOverride = cpuBackendOverride;
		FloppyDriveAudio = floppyDriveAudio;
		Input = input;
		BaseDirectory = baseDirectory;
		HasExplicitProfile = hasExplicitProfile;
		Error = error;
	}

	public CopperScreenProfile Profile { get; }

	public string? DiskPath { get; }

	public string?[] DriveDiskPaths { get; }

	public bool?[] DriveWriteProtected { get; }

	public IReadOnlyList<CopperScreenHardfileSettings> HardDrives { get; }

	public string? KickstartRomPath { get; }

	public M68kBackendKind? CpuBackendOverride { get; }

	public FloppyDriveAudioOptions FloppyDriveAudio { get; }

	public CopperScreenInputOptions Input { get; }

	public string BaseDirectory { get; }

	public bool HasExplicitProfile { get; }

	public string? Error { get; }

	internal static CopperScreenStartupOptions FromSettings(
		CopperScreenProfile profile,
		string?[] driveDiskPaths,
		bool?[] driveWriteProtected,
		string? kickstartRomPath,
		M68kBackendKind? cpuBackendOverride,
		FloppyDriveAudioOptions floppyDriveAudio,
		CopperScreenInputOptions input,
		string baseDirectory,
		bool hasExplicitProfile = true,
		string? error = null)
	{
		var normalizedDriveDiskPaths = NormalizeDrivePaths(driveDiskPaths, baseDirectory);
		var normalizedWriteProtected = NormalizeDriveWriteProtected(driveWriteProtected);
		var normalizedHardDrives = NormalizeHardDrivePaths(profile.HardDrives, baseDirectory);
		return new CopperScreenStartupOptions(
			profile,
			normalizedDriveDiskPaths[0],
			normalizedDriveDiskPaths,
			normalizedWriteProtected,
			normalizedHardDrives,
			ResolveRomPath(kickstartRomPath ?? profile.KickstartRomPath, baseDirectory),
			cpuBackendOverride,
			floppyDriveAudio,
			input,
			baseDirectory,
			hasExplicitProfile,
			error);
	}

	public static CopperScreenStartupOptions Default(string baseDirectory)
	{
		var profile = CopperScreenProfile.LoadDefault(baseDirectory, out var error);
		var driveDiskPaths = CreateDriveDiskPathArray(profile, null, baseDirectory);
		var hardDrives = NormalizeHardDrivePaths(profile.HardDrives, baseDirectory);
		return new CopperScreenStartupOptions(
			profile,
			null,
			driveDiskPaths,
			CreateDriveWriteProtectedArray(profile),
			hardDrives,
			ResolveRomPath(profile.KickstartRomPath, baseDirectory),
			null,
			profile.FloppyDriveAudio,
			profile.Input,
			baseDirectory,
			false,
			error);
	}

	public static CopperScreenStartupOptions Parse(string[]? args, string baseDirectory)
	{
		var startupArgs = args ?? Array.Empty<string>();
		var profile = CopperScreenProfile.LoadDefault(baseDirectory, out var defaultProfileError);
		var error = defaultProfileError;
		var profileExplicit = false;
		string? diskPath = null;
		string? kickstartRomPath = null;
		M68kBackendKind? cpuBackendOverride = null;
		bool? floppySoundsEnabledOverride = null;
		FloppyDriveAudioMode? floppySoundModeOverride = null;
		string? floppySoundPackOverride = null;
		float? floppySoundVolumeOverride = null;
		var hardDriveOverrides = new List<CopperScreenHardfileSettings>();

		for (var i = 0; i < startupArgs.Length; i++)
		{
			var arg = startupArgs[i];
			if (string.IsNullOrWhiteSpace(arg))
			{
				continue;
			}

			if (TryReadOptionValue(startupArgs, ref i, arg, "--profile", "-p", out var profileValue))
			{
				if (!CopperScreenProfile.TryLoad(profileValue, baseDirectory, out profile, out var profileError))
				{
					error ??= profileError;
				}
				else if (error == defaultProfileError)
				{
					error = null;
				}

				profileExplicit = true;
				continue;
			}

			if (TryReadOptionValue(startupArgs, ref i, arg, "--kickstart", "--kickstart-rom", out var romValue) ||
				TryReadOptionValue(startupArgs, ref i, arg, "--rom", null, out romValue))
			{
				kickstartRomPath = ResolveOptionalPath(romValue, baseDirectory);
				continue;
			}

			if (TryReadOptionValue(startupArgs, ref i, arg, "--cpu", null, out var cpuBackendValue))
			{
				try
				{
					cpuBackendOverride = CopperScreenProfile.ParseCpuBackend(cpuBackendValue);
				}
				catch (InvalidOperationException ex)
				{
					error ??= ex.Message;
				}

				continue;
			}

			if (IsOption(arg, "--jit"))
			{
				cpuBackendOverride = M68kBackendKind.JitM68000;
				continue;
			}

			if (IsOption(arg, "--jit-m68040") ||
				IsOption(arg, "--jit-68040") ||
				IsOption(arg, "--m68040-jit"))
			{
				cpuBackendOverride = M68kBackendKind.JitM68040;
				continue;
			}

			if (IsOption(arg, "--m68020") || IsOption(arg, "--68020"))
			{
				cpuBackendOverride = M68kBackendKind.AccurateM68020;
				continue;
			}

			if (IsOption(arg, "--m68030") || IsOption(arg, "--68030"))
			{
				cpuBackendOverride = M68kBackendKind.AccurateM68030;
				continue;
			}

			if (IsOption(arg, "--m68040") || IsOption(arg, "--68040"))
			{
				cpuBackendOverride = M68kBackendKind.AccurateM68040;
				continue;
			}

			if (TryReadOptionValue(startupArgs, ref i, arg, "--floppy-sounds", null, out var floppySoundsValue))
			{
				if (TryParseOnOff(floppySoundsValue, out var enabled))
				{
					floppySoundsEnabledOverride = enabled;
				}
				else
				{
					error ??= $"Unsupported floppy sound setting '{floppySoundsValue}'. Use on or off.";
				}

				continue;
			}

			if (TryReadOptionValue(startupArgs, ref i, arg, "--floppy-sound-pack", null, out var floppySoundPack))
			{
				floppySoundPackOverride = floppySoundPack;
				continue;
			}

			if (TryReadOptionValue(startupArgs, ref i, arg, "--floppy-sound-mode", null, out var floppySoundMode))
			{
				if (FloppyDriveAudioOptions.TryParseMode(floppySoundMode, out var mode))
				{
					floppySoundModeOverride = mode;
				}
				else
				{
					error ??= $"Unsupported floppy sound mode '{floppySoundMode}'. Use synthetic or samples.";
				}

				continue;
			}

			if (TryReadOptionValue(startupArgs, ref i, arg, "--floppy-sound-volume", null, out var floppySoundVolume))
			{
				if (float.TryParse(floppySoundVolume, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var volume))
				{
					floppySoundVolumeOverride = FloppyDriveAudioOptions.ClampVolume(volume);
				}
				else
				{
					error ??= $"Unsupported floppy sound volume '{floppySoundVolume}'.";
				}

				continue;
			}

			if (TryReadOptionValue(startupArgs, ref i, arg, "--hdf", null, out var hardfileValue) ||
				TryReadOptionValue(startupArgs, ref i, arg, "--hardfile", null, out hardfileValue))
			{
				hardDriveOverrides.Add(new CopperScreenHardfileSettings(
					hardDriveOverrides.Count,
					ResolveOptionalPath(hardfileValue, baseDirectory) ?? hardfileValue,
					ReadOnly: false,
					CreateSizeBytes: 0));
				continue;
			}

			if (TryReadOptionValue(startupArgs, ref i, arg, "--hdf-readonly", null, out var readonlyHardfileValue) ||
				TryReadOptionValue(startupArgs, ref i, arg, "--hardfile-readonly", null, out readonlyHardfileValue))
			{
				hardDriveOverrides.Add(new CopperScreenHardfileSettings(
					hardDriveOverrides.Count,
					ResolveOptionalPath(readonlyHardfileValue, baseDirectory) ?? readonlyHardfileValue,
					ReadOnly: true,
					CreateSizeBytes: 0));
				continue;
			}

			if (IsOption(arg, "--real-kickstart"))
			{
				if (!CopperScreenProfile.TryLoadWithKickstartSource(
					profile,
					CopperScreenKickstartSource.Kickstart13Rom,
					baseDirectory,
					out profile,
					out var profileError))
				{
					error ??= profileError;
				}
				else if (error == defaultProfileError)
				{
					error = null;
				}

				profileExplicit = true;
				continue;
			}

			if (IsOption(arg, "--copperstart"))
			{
				if (!CopperScreenProfile.TryLoadWithKickstartSource(
					profile,
					CopperScreenKickstartSource.CopperStart,
					baseDirectory,
					out profile,
					out var profileError))
				{
					error ??= profileError;
				}
				else if (error == defaultProfileError)
				{
					error = null;
				}

				profileExplicit = true;
				continue;
			}

			if (arg.StartsWith("-", StringComparison.Ordinal))
			{
				error ??= $"Unknown CopperScreen option '{arg}'.";
				continue;
			}

			if (diskPath == null)
			{
				diskPath = ResolveExistingFile(arg, baseDirectory);
			}
		}

		if (kickstartRomPath != null && !profileExplicit)
		{
			if (!CopperScreenProfile.TryLoadWithKickstartSource(
				profile,
				CopperScreenKickstartSource.Kickstart13Rom,
				baseDirectory,
				out profile,
				out var profileError))
			{
				error ??= profileError;
			}
			else if (error == defaultProfileError)
			{
				error = null;
			}
		}
		else if (kickstartRomPath != null && !profile.UsesKickstartRom)
		{
			error ??= "A Kickstart ROM path was supplied with a CopperStart profile.";
		}

		var floppyDriveAudio = profile.FloppyDriveAudio.WithOverrides(
			floppySoundsEnabledOverride,
			floppySoundModeOverride,
			floppySoundPackOverride,
			floppySoundVolumeOverride);
		var driveDiskPaths = CreateDriveDiskPathArray(profile, diskPath, baseDirectory);
		var driveWriteProtected = CreateDriveWriteProtectedArray(profile);
		var hardDrives = hardDriveOverrides.Count == 0
			? NormalizeHardDrivePaths(profile.HardDrives, baseDirectory)
			: hardDriveOverrides;
		var resolvedKickstartRomPath = ResolveRomPath(kickstartRomPath ?? profile.KickstartRomPath, baseDirectory);
		return new CopperScreenStartupOptions(
			profile,
			driveDiskPaths[0],
			driveDiskPaths,
			driveWriteProtected,
			hardDrives,
			resolvedKickstartRomPath,
			cpuBackendOverride,
			floppyDriveAudio,
			profile.Input,
			baseDirectory,
			profileExplicit,
			error);
	}

	private static string?[] CreateDriveDiskPathArray(CopperScreenProfile profile, string? diskPath, string baseDirectory)
	{
		var paths = new string?[4];
		if (profile.MediaDrives.Count > 0)
		{
			for (var i = 0; i < profile.MediaDrives.Count; i++)
			{
				var drive = profile.MediaDrives[i];
				if ((uint)drive.Index < (uint)paths.Length)
				{
					paths[drive.Index] = ResolveOptionalPath(drive.DiskPath, baseDirectory);
				}
			}
		}

		if (diskPath != null)
		{
			paths[0] = diskPath;
			PopulateArchiveDiskSetPaths(paths, diskPath);
		}

		return paths;
	}

	private static void PopulateArchiveDiskSetPaths(string?[] paths, string diskPath)
	{
		if (!CopperScreenDiskImageArchive.TryReadDiskSet(diskPath, out var diskSet, out _) ||
			diskSet == null ||
			diskSet.Entries.Count < 2)
		{
			return;
		}

		foreach (var assignment in diskSet.CreateDefaultAssignments())
		{
			if ((uint)assignment.DriveIndex >= (uint)paths.Length ||
				string.IsNullOrWhiteSpace(assignment.DiskPath))
			{
				continue;
			}

			if (assignment.DriveIndex != 0 && !string.IsNullOrWhiteSpace(paths[assignment.DriveIndex]))
			{
				continue;
			}

			paths[assignment.DriveIndex] = assignment.DiskPath;
		}
	}

	private static bool?[] CreateDriveWriteProtectedArray(CopperScreenProfile profile)
	{
		var writeProtected = new bool?[4];
		foreach (var drive in profile.MediaDrives)
		{
			if ((uint)drive.Index < (uint)writeProtected.Length)
			{
				writeProtected[drive.Index] = drive.WriteProtected;
			}
		}

		return writeProtected;
	}

	private static string?[] NormalizeDrivePaths(string?[] driveDiskPaths, string baseDirectory)
	{
		var paths = new string?[4];
		for (var i = 0; i < Math.Min(paths.Length, driveDiskPaths.Length); i++)
		{
			paths[i] = ResolveOptionalPath(driveDiskPaths[i], baseDirectory);
		}

		if (!string.IsNullOrWhiteSpace(paths[0]))
		{
			PopulateArchiveDiskSetPaths(paths, paths[0]!);
		}

		return paths;
	}

	private static bool?[] NormalizeDriveWriteProtected(bool?[] driveWriteProtected)
	{
		var values = new bool?[4];
		for (var i = 0; i < Math.Min(values.Length, driveWriteProtected.Length); i++)
		{
			values[i] = driveWriteProtected[i];
		}

		return values;
	}

	private static IReadOnlyList<CopperScreenHardfileSettings> NormalizeHardDrivePaths(
		IReadOnlyList<CopperScreenHardfileSettings> hardDrives,
		string baseDirectory)
	{
		if (hardDrives.Count == 0)
		{
			return Array.Empty<CopperScreenHardfileSettings>();
		}

		var normalized = new List<CopperScreenHardfileSettings>(hardDrives.Count);
		foreach (var drive in hardDrives)
		{
			var path = ResolveOptionalPath(drive.Path, baseDirectory) ?? Path.GetFullPath(Path.Combine(baseDirectory, drive.Path));
			normalized.Add(drive with { Path = path });
		}

		return normalized;
	}

	private static bool TryParseOnOff(string value, out bool enabled)
	{
		switch (value.Trim().ToLowerInvariant())
		{
			case "on":
			case "true":
			case "1":
			case "yes":
			case "enabled":
				enabled = true;
				return true;
			case "off":
			case "false":
			case "0":
			case "no":
			case "disabled":
				enabled = false;
				return true;
			default:
				enabled = false;
				return false;
		}
	}

	private static bool TryReadOptionValue(
		string[] args,
		ref int index,
		string arg,
		string longName,
		string? shortName,
		out string value)
	{
		value = string.Empty;
		if (arg.StartsWith(longName + "=", StringComparison.OrdinalIgnoreCase))
		{
			value = arg[(longName.Length + 1)..];
			return true;
		}

		if (shortName != null && arg.StartsWith(shortName + "=", StringComparison.OrdinalIgnoreCase))
		{
			value = arg[(shortName.Length + 1)..];
			return true;
		}

		if (!IsOption(arg, longName) && (shortName == null || !IsOption(arg, shortName)))
		{
			return false;
		}

		if (index + 1 < args.Length)
		{
			index++;
			value = args[index];
		}

		return true;
	}

	private static bool IsOption(string arg, string option)
	{
		return string.Equals(arg, option, StringComparison.OrdinalIgnoreCase);
	}

	private static string? ResolveExistingFile(string path, string baseDirectory)
	{
		var resolved = ResolveOptionalPath(path, baseDirectory);
		return resolved != null && CopperScreenDiskImageArchive.DiskPathExists(resolved) ? resolved : null;
	}

	private static string? ResolveOptionalPath(string? path, string baseDirectory)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}

		if (CopperScreenDiskImageArchive.TrySplitEntryPathRaw(path, out var archivePath, out var entryName))
		{
			var resolvedArchivePath = ResolvePlainOptionalPath(archivePath, baseDirectory);
			return resolvedArchivePath == null ? null : CopperScreenDiskImageArchive.CreateEntryPath(resolvedArchivePath, entryName);
		}

		return ResolvePlainOptionalPath(path, baseDirectory);
	}

	private static string? ResolvePlainOptionalPath(string path, string baseDirectory)
	{
		if (Path.IsPathFullyQualified(path))
		{
			return Path.GetFullPath(path);
		}

		foreach (var candidate in EnumerateRelativePathCandidates(path, baseDirectory))
		{
			if (File.Exists(candidate))
			{
				return Path.GetFullPath(candidate);
			}
		}

		return Path.GetFullPath(Path.Combine(baseDirectory, path));
	}

	private static string? ResolveRomPath(string? path, string baseDirectory)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}

		if (Path.IsPathFullyQualified(path))
		{
			return Path.GetFullPath(path);
		}

		foreach (var candidate in EnumerateRelativePathCandidates(path, baseDirectory))
		{
			if (File.Exists(candidate))
			{
				return Path.GetFullPath(candidate);
			}
		}

		return Path.GetFullPath(Path.Combine(baseDirectory, path));
	}

	private static IEnumerable<string> EnumerateRelativePathCandidates(string path, string baseDirectory)
	{
		yield return Path.GetFullPath(path);
		yield return Path.GetFullPath(Path.Combine(baseDirectory, path));

		var directory = new DirectoryInfo(baseDirectory);
		while (directory != null)
		{
			yield return Path.Combine(directory.FullName, path);
			yield return Path.Combine(directory.FullName, "CopperScreen", path);
			directory = directory.Parent;
		}
	}
}
