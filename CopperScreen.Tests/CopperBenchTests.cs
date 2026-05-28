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
	public void CopperBenchPauseResetAndNextDiskActionsCallEmulator()
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
