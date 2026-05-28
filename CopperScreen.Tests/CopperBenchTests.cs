using System.IO.Compression;
using System.Text;
using CopperMod.Amiga;
using CopperScreen;

namespace CopperScreen.Tests;

public sealed class CopperBenchTests
{
	[Fact]
	public void NoDiskStillRendersInsertDiskWhileToolbarStateExists()
	{
		var emulator = CopperScreenEmulator.CreateWithoutDisk();
		var bench = new CopperBenchViewModel(emulator);

		emulator.RenderNextFrame();

		Assert.Equal("insert disk image", emulator.StatusText);
		Assert.True(bench.IsToolbarVisible);
		Assert.False(bench.IsOverlayVisible);
		Assert.True(emulator.Framebuffer.Distinct().Count() > 3);
	}

	[Fact]
	public void CopperBenchOverlayTogglesAndRefreshesFromViewModel()
	{
		var emulator = CopperScreenEmulator.CreateWithoutDisk();
		var bench = new CopperBenchViewModel(emulator);

		bench.ToggleOverlay();

		Assert.True(bench.IsOverlayVisible);
		Assert.Equal("No disk image", bench.StatusMessage);

		bench.HideOverlay();

		Assert.False(bench.IsOverlayVisible);
	}

	[Fact]
	public void CopperBenchListsZippedAdfDrawersAndProjects()
	{
		using var temp = new TemporaryDiskSet();
		var diskPath = temp.WriteZip("CopperBench Test (Disk 1 of 2).zip", CreateWorkbenchDiskBytes());
		var emulator = CopperScreenEmulator.Create(new[] { diskPath }, AppContext.BaseDirectory);
		var bench = new CopperBenchViewModel(emulator);

		bench.ToggleOverlay();

		Assert.Contains(bench.Entries, entry => entry.Name == "C" && entry.Kind == CopperBenchEntryKind.Drawer);
		Assert.Contains(bench.Entries, entry => entry.Name == "Project" && entry.Kind == CopperBenchEntryKind.Project);
	}

	[Fact]
	public void CopperBenchSelectedDetailsIncludePreviewMetadata()
	{
		using var temp = new TemporaryDiskSet();
		var diskPath = temp.WriteZip("CopperBench Test (Disk 1 of 1).zip", CreateWorkbenchDiskBytes());
		var emulator = CopperScreenEmulator.Create(new[] { diskPath }, AppContext.BaseDirectory);
		var bench = new CopperBenchViewModel(emulator);
		bench.ShowOverlay();
		bench.SelectIndex(bench.Entries.ToList().FindIndex(entry => entry.Name == "Project"));

		var details = bench.SelectedDetails;

		Assert.Contains("Project", details);
		Assert.Contains("Path: DF0:Project", details);
		Assert.Contains("default tool C/Tool", details);
		Assert.Contains("STACK=4096", details);
	}

	[Fact]
	public void CopperBenchLaunchesWorkbenchProjectThroughEmulator()
	{
		using var temp = new TemporaryDiskSet();
		var diskPath = temp.WriteZip("CopperBench Test (Disk 1 of 1).zip", CreateWorkbenchDiskBytes());
		var emulator = CopperScreenEmulator.Create(new[] { diskPath }, AppContext.BaseDirectory);
		var bench = new CopperBenchViewModel(emulator);
		bench.ToggleOverlay();
		bench.SelectIndex(bench.Entries.ToList().FindIndex(entry => entry.Name == "Project"));

		Assert.True(bench.LaunchSelected(), bench.StatusMessage);

		Assert.False(bench.IsOverlayVisible);
		Assert.StartsWith("CopperBench launched C/Tool", emulator.StatusText);
	}

