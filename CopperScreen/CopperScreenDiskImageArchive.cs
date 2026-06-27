using System.IO.Compression;
using System.Text.RegularExpressions;
using CopperMod.Amiga;

namespace CopperScreen;

internal sealed record CopperScreenArchiveDiskEntry(
	string ArchivePath,
	string EntryName,
	long Length,
	int? DiskNumber,
	int? DiskTotal,
	bool LooksBootable)
{
	public string DisplayName => Path.GetFileName(EntryName);

	public string ReferencePath => CopperScreenDiskImageArchive.CreateEntryPath(ArchivePath, EntryName);

	public override string ToString()
	{
		var diskNumber = DiskNumber.HasValue
			? DiskTotal.HasValue
				? $"Disk {DiskNumber.Value} of {DiskTotal.Value}"
				: $"Disk {DiskNumber.Value}"
			: "Disk image";
		return LooksBootable ? $"{diskNumber} - {DisplayName} - bootable" : $"{diskNumber} - {DisplayName}";
	}
}

internal sealed record CopperScreenArchiveDiskSet(string ArchivePath, IReadOnlyList<CopperScreenArchiveDiskEntry> Entries)
{
	public CopperScreenDriveDiskAssignment[] CreateDefaultAssignments()
	{
		var ordered = CopperScreenDiskImageArchive.OrderEntriesForDefaultAssignment(Entries);
		var assignments = new CopperScreenDriveDiskAssignment[4];
		for (var driveIndex = 0; driveIndex < assignments.Length; driveIndex++)
		{
			assignments[driveIndex] = new CopperScreenDriveDiskAssignment(driveIndex, driveIndex < ordered.Count ? ordered[driveIndex].ReferencePath : null);
		}

		return assignments;
	}
}

internal readonly record struct CopperScreenDriveDiskAssignment(int DriveIndex, string? DiskPath);

internal static class CopperScreenDiskImageArchive
{
	private const string ZipEntrySeparator = "#/";
	private static readonly string[] SupportedDiskEntryExtensions = [".adf", ".adz", ".dms", ".ipf", ".scp"];

