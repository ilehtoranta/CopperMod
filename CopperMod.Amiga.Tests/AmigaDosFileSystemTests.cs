using System.Text;
using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class AmigaDosFileSystemTests
{
	[Fact]
	public void DirectoryListingAndNestedReadsUseAmigaDosPaths()
	{
		var fileSystem = new AmigaDosFileSystem(CreateWorkbenchDisk());

		var root = fileSystem.ListDirectory("");
		var cDrawer = Assert.Single(root.Where(entry => entry.Name == "C"));
		var project = Assert.Single(root.Where(entry => entry.Name == "Project"));
		var tools = fileSystem.ListDirectory("C");

		Assert.True(cDrawer.IsDirectory);
		Assert.True(project.IsFile);
		Assert.Contains(tools, entry => entry.Name == "Tool" && entry.IsFile);
		Assert.True(fileSystem.TryReadFile("DF0:C/Tool", out var executable));
		Assert.True(AmigaHunkProgramLoader.HasHunkHeader(executable));
	}

	[Fact]
	public void WorkbenchDiskObjectParsesDefaultToolToolTypesAndStack()
	{
		var fileSystem = new AmigaDosFileSystem(CreateWorkbenchDisk());

		Assert.True(fileSystem.TryReadWorkbenchDiskObject("Project", out var diskObject));

		Assert.Equal("Project", diskObject.Label);
		Assert.Equal("C/Tool", diskObject.DefaultToolPath);
		Assert.Equal(8192, diskObject.StackSize);
		Assert.Contains("STACK=8192", diskObject.ToolTypes);
		Assert.Contains("0LANGUAGES=ENGLISH,FRENCH", diskObject.ToolTypes);
	}

	[Fact]
	public void LaunchRequestUsesProjectDefaultToolAndCurrentDirectory()
	{
		var fileSystem = new AmigaDosFileSystem(CreateWorkbenchDisk());

		Assert.True(fileSystem.TryCreateLaunchRequest("Project", out var request, out var message), message);

		Assert.Equal("C/Tool", request.ExecutablePath);
		Assert.Equal("Project", request.ProjectPath);
		Assert.Equal(string.Empty, request.CurrentDirectory);
		Assert.Equal(8192, request.StackSize);
		Assert.Contains("CLOSEWB=YES", request.ToolTypes);
	}

	[Fact]
	public void DirectoryListingSupportsLogicalHeaderKeysAndByteStyleFileType()
	{
		var fileSystem = new AmigaDosFileSystem(CreateLogicalKeyDisk());

		var root = fileSystem.ListDirectory("");
		var cDrawer = Assert.Single(root.Where(entry => entry.Name == "C"));
		var tools = fileSystem.ListDirectory("C");

		Assert.True(cDrawer.IsDirectory);
		var tool = Assert.Single(tools.Where(entry => entry.Name == "Tool"));
		Assert.True(tool.IsFile);
		Assert.True(fileSystem.TryReadFile("DF0:C/Tool", out var data));
		Assert.Equal(new byte[] { 0x4E, 0x75 }, data);
	}

	[Fact]
	public void BootControllerLaunchProgramSetsStartupRegisters()
	{
		var disk = CreateWorkbenchDisk();
		var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));
		var boot = new AmigaBootController(machine);
		var fileSystem = new AmigaDosFileSystem(disk);
		Assert.True(fileSystem.TryCreateLaunchRequest("Project", out var request, out var requestMessage), requestMessage);

		boot.StartWorkbenchSession(disk);
		Assert.True(boot.TryLaunchProgram(request, out var result, out var launchMessage), launchMessage);

		Assert.Equal(result.EntryAddress, machine.Cpu.State.ProgramCounter);
		Assert.Equal((uint)result.StartupArguments.Length, machine.Cpu.State.D[0]);
		Assert.Equal(AmigaKickstartHost.ExecLibraryBase, machine.Cpu.State.A[6]);
		Assert.Equal("C/Tool", result.ExecutablePath);
		Assert.Equal(8192, result.StackSize);
		var startup = ReadCString(machine.Bus, machine.Cpu.State.A[0], 256);
		Assert.Contains("LANGUAGES ENGLISH", startup);
		Assert.Contains("CLOSEWB", startup);
		Assert.DoesNotContain("LANGUAGES ENGLISH,FRENCH", startup);
	}

	private static AmigaDiskImage CreateWorkbenchDisk()
	{
		var data = new byte[AmigaDiskImage.StandardAdfSize];
		data[0] = (byte)'D';
		data[1] = (byte)'O';
		data[2] = (byte)'S';
		WriteDirectoryHeader(data, 880, 0, "Workbench", 1);
		WriteDirectoryHeader(data, 10, 880, "C", 2);
		WriteFile(data, 11, 10, "Tool", CreateRtsHunk(), 100);
		WriteFile(data, 12, 880, "Project", Encoding.ASCII.GetBytes("project"), 101);
		WriteFile(
			data,
			13,
			880,
			"Project.info",
			CreateIconData("C/Tool", "STACK=8192", "0LANGUAGES=ENGLISH,FRENCH", "CLOSEWB=YES"),
			102);
		return AmigaDiskImage.FromAdfBytes(data, "workbench.adf");
	}

	private static AmigaDiskImage CreateLogicalKeyDisk()
	{
		var data = new byte[AmigaDiskImage.StandardAdfSize];
		data[0] = (byte)'D';
		data[1] = (byte)'O';
		data[2] = (byte)'S';
		WriteDirectoryHeader(data, 880, 0, "Logical Keys", 1);
		BigEndian.WriteUInt32(data, (880 * AmigaDiskImage.SectorSize) + 24, 100);
		WriteDirectoryHeader(data, 20, 77, "C", 2, headerKey: 100);
		WriteDirectoryHeader(data, 21, 100, "Tool", -3, headerKey: 101, rawSecondaryType: 0x000000FD);
		var fileHeaderOffset = 21 * AmigaDiskImage.SectorSize;
		BigEndian.WriteUInt32(data, fileHeaderOffset + 0x10, 30);
		BigEndian.WriteUInt32(data, fileHeaderOffset + 0x144, 2);
		var dataOffset = 30 * AmigaDiskImage.SectorSize;
		BigEndian.WriteUInt32(data, dataOffset, 8);
		BigEndian.WriteUInt32(data, dataOffset + 12, 2);
		BigEndian.WriteUInt32(data, dataOffset + 16, 0);
		data[dataOffset + 24] = 0x4E;
		data[dataOffset + 25] = 0x75;
		return AmigaDiskImage.FromAdfBytes(data, "logical-keys.adf");
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

	private static void WriteDirectoryHeader(
		byte[] data,
		int block,
		int parentBlock,
		string name,
		int secondaryType,
		int headerKey = 0,
		uint? rawSecondaryType = null)
	{
		var offset = block * AmigaDiskImage.SectorSize;
		BigEndian.WriteUInt32(data, offset, 2);
		if (headerKey != 0)
		{
			BigEndian.WriteUInt32(data, offset + 4, (uint)headerKey);
		}

		WriteName(data, offset + 432, name);
		BigEndian.WriteUInt32(data, offset + 0x1F4, (uint)parentBlock);
		BigEndian.WriteUInt32(data, offset + 0x1FC, rawSecondaryType ?? unchecked((uint)secondaryType));
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

	private static string ReadCString(AmigaBus bus, uint address, int maxLength)
	{
		var builder = new StringBuilder();
		for (var i = 0; i < maxLength; i++)
		{
			var value = bus.ReadByte(address + (uint)i);
			if (value == 0)
			{
				break;
			}

			builder.Append((char)value);
		}

		return builder.ToString();
	}
}