	[Fact]
	public void WorkbenchStyleBootStopsAtCopperBenchUntilUserLaunchesProject()
	{
		using var temp = new TemporaryDiskSet();
		var diskPath = temp.WriteZip("CopperBench Boot (Disk 1 of 1).zip", CreateBootableWorkbenchDiskBytes());
		var emulator = CopperScreenEmulator.Create(new[] { diskPath }, AppContext.BaseDirectory);
		var bench = new CopperBenchViewModel(emulator);

		emulator.RenderNextFrame();

		Assert.True(emulator.IsWorkbenchHandoffPending);
		Assert.True(emulator.IsPaused);
		Assert.True(emulator.ConsumeCopperBenchRequest());
		Assert.Equal("Workbench handoff: choose a CopperBench item", emulator.StatusText);
		bench.ShowOverlay();
		Assert.True(bench.IsOverlayVisible);
		var projectIndex = bench.Entries.ToList().FindIndex(entry => entry.Name == "Project");
		Assert.True(projectIndex >= 0);
		bench.SelectIndex(projectIndex);

		Assert.True(bench.LaunchSelected(), bench.StatusMessage);

		Assert.False(bench.IsOverlayVisible);
		Assert.False(emulator.IsPaused);
		Assert.False(emulator.IsWorkbenchHandoffPending);
		Assert.StartsWith("CopperBench launched C/Tool", emulator.StatusText);
	}

	[Theory]
	[InlineData("Hired Guns v1.08.39.25 (1993-09-24)(Psygnosis)(M5)(Disk 1 of 5).zip")]
	[InlineData("Hired Guns v1.08.39.25 (1993-09-24)(Psygnosis)(M5)(Disk 1 of 5)[cr Loons][f ATX].zip")]
	public void CopperBenchBrowsesHiredGunsRootWhenAvailable(string fileName)
	{
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", fileName);
		if (diskPath == null)
		{
			return;
		}

		var emulator = CopperScreenEmulator.Create(new[] { diskPath }, AppContext.BaseDirectory);
		var bench = new CopperBenchViewModel(emulator);

		bench.ShowOverlay();

		Assert.True(bench.Entries.Count > 5, bench.StatusMessage);
		Assert.Contains(bench.Entries, entry => entry.Name == "C" && entry.Kind == CopperBenchEntryKind.Drawer);
		Assert.Contains(bench.Entries, entry => entry.Name == "Hired Guns" && entry.Kind == CopperBenchEntryKind.Project);
		Assert.DoesNotContain("Empty drawer", bench.StatusMessage);
	}

	[Fact]
	public void CopperBenchPauseResetAndDiskNavigationActionsCallEmulator()
	{
		using var temp = new TemporaryDiskSet();
		var disk1 = temp.WriteZip("CopperBench Test (Disk 1 of 2).zip", CreateWorkbenchDiskBytes());
		var disk2 = temp.WriteZip("CopperBench Test (Disk 2 of 2).zip", CreateWorkbenchDiskBytes());
		var emulator = CopperScreenEmulator.Create(new[] { disk1 }, AppContext.BaseDirectory);
		var bench = new CopperBenchViewModel(emulator);

		bench.TogglePause();
		Assert.True(emulator.IsPaused);

		bench.Reset();
		Assert.Equal(Path.GetFileName(disk1), emulator.StatusText);

		bench.InsertNextDisk();
		Assert.Equal(Path.GetFullPath(disk2), emulator.DiskPath);
		Assert.StartsWith("Inserted ", bench.StatusMessage);

		bench.InsertPreviousDisk();
		Assert.Equal(Path.GetFullPath(disk1), emulator.DiskPath);
		Assert.StartsWith("Inserted ", bench.StatusMessage);
	}

	private static byte[] CreateWorkbenchDiskBytes()
	{
		var data = new byte[AmigaDiskImage.StandardAdfSize];
		data[0] = (byte)'D';
		data[1] = (byte)'O';
		data[2] = (byte)'S';
		WriteDirectoryHeader(data, 880, 0, "Workbench", 1);
		WriteDirectoryHeader(data, 10, 880, "C", 2);
		WriteFile(data, 11, 10, "Tool", CreateRtsHunk(), 100);
		WriteFile(data, 12, 880, "Project", Encoding.ASCII.GetBytes("project"), 101);
		WriteFile(data, 13, 880, "Project.info", CreateIconData("C/Tool", "STACK=4096", "FLAG=YES"), 102);
		return data;
	}

	private static byte[] CreateBootableWorkbenchDiskBytes()
	{
		var data = CreateWorkbenchDiskBytes();
		data[12] = 0x4E;
		data[13] = 0xF9;
		data[14] = 0x00;
		data[15] = 0x00;
		data[16] = 0x00;
		data[17] = 0x00;
		BigEndian.WriteUInt32(data, 4, CalculateBootChecksum(data.AsSpan(0, 1024)));
		return data;
	}

