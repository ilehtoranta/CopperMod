using System.Text.RegularExpressions;

namespace CopperScreen;

// Host filename navigation, independent of the selected emulator.
internal static class CopperScreenDiskNavigation
{
	public static string? ResolveAdjacentDiskPath(string? currentDiskPath, int delta)
	{
		if (string.IsNullOrWhiteSpace(currentDiskPath))
		{
			return null;
		}

		var archiveAdjacentPath = CopperScreenDiskImageArchive.ResolveAdjacentEntryPath(currentDiskPath, delta);
		if (archiveAdjacentPath != null)
		{
			return archiveAdjacentPath;
		}

		var directory = Path.GetDirectoryName(currentDiskPath);
		var fileName = Path.GetFileName(currentDiskPath);
		if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
		{
			return null;
		}

		var match = MatchDiskNumber(fileName);
		if (!match.Success ||
			!int.TryParse(match.Groups["number"].Value, out var number) ||
			!int.TryParse(match.Groups["total"].Value, out var total))
		{
			return null;
		}

		var target = number + delta;
		if (target < 1 || target > total)
		{
			return null;
		}

		var numberText = match.Groups["number"].Value;
		var replacement = numberText.Length > 1
			? target.ToString().PadLeft(numberText.Length, '0')
			: target.ToString();
		var nextName = fileName.Remove(match.Groups["number"].Index, match.Groups["number"].Length)
			.Insert(match.Groups["number"].Index, replacement);
		var candidate = Path.Combine(directory, nextName);
		if (File.Exists(candidate))
		{
			return Path.GetFullPath(candidate);
		}

		return ResolveUniqueAdjacentDiskSibling(directory, fileName, match, target, total);
	}

	private static Match MatchDiskNumber(string fileName)
		=> Regex.Match(fileName, @"(?<prefix>Disk\s*)(?<number>\d+)(?<suffix>\s*of\s*(?<total>\d+))", RegexOptions.IgnoreCase);

	private static string? ResolveUniqueAdjacentDiskSibling(
		string directory,
		string fileName,
		Match sourceMatch,
		int target,
		int total)
	{
		if (!Directory.Exists(directory))
		{
			return null;
		}

		var sourceNumberPrefix = fileName[..sourceMatch.Groups["number"].Index];
		var extension = Path.GetExtension(fileName);
		var matches = new List<string>();
		foreach (var path in Directory.EnumerateFiles(directory))
		{
			var siblingName = Path.GetFileName(path);
			if (!string.Equals(Path.GetExtension(siblingName), extension, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var siblingMatch = MatchDiskNumber(siblingName);
			if (!siblingMatch.Success ||
				!int.TryParse(siblingMatch.Groups["number"].Value, out var siblingNumber) ||
				!int.TryParse(siblingMatch.Groups["total"].Value, out var siblingTotal) ||
				siblingNumber != target ||
				siblingTotal != total)
			{
				continue;
			}

			var siblingNumberPrefix = siblingName[..siblingMatch.Groups["number"].Index];
			if (!string.Equals(siblingNumberPrefix, sourceNumberPrefix, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			matches.Add(path);
		}

		return matches.Count == 1 ? Path.GetFullPath(matches[0]) : null;
	}

}
