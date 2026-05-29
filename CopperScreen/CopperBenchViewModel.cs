using System.Text;
using CopperMod.Amiga;

namespace CopperScreen;

internal sealed class CopperBenchViewModel
{
	private readonly CopperScreenEmulator _emulator;
	private readonly object _emulatorSync;
	private readonly List<CopperBenchEntry> _entries = new List<CopperBenchEntry>();

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
				return _emulator.IsPaused;
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
		lock (_emulatorSync)
		{
			_emulator.TogglePaused();
			StatusMessage = _emulator.IsPaused ? "Paused" : "Running";
		}
	}

	public void Reset()
	{
		lock (_emulatorSync)
		{
			_emulator.Reset();
		}

		Refresh();
		StatusMessage = "Reset";
	}

	public void PulseFire()
	{
		lock (_emulatorSync)
		{
			_emulator.PulsePrimaryFire();
		}

		StatusMessage = "Fire";
	}

	public void InsertDisk(string diskPath)
	{
		var inserted = false;
		string diskName;
		string statusText;
		lock (_emulatorSync)
		{
			inserted = _emulator.InsertDisk(diskPath);
			diskName = _emulator.DiskName;
			statusText = _emulator.StatusText;
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
		var inserted = false;
		string diskName;
		string statusText;
		lock (_emulatorSync)
		{
			inserted = _emulator.InsertNextDisk();
			diskName = _emulator.DiskName;
			statusText = _emulator.StatusText;
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
		var inserted = false;
		string diskName;
		string statusText;
		lock (_emulatorSync)
		{
			inserted = _emulator.InsertPreviousDisk();
			diskName = _emulator.DiskName;
			statusText = _emulator.StatusText;
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
		_entries.Clear();
		SelectedIndex = -1;
		string? diskPath;
		lock (_emulatorSync)
		{
			diskPath = _emulator.DiskPath;
		}

		if (diskPath == null)
		{
			StatusMessage = "No disk image";
			return;
		}

		try
		{
			var disk = AmigaDiskImage.Load(diskPath);
			if (!AmigaDosFileSystem.IsSupported(disk))
			{
				StatusMessage = "DF0: is not a supported OFS DOS\\0 disk";
				return;
			}

			var fileSystem = new AmigaDosFileSystem(disk);
			foreach (var entry in fileSystem.ListDirectory(CurrentPath))
			{
				if (entry.Name.EndsWith(".info", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				var path = AmigaDosFileSystem.CombinePath(CurrentPath, entry.Name);
				_entries.Add(CreateEntry(fileSystem, entry, path));
			}

			StatusMessage = _entries.Count == 0 ? "Empty drawer" : $"{_entries.Count} item(s)";
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AmigaEmulationException or ArgumentException)
		{
			StatusMessage = ex.Message;
		}
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
			launched = _emulator.LaunchCopperBenchPath(selected.Path, out message);
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
