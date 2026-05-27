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
	public void PrimaryFirePulseReachesCiaAPortDuringRender()
	{
		var emulator = CopperScreenEmulator.CreateWithoutDisk();

		emulator.PulsePrimaryFire(frames: 1);
		emulator.RenderNextFrame();

		Assert.Equal("insert disk image", emulator.StatusText);
		Assert.False(emulator.IsPrimaryFirePressed);
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
		for (var frame = 0; frame < 860; frame++)
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
			if (frame > 840 && (snapshot.Bplcon0 & 0x0004) != 0)
			{
				var checksum = Checksum(emulator.Framebuffer);
				if (previousLaceChecksum == checksum)
				{
					stableLaceFrames++;
				}

				previousLaceChecksum = checksum;
			}
		}

		Assert.StartsWith("boot program running:", emulator.StatusText);
		Assert.DoesNotContain("AMIGA_BOOT_UNSUPPORTED_OPCODE", emulator.StatusText);
		Assert.DoesNotContain("AMIGA_BOOT_FAULT", emulator.StatusText);
		Assert.True(reachedHamDisplay, "Expected the post-fire loader to reach its six-bitplane HAM display without throwing.");
		Assert.True(stableLaceFrames > 4, "Expected the interlaced HAM title to be presented without alternating-frame jitter.");
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

	private static uint Checksum(ReadOnlySpan<int> pixels)
	{
		var checksum = 2166136261u;
		foreach (var pixel in pixels)
		{
			checksum = (checksum ^ unchecked((uint)pixel)) * 16777619u;
		}

		return checksum;
	}

}
