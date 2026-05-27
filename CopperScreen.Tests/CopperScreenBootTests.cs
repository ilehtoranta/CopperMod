using CopperMod.Amiga;
using CopperScreen;

namespace CopperScreen.Tests;

public sealed class CopperScreenBootTests
{
	[Fact]
	public void ZippedDiskOneResolvesToStandardBootableAdf()
	{
		var diskPath = FindWorkspaceFile("CopperScreen", "TestImages", "Full Contact (1991)(Team 17)(Disk 1 of 2).zip");
		var disk = AmigaDiskImage.Load(diskPath);

		Assert.Equal(AmigaDiskImage.StandardAdfSize, disk.Data.Length);
		Assert.Equal((byte)'D', disk.BootBlock[0]);
		Assert.Equal((byte)'O', disk.BootBlock[1]);
		Assert.Equal((byte)'S', disk.BootBlock[2]);
		Assert.True(AmigaBootController.HasBootableShape(disk.BootBlock));
	}

	[Fact]
	public void NoDiskRendersNonBlankInsertDiskFramebuffer()
	{
		var emulator = CopperScreenEmulator.CreateWithoutDisk();

		emulator.RenderNextFrame();

		Assert.True(emulator.Framebuffer.Distinct().Count() > 3);
		Assert.Equal("insert disk image", emulator.StatusText);
	}

	[Fact]
	public void DiskOneBootLoadsAndExecutesBootBlock()
	{
		var diskPath = FindWorkspaceFile("CopperScreen", "TestImages", "Full Contact (1991)(Team 17)(Disk 1 of 2).zip");
		var disk = AmigaDiskImage.Load(diskPath);
		var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));
		var boot = new AmigaBootController(machine);

		var result = boot.BootFromDisk(disk, maxInstructions: 25_000);

		Assert.Equal(AmigaBootController.BootBlockAddress, result.LoadedAddress);
		Assert.Equal(AmigaBootController.BootEntryAddress, result.EntryAddress);
		Assert.True(result.InstructionsExecuted > 0);
		Assert.True(result.CompletedBootBlock);
		Assert.Equal(0x33FC, BigEndian.ReadUInt16(machine.Bus.ChipRam, (int)AmigaBootController.BootEntryAddress, "boot entry opcode"));
		Assert.Equal((byte)'D', machine.Bus.ChipRam[0x6FFF4]);
		Assert.Equal((byte)'O', machine.Bus.ChipRam[0x6FFF5]);
		Assert.Equal((byte)'S', machine.Bus.ChipRam[0x6FFF6]);
		var fatalDiagnostics = result.Diagnostics
			.Where(diagnostic => diagnostic.Code is "AMIGA_BOOT_UNSUPPORTED_OPCODE" or "AMIGA_BOOT_FAULT" or "AMIGA_BOOT_OVERRUN")
			.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")
			.ToArray();
		Assert.True(fatalDiagnostics.Length == 0, string.Join(Environment.NewLine, fatalDiagnostics));
	}

	[Fact]
	public void DiskOneProtectedLoaderReportsUnsupportedInsteadOfRenderingGarbledFrame()
	{
		var diskPath = FindWorkspaceFile("CopperScreen", "TestImages", "Full Contact (1991)(Team 17)(Disk 1 of 2).zip");
		var disk = AmigaDiskImage.Load(diskPath);
		var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));
		var boot = new AmigaBootController(machine);

		var result = boot.BootFromDisk(disk, maxInstructions: 250_000, runMode: AmigaBootRunMode.ContinueAfterBootDiskRead);

		Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "AMIGA_BOOT_PROTECTED_DISK_UNSUPPORTED");
		Assert.True(machine.Cpu.State.Halted);
	}

	[Fact]
	public void NoArgumentDiskResolutionDoesNotAutoloadTestImages()
	{
		var resolved = CopperScreenEmulator.ResolveDiskPath(Array.Empty<string>(), AppContext.BaseDirectory);

		Assert.Null(resolved);
	}

	[Fact]
	public void ExplicitDiskPathResolutionUsesProvidedImage()
	{
		var diskPath = FindWorkspaceFile("CopperScreen", "TestImages", "Full Contact (1991)(Team 17)(Disk 1 of 2).zip");
		var resolved = CopperScreenEmulator.ResolveDiskPath(new[] { diskPath }, AppContext.BaseDirectory);

		Assert.Equal(Path.GetFullPath(diskPath), resolved);
	}

	[Fact]
	public void ExplicitDiskRenderAdvancesToVisibleBootProgramFrame()
	{
		var diskPath = FindWorkspaceFile("CopperScreen", "TestImages", "Full Contact (1991)(Team 17)(Disk 1 of 2).zip");
		var emulator = CopperScreenEmulator.Create(new[] { diskPath }, AppContext.BaseDirectory);

		emulator.RenderNextFrame();

		Assert.Equal("AMIGA_BOOT_PROTECTED_DISK_UNSUPPORTED", emulator.StatusText);
		Assert.True(emulator.Framebuffer.Distinct().Count() > 1);
	}

	[Fact]
	public void CrackedDiskBootsPastFltLoaderWhenAvailable()
	{
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Full Contact (1991)(Team 17)(Disk 1 of 2)[cr FLT].zip");
		if (diskPath == null)
		{
			return;
		}

		var disk = AmigaDiskImage.Load(diskPath);
		var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));
		var boot = new AmigaBootController(machine);

		var result = boot.BootFromDisk(disk, maxInstructions: 250_000, runMode: AmigaBootRunMode.ContinueAfterBootDiskRead);
		for (var i = 0; i < 250 && machine.Bus.Disk.CaptureSnapshot().TransferCount == 0 && !machine.Cpu.State.Halted; i++)
		{
			result = boot.ContinueExecution(maxInstructions: 25_000);
		}

		var fatalDiagnostics = result.Diagnostics
			.Where(diagnostic => diagnostic.Code is "AMIGA_BOOT_UNSUPPORTED_OPCODE" or "AMIGA_BOOT_FAULT" or "AMIGA_BOOT_PROTECTED_DISK_UNSUPPORTED")
			.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")
			.ToArray();
		Assert.True(fatalDiagnostics.Length == 0, string.Join(Environment.NewLine, fatalDiagnostics));
		Assert.True(machine.Bus.Disk.CaptureSnapshot().TransferCount > 0);
	}

	[Fact]
	public void CrackedDiskRenderDoesNotTreatExecutionSliceAsFatalWhenAvailable()
	{
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Full Contact (1991)(Team 17)(Disk 1 of 2)[cr FLT].zip");
		if (diskPath == null)
		{
			return;
		}

		var emulator = CopperScreenEmulator.Create(new[] { diskPath }, AppContext.BaseDirectory);

		emulator.RenderNextFrame();
		emulator.RenderNextFrame();

		Assert.StartsWith("boot program running:", emulator.StatusText);
		Assert.DoesNotContain("AMIGA_BOOT_UNSUPPORTED_OPCODE", emulator.StatusText);
		Assert.DoesNotContain("AMIGA_BOOT_FAULT", emulator.StatusText);
		Assert.True(emulator.Framebuffer.Distinct().Count() > 1);
	}

	private static string FindWorkspaceFile(params string[] parts)
	{
		var found = TryFindWorkspaceFile(parts);
		if (found != null)
		{
			return found;
		}

		return string.Join(Path.DirectorySeparatorChar, parts);
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
}
