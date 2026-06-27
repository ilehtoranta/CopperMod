using System.Text;
using CopperMod.Amiga;

namespace CopperScreen;

internal sealed class CopperBenchViewModel
{
	private readonly CopperScreenEmulator? _emulator;
	private readonly object _emulatorSync;
	private readonly List<CopperBenchEntry> _entries = new List<CopperBenchEntry>();

	public CopperBenchViewModel()
	{
		_emulatorSync = new object();
		IsToolbarVisible = true;
		CurrentPath = string.Empty;
		StatusMessage = "CopperBench ready";
	}

	public CopperBenchViewModel(CopperScreenEmulator emulator, object? emulatorSync = null)
	{
		_emulator = emulator ?? throw new ArgumentNullException(nameof(emulator));
		_emulatorSync = emulatorSync ?? new object();
		IsToolbarVisible = true;
		CurrentPath = string.Empty;
		StatusMessage = "CopperBench ready";
	}

	public bool IsOverlayVisible { get; private set; }

	public bool IsToolbarVisible { get; private set; }

	public bool IsPaused
	{
		get
		{
			lock (_emulatorSync)
			{
				return _emulator?.IsPaused ?? false;
			}
		}
	}

	public string CurrentPath { get; private set; }

	public string DisplayPath => CurrentPath.Length == 0 ? "DF0:" : "DF0:" + CurrentPath;

	public IReadOnlyList<CopperBenchEntry> Entries => _entries;

	public int SelectedIndex { get; private set; } = -1;

	public CopperBenchEntry? SelectedEntry => SelectedIndex >= 0 && SelectedIndex < _entries.Count
		? _entries[SelectedIndex]
		: null;

	public string StatusMessage { get; private set; }

	public string SelectedDetails
	{
		get
		{
			var selected = SelectedEntry;
			if (selected == null)
			{
				return StatusMessage;
			}

			var builder = new StringBuilder();
			builder.AppendLine(selected.Name);
			builder.AppendLine(selected.Kind.ToString());
			builder.Append("Path: DF0:");
			builder.AppendLine(selected.Path);
			if (selected.Size > 0)
			{
				builder.Append("Size: ");
				builder.Append(selected.Size);
				builder.AppendLine(" bytes");
			}

			if (!string.IsNullOrWhiteSpace(selected.Details))
			{
				builder.AppendLine();
				builder.Append(selected.Details);
			}

			return builder.ToString();
		}
	}

	public void ToggleOverlay()
	{
		IsOverlayVisible = !IsOverlayVisible;
		if (IsOverlayVisible)
		{
			Refresh();
		}
	}

	public void ShowOverlay()
	{
		IsOverlayVisible = true;
		Refresh();
	}

	public void HideOverlay()
	{
		IsOverlayVisible = false;
	}

	public void ToggleToolbar()
	{
		IsToolbarVisible = !IsToolbarVisible;
	}

	public void TogglePause()
	{
		var emulator = RequireEmulator();
		lock (_emulatorSync)
		{
			emulator.TogglePaused();
			StatusMessage = emulator.IsPaused ? "Paused" : "Running";
		}
	}

	public void Reset()
	{
		var emulator = RequireEmulator();
		lock (_emulatorSync)
		{
			emulator.Reset();
		}

		Refresh();
		StatusMessage = "Reset";
	}

	public void PulseFire()
	{
		var emulator = RequireEmulator();
		lock (_emulatorSync)
		{
			emulator.PulsePrimaryFire();
		}

		StatusMessage = "Fire";
	}

	public void InsertDisk(string diskPath)
	{
		var emulator = RequireEmulator();
		var inserted = false;
		string diskName;
		string statusText;
		lock (_emulatorSync)
		{
			inserted = emulator.InsertDisk(diskPath);
			diskName = emulator.DiskName;
			statusText = emulator.StatusText;
		}

		if (inserted)
		{
			CurrentPath = string.Empty;
			Refresh();
			StatusMessage = "Inserted " + diskName;
		}
		else
		{
			Refresh();
			StatusMessage = statusText;
		}
	}

