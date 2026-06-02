using System.Reflection;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Avalonia.Input;
using CopperMod.Amiga;
using CopperMod.Ipf;
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
	public void ShadowOfTheBeastIpfDoesNotExitDuringInitialFramesWhenAvailable()
	{
		var diskPath = TryFindWorkspaceFile(
			"CopperScreen",
			"TestImages",
			"Shadow of the Beast (1989)(Psygnosis)(US)(Disk 1 of 2).zip");
		if (diskPath == null)
		{
			return;
		}

		var emulator = CopperScreenEmulator.Create(new[] { diskPath }, AppContext.BaseDirectory);

		for (var frame = 0; frame < 120; frame++)
		{
			emulator.RenderNextFrame();
		}

		Assert.False(string.IsNullOrWhiteSpace(emulator.StatusText));
		Assert.StartsWith("boot program running:", emulator.StatusText);
		Assert.False(emulator.StatusText.StartsWith("AMIGA_BOOT_", StringComparison.Ordinal), emulator.StatusText);
	}

	[Fact]
	public void ShadowOfTheBeastPnaKickstartBootWaitsForFireThenLeavesCylinderZeroWhenAvailable()
	{
		var diskPath = TryFindWorkspaceFile(
			"CopperScreen",
			"TestImages",
			"Shadow of the Beast (1989)(Psygnosis)(Disk 1 of 2)[cr PNA].zip");
		if (diskPath == null)
		{
			return;
		}

		var emulator = CopperScreenEmulator.Create(new[] { "--profile", "expanded-kickstart13", diskPath }, AppContext.BaseDirectory);
		var machine = GetMachine(emulator);
		var maxCylinder = 0;
		for (var frame = 0; frame < 700; frame++)
		{
			emulator.RenderNextFrame();
			var disk = machine.Bus.Disk.CaptureSnapshot();
			maxCylinder = Math.Max(maxCylinder, disk.LastTransferCylinder);
			if (ContainsFatalBootStatus(emulator.StatusText))
			{
				break;
			}
		}

		var beforeFire = machine.Bus.Disk.CaptureSnapshot();
		var beforeFireTrace = machine.Bus.Disk.CaptureDmaTrace();
		Assert.False(ContainsFatalBootStatus(emulator.StatusText), emulator.StatusText);
		Assert.True(
			beforeFire.TransferCount >= 2,
			$"Expected the PNA boot to read both sides of cylinder 0 before the crack intro wait; transfers={beforeFire.TransferCount}, last={beforeFire.LastTransferCylinder}.{beforeFire.LastTransferHead}, dsklen=0x{beforeFire.Dsklen:X4}, dskbytr=0x{beforeFire.Dskbytr:X4}, PC=0x{machine.Cpu.State.ProgramCounter:X6}.");
		Assert.DoesNotContain(
			beforeFireTrace,
			entry => entry.Kind == AmigaDiskDmaTraceKind.Cancelled &&
				entry.Cylinder == 0 &&
				entry.Head == 1 &&
				entry.TransferredWords < entry.RequestedWords);

		emulator.PulsePrimaryFire(60);
		for (var frame = 0; frame < 200 && maxCylinder == 0; frame++)
		{
			emulator.RenderNextFrame();
			var disk = machine.Bus.Disk.CaptureSnapshot();
			maxCylinder = Math.Max(maxCylinder, disk.LastTransferCylinder);
			if (ContainsFatalBootStatus(emulator.StatusText))
			{
				break;
			}
		}

		var afterFire = machine.Bus.Disk.CaptureSnapshot();
		Assert.False(ContainsFatalBootStatus(emulator.StatusText), emulator.StatusText);
		Assert.True(
			maxCylinder > 0,
			$"Expected fire to release the PNA crack intro wait and continue loading beyond cylinder 0; transfers={afterFire.TransferCount}, maxCylinder={maxCylinder}, last={afterFire.LastTransferCylinder}.{afterFire.LastTransferHead}, dsklen=0x{afterFire.Dsklen:X4}, dskbytr=0x{afterFire.Dskbytr:X4}, PC=0x{machine.Cpu.State.ProgramCounter:X6}.");
	}

	[Fact]
	public void ShadowOfTheBeastIpfDiskTwoParallaxSceneReachesCoreSignalsAfterFireWaitsWhenAvailable()
	{
		var diskPath = TryFindWorkspaceFile(
			"CopperScreen",
			"TestImages",
			"Shadow of the Beast (1989)(Psygnosis)(US)(Disk 1 of 2).zip");
		if (diskPath == null || CopperScreenEmulator.ResolveNextDiskPath(diskPath) == null)
		{
			return;
		}

		var result = RunShadowOfTheBeastDiskTwoDiagnostic(diskPath, agnusTimingMode: null, stopAtCoreScene: true);

		Assert.True(
			result.ReachedCoreScene,
			BuildShadowOfTheBeastDiskTwoDiagnostic(result));
		Assert.Contains(
			result.DiskTrace,
			entry => entry.Trace.Kind == AmigaDiskDmaTraceKind.Started && entry.Trace.TransferCount > result.TransferCountAtSwap);
		Assert.DoesNotContain(
			result.DiskTrace,
			entry => entry.Trace.Kind == AmigaDiskDmaTraceKind.SyncMissing && entry.Trace.TransferCount > result.TransferCountAtSwap);
	}

	[Fact]
	public void ShadowOfTheBeastIpfDiskTwoOrientationDiagnosticsWhenEnabled()
	{
		if (!string.Equals(
			Environment.GetEnvironmentVariable("COPPERSCREEN_SHADOW_IPF_ORIENTATION_DIAGNOSTICS"),
			"1",
			StringComparison.Ordinal))
		{
			return;
		}

		var diskOnePath = TryFindWorkspaceFile(
			"CopperScreen",
			"TestImages",
			"Shadow of the Beast (1989)(Psygnosis)(US)(Disk 1 of 2).zip");
		if (diskOnePath == null)
		{
			return;
		}

		var diskTwoPath = CopperScreenEmulator.ResolveNextDiskPath(diskOnePath);
		if (diskTwoPath == null)
		{
			return;
		}

		var results = Enum.GetValues<ShadowIpfOrientationVariant>()
			.Select(variant => RunShadowOrientationDiagnostic(diskOnePath, diskTwoPath, variant))
			.ToArray();
		var production = Assert.Single(results.Where(result => result.Variant == ShadowIpfOrientationVariant.Production));
		var diagnostic = string.Join(Environment.NewLine, results.Select(result => result.ToDiagnosticString()));

		Assert.False(ContainsFatalBootStatus(production.StatusText), diagnostic);
		Assert.True(production.SawPostSwapTransfer, diagnostic);
		Assert.True(production.ReachedDiskTwoScene, diagnostic);
	}

	[Fact]
	public void ShadowOfTheBeastIpfDiskTwoAgnusTimingDiagnosticsWhenEnabled()
	{
		if (!string.Equals(
			Environment.GetEnvironmentVariable("COPPERSCREEN_SHADOW_AGNUS_TIMING_DIAGNOSTICS"),
			"1",
			StringComparison.Ordinal))
		{
			return;
		}

		var diskPath = TryFindWorkspaceFile(
			"CopperScreen",
			"TestImages",
			"Shadow of the Beast (1989)(Psygnosis)(US)(Disk 1 of 2).zip");
		if (diskPath == null || CopperScreenEmulator.ResolveNextDiskPath(diskPath) == null)
		{
			return;
		}

		var legacy = RunShadowOfTheBeastDiskTwoDiagnostic(diskPath, "legacy", stopAtCoreScene: false);
		var slot = RunShadowOfTheBeastDiskTwoDiagnostic(diskPath, "slot", stopAtCoreScene: false);
		var diagnostic = string.Join(
			Environment.NewLine,
			BuildShadowOfTheBeastDiskTwoDiagnostic(legacy),
			BuildShadowOfTheBeastDiskTwoDiagnostic(slot));

		Assert.False(ContainsFatalBootStatus(legacy.StatusText), diagnostic);
		Assert.False(ContainsFatalBootStatus(slot.StatusText), diagnostic);
		Assert.True(legacy.SawPostSwapTransfer, diagnostic);
		Assert.True(slot.SawPostSwapTransfer, diagnostic);
		Assert.True(legacy.ReachedCoreScene, diagnostic);
		Assert.True(slot.ReachedCoreScene, diagnostic);
		Assert.DoesNotContain(
			legacy.DiskTrace,
			entry => entry.Trace.Kind == AmigaDiskDmaTraceKind.SyncMissing && entry.Trace.TransferCount > legacy.TransferCountAtSwap);
		Assert.DoesNotContain(
			slot.DiskTrace,
			entry => entry.Trace.Kind == AmigaDiskDmaTraceKind.SyncMissing && entry.Trace.TransferCount > slot.TransferCountAtSwap);
	}

	[Fact]
	public void OperationThunderboltIpfDoesNotHitEarlyRawLoaderFaultWhenAvailable()
	{
		var diskPath = TryFindWorkspaceFile(
			"CopperScreen",
			"TestImages",
			"Operation Thunderbolt (1990)(Ocean)(FR)(en)(Disk 1 of 2)[compilation Les Justiciers 2].zip");
		if (diskPath == null)
		{
			return;
		}

		var emulator = CopperScreenEmulator.Create(new[] { diskPath }, AppContext.BaseDirectory);
		for (var frame = 0; frame < 240; frame++)
		{
			emulator.RenderNextFrame();
			if (emulator.StatusText.Contains("AMIGA_BOOT_UNSUPPORTED_OPCODE", StringComparison.Ordinal) ||
				emulator.StatusText.Contains("AMIGA_BOOT_FAULT", StringComparison.Ordinal))
			{
				break;
			}
		}

		Assert.False(string.IsNullOrWhiteSpace(emulator.StatusText));
		Assert.StartsWith("boot program running:", emulator.StatusText);
		Assert.DoesNotContain("AMIGA_BOOT_UNSUPPORTED_OPCODE", emulator.StatusText);
		Assert.DoesNotContain("AMIGA_BOOT_FAULT", emulator.StatusText);
	}

	[Fact]
	public void InvalidStartupDiskRendersStatusInsteadOfThrowing()
	{
		var diskPath = Path.Combine(Path.GetTempPath(), "copperscreen-invalid-startup.adf");
		File.WriteAllBytes(diskPath, new byte[128]);
		try
		{
			var emulator = CopperScreenEmulator.Create(new[] { diskPath }, AppContext.BaseDirectory);

			emulator.RenderNextFrame();

			Assert.Contains("sector images", emulator.StatusText);
		}
		finally
		{
			File.Delete(diskPath);
		}
	}

	[Fact]
	public void BootingDiskLeavesBlankHardwareFrameInsteadOfInsertDiskOverlay()
	{
		var diskPath = Path.Combine(Path.GetTempPath(), "copperscreen-blank-boot.adf");
		File.WriteAllBytes(diskPath, CreateBranchToSelfBootDisk());
		try
		{
			var emulator = CopperScreenEmulator.Create(new[] { diskPath }, AppContext.BaseDirectory);

			emulator.RenderNextFrame();

			Assert.StartsWith("boot program running:", emulator.StatusText);
			Assert.All(emulator.Framebuffer, pixel => Assert.Equal(unchecked((int)0xFF000000), pixel));
		}
		finally
		{
			File.Delete(diskPath);
		}
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
	public void DiskOneBootDoesNotInstallSyntheticProtectionGate()
	{
		var diskPath = FindWorkspaceFile("CopperScreen", "TestImages", "Full Contact (1991)(Team 17)(Disk 1 of 2).zip");
		var disk = AmigaDiskImage.Load(diskPath);
		var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));
		var boot = new AmigaBootController(machine);

		var result = boot.BootFromDisk(disk, maxInstructions: 25_000);

		Assert.False(machine.Bus.HasHostCallback(0x0007_B000));
		Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "AMIGA_BOOT_PROTECTED_DISK_UNSUPPORTED");
	}

	[Fact]
	public void DiskOneKickstartRomBootDoesNotInstallSyntheticProtectionGateWhenAvailable()
	{
		var romPath = TryFindWorkspaceFile("CopperScreen", "ROM", "Kickstart_13.rom");
		if (romPath == null)
		{
			return;
		}

		var diskPath = FindWorkspaceFile("CopperScreen", "TestImages", "Full Contact (1991)(Team 17)(Disk 1 of 2).zip");
		var options = AmigaMachineOptions
			.ForProfile(AmigaMachineProfile.A500Pal512KBoot)
			.WithKickstart(AmigaKickstartConfiguration.FromRomImage(AmigaKickstartVersion.Kickstart13, File.ReadAllBytes(romPath)));
		var machine = new AmigaMachine(options);
		var boot = new AmigaBootController(machine);

		boot.StartKickstartRomBoot(AmigaDiskImage.Load(diskPath));

		Assert.False(machine.Bus.HasHostCallback(0x0007_B000));
	}

	[Fact]
	public void FullContactIpfDiskOneDoesNotInstallSyntheticProtectionGateWhenAvailable()
	{
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Full Contact v1.2-2.0 (1991)(Team 17)(Disk 1 of 2).zip");
		if (diskPath == null)
		{
			return;
		}

		var disk = AmigaDiskImage.Load(diskPath);
		var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));
		var boot = new AmigaBootController(machine);

		var result = boot.BootFromDisk(disk, maxInstructions: 25_000);

		Assert.True(disk.HasPreservedTrackData);
		Assert.False(machine.Bus.HasHostCallback(0x0007_B000));
		Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "AMIGA_BOOT_PROTECTED_DISK_UNSUPPORTED");
	}

	[Fact]
	public void FullContactIpfKickstartRomBootDoesNotInstallSyntheticProtectionGateWhenAvailable()
	{
		var romPath = TryFindWorkspaceFile("CopperScreen", "ROM", "Kickstart_13.rom");
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Full Contact v1.2-2.0 (1991)(Team 17)(Disk 1 of 2).zip");
		if (romPath == null || diskPath == null)
		{
			return;
		}

		var options = AmigaMachineOptions
			.ForProfile(AmigaMachineProfile.A500Pal512KBoot)
			.WithKickstart(AmigaKickstartConfiguration.FromRomImage(AmigaKickstartVersion.Kickstart13, File.ReadAllBytes(romPath)));
		var machine = new AmigaMachine(options);
		var boot = new AmigaBootController(machine);

		boot.StartKickstartRomBoot(AmigaDiskImage.Load(diskPath));

		Assert.False(machine.Bus.HasHostCallback(0x0007_B000));
	}

	[Fact]
	public void FullContactIpfBootReachesProtectedLoadingScreenWhenAvailable()
	{
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Full Contact v1.2-2.0 (1991)(Team 17)(Disk 1 of 2).zip");
		if (diskPath == null)
		{
			return;
		}

		var emulator = CopperScreenEmulator.Create(new[] { "--profile", "vanilla-kickstart13", diskPath }, AppContext.BaseDirectory);
		var fatalFrame = -1;
		var reachedLoadingScreen = false;
		for (var frame = 0; frame < 1_200; frame++)
		{
			emulator.RenderNextFrame();
			if (ContainsFatalBootStatus(emulator.StatusText))
			{
				fatalFrame = frame;
				break;
			}

			reachedLoadingScreen |= emulator.DisplaySnapshot.LastBitplaneNonZeroPixels > 40_000 &&
				GetMachine(emulator).Bus.Disk.CaptureSnapshot().TransferCount > 24;
			if (reachedLoadingScreen)
			{
				break;
			}
		}

		var machine = GetMachine(emulator);
		var disk = machine.Bus.Disk.CaptureSnapshot();
		var trace = machine.Bus.Disk.CaptureDmaTrace();
		var diagnostics = string.Join(Environment.NewLine, GetDiagnostics(emulator).Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
		Assert.Equal(-1, fatalFrame);
		Assert.StartsWith("boot program running:", emulator.StatusText);
		Assert.True(
			reachedLoadingScreen,
			$"Expected Full Contact IPF to reach the protected loading screen; transfers={disk.TransferCount}, nonzero={emulator.DisplaySnapshot.LastBitplaneNonZeroPixels}, PC=0x{machine.Cpu.State.ProgramCounter:X6}, dsklen=0x{disk.Dsklen:X4}, dskbytr=0x{disk.Dskbytr:X4}, dmacon=0x{machine.Bus.Paula.Dmacon:X4}, adkcon=0x{machine.Bus.Paula.Adkcon:X4}.{Environment.NewLine}{diagnostics}");
		Assert.DoesNotContain(trace, entry => entry.Kind == AmigaDiskDmaTraceKind.SyncMissing);
	}

	[Fact]
	public void FullContactAdfKickstartRomBootReachesExtendedLoadingScreenWhenAvailable()
	{
		var romPath = TryFindWorkspaceFile("CopperScreen", "ROM", "Kickstart_13.rom");
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Full Contact (1991)(Team 17)(Disk 1 of 2).zip");
		if (romPath == null || diskPath == null)
		{
			return;
		}

		var emulator = CopperScreenEmulator.Create(new[] { "--profile", "expanded-kickstart13", diskPath }, AppContext.BaseDirectory);
		var machine = GetMachine(emulator);
		var fatalFrame = -1;
		var reachedLoadingScreen = false;
		for (var frame = 0; frame < 1_400; frame++)
		{
			emulator.RenderNextFrame();
			if (ContainsFatalBootStatus(emulator.StatusText))
			{
				fatalFrame = frame;
				break;
			}

			reachedLoadingScreen |= emulator.DisplaySnapshot.LastBitplaneNonZeroPixels > 40_000 &&
				machine.Bus.Disk.CaptureSnapshot().TransferCount > 24;
			if (reachedLoadingScreen)
			{
				break;
			}
		}

		var snapshot = machine.Bus.Disk.CaptureSnapshot();
		var diagnostics = string.Join(Environment.NewLine, GetDiagnostics(emulator).Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
		Assert.Equal(-1, fatalFrame);
		Assert.StartsWith("boot program running:", emulator.StatusText);
		Assert.True(
			reachedLoadingScreen,
			$"Expected the exact Full Contact ADF to reach its extended loading screen; transfers={snapshot.TransferCount}, nonzero={emulator.DisplaySnapshot.LastBitplaneNonZeroPixels}, PC=0x{machine.Cpu.State.ProgramCounter:X6}, dsklen=0x{snapshot.Dsklen:X4}, dskbytr=0x{snapshot.Dskbytr:X4}, dmacon=0x{machine.Bus.Paula.Dmacon:X4}, adkcon=0x{machine.Bus.Paula.Adkcon:X4}.{Environment.NewLine}{diagnostics}");
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
	public void NextDiskResolutionPreservesCrackSuffixAndSelectsDiskTwo()
	{
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Full Contact (1991)(Team 17)(Disk 1 of 2)[cr FLT].zip");
		if (diskPath == null)
		{
			return;
		}

		var resolved = CopperScreenEmulator.ResolveNextDiskPath(diskPath);

		Assert.NotNull(resolved);
		Assert.EndsWith("Full Contact (1991)(Team 17)(Disk 2 of 2)[cr FLT].zip", resolved);
	}

	[Fact]
	public void PreviousDiskResolutionPreservesCrackSuffixAndSelectsDiskOne()
	{
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Full Contact (1991)(Team 17)(Disk 2 of 2)[cr FLT].zip");
		if (diskPath == null)
		{
			return;
		}

		var resolved = CopperScreenEmulator.ResolvePreviousDiskPath(diskPath);

		Assert.NotNull(resolved);
		Assert.EndsWith("Full Contact (1991)(Team 17)(Disk 1 of 2)[cr FLT].zip", resolved);
	}

	[Fact]
	public void InsertNextDiskUpdatesCurrentImageWhenMatchingDiskExists()
	{
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Full Contact (1991)(Team 17)(Disk 1 of 2).zip");
		if (diskPath == null)
		{
			return;
		}

		var emulator = CopperScreenEmulator.Create(new[] { diskPath }, AppContext.BaseDirectory);

		Assert.True(emulator.InsertNextDisk());

		Assert.EndsWith("Full Contact (1991)(Team 17)(Disk 2 of 2).zip", emulator.DiskPath);
		Assert.StartsWith("inserted ", emulator.StatusText);
		Assert.False(emulator.IsPrimaryFirePressed);
	}

	[Fact]
	public void InsertNextDiskDuringRunningEmulationUsesVisibleEjectPhase()
	{
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Full Contact (1991)(Team 17)(Disk 1 of 2).zip");
		if (diskPath == null)
		{
			return;
		}

		var emulator = CopperScreenEmulator.Create(new[] { diskPath }, AppContext.BaseDirectory);
		emulator.RenderNextFrame();

		Assert.True(emulator.InsertNextDisk());

		Assert.EndsWith("Full Contact (1991)(Team 17)(Disk 2 of 2).zip", emulator.DiskPath);
		Assert.True(emulator.IsDiskSwapPending);
		Assert.Equal("DF0 changing disk", emulator.DriveStatusText);

		for (var frame = 0; frame < 26; frame++)
		{
			emulator.RenderNextFrame();
		}

		Assert.False(emulator.IsDiskSwapPending);
		Assert.Contains("DF0", emulator.DriveStatusText);
	}

	[Fact]
	public void PrimaryFirePulseReachesCiaAPortDuringRender()
	{
		var emulator = CopperScreenEmulator.CreateWithoutDisk();

		emulator.PulsePrimaryFire(frames: 1);
		emulator.RenderNextFrame();

		Assert.Equal("insert disk image", emulator.StatusText);
		Assert.False(emulator.IsPrimaryFirePressed);
	}

	[Fact]
	public void PrimaryFirePulseDrivesBothJoystickFireLines()
	{
		var emulator = CopperScreenEmulator.CreateWithoutDisk();

		emulator.PulsePrimaryFire(frames: 2);
		emulator.RenderNextFrame();

		var machine = GetMachine(emulator);
		var ciaPortA = machine.Bus.ReadByte(0x00BFE001);
		Assert.Equal(0, ciaPortA & 0xC0);
		Assert.True(emulator.IsPrimaryFirePressed);
	}

	[Fact]
	public void MousePortInputReachesAmigaPortOneRegisters()
	{
		var emulator = CopperScreenEmulator.CreateWithoutDisk();

		emulator.MoveMousePort(5, -1);
		emulator.SetMouseButtons(primaryFirePressed: true, secondFirePressed: true);
		emulator.RenderNextFrame();

		var machine = GetMachine(emulator);
		Assert.Equal(0xFF05, machine.Bus.ReadWord(0x00DFF00A));
		Assert.Equal(0, machine.Bus.ReadByte(0x00BFE001) & 0x40);
		Assert.NotEqual(0, machine.Bus.ReadByte(0x00BFE001) & 0x80);
		Assert.Equal(0, machine.Bus.ReadWord(0x00DFF016) & 0x0400);
		Assert.NotEqual(0, machine.Bus.ReadWord(0x00DFF016) & 0x4000);
	}

	[Fact]
	public void QuickMouseButtonClickSurvivesUntilNextRenderedFrame()
	{
		var emulator = CopperScreenEmulator.CreateWithoutDisk();

		emulator.SetMouseButtons(primaryFirePressed: true, secondFirePressed: true);
		emulator.SetMouseButtons(primaryFirePressed: false, secondFirePressed: false);
		emulator.RenderNextFrame();

		var machine = GetMachine(emulator);
		Assert.Equal(0, machine.Bus.ReadByte(0x00BFE001) & 0x40);
		Assert.Equal(0, machine.Bus.ReadWord(0x00DFF016) & 0x0400);
		Assert.True(emulator.IsPrimaryFirePressed);

		emulator.RenderNextFrame();

		Assert.NotEqual(0, machine.Bus.ReadByte(0x00BFE001) & 0x40);
		Assert.NotEqual(0, machine.Bus.ReadWord(0x00DFF016) & 0x0400);
		Assert.False(emulator.IsPrimaryFirePressed);
	}

	[Fact]
	public void NumpadJoystickMappingUsesPhysicalKeyWhenNumLockIsOff()
	{
		Assert.Equal(MainWindow.JoystickKeys.NumPad2, MainWindow.GetJoystickKey(Key.Down, PhysicalKey.NumPad2));
		Assert.Equal(MainWindow.JoystickKeys.None, MainWindow.GetJoystickKey(Key.Down, PhysicalKey.ArrowDown));
	}

	[Fact]
	public void NumpadJoystickInputReachesAmigaPortTwoRegisters()
	{
		var emulator = CopperScreenEmulator.CreateWithoutDisk();

		emulator.SetJoystickPort(
			up: true,
			down: false,
			left: true,
			right: false,
			primaryFirePressed: true,
			secondFirePressed: true);
        emulator.RenderNextFrame();

        var machine = GetMachine(emulator);
        Assert.Equal(0x0200, machine.Bus.ReadWord(0x00DFF00C));
        Assert.NotEqual(0, machine.Bus.ReadByte(0x00BFE001) & 0x40);
        Assert.Equal(0, machine.Bus.ReadByte(0x00BFE001) & 0x80);
        Assert.NotEqual(0, machine.Bus.ReadWord(0x00DFF016) & 0x0400);
        Assert.Equal(0, machine.Bus.ReadWord(0x00DFF016) & 0x4000);
	}

	[Fact]
	public void JoystickDownReachesAmigaPortTwoRegisters()
	{
		var emulator = CopperScreenEmulator.CreateWithoutDisk();

		emulator.SetJoystickPort(
			up: false,
			down: true,
			left: false,
			right: false,
			primaryFirePressed: false,
			secondFirePressed: false);
        emulator.RenderNextFrame();

        var machine = GetMachine(emulator);
        Assert.Equal(0x0001, machine.Bus.ReadWord(0x00DFF00C));
    }

	[Fact]
	public void ExplicitDiskRenderStartsBootProgramWithoutSyntheticProtectionStatus()
	{
		var diskPath = FindWorkspaceFile("CopperScreen", "TestImages", "Full Contact (1991)(Team 17)(Disk 1 of 2).zip");
		var emulator = CopperScreenEmulator.Create(new[] { diskPath }, AppContext.BaseDirectory);

		emulator.RenderNextFrame();

		Assert.StartsWith("boot program running:", emulator.StatusText);
		Assert.DoesNotContain("AMIGA_BOOT_PROTECTED_DISK_UNSUPPORTED", emulator.StatusText);
		Assert.NotEmpty(emulator.Framebuffer);
	}

	[Fact]
	public void CrackedDiskWaitsForDiskTwoInsteadOfContinuingWithDiskOneWhenAvailable()
	{
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Full Contact (1991)(Team 17)(Disk 1 of 2)[cr FLT].zip");
		if (diskPath == null)
		{
			return;
		}

		var disk = AmigaDiskImage.Load(diskPath);
		var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));
		var boot = new AmigaBootController(machine);

		var result = BootFromDiskWithAdjacent(boot, machine, disk, diskPath, maxInstructions: 250_000);
		for (var i = 0; i < 250 && machine.Bus.Disk.CaptureSnapshot().TransferCount == 0 && !machine.Cpu.State.Halted; i++)
		{
			result = boot.ContinueExecution(maxInstructions: 25_000);
		}

		Assert.Equal(0, machine.Bus.Disk.CaptureSnapshot().TransferCount);
		var fatalDiagnostics = result.Diagnostics
			.Where(diagnostic => diagnostic.Code is "AMIGA_BOOT_UNSUPPORTED_OPCODE" or "AMIGA_BOOT_FAULT" or "AMIGA_BOOT_PROTECTED_DISK_UNSUPPORTED")
			.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")
			.ToArray();
		Assert.True(fatalDiagnostics.Length == 0, string.Join(Environment.NewLine, fatalDiagnostics));
		Assert.NotEqual(0, machine.Bus.ReadByte(0x00BFE001) & 0x04);
	}

	[Fact]
	public void CrackedDiskStartsCustomLoaderWhenFirePressedOnDiskOneWhenAvailable()
	{
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Full Contact (1991)(Team 17)(Disk 1 of 2)[cr FLT].zip");
		if (diskPath == null)
		{
			return;
		}

		var disk = AmigaDiskImage.Load(diskPath);
		var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot).WithFloppyDriveCount(1));
		var boot = new AmigaBootController(machine);

		var result = BootFromDiskWithAdjacent(boot, machine, disk, diskPath, maxInstructions: 250_000);
		for (var i = 0; i < 300 && machine.Bus.Disk.CaptureSnapshot().TransferCount == 0 && !machine.Cpu.State.Halted; i++)
		{
			machine.Bus.GamePort0FirePressed = i is >= 120 and < 160;
			result = boot.ContinueExecution(maxInstructions: 25_000);
		}

		var fatalDiagnostics = result.Diagnostics
			.Where(diagnostic => diagnostic.Code is "AMIGA_BOOT_UNSUPPORTED_OPCODE" or "AMIGA_BOOT_FAULT" or "AMIGA_BOOT_PROTECTED_DISK_UNSUPPORTED")
			.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")
			.ToArray();
		Assert.True(fatalDiagnostics.Length == 0, string.Join(Environment.NewLine, fatalDiagnostics));
		if (IsWaitingOnUnavailableOrNotReadyDrive(machine))
		{
			return;
		}

		var diskSnapshot = machine.Bus.Disk.CaptureSnapshot();
		Assert.True(
			diskSnapshot.TransferCount > 0,
			$"Expected a raw disk transfer; selectedDrive={diskSnapshot.SelectedDrive}, activeDmaDrive={diskSnapshot.ActiveDmaDrive}, selected={diskSnapshot.Selected}, motor={diskSnapshot.MotorOn}, ciab=0x{diskSnapshot.CiabPortB:X2}, dsklen=0x{diskSnapshot.Dsklen:X4}, dskbytr=0x{diskSnapshot.Dskbytr:X4}, dmacon=0x{machine.Bus.Paula.Dmacon:X4}, adkcon=0x{machine.Bus.Paula.Adkcon:X4}, PC=0x{machine.Cpu.State.ProgramCounter:X6}.");
	}

	[Fact]
	public void FullContactCopperStartDoesNotStickInInitialBlitterWaitWhenAvailable()
	{
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Full Contact (1991)(Team 17)(Disk 1 of 2)[cr FLT].zip");
		if (diskPath == null)
		{
			return;
		}

		var emulator = CopperScreenEmulator.Create(new[] { "--profile", "vanilla-copperstart", diskPath }, AppContext.BaseDirectory);
		for (var frame = 0; frame < 420; frame++)
		{
			if (frame == 260)
			{
				emulator.PulsePrimaryFire(frames: 30);
			}

			emulator.RenderNextFrame();
		}

		var machine = GetMachine(emulator);
		var disk = machine.Bus.Disk.CaptureSnapshot();
		Assert.StartsWith("boot program running:", emulator.StatusText);
		Assert.NotEqual(0x04D89Cu, machine.Cpu.State.ProgramCounter);
		Assert.True(
			disk.TransferCount > 0,
			$"Expected CopperStart to reach the raw loader after the intro blit; transfers={disk.TransferCount}, dmacon=0x{machine.Bus.Paula.Dmacon:X4}, PC=0x{machine.Cpu.State.ProgramCounter:X6}.");
		Assert.NotEqual(0, machine.Bus.Paula.Dmacon & 0x0040);
	}

	[Fact]
	public void CrackedDiskIntroWaitsForFireAndProducesAudioWhenAvailable()
	{
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Full Contact (1991)(Team 17)(Disk 1 of 2)[cr FLT].zip");
		if (diskPath == null)
		{
			return;
		}

		var emulator = CopperScreenEmulator.Create(new[] { "--profile", "vanilla-copperstart", diskPath }, AppContext.BaseDirectory);
		var audio = new float[emulator.AudioFramesPerAppFrame(44_100) * 2];
		var peak = 0.0f;
		var quantizedAudioLevels = new HashSet<int>();
		var displayedBitmap = false;
		for (var frame = 0; frame < 300; frame++)
		{
			emulator.RenderNextFrame();
			var snapshot = emulator.DisplaySnapshot;
			displayedBitmap |= snapshot.LastBitplaneNonZeroPixels > 10_000 &&
				snapshot.LastBitplaneMinX <= 8 &&
				snapshot.LastBitplaneMaxX >= 300 &&
				snapshot.LastBitplaneMaxY >= 180 &&
				emulator.Framebuffer.Distinct().Take(3).Count() > 2;
			var frames = emulator.RenderAudio(audio, 44_100, 2);
			for (var i = 0; i < frames * 2; i++)
			{
				peak = Math.Max(peak, Math.Abs(audio[i]));
				if (Math.Abs(audio[i]) > 0.0001f)
				{
					quantizedAudioLevels.Add((int)MathF.Round(audio[i] * 4096.0f));
				}
			}
		}

		Assert.StartsWith("boot program running:", emulator.StatusText);
		if (IsWaitingOnUnavailableOrNotReadyDrive(GetMachine(emulator)))
		{
			return;
		}

		Assert.True(displayedBitmap, "Expected the cracktro bitmap, Topaz text, and mid-scanline effects to span the visible display while waiting for fire.");
		Assert.True(peak > 0.0001f, $"Expected audible cracktro output, peak was {peak}.");
		Assert.True(quantizedAudioLevels.Count > 16, $"Expected time-varying cracktro audio, got {quantizedAudioLevels.Count} sample levels.");
	}

	[Fact]
	public void CrackedDiskIntroDoesNotLeakBitplanePixelsOnCopperDisabledLineWhenAvailable()
	{
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Full Contact (1991)(Team 17)(Disk 1 of 2)[cr FLT].zip");
		if (diskPath == null)
		{
			return;
		}

		var emulator = CopperScreenEmulator.Create(new[] { "--profile", "vanilla-copperstart", diskPath }, AppContext.BaseDirectory);
		for (var frame = 0; frame < 300; frame++)
		{
			emulator.RenderNextFrame();
		}

		Assert.StartsWith("boot program running:", emulator.StatusText);
		var separatorPixels = CountNonBlackPixels(emulator.Framebuffer, emulator.Width, 0, 95, emulator.Width, 1);
		Assert.True(
			separatorPixels == 0,
			$"Expected the Copper-disabled cracktro separator line to remain black, got {separatorPixels}; row95={BuildRowProbe(emulator.Framebuffer, emulator.Width, 95, 0, emulator.Width, step: 2)}");
	}

	[Fact]
	public void AudioFramesPerAppFrameUsesExactPalCapacity()
	{
		var emulator = CopperScreenEmulator.CreateWithoutDisk();
		var expectedCapacity = (int)((((long)44_100 * AmigaConstants.A500PalCpuCyclesPerFrame) - 1) /
			AmigaConstants.A500PalCpuCyclesPerSecond) + 1;

		Assert.Equal(881, expectedCapacity);
		Assert.Equal(expectedCapacity, emulator.AudioFramesPerAppFrame(44_100));
		Assert.NotEqual(882, emulator.AudioFramesPerAppFrame(44_100));
	}

	[Fact]
	public void AudioSilenceUsesExactVariablePalFrameCounts()
	{
		var emulator = CopperScreenEmulator.CreateWithoutDisk();
		var audio = new float[emulator.AudioFramesPerAppFrame(44_100) * 2];
		var counts = new HashSet<int>();
		var totalFrames = 0L;
		const int renderedFrames = 1000;

		for (var frame = 0; frame < renderedFrames; frame++)
		{
			var frames = emulator.RenderAudio(audio, 44_100, 2);
			counts.Add(frames);
			totalFrames += frames;
			Assert.All(audio.AsSpan(0, frames * 2).ToArray(), sample => Assert.Equal(0.0f, sample));
		}

		var expectedTotal = ((long)44_100 * AmigaConstants.A500PalCpuCyclesPerFrame * renderedFrames) /
			AmigaConstants.A500PalCpuCyclesPerSecond;
		Assert.Equal(expectedTotal, totalFrames);
		Assert.Contains(880, counts);
		Assert.Contains(881, counts);
	}

	[Fact]
	public void AudioSilenceCanReturnZeroFramesAtVeryLowSampleRates()
	{
		var emulator = CopperScreenEmulator.CreateWithoutDisk();
		var audio = new float[emulator.AudioFramesPerAppFrame(1) * 2];
		var totalFrames = 0;
		var sawZeroFrame = false;
		const int renderedFrames = 51;

		for (var frame = 0; frame < renderedFrames; frame++)
		{
			var frames = emulator.RenderAudio(audio, 1, 2);
			sawZeroFrame |= frames == 0;
			totalFrames += frames;
		}

		var expectedTotal = (int)(((long)AmigaConstants.A500PalCpuCyclesPerFrame * renderedFrames) /
			AmigaConstants.A500PalCpuCyclesPerSecond);
		Assert.True(sawZeroFrame);
		Assert.Equal(expectedTotal, totalFrames);
	}

	[Fact]
	public void AudioIntegerResamplingMapsSourceFramesDeterministically()
	{
		Assert.Equal(0, CopperScreenEmulator.MapOutputFrameToSourceFrame(0, 5, 7));
		Assert.Equal(1, CopperScreenEmulator.MapOutputFrameToSourceFrame(1, 5, 7));
		Assert.Equal(2, CopperScreenEmulator.MapOutputFrameToSourceFrame(2, 5, 7));
		Assert.Equal(4, CopperScreenEmulator.MapOutputFrameToSourceFrame(3, 5, 7));
		Assert.Equal(5, CopperScreenEmulator.MapOutputFrameToSourceFrame(4, 5, 7));
		Assert.Equal(0, CopperScreenEmulator.MapOutputFrameToSourceFrame(0, 1, 7));
		Assert.Equal(0, CopperScreenEmulator.MapOutputFrameToSourceFrame(3, 8, 1));
	}

	[Fact]
	public void CrackedDiskIntroPatchesTextFontFromKickstartShimWhenAvailable()
	{
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Full Contact (1991)(Team 17)(Disk 1 of 2)[cr FLT].zip");
		if (diskPath == null)
		{
			return;
		}

		var disk = AmigaDiskImage.Load(diskPath);
		var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot).WithFloppyDriveCount(1));
		var boot = new AmigaBootController(machine);
		StartBootFromDiskWithAdjacent(boot, machine, disk, diskPath);
		var targetCycle = 0L;
		var palFrameCycles = Math.Max(1, (long)Math.Round(AmigaConstants.A500PalCpuClockHz / AmigaConstants.A500PalVBlankHz));
		for (var frame = 0; frame < 600 && !machine.Cpu.State.Halted; frame++)
		{
			targetCycle += palFrameCycles;
			boot.ContinueExecutionUntilCycle(targetCycle, maxInstructions: 100_000);
		}

		if (IsWaitingOnUnavailableOrNotReadyDrive(machine))
		{
			return;
		}

		Assert.Equal(AmigaKickstartRomFont.FontBaseAddress, BigEndian.ReadUInt32(machine.Bus.ChipRam, 0x04E442, "cracktro font pointer"));
		Assert.Equal(machine.Bus.ReadByte(AmigaKickstartRomFont.FontBaseAddress + (byte)'F'), machine.Bus.ChipRam[0x0660AB]);
		Assert.Equal(machine.Bus.ReadByte(AmigaKickstartRomFont.FontBaseAddress + 0x180 + (byte)'F'), machine.Bus.ChipRam[0x0660D3]);
		Assert.Equal(machine.Bus.ReadByte(AmigaKickstartRomFont.FontBaseAddress + 0x240 + (byte)'F'), machine.Bus.ChipRam[0x0660FB]);
	}

	[Fact]
	public void AudioCatchUpQueuesSeveralBuffersWhenTimerFallsBehind()
	{
		Assert.Equal(5, MainWindow.CalculateFramesToRender(0, catchUpAudio: true));
		Assert.Equal(3, MainWindow.CalculateFramesToRender(2, catchUpAudio: true));
		Assert.Equal(0, MainWindow.CalculateFramesToRender(5, catchUpAudio: true));
		Assert.Equal(0, MainWindow.CalculateFramesToRender(8, catchUpAudio: true));
		Assert.Equal(1, MainWindow.CalculateFramesToRender(0, catchUpAudio: false));
		Assert.Equal(0, MainWindow.CalculateFramesToRender(8, catchUpAudio: false));
		Assert.Equal(1, MainWindow.CalculateFramesToRender(null, catchUpAudio: true));
	}

	[Fact]
	public void CrackedDiskRunsPastPostFireDecodedEntryWithoutBootFaultWhenAvailable()
	{
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Full Contact (1991)(Team 17)(Disk 1 of 2)[cr FLT].zip");
		if (diskPath == null)
		{
			return;
		}

		var disk = AmigaDiskImage.Load(diskPath);
		var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot).WithFloppyDriveCount(1));
		var boot = new AmigaBootController(machine);
		var reachedDecodedLoader = false;

		var result = BootFromDiskWithAdjacent(boot, machine, disk, diskPath, maxInstructions: 250_000);
		for (var i = 0; i < 750 && !machine.Cpu.State.Halted; i++)
		{
			machine.Bus.GamePort0FirePressed = i is >= 120 and < 160;
			result = boot.ContinueExecution(maxInstructions: 25_000);
			var pc = machine.Cpu.State.ProgramCounter;
			reachedDecodedLoader |= pc is >= 0x0001_C300 and < 0x0001_D000;
			if (result.Diagnostics.Any(diagnostic => diagnostic.Code is "AMIGA_BOOT_UNSUPPORTED_OPCODE" or "AMIGA_BOOT_FAULT" or "AMIGA_BOOT_PROTECTED_DISK_UNSUPPORTED"))
			{
				break;
			}
		}

		var fatalDiagnostics = result.Diagnostics
			.Where(diagnostic => diagnostic.Code is "AMIGA_BOOT_UNSUPPORTED_OPCODE" or "AMIGA_BOOT_FAULT" or "AMIGA_BOOT_PROTECTED_DISK_UNSUPPORTED")
			.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")
			.ToArray();
		Assert.True(fatalDiagnostics.Length == 0, string.Join(Environment.NewLine, fatalDiagnostics));
		if (IsWaitingOnUnavailableOrNotReadyDrive(machine))
		{
			return;
		}

		Assert.True(reachedDecodedLoader);
	}

	[Fact]
	public void SuperfrogCrackedBootReadsRawTrackHeadersBeforeJumpingWhenAvailable()
	{
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Superfrog (1993)(Team 17)(Disk 1 of 4)[cr CSL][t +10 TRSI].zip");
		if (diskPath == null)
		{
			return;
		}

		var disk = AmigaDiskImage.Load(diskPath);
		var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));
		var boot = new AmigaBootController(machine);
		var decodedRawTrack = false;
		var readRawHeader = false;

		var result = BootFromDiskWithAdjacent(boot, machine, disk, diskPath, maxInstructions: 250_000);
		for (var i = 0; i < 4_000 && !machine.Cpu.State.Halted; i++)
		{
			var firePressed = machine.Cpu.State.Cycles < 4_000_000;
			machine.Bus.GamePort0FirePressed = firePressed;
			machine.Bus.GamePort1FirePressed = firePressed;
			result = boot.ContinueExecution(maxInstructions: 2_500);
			decodedRawTrack |= machine.Bus.ReadWord(0x0007_C180) == 0x4154;
			readRawHeader |= machine.Bus.ReadWord(0x0007_0406) == 0x4489 &&
				(DecodeOddEvenLong(machine.Bus.ChipRam, 0x0007_0408, 0x0007_040C) & 0xFFFF_0000u) == 0xFF00_0000u;
			if (decodedRawTrack)
			{
				break;
			}

			if (result.Diagnostics.Any(diagnostic => diagnostic.Code is "AMIGA_BOOT_UNSUPPORTED_OPCODE" or "AMIGA_BOOT_FAULT"))
			{
				break;
			}
		}

		var fatalDiagnostics = result.Diagnostics
			.Where(diagnostic => diagnostic.Code is "AMIGA_BOOT_UNSUPPORTED_OPCODE" or "AMIGA_BOOT_FAULT")
			.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")
			.ToArray();
		if (IsWaitingOnUnavailableExternalDrive(machine))
		{
			return;
		}

		if (decodedRawTrack)
		{
			Assert.True(fatalDiagnostics.Length == 0, string.Join(Environment.NewLine, fatalDiagnostics));
		}

		var diskSnapshot = machine.Bus.Disk.CaptureSnapshot();
		Assert.True(
			readRawHeader || diskSnapshot.TransferCount > 0,
			$"{string.Join(Environment.NewLine, fatalDiagnostics)}{Environment.NewLine}transfers={diskSnapshot.TransferCount}, selectedDrive={diskSnapshot.SelectedDrive}, lastDrive={diskSnapshot.LastTransferDrive}, last={diskSnapshot.LastTransferCylinder}.{diskSnapshot.LastTransferHead}@0x{diskSnapshot.LastTransferAddress:X6}, ciab=0x{diskSnapshot.CiabPortB:X2}, dsklen=0x{diskSnapshot.Dsklen:X4}, dskbytr=0x{diskSnapshot.Dskbytr:X4}, dmacon=0x{machine.Bus.Paula.Dmacon:X4}, adkcon=0x{machine.Bus.Paula.Adkcon:X4}, PC=0x{machine.Cpu.State.ProgramCounter:X6}.");
	}

	[Fact]
	public void SuperfrogCslCracktroLineBlitsDoNotFaultWhenAvailable()
	{
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Superfrog (1993)(Team 17)(Disk 1 of 4)[cr CSL].zip");
		if (diskPath == null)
		{
			return;
		}

		var emulator = CopperScreenEmulator.Create(new[] { diskPath }, AppContext.BaseDirectory);
		for (var frame = 0; frame < 90; frame++)
		{
			emulator.RenderNextFrame();
			if (emulator.StatusText.Contains("AMIGA_BOOT_UNSUPPORTED_OPCODE", StringComparison.Ordinal) ||
				emulator.StatusText.Contains("AMIGA_BOOT_FAULT", StringComparison.Ordinal))
			{
				break;
			}
		}

		Assert.StartsWith("boot program running:", emulator.StatusText);
		Assert.DoesNotContain("AMIGA_BOOT_UNSUPPORTED_OPCODE", emulator.StatusText);
		Assert.DoesNotContain("AMIGA_BOOT_FAULT", emulator.StatusText);
	}

	[Fact]
	public void SuperfrogCslCracktroFilledCubeRemainsInTopLeftWhenAvailable()
	{
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Superfrog (1993)(Team 17)(Disk 1 of 4)[cr CSL].zip");
		if (diskPath == null)
		{
			return;
		}

		var emulator = CopperScreenEmulator.Create(new[] { diskPath }, AppContext.BaseDirectory);
		for (var frame = 0; frame < 500; frame++)
		{
			emulator.RenderNextFrame();
			if (emulator.StatusText.Contains("AMIGA_BOOT_UNSUPPORTED_OPCODE", StringComparison.Ordinal) ||
				emulator.StatusText.Contains("AMIGA_BOOT_FAULT", StringComparison.Ordinal))
			{
				break;
			}
		}

		var cubeRegion = CountNonBlackPixels(emulator.Framebuffer, emulator.Width, x0: 0, y0: 0, width: 180, height: 170);
		var strayRightRegion = CountNonBlackPixels(emulator.Framebuffer, emulator.Width, x0: 220, y0: 0, width: 100, height: 170);
		Assert.StartsWith("boot program running:", emulator.StatusText);
		Assert.DoesNotContain("AMIGA_BOOT_UNSUPPORTED_OPCODE", emulator.StatusText);
		Assert.DoesNotContain("AMIGA_BOOT_FAULT", emulator.StatusText);
		Assert.True(cubeRegion > 8_000, $"Expected the filled cube/viewport in the top-left region, saw {cubeRegion} non-black pixels.");
		Assert.True(strayRightRegion < 250, $"Expected the polygon line blits to stay out of the top-right region, saw {strayRightRegion} non-black pixels.");
	}

	[Fact]
	public void MajorMotionDosBootStartsStartupSequenceWhenAvailable()
	{
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Major Motion (1988)(Microdeal).zip");
		if (diskPath == null)
		{
			return;
		}

		var disk = AmigaDiskImage.Load(diskPath);
		var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));
		var boot = new AmigaBootController(machine);

		var result = BootFromDiskWithAdjacent(boot, machine, disk, diskPath, maxInstructions: 250_000);

		var fatalDiagnostics = result.Diagnostics
			.Where(diagnostic => diagnostic.Code is "AMIGA_BOOT_UNSUPPORTED_OPCODE" or "AMIGA_BOOT_FAULT" or "AMIGA_BOOT_PROTECTED_DISK_UNSUPPORTED")
			.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")
			.ToArray();
		Assert.True(fatalDiagnostics.Length == 0, string.Join(Environment.NewLine, fatalDiagnostics));
		Assert.Contains(result.Diagnostics, diagnostic =>
			diagnostic.Code == "AMIGA_BOOT_DOS_AUTOSTART" &&
			diagnostic.Message.Contains("startup-sequence command major", StringComparison.OrdinalIgnoreCase));
		Assert.NotEqual(0u, machine.Cpu.State.ProgramCounter);
		Assert.NotEqual(AmigaBootController.BootEntryAddress, machine.Cpu.State.ProgramCounter);
	}

	[Fact]
	public void MajorMotionKickstartBootDoesNotLeakWorkbenchBitplanesAtBottomWhenAvailable()
	{
		if (!string.Equals(
			Environment.GetEnvironmentVariable("COPPERSCREEN_MAJOR_MOTION_BOOT_DIAGNOSTICS"),
			"1",
			StringComparison.Ordinal))
		{
			return;
		}

		var romPath = TryFindWorkspaceFile("CopperScreen", "ROM", "Kickstart_13.rom");
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "OK", "Major Motion (1988)(Microdeal).zip") ??
			TryFindWorkspaceFile("CopperScreen", "TestImages", "Major Motion (1988)(Microdeal).zip");
		if (romPath == null || diskPath == null)
		{
			return;
		}

		var emulator = CopperScreenEmulator.Create(new[] { "--profile", "expanded-kickstart13", diskPath }, AppContext.BaseDirectory);
		var machine = GetMachine(emulator);
		var observedWrappedWindow = false;
		var observedFrame = -1;
		var worstUnexpectedPixels = 0;
		var worstDominantColor = 0;
		var worstDominantCount = 0;
		var worstDisplay = emulator.DisplaySnapshot;
		for (var frame = 0; frame < 700; frame++)
		{
			emulator.RenderNextFrame();
			var display = emulator.DisplaySnapshot;
			if (ContainsFatalBootStatus(emulator.StatusText))
			{
				break;
			}

			if (display.DiwStart != 0x0581 || display.DiwStop != 0x40C1)
			{
				continue;
			}

			observedWrappedWindow = true;
			observedFrame = frame;
			var unexpectedPixels = CountPixelsOutsideDominantColor(
				emulator.Framebuffer,
				emulator.Width,
				x0: 80,
				y0: emulator.Height - 72,
				width: emulator.Width - 160,
				height: 56,
				out var dominantColor,
				out var dominantCount);
			if (unexpectedPixels > worstUnexpectedPixels)
			{
				worstUnexpectedPixels = unexpectedPixels;
				worstDominantColor = dominantColor;
				worstDominantCount = dominantCount;
				worstDisplay = display;
			}

			if (frame > 450)
			{
				break;
			}
		}

		Assert.StartsWith("boot program running:", emulator.StatusText);
		Assert.DoesNotContain("AMIGA_BOOT_FAULT", emulator.StatusText);
		Assert.DoesNotContain("AMIGA_BOOT_UNSUPPORTED_OPCODE", emulator.StatusText);
		Assert.True(observedWrappedWindow, $"Expected to observe Major Motion's wrapped PAL DIW state; status='{emulator.StatusText}', PC=0x{machine.Cpu.State.ProgramCounter:X6}, display={BuildBitplaneColorDiagnostic(emulator.DisplaySnapshot)}, diw=0x{emulator.DisplaySnapshot.DiwStart:X4}/0x{emulator.DisplaySnapshot.DiwStop:X4}, ddf=0x{emulator.DisplaySnapshot.DdfStart:X4}/0x{emulator.DisplaySnapshot.DdfStop:X4}, bplcon0=0x{emulator.DisplaySnapshot.Bplcon0:X4}.");
		Assert.True(
			worstUnexpectedPixels <= 24,
			$"Expected the Major Motion boot screen bottom strip to remain a clean background while wrapped DIW=$0581/$40C1; " +
			$"unexpected={worstUnexpectedPixels}, dominant=0x{worstDominantColor:X8}, dominantCount={worstDominantCount}, " +
			$"frame={observedFrame}, status='{emulator.StatusText}', PC=0x{machine.Cpu.State.ProgramCounter:X6}, " +
			$"display={BuildBitplaneColorDiagnostic(worstDisplay)}.");
	}

	[Fact]
	public void Xenon2CrackedBootReachesLoaderWaitWithoutFaultWhenAvailable()
	{
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Xenon 2 - Megablast (1989)(Image Works)(Disk 1 of 2)[cr Band][h Cardinals][t +3 Band].zip");
		if (diskPath == null)
		{
			return;
		}

		var disk = AmigaDiskImage.Load(diskPath);
		var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));
		var boot = new AmigaBootController(machine);

		var result = boot.BootFromDisk(disk, maxInstructions: 250_000, runMode: AmigaBootRunMode.ContinueAfterBootDiskRead);

		var fatalDiagnostics = result.Diagnostics
			.Where(diagnostic => diagnostic.Code is "AMIGA_BOOT_UNSUPPORTED_OPCODE" or "AMIGA_BOOT_FAULT" or "AMIGA_BOOT_PROTECTED_DISK_UNSUPPORTED")
			.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")
			.ToArray();
		Assert.True(fatalDiagnostics.Length == 0, string.Join(Environment.NewLine, fatalDiagnostics));
		Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "AMIGA_BOOT_UNSUPPORTED_IO");
		Assert.Equal(AmigaKickstartHost.ExecLibraryBase, machine.Cpu.State.A[6]);
		Assert.True(machine.Cpu.State.ProgramCounter is >= 0x0005_0000 and < 0x0005_0200, $"Expected Xenon 2 loader wait, PC=0x{machine.Cpu.State.ProgramCounter:X6}.");
	}

	[Fact]
	public void MajorMotionCopperBenchLaunchReceivesVblankInterruptsWhenAvailable()
	{
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Major Motion (1988)(Microdeal).zip");
		if (diskPath == null)
		{
			return;
		}

		var emulator = CopperScreenEmulator.Create(new[] { diskPath }, AppContext.BaseDirectory);
		emulator.RenderNextFrame();
		Assert.True(emulator.IsWorkbenchHandoffPending);
		Assert.True(emulator.LaunchCopperBenchPath("major", out var message), message);

		for (var frame = 0; frame < 360; frame++)
		{
			if (frame is 120 or 210 or 300)
			{
				emulator.PulsePrimaryFire(frames: 35);
			}

			emulator.RenderNextFrame();
		}

		var machine = GetMachine(emulator);
		var disk = machine.Bus.Disk.CaptureSnapshot();
		Assert.StartsWith("boot program running:", emulator.StatusText);
		Assert.DoesNotContain("AMIGA_BOOT_FAULT", emulator.StatusText);
		Assert.DoesNotContain("AMIGA_BOOT_UNSUPPORTED_OPCODE", emulator.StatusText);
		Assert.NotEqual(0x000C_0736u, machine.Cpu.State.ProgramCounter);
		Assert.True(disk.TransferCount > 0, $"Expected Major Motion VBL code to progress into disk DMA; PC=0x{machine.Cpu.State.ProgramCounter:X6}.");
	}

	[Fact]
	public void Xenon2CrackedBootReportsNullPcInsteadOfSilentHangWhenAvailable()
	{
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Xenon 2 - Megablast (1989)(Image Works)(Disk 1 of 2)[cr Band][h Cardinals][t +3 Band].zip");
		if (diskPath == null)
		{
			return;
		}

		var emulator = CopperScreenEmulator.Create(new[] { diskPath }, AppContext.BaseDirectory);
		for (var frame = 0; frame < 520; frame++)
		{
			if (frame == 360)
			{
				emulator.PulsePrimaryFire(frames: 35);
			}

			emulator.RenderNextFrame();
			if (emulator.StatusText.Contains("AMIGA_BOOT_NULL_PC", StringComparison.Ordinal))
			{
				break;
			}
		}

		Assert.Contains("AMIGA_BOOT_NULL_PC", emulator.StatusText);
	}

	[Fact]
	public void Kickstart13RomColdBootStartsAtResetVectorWhenAvailable()
	{
		var romPath = TryFindWorkspaceFile("CopperScreen", "ROM", "Kickstart_13.rom");
		if (romPath == null)
		{
			return;
		}

		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Superfrog (1993)(Team 17)(Disk 1 of 4)[cr CSL].zip");
		if (diskPath == null)
		{
			return;
		}

		var rom = File.ReadAllBytes(romPath);
		var resetPc = BigEndian.ReadUInt32(rom, 4, "Kickstart reset program counter");
		var options = AmigaMachineOptions
			.ForProfile(AmigaMachineProfile.A500Pal512KBoot)
			.WithKickstart(AmigaKickstartConfiguration.FromRomImage(AmigaKickstartVersion.Kickstart13, rom));
		var machine = new AmigaMachine(options);
		var boot = new AmigaBootController(machine);

		boot.StartKickstartRomBoot(AmigaDiskImage.Load(diskPath));

		Assert.Equal(resetPc, machine.Cpu.State.ProgramCounter);
		Assert.Equal(resetPc, machine.Bus.ReadLong(0x0000_0004));
		Assert.Equal(resetPc, machine.Bus.ReadLong(0x00FC_0004));
	}

	[Fact]
	public void SuperfrogCslKickstart13RomLoadsAndEntersBootBlockWhenAvailable()
	{
		var romPath = TryFindWorkspaceFile("CopperScreen", "ROM", "Kickstart_13.rom");
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Superfrog (1993)(Team 17)(Disk 1 of 4)[cr CSL].zip");
		if (romPath == null || diskPath == null)
		{
			return;
		}

		var options = AmigaMachineOptions
			.ForProfile(AmigaMachineProfile.A500Pal512KBoot)
			.WithKickstart(AmigaKickstartConfiguration.FromRomImage(AmigaKickstartVersion.Kickstart13, File.ReadAllBytes(romPath)));
		var machine = new AmigaMachine(options);
		var boot = new AmigaBootController(machine);
		var disk = AmigaDiskImage.Load(diskPath);
		var bootBlock = disk.BootBlock.ToArray();

		boot.StartKickstartRomBoot(disk);
		AmigaDiskControllerSnapshot? firstTransfer = null;
		uint? bootBlockAddress = null;
		var enteredBootBlock = false;
		var fatalDiagnostic = string.Empty;
		for (var i = 0; i < 20_000_000 && !machine.Cpu.State.Halted; i++)
		{
			var pc = machine.Cpu.State.ProgramCounter;
			if (bootBlockAddress.HasValue && pc >= bootBlockAddress.Value && pc < bootBlockAddress.Value + bootBlock.Length)
			{
				enteredBootBlock = true;
				break;
			}

			try
			{
				machine.Cpu.ExecuteInstruction();
			}
			catch (UnsupportedM68kOpcodeException ex)
			{
				fatalDiagnostic = $"AMIGA_BOOT_UNSUPPORTED_OPCODE: {ex.Message}";
				break;
			}
			catch (AmigaEmulationException ex)
			{
				fatalDiagnostic = $"AMIGA_BOOT_FAULT: {ex.Message}";
				break;
			}

			machine.Bus.AdvanceRasterTo(machine.Cpu.State.Cycles);
			machine.Bus.AdvanceCiasTo(machine.Cpu.State.Cycles);
			machine.Bus.AdvanceDmaTo(machine.Cpu.State.Cycles);
			machine.DispatchPendingHardwareInterrupt();

			var snapshot = machine.Bus.Disk.CaptureSnapshot();
			firstTransfer ??= snapshot.TransferCount > 0 ? snapshot : null;
			if (firstTransfer.HasValue && !bootBlockAddress.HasValue && (i % 10_000) == 0)
			{
				bootBlockAddress = FindBootBlockCopy(machine.Bus, bootBlock);
			}
		}

		Assert.True(string.IsNullOrEmpty(fatalDiagnostic), fatalDiagnostic);
		Assert.True(firstTransfer.HasValue, "Expected Kickstart to initiate at least one DF0 disk DMA transfer.");
		Assert.True(firstTransfer.Value.LastTransferWords > 0);
		Assert.True(bootBlockAddress.HasValue, "Expected Kickstart to decode the inserted disk bootblock into memory.");
		Assert.Equal(BigEndian.ReadUInt32(bootBlock, 0, "source bootblock signature"), machine.Bus.ReadLong(bootBlockAddress.Value));
		Assert.Equal(BigEndian.ReadUInt32(bootBlock, 4, "source bootblock checksum"), machine.Bus.ReadLong(bootBlockAddress.Value + 4));
		Assert.True(enteredBootBlock, $"Expected execution to reach the loaded bootblock at 0x{bootBlockAddress.Value:X6}; PC=0x{machine.Cpu.State.ProgramCounter:X6}.");
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
		Assert.NotEmpty(emulator.Framebuffer);
	}

	[Theory]
	[InlineData("Hired Guns v1.08.39.25 (1993-09-24)(Psygnosis)(M5)(Disk 1 of 5).zip")]
	[InlineData("Hired Guns v1.08.39.25 (1993-09-24)(Psygnosis)(M5)(Disk 1 of 5)[cr Loons][f ATX].zip")]
	public void HiredGunsWorkbenchBootStartsSystemTakeoverAndOpensMainExecutableWithoutEarlyCpuFaultWhenAvailable(string fileName)
	{
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", fileName);
		if (diskPath == null)
		{
			return;
		}

		var disk = AmigaDiskImage.Load(diskPath);
		var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));
		var boot = new AmigaBootController(machine);

		var result = boot.BootFromDisk(disk, maxInstructions: 250_000, runMode: AmigaBootRunMode.ContinueAfterBootDiskRead);
		for (var slice = 0; slice < 240 && !machine.Cpu.State.Halted; slice++)
		{
			if (result.Diagnostics.Any(diagnostic =>
				diagnostic.Code == "AMIGA_BOOT_DOS_OPEN" &&
				diagnostic.Message.Contains(":Hired Guns", StringComparison.OrdinalIgnoreCase)))
			{
				break;
			}

			result = boot.ContinueExecution(maxInstructions: 25_000);
		}

		var fatalDiagnostics = result.Diagnostics
			.Where(diagnostic => diagnostic.Code is "AMIGA_BOOT_UNSUPPORTED_OPCODE" or "AMIGA_BOOT_FAULT" or "AMIGA_BOOT_PROTECTED_DISK_UNSUPPORTED")
			.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")
			.ToArray();
		var diagnosticReport = string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
		Assert.True(fatalDiagnostics.Length == 0, string.Join(Environment.NewLine, fatalDiagnostics));
		Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "AMIGA_BOOT_DOS_AUTOSTART");
		Assert.True(result.Diagnostics.Any(diagnostic =>
			diagnostic.Code == "AMIGA_BOOT_DOS_OPEN" &&
			diagnostic.Message.Contains(":Hired Guns", StringComparison.OrdinalIgnoreCase)), diagnosticReport);
		Assert.False(machine.Cpu.State.Halted);
	}

	[Fact]
	public void HiredGunsCrackedBootDoesNotEnterRawDiskRedRetryLoopWhenAvailable()
	{
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Hired Guns v1.08.39.25 (1993-09-24)(Psygnosis)(M5)(Disk 1 of 5)[cr Loons][f ATX].zip");
		if (diskPath == null)
		{
			return;
		}

		if (!HasCompleteAdjacentDiskSet(diskPath))
		{
			return;
		}

		var disk = AmigaDiskImage.Load(diskPath);
		var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));
		var boot = new AmigaBootController(machine);
		var result = BootFromDiskWithAdjacent(boot, machine, disk, diskPath, maxInstructions: 250_000);

		for (var frame = 0; frame < 269 && !machine.Cpu.State.Halted; frame++)
		{
			result = boot.ContinueExecutionUntilCycle((frame + 1) * (long)Math.Round(AmigaConstants.A500PalCpuClockHz / AmigaConstants.A500PalVBlankHz), maxInstructions: 100_000);
		}

		var enteredRedRetryLoop = false;
		for (var i = 0; i < 200_000 && !machine.Cpu.State.Halted; i++)
		{
			var beforePc = machine.Cpu.State.ProgramCounter;
			if (beforePc == 0x00C1F960)
			{
				enteredRedRetryLoop = true;
				break;
			}

			machine.Cpu.ExecuteInstruction();
			machine.Bus.AdvanceRasterTo(machine.Cpu.State.Cycles);
			machine.Bus.AdvanceCiasTo(machine.Cpu.State.Cycles);
			machine.Bus.AdvanceDmaTo(machine.Cpu.State.Cycles);
			machine.DispatchPendingHardwareInterrupt();
		}

		var snapshot = machine.Bus.Disk.CaptureSnapshot();
		var diagnostics = string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
		Assert.False(enteredRedRetryLoop);
		Assert.False(machine.Cpu.State.Halted, diagnostics);
		if (IsWaitingOnUnavailableExternalDrive(machine))
		{
			return;
		}

		Assert.True(
			snapshot.TransferCount > 10,
			$"Expected the raw loader to perform repeated disk transfers; saw {snapshot.TransferCount}. " +
			$"DF0 selected={snapshot.Selected}, motor={snapshot.MotorOn}, dsklen=0x{snapshot.Dsklen:X4}, dskbytr=0x{snapshot.Dskbytr:X4}, " +
			$"ciab=0x{snapshot.CiabPortB:X2}, dmacon=0x{machine.Bus.Paula.Dmacon:X4}, adkcon=0x{machine.Bus.Paula.Adkcon:X4}.");
	}

	[Fact]
	public void HiredGunsSystemTakeoverDoesNotShowRedDiskRetryScreenWhenAvailable()
	{
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Hired Guns v1.08.39.25 (1993-09-24)(Psygnosis)(M5)(Disk 1 of 5).zip");
		if (diskPath == null)
		{
			return;
		}

		if (!HasCompleteAdjacentDiskSet(diskPath))
		{
			return;
		}

		var emulator = CopperScreenEmulator.Create(new[] { diskPath }, AppContext.BaseDirectory);
		BootToCopperBenchAndLaunchWorkbenchDefaultTool(emulator, diskPath);
		var redFrame = -1;
		for (var frame = 0; frame < 900; frame++)
		{
			emulator.RenderNextFrame();
			if (CountRedDominantPixels(emulator.Framebuffer) > 40_000)
			{
				redFrame = frame;
				break;
			}
		}

		var machine = GetMachine(emulator);
		var disk = machine.Bus.Disk.CaptureSnapshot();
		Assert.True(
			redFrame == -1,
			$"Hired Guns entered the raw disk retry screen at frame {redFrame}; " +
			$"transfers={disk.TransferCount}, selectedDrive={disk.SelectedDrive}, lastDrive={disk.LastTransferDrive}, " +
			$"ciab=0x{disk.CiabPortB:X2}, dsklen=0x{disk.Dsklen:X4}, dskbytr=0x{disk.Dskbytr:X4}, " +
			$"dmacon=0x{machine.Bus.Paula.Dmacon:X4}, PC=0x{machine.Cpu.State.ProgramCounter:X6}.");
	}

	[Fact]
	public void CopperBenchLaunchInsertsAdjacentExternalDiskWhenConfigured()
	{
		var sourceDiskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Hired Guns v1.08.39.25 (1993-09-24)(Psygnosis)(M5)(Disk 1 of 5).zip");
		if (sourceDiskPath == null)
		{
			return;
		}

		var tempDirectory = Path.Combine(Path.GetTempPath(), "CopperScreenTests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tempDirectory);
		try
		{
			var diskOnePath = Path.Combine(tempDirectory, Path.GetFileName(sourceDiskPath));
			var diskTwoPath = diskOnePath.Replace("Disk 1 of 5", "Disk 2 of 5", StringComparison.OrdinalIgnoreCase);
			File.Copy(sourceDiskPath, diskOnePath);
			File.Copy(sourceDiskPath, diskTwoPath);

			var emulator = CopperScreenEmulator.Create(new[] { diskOnePath }, AppContext.BaseDirectory);
			BootToCopperBenchAndLaunchWorkbenchDefaultTool(emulator, diskOnePath);

			var machine = GetMachine(emulator);
			Assert.Equal(2, machine.Bus.Disk.ConnectedDriveCount);
			Assert.NotNull(machine.Bus.Disk.Drive1.Disk);
		}
		finally
		{
			Directory.Delete(tempDirectory, recursive: true);
		}
	}

	[Fact]
	public void HiredGunsSystemTakeoverUsesSelectedWorkbenchLanguageWhenAvailable()
	{
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Hired Guns v1.08.39.25 (1993-09-24)(Psygnosis)(M5)(Disk 1 of 5).zip");
		if (diskPath == null)
		{
			return;
		}

		var emulator = CopperScreenEmulator.Create(new[] { diskPath }, AppContext.BaseDirectory);
		BootToCopperBenchAndLaunchWorkbenchDefaultTool(emulator, diskPath);
		for (var frame = 0; frame < 700; frame++)
		{
			emulator.RenderNextFrame();
		}

		var machine = (AmigaMachine)typeof(CopperScreenEmulator)
			.GetField("_machine", BindingFlags.Instance | BindingFlags.NonPublic)!
			.GetValue(emulator)!;
		var boot = (AmigaBootController)typeof(CopperScreenEmulator)
			.GetField("_boot", BindingFlags.Instance | BindingFlags.NonPublic)!
			.GetValue(emulator)!;
		var diagnostics = string.Join(Environment.NewLine, boot.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
		Assert.False(machine.Cpu.State.Halted, diagnostics);
		Assert.Equal(0, machine.Bus.ReadByte(0x00C02756));
		Assert.DoesNotContain(boot.Diagnostics, diagnostic => diagnostic.Code is "AMIGA_BOOT_UNSUPPORTED_OPCODE" or "AMIGA_BOOT_FAULT");
	}

	[Fact]
	public void HiredGunsLoadingScreenReachesInterlacedHamTitleWhenAvailable()
	{
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Hired Guns v1.08.39.25 (1993-09-24)(Psygnosis)(M5)(Disk 1 of 5).zip");
		if (diskPath == null)
		{
			return;
		}

		if (!HasCompleteAdjacentDiskSet(diskPath))
		{
			return;
		}

		var emulator = CopperScreenEmulator.Create(new[] { diskPath }, AppContext.BaseDirectory);
		BootToCopperBenchAndLaunchWorkbenchDefaultTool(emulator, diskPath);
		for (var frame = 0; frame < 1250; frame++)
		{
			emulator.RenderNextFrame();
			var candidate = emulator.DisplaySnapshot;
			if ((candidate.Bplcon0 & 0x6804) == 0x6804 && candidate.LastBitplaneNonZeroPixels > 40_000)
			{
				break;
			}
		}

		var snapshot = emulator.DisplaySnapshot;
		Assert.StartsWith("boot program running:", emulator.StatusText);
		if (IsWaitingOnUnavailableExternalDrive(GetMachine(emulator)))
		{
			return;
		}

		Assert.Equal(0x6000, snapshot.Bplcon0 & 0x7000);
		Assert.Equal(0x0804, snapshot.Bplcon0 & 0x0804);
		Assert.True(snapshot.LastBitplaneNonZeroPixels > 40_000, $"Expected the Hired Guns title/loading screen to be decoded; saw {snapshot.LastBitplaneNonZeroPixels} pixels.");
		Assert.True(snapshot.LastBitplaneMinX <= 4 && snapshot.LastBitplaneMaxX >= 340, $"Expected the loading screen to span the display width; box was {snapshot.LastBitplaneMinX}-{snapshot.LastBitplaneMaxX}.");
		Assert.True(snapshot.LastBitplaneMinY <= 60 && snapshot.LastBitplaneMaxY >= 180, $"Expected the loading screen to span the title area; box was {snapshot.LastBitplaneMinY}-{snapshot.LastBitplaneMaxY}.");
	}

	[Fact]
	public void CrackedDiskPostFireReachesHamDisplayWithoutExitingWhenAvailable()
	{
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Full Contact (1991)(Team 17)(Disk 1 of 2)[cr FLT].zip");
		if (diskPath == null)
		{
			return;
		}

		var emulator = CopperScreenEmulator.Create(new[] { "--profile", "vanilla-copperstart", diskPath }, AppContext.BaseDirectory);
		var reachedHamDisplay = false;
		uint? previousLaceChecksum = null;
		var stableLaceFrames = 0;
		for (var frame = 0; frame < 1400; frame++)
		{
			if (frame == 260)
			{
				emulator.PulsePrimaryFire(frames: 30);
			}

			emulator.RenderNextFrame();
			var snapshot = emulator.DisplaySnapshot;
			reachedHamDisplay |= (snapshot.Bplcon0 & 0x6800) == 0x6800 &&
				snapshot.LastBitplaneNonZeroPixels > 1000 &&
				snapshot.LastBitplaneMaxY >= 200;
			if (reachedHamDisplay && (snapshot.Bplcon0 & 0x0004) != 0)
			{
				var checksum = Checksum(emulator.Framebuffer);
				if (previousLaceChecksum == checksum)
				{
					stableLaceFrames++;
				}

				previousLaceChecksum = checksum;
				if (stableLaceFrames > 4)
				{
					break;
				}
			}
		}

		Assert.StartsWith("boot program running:", emulator.StatusText);
		Assert.DoesNotContain("AMIGA_BOOT_UNSUPPORTED_OPCODE", emulator.StatusText);
		Assert.DoesNotContain("AMIGA_BOOT_FAULT", emulator.StatusText);
		if (IsWaitingOnUnavailableOrNotReadyDrive(GetMachine(emulator)))
		{
			return;
		}

		Assert.True(reachedHamDisplay, "Expected the post-fire loader to reach its six-bitplane HAM display without throwing.");
		Assert.True(stableLaceFrames > 4, "Expected the interlaced HAM title to be presented without alternating-frame jitter.");
	}

	[Fact]
	public void CrackedDiskPostFireHamIntroDoesNotRenderSpritesAfterCylinderFortyWhenAvailable()
	{
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Full Contact (1991)(Team 17)(Disk 1 of 2)[cr FLT].zip");
		if (diskPath == null)
		{
			return;
		}

		var emulator = CopperScreenEmulator.Create(new[] { "--profile", "vanilla-copperstart", diskPath }, AppContext.BaseDirectory);
		var machine = GetMachine(emulator);
		var observedFrames = 0;
		var maxCylinder = 0;
		var firstCylinderFortyFrame = -1;
		var firstHamFrame = -1;
		var bestBitplanePixels = 0;
		var lastDisk = machine.Bus.Disk.CaptureSnapshot();
		var lastDisplay = emulator.DisplaySnapshot;
		for (var frame = 0; frame < 2000 && observedFrames < 24; frame++)
		{
			if (frame == 260)
			{
				emulator.PulsePrimaryFire(frames: 30);
			}

			emulator.RenderNextFrame();
			var display = emulator.DisplaySnapshot;
			var disk = machine.Bus.Disk.CaptureSnapshot();
			lastDisk = disk;
			lastDisplay = display;
			if (disk.LastTransferCylinder > maxCylinder)
			{
				maxCylinder = disk.LastTransferCylinder;
			}

			if (firstCylinderFortyFrame < 0 && disk.LastTransferCylinder >= 40)
			{
				firstCylinderFortyFrame = frame;
			}

			if (display.LastBitplaneNonZeroPixels > bestBitplanePixels)
			{
				bestBitplanePixels = display.LastBitplaneNonZeroPixels;
			}

			if (firstHamFrame < 0 &&
				(display.Bplcon0 & 0x6800) == 0x6800 &&
				display.LastBitplaneNonZeroPixels > 1000 &&
				display.LastBitplaneMaxY >= 200)
			{
				firstHamFrame = frame;
			}

			if (disk.LastTransferCylinder < 40 ||
				(display.Bplcon0 & 0x6800) != 0x6800 ||
				display.LastBitplaneNonZeroPixels <= 1000 ||
				display.LastBitplaneMaxY < 200)
			{
				continue;
			}

			observedFrames++;
			Assert.True(
				display.LastSpriteNonZeroPixels == 0,
				$"Expected Full Contact's post-cylinder-40 HAM intro to be sprite-silent; frame={frame}, observed={observedFrames}, last={disk.LastTransferCylinder}.{disk.LastTransferHead}@0x{disk.LastTransferAddress:X6}, bplcon0=0x{display.Bplcon0:X4}, dmacon=0x{machine.Bus.Paula.Dmacon:X4}, spriteBox={display.LastSpriteMinX},{display.LastSpriteMinY}-{display.LastSpriteMaxX},{display.LastSpriteMaxY}, spriteDma={display.LastSpriteDmaFetches}, missedSpriteSlots={display.LastMissedSpriteDmaSlots}, PC=0x{machine.Cpu.State.ProgramCounter:X6}.");
		}

		Assert.StartsWith("boot program running:", emulator.StatusText);
		Assert.DoesNotContain("AMIGA_BOOT_UNSUPPORTED_OPCODE", emulator.StatusText);
		Assert.DoesNotContain("AMIGA_BOOT_FAULT", emulator.StatusText);
		if (IsWaitingOnUnavailableOrNotReadyDrive(machine))
		{
			return;
		}

		Assert.True(
			observedFrames >= 24,
			$"Expected to observe the post-cylinder-40 HAM intro for at least 24 frames; observed={observedFrames}, maxCylinder={maxCylinder}, firstCylinder40Frame={firstCylinderFortyFrame}, firstHamFrame={firstHamFrame}, bestBitplanePixels={bestBitplanePixels}, last={lastDisk.LastTransferCylinder}.{lastDisk.LastTransferHead}@0x{lastDisk.LastTransferAddress:X6}, bplcon0=0x{lastDisplay.Bplcon0:X4}, bitplanePixels={lastDisplay.LastBitplaneNonZeroPixels}, bitplaneBox={lastDisplay.LastBitplaneMinX},{lastDisplay.LastBitplaneMinY}-{lastDisplay.LastBitplaneMaxX},{lastDisplay.LastBitplaneMaxY}, spritePixels={lastDisplay.LastSpriteNonZeroPixels}.");
	}

	[Fact]
	public void RealKickstartCrackedDiskHamIntroDoesNotRenderSpritePointerDataAsControlBlocksWhenAvailable()
	{
		var romPath = TryFindWorkspaceFile("CopperScreen", "ROM", "Kickstart_13.rom");
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Full Contact (1991)(Team 17)(Disk 1 of 2)[cr FLT].zip");
		if (romPath == null || diskPath == null)
		{
			return;
		}

		var emulator = CopperScreenEmulator.Create(new[] { "--profile", "expanded-kickstart13", diskPath }, AppContext.BaseDirectory);
		var machine = GetMachine(emulator);
		var firePulsed = false;
		var observedFrames = 0;
		var lastDisk = machine.Bus.Disk.CaptureSnapshot();
		var lastDisplay = emulator.DisplaySnapshot;
		for (var frame = 0; frame < 2300 && observedFrames < 24; frame++)
		{
			emulator.RenderNextFrame();
			var cpu = emulator.CpuState;
			if (!firePulsed && cpu.ProgramCounter is >= 0x04D200 and <= 0x04D240)
			{
				emulator.PulsePrimaryFire(frames: 30);
				firePulsed = true;
			}

			var display = emulator.DisplaySnapshot;
			var disk = machine.Bus.Disk.CaptureSnapshot();
			lastDisk = disk;
			lastDisplay = display;
			if (!firePulsed ||
				disk.LastTransferCylinder < 11 ||
				disk.LastTransferCylinder > 22 ||
				(display.Bplcon0 & 0x6800) != 0x6800 ||
				display.LastBitplaneNonZeroPixels <= 10_000 ||
				display.LastBitplaneMaxY < 200)
			{
				continue;
			}

			observedFrames++;
			Assert.True(
				display.LastSpriteNonZeroPixels == 0,
				$"Expected Full Contact's real-Kickstart HAM intro to keep Copper-written sprite pointers from becoming DMA control blocks; frame={frame}, observed={observedFrames}, last={disk.LastTransferCylinder}.{disk.LastTransferHead}@0x{disk.LastTransferAddress:X6}, bplcon0=0x{display.Bplcon0:X4}, bplcon2=0x{display.Bplcon2:X4}, dmacon=0x{machine.Bus.Paula.Dmacon:X4}, spriteBox={display.LastSpriteMinX},{display.LastSpriteMinY}-{display.LastSpriteMaxX},{display.LastSpriteMaxY}, spriteDma={display.LastSpriteDmaFetches}, missedSpriteSlots={display.LastMissedSpriteDmaSlots}, PC=0x{machine.Cpu.State.ProgramCounter:X6}.");
		}

		Assert.StartsWith("boot program running:", emulator.StatusText);
		Assert.DoesNotContain("AMIGA_BOOT_UNSUPPORTED_OPCODE", emulator.StatusText);
		Assert.DoesNotContain("AMIGA_BOOT_FAULT", emulator.StatusText);
		if (IsWaitingOnUnavailableOrNotReadyDrive(machine))
		{
			return;
		}

		Assert.True(firePulsed, "Expected to reach the FLT fire wait loop before entering Full Contact.");
		Assert.True(
			observedFrames >= 12,
			$"Expected to observe Full Contact's real-Kickstart HAM intro around cylinder 11-22; observed={observedFrames}, last={lastDisk.LastTransferCylinder}.{lastDisk.LastTransferHead}@0x{lastDisk.LastTransferAddress:X6}, bplcon0=0x{lastDisplay.Bplcon0:X4}, bitplanePixels={lastDisplay.LastBitplaneNonZeroPixels}, spritePixels={lastDisplay.LastSpriteNonZeroPixels}.");
	}

	[Fact]
	public void LemmingsCrackedBootContinuesAfterMouseFireWhenAvailable()
	{
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Lemmings (1991)(Psygnosis)(Disk 1 of 2)[cr SR].zip");
		if (diskPath == null)
		{
			return;
		}

		var emulator = CopperScreenEmulator.Create(new[] { diskPath }, AppContext.BaseDirectory);
		var machine = GetMachine(emulator);
		var loadedMain = false;
		for (var frame = 0; frame < 360; frame++)
		{
			emulator.SetMouseButtons(primaryFirePressed: frame is >= 180 and < 240, secondFirePressed: false);
			emulator.RenderNextFrame();

			var frameDisk = machine.Bus.Disk.CaptureSnapshot();
			loadedMain = frameDisk.TransferCount >= 7 &&
				machine.Bus.ReadWord(0x00000400) == 0x2C7C &&
				machine.Bus.ReadLong(0x00000402) == 0x00DFF000;
			if (loadedMain)
			{
				break;
			}

			Assert.DoesNotContain("AMIGA_BOOT_UNSUPPORTED_OPCODE", emulator.StatusText);
			Assert.DoesNotContain("AMIGA_BOOT_FAULT", emulator.StatusText);
		}

		var disk = machine.Bus.Disk.CaptureSnapshot();
		var diagnostics = GetDiagnostics(emulator);
		var diagnosticReport = string.Join(Environment.NewLine, diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
		Assert.StartsWith("boot program running:", emulator.StatusText);
		Assert.DoesNotContain("AMIGA_BOOT_UNSUPPORTED_OPCODE", emulator.StatusText);
		Assert.DoesNotContain("AMIGA_BOOT_FAULT", emulator.StatusText);
		if (IsWaitingOnUnavailableExternalDrive(machine))
		{
			return;
		}

		Assert.True(loadedMain, $"Expected post-fire loader to load the main program from both disk sides; transfers={disk.TransferCount}, last={disk.LastTransferCylinder}.{disk.LastTransferHead}@0x{disk.LastTransferAddress:X6}, selected={disk.Selected}, motor={disk.MotorOn}, ciab=0x{disk.CiabPortB:X2}, dsklen=0x{disk.Dsklen:X4}, dskbytr=0x{disk.Dskbytr:X4}, dmacon=0x{machine.Bus.Paula.Dmacon:X4}, adkcon=0x{machine.Bus.Paula.Adkcon:X4}, PC=0x{machine.Cpu.State.ProgramCounter:X6}.{Environment.NewLine}{diagnosticReport}");
	}

	[Fact]
	public void DesertStrikeKickstart13BootContinuesPastCylinderTwentyThreeAfterMouseFireWhenAvailable()
	{
		var romPath = TryFindWorkspaceFile("CopperScreen", "ROM", "Kickstart_13.rom");
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Desert Strike - Return to the Gulf (1993)(Electronic Arts)(Disk 1 of 3).zip");
		if (romPath == null || diskPath == null)
		{
			return;
		}

		var emulator = CopperScreenEmulator.Create(new[] { "--profile", "expanded-kickstart13", diskPath }, AppContext.BaseDirectory);
		var machine = GetMachine(emulator);
		var maxCylinder = -1;
		const int TitleMouseFireFrame = 1_800;
		const int TitleMouseFireFrames = 60;
		var firstCylinderSeventeenFrame = -1;
		var firstFireFrame = -1;
		var lastProgressFrame = 0;
		var lastProgressTransferCount = 0;
		string? lastProgress = null;

		for (var frame = 0; frame < 5_000; frame++)
		{
			var firePressed = frame is >= TitleMouseFireFrame and < TitleMouseFireFrame + TitleMouseFireFrames;
			if (firePressed && firstFireFrame < 0)
			{
				firstFireFrame = frame;
			}

			emulator.SetMouseButtons(firePressed, secondFirePressed: false);
			emulator.SetJoystickPort(
				up: false,
				down: false,
				left: false,
				right: false,
				primaryFirePressed: false,
				secondFirePressed: false);
			emulator.RenderNextFrame();

			var disk = machine.Bus.Disk.CaptureSnapshot();
			if (disk.LastTransferCylinder > maxCylinder)
			{
				maxCylinder = disk.LastTransferCylinder;
			}

			if (firstCylinderSeventeenFrame < 0 && disk.LastTransferCylinder >= 17)
			{
				firstCylinderSeventeenFrame = frame;
			}

			if (disk.TransferCount != lastProgressTransferCount)
			{
				lastProgressFrame = frame;
				lastProgressTransferCount = disk.TransferCount;
				lastProgress = $"{disk.LastTransferDrive}:{disk.LastTransferCylinder}.{disk.LastTransferHead}@0x{disk.LastTransferAddress:X6}";
			}

			if (disk.LastTransferCylinder > 23)
			{
				emulator.SetMouseButtons(primaryFirePressed: false, secondFirePressed: false);
				emulator.SetJoystickPort(
					up: false,
					down: false,
					left: false,
					right: false,
					primaryFirePressed: false,
					secondFirePressed: false);
				Assert.False(ContainsFatalBootStatus(emulator.StatusText), emulator.StatusText);
				return;
			}

			if (ContainsFatalBootStatus(emulator.StatusText))
			{
				break;
			}
		}

		var finalDisk = machine.Bus.Disk.CaptureSnapshot();
		var pc = machine.Cpu.State.ProgramCounter & 0x00FF_FFFF;
		var opcode = machine.Bus.ReadWord(pc);
		var intreq = machine.Bus.ReadWord(0x00DFF01E);
		var intena = machine.Bus.ReadWord(0x00DFF01C);
		var dmaconr = machine.Bus.ReadWord(0x00DFF002);
		emulator.SetMouseButtons(primaryFirePressed: false, secondFirePressed: false);
		emulator.SetJoystickPort(
			up: false,
			down: false,
			left: false,
			right: false,
			primaryFirePressed: false,
			secondFirePressed: false);
		Assert.True(
			maxCylinder > 23,
			$"Expected Desert Strike to continue past cylinder 23 after mouse fire at frame 1800; maxCylinder={maxCylinder}, firstFireFrame={firstFireFrame}, firstCylinder17Frame={firstCylinderSeventeenFrame}, lastProgressFrame={lastProgressFrame}, lastProgress={lastProgress}, last={finalDisk.LastTransferDrive}:{finalDisk.LastTransferCylinder}.{finalDisk.LastTransferHead}@0x{finalDisk.LastTransferAddress:X6}, transfers={finalDisk.TransferCount}, selected={finalDisk.SelectedDrive}, active={finalDisk.ActiveDmaDrive}/{finalDisk.ActiveDma}, dsklen=0x{finalDisk.Dsklen:X4}, dskbytr=0x{finalDisk.Dskbytr:X4}, dsksync=0x{finalDisk.Dsksync:X4}, dmacon=0x{machine.Bus.Paula.Dmacon:X4}, dmaconr=0x{dmaconr:X4}, intreq=0x{intreq:X4}, intena=0x{intena:X4}, adkcon=0x{machine.Bus.Paula.Adkcon:X4}, ciab=0x{finalDisk.CiabPortB:X2}, pc=0x{pc:X6}, opcode=0x{opcode:X4}, sr=0x{machine.Cpu.State.StatusRegister:X4}, cycles={machine.Cpu.State.Cycles}, halted={machine.Cpu.State.Halted}, stopped={machine.Cpu.State.Stopped}, status={emulator.StatusText}");
	}

	private static void BootToCopperBenchAndLaunchWorkbenchDefaultTool(CopperScreenEmulator emulator, string diskPath)
	{
		for (var frame = 0; frame < 120 && !emulator.IsWorkbenchHandoffPending; frame++)
		{
			emulator.RenderNextFrame();
		}

		Assert.True(emulator.IsWorkbenchHandoffPending, emulator.StatusText);
		Assert.True(emulator.IsPaused);
		Assert.True(emulator.ConsumeCopperBenchRequest());
		var fileSystem = new AmigaDosFileSystem(AmigaDiskImage.Load(diskPath));
		Assert.True(fileSystem.TryResolveWorkbenchDefaultTool(out var projectPath, out var toolPath, out _), "Expected a Workbench default tool on DF0:.");
		Assert.True(emulator.LaunchCopperBenchPath(projectPath, out var message), $"{message} ({projectPath} -> {toolPath})");
		Assert.StartsWith("CopperBench launched ", emulator.StatusText);
	}

	private static string? BuildFatalDiagnosticStatus(IReadOnlyList<AmigaBootDiagnostic> diagnostics)
	{
		var fatalDiagnostics = diagnostics
			.Where(diagnostic => diagnostic.Code is "AMIGA_BOOT_UNSUPPORTED_OPCODE" or "AMIGA_BOOT_FAULT" or "AMIGA_BOOT_PROTECTED_DISK_UNSUPPORTED" or "AMIGA_BOOT_NULL_PC")
			.Select(diagnostic => diagnostic.Code)
			.ToArray();
		return fatalDiagnostics.Length == 0
			? null
			: string.Join(", ", fatalDiagnostics);
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

	private static AmigaMachine GetMachine(CopperScreenEmulator emulator)
	{
		return (AmigaMachine)typeof(CopperScreenEmulator)
			.GetField("_machine", BindingFlags.NonPublic | BindingFlags.Instance)!
			.GetValue(emulator)!;
	}

	private static AmigaBootResult BootFromDiskWithAdjacent(
		AmigaBootController boot,
		AmigaMachine machine,
		AmigaDiskImage disk,
		string diskPath,
		int maxInstructions)
	{
		StartBootFromDiskWithAdjacent(boot, machine, disk, diskPath);
		return boot.ContinueExecution(maxInstructions);
	}

	private static void StartBootFromDiskWithAdjacent(
		AmigaBootController boot,
		AmigaMachine machine,
		AmigaDiskImage disk,
		string diskPath)
	{
		boot.StartBootFromDisk(disk);
		InsertAdjacentExternalDisks(machine, diskPath);
	}

	private static void InsertAdjacentExternalDisks(AmigaMachine machine, string diskPath)
	{
		var adjacentPath = diskPath;
		for (var driveIndex = 1; driveIndex < machine.Bus.Disk.ConnectedDriveCount; driveIndex++)
		{
			adjacentPath = CopperScreenEmulator.ResolveNextDiskPath(adjacentPath);
			if (adjacentPath == null)
			{
				return;
			}

			GetDrive(machine, driveIndex).Insert(AmigaDiskImage.Load(adjacentPath));
		}
	}

	private static bool HasCompleteAdjacentDiskSet(string diskPath)
	{
		var match = Regex.Match(
			Path.GetFileName(diskPath),
			@"Disk\s*(?<number>\d+)\s*of\s*(?<total>\d+)",
			RegexOptions.IgnoreCase);
		if (!match.Success ||
			!int.TryParse(match.Groups["number"].Value, out var number) ||
			!int.TryParse(match.Groups["total"].Value, out var total))
		{
			return false;
		}

		var currentPath = diskPath;
		for (var diskNumber = number + 1; diskNumber <= total; diskNumber++)
		{
			currentPath = CopperScreenEmulator.ResolveNextDiskPath(currentPath);
			if (currentPath == null)
			{
				return false;
			}
		}

		return true;
	}

	private static AmigaFloppyDrive GetDrive(AmigaMachine machine, int driveIndex)
	{
		return driveIndex switch
		{
			0 => machine.Bus.Disk.Drive0,
			1 => machine.Bus.Disk.Drive1,
			2 => machine.Bus.Disk.Drive2,
			3 => machine.Bus.Disk.Drive3,
			_ => throw new ArgumentOutOfRangeException(nameof(driveIndex))
		};
	}

	private static bool IsWaitingOnUnavailableExternalDrive(AmigaMachine machine)
	{
		var disk = machine.Bus.Disk.CaptureSnapshot();
		var selectedLine = GetSelectedDriveLine(disk.CiabPortB);
		if (selectedLine <= 0 || disk.ActiveDma || disk.Dsklen != 0x4000)
		{
			return false;
		}

		return selectedLine >= disk.ConnectedDriveCount ||
			GetDrive(machine, selectedLine).Disk == null;
	}

	private static bool IsWaitingOnUnavailableOrNotReadyDrive(AmigaMachine machine)
	{
		var disk = machine.Bus.Disk.CaptureSnapshot();
		var selectedLine = GetSelectedDriveLine(disk.CiabPortB);
		if (selectedLine < 0 || disk.ActiveDma || disk.Dsklen != 0x4000)
		{
			return false;
		}

		if (selectedLine >= disk.ConnectedDriveCount)
		{
			return true;
		}

		var drive = GetDrive(machine, selectedLine);
		return drive.Disk == null || !drive.MotorOn;
	}

	private static int GetSelectedDriveLine(byte ciabPortB)
	{
		for (var driveIndex = 0; driveIndex < 4; driveIndex++)
		{
			if ((ciabPortB & (1 << (driveIndex + 3))) == 0)
			{
				return driveIndex;
			}
		}

		return -1;
	}

	private static IReadOnlyList<AmigaBootDiagnostic> GetDiagnostics(CopperScreenEmulator emulator)
	{
		var boot = (AmigaBootController)typeof(CopperScreenEmulator)
			.GetField("_boot", BindingFlags.NonPublic | BindingFlags.Instance)!
			.GetValue(emulator)!;
		return boot.Diagnostics;
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

	private static uint? FindBootBlockCopy(AmigaBus bus, byte[] bootBlock)
	{
		var chipOffset = FindBytes(bus.ChipRam, bootBlock);
		if (chipOffset >= 0)
		{
			return (uint)chipOffset;
		}

		var expansionOffset = FindBytes(bus.ExpansionRam, bootBlock);
		return expansionOffset >= 0 ? bus.ExpansionRamBase + (uint)expansionOffset : null;
	}

	private static int FindBytes(byte[] haystack, byte[] needle)
	{
		if (needle.Length == 0 || haystack.Length < needle.Length)
		{
			return -1;
		}

		for (var offset = 0; offset <= haystack.Length - needle.Length; offset++)
		{
			if (haystack[offset] != needle[0])
			{
				continue;
			}

			if (haystack.AsSpan(offset, needle.Length).SequenceEqual(needle))
			{
				return offset;
			}
		}

		return -1;
	}

	private static uint DecodeOddEvenLong(ReadOnlySpan<byte> data, int oddOffset, int evenOffset)
	{
		var odd = BigEndian.ReadUInt32(data, oddOffset, "odd MFM longword");
		var even = BigEndian.ReadUInt32(data, evenOffset, "even MFM longword");
		return ((odd & 0x5555_5555u) << 1) | (even & 0x5555_5555u);
	}

	private static uint Checksum(ReadOnlySpan<int> pixels)
	{
		var checksum = 2166136261u;
		foreach (var pixel in pixels)
		{
			checksum = (checksum ^ unchecked((uint)pixel)) * 16777619u;
		}

		return checksum;
	}

	private static int CountNonBlackPixels(IReadOnlyList<int> framebuffer, int stride, int x0, int y0, int width, int height)
	{
		var count = 0;
		const int Black = unchecked((int)0xFF000000);
		for (var y = y0; y < y0 + height; y++)
		{
			for (var x = x0; x < x0 + width; x++)
			{
				if (framebuffer[(y * stride) + x] != Black)
				{
					count++;
				}
			}
		}

		return count;
	}

	private static int CountPixelsOutsideDominantColor(
		IReadOnlyList<int> framebuffer,
		int stride,
		int x0,
		int y0,
		int width,
		int height,
		out int dominantColor,
		out int dominantCount)
	{
		var colors = new Dictionary<int, int>();
		for (var y = y0; y < y0 + height; y++)
		{
			for (var x = x0; x < x0 + width; x++)
			{
				var pixel = framebuffer[(y * stride) + x];
				colors[pixel] = colors.TryGetValue(pixel, out var count) ? count + 1 : 1;
			}
		}

		var total = width * height;
		var dominant = colors.Count == 0
			? default
			: colors.OrderByDescending(pair => pair.Value).First();
		dominantColor = dominant.Key;
		dominantCount = dominant.Value;
		return total - dominantCount;
	}

	private static string BuildRowProbe(IReadOnlyList<int> framebuffer, int stride, int y, int x0, int width, int step)
	{
		var chars = new char[(width + step - 1) / step];
		for (var i = 0; i < chars.Length; i++)
		{
			var colored = false;
			var whiteOrGray = false;
			for (var dx = 0; dx < step; dx++)
			{
				var x = x0 + (i * step) + dx;
				if (x >= x0 + width)
				{
					break;
				}

				var pixel = unchecked((uint)framebuffer[(y * stride) + x]);
				if ((pixel & 0x00FF_FFFFu) == 0)
				{
					continue;
				}

				var r = (pixel >> 16) & 0xFF;
				var g = (pixel >> 8) & 0xFF;
				var b = pixel & 0xFF;
				if (r != g || g != b)
				{
					colored = true;
				}
				else
				{
					whiteOrGray = true;
				}
			}

			chars[i] = colored ? '*' : whiteOrGray ? '#' : '.';
		}

		return new string(chars);
	}

	private static int CountRedDominantPixels(IReadOnlyList<int> framebuffer)
	{
		var count = 0;
		foreach (var pixel in framebuffer)
		{
			var red = (pixel >> 16) & 0xFF;
			var green = (pixel >> 8) & 0xFF;
			var blue = pixel & 0xFF;
			if (red > 140 && green < 90 && blue < 90)
			{
				count++;
			}
		}

		return count;
	}

	private static bool ContainsFatalBootStatus(string statusText)
	{
		return statusText.Contains("AMIGA_BOOT_UNSUPPORTED_OPCODE", StringComparison.Ordinal) ||
			statusText.Contains("AMIGA_BOOT_FAULT", StringComparison.Ordinal);
	}

	private static ShadowDiskTwoDiagnosticResult RunShadowOfTheBeastDiskTwoDiagnostic(
		string diskPath,
		string? agnusTimingMode,
		bool stopAtCoreScene)
	{
		const int MaxFrames = 7_000;
		const int IdleFramesBeforeFire = 45;
		const int FirePulseFrames = 20;
		var emulator = CopperScreenEmulator.Create(
			CreateShadowOfTheBeastDiskTwoArgs(diskPath, agnusTimingMode),
			AppContext.BaseDirectory);
		var machine = GetMachine(emulator);
		var frame = 0;
		var renderedFrame = 0;
		var firePulses = 0;
		var idleFrames = 0;
		var previousTransferCount = -1;
		var swappedToDiskTwo = false;
		var sawPostSwapTransfer = false;
		var transferCountAtSwap = 0;
		var swapFrame = 0;
		var diskTwoChecksumChanges = 0;
		uint? previousDiskTwoChecksum = null;
		var visual = default(FrameVisualMetrics);
		var reachedCoreScene = false;
		var edgeOraclePassed = false;
		var diskTrace = new List<ShadowDiskTraceFrameEntry>();
		var observedTraceEntries = 0;
		machine.Bus.Disk.ClearDmaTrace();

		for (frame = 1; frame <= MaxFrames; frame++)
		{
			renderedFrame = frame;
			emulator.RenderNextFrame();
			var disk = machine.Bus.Disk.CaptureSnapshot();
			var display = emulator.DisplaySnapshot;
			AppendNewDiskTraceEntries(machine.Bus.Disk, ref observedTraceEntries, frame, diskTrace);

			idleFrames = previousTransferCount == disk.TransferCount && !disk.ActiveDma
				? idleFrames + 1
				: 0;
			previousTransferCount = disk.TransferCount;

			if (!swappedToDiskTwo && disk.LastTransferCylinder >= 69 && idleFrames >= IdleFramesBeforeFire)
			{
				Assert.True(emulator.InsertNextDisk(), "Expected Shadow of the Beast disk 2 to be available for DF0 swap.");
				swappedToDiskTwo = true;
				swapFrame = frame;
				transferCountAtSwap = disk.TransferCount;
				idleFrames = 0;
				continue;
			}

			sawPostSwapTransfer |= swappedToDiskTwo && disk.TransferCount > transferCountAtSwap;
			if (swappedToDiskTwo)
			{
				visual = MeasureFrame(emulator.Framebuffer);
			}

			if (sawPostSwapTransfer)
			{
				if (previousDiskTwoChecksum.HasValue && previousDiskTwoChecksum.Value != visual.Checksum)
				{
					diskTwoChecksumChanges++;
				}

				previousDiskTwoChecksum = visual.Checksum;
			}

			if (!sawPostSwapTransfer && idleFrames >= IdleFramesBeforeFire)
			{
				firePulses++;
				emulator.PulsePrimaryFire(FirePulseFrames);
				idleFrames = 0;
			}

			if (ContainsFatalBootStatus(emulator.StatusText))
			{
				break;
			}

			if (sawPostSwapTransfer &&
				IsShadowOfTheBeastDiskTwoCoreScene(emulator.StatusText, display, visual, diskTwoChecksumChanges))
			{
				reachedCoreScene = true;
				edgeOraclePassed = PassesShadowOfTheBeastDiskTwoEdgeOracle(MeasureShadowEdgeStrips(emulator.Framebuffer));
				if (stopAtCoreScene || edgeOraclePassed)
				{
					break;
				}
			}
		}

		var finalVisual = visual.NonBlackPixels == 0 && visual.DistinctColors == 0
			? MeasureFrame(emulator.Framebuffer)
			: visual;
		var finalEdge = MeasureShadowEdgeStrips(emulator.Framebuffer);
		return new ShadowDiskTwoDiagnosticResult(
			agnusTimingMode ?? "profile",
			renderedFrame,
			firePulses,
			idleFrames,
			swappedToDiskTwo,
			sawPostSwapTransfer,
			reachedCoreScene,
			edgeOraclePassed || PassesShadowOfTheBeastDiskTwoEdgeOracle(finalEdge),
			swapFrame,
			transferCountAtSwap,
			diskTwoChecksumChanges,
			emulator.StatusText,
			finalVisual,
			finalEdge,
			emulator.DisplaySnapshot,
			machine.Bus.Disk.CaptureSnapshot(),
			machine.Bus.Paula.Dmacon,
			machine.Bus.Paula.Adkcon,
			machine.Cpu.State.ProgramCounter,
			diskTrace);
	}

	private static string[] CreateShadowOfTheBeastDiskTwoArgs(string diskPath, string? agnusTimingMode)
	{
		if (string.IsNullOrWhiteSpace(agnusTimingMode))
		{
			return new[] { "--profile", "expanded-copperstart", diskPath };
		}

		return new[] { "--profile", "expanded-copperstart", "--agnus-timing", agnusTimingMode, diskPath };
	}

	private static bool IsShadowOfTheBeastDiskTwoCoreScene(
		string statusText,
		OcsDisplaySnapshot display,
		FrameVisualMetrics visual,
		int diskTwoChecksumChanges)
	{
		return statusText.StartsWith("boot program running:", StringComparison.Ordinal) &&
			!ContainsFatalBootStatus(statusText) &&
			display.LastBitplaneNonZeroPixels > 40_000 &&
			display.LastBitplaneMinX <= 40 &&
			display.LastBitplaneMaxX >= 300 &&
			display.LastBitplaneMaxY >= 200 &&
			display.LastSpriteNonZeroPixels > 16 &&
			visual.NonBlackPixels > 40_000 &&
			visual.DistinctColors >= 32 &&
			diskTwoChecksumChanges >= 5;
	}

	private static bool PassesShadowOfTheBeastDiskTwoEdgeOracle(ShadowEdgeMetrics edge)
	{
		return edge.LeftOuterNonBlackPixels <= 128 &&
			edge.RightOuterNonBlackPixels <= 128 &&
			edge.RightInnerBlackPixels <= 8_000;
	}

	private static string BuildShadowOfTheBeastDiskTwoDiagnostic(ShadowDiskTwoDiagnosticResult result)
	{
		var disk = result.Disk;
		var display = result.Display;
		var diagnosticVisual = result.Visual;
		var edge = result.Edge;
		return
			"Expected Shadow of the Beast IPF to swap DF0 to disk 2 and reach the animated parallax scene core signals. " +
			$"timing={result.TimingMode}, frame={result.Frame}, firePulses={result.FirePulses}, idleFrames={result.IdleFrames}, " +
			$"swappedToDiskTwo={result.SwappedToDiskTwo}, sawPostSwapTransfer={result.SawPostSwapTransfer}, " +
			$"reachedCoreScene={result.ReachedCoreScene}, oldEdgeOracle={result.EdgeOraclePassed}, " +
			$"swapFrame={result.SwapFrame}, transferCountAtSwap={result.TransferCountAtSwap}, " +
			$"status='{result.StatusText}', transfers={disk.TransferCount}, lastDrive={disk.LastTransferDrive}, " +
			$"last={disk.LastTransferCylinder}.{disk.LastTransferHead}@0x{disk.LastTransferAddress:X6}, " +
			$"selectedDrive={disk.SelectedDrive}, activeDma={disk.ActiveDma}, dsklen=0x{disk.Dsklen:X4}, " +
			$"dskbytr=0x{disk.Dskbytr:X4}, ciab=0x{disk.CiabPortB:X2}, " +
			$"bplcon0=0x{display.Bplcon0:X4}, bplcon1=0x{display.Bplcon1:X4}, bplcon2=0x{display.Bplcon2:X4}, " +
			$"bitplanePixels={display.LastBitplaneNonZeroPixels}, " +
			$"bitplaneBox={display.LastBitplaneMinX},{display.LastBitplaneMinY}-{display.LastBitplaneMaxX},{display.LastBitplaneMaxY}, " +
			$"normalPf={display.LastNormalPlayfieldNonZeroPixels} " +
			$"{display.LastNormalPlayfieldMinX},{display.LastNormalPlayfieldMinY}-{display.LastNormalPlayfieldMaxX},{display.LastNormalPlayfieldMaxY}, " +
			$"pf1={display.LastPlayfield1NonZeroPixels} " +
			$"{display.LastPlayfield1MinX},{display.LastPlayfield1MinY}-{display.LastPlayfield1MaxX},{display.LastPlayfield1MaxY}, " +
			$"pf2={display.LastPlayfield2NonZeroPixels} " +
			$"{display.LastPlayfield2MinX},{display.LastPlayfield2MinY}-{display.LastPlayfield2MaxX},{display.LastPlayfield2MaxY}, " +
			$"baseRows=[{string.Join(',', display.BitplaneBaseRows)}], " +
			$"colorIndexes=[{BuildBitplaneColorDiagnostic(display)}], " +
			$"spritePixels={display.LastSpriteNonZeroPixels}, " +
			$"spriteBox={display.LastSpriteMinX},{display.LastSpriteMinY}-{display.LastSpriteMaxX},{display.LastSpriteMaxY}, " +
			$"displayDma={display.LastBitplaneDmaFetches}/{display.LastSpriteDmaFetches}, missedSpriteSlots={display.LastMissedSpriteDmaSlots}, " +
			$"displayDmaCycles={display.LastFirstDisplayDmaCycle}-{display.LastLastDisplayDmaCycle}, " +
			$"edgeLeft={edge.LeftOuterNonBlackPixels}, edgeRight={edge.RightOuterNonBlackPixels}, rightInnerBlack={edge.RightInnerBlackPixels}, " +
			$"nonBlack={diagnosticVisual.NonBlackPixels}, colors={diagnosticVisual.DistinctColors}, " +
			$"checksumChangesAfterDisk2={result.DiskTwoChecksumChanges}, checksum=0x{diagnosticVisual.Checksum:X8}, " +
			$"dmacon=0x{result.Dmacon:X4}, adkcon=0x{result.Adkcon:X4}, PC=0x{result.ProgramCounter:X6}. " +
			BuildShadowDiskTraceDiagnostic(result.DiskTrace, result.TransferCountAtSwap);
	}

	private static string BuildBitplaneColorDiagnostic(OcsDisplaySnapshot display)
	{
		return string.Join(
			",",
			display.BitplaneColorCounts
				.Select((count, index) => (count, index))
				.Where(entry => entry.count > 0)
				.OrderByDescending(entry => entry.count)
				.Take(12)
				.Select(entry => $"{entry.index}:{entry.count}"));
	}

	private static void AppendNewDiskTraceEntries(
		AmigaDiskController disk,
		ref int observedTraceEntries,
		int frame,
		List<ShadowDiskTraceFrameEntry> destination)
	{
		var trace = disk.CaptureDmaTrace();
		if (trace.Length < observedTraceEntries)
		{
			observedTraceEntries = 0;
		}

		for (var index = observedTraceEntries; index < trace.Length; index++)
		{
			destination.Add(new ShadowDiskTraceFrameEntry(frame, trace[index]));
		}

		observedTraceEntries = trace.Length;
	}

	private static string BuildShadowDiskTraceDiagnostic(
		IReadOnlyList<ShadowDiskTraceFrameEntry> trace,
		int transferCountAtSwap)
	{
		if (trace.Count == 0)
		{
			return "diskTrace=empty.";
		}

		var started = trace.Count(entry => entry.Trace.Kind == AmigaDiskDmaTraceKind.Started);
		var completed = trace.Count(entry => entry.Trace.Kind == AmigaDiskDmaTraceKind.Completed);
		var cancelled = trace.Count(entry => entry.Trace.Kind == AmigaDiskDmaTraceKind.Cancelled || entry.Trace.Kind == AmigaDiskDmaTraceKind.Stopped);
		var syncMisses = trace.Count(entry => entry.Trace.Kind == AmigaDiskDmaTraceKind.SyncMissing);
		var postSwapStarts = trace.Count(entry => entry.Trace.Kind == AmigaDiskDmaTraceKind.Started && entry.Trace.TransferCount > transferCountAtSwap);
		var lastEntries = string.Join(
			" | ",
			trace.TakeLast(8).Select(entry =>
				$"f{entry.Frame}:{entry.Trace.Kind}#{entry.Trace.TransferCount} c={entry.Trace.Cycle} " +
				$"d{entry.Trace.Drive} {entry.Trace.Cylinder}.{entry.Trace.Head} " +
				$"src={entry.Trace.SourceBit}/{entry.Trace.TrackBitLength} wait={entry.Trace.SyncWaitBits} " +
				$"words={entry.Trace.TransferredWords}/{entry.Trace.RequestedWords} " +
				$"len=0x{entry.Trace.Dsklen:X4} sync=0x{entry.Trace.Dsksync:X4} " +
				$"adk=0x{entry.Trace.Adkcon:X4} bytr=0x{entry.Trace.Dskbytr:X4} datr=0x{entry.Trace.Dskdatr:X4}"));
		return
			$"diskTrace entries={trace.Count}, started={started}, completed={completed}, cancelled={cancelled}, " +
			$"syncMisses={syncMisses}, postSwapStarts={postSwapStarts}, last=[{lastEntries}].";
	}

	private static ShadowOrientationDiagnosticResult RunShadowOrientationDiagnostic(
		string diskOnePath,
		string diskTwoPath,
		ShadowIpfOrientationVariant variant)
	{
		const int MaxFrames = 7_000;
		const int IdleFramesBeforeFire = 45;
		const int FirePulseFrames = 20;
		var diskOne = LoadIpfOrientationVariant(diskOnePath, variant);
		var diskTwo = LoadIpfOrientationVariant(diskTwoPath, variant);
		var emulator = CopperScreenEmulator.CreateWithLoadedDisk(
			new[] { "--profile", "expanded-copperstart", diskOnePath },
			AppContext.BaseDirectory,
			diskOne);
		var machine = GetMachine(emulator);
		var diskTrace = new List<ShadowDiskTraceFrameEntry>();
		var observedTraceEntries = 0;
		var idleFrames = 0;
		var previousTransferCount = -1;
		var swappedToDiskTwo = false;
		var sawPostSwapTransfer = false;
		var transferCountAtSwap = 0;
		var checksumChanges = 0;
		uint? previousChecksum = null;
		var reachedScene = false;
		var frame = 0;
		machine.Bus.Disk.ClearDmaTrace();

		for (frame = 1; frame <= MaxFrames; frame++)
		{
			emulator.RenderNextFrame();
			var disk = machine.Bus.Disk.CaptureSnapshot();
			AppendNewDiskTraceEntries(machine.Bus.Disk, ref observedTraceEntries, frame, diskTrace);
			idleFrames = previousTransferCount == disk.TransferCount && !disk.ActiveDma
				? idleFrames + 1
				: 0;
			previousTransferCount = disk.TransferCount;

			if (!swappedToDiskTwo && disk.LastTransferCylinder >= 69 && idleFrames >= IdleFramesBeforeFire)
			{
				Assert.True(emulator.InsertLoadedDisk(diskTwoPath, diskTwo, markChanged: true));
				swappedToDiskTwo = true;
				transferCountAtSwap = disk.TransferCount;
				idleFrames = 0;
				continue;
			}

			sawPostSwapTransfer |= swappedToDiskTwo && disk.TransferCount > transferCountAtSwap;
			if (sawPostSwapTransfer)
			{
				var visual = MeasureFrame(emulator.Framebuffer);
				if (previousChecksum.HasValue && previousChecksum.Value != visual.Checksum)
				{
					checksumChanges++;
				}

				previousChecksum = visual.Checksum;
				if (IsShadowOfTheBeastDiskTwoCoreScene(emulator.StatusText, emulator.DisplaySnapshot, visual, checksumChanges))
				{
					reachedScene = true;
					break;
				}
			}

			if (!sawPostSwapTransfer && idleFrames >= IdleFramesBeforeFire)
			{
				emulator.PulsePrimaryFire(FirePulseFrames);
				idleFrames = 0;
			}

			if (ContainsFatalBootStatus(emulator.StatusText))
			{
				break;
			}
		}

		return new ShadowOrientationDiagnosticResult(
			variant,
			frame,
			swappedToDiskTwo,
			sawPostSwapTransfer,
			reachedScene,
			emulator.StatusText,
			BuildShadowTraceSignature(diskTrace, transferCountAtSwap),
			BuildShadowDiskTraceDiagnostic(diskTrace, transferCountAtSwap));
	}

	private static string BuildShadowTraceSignature(IReadOnlyList<ShadowDiskTraceFrameEntry> trace, int transferCountAtSwap)
	{
		return string.Join(
			",",
			trace
				.Where(entry => entry.Trace.Kind is AmigaDiskDmaTraceKind.Started or AmigaDiskDmaTraceKind.Completed)
				.Where(entry => entry.Trace.TransferCount >= transferCountAtSwap)
				.TakeLast(32)
				.Select(entry =>
					$"{entry.Trace.Kind}:{entry.Trace.TransferCount}:{entry.Trace.Drive}:{entry.Trace.Cylinder}.{entry.Trace.Head}:" +
					$"{entry.Trace.RequestedWords}:{entry.Trace.SourceBit}:{entry.Trace.SyncWaitBits}:{entry.Trace.CompletionCycle - entry.Trace.Cycle}"));
	}

	private static AmigaDiskImage LoadIpfOrientationVariant(string path, ShadowIpfOrientationVariant variant)
	{
		if (variant == ShadowIpfOrientationVariant.Production)
		{
			return AmigaDiskImage.Load(path);
		}

		var image = ReadIpfImageBytes(path);
		var options = variant == ShadowIpfOrientationVariant.DataRelative
			? new IpfDecodeOptions { StartAtIndex = false }
			: IpfDecodeOptions.Default;
		var ipf = IpfDecoder.Decode(image, options);
		var tracks = new AmigaEncodedTrack[AmigaDiskImage.TrackCount];
		foreach (var track in ipf.Tracks)
		{
			if ((uint)track.Cylinder >= AmigaDiskImage.CylinderCount ||
				(uint)track.Head >= AmigaDiskImage.HeadCount)
			{
				continue;
			}

			var data = variant switch
			{
				ShadowIpfOrientationVariant.RotateForward => RotateTrackBits(track.Data, track.BitLength, track.StartBit),
				ShadowIpfOrientationVariant.RotateBackward => RotateTrackBits(track.Data, track.BitLength, -track.StartBit),
				_ => track.Data
			};
			tracks[(track.Cylinder * AmigaDiskImage.HeadCount) + track.Head] = new AmigaEncodedTrack(data, track.BitLength);
		}

		for (var index = 0; index < tracks.Length; index++)
		{
			if (tracks[index].BitLength == 0)
			{
				tracks[index] = AmigaEncodedTrack.FromBytes(AmigaDosTrackEncoder.CreateUnformattedTrack());
			}
		}

		return AmigaDiskImage.FromEncodedTracks(tracks, $"{Path.GetFileName(path)}:{variant}");
	}

	private static byte[] ReadIpfImageBytes(string path)
	{
		if (Path.GetExtension(path).Equals(".ipf", StringComparison.OrdinalIgnoreCase))
		{
			return File.ReadAllBytes(path);
		}

		using var archive = ZipFile.OpenRead(path);
		var entry = archive.Entries.Single(entry =>
			!string.IsNullOrEmpty(entry.Name) &&
			entry.Name.EndsWith(".ipf", StringComparison.OrdinalIgnoreCase));
		using var input = entry.Open();
		using var output = new MemoryStream();
		input.CopyTo(output);
		return output.ToArray();
	}

	private static byte[] RotateTrackBits(byte[] source, int bitLength, int shiftBits)
	{
		var rotated = new byte[(bitLength + 7) / 8];
		shiftBits = Mod(shiftBits, bitLength);
		for (var bit = 0; bit < bitLength; bit++)
		{
			if (((source[bit >> 3] >> (7 - (bit & 7))) & 1) == 0)
			{
				continue;
			}

			var targetBit = (bit + shiftBits) % bitLength;
			rotated[targetBit >> 3] = (byte)(rotated[targetBit >> 3] | (1 << (7 - (targetBit & 7))));
		}

		return rotated;
	}

	private static int Mod(int value, int modulus)
	{
		var result = value % modulus;
		return result < 0 ? result + modulus : result;
	}

	private static FrameVisualMetrics MeasureFrame(IReadOnlyList<int> framebuffer)
	{
		const int Black = unchecked((int)0xFF000000);
		const int ColorLimit = 1024;
		var checksum = 2166136261u;
		var nonBlack = 0;
		var colors = new HashSet<int>();
		foreach (var pixel in framebuffer)
		{
			checksum = (checksum ^ unchecked((uint)pixel)) * 16777619u;
			if (pixel != Black)
			{
				nonBlack++;
			}

			if (colors.Count < ColorLimit)
			{
				colors.Add(pixel);
			}
		}

		return new FrameVisualMetrics(nonBlack, colors.Count, checksum);
	}

	private static ShadowEdgeMetrics MeasureShadowEdgeStrips(IReadOnlyList<int> framebuffer)
	{
		const int Black = unchecked((int)0xFF000000);
		const int Width = AmigaConstants.PalLowResWidth;
		const int Height = AmigaConstants.PalLowResHeight;
		const int OuterStrip = 8;
		const int RightInnerStart = Width - 32;
		const int RightInnerStop = Width - OuterStrip;
		var leftOuter = 0;
		var rightOuter = 0;
		var rightInnerBlack = 0;
		for (var y = 0; y < Height; y++)
		{
			var row = y * Width;
			for (var x = 0; x < OuterStrip; x++)
			{
				if (framebuffer[row + x] != Black)
				{
					leftOuter++;
				}

				if (framebuffer[row + Width - OuterStrip + x] != Black)
				{
					rightOuter++;
				}
			}

			for (var x = RightInnerStart; x < RightInnerStop; x++)
			{
				if (framebuffer[row + x] == Black)
				{
					rightInnerBlack++;
				}
			}
		}

		return new ShadowEdgeMetrics(leftOuter, rightOuter, rightInnerBlack);
	}

	private readonly struct FrameVisualMetrics
	{
		public FrameVisualMetrics(int nonBlackPixels, int distinctColors, uint checksum)
		{
			NonBlackPixels = nonBlackPixels;
			DistinctColors = distinctColors;
			Checksum = checksum;
		}

		public int NonBlackPixels { get; }

		public int DistinctColors { get; }

		public uint Checksum { get; }
	}

	private readonly struct ShadowEdgeMetrics
	{
		public ShadowEdgeMetrics(int leftOuterNonBlackPixels, int rightOuterNonBlackPixels, int rightInnerBlackPixels)
		{
			LeftOuterNonBlackPixels = leftOuterNonBlackPixels;
			RightOuterNonBlackPixels = rightOuterNonBlackPixels;
			RightInnerBlackPixels = rightInnerBlackPixels;
		}

		public int LeftOuterNonBlackPixels { get; }

		public int RightOuterNonBlackPixels { get; }

		public int RightInnerBlackPixels { get; }
	}

	private readonly struct ShadowDiskTwoDiagnosticResult
	{
		public ShadowDiskTwoDiagnosticResult(
			string timingMode,
			int frame,
			int firePulses,
			int idleFrames,
			bool swappedToDiskTwo,
			bool sawPostSwapTransfer,
			bool reachedCoreScene,
			bool edgeOraclePassed,
			int swapFrame,
			int transferCountAtSwap,
			int diskTwoChecksumChanges,
			string statusText,
			FrameVisualMetrics visual,
			ShadowEdgeMetrics edge,
			OcsDisplaySnapshot display,
			AmigaDiskControllerSnapshot disk,
			ushort dmacon,
			ushort adkcon,
			uint programCounter,
			IReadOnlyList<ShadowDiskTraceFrameEntry> diskTrace)
		{
			TimingMode = timingMode;
			Frame = frame;
			FirePulses = firePulses;
			IdleFrames = idleFrames;
			SwappedToDiskTwo = swappedToDiskTwo;
			SawPostSwapTransfer = sawPostSwapTransfer;
			ReachedCoreScene = reachedCoreScene;
			EdgeOraclePassed = edgeOraclePassed;
			SwapFrame = swapFrame;
			TransferCountAtSwap = transferCountAtSwap;
			DiskTwoChecksumChanges = diskTwoChecksumChanges;
			StatusText = statusText;
			Visual = visual;
			Edge = edge;
			Display = display;
			Disk = disk;
			Dmacon = dmacon;
			Adkcon = adkcon;
			ProgramCounter = programCounter;
			DiskTrace = diskTrace;
		}

		public string TimingMode { get; }

		public int Frame { get; }

		public int FirePulses { get; }

		public int IdleFrames { get; }

		public bool SwappedToDiskTwo { get; }

		public bool SawPostSwapTransfer { get; }

		public bool ReachedCoreScene { get; }

		public bool EdgeOraclePassed { get; }

		public int SwapFrame { get; }

		public int TransferCountAtSwap { get; }

		public int DiskTwoChecksumChanges { get; }

		public string StatusText { get; }

		public FrameVisualMetrics Visual { get; }

		public ShadowEdgeMetrics Edge { get; }

		public OcsDisplaySnapshot Display { get; }

		public AmigaDiskControllerSnapshot Disk { get; }

		public ushort Dmacon { get; }

		public ushort Adkcon { get; }

		public uint ProgramCounter { get; }

		public IReadOnlyList<ShadowDiskTraceFrameEntry> DiskTrace { get; }
	}

	private readonly struct ShadowDiskTraceFrameEntry
	{
		public ShadowDiskTraceFrameEntry(int frame, AmigaDiskDmaTraceEntry trace)
		{
			Frame = frame;
			Trace = trace;
		}

		public int Frame { get; }

		public AmigaDiskDmaTraceEntry Trace { get; }
	}

	private enum ShadowIpfOrientationVariant
	{
		Production,
		DataRelative,
		RotateForward,
		RotateBackward
	}

	private readonly struct ShadowOrientationDiagnosticResult
	{
		public ShadowOrientationDiagnosticResult(
			ShadowIpfOrientationVariant variant,
			int frame,
			bool swappedToDiskTwo,
			bool sawPostSwapTransfer,
			bool reachedDiskTwoScene,
			string statusText,
			string traceSignature,
			string traceDiagnostic)
		{
			Variant = variant;
			Frame = frame;
			SwappedToDiskTwo = swappedToDiskTwo;
			SawPostSwapTransfer = sawPostSwapTransfer;
			ReachedDiskTwoScene = reachedDiskTwoScene;
			StatusText = statusText;
			TraceSignature = traceSignature;
			TraceDiagnostic = traceDiagnostic;
		}

		public ShadowIpfOrientationVariant Variant { get; }

		public int Frame { get; }

		public bool SwappedToDiskTwo { get; }

		public bool SawPostSwapTransfer { get; }

		public bool ReachedDiskTwoScene { get; }

		public string StatusText { get; }

		public string TraceSignature { get; }

		public string TraceDiagnostic { get; }

		public string ToDiagnosticString()
		{
			return
				$"{Variant}: frame={Frame}, swapped={SwappedToDiskTwo}, postSwap={SawPostSwapTransfer}, " +
				$"scene={ReachedDiskTwoScene}, status='{StatusText}', signature='{TraceSignature}', {TraceDiagnostic}";
		}
	}

	private static byte[] CreateBranchToSelfBootDisk()
	{
		var data = new byte[AmigaDiskImage.StandardAdfSize];
		data[0] = (byte)'D';
		data[1] = (byte)'O';
		data[2] = (byte)'S';
		data[12] = 0x60;
		data[13] = 0xFE;
		BigEndian.WriteUInt32(data, 4, CalculateBootChecksum(data.AsSpan(0, 1024)));
		return data;
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

}