	private static byte[] CreateRtsHunk()
	{
		var data = new List<byte>();
		WriteLong(data, 0x0000_03F3);
		WriteLong(data, 0);
		WriteLong(data, 1);
		WriteLong(data, 0);
		WriteLong(data, 0);
		WriteLong(data, 1);
		WriteLong(data, 0x0000_03E9);
		WriteLong(data, 1);
		WriteWord(data, 0x4E75);
		WriteWord(data, 0);
		WriteLong(data, 0x0000_03F2);
		return data.ToArray();
	}

	private static byte[] CreateIconData(params string[] values)
	{
		var data = new List<byte>();
		foreach (var value in values)
		{
			var bytes = Encoding.ASCII.GetBytes(value);
			data.Add((byte)bytes.Length);
			data.AddRange(bytes);
			data.Add(0);
		}

		return data.ToArray();
	}

	private static void WriteDirectoryHeader(byte[] data, int block, int parentBlock, string name, int secondaryType)
	{
		var offset = block * AmigaDiskImage.SectorSize;
		BigEndian.WriteUInt32(data, offset, 2);
		WriteName(data, offset + 432, name);
		BigEndian.WriteUInt32(data, offset + 0x1F4, (uint)parentBlock);
		BigEndian.WriteUInt32(data, offset + 0x1FC, unchecked((uint)secondaryType));
	}

	private static void WriteFile(byte[] data, int block, int parentBlock, string name, byte[] content, int dataBlock)
	{
		WriteDirectoryHeader(data, block, parentBlock, name, -3);
		var headerOffset = block * AmigaDiskImage.SectorSize;
		BigEndian.WriteUInt32(data, headerOffset + 0x10, (uint)dataBlock);
		BigEndian.WriteUInt32(data, headerOffset + 0x144, (uint)content.Length);

		var dataOffset = dataBlock * AmigaDiskImage.SectorSize;
		BigEndian.WriteUInt32(data, dataOffset, 8);
		BigEndian.WriteUInt32(data, dataOffset + 12, (uint)content.Length);
		BigEndian.WriteUInt32(data, dataOffset + 16, 0);
		Array.Copy(content, 0, data, dataOffset + 24, content.Length);
	}

	private static void WriteName(byte[] data, int offset, string name)
	{
		var bytes = Encoding.ASCII.GetBytes(name);
		data[offset] = (byte)bytes.Length;
		Array.Copy(bytes, 0, data, offset + 1, bytes.Length);
	}

	private static void WriteLong(List<byte> data, uint value)
	{
		data.Add((byte)(value >> 24));
		data.Add((byte)(value >> 16));
		data.Add((byte)(value >> 8));
		data.Add((byte)value);
	}

	private static void WriteWord(List<byte> data, ushort value)
	{
		data.Add((byte)(value >> 8));
		data.Add((byte)value);
	}

	private static uint CalculateBootChecksum(ReadOnlySpan<byte> bootBlock)
	{
		var sum = 0u;
		for (var offset = 0; offset < 1024; offset += 4)
		{
			var value = BigEndian.ReadUInt32(bootBlock, offset, "boot block checksum word");
			var previous = sum;
			sum += value;
			if (sum < previous)
			{
				sum++;
			}
		}

		return ~sum;
	}

	private static string? TryFindWorkspaceFile(params string[] parts)
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory != null)
		{
			var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
			if (File.Exists(candidate))
			{
				return candidate;
			}

			directory = directory.Parent;
		}

		return null;
	}

	private sealed class TemporaryDiskSet : IDisposable
	{
		private readonly string _directory = Path.Combine(Path.GetTempPath(), "copperbench-" + Guid.NewGuid().ToString("N"));

		public TemporaryDiskSet()
		{
			Directory.CreateDirectory(_directory);
		}

		public string WriteZip(string fileName, byte[] adfData)
		{
			var path = Path.Combine(_directory, fileName);
			using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
			var entry = archive.CreateEntry(Path.ChangeExtension(fileName, ".adf"));
			using var stream = entry.Open();
			stream.Write(adfData, 0, adfData.Length);
			return path;
		}

		public void Dispose()
		{
			Directory.Delete(_directory, recursive: true);
		}
	}
}