	public void InsertNextDisk()
	{
		var emulator = RequireEmulator();
		var inserted = false;
		string diskName;
		string statusText;
		lock (_emulatorSync)
		{
			inserted = emulator.InsertNextDisk();
			diskName = emulator.DiskName;
			statusText = emulator.StatusText;
		}

		if (inserted)
		{
			CurrentPath = string.Empty;
			Refresh();
			StatusMessage = "Inserted " + diskName;
		}
		else
		{
			Refresh();
			StatusMessage = statusText;
		}
	}

	public void InsertPreviousDisk()
	{
		var emulator = RequireEmulator();
		var inserted = false;
		string diskName;
		string statusText;
		lock (_emulatorSync)
		{
			inserted = emulator.InsertPreviousDisk();
			diskName = emulator.DiskName;
			statusText = emulator.StatusText;
		}

		if (inserted)
		{
			CurrentPath = string.Empty;
			Refresh();
			StatusMessage = "Inserted " + diskName;
		}
		else
		{
			Refresh();
			StatusMessage = statusText;
		}
	}

	public void Refresh()
	{
		var emulator = RequireEmulator();
		_entries.Clear();
		SelectedIndex = -1;
		string? diskPath;
		lock (_emulatorSync)
		{
			diskPath = emulator.DiskPath;
		}

		LoadEntries(diskPath, CurrentPath, _entries, out var status);
		StatusMessage = status;
	}

	public async Task RefreshAsync(string? diskPath)
	{
		var path = CurrentPath;
		var result = await Task.Run(() =>
		{
			var entries = new List<CopperBenchEntry>();
			LoadEntries(diskPath, path, entries, out var status);
			return (entries, status);
		}).ConfigureAwait(true);

		_entries.Clear();
		_entries.AddRange(result.entries);
		SelectedIndex = -1;
		StatusMessage = result.status;
	}

	public async Task ToggleOverlayAsync(string? diskPath)
	{
		IsOverlayVisible = !IsOverlayVisible;
		if (IsOverlayVisible)
		{
			await RefreshAsync(diskPath).ConfigureAwait(true);
		}
	}

	public async Task ShowOverlayAsync(string? diskPath)
	{
		IsOverlayVisible = true;
		await RefreshAsync(diskPath).ConfigureAwait(true);
	}

	public async Task GoUpAsync(string? diskPath)
	{
		if (CurrentPath.Length == 0)
		{
			return;
		}

		CurrentPath = AmigaDosFileSystem.GetDirectoryName(CurrentPath);
		await RefreshAsync(diskPath).ConfigureAwait(true);
	}

	public void SelectIndex(int index)
	{
		SelectedIndex = index >= 0 && index < _entries.Count ? index : -1;
	}

	public void GoUp()
	{
		if (CurrentPath.Length == 0)
		{
			return;
		}

		CurrentPath = AmigaDosFileSystem.GetDirectoryName(CurrentPath);
		Refresh();
	}

	public void ActivateSelected()
	{
		var selected = SelectedEntry;
		if (selected == null)
		{
			return;
		}

		if (selected.Kind == CopperBenchEntryKind.Drawer)
		{
			CurrentPath = selected.Path;
			Refresh();
			return;
		}

		LaunchSelected();
	}

	public bool LaunchSelected()
	{
		var emulator = RequireEmulator();
		var selected = SelectedEntry;
		if (selected == null)
		{
			StatusMessage = "No entry selected";
			return false;
		}

		if (selected.Kind == CopperBenchEntryKind.Drawer)
		{
			StatusMessage = "Open the drawer instead of launching it";
			return false;
		}

		string message;
		bool launched;
		lock (_emulatorSync)
		{
			launched = emulator.LaunchCopperBenchPath(selected.Path, out message);
		}

		if (!launched)
		{
			StatusMessage = message;
			return false;
		}

		StatusMessage = message;
		IsOverlayVisible = false;
		return true;
	}

