using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CopperMod.Amiga;

namespace CopperScreen;

internal sealed class CopperScreenStartupOptions
{
	private CopperScreenStartupOptions(
		CopperScreenProfile profile,
		string? diskPath,
		string?[] driveDiskPaths,
		bool?[] driveWriteProtected,
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
		return new CopperScreenStartupOptions(
			profile,
			normalizedDriveDiskPaths[0],
			normalizedDriveDiskPaths,
			normalizedWriteProtected,
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
		return new CopperScreenStartupOptions(
			profile,
			null,
			driveDiskPaths,
			CreateDriveWriteProtectedArray(profile),
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
		var resolvedKickstartRomPath = ResolveRomPath(kickstartRomPath ?? profile.KickstartRomPath, baseDirectory);
		return new CopperScreenStartupOptions(
			profile,
			driveDiskPaths[0],
			driveDiskPaths,
			driveWriteProtected,
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

		var workbenchExternalDiskSourcePath = diskPath;
		if (diskPath != null)
		{
			paths[0] = profile.AutoStartWorkbenchStartupSequence
				? ResolveWorkbenchStartupDiskPath(diskPath, baseDirectory)
				: diskPath;
		}
		else if (profile.AutoStartWorkbenchStartupSequence)
		{
			workbenchExternalDiskSourcePath = paths[0];
			paths[0] = ResolveWorkbenchStartupDiskPath(paths[0], baseDirectory);
		}

		if (profile.AutoStartWorkbenchStartupSequence)
		{
			PopulateWorkbenchStartupExternalDisks(paths, workbenchExternalDiskSourcePath, baseDirectory, profile.FloppyDriveCount);
		}

		return paths;
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

	internal static string? ResolveWorkbenchStartupDiskPath(string? path, string baseDirectory)
	{
		var resolved = ResolveOptionalPath(path, baseDirectory);
		if (string.IsNullOrWhiteSpace(resolved) ||
			ContainsWorkbenchDesktop(resolved))
		{
			return resolved;
		}

		if (TryExtractWorkbenchDesktopDiskFromArchive(resolved, out var archivedDesktopDisk))
		{
			return archivedDesktopDisk;
		}

		foreach (var candidate in EnumerateWorkbenchDesktopDiskCandidates(resolved))
		{
			if (ContainsWorkbenchDesktop(candidate))
			{
				return candidate;
			}

			if (TryExtractWorkbenchDesktopDiskFromArchive(candidate, out archivedDesktopDisk))
			{
				return archivedDesktopDisk;
			}
		}

		return resolved;
	}

	private static IEnumerable<string> EnumerateWorkbenchDesktopDiskCandidates(string configuredPath)
	{
		var directory = Path.GetDirectoryName(configuredPath);
		if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
		{
			yield break;
		}

		var configuredName = Path.GetFileName(configuredPath);
		var exactCandidateName = configuredName
			.Replace("(Disk 1 of 6)", "(Disk 2 of 6)", StringComparison.OrdinalIgnoreCase)
			.Replace("(Install)", "(Workbench)", StringComparison.OrdinalIgnoreCase);
		if (!string.Equals(exactCandidateName, configuredName, StringComparison.OrdinalIgnoreCase))
		{
			var exactCandidate = Path.Combine(directory, exactCandidateName);
			if (File.Exists(exactCandidate))
			{
				yield return Path.GetFullPath(exactCandidate);
			}
		}

		var candidates = Directory.EnumerateFiles(directory)
			.Where(path =>
				IsLikelyWorkbenchDesktopDiskName(path) &&
				IsSameWorkbenchDiskSetCandidate(configuredName, Path.GetFileName(path)))
			.OrderByDescending(path => Path.GetFileName(path).Contains("Disk 2 of 6", StringComparison.OrdinalIgnoreCase))
			.ThenBy(path => path, StringComparer.OrdinalIgnoreCase);
		foreach (var candidate in candidates)
		{
			yield return Path.GetFullPath(candidate);
		}
	}

	private static void PopulateWorkbenchStartupExternalDisks(
		string?[] paths,
		string? configuredDiskPath,
		string baseDirectory,
		int profileFloppyDriveCount)
	{
		var bootDiskPath = paths[0];
		if (profileFloppyDriveCount <= 1 || string.IsNullOrWhiteSpace(bootDiskPath))
		{
			return;
		}

		var sourcePath = ResolveOptionalPath(configuredDiskPath, baseDirectory) ?? bootDiskPath;
		if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(bootDiskPath), StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		var driveIndex = 1;
		foreach (var candidate in EnumerateWorkbenchStartupExternalDiskCandidates(sourcePath, bootDiskPath))
		{
			if (driveIndex >= paths.Length || driveIndex >= profileFloppyDriveCount)
			{
				return;
			}

			paths[driveIndex++] = candidate;
		}
	}

	private static IEnumerable<string> EnumerateWorkbenchStartupExternalDiskCandidates(string sourcePath, string bootDiskPath)
	{
		var returned = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFullPath(bootDiskPath) };
		var sourceFullPath = Path.GetFullPath(sourcePath);
		if (Path.GetExtension(sourceFullPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
		{
			foreach (var archiveDiskPath in EnumerateWorkbenchStartupArchiveExternalDiskCandidates(sourceFullPath, bootDiskPath))
			{
				if (returned.Add(archiveDiskPath))
				{
					yield return archiveDiskPath;
				}
			}
		}
		else if (!returned.Contains(sourceFullPath) &&
			File.Exists(sourceFullPath) &&
			IsLikelyWorkbenchDiskSetName(sourceFullPath))
		{
			returned.Add(sourceFullPath);
			yield return sourceFullPath;
		}

		var directory = Path.GetDirectoryName(sourceFullPath);
		if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
		{
			yield break;
		}

		var sourceNumber = TryReadWorkbenchDiskNumber(Path.GetFileName(sourceFullPath), out var parsedNumber)
			? parsedNumber
			: int.MaxValue;
		var candidates = Directory.EnumerateFiles(directory)
			.Where(path =>
				IsLikelyWorkbenchDiskSetName(path) &&
				IsSameWorkbenchDiskSetCandidate(Path.GetFileName(sourceFullPath), Path.GetFileName(path)) &&
				!returned.Contains(Path.GetFullPath(path)))
			.Select(path => new
			{
				Path = Path.GetFullPath(path),
				DiskNumber = TryReadWorkbenchDiskNumber(Path.GetFileName(path), out var diskNumber)
					? diskNumber
					: int.MaxValue
			})
			.OrderBy(candidate => candidate.DiskNumber == sourceNumber ? 0 : 1)
			.ThenBy(candidate => candidate.DiskNumber)
			.ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase);
		foreach (var candidate in candidates)
		{
			returned.Add(candidate.Path);
			yield return candidate.Path;
		}
	}

	private static IEnumerable<string> EnumerateWorkbenchStartupArchiveExternalDiskCandidates(string archivePath, string bootDiskPath)
	{
		if (!File.Exists(archivePath))
		{
			yield break;
		}

		List<WorkbenchArchiveDiskCandidate> candidates;
		try
		{
			using var archive = ZipFile.OpenRead(archivePath);
			candidates = archive.Entries
				.Where(entry =>
					!string.IsNullOrWhiteSpace(entry.Name) &&
					(entry.Name.EndsWith(".adf", StringComparison.OrdinalIgnoreCase) ||
						entry.Name.EndsWith(".adz", StringComparison.OrdinalIgnoreCase)) &&
					IsLikelyWorkbenchDiskSetName(entry.Name) &&
					IsSameWorkbenchDiskSetCandidate(Path.GetFileName(archivePath), entry.Name))
				.Select(entry => new WorkbenchArchiveDiskCandidate(
					entry.FullName,
					TryReadWorkbenchDiskNumber(entry.Name, out var diskNumber) ? diskNumber : int.MaxValue))
				.OrderBy(candidate => candidate.DiskNumber)
				.ThenBy(candidate => candidate.FullName, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or AmigaEmulationException or ArgumentException or InvalidOperationException)
		{
			yield break;
		}

		foreach (var candidate in candidates)
		{
			string? cachedPath;
			try
			{
				using var archive = ZipFile.OpenRead(archivePath);
				var entry = archive.GetEntry(candidate.FullName);
				var adfBytes = entry == null ? null : ReadArchiveAdfBytes(entry);
				cachedPath = adfBytes == null
					? null
					: WriteArchiveDiskCacheFile(archivePath, entry!, adfBytes);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or AmigaEmulationException or ArgumentException or InvalidOperationException)
			{
				continue;
			}

			if (cachedPath != null &&
				!string.Equals(Path.GetFullPath(cachedPath), Path.GetFullPath(bootDiskPath), StringComparison.OrdinalIgnoreCase))
			{
				yield return cachedPath;
			}
		}
	}

	private static bool IsLikelyWorkbenchDesktopDiskName(string path)
	{
		var name = Path.GetFileName(path);
		var extension = Path.GetExtension(name);
		return (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
				extension.Equals(".adf", StringComparison.OrdinalIgnoreCase) ||
				extension.Equals(".adz", StringComparison.OrdinalIgnoreCase)) &&
			name.Contains("Workbench", StringComparison.OrdinalIgnoreCase) &&
			!name.Contains("Install", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsLikelyWorkbenchDiskSetName(string path)
	{
		var name = Path.GetFileName(path);
		var extension = Path.GetExtension(name);
		return (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
				extension.Equals(".adf", StringComparison.OrdinalIgnoreCase) ||
				extension.Equals(".adz", StringComparison.OrdinalIgnoreCase)) &&
			name.Contains("Workbench", StringComparison.OrdinalIgnoreCase);
	}

	private static bool TryReadWorkbenchDiskNumber(string fileName, out int diskNumber)
	{
		diskNumber = 0;
		var match = Regex.Match(fileName, @"Disk\s*(?<number>\d+)\s*of\s*\d+", RegexOptions.IgnoreCase);
		return match.Success && int.TryParse(match.Groups["number"].Value, out diskNumber);
	}

	private static bool IsSameWorkbenchDiskSetCandidate(string configuredName, string candidateName)
	{
		if (!TryReadWorkbenchDiskSetIdentity(configuredName, out var configuredPrefix, out var configuredTotal))
		{
			return true;
		}

		return TryReadWorkbenchDiskSetIdentity(candidateName, out var candidatePrefix, out var candidateTotal) &&
			configuredTotal == candidateTotal &&
			string.Equals(configuredPrefix, candidatePrefix, StringComparison.OrdinalIgnoreCase);
	}

	private static bool TryReadWorkbenchDiskSetIdentity(string fileName, out string prefix, out int total)
	{
		prefix = string.Empty;
		total = 0;
		var match = Regex.Match(fileName, @"Disk\s*\d+\s*of\s*(?<total>\d+)", RegexOptions.IgnoreCase);
		if (!match.Success || !int.TryParse(match.Groups["total"].Value, out total))
		{
			return false;
		}

		prefix = fileName[..match.Index].Trim();
		return true;
	}

	private static bool ContainsWorkbenchDesktop(string path)
	{
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
		{
			return false;
		}

		try
		{
			var disk = AmigaDiskImage.Load(path);
			if (!AmigaDosFileSystem.IsSupported(disk))
			{
				return false;
			}

			var fileSystem = new AmigaDosFileSystem(disk);
			return fileSystem.TryFindEntry("System/Workbench", out var entry) && entry.IsFile;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AmigaEmulationException or ArgumentException or InvalidOperationException)
		{
			return false;
		}
	}

	private static bool TryExtractWorkbenchDesktopDiskFromArchive(string path, out string diskPath)
	{
		diskPath = string.Empty;
		if (!Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
			!File.Exists(path))
		{
			return false;
		}

		try
		{
			using var archive = ZipFile.OpenRead(path);
			var entries = archive.Entries
				.Where(entry =>
					!string.IsNullOrWhiteSpace(entry.Name) &&
					(entry.Name.EndsWith(".adf", StringComparison.OrdinalIgnoreCase) ||
						entry.Name.EndsWith(".adz", StringComparison.OrdinalIgnoreCase)))
				.OrderByDescending(entry => entry.Name.Contains("Disk 2 of 6", StringComparison.OrdinalIgnoreCase))
				.ThenByDescending(entry => entry.Name.Contains("Workbench", StringComparison.OrdinalIgnoreCase))
				.ThenBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase);
			foreach (var entry in entries)
			{
				var adfBytes = ReadArchiveAdfBytes(entry);
				if (adfBytes == null || !ContainsWorkbenchDesktop(adfBytes))
				{
					continue;
				}

				diskPath = WriteArchiveDiskCacheFile(path, entry, adfBytes);
				return true;
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or AmigaEmulationException or ArgumentException or InvalidOperationException)
		{
			return false;
		}

		return false;
	}

	private static byte[]? ReadArchiveAdfBytes(ZipArchiveEntry entry)
	{
		using var entryStream = entry.Open();
		using var memory = new MemoryStream();
		if (entry.Name.EndsWith(".adz", StringComparison.OrdinalIgnoreCase))
		{
			using var gzip = new GZipStream(entryStream, CompressionMode.Decompress);
			gzip.CopyTo(memory);
		}
		else
		{
			entryStream.CopyTo(memory);
		}

		var data = memory.ToArray();
		return data.Length == AmigaDiskImage.StandardAdfSize ? data : null;
	}

	private static bool ContainsWorkbenchDesktop(byte[] adfBytes)
	{
		try
		{
			var disk = AmigaDiskImage.FromAdfBytes(adfBytes.ToArray(), "workbench.adf");
			if (!AmigaDosFileSystem.IsSupported(disk))
			{
				return false;
			}

			var fileSystem = new AmigaDosFileSystem(disk);
			return fileSystem.TryFindEntry("System/Workbench", out var entry) && entry.IsFile;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AmigaEmulationException or ArgumentException or InvalidOperationException)
		{
			return false;
		}
	}

	private static string WriteArchiveDiskCacheFile(string archivePath, ZipArchiveEntry entry, byte[] adfBytes)
	{
		var cacheDirectory = Path.Combine(Path.GetTempPath(), "CopperScreen", "Workbench31ArchiveCache");
		Directory.CreateDirectory(cacheDirectory);
		var archiveInfo = new FileInfo(archivePath);
		var identity = string.Join(
			"|",
			Path.GetFullPath(archivePath),
			archiveInfo.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture),
			entry.FullName,
			entry.Length.ToString(CultureInfo.InvariantCulture),
			entry.CompressedLength.ToString(CultureInfo.InvariantCulture));
		var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).Substring(0, 16);
		var fileName = SanitizeFileName(Path.GetFileNameWithoutExtension(entry.Name));
		if (string.IsNullOrWhiteSpace(fileName))
		{
			fileName = "workbench31";
		}

		var outputPath = Path.Combine(cacheDirectory, $"{fileName}-{hash}.adf");
		if (!File.Exists(outputPath) || new FileInfo(outputPath).Length != adfBytes.Length)
		{
			File.WriteAllBytes(outputPath, adfBytes);
		}

		return outputPath;
	}

	private static string SanitizeFileName(string name)
	{
		var invalid = Path.GetInvalidFileNameChars();
		var builder = new StringBuilder(name.Length);
		foreach (var character in name)
		{
			builder.Append(invalid.Contains(character) ? '_' : character);
		}

		return builder.ToString().Trim();
	}

	private readonly record struct WorkbenchArchiveDiskCandidate(string FullName, int DiskNumber);

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
		return resolved != null && File.Exists(resolved) ? resolved : null;
	}

	private static string? ResolveOptionalPath(string? path, string baseDirectory)
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