	public static bool TryReadDiskSet(string path, out CopperScreenArchiveDiskSet? diskSet, out string? error)
	{
		diskSet = null;
		error = null;
		if (!Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		var archivePath = Path.GetFullPath(path);
		try
		{
			using var archive = ZipFile.OpenRead(archivePath);
			var entries = new List<CopperScreenArchiveDiskEntry>();
			foreach (var entry in archive.Entries)
			{
				if (string.IsNullOrEmpty(entry.Name) || !IsSupportedDiskEntryName(entry.Name))
				{
					continue;
				}

				var diskNumber = TryParseDiskNumber(entry.FullName, out var number, out var total) ? number : (int?)null;
				entries.Add(new CopperScreenArchiveDiskEntry(
					archivePath,
					NormalizeEntryName(entry.FullName),
					entry.Length,
					diskNumber,
					total,
					LooksBootable(entry)));
			}

			diskSet = new CopperScreenArchiveDiskSet(
				archivePath,
				entries.OrderBy(entry => entry.EntryName, StringComparer.OrdinalIgnoreCase).ToArray());
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
		{
			error = ex.Message;
			return true;
		}
	}

	public static bool DiskPathExists(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}

		if (!TrySplitEntryPath(path, out var archivePath, out var entryName))
		{
			return File.Exists(path);
		}

		if (!File.Exists(archivePath))
		{
			return false;
		}

		try
		{
			using var archive = ZipFile.OpenRead(archivePath);
			return archive.GetEntry(entryName) != null;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
		{
			return false;
		}
	}

	public static AmigaDiskImage LoadDiskImage(string path)
	{
		if (!TrySplitEntryPath(path, out var archivePath, out var entryName))
		{
			return AmigaDiskImage.Load(path);
		}

		return LoadArchiveEntry(archivePath, entryName);
	}

	public static string CreateEntryPath(string archivePath, string entryName)
		=> Path.GetFullPath(archivePath) + ZipEntrySeparator + NormalizeEntryName(entryName);

	public static string NormalizeDiskPath(string path)
	{
		if (!TrySplitEntryPath(path, out var archivePath, out var entryName))
		{
			return Path.GetFullPath(path);
		}

		return CreateEntryPath(archivePath, entryName);
	}

	public static string GetDisplayName(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return "No disk";
		}

		return TrySplitEntryPath(path, out _, out var entryName)
			? Path.GetFileName(entryName)
			: Path.GetFileName(path);
	}

	public static bool TrySplitEntryPath(string path, out string archivePath, out string entryName)
	{
		if (!TrySplitEntryPathRaw(path, out var rawArchivePath, out entryName))
		{
			archivePath = string.Empty;
			return false;
		}

		archivePath = Path.GetFullPath(rawArchivePath);
		return true;
	}

	public static bool TrySplitEntryPathRaw(string path, out string archivePath, out string entryName)
	{
		archivePath = string.Empty;
		entryName = string.Empty;
		var separatorIndex = path.IndexOf(ZipEntrySeparator, StringComparison.Ordinal);
		if (separatorIndex <= 0)
		{
			return false;
		}

		var candidateArchivePath = path[..separatorIndex];
		if (!Path.GetExtension(candidateArchivePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		var candidateEntryName = path[(separatorIndex + ZipEntrySeparator.Length)..];
		if (string.IsNullOrWhiteSpace(candidateEntryName))
		{
			return false;
		}

		archivePath = candidateArchivePath;
		entryName = NormalizeEntryName(candidateEntryName);
		return true;
	}

	public static string? ResolveAdjacentEntryPath(string currentDiskPath, int delta)
	{
		if (!TrySplitEntryPath(currentDiskPath, out var archivePath, out var entryName))
		{
			return null;
		}

		var fileName = Path.GetFileName(entryName);
		if (!TryParseDiskNumber(fileName, out var number, out var total))
		{
			return null;
		}

		var target = number + delta;
		if (target < 1 || (total.HasValue && target > total.Value))
		{
			return null;
		}

		try
		{
			using var archive = ZipFile.OpenRead(archivePath);
			var matches = new List<string>();
			foreach (var entry in archive.Entries)
			{
				if (string.IsNullOrEmpty(entry.Name) ||
					!IsSupportedDiskEntryName(entry.Name) ||
					!TryParseDiskNumber(entry.FullName, out var entryNumber, out var entryTotal) ||
					entryNumber != target ||
					(total.HasValue && entryTotal.HasValue && entryTotal.Value != total.Value))
				{
					continue;
				}

				matches.Add(NormalizeEntryName(entry.FullName));
			}

			return matches.Count == 1 ? CreateEntryPath(archivePath, matches[0]) : null;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
		{
			return null;
		}
	}

	internal static IReadOnlyList<CopperScreenArchiveDiskEntry> OrderEntriesForDefaultAssignment(
		IReadOnlyList<CopperScreenArchiveDiskEntry> entries)
	{
		if (entries.Count == 0)
		{
			return Array.Empty<CopperScreenArchiveDiskEntry>();
		}

		var bootEntry =
			entries.FirstOrDefault(entry => entry.LooksBootable && entry.DiskNumber is null or 1) ??
			entries.FirstOrDefault(entry => entry.DiskNumber == 1) ??
			entries.FirstOrDefault(entry => entry.LooksBootable) ??
			entries[0];
		var ordered = new List<CopperScreenArchiveDiskEntry> { bootEntry };
		ordered.AddRange(entries
			.Where(entry => !ReferenceEquals(entry, bootEntry))
			.OrderBy(entry => entry.DiskNumber ?? int.MaxValue)
			.ThenBy(entry => entry.EntryName, StringComparer.OrdinalIgnoreCase));
		return ordered;
	}

	private static AmigaDiskImage LoadArchiveEntry(string archivePath, string entryName)
	{
		using var archive = ZipFile.OpenRead(archivePath);
		var entry = archive.GetEntry(entryName)
			?? throw new AmigaEmulationException($"ZIP archive does not contain '{entryName}'.");
		if (!IsSupportedDiskEntryName(entry.Name))
		{
			throw new AmigaEmulationException("ZIP archive entry is not a supported disk image.");
		}

		var tempPath = Path.Combine(Path.GetTempPath(), "copperscreen-" + Guid.NewGuid().ToString("N") + Path.GetExtension(entry.Name));
		try
		{
			entry.ExtractToFile(tempPath);
			return AmigaDiskImage.Load(tempPath);
		}
		finally
		{
			try
			{
				File.Delete(tempPath);
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}
		}
	}

	private static bool IsSupportedDiskEntryName(string name)
		=> SupportedDiskEntryExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);

	private static bool LooksBootable(ZipArchiveEntry entry)
	{
		if (!TryReadBootBlock(entry, out var bootBlock))
		{
			return false;
		}

		return bootBlock.Length >= 1024 &&
			bootBlock[0] == (byte)'D' &&
			bootBlock[1] == (byte)'O' &&
			bootBlock[2] == (byte)'S' &&
			HasValidBootBlockChecksum(bootBlock);
	}

	private static bool TryReadBootBlock(ZipArchiveEntry entry, out byte[] bootBlock)
	{
		bootBlock = Array.Empty<byte>();
		var extension = Path.GetExtension(entry.Name);
		try
		{
			using var input = entry.Open();
			Stream source = input;
			if (extension.Equals(".adz", StringComparison.OrdinalIgnoreCase))
			{
				source = new GZipStream(input, CompressionMode.Decompress);
			}
			else if (!extension.Equals(".adf", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			using (source)
			{
				bootBlock = new byte[1024];
				var offset = 0;
				while (offset < bootBlock.Length)
				{
					var read = source.Read(bootBlock, offset, bootBlock.Length - offset);
					if (read == 0)
					{
						return false;
					}

					offset += read;
				}
			}

			return true;
		}
		catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
		{
			return false;
		}
	}

	private static bool HasValidBootBlockChecksum(ReadOnlySpan<byte> bootBlock)
	{
		var sum = 0u;
		for (var offset = 0; offset < 1024; offset += 4)
		{
			var value =
				((uint)bootBlock[offset] << 24) |
				((uint)bootBlock[offset + 1] << 16) |
				((uint)bootBlock[offset + 2] << 8) |
				bootBlock[offset + 3];
			var previous = sum;
			sum += value;
			if (sum < previous)
			{
				sum++;
			}
		}

		return sum == uint.MaxValue;
	}

	private static bool TryParseDiskNumber(string name, out int number, out int? total)
	{
		number = 0;
		total = null;
		var match = Regex.Match(
			name,
			@"(?:Disk|Disc|Dsk)\s*(?<number>\d+)(?:\s*of\s*(?<total>\d+))?",
			RegexOptions.IgnoreCase);
		if (!match.Success || !int.TryParse(match.Groups["number"].Value, out number))
		{
			return false;
		}

		if (match.Groups["total"].Success && int.TryParse(match.Groups["total"].Value, out var parsedTotal))
		{
			total = parsedTotal;
		}

		return true;
	}

	private static string NormalizeEntryName(string entryName)
		=> entryName.Replace('\\', '/').TrimStart('/');
}
