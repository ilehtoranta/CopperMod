namespace CopperMod;

internal static class ModuleFileNavigator
{
	private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

	public static string? ResolveNextFilePath(string? currentPath)
	{
		return ResolveAdjacentFilePath(currentPath, 1);
	}

	public static string? ResolvePreviousFilePath(string? currentPath)
	{
		return ResolveAdjacentFilePath(currentPath, -1);
	}

	private static string? ResolveAdjacentFilePath(string? currentPath, int delta)
	{
		if (string.IsNullOrWhiteSpace(currentPath))
		{
			return null;
		}

		var fullPath = Path.GetFullPath(currentPath);
		if (!File.Exists(fullPath))
		{
			return null;
		}

		var directoryPath = Path.GetDirectoryName(fullPath);
		if (string.IsNullOrWhiteSpace(directoryPath))
		{
			return null;
		}

		var entries = EnumerateSortedEntries(directoryPath);
		var index = FindEntryIndex(entries, fullPath);
		if (index >= 0)
		{
			var adjacent = delta > 0
				? FindFirstFileAfterEntry(entries, index)
				: FindLastFileBeforeEntry(entries, index);
			if (adjacent != null)
			{
				return adjacent;
			}
		}

		return delta > 0
			? FindFirstFileInNextFolder(directoryPath)
			: FindLastFileInPreviousFolder(directoryPath);
	}

	private static string? FindFirstFileInNextFolder(string directoryPath)
	{
		var directory = new DirectoryInfo(directoryPath);
		while (directory.Parent != null)
		{
			var siblings = EnumerateSortedEntries(directory.Parent.FullName);
			var index = FindEntryIndex(siblings, directory.FullName);
			var candidate = FindFirstFileAfterEntry(siblings, index);
			if (candidate != null)
			{
				return candidate;
			}

			directory = directory.Parent;
		}

		return null;
	}

	private static string? FindLastFileInPreviousFolder(string directoryPath)
	{
		var directory = new DirectoryInfo(directoryPath);
		while (directory.Parent != null)
		{
			var siblings = EnumerateSortedEntries(directory.Parent.FullName);
			var index = FindEntryIndex(siblings, directory.FullName);
			var candidate = FindLastFileBeforeEntry(siblings, index);
			if (candidate != null)
			{
				return candidate;
			}

			directory = directory.Parent;
		}

		return null;
	}

	private static string? FindFirstFileInTree(string directoryPath)
	{
		foreach (var entry in EnumerateSortedEntries(directoryPath))
		{
			if (!entry.IsDirectory)
			{
				return entry.Path;
			}

			var childFile = FindFirstFileInTree(entry.Path);
			if (childFile != null)
			{
				return childFile;
			}
		}

		return null;
	}

	private static string? FindLastFileInTree(string directoryPath)
	{
		var entries = EnumerateSortedEntries(directoryPath);
		for (var i = entries.Count - 1; i >= 0; i--)
		{
			var entry = entries[i];
			if (!entry.IsDirectory)
			{
				return entry.Path;
			}

			var childFile = FindLastFileInTree(entry.Path);
			if (childFile != null)
			{
				return childFile;
			}
		}

		return null;
	}

	private static string? FindFirstFileAfterEntry(IReadOnlyList<FileSystemEntry> entries, int index)
	{
		for (var i = index + 1; i < entries.Count; i++)
		{
			var entry = entries[i];
			if (!entry.IsDirectory)
			{
				return entry.Path;
			}

			var childFile = FindFirstFileInTree(entry.Path);
			if (childFile != null)
			{
				return childFile;
			}
		}

		return null;
	}

	private static string? FindLastFileBeforeEntry(IReadOnlyList<FileSystemEntry> entries, int index)
	{
		for (var i = index - 1; i >= 0; i--)
		{
			var entry = entries[i];
			if (!entry.IsDirectory)
			{
				return entry.Path;
			}

			var childFile = FindLastFileInTree(entry.Path);
			if (childFile != null)
			{
				return childFile;
			}
		}

		return null;
	}

	private static List<FileSystemEntry> EnumerateSortedEntries(string directoryPath)
	{
		try
		{
			return Directory.EnumerateFileSystemEntries(directoryPath)
				.Select(path => new FileSystemEntry(Path.GetFullPath(path), Directory.Exists(path)))
				.OrderBy(entry => Path.GetFileName(entry.Path), NameComparer)
				.ThenBy(entry => Path.GetFileName(entry.Path), StringComparer.Ordinal)
				.ToList();
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
		{
			return new List<FileSystemEntry>();
		}
	}

	private static int FindEntryIndex(IReadOnlyList<FileSystemEntry> entries, string path)
	{
		for (var i = 0; i < entries.Count; i++)
		{
			if (string.Equals(entries[i].Path, path, StringComparison.OrdinalIgnoreCase))
			{
				return i;
			}
		}

		return -1;
	}

	private readonly struct FileSystemEntry
	{
		public FileSystemEntry(string path, bool isDirectory)
		{
			Path = path;
			IsDirectory = isDirectory;
		}

		public string Path { get; }

		public bool IsDirectory { get; }
	}
}
