using System.IO.Compression;
using CopperMod.Amiga;
using CopperScreen;

namespace CopperScreen.Tests;

public sealed class CopperScreenDiskImageArchiveTests
{
	[Fact]
	public void MultiDiskZipDefaultsBootableDiskOneToDf0()
	{
		var directory = Path.Combine(Path.GetTempPath(), "copperscreen-zip-disks-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		try
		{
			var zipPath = Path.Combine(directory, "Desert Dream (Kefrens).zip");
			WriteZip(
				zipPath,
				("Desert Dream (Kefrens) Disk 2 of 2.adf", new byte[AmigaDiskImage.StandardAdfSize]),
				("Desert Dream (Kefrens) Disk 1 of 2.adf", CreateBootableAdf()));

			var detected = CopperScreenDiskImageArchive.TryReadDiskSet(zipPath, out var diskSet, out var error);

			Assert.True(detected);
			Assert.Null(error);
			Assert.NotNull(diskSet);
			Assert.Equal(2, diskSet.Entries.Count);

			var assignments = diskSet.CreateDefaultAssignments();
			Assert.EndsWith("#/Desert Dream (Kefrens) Disk 1 of 2.adf", assignments[0].DiskPath, StringComparison.Ordinal);
			Assert.EndsWith("#/Desert Dream (Kefrens) Disk 2 of 2.adf", assignments[1].DiskPath, StringComparison.Ordinal);
			Assert.True(CopperScreenDiskImageArchive.DiskPathExists(assignments[0].DiskPath));
			Assert.Equal("Desert Dream (Kefrens) Disk 1 of 2.adf", CopperScreenDiskImageArchive.GetDisplayName(assignments[0].DiskPath));

			var loaded = CopperScreenDiskImageArchive.LoadDiskImage(assignments[0].DiskPath!);
			Assert.Equal(AmigaDiskImage.StandardAdfSize, loaded.Data.Length);
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Fact]
	public void StartupOptionsNormalizeRelativeArchiveEntryPaths()
	{
		var baseDirectory = Path.Combine(Path.GetTempPath(), "copperscreen-zip-profile-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Path.Combine(baseDirectory, "TestImages"));
		try
		{
			var zipPath = Path.Combine(baseDirectory, "TestImages", "bundle.zip");
			WriteZip(zipPath, ("Game Disk 1 of 2.adf", CreateBootableAdf()));
			var relativeReference = Path.Combine("TestImages", "bundle.zip") + "#/Game Disk 1 of 2.adf";
			var profile = CopperScreenProfile.LoadDefault(AppContext.BaseDirectory, out _);

			var options = CopperScreenStartupOptions.FromSettings(
				profile,
				[relativeReference, null, null, null],
				[null, null, null, null],
				profile.KickstartRomPath,
				null,
				profile.FloppyDriveAudio,
				profile.Input,
				baseDirectory);

			Assert.Equal(CopperScreenDiskImageArchive.CreateEntryPath(zipPath, "Game Disk 1 of 2.adf"), options.DiskPath);
			Assert.True(CopperScreenDiskImageArchive.DiskPathExists(options.DiskPath));
		}
		finally
		{
			Directory.Delete(baseDirectory, recursive: true);
		}
	}

	[Fact]
	public void StartupArgumentMultiDiskZipAutoAssignsArchiveEntries()
	{
		var directory = Path.Combine(Path.GetTempPath(), "copperscreen-zip-startup-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		try
		{
			var zipPath = Path.Combine(directory, "Desert Dream (Kefrens).zip");
			WriteZip(
				zipPath,
				("Desert Dream (Kefrens) B.adf", new byte[AmigaDiskImage.StandardAdfSize]),
				("Desert Dream (Kefrens) A.adf", CreateBootableAdf()));

			var options = CopperScreenStartupOptions.Parse(
				["--profile", "Profiles\\expanded-kickstart13.json", zipPath],
				AppContext.BaseDirectory);

			Assert.Null(options.Error);
			Assert.EndsWith("#/Desert Dream (Kefrens) A.adf", options.DiskPath, StringComparison.Ordinal);
			Assert.Equal(options.DiskPath, options.DriveDiskPaths[0]);
			Assert.EndsWith("#/Desert Dream (Kefrens) B.adf", options.DriveDiskPaths[1], StringComparison.Ordinal);
			Assert.True(CopperScreenDiskImageArchive.DiskPathExists(options.DriveDiskPaths[0]));
			Assert.True(CopperScreenDiskImageArchive.DiskPathExists(options.DriveDiskPaths[1]));
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	private static void WriteZip(string path, params (string EntryName, byte[] Data)[] entries)
	{
		using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
		foreach (var (entryName, data) in entries)
		{
			var entry = archive.CreateEntry(entryName);
			using var stream = entry.Open();
			stream.Write(data);
		}
	}

	private static byte[] CreateBootableAdf()
	{
		var data = new byte[AmigaDiskImage.StandardAdfSize];
		data[0] = (byte)'D';
		data[1] = (byte)'O';
		data[2] = (byte)'S';
		data[12] = 0x60;
		data[13] = 0xFE;
		WriteUInt32(data, 4, CalculateBootChecksum(data.AsSpan(0, 1024)));
		return data;
	}

	private static uint CalculateBootChecksum(ReadOnlySpan<byte> bootBlock)
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

		return ~sum;
	}

	private static void WriteUInt32(byte[] data, int offset, uint value)
	{
		data[offset] = (byte)(value >> 24);
		data[offset + 1] = (byte)(value >> 16);
		data[offset + 2] = (byte)(value >> 8);
		data[offset + 3] = (byte)value;
	}
}