	public async Task<bool> ActivateSelectedAsync(string? diskPath, Func<string, Task<CopperScreenCommandResult>> launchAsync)
	{
		var selected = SelectedEntry;
		if (selected == null)
		{
			StatusMessage = "No entry selected";
			return false;
		}

		if (selected.Kind == CopperBenchEntryKind.Drawer)
		{
			CurrentPath = selected.Path;
			await RefreshAsync(diskPath).ConfigureAwait(true);
			return true;
		}

		if (selected.Kind == CopperBenchEntryKind.File)
		{
			StatusMessage = "Select a tool or project";
			return false;
		}

		var result = await launchAsync(selected.Path).ConfigureAwait(true);
		StatusMessage = result.Message;
		if (result.Success)
		{
			IsOverlayVisible = false;
		}

		return result.Success;
	}

	public void SetStatusMessage(string message)
	{
		StatusMessage = message;
	}

	public void ResetPath()
	{
		CurrentPath = string.Empty;
	}

	private CopperScreenEmulator RequireEmulator()
		=> _emulator ?? throw new InvalidOperationException("This CopperBench view model is not attached to a direct emulator instance.");

	private static void LoadEntries(string? diskPath, string currentPath, List<CopperBenchEntry> entries, out string status)
	{
		entries.Clear();
		if (diskPath == null)
		{
			status = "No disk image";
			return;
		}

		try
		{
			var disk = CopperScreenDiskImageArchive.LoadDiskImage(diskPath);
			if (!AmigaDosFileSystem.IsSupported(disk))
			{
				status = "DF0: is not a supported OFS DOS\\0 disk";
				return;
			}

			var fileSystem = new AmigaDosFileSystem(disk);
			foreach (var entry in fileSystem.ListDirectory(currentPath))
			{
				if (entry.Name.EndsWith(".info", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				var path = AmigaDosFileSystem.CombinePath(currentPath, entry.Name);
				entries.Add(CreateEntry(fileSystem, entry, path));
			}

			status = entries.Count == 0 ? "Empty drawer" : $"{entries.Count} item(s)";
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AmigaEmulationException or ArgumentException or InvalidDataException)
		{
			status = ex.Message;
		}
	}

	private static CopperBenchEntry CreateEntry(AmigaDosFileSystem fileSystem, AmigaDosDirectoryEntry entry, string path)
	{
		if (entry.IsDirectory)
		{
			return new CopperBenchEntry(entry.Name, path, CopperBenchEntryKind.Drawer, entry.Size, "Drawer");
		}

		var kind = CopperBenchEntryKind.File;
		var details = $"{entry.Size} bytes";
		if (fileSystem.TryReadWorkbenchDiskObject(path, out var diskObject))
		{
			if (diskObject.HasDefaultTool)
			{
				kind = CopperBenchEntryKind.Project;
				details = $"Project, default tool {diskObject.DefaultToolPath}, stack {diskObject.StackSize}";
			}
			else
			{
				details = $"Icon metadata, stack {diskObject.StackSize}";
			}

			if (diskObject.ToolTypes.Count != 0)
			{
				details += Environment.NewLine + string.Join(Environment.NewLine, diskObject.ToolTypes);
			}
		}

		if (kind == CopperBenchEntryKind.File &&
			fileSystem.TryReadFile(path, out var data) &&
			AmigaHunkProgramLoader.HasHunkHeader(data))
		{
			kind = CopperBenchEntryKind.Tool;
			details = $"HUNK executable, {entry.Size} bytes";
		}

		return new CopperBenchEntry(entry.Name, path, kind, entry.Size, details);
	}
}

internal sealed class CopperBenchEntry
{
	public CopperBenchEntry(string name, string path, CopperBenchEntryKind kind, int size, string details)
	{
		Name = name;
		Path = path;
		Kind = kind;
		Size = size;
		Details = details;
	}

	public string Name { get; }

	public string Path { get; }

	public CopperBenchEntryKind Kind { get; }

	public int Size { get; }

	public string Details { get; }

	public override string ToString()
	{
		var prefix = Kind switch
		{
			CopperBenchEntryKind.Drawer => "[Drawer]",
			CopperBenchEntryKind.Project => "[Project]",
			CopperBenchEntryKind.Tool => "[Tool]",
			_ => "[File]"
		};
		return $"{prefix} {Name}";
	}
}

internal enum CopperBenchEntryKind
{
	Drawer,
	File,
	Tool,
	Project
}
