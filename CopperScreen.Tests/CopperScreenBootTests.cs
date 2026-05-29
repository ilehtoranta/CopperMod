using System.Reflection;
using Avalonia.Input;
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
	public void ExplicitDiskRenderAdvancesToVisibleBootProgramFrame()
	{
		var diskPath = FindWorkspaceFile("CopperScreen", "TestImages", "Full Contact (1991)(Team 17)(Disk 1 of 2).zip");
		var emulator = CopperScreenEmulator.Create(new[] { diskPath }, AppContext.BaseDirectory);

		emulator.RenderNextFrame();

		Assert.Equal("AMIGA_BOOT_PROTECTED_DISK_UNSUPPORTED", emulator.StatusText);
		Assert.True(emulator.Framebuffer.Distinct().Count() > 1);
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

		var result = boot.BootFromDisk(disk, maxInstructions: 250_000, runMode: AmigaBootRunMode.ContinueAfterBootDiskRead);
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
		var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));
		var boot = new AmigaBootController(machine);

		var result = boot.BootFromDisk(disk, maxInstructions: 250_000, runMode: AmigaBootRunMode.ContinueAfterBootDiskRead);
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
		Assert.True(machine.Bus.Disk.CaptureSnapshot().TransferCount > 0);
	}

	[Fact]
	public void CrackedDiskIntroWaitsForFireAndProducesAudioWhenAvailable()
	{
		var diskPath = TryFindWorkspaceFile("CopperScreen", "TestImages", "Full Contact (1991)(Team 17)(Disk 1 of 2)[cr FLT].zip");
		if (diskPath == null)
		{
			return;
		}

		var emulator = CopperScreenEmulator.Create(new[] { diskPath }, AppContext.BaseDirectory);
		var audio = new float[emulator.AudioFramesPerAppFrame(44_100) * 2];
		var peak = 0.0f;
		var quantizedAudioLevels = new HashSet<int>();
		var displayedBitmap = false;
		for (var frame = 0; frame < 300; frame++)
		{
			emulator.RenderNextFrame();
			var snapshot = emulator.DisplaySnapshot;
			displayedBitmap |= snapshot.LastBitplaneNonZeroPixels > 1000 &&
				snapshot.LastBitplaneMinX <= 8 &&
				snapshot.LastBitplaneMaxX >= 300 &&
				snapshot.LastBitplaneMaxY >= 70 &&
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
		Assert.True(displayedBitmap, "Expected the cracktro bitmap to span the visible display while waiting for fire.");
		Assert.True(peak > 0.0001f, $"Expected audible cracktro output, peak was {peak}.");
		Assert.True(quantizedAudioLevels.Count > 16, $"Expected time-varying cracktro audio, got {quantizedAudioLevels.Count} sample levels.");
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
		var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(disk);
		var targetCycle = 0L;
		var palFrameCycles = Math.Max(1, (long)Math.Round(AmigaConstants.A500PalCpuClockHz / AmigaConstants.A500PalVBlankHz));
		for (var frame = 0; frame < 600 && !machine.Cpu.State.Halted; frame++)
		{
			targetCycle += palFrameCycles;
			boot.ContinueExecutionUntilCycle(targetCycle, maxInstructions: 100_000);
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
		var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));
		var boot = new AmigaBootController(machine);
		var reachedDecodedLoader = false;

		var result = boot.BootFromDisk(disk, maxInstructions: 250_000, runMode: AmigaBootRunMode.ContinueAfterBootDiskRead);
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

		var result = boot.BootFromDisk(disk, maxInstructions: 250_000, runMode: AmigaBootRunMode.ContinueAfterBootDiskRead);
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
		if (decodedRawTrack)
		{
			Assert.True(fatalDiagnostics.Length == 0, string.Join(Environment.NewLine, fatalDiagnostics));
		}

		Assert.True(readRawHeader, string.Join(Environment.NewLine, fatalDiagnostics));
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

		var result = boot.BootFromDisk(disk, maxInstructions: 250_000, runMode: AmigaBootRunMode.ContinueAfterBootDiskRead);

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

		var disk = AmigaDiskImage.Load(diskPath);
		var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));
		var boot = new AmigaBootController(machine);
		var result = boot.BootFromDisk(disk, maxInstructions: 250_000, runMode: AmigaBootRunMode.ContinueAfterBootDiskRead);

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
		Assert.True(snapshot.TransferCount > 10, $"Expected the raw loader to perform repeated disk transfers; saw {snapshot.TransferCount}.");
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

		var emulator = CopperScreenEmulator.Create(new[] { diskPath }, AppContext.BaseDirectory);
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
		Assert.True(reachedHamDisplay, "Expected the post-fire loader to reach its six-bitplane HAM display without throwing.");
		Assert.True(stableLaceFrames > 4, "Expected the interlaced HAM title to be presented without alternating-frame jitter.");
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
		Assert.True(loadedMain, $"Expected post-fire loader to load the main program from both disk sides; transfers={disk.TransferCount}, last={disk.LastTransferCylinder}.{disk.LastTransferHead}@0x{disk.LastTransferAddress:X6}, PC=0x{machine.Cpu.State.ProgramCounter:X6}.{Environment.NewLine}{diagnosticReport}");
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
