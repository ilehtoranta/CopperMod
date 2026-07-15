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
	public void LaunchRequestMapsAssignStylePrefixToBootDiskDrawer()
	{
		var fileSystem = new AmigaDosFileSystem(CreateWorkbenchDisk());

		Assert.True(fileSystem.TryCreateLaunchRequest("C:Tool", out var request, out var message), message);

		Assert.Equal("C/Tool", request.ExecutablePath);
		Assert.Null(request.ProjectPath);
		Assert.Equal("C", request.CurrentDirectory);
	}

	[Fact]
	public void LaunchRequestStripsMountedVolumeNameFromWorkbenchDefaultTool()
	{
		var fileSystem = new AmigaDosFileSystem(CreateWorkbenchDisk("Workbench Disk:C/Tool"));

		Assert.True(fileSystem.TryCreateLaunchRequest("Project", out var request, out var message), message);

		Assert.Equal("C/Tool", request.ExecutablePath);
		Assert.Equal("Project", request.ProjectPath);
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
	public void FastFileSystemReadsRawBlocksAcrossExtensionBlocks()
	{
		var fileSystem = new AmigaDosFileSystem(CreateFastFileSystemDisk());

		Assert.True(fileSystem.TryReadFile("DF0:Big", out var data));

		Assert.Equal((72 * AmigaDiskImage.SectorSize) + 7, data.Length);
		Assert.Equal(0x10, data[0]);
		Assert.Equal(0x11, data[AmigaDiskImage.SectorSize]);
		Assert.Equal(0x58, data[72 * AmigaDiskImage.SectorSize]);
		Assert.Equal(0x58, data[^1]);
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
		for (var i = 0; i < 8; i++)
		{
			machine.Cpu.State.D[i] = 0xD0D0_0000u + (uint)i;
			machine.Cpu.State.A[i] = 0xA0A0_0000u + (uint)i;
		}

		Assert.True(boot.TryLaunchProgram(request, out var result, out var launchMessage), launchMessage);

		Assert.Equal(result.EntryAddress, machine.Cpu.State.ProgramCounter);
		Assert.Equal((uint)result.StartupArguments.Length, machine.Cpu.State.D[0]);
		Assert.Equal(AmigaKickstartHost.ExecLibraryBase, machine.Cpu.State.A[6]);
		for (var register = 1; register < 8; register++)
		{
			Assert.Equal(0u, machine.Cpu.State.D[register]);
		}

		for (var register = 1; register < 6; register++)
		{
			Assert.Equal(0u, machine.Cpu.State.A[register]);
		}

		Assert.Equal("C/Tool", result.ExecutablePath);
		Assert.Equal(8192, result.StackSize);
		var startup = ReadCString(machine.Bus, machine.Cpu.State.A[0], 256);
		Assert.Contains("LANGUAGES ENGLISH", startup);
		Assert.Contains("CLOSEWB", startup);
		Assert.DoesNotContain("LANGUAGES ENGLISH,FRENCH", startup);
	}

	[Fact]
	public void BootControllerAutoRunStartupSequenceLaunchesLoadWbFromDiskWhenSystemWorkbenchExists()
	{
		var disk = CreateBootableWorkbenchStartupDisk();
		var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));
		var boot = new AmigaBootController(machine)
		{
			AutoRunStartupSequence = true,
			AutoStartWorkbenchDefaultTool = true
		};

		var result = boot.BootFromDisk(disk, maxInstructions: 20_000, AmigaBootRunMode.ContinueAfterBootDiskRead);

		var diagnostics = result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}").ToArray();
		Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Contains("AMIGA_BOOT_DOS_WORKBENCH_MEDIA_INCOMPLETE", StringComparison.Ordinal));
		Assert.Contains(result.Diagnostics, diagnostic =>
			diagnostic.Code == "AMIGA_BOOT_COPPERBENCH_LAUNCH" &&
			diagnostic.Message.Contains("C/LoadWB", StringComparison.OrdinalIgnoreCase));
		Assert.Contains(result.Diagnostics, diagnostic =>
			diagnostic.Code == "AMIGA_BOOT_DOS_AUTOSTART" &&
			diagnostic.Message.Contains("C:LoadWB", StringComparison.OrdinalIgnoreCase) &&
			!diagnostic.Message.Contains("System/Workbench", StringComparison.OrdinalIgnoreCase));
		Assert.DoesNotContain(result.Diagnostics, diagnostic =>
			diagnostic.Code.Contains("WORKBENCH", StringComparison.OrdinalIgnoreCase) ||
			diagnostic.Code.Contains("LOADWB_HOST", StringComparison.OrdinalIgnoreCase));
		Assert.Equal(0x00FF_FFFCu, result.FinalProgramCounter);
	}

	[Fact]
	public void BootControllerAutoRunStartupSequenceLaunchesLoadWbWhenSystemWorkbenchIsAbsent()
	{
		var disk = CreateBootableWorkbenchStartupDisk(includeSystemWorkbench: false);
		var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));
		var boot = new AmigaBootController(machine)
		{
			AutoRunStartupSequence = true,
			AutoStartWorkbenchDefaultTool = true
		};

		var result = boot.BootFromDisk(disk, maxInstructions: 20_000, AmigaBootRunMode.ContinueAfterBootDiskRead);

		var diagnostics = result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}").ToArray();
		Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Contains("AMIGA_BOOT_DOS_WORKBENCH_MEDIA_INCOMPLETE", StringComparison.Ordinal));
		Assert.Contains(result.Diagnostics, diagnostic =>
			diagnostic.Code == "AMIGA_BOOT_COPPERBENCH_LAUNCH" &&
			diagnostic.Message.Contains("C/LoadWB", StringComparison.OrdinalIgnoreCase));
		Assert.Contains(result.Diagnostics, diagnostic =>
			diagnostic.Code == "AMIGA_BOOT_DOS_AUTOSTART" &&
			diagnostic.Message.Contains("C:LoadWB", StringComparison.OrdinalIgnoreCase) &&
			!diagnostic.Message.Contains("System/Workbench", StringComparison.OrdinalIgnoreCase));
		Assert.DoesNotContain(result.Diagnostics, diagnostic =>
			diagnostic.Code.Contains("WORKBENCH", StringComparison.OrdinalIgnoreCase) ||
			diagnostic.Code.Contains("LOADWB_HOST", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void BootControllerAutoRunStartupSequenceDoesNotAttachWorkbenchAfterNativeLoadWbOpensWorkbenchLibrary()
	{
		var disk = CreateBootableWorkbenchStartupDisk(
			includeSystemWorkbench: false,
			"C:LoadWB\r\nEndCLI >NIL:\r\n",
			CreateOpenWorkbenchLibraryHunk());
		var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));
		var boot = new AmigaBootController(machine)
		{
			AutoRunStartupSequence = true,
			AutoStartWorkbenchDefaultTool = true
		};

		var result = boot.BootFromDisk(disk, maxInstructions: 20_000, AmigaBootRunMode.ContinueAfterBootDiskRead);

		var diagnostics = result.Diagnostics.ToArray();
		var launchIndex = Array.FindIndex(diagnostics, diagnostic =>
			diagnostic.Code == "AMIGA_BOOT_COPPERBENCH_LAUNCH" &&
			diagnostic.Message.Contains("C/LoadWB", StringComparison.OrdinalIgnoreCase));
		var openIndex = Array.FindIndex(diagnostics, diagnostic =>
			diagnostic.Code == "AMIGA_BOOT_OPEN_LIBRARY" &&
			diagnostic.Message.Contains("workbench.library", StringComparison.OrdinalIgnoreCase));
		Assert.True(launchIndex >= 0, string.Join(Environment.NewLine, diagnostics.Select(FormatDiagnostic)));
		Assert.True(openIndex > launchIndex, string.Join(Environment.NewLine, diagnostics.Select(FormatDiagnostic)));
		Assert.DoesNotContain(diagnostics, diagnostic =>
			diagnostic.Code.Contains("WORKBENCH", StringComparison.OrdinalIgnoreCase) ||
			diagnostic.Code.Contains("LOADWB_HOST", StringComparison.OrdinalIgnoreCase));
		Assert.Equal(0x00FF_FFFCu, result.FinalProgramCounter);
	}

	[Fact]
	public void BootControllerAutoRunStartupSequenceContinuesNativeLoadWbAfterReadArgs()
	{
		var disk = CreateBootableWorkbenchStartupDisk(
			includeSystemWorkbench: false,
			"C:LoadWB\r\nEndCLI >NIL:\r\n",
			CreateLoadWorkbenchReadArgsHunk());
		var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));
		var boot = new AmigaBootController(machine)
		{
			AutoRunStartupSequence = true,
			AutoStartWorkbenchDefaultTool = true
		};

		var result = boot.BootFromDisk(disk, maxInstructions: 20_000, AmigaBootRunMode.ContinueAfterBootDiskRead);

		var diagnostics = result.Diagnostics.ToArray();
		var dosOpenIndex = Array.FindIndex(diagnostics, diagnostic =>
			diagnostic.Code == "AMIGA_BOOT_OPEN_LIBRARY" &&
			diagnostic.Message.Contains("dos.library", StringComparison.OrdinalIgnoreCase));
		var readArgsIndex = Array.FindIndex(diagnostics, diagnostic => diagnostic.Code == "AMIGA_BOOT_DOS_READ_ARGS");
		var workbenchOpenIndex = Array.FindIndex(diagnostics, diagnostic =>
			diagnostic.Code == "AMIGA_BOOT_OPEN_LIBRARY" &&
			diagnostic.Message.Contains("workbench.library", StringComparison.OrdinalIgnoreCase));
		Assert.True(dosOpenIndex >= 0, string.Join(Environment.NewLine, diagnostics.Select(FormatDiagnostic)));
		Assert.True(readArgsIndex > dosOpenIndex, string.Join(Environment.NewLine, diagnostics.Select(FormatDiagnostic)));
		Assert.True(workbenchOpenIndex > readArgsIndex, string.Join(Environment.NewLine, diagnostics.Select(FormatDiagnostic)));
		Assert.DoesNotContain(diagnostics, diagnostic =>
			diagnostic.Code.Contains("WORKBENCH", StringComparison.OrdinalIgnoreCase) ||
			diagnostic.Code.Contains("LOADWB_HOST", StringComparison.OrdinalIgnoreCase));
		Assert.Equal(0x00FF_FFFCu, result.FinalProgramCounter);
	}

	[Fact]
	public void BootControllerLeavesNativeLoadWbParkedAtReturnSentinelAcrossExecutionSlices()
	{
		var disk = CreateBootableWorkbenchStartupDisk(
			includeSystemWorkbench: false,
			"C:LoadWB\r\nEndCLI >NIL:\r\n",
			CreateLoadWorkbenchReadArgsHunk());
		var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));
		var boot = new AmigaBootController(machine)
		{
			AutoRunStartupSequence = true,
			AutoStartWorkbenchDefaultTool = true
		};

		var result = boot.BootFromDisk(disk, maxInstructions: 20_000, AmigaBootRunMode.ContinueAfterBootDiskRead);
		var parked = boot.ContinueExecution(maxInstructions: 128);

		Assert.Equal(0x00FF_FFFCu, result.FinalProgramCounter);
		Assert.Equal(0, parked.InstructionsExecuted);
		Assert.True(parked.CompletedBootBlock);
		Assert.Equal(0x00FF_FFFCu, parked.FinalProgramCounter);
		Assert.DoesNotContain(parked.Diagnostics, diagnostic =>
			diagnostic.Code is "AMIGA_BOOT_UNSUPPORTED_OPCODE" or "AMIGA_BOOT_FAULT");
	}

	[Fact]
	public void BootControllerAutoRunStartupSequenceModelsWorkbenchSetupCommands()
	{
		var disk = CreateBootableWorkbenchStartupDisk(
			includeSystemWorkbench: false,
			"""
			C:SetPatch >NIL:
			C:Version >NIL:
			C:AddBuffers >NIL: DF0: 15
			FailAt 21
			C:MakeDir RAM:T
			C:Copy ENVARC: RAM:ENV QUIET ALL NOREQ
			C:Assign >NIL: ENV: RAM:ENV
			C:Assign >NIL: T: RAM:T
			C:BindDrivers
			C:LoadWB
			EndCLI >NIL:
			""");
		var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));
		var boot = new AmigaBootController(machine)
		{
			AutoRunStartupSequence = true,
			AutoStartWorkbenchDefaultTool = true
		};

		var result = boot.BootFromDisk(disk, maxInstructions: 20_000, AmigaBootRunMode.ContinueAfterBootDiskRead);

		var diagnostics = result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}").ToArray();
		Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Contains("AMIGA_BOOT_DOS_STARTUP_SKIP", StringComparison.Ordinal));
		Assert.Contains(result.Diagnostics, diagnostic =>
			diagnostic.Code == "AMIGA_BOOT_DOS_STARTUP_HOST" &&
			diagnostic.Message.Contains("SetPatch", StringComparison.OrdinalIgnoreCase) &&
			diagnostic.Message.Contains("68040.library", StringComparison.OrdinalIgnoreCase));
		Assert.Contains(result.Diagnostics, diagnostic =>
			diagnostic.Code == "AMIGA_BOOT_DOS_STARTUP_HOST" &&
			diagnostic.Message.Contains("Copy ENVARC", StringComparison.OrdinalIgnoreCase));
		Assert.Contains(result.Diagnostics, diagnostic =>
			diagnostic.Code == "AMIGA_BOOT_DOS_STARTUP_HOST" &&
			diagnostic.Message.Contains("Assign", StringComparison.OrdinalIgnoreCase));
		Assert.Contains(result.Diagnostics, diagnostic =>
			diagnostic.Code == "AMIGA_BOOT_COPPERBENCH_LAUNCH" &&
			diagnostic.Message.Contains("C/LoadWB", StringComparison.OrdinalIgnoreCase));
		Assert.DoesNotContain(result.Diagnostics, diagnostic =>
			diagnostic.Code.Contains("WORKBENCH", StringComparison.OrdinalIgnoreCase) ||
			diagnostic.Code.Contains("LOADWB_HOST", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void BootControllerAutoRunStartupSequenceFindsSetPatch68040LibraryOnExternalInstallDisk()
	{
		var bootDisk = CreateBootableWorkbenchStartupDisk(
			includeSystemWorkbench: false,
			"""
			C:SetPatch >NIL:
			C:LoadWB
			EndCLI >NIL:
			""",
			CreateRtsHunk(),
			includeM68040Library: false);
		var installDisk = CreateWorkbenchInstallSupportDisk();
		var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));
		var boot = new AmigaBootController(machine)
		{
			AutoRunStartupSequence = true,
			AutoStartWorkbenchDefaultTool = true
		};

		boot.StartBootFromDisk(bootDisk);
		boot.Drive1.Insert(installDisk);
		var result = boot.ContinueExecution(maxInstructions: 20_000);

		var diagnostics = result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}").ToArray();
		Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Contains("AMIGA_BOOT_DOS_STARTUP_SKIP", StringComparison.Ordinal));
		Assert.Contains(result.Diagnostics, diagnostic =>
			diagnostic.Code == "AMIGA_BOOT_DOS_STARTUP_HOST" &&
			diagnostic.Message.Contains("SetPatch", StringComparison.OrdinalIgnoreCase) &&
			diagnostic.Message.Contains("68040.library", StringComparison.OrdinalIgnoreCase));
		Assert.Contains(result.Diagnostics, diagnostic =>
			diagnostic.Code == "AMIGA_BOOT_COPPERBENCH_LAUNCH" &&
			diagnostic.Message.Contains("C/LoadWB", StringComparison.OrdinalIgnoreCase));
		Assert.DoesNotContain(result.Diagnostics, diagnostic =>
			diagnostic.Code.Contains("WORKBENCH", StringComparison.OrdinalIgnoreCase) ||
			diagnostic.Code.Contains("LOADWB_HOST", StringComparison.OrdinalIgnoreCase));
	}

	private static AmigaDiskImage CreateWorkbenchDisk(string defaultTool = "C/Tool")
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
			CreateIconData(defaultTool, "STACK=8192", "0LANGUAGES=ENGLISH,FRENCH", "CLOSEWB=YES"),
			102);
		return AmigaDiskImage.FromAdfBytes(data, "workbench.adf");
	}

	private static AmigaDiskImage CreateBootableWorkbenchStartupDisk()
		=> CreateBootableWorkbenchStartupDisk(includeSystemWorkbench: true);

	private static AmigaDiskImage CreateBootableWorkbenchStartupDisk(bool includeSystemWorkbench)
		=> CreateBootableWorkbenchStartupDisk(includeSystemWorkbench, "C:LoadWB\r\n");

	private static AmigaDiskImage CreateBootableWorkbenchStartupDisk(bool includeSystemWorkbench, string startupSequence)
		=> CreateBootableWorkbenchStartupDisk(includeSystemWorkbench, startupSequence, CreateRtsHunk());

	private static AmigaDiskImage CreateBootableWorkbenchStartupDisk(
		bool includeSystemWorkbench,
		string startupSequence,
		byte[] loadWorkbenchExecutable,
		bool includeM68040Library = true)
	{
		var data = new byte[AmigaDiskImage.StandardAdfSize];
		data[0] = (byte)'D';
		data[1] = (byte)'O';
		data[2] = (byte)'S';
		data[12] = 0x4E;
		data[13] = 0xF9;
		data[14] = 0x00;
		data[15] = 0x00;
		data[16] = 0x00;
		data[17] = 0x00;
		WriteDirectoryHeader(data, 880, 0, "Workbench", 1);
		WriteDirectoryHeader(data, 10, 880, "C", 2);
		WriteDirectoryHeader(data, 20, 880, "S", 2);
		WriteDirectoryHeader(data, 30, 880, "System", 2);
		WriteDirectoryHeader(data, 40, 880, "Libs", 2);
		WriteDirectoryHeader(data, 50, 880, "Prefs", 2);
		WriteDirectoryHeader(data, 51, 50, "Env-Archive", 2);
		WriteFile(data, 11, 10, "LoadWB", loadWorkbenchExecutable, 100);
		WriteFile(data, 21, 20, "Startup-Sequence", Encoding.ASCII.GetBytes(startupSequence), 101);
		if (includeM68040Library)
		{
			WriteFile(data, 41, 40, "68040.library", CreateRtsHunk(), 103);
		}

		if (includeSystemWorkbench)
		{
			WriteFile(data, 31, 30, "Workbench", CreateRtsHunk(), 102);
		}

		BigEndian.WriteUInt32(data, 4, CalculateBootChecksum(data.AsSpan(0, 1024)));
		return AmigaDiskImage.FromAdfBytes(data, "workbench-startup.adf");
	}

	private static AmigaDiskImage CreateWorkbenchInstallSupportDisk()
	{
		var data = new byte[AmigaDiskImage.StandardAdfSize];
		data[0] = (byte)'D';
		data[1] = (byte)'O';
		data[2] = (byte)'S';
		WriteDirectoryHeader(data, 880, 0, "Install3.1", 1);
		WriteDirectoryHeader(data, 40, 880, "Libs", 2);
		WriteFile(data, 41, 40, "68040.library", CreateRtsHunk(), 103);
		return AmigaDiskImage.FromAdfBytes(data, "install-support.adf");
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

	private static AmigaDiskImage CreateFastFileSystemDisk()
	{
		var data = new byte[AmigaDiskImage.StandardAdfSize];
		data[0] = (byte)'D';
		data[1] = (byte)'O';
		data[2] = (byte)'S';
		data[3] = 1;
		WriteDirectoryHeader(data, 880, 0, "FFS", 1);
		var content = new byte[(72 * AmigaDiskImage.SectorSize) + 7];
		for (var i = 0; i < content.Length; i++)
		{
			content[i] = (byte)(0x10 + (i / AmigaDiskImage.SectorSize));
		}

		WriteFastFile(data, 20, 880, "Big", content, 100, 200);
		return AmigaDiskImage.FromAdfBytes(data, "ffs.adf");
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

	private static byte[] CreateOpenWorkbenchLibraryHunk()
	{
		var code = new List<byte>();
		WriteWord(code, 0x43FA); // LEA workbench.library(PC),A1
		WriteWord(code, 0x0010);
		WriteWord(code, 0x7000); // MOVEQ #0,D0
		WriteWord(code, 0x4EB9); // JSR OpenLibrary(A6) host LVO
		WriteLong(code, AmigaKickstartHost.ExecLibraryBase - 552);
		WriteWord(code, 0x2C40); // MOVEA.L D0,A6
		WriteWord(code, 0x4EAE); // JSR first workbench.library LVO
		WriteWord(code, 0xFFFA);
		WriteWord(code, 0x4E75); // RTS
		code.AddRange(Encoding.ASCII.GetBytes("workbench.library"));
		code.Add(0);
		while ((code.Count & 3) != 0)
		{
			code.Add(0);
		}

		var data = new List<byte>();
		WriteLong(data, 0x0000_03F3);
		WriteLong(data, 0);
		WriteLong(data, 1);
		WriteLong(data, 0);
		WriteLong(data, 0);
		WriteLong(data, (uint)(code.Count / 4));
		WriteLong(data, 0x0000_03E9);
		WriteLong(data, (uint)(code.Count / 4));
		data.AddRange(code);
		WriteLong(data, 0x0000_03F2);
		return data.ToArray();
	}

	private static byte[] CreateLoadWorkbenchReadArgsHunk()
	{
		var code = new List<byte>();
		var dosNamePatch = EmitLeaPcRelative(code, 0x43FA); // LEA dos.library(PC),A1
		WriteWord(code, 0x7000); // MOVEQ #0,D0
		WriteWord(code, 0x4EB9); // JSR OpenLibrary(A6) host LVO
		WriteLong(code, AmigaKickstartHost.ExecLibraryBase - 552);
		WriteWord(code, 0x2C40); // MOVEA.L D0,A6
		var templatePatch = EmitLeaPcRelative(code, 0x41FA); // LEA template(PC),A0
		WriteWord(code, 0x2208); // MOVE.L A0,D1
		var arrayPatch = EmitLeaPcRelative(code, 0x41FA); // LEA arg array(PC),A0
		WriteWord(code, 0x2408); // MOVE.L A0,D2
		WriteWord(code, 0x7600); // MOVEQ #0,D3
		WriteWord(code, 0x4EAE); // JSR ReadArgs(A6)
		WriteWord(code, 0xFCE2);
		WriteWord(code, 0x4A80); // TST.L D0
		WriteWord(code, 0x6700); // BEQ.W fail
		var failPatch = code.Count;
		WriteWord(code, 0);
		WriteWord(code, 0x2C7C); // MOVEA.L #ExecBase,A6
		WriteLong(code, AmigaKickstartHost.ExecLibraryBase);
		var workbenchNamePatch = EmitLeaPcRelative(code, 0x43FA); // LEA workbench.library(PC),A1
		WriteWord(code, 0x7000); // MOVEQ #0,D0
		WriteWord(code, 0x4EB9); // JSR OpenLibrary(A6) host LVO
		WriteLong(code, AmigaKickstartHost.ExecLibraryBase - 552);
		WriteWord(code, 0x2C40); // MOVEA.L D0,A6
		WriteWord(code, 0x4EAE); // JSR first workbench.library LVO
		WriteWord(code, 0xFFFA);
		WriteWord(code, 0x4E75); // RTS
		var failOffset = code.Count;
		WriteWord(code, 0x4E75); // RTS

		var dosNameOffset = code.Count;
		code.AddRange(Encoding.ASCII.GetBytes("dos.library"));
		code.Add(0);
		var templateOffset = code.Count;
		code.AddRange(Encoding.ASCII.GetBytes("DELAY/S,CLEANUP/S,DEBUG/S"));
		code.Add(0);
		while ((code.Count & 3) != 0)
		{
			code.Add(0);
		}

		var arrayOffset = code.Count;
		for (var i = 0; i < 3; i++)
		{
			WriteLong(code, 0);
		}

		var workbenchNameOffset = code.Count;
		code.AddRange(Encoding.ASCII.GetBytes("workbench.library"));
		code.Add(0);
		while ((code.Count & 3) != 0)
		{
			code.Add(0);
		}

		PatchPcRelativeWord(code, dosNamePatch, dosNameOffset);
		PatchPcRelativeWord(code, templatePatch, templateOffset);
		PatchPcRelativeWord(code, arrayPatch, arrayOffset);
		PatchPcRelativeWord(code, workbenchNamePatch, workbenchNameOffset);
		PatchWord(code, failPatch, checked((ushort)(failOffset - failPatch)));

		var data = new List<byte>();
		WriteLong(data, 0x0000_03F3);
		WriteLong(data, 0);
		WriteLong(data, 1);
		WriteLong(data, 0);
		WriteLong(data, 0);
		WriteLong(data, (uint)(code.Count / 4));
		WriteLong(data, 0x0000_03E9);
		WriteLong(data, (uint)(code.Count / 4));
		data.AddRange(code);
		WriteLong(data, 0x0000_03F2);
		return data.ToArray();
	}

	private static int EmitLeaPcRelative(List<byte> code, ushort opcode)
	{
		WriteWord(code, opcode);
		var patchOffset = code.Count;
		WriteWord(code, 0);
		return patchOffset;
	}

	private static void PatchPcRelativeWord(List<byte> code, int patchOffset, int targetOffset)
	{
		var displacement = targetOffset - patchOffset;
		PatchWord(code, patchOffset, unchecked((ushort)checked((short)displacement)));
	}

	private static void PatchWord(List<byte> code, int offset, ushort value)
	{
		code[offset] = (byte)(value >> 8);
		code[offset + 1] = (byte)value;
	}

	private static string FormatDiagnostic(AmigaBootDiagnostic diagnostic)
		=> $"{diagnostic.Code}: {diagnostic.Message}";

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

	private static void WriteFastFile(
		byte[] data,
		int block,
		int parentBlock,
		string name,
		byte[] content,
		int firstDataBlock,
		int extensionBlock)
	{
		WriteDirectoryHeader(data, block, parentBlock, name, -3);
		var blockNumbers = new List<int>();
		for (var offset = 0; offset < content.Length; offset += AmigaDiskImage.SectorSize)
		{
			var dataBlock = firstDataBlock + blockNumbers.Count;
			blockNumbers.Add(dataBlock);
			Array.Copy(
				content,
				offset,
				data,
				dataBlock * AmigaDiskImage.SectorSize,
				Math.Min(AmigaDiskImage.SectorSize, content.Length - offset));
		}

		var headerOffset = block * AmigaDiskImage.SectorSize;
		BigEndian.WriteUInt32(data, headerOffset + 0x08, (uint)Math.Min(72, blockNumbers.Count));
		BigEndian.WriteUInt32(data, headerOffset + 0x10, (uint)firstDataBlock);
		BigEndian.WriteUInt32(data, headerOffset + 0x144, (uint)content.Length);
		WriteFastFileBlockTable(data, headerOffset, blockNumbers.Take(72).ToArray());
		if (blockNumbers.Count <= 72)
		{
			return;
		}

		BigEndian.WriteUInt32(data, headerOffset + 0x1F8, (uint)extensionBlock);
		var extensionOffset = extensionBlock * AmigaDiskImage.SectorSize;
		BigEndian.WriteUInt32(data, extensionOffset, 16);
		BigEndian.WriteUInt32(data, extensionOffset + 4, (uint)extensionBlock);
		BigEndian.WriteUInt32(data, extensionOffset + 8, (uint)(blockNumbers.Count - 72));
		BigEndian.WriteUInt32(data, extensionOffset + 0x1F4, (uint)block);
		BigEndian.WriteUInt32(data, extensionOffset + 0x1FC, unchecked((uint)-3));
		WriteFastFileBlockTable(data, extensionOffset, blockNumbers.Skip(72).ToArray());
	}

	private static void WriteFastFileBlockTable(byte[] data, int offset, IReadOnlyList<int> blockNumbers)
	{
		for (var i = 0; i < blockNumbers.Count; i++)
		{
			BigEndian.WriteUInt32(data, offset + 24 + ((71 - i) * 4), (uint)blockNumbers[i]);
		}
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
