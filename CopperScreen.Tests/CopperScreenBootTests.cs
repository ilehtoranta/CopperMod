using System.Reflection;
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
		Assert.Equal(0x0101, machine.Bus.ReadWord(0x00DFF00C));
		Assert.NotEqual(0, machine.Bus.ReadByte(0x00BFE001) & 0x40);
		Assert.Equal(0, machine.Bus.ReadByte(0x00BFE001) & 0x80);
		Assert.NotEqual(0, machine.Bus.ReadWord(0x00DFF016) & 0x0400);
		Assert.Equal(0, machine.Bus.ReadWord(0x00DFF016) & 0x4000);
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
			machine.Bus.Paula.AdvanceTo(machine.Cpu.State.Cycles);
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

	private static AmigaMachine GetMachine(CopperScreenEmulator emulator)
	{
		return (AmigaMachine)typeof(CopperScreenEmulator)
			.GetField("_machine", BindingFlags.NonPublic | BindingFlags.Instance)!
			.GetValue(emulator)!;
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
