using System.Reflection;
using CopperMod.Amiga;
using CopperMod.Amiga.CopperStart.Devices.Trackdisk;

namespace CopperMod.Amiga.Tests;

public sealed class AmigaBootMemoryTests
{
	private const uint ChipPublicLowerAddress = 0x0000_0400;
	private const uint PrivateMetadataSize = 0x0000_1000;
	private const uint PseudoFastMetadataSize = 0x0000_0200;
	private const uint KickstartRomPseudoFastReserve = 0x0001_0000;
	private const uint RealFastMetadataSize = 0x0000_0200;
	private const uint PseudoFastCurrentTaskOffset = 0x0000_0100;
	private const uint ChipOnlyMemHeaderOffset = 0x0000_0100;
	private const uint ChipOnlyMemNameOffset = 0x0000_0180;
	private const uint ExecWaitResumeGatewayAddress = 0x00F0_8500;
	private const int ExecSoftVerOffset = 0x22;
	private const int ExecLowMemChkSumOffset = 0x24;
	private const int ExecChkBaseOffset = 0x26;
	private const int ExecColdCaptureOffset = 0x2A;
	private const int ExecCoolCaptureOffset = 0x2E;
	private const int ExecWarmCaptureOffset = 0x32;
	private const int ExecSysStkUpperOffset = 0x36;
	private const int ExecSysStkLowerOffset = 0x3A;
	private const int ExecMaxLocMemOffset = 0x3E;
	private const int ExecMaxExtMemOffset = 0x4E;
	private const int ExecChkSumOffset = 0x52;
	private const int ExecMemListOffset = 0x142;
	private const int ExecResourceListOffset = 0x150;
	private const int ExecDeviceListOffset = 0x15E;
	private const int ExecLibListOffset = 0x17A;
	private const int LibraryVersionOffset = 0x14;
	private const int LibraryOpenCountOffset = 0x20;
	private const int MemNodeNameOffset = 0x0A;
	private const int MemHeaderAttributesOffset = 0x0E;
	private const int MemHeaderFirstChunkOffset = 0x10;
	private const int MemHeaderLowerOffset = 0x14;
	private const int MemHeaderUpperOffset = 0x18;
	private const int MemHeaderFreeOffset = 0x1C;
	private const int ExecThisTaskOffset = 0x114;
	private const int ExecPortListOffset = 0x188;
	private const int ExecTaskReadyOffset = 0x196;
	private const int ExecTaskWaitOffset = 0x1A4;
	private const int TaskSigRecvdOffset = 0x1A;
	private const int TaskSigWaitOffset = 0x16;
	private const int TaskSigExceptOffset = 0x1E;
	private const int TaskStackPointerOffset = 0x36;
	private const int TaskStateOffset = 0x0F;
	private const int SemaphoreNestCountOffset = 0x0E;
	private const int SemaphoreWaitQueueOffset = 0x10;
	private const int SemaphoreOwnerOffset = 0x28;
	private const int SemaphoreQueueCountOffset = 0x2C;
	private const int MessageReplyPortOffset = 0x0E;
	private const int ExecTaskTrapCodeOffset = 0x130;
	private const int MemChunkNextOffset = 0x00;
	private const int MemChunkBytesOffset = 0x04;
	private const int BootIoCommandOffset = 0x1C;
	private const int BootIoErrorOffset = 0x1F;
	private const int BootIoActualOffset = 0x20;
	private const int BootIoLengthOffset = 0x24;
	private const int BootIoDataOffset = 0x28;
	private const int BootIoOffsetOffset = 0x2C;
	private const int TaskTrapAllocOffset = 0x22;
	private const int TaskTrapAbleOffset = 0x24;
	private const int TaskTrapCodeOffset = 0x32;
	private const int InterruptDataOffset = 0x0E;
	private const int InterruptCodeOffset = 0x12;
	private const int VBlankInterruptNumber = 5;
	private const int ScreenWidthOffset = 0x0C;
	private const int ScreenHeightOffset = 0x0E;
	private const int ScreenViewPortOffset = 0x2C;
	private const int ScreenRastPortOffset = 0x54;
	private const int WindowRPortOffset = 0x32;
	private const int WindowIdcmpFlagsOffset = 0x52;
	private const int WindowUserPortOffset = 0x56;
	private const int MsgPortSigBitOffset = 0x0F;
	private const int MsgPortSigTaskOffset = 0x10;
	private const int MsgPortMsgListOffset = 0x14;
	private const int RastPortBitMapOffset = 0x04;
	private const int ScreenBitMapOffset = 0xB8;
	private const int GadgetNextOffset = 0x00;
	private const int GadgetLeftEdgeOffset = 0x04;
	private const int GadgetTopEdgeOffset = 0x06;
	private const int GadgetWidthOffset = 0x08;
	private const int GadgetHeightOffset = 0x0A;
	private const int GadgetIdOffset = 0x26;
	private const int IntuiMessageClassOffset = 0x14;
	private const int IntuiMessageCodeOffset = 0x18;
	private const int IntuiMessageIAddressOffset = 0x1C;
	private const int IntuiMessageMouseXOffset = 0x20;
	private const int IntuiMessageMouseYOffset = 0x22;
	private const int ViewViewPortOffset = 0x00;
	private const int ViewLofCprListOffset = 0x04;
	private const int ViewShfCprListOffset = 0x08;
	private const int ViewStructSize = 0x12;
	private const int BitMapBytesPerRowOffset = 0x00;
	private const int BitMapRowsOffset = 0x02;
	private const int BitMapDepthOffset = 0x05;
	private const int BitMapPlanesOffset = 0x08;
	private const int CprListStartOffset = 0x04;
	private const int NewScreenWidthOffset = 0x04;
	private const int NewScreenHeightOffset = 0x06;
	private const int NewScreenDepthOffset = 0x08;
	private const int NewScreenViewModesOffset = 0x0C;
	private const int ViewPortDspInsOffset = 0x08;
	private const int ViewPortDWidthOffset = 0x18;
	private const int ViewPortDHeightOffset = 0x1A;
	private const int ViewPortModesOffset = 0x20;
	private const int ViewPortRasInfoOffset = 0x24;
	private const ushort ViewModeHires = 0x8000;
	private const uint IdcmpGadgetDown = 0x0000_0020;
	private const uint IdcmpGadgetUp = 0x0000_0040;
	private const uint MemfPublic = 0x0000_0001;
	private const uint MemfChip = 0x0000_0002;
	private const uint MemfFast = 0x0000_0004;
	private const uint MemfClear = 0x0001_0000;

	[Fact]
	public void EmulatorCreatesNoCyberGraphicsLayerWithoutRtgVram()
	{
		var machine = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot));
		var boot = new AmigaBootController(machine);

		Assert.False(boot.HasCyberGraphics);
		Assert.Null(machine.Bus.AutoconfigRtg);
		Assert.False(boot.TryGetRtgComposition(out _));
	}

	[Fact]
	public void EmulatorAttachesCyberGraphicsFirmwareWhenRtgVramIsConfigured()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithCpu(AmigaM68kCoreFactory.Default, M68kBackendKind.AccurateM68040)
			.WithRtgVram(16L * 1024 * 1024));
		var boot = new AmigaBootController(machine);

		Assert.True(boot.HasCyberGraphics);
		Assert.True(Assert.IsType<AutoconfigRtgBoard>(machine.Bus.AutoconfigRtg).HasFirmware);
	}

	[Fact]
	public void HostShimBootInstallsCyberGraphicsDiagnosticResidentAndResolvesOnlyItsExactName()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithCpu(AmigaM68kCoreFactory.Default, M68kBackendKind.AccurateM68040)
			.WithLiveAgnusDma(false)
			.WithRtgVram(16L * 1024 * 1024));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;

		const uint nameAddress = 0x1800;
		WriteCString(bus, nameAddress, "cybergraphics.library");
		var openState = new M68kCpuState();
		openState.A[1] = nameAddress;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -408), openState));

		var libraryBase = openState.D[0];
		Assert.NotEqual(0u, libraryBase);
		Assert.NotEqual(AmigaKickstartHost.GraphicsLibraryBase, libraryBase);
		Assert.Equal(libraryBase, boot.CyberGraphics.LibraryBase);
		Assert.Equal(libraryBase, bus.ReadLong(AmigaKickstartHost.ExecLibraryBase + 0x17A));

		var cyberState = new M68kCpuState { D = { [0] = 0xFFFF_FFFF } };
		Assert.True(InvokeHostTrap(bus, Lvo(libraryBase, -54), cyberState));
		Assert.Equal(0u, cyberState.D[0]);

		WriteCString(bus, nameAddress, "notcybergraphics.library");
		var partialState = new M68kCpuState();
		partialState.A[1] = nameAddress;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -408), partialState));
		Assert.Equal(AmigaKickstartHost.DummyLibraryBase, partialState.D[0]);
	}

	[Fact]
	public void NativeCopperStartTakeoverPreparesRuntimeBoundaryExactlyOnce()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine)
		{
			AutoRunStartupSequence = true,
			AutoStartWorkbenchDefaultTool = true
		};
		boot.StartBootFromDisk(CreateBootableDisk());

		Assert.False(boot.TryPrepareCopperStartRuntimeHandoff());
		CompleteInitialHostTrackdiskRead(machine);
		machine.Cpu.State.ProgramCounter = 0x0000_2000;
		machine.Bus.WriteLong(2 * 4, 0);
		machine.Bus.WriteLong(4, 0);
		machine.Bus.WriteLong((24 + 1) * 4, 0);

		Assert.True(boot.TryPrepareCopperStartRuntimeHandoff());
		Assert.Equal(1, boot.CopperStartRuntimeHandoffCount);
		Assert.NotEqual(0u, machine.Bus.ReadLong(2 * 4));
		Assert.NotEqual(0u, machine.Bus.ReadLong(4));
		Assert.NotEqual(0u, machine.Bus.ReadLong((24 + 1) * 4));
		Assert.False(boot.TryPrepareCopperStartRuntimeHandoff());
		Assert.Equal(1, boot.CopperStartRuntimeHandoffCount);
	}

	[Fact]
	public void NativeCopperStartTakeoverRequiresLoadedProgramAndResetsWithBootState()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		CompleteInitialHostTrackdiskRead(machine);

		machine.Cpu.State.ProgramCounter = 0;
		Assert.False(boot.TryPrepareCopperStartRuntimeHandoff());
		machine.Cpu.State.ProgramCounter = AmigaBootController.BootBlockAddress + 0x20;
		Assert.False(boot.TryPrepareCopperStartRuntimeHandoff());
		machine.Cpu.State.ProgramCounter = 0x0000_2000;
		Assert.True(boot.TryPrepareCopperStartRuntimeHandoff());

		boot.StartBootFromDisk(CreateBootableDisk());
		Assert.Equal(0, boot.CopperStartRuntimeHandoffCount);
		Assert.False(boot.TryPrepareCopperStartRuntimeHandoff());
	}

	[Theory]
	[InlineData("_dosBootContinuationStarted")]
	[InlineData("_startupSequenceActive")]
	[InlineData("_kickstartRomBootActive")]
	public void CopperStartRuntimeTakeoverRejectsActiveHostOrRomOrchestration(string activeStateField)
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		CompleteInitialHostTrackdiskRead(machine);
		machine.Cpu.State.ProgramCounter = 0x0000_2000;
		SetPrivateBoolean(boot, activeStateField, true);

		Assert.False(boot.TryPrepareCopperStartRuntimeHandoff());
		Assert.Equal(0, boot.CopperStartRuntimeHandoffCount);
	}

	[Fact]
	public void CopperStartRuntimeTakeoverRejectsPendingWorkbenchLaunch()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		CompleteInitialHostTrackdiskRead(machine);
		machine.Cpu.State.ProgramCounter = 0x0000_2000;
		var request = new AmigaProgramLaunchRequest(
			"C:Program",
			projectPath: null,
			currentDirectory: string.Empty,
			toolTypes: Array.Empty<string>(),
			stackSize: 4096,
			cliArguments: null);
		typeof(AmigaBootController)
			.GetProperty(nameof(AmigaBootController.PendingWorkbenchLaunchRequest))!
			.SetValue(boot, request);

		Assert.False(boot.TryPrepareCopperStartRuntimeHandoff());
		Assert.Equal(0, boot.CopperStartRuntimeHandoffCount);
	}

	[Fact]
	public void WorkbenchCliArgumentsPreserveNumericLanguageSelection()
	{
		var arguments = AmigaBootController.BuildCliArguments(new[]
		{
			"$CODE=\"Hired Guns Disk 1:Hired Guns\"",
			".DATA=\"Hired Guns Disk 1:C/SystemTakeover.dat\"",
			"0LANGUAGES=ENGLISH,FRENCH,GERMAN,ITALIAN,SPANISH",
			"CHIP=524032",
			"RELOCATE=YES",
			"UNPACK=YES",
			"KILLSYS=YES"
		});

		Assert.Contains("CODE \"Hired Guns Disk 1:Hired Guns\"", arguments);
		Assert.Contains("DATA \"Hired Guns Disk 1:C/SystemTakeover.dat\"", arguments);
		Assert.Contains("CHIP 524032", arguments);
		Assert.Contains("LANGUAGES ENGLISH", arguments);
		Assert.Contains("RELOCATE", arguments);
		Assert.Contains("UNPACK", arguments);
		Assert.Contains("KILLSYS", arguments);
		Assert.DoesNotContain("LANGUAGES ENGLISH,FRENCH", arguments);
	}

	[Fact]
	public void BootShimBuildsKickstartStyleMemListWithPseudoFastFirst()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var listAddress = AmigaKickstartHost.ExecLibraryBase + ExecMemListOffset;
		var fastHeader = bus.ExpansionRamBase;
		var chipHeader = fastHeader + 0x40;
		var fastLower = bus.ExpansionRamBase + PseudoFastMetadataSize;
		var fastUpper = bus.ExpansionRamBase + (uint)bus.ExpansionRam.Length - 0x1000;
		var chipLower = ChipPublicLowerAddress;
		var chipUpper = (uint)bus.ChipRam.Length;
		var currentTaskAddress = bus.ExpansionRamBase + PseudoFastCurrentTaskOffset;

		Assert.Equal(fastHeader, bus.ReadLong(listAddress));
		Assert.Equal(0u, bus.ReadLong(listAddress + 4));
		Assert.Equal(chipHeader, bus.ReadLong(listAddress + 8));
		AssertExecBaseStaticFields(bus, 0x0008_0000, 0x00C8_0000);
		AssertMemoryHeader(bus, fastHeader, chipHeader, listAddress, MemfPublic | MemfFast, fastLower, fastUpper, "pseudo-fast");
		AssertMemoryHeader(bus, chipHeader, listAddress + 4, fastHeader, MemfPublic | MemfChip, chipLower, chipUpper, "chip");
		Assert.Equal((ushort)AmigaBootController.CmdRead, bus.ReadWord(AmigaBootController.BootIoRequestAddress + BootIoCommandOffset));
		Assert.False(machine.Cpu.State.GetFlag(M68kCpuState.Supervisor));
		Assert.Equal(0x400u, machine.Cpu.State.SupervisorStackPointer);
		var currentTask = bus.ReadLong(AmigaKickstartHost.ExecLibraryBase + ExecThisTaskOffset);
		Assert.Equal(currentTaskAddress, currentTask);
		Assert.Equal(bus.ReadLong(AmigaKickstartHost.ExecLibraryBase + ExecTaskTrapCodeOffset), bus.ReadLong(currentTask + TaskTrapCodeOffset));
		Assert.NotEqual(0u, bus.ReadLong(0x90));
	}

	[Fact]
	public void A500PlusHostShim20BootsDeterministicallyWithOneMiBChipRam()
	{
		var machine = StartBootShim(MachineProfile.A500PlusEcsPal);
		var bus = machine.Bus;
		var listAddress = AmigaKickstartHost.ExecLibraryBase + ExecMemListOffset;
		var privateBase = (uint)bus.ChipRam.Length - PrivateMetadataSize;
		var chipHeader = privateBase + ChipOnlyMemHeaderOffset;

		Assert.Equal(AmigaChipset.EcsPal, bus.Chipset);
		Assert.Equal(1024 * 1024, bus.ChipRam.Length);
		Assert.Equal(KickstartBackendKind.HostShim, machine.Kickstart.Configuration.Backend);
		Assert.Equal(KickstartVersion.Kickstart20, machine.Kickstart.Configuration.Version);
		Assert.Equal(chipHeader, bus.ReadLong(listAddress));
		AssertMemoryHeader(
			bus,
			chipHeader,
			listAddress + 4,
			listAddress,
			MemfPublic | MemfChip,
			ChipPublicLowerAddress,
			privateBase,
			"chip");
		Assert.False(machine.Cpu.State.GetFlag(M68kCpuState.Supervisor));
		Assert.NotEqual(0u, bus.ReadLong(0x90));
	}

	[Fact]
	public void BootShimAddsRealFastBeforePseudoFastInKickstartMemList()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithRealFastRam(8 * 1024 * 1024)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		var listAddress = AmigaKickstartHost.ExecLibraryBase + ExecMemListOffset;
		var realHeader = bus.RealFastRamBase;
		var pseudoHeader = bus.RealFastRamBase + 0x80;
		var chipHeader = bus.RealFastRamBase + 0x100;
		var realLower = bus.RealFastRamBase + RealFastMetadataSize;
		var realUpper = bus.RealFastRamBase + (uint)bus.RealFastRam.Length;
		var pseudoLower = bus.ExpansionRamBase;
		var pseudoUpper = bus.ExpansionRamBase + (uint)bus.ExpansionRam.Length - 0x1000;
		var chipLower = ChipPublicLowerAddress;
		var chipUpper = (uint)bus.ChipRam.Length;

		Assert.Equal(realHeader, bus.ReadLong(listAddress));
		Assert.Equal(0u, bus.ReadLong(listAddress + 4));
		Assert.Equal(chipHeader, bus.ReadLong(listAddress + 8));
		AssertExecBaseStaticFields(bus, 0x0008_0000, 0x00C8_0000);
		AssertMemoryHeader(bus, realHeader, pseudoHeader, listAddress, MemfPublic | MemfFast, realLower, realUpper, "real-fast");
		AssertMemoryHeader(bus, pseudoHeader, chipHeader, realHeader, MemfPublic | MemfFast, pseudoLower, pseudoUpper, "pseudo-fast");
		AssertMemoryHeader(bus, chipHeader, listAddress + 4, pseudoHeader, MemfPublic | MemfChip, chipLower, chipUpper, "chip");
		Assert.Equal(realLower, InvokeAllocMem(bus, 0x1000, MemfPublic | MemfFast));
	}

	[Fact]
	public void KickstartRomBootReservesLowPseudoFastAlreadyUsedByRom()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithRealFastRam(8 * 1024 * 1024)
			.WithKickstart(KickstartConfiguration.FromRomImage(
				KickstartVersion.Kickstart20,
				CreateMinimalKickstartRom()))
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartKickstartRomBoot(CreateBootableDisk());
		machine.Cpu.State.VectorBaseRegister = 0x00F8_0000;

		InvokeInstallBootHostTraps(boot);

		var bus = machine.Bus;
		var listAddress = AmigaKickstartHost.ExecLibraryBase + ExecMemListOffset;
		var realHeader = bus.RealFastRamBase;
		var pseudoHeader = bus.RealFastRamBase + 0x80;
		var chipHeader = bus.RealFastRamBase + 0x100;
		var pseudoLower = bus.ExpansionRamBase + KickstartRomPseudoFastReserve;
		var pseudoUpper = bus.ExpansionRamBase + (uint)bus.ExpansionRam.Length - 0x1000;

		Assert.Equal(realHeader, bus.ReadLong(listAddress));
		Assert.Equal(chipHeader, bus.ReadLong(listAddress + 8));
		Assert.Equal(0u, machine.Cpu.State.VectorBaseRegister);
		Assert.Equal(0x0007_EFFCu, machine.Cpu.State.SupervisorStackPointer);
		AssertMemoryHeader(bus, pseudoHeader, chipHeader, realHeader, MemfPublic | MemfFast, pseudoLower, pseudoUpper, "pseudo-fast");
		Assert.Equal(pseudoLower, bus.ReadLong(pseudoHeader + MemHeaderFirstChunkOffset));
		Assert.Equal(0u, bus.ReadLong(bus.ExpansionRamBase + MemChunkNextOffset));
		Assert.Equal(0u, bus.ReadLong(bus.ExpansionRamBase + MemChunkBytesOffset));
	}

	[Fact]
	public void WorkbenchSessionInstallsHostShimForRomConfiguredMachine()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithKickstart(KickstartConfiguration.FromRomImage(KickstartVersion.Kickstart13, new byte[8]))
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);

		boot.StartWorkbenchSession(CreateBootableDisk());

		Assert.Equal(KickstartBackendKind.RomImage, machine.Kickstart.Configuration.Backend);
		Assert.Equal(AmigaKickstartHost.ExecStructAddress, machine.Bus.ReadLong(0));
		Assert.Equal(AmigaKickstartHost.ExecLibraryBase, machine.Bus.ReadLong(4));
		Assert.Equal(AmigaKickstartHost.ExecLibraryBase, machine.Cpu.State.A[6]);
		Assert.True(machine.Bus.HasHostGateway(Lvo(AmigaKickstartHost.ExecLibraryBase, -408)));
		Assert.True(machine.Bus.HasHostGateway(Lvo(AmigaKickstartHost.ExecLibraryBase, -1206)));
		Assert.True(machine.Bus.HasHostGateway(Lvo(AmigaKickstartHost.ExecLibraryBase, -1212)));
		Assert.True(machine.Bus.HasHostGateway(Lvo(AmigaKickstartHost.DosLibraryBase, -30)));
		Assert.True(machine.Bus.HasHostGateway(Lvo(AmigaKickstartHost.DosLibraryBase, -798)));
	}

	[Fact]
	public void ChipOnlyBootProfileKeepsMemListMetadataOutOfPublicLowMemory()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KChipOnlyBoot);
		var bus = machine.Bus;
		var listAddress = AmigaKickstartHost.ExecLibraryBase + ExecMemListOffset;
		var privateBase = (uint)bus.ChipRam.Length - PrivateMetadataSize;
		var chipHeader = privateBase + ChipOnlyMemHeaderOffset;
		var chipLower = ChipPublicLowerAddress;
		var chipUpper = privateBase;
		var chipNameAddress = privateBase + ChipOnlyMemNameOffset;

		Assert.Empty(bus.ExpansionRam);
		Assert.Equal(chipHeader, bus.ReadLong(listAddress));
		Assert.Equal(0u, bus.ReadLong(listAddress + 4));
		Assert.Equal(chipHeader, bus.ReadLong(listAddress + 8));
		Assert.Equal(privateBase, bus.ReadLong(AmigaKickstartHost.ExecLibraryBase + ExecThisTaskOffset));
		Assert.Equal(chipNameAddress, bus.ReadLong(chipHeader + MemNodeNameOffset));
		AssertExecBaseStaticFields(bus, 0x0008_0000, 0);
		AssertMemoryHeader(bus, chipHeader, listAddress + 4, listAddress, MemfPublic | MemfChip, chipLower, chipUpper, "chip");
	}

	[Fact]
	public void AllocMemAvailMemAndFreeMemUseKickstartMemListChunks()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var fastHeader = bus.ExpansionRamBase;
		var chipHeader = fastHeader + 0x40;
		var initialFastFree = bus.ReadLong(fastHeader + MemHeaderFreeOffset);
		var initialChipFree = bus.ReadLong(chipHeader + MemHeaderFreeOffset);

		var publicAllocation = InvokeAllocMem(bus, 0x1000, MemfPublic);
		var chipAllocation = InvokeAllocMem(bus, 0x2000, MemfPublic | MemfChip);

		Assert.Equal(bus.ExpansionRamBase + PseudoFastMetadataSize, publicAllocation);
		Assert.Equal(ChipPublicLowerAddress, chipAllocation);
		Assert.Equal(initialFastFree - 0x1000, bus.ReadLong(fastHeader + MemHeaderFreeOffset));
		Assert.Equal(initialChipFree - 0x2000, bus.ReadLong(chipHeader + MemHeaderFreeOffset));
		Assert.Equal(initialFastFree - 0x1000, InvokeAvailMem(bus, MemfFast));
		Assert.Equal(initialChipFree - 0x2000, InvokeAvailMem(bus, MemfChip));

		InvokeFreeMem(bus, publicAllocation, 0x1000);
		InvokeFreeMem(bus, chipAllocation, 0x2000);

		Assert.Equal(initialFastFree, bus.ReadLong(fastHeader + MemHeaderFreeOffset));
		Assert.Equal(initialChipFree, bus.ReadLong(chipHeader + MemHeaderFreeOffset));
		Assert.Equal(initialFastFree, InvokeAvailMem(bus, MemfFast));
		Assert.Equal(initialChipFree, InvokeAvailMem(bus, MemfChip));
		Assert.Equal(bus.ExpansionRamBase + PseudoFastMetadataSize, bus.ReadLong(fastHeader + MemHeaderFirstChunkOffset));
		Assert.Equal(ChipPublicLowerAddress, bus.ReadLong(chipHeader + MemHeaderFirstChunkOffset));
	}

	[Fact]
	public void ChipOnlyAllocMemCanReturnLowChipMemoryAboveSupervisorStack()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KChipOnlyBoot);
		var bus = machine.Bus;
		var chipHeader = (uint)bus.ChipRam.Length - PrivateMetadataSize + ChipOnlyMemHeaderOffset;
		var allocatableBytes = bus.ReadLong(chipHeader + MemHeaderFreeOffset);

		var allocation = InvokeAllocMem(bus, allocatableBytes, MemfPublic | MemfChip);

		Assert.Equal(ChipPublicLowerAddress, allocation);
		Assert.Equal(0u, bus.ReadLong(chipHeader + MemHeaderFirstChunkOffset));
		Assert.Equal(0u, bus.ReadLong(chipHeader + MemHeaderFreeOffset));
	}

	[Fact]
	public void ExecBaseCaptureVectorsAreWritableRuntimeState()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var execBase = AmigaKickstartHost.ExecLibraryBase;

		bus.WriteLong(execBase + ExecColdCaptureOffset, 0x0000_0400);
		bus.WriteLong(execBase + ExecCoolCaptureOffset, 0x0000_0500);
		bus.WriteLong(execBase + ExecWarmCaptureOffset, 0x0000_0600);

		Assert.Equal(0x0000_0400u, bus.ReadLong(execBase + ExecColdCaptureOffset));
		Assert.Equal(0x0000_0500u, bus.ReadLong(execBase + ExecCoolCaptureOffset));
		Assert.Equal(0x0000_0600u, bus.ReadLong(execBase + ExecWarmCaptureOffset));
	}

	[Fact]
	public void AllocMemOnlyClearsMemoryWhenMemfClearIsRequested()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var expectedAddress = ChipPublicLowerAddress;
		bus.WriteLong(expectedAddress, 0xAABBCCDD);

		var allocation = InvokeAllocMem(bus, 0x10, MemfPublic | MemfChip);

		Assert.Equal(expectedAddress, allocation);
		Assert.Equal(0xAABBCCDDu, bus.ReadLong(allocation));

		machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		bus = machine.Bus;
		bus.WriteLong(expectedAddress, 0xAABBCCDD);

		allocation = InvokeAllocMem(bus, 0x10, MemfPublic | MemfChip | MemfClear);

		Assert.Equal(expectedAddress, allocation);
		Assert.Equal(0u, bus.ReadLong(allocation));
	}

	[Fact]
	public void AllocAbsReservesFixedAddressFromKickstartMemList()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var chipHeader = bus.ExpansionRamBase + 0x40;
		var initialChipFree = bus.ReadLong(chipHeader + MemHeaderFreeOffset);

		var allocation = InvokeAllocAbs(bus, 0x200, 0x1000);
		var duplicate = InvokeAllocAbs(bus, 0x200, 0x1000);

		Assert.Equal(0x1000u, allocation);
		Assert.Equal(0u, duplicate);
		Assert.Equal(initialChipFree - 0x200, bus.ReadLong(chipHeader + MemHeaderFreeOffset));
		Assert.Equal(ChipPublicLowerAddress, bus.ReadLong(chipHeader + MemHeaderFirstChunkOffset));
		Assert.Equal(0x1000u - ChipPublicLowerAddress, bus.ReadLong(ChipPublicLowerAddress + MemChunkBytesOffset));
		Assert.Equal(0x1200u, bus.ReadLong(ChipPublicLowerAddress + MemChunkNextOffset));
		Assert.Equal((uint)bus.ChipRam.Length - 0x1200u, bus.ReadLong(0x1200 + MemChunkBytesOffset));

		InvokeFreeMem(bus, allocation, 0x200);

		Assert.Equal(initialChipFree, bus.ReadLong(chipHeader + MemHeaderFreeOffset));
		Assert.Equal(ChipPublicLowerAddress, bus.ReadLong(chipHeader + MemHeaderFirstChunkOffset));
		Assert.Equal(0u, bus.ReadLong(ChipPublicLowerAddress + MemChunkNextOffset));
		Assert.Equal(initialChipFree, bus.ReadLong(ChipPublicLowerAddress + MemChunkBytesOffset));
	}

	[Fact]
	public void DoIoReadClearsIoErrorAndReportsActualLength()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var io = AmigaBootController.BootIoRequestAddress;
		var state = new M68kCpuState();
		state.A[1] = io;
		bus.WriteWord(io + BootIoCommandOffset, AmigaBootController.CmdRead);
		bus.WriteByte(io + BootIoErrorOffset, 0xCC, 0);
		bus.WriteLong(io + BootIoActualOffset, 0xDEAD_BEEFu);
		bus.WriteLong(io + BootIoLengthOffset, 0x20);
		bus.WriteLong(io + BootIoDataOffset, ChipPublicLowerAddress);
		bus.WriteLong(io + BootIoOffsetOffset, 0x400);

		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -456), state));

		Assert.Equal(0u, state.D[0]);
		Assert.Equal(0, bus.ReadByte(io + BootIoErrorOffset));
		Assert.Equal(0x20u, bus.ReadLong(io + BootIoActualOffset));
	}

	[Fact]
	public void SuperStateReturnsSupervisorStackAndKeepsUserStackActive()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var state = new M68kCpuState();
		state.ResetStackPointers(supervisorStackPointer: 0x400, userStackPointer: 0x2000, supervisorMode: false);
		state.SetActiveStackPointer(0x1FFC);

		Assert.True(InvokeHostTrap(machine.Bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -150), state));

		Assert.Equal(0x400u, state.D[0]);
		Assert.True(state.GetFlag(M68kCpuState.Supervisor));
		Assert.Equal(0x1FFCu, state.A[7]);

		state.D[0] = 0x400;
		Assert.True(InvokeHostTrap(machine.Bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -156), state));

		Assert.False(state.GetFlag(M68kCpuState.Supervisor));
		Assert.Equal(0x1FFCu, state.A[7]);
		Assert.Equal(0x400u, state.SupervisorStackPointer);
	}

	[Fact]
	public void FindTaskAndTrapAllocationUseCurrentTask()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var state = new M68kCpuState();

		Assert.True(InvokeHostTrap(machine.Bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -294), state));

		var currentTask = machine.Bus.ReadLong(AmigaKickstartHost.ExecLibraryBase + ExecThisTaskOffset);
		Assert.Equal(currentTask, state.D[0]);

		state.D[0] = 4;
		Assert.True(InvokeHostTrap(machine.Bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -342), state));

		Assert.Equal(4u, state.D[0]);
		Assert.Equal(0x0010, machine.Bus.ReadWord(currentTask + TaskTrapAllocOffset));
		Assert.Equal(0x0010, machine.Bus.ReadWord(currentTask + TaskTrapAbleOffset));

		state.D[0] = 4;
		Assert.True(InvokeHostTrap(machine.Bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -342), state));

		Assert.Equal(0xFFFF_FFFFu, state.D[0]);

		state.D[0] = 4;
		Assert.True(InvokeHostTrap(machine.Bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -348), state));

		Assert.Equal(0, machine.Bus.ReadWord(currentTask + TaskTrapAllocOffset));
		Assert.Equal(0, machine.Bus.ReadWord(currentTask + TaskTrapAbleOffset));
	}

	[Fact]
	public void BootRunnerExecutesTrapVectorCodeAtSupervisorStackPage()
	{
		var machine = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot));
		var boot = new AmigaBootController(machine);

		var result = boot.BootFromDisk(
			CreateTrapVectorToStackPageDisk(),
			maxInstructions: 64,
			runMode: AmigaBootRunMode.ContinueAfterBootDiskRead);

		Assert.True(result.CompletedBootBlock, string.Join(Environment.NewLine, result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
		Assert.Equal(0x33FC_BEEFu, machine.Bus.ReadLong(0x400));
		Assert.Equal(0x0000_0500u, machine.Bus.ReadLong(0x404));
		Assert.Equal(0x4E73u, machine.Bus.ReadWord(0x408));
		Assert.Equal(0xBEEFu, machine.Bus.ReadWord(0x500));
	}

	[Fact]
	public void DefaultTrapVectorDispatchesThroughCurrentTaskTrapCode()
	{
		var machine = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot));
		var boot = new AmigaBootController(machine);

		var result = boot.BootFromDisk(
			CreateCurrentTaskTrapCodeDisk(AmigaConstants.A500BootPseudoFastRamBase + PseudoFastCurrentTaskOffset),
			maxInstructions: 64,
			runMode: AmigaBootRunMode.ContinueAfterBootDiskRead);

		Assert.True(result.CompletedBootBlock, string.Join(Environment.NewLine, result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
		Assert.NotEqual(0u, machine.Bus.ReadLong(0x90));
		Assert.Equal(0xBEEFu, machine.Bus.ReadWord(0x500));
	}

	[Fact]
	public void HostTaskTrapVectorsRefreshAfterGuestClearsProbeVectors()
	{
		var machine = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;

		bus.WriteLong(2u * 4u, 0);
		bus.WriteLong(11u * 4u, 0);
		bus.WriteLong(32u * 4u, 0);
		machine.Cpu.State.VectorBaseRegister = 0x00F8_0000;

		InvokeEnsureTaskTrapVectorsCurrent(boot);

		Assert.Equal(0u, machine.Cpu.State.VectorBaseRegister);
		Assert.True(bus.HasHostGateway(bus.ReadLong(2u * 4u)));
		Assert.True(bus.HasHostGateway(bus.ReadLong(11u * 4u)));
		Assert.True(bus.HasHostGateway(bus.ReadLong(32u * 4u)));
		Assert.NotEqual(0u, bus.ReadLong(2u * 4u));
		Assert.NotEqual(0u, bus.ReadLong(11u * 4u));
		Assert.NotEqual(0u, bus.ReadLong(32u * 4u));
	}

	[Fact]
	public void HostLineFTaskTrapSkipsDecodedM68040FpuProbe()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint codeAddress = 0x0000_2000;
		const uint stackTop = 0x0000_7000;
		bus.WriteWord(codeAddress, 0xF200);
		bus.WriteWord(codeAddress + 2, 0x4078);

		var state = new M68kCpuState();
		state.EnableM68020StackMode();
		state.ResetStackPointers(stackTop, stackTop, supervisorMode: true);
		state.SetActiveStackPointer(stackTop - 8);
		bus.WriteWord(stackTop - 8, M68kCpuState.Supervisor);
		bus.WriteLong(stackTop - 6, codeAddress);
		bus.WriteWord(stackTop - 2, 11 * 4);

		var lineFDispatcher = bus.ReadLong(11u * 4u);
		Assert.True(InvokeHostTrap(bus, lineFDispatcher, state));
		Assert.True(InvokeHostTrap(bus, state.ProgramCounter, state));

		Assert.Equal(codeAddress + 4, state.ProgramCounter);
		Assert.Equal(stackTop, state.A[7]);
		Assert.Equal(M68kCpuState.Supervisor, state.StatusRegister);
	}

	[Fact]
	public void HostTaskTrapRecoversZeroVectorLineFProbeFrame()
	{
		var machine = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		const uint codeAddress = 0x0000_2100;
		const uint stackTop = 0x0000_7100;
		bus.WriteWord(codeAddress, 0xF200);
		bus.WriteWord(codeAddress + 2, 0x4078);

		var state = machine.Cpu.State;
		state.EnableM68020StackMode();
		state.ResetStackPointers(stackTop, stackTop, supervisorMode: true);
		state.ProgramCounter = 0;
		state.SetActiveStackPointer(stackTop - 8);
		bus.WriteWord(stackTop - 8, M68kCpuState.Supervisor);
		bus.WriteLong(stackTop - 6, codeAddress);
		bus.WriteWord(stackTop - 2, 11 * 4);

		Assert.True(InvokeRecoverHostTaskTrapFromZeroVector(boot));

		Assert.Equal(codeAddress + 4, state.ProgramCounter);
		Assert.Equal(stackTop, state.A[7]);
		Assert.Equal(M68kCpuState.Supervisor, state.StatusRegister);
	}

	[Fact]
	public void HostLineATaskTrapSkipsProbeOpcode()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint codeAddress = 0x0000_2200;
		const uint stackTop = 0x0000_7200;
		bus.WriteWord(codeAddress, 0xA108);

		var state = new M68kCpuState();
		state.EnableM68020StackMode();
		state.ResetStackPointers(stackTop, stackTop, supervisorMode: true);
		state.SetActiveStackPointer(stackTop - 8);
		bus.WriteWord(stackTop - 8, M68kCpuState.Supervisor);
		bus.WriteLong(stackTop - 6, codeAddress);
		bus.WriteWord(stackTop - 2, 10 * 4);

		var lineADispatcher = bus.ReadLong(10u * 4u);
		Assert.True(InvokeHostTrap(bus, lineADispatcher, state));
		Assert.True(InvokeHostTrap(bus, state.ProgramCounter, state));

		Assert.Equal(codeAddress + 2, state.ProgramCounter);
		Assert.Equal(stackTop, state.A[7]);
		Assert.Equal(M68kCpuState.Supervisor, state.StatusRegister);
	}

	[Fact]
	public void HostIllegalInstructionTaskTrapSkipsIllegalProbeOpcode()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint codeAddress = 0x0000_2400;
		const uint stackTop = 0x0000_7400;
		bus.WriteWord(codeAddress, 0x4AFC);

		var state = new M68kCpuState();
		state.EnableM68020StackMode();
		state.ResetStackPointers(stackTop, stackTop, supervisorMode: true);
		state.SetActiveStackPointer(stackTop - 8);
		bus.WriteWord(stackTop - 8, M68kCpuState.Supervisor);
		bus.WriteLong(stackTop - 6, codeAddress);
		bus.WriteWord(stackTop - 2, 4 * 4);

		var illegalDispatcher = bus.ReadLong(4u * 4u);
		Assert.True(InvokeHostTrap(bus, illegalDispatcher, state));
		Assert.True(InvokeHostTrap(bus, state.ProgramCounter, state));

		Assert.Equal(codeAddress + 2, state.ProgramCounter);
		Assert.Equal(stackTop, state.A[7]);
		Assert.Equal(M68kCpuState.Supervisor, state.StatusRegister);
	}

	[Fact]
	public void HostBusErrorTaskTrapSkipsDecodedProbeInstruction()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint codeAddress = 0x0000_2500;
		const uint stackTop = 0x0000_7500;
		bus.WriteWord(codeAddress, 0x0000);
		bus.WriteWord(codeAddress + 2, 0x0012);

		var state = new M68kCpuState();
		state.EnableM68020StackMode();
		state.ResetStackPointers(stackTop, stackTop, supervisorMode: true);
		state.SetActiveStackPointer(stackTop - 8);
		bus.WriteWord(stackTop - 8, 0);
		bus.WriteLong(stackTop - 6, codeAddress);
		bus.WriteWord(stackTop - 2, 2 * 4);

		var busErrorDispatcher = bus.ReadLong(2u * 4u);
		Assert.True(InvokeHostTrap(bus, busErrorDispatcher, state));
		Assert.True(InvokeHostTrap(bus, state.ProgramCounter, state));

		Assert.Equal(codeAddress + 4, state.ProgramCounter);
		Assert.Equal(stackTop, state.A[7]);
		Assert.Equal(0, state.StatusRegister);
	}

	[Fact]
	public void HostBusErrorTaskTrapPopsLegacyBusFrame()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint codeAddress = 0x0000_2700;
		const uint stackTop = 0x0000_7700;
		bus.WriteWord(codeAddress, 0x0000);
		bus.WriteWord(codeAddress + 2, 0x0012);

		var state = new M68kCpuState();
		state.EnableM68020StackMode();
		state.ResetStackPointers(stackTop, stackTop, supervisorMode: true);
		state.SetActiveStackPointer(stackTop - 14);
		bus.WriteWord(stackTop - 14, 0);
		bus.WriteLong(stackTop - 12, codeAddress);
		bus.WriteWord(stackTop - 8, 0);
		bus.WriteWord(stackTop - 6, 0);
		bus.WriteLong(stackTop - 4, codeAddress);

		var busErrorDispatcher = bus.ReadLong(2u * 4u);
		Assert.True(InvokeHostTrap(bus, busErrorDispatcher, state));
		Assert.True(InvokeHostTrap(bus, state.ProgramCounter, state));

		Assert.Equal(codeAddress + 4, state.ProgramCounter);
		Assert.Equal(stackTop, state.A[7]);
		Assert.Equal(0, state.StatusRegister);
	}

	[Fact]
	public void HostDefaultTaskTrapPopsLegacyBusFrameThatLooksLikeVectorPrefix()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint codeAddress = 0x0002_2300;
		const uint stackTop = 0x0000_7800;
		bus.WriteWord(codeAddress, 0x0000);
		bus.WriteWord(codeAddress + 2, 0x0012);

		var state = new M68kCpuState();
		state.EnableM68020StackMode();
		state.ResetStackPointers(stackTop, stackTop, supervisorMode: true);
		state.SetActiveStackPointer(stackTop - 14);
		bus.WriteWord(stackTop - 14, 0);
		bus.WriteLong(stackTop - 12, codeAddress);
		bus.WriteWord(stackTop - 8, 0);
		bus.WriteWord(stackTop - 6, 0);
		bus.WriteLong(stackTop - 4, codeAddress);

		var defaultTrapCode = bus.ReadLong(AmigaKickstartHost.ExecLibraryBase + ExecTaskTrapCodeOffset);
		Assert.True(InvokeHostTrap(bus, defaultTrapCode, state));

		Assert.Equal(codeAddress + 4, state.ProgramCounter);
		Assert.Equal(stackTop, state.A[7]);
		Assert.Equal(0, state.StatusRegister);
	}

	[Fact]
	public void HostBusErrorTaskTrapReturnsFromUnmappedFetchProbe()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint faultAddress = 0x0008_0004;
		const uint returnAddress = 0x0000_2800;
		const uint supervisorStackTop = 0x0000_7500;
		const uint userStack = 0x0000_6000;
		bus.WriteWord(returnAddress, 0x4E71);
		bus.WriteLong(userStack, returnAddress);

		var state = new M68kCpuState();
		state.EnableM68020StackMode();
		state.ResetStackPointers(supervisorStackTop, userStack, supervisorMode: true);
		state.SetUserStackPointer(userStack);
		state.SetActiveStackPointer(supervisorStackTop - 8);
		bus.WriteWord(supervisorStackTop - 8, 0);
		bus.WriteLong(supervisorStackTop - 6, faultAddress);
		bus.WriteWord(supervisorStackTop - 2, 2 * 4);

		var busErrorDispatcher = bus.ReadLong(2u * 4u);
		Assert.True(InvokeHostTrap(bus, busErrorDispatcher, state));
		Assert.True(InvokeHostTrap(bus, state.ProgramCounter, state));

		Assert.Equal(returnAddress, state.ProgramCounter);
		Assert.Equal(userStack + 4, state.A[7]);
		Assert.Equal(0, state.StatusRegister);
	}

	[Fact]
	public void HostPrivilegeTaskTrapSkipsMovecProbeOpcode()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint codeAddress = 0x0000_2600;
		const uint stackTop = 0x0000_7600;
		bus.WriteWord(codeAddress, 0x4E7A);
		bus.WriteWord(codeAddress + 2, 0x0002);

		var state = new M68kCpuState();
		state.EnableM68020StackMode();
		state.ResetStackPointers(stackTop, stackTop, supervisorMode: true);
		state.SetActiveStackPointer(stackTop - 8);
		bus.WriteWord(stackTop - 8, 0);
		bus.WriteLong(stackTop - 6, codeAddress);
		bus.WriteWord(stackTop - 2, 8 * 4);

		var privilegeDispatcher = bus.ReadLong(8u * 4u);
		Assert.True(InvokeHostTrap(bus, privilegeDispatcher, state));
		Assert.True(InvokeHostTrap(bus, state.ProgramCounter, state));

		Assert.Equal(codeAddress + 4, state.ProgramCounter);
		Assert.Equal(stackTop, state.A[7]);
		Assert.Equal(0, state.StatusRegister);
	}

	[Fact]
	public void RethinkDisplayPublishesCurrentViewCopperList()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		WriteCopperColorList(bus, 0x2400, 0x0F00);
		WriteCopperColorList(bus, 0x2600, 0x00F0);
		bus.WriteLong(0x2304, 0x2400);
		bus.WriteLong(0x2200 + ViewLofCprListOffset, 0x2300);
		var state = new M68kCpuState();
		state.A[1] = 0x2200;

		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE), state));
		bus.WriteLong(0x2304, 0x2600);
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -0x186), state));
		var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
		bus.Display.RenderFrame(frame);

		Assert.Equal(0xFF00FF00u, Pixel(frame, 0, 0));
	}

	[Fact]
	public void GraphicsV39AllocatesLinearRtgBitMapsAndBltBitMapRastPortCopiesPlanarPixels()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithRtgVram(256L * 1024 * 1024)
			.WithCpu(AmigaM68kCoreFactory.Default, M68kBackendKind.AccurateM68040)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;

		var alloc = new M68kCpuState();
		alloc.D[0] = 16;
		alloc.D[1] = 1;
		alloc.D[2] = 8;
		alloc.D[3] = 1;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -918), alloc));
		var destinationBitMap = alloc.D[0];
		Assert.NotEqual(0u, destinationBitMap);
		var destination = bus.ReadLong(destinationBitMap + BitMapPlanesOffset);
		Assert.Equal(0x8000_0000u, destination);

		var getWidth = new M68kCpuState();
		getWidth.A[0] = destinationBitMap;
		getWidth.D[1] = 8;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -960), getWidth));
		Assert.Equal(16u, getWidth.D[0]);

		const uint sourceBitMap = 0x2600;
		const uint sourcePlane = 0x2700;
		const uint destinationRastPort = 0x2800;
		bus.WriteWord(sourceBitMap + BitMapBytesPerRowOffset, 2);
		bus.WriteWord(sourceBitMap + BitMapRowsOffset, 1);
		bus.WriteByte(sourceBitMap + BitMapDepthOffset, 1, 0);
		bus.WriteLong(sourceBitMap + BitMapPlanesOffset, sourcePlane);
		bus.WriteWord(sourcePlane, 0x8000);
		bus.WriteLong(destinationRastPort + RastPortBitMapOffset, destinationBitMap);
		bus.WriteByte(destinationRastPort + 0x18, 0xFF, 0);
		var blit = new M68kCpuState();
		blit.A[0] = sourceBitMap;
		blit.A[1] = destinationRastPort;
		blit.D[4] = 1;
		blit.D[5] = 1;
		blit.D[6] = 0xC0;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -606), blit));
		Assert.Equal(1u, blit.D[0]);
		Assert.Equal((byte)1, bus.ReadByte(destination));

		var free = new M68kCpuState();
		free.A[0] = destinationBitMap;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -924), free));
		Assert.False(bus.RtgVram.IsAllocatedAddress(destination));
	}

	[Fact]
	public void RtgRectFillUsesLayerClipRectsAndObscuredBackingBitMapCoordinates()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithRtgVram(256L * 1024 * 1024)
			.WithCpu(AmigaM68kCoreFactory.Default, M68kBackendKind.AccurateM68040)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		var patches = Assert.IsAssignableFrom<ICyberGraphicsGuestServices>(boot);

		static uint AllocateBitMap(AmigaBus bus, int width, int height)
		{
			var alloc = new M68kCpuState();
			alloc.D[0] = (uint)width;
			alloc.D[1] = (uint)height;
			alloc.D[2] = 8;
			alloc.D[3] = 1;
			Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -918), alloc));
			return alloc.D[0];
		}

		var screenBitMap = AllocateBitMap(bus, 32, 16);
		var backingBitMap = AllocateBitMap(bus, 3, 4);
		Assert.True(boot.CyberGraphics.TryGetBitMapSurface(screenBitMap, out var screen));
		Assert.True(boot.CyberGraphics.TryGetBitMapSurface(backingBitMap, out var backing));
		const uint rastPort = 0x2800;
		const uint layer = 0x2900;
		const uint obscured = 0x2A00;
		const uint visible = 0x2A40;
		bus.WriteLong(rastPort + 0x00, layer);
		bus.WriteLong(rastPort + RastPortBitMapOffset, screenBitMap);
		bus.WriteByte(rastPort + 0x18, 0xFF, 0);
		bus.WriteByte(rastPort + 0x19, 7, 0);
		bus.WriteLong(layer + 0x08, obscured);
		bus.WriteWord(layer + 0x10, 10);
		bus.WriteWord(layer + 0x12, 4);
		bus.WriteWord(layer + 0x14, 17);
		bus.WriteWord(layer + 0x16, 7);
		bus.WriteLong(obscured + 0x00, visible);
		bus.WriteLong(obscured + 0x0C, backingBitMap);
		bus.WriteWord(obscured + 0x10, 10);
		bus.WriteWord(obscured + 0x12, 4);
		bus.WriteWord(obscured + 0x14, 12);
		bus.WriteWord(obscured + 0x16, 7);
		bus.WriteWord(visible + 0x10, 15);
		bus.WriteWord(visible + 0x12, 4);
		bus.WriteWord(visible + 0x14, 17);
		bus.WriteWord(visible + 0x16, 7);
		boot.CyberGraphics.RegisterRastPort(rastPort, screen);
		Assert.True(patches.TryGetRastPortClipFragments(rastPort, 0, 0, 8, 4, out var processFragments));
		Assert.Equal(2, processFragments.Count);
		Assert.Equal(backingBitMap, processFragments[0].BitMapAddress);
		Assert.Equal((0, 0, 0, 0, 3, 4), (
			processFragments[0].RequestX,
			processFragments[0].RequestY,
			processFragments[0].BitMapX,
			processFragments[0].BitMapY,
			processFragments[0].Width,
			processFragments[0].Height));
		Assert.Equal(screenBitMap, processFragments[1].BitMapAddress);
		Assert.Equal((5, 0, 15, 4, 3, 4), (
			processFragments[1].RequestX,
			processFragments[1].RequestY,
			processFragments[1].BitMapX,
			processFragments[1].BitMapY,
			processFragments[1].Width,
			processFragments[1].Height));

		var process = new M68kCpuState();
		process.A[1] = rastPort;
		process.D[0] = 0;
		process.D[1] = 0;
		process.D[2] = 8;
		process.D[3] = 4;
		process.D[4] = 0;
		process.D[5] = 16;
		Assert.True(boot.CyberGraphics.Invoke(-228, process));
		Assert.Equal(24u, process.D[0]);
		for (var y = 0; y < 4; y++)
		{
			Assert.Equal(new byte[] { 16, 16, 16 }, Enumerable.Range(0, 3)
				.Select(x => bus.ReadByte(backing.GuestBaseAddress + (uint)(y * backing.BytesPerRow + x))));
			var screenValues = Enumerable.Range(0, 32)
				.Select(x => bus.ReadByte(screen.GuestBaseAddress + (uint)((y + 4) * screen.BytesPerRow + x)))
				.ToArray();
			Assert.True(screenValues.Skip(15).Take(3).SequenceEqual(new byte[] { 16, 16, 16 }), string.Join(",", screenValues));
			Assert.All(Enumerable.Range(10, 5), x =>
				Assert.Equal((byte)0, bus.ReadByte(screen.GuestBaseAddress + (uint)((y + 4) * screen.BytesPerRow + x))));
		}

		process = new M68kCpuState();
		process.A[1] = rastPort;
		process.D[2] = 1;
		process.D[3] = 1;
		process.D[4] = 0;
		process.D[5] = 16;
		process.D[0] = 20;
		Assert.True(boot.CyberGraphics.Invoke(-228, process));
		Assert.Equal(0u, process.D[0]);

		var fill = new M68kCpuState();
		fill.A[1] = rastPort;
		fill.D[0] = 0;
		fill.D[1] = 0;
		fill.D[2] = 7;
		fill.D[3] = 3;
		Assert.True(patches.TryInvokeGraphicsLibraryPatch(-306, fill));

		for (var y = 0; y < 4; y++)
		{
			Assert.Equal(new byte[] { 7, 7, 7 }, Enumerable.Range(0, 3)
				.Select(x => bus.ReadByte(backing.GuestBaseAddress + (uint)(y * backing.BytesPerRow + x))));
		}
		for (var y = 4; y <= 7; y++)
		{
			Assert.Equal(new byte[] { 7, 7, 7 }, Enumerable.Range(15, 3)
				.Select(x => bus.ReadByte(screen.GuestBaseAddress + (uint)(y * screen.BytesPerRow + x))));
			Assert.All(Enumerable.Range(10, 5), x =>
				Assert.Equal((byte)0, bus.ReadByte(screen.GuestBaseAddress + (uint)(y * screen.BytesPerRow + x))));
		}

		bus.ClearMemory(screen.GuestBaseAddress, screen.BytesPerRow * screen.Height);
		bus.ClearMemory(backing.GuestBaseAddress, backing.BytesPerRow * backing.Height);
		var highWordFill = new M68kCpuState();
		highWordFill.A[1] = rastPort;
		highWordFill.D[0] = 0x0001_0000;
		highWordFill.D[1] = 0;
		highWordFill.D[2] = 0x0001_0007;
		highWordFill.D[3] = 3;
		Assert.True(patches.TryInvokeGraphicsLibraryPatch(-306, highWordFill));
		Assert.All(Enumerable.Range(0, backing.BytesPerRow * backing.Height),
			offset => Assert.Equal((byte)0, bus.ReadByte(backing.GuestBaseAddress + (uint)offset)));
		Assert.All(Enumerable.Range(0, screen.BytesPerRow * screen.Height),
			offset => Assert.Equal((byte)0, bus.ReadByte(screen.GuestBaseAddress + (uint)offset)));

		var move = new M68kCpuState();
		move.A[1] = rastPort;
		Assert.True(patches.TryInvokeGraphicsLibraryPatch(-240, move));
		var draw = new M68kCpuState();
		draw.A[1] = rastPort;
		draw.D[0] = 7;
		Assert.True(patches.TryInvokeGraphicsLibraryPatch(-246, draw));
		Assert.Equal(new byte[] { 7, 7, 7 }, Enumerable.Range(0, 3)
			.Select(x => bus.ReadByte(backing.GuestBaseAddress + (uint)x)));
		Assert.Equal(new byte[] { 7, 7, 7 }, Enumerable.Range(15, 3)
			.Select(x => bus.ReadByte(screen.GuestBaseAddress + (uint)(4 * screen.BytesPerRow + x))));
		bus.ClearMemory(screen.GuestBaseAddress, screen.BytesPerRow * screen.Height);
		bus.ClearMemory(backing.GuestBaseAddress, backing.BytesPerRow * backing.Height);
		const uint sourceBitMap = 0x2B00;
		const uint sourcePlane = 0x2C00;
		bus.WriteWord(sourceBitMap + BitMapBytesPerRowOffset, 2);
		bus.WriteWord(sourceBitMap + BitMapRowsOffset, 4);
		bus.WriteByte(sourceBitMap + BitMapDepthOffset, 1, 0);
		bus.WriteLong(sourceBitMap + BitMapPlanesOffset, sourcePlane);
		for (var y = 0; y < 4; y++)
		{
			bus.WriteByte(sourcePlane + (uint)(y * 2), 0xFF, 0);
		}

		var blit = new M68kCpuState();
		blit.A[0] = sourceBitMap;
		blit.A[1] = rastPort;
		blit.D[4] = 8;
		blit.D[5] = 4;
		blit.D[6] = 0xC0;
		Assert.True(patches.TryInvokeGraphicsLibraryPatch(-606, blit));
		Assert.Equal(1u, blit.D[0]);
		for (var y = 0; y < 4; y++)
		{
			Assert.Equal(new byte[] { 1, 1, 1 }, Enumerable.Range(0, 3)
				.Select(x => bus.ReadByte(backing.GuestBaseAddress + (uint)(y * backing.BytesPerRow + x))));
			Assert.Equal(new byte[] { 1, 1, 1 }, Enumerable.Range(15, 3)
				.Select(x => bus.ReadByte(screen.GuestBaseAddress + (uint)((y + 4) * screen.BytesPerRow + x))));
		}

		var copyBitMap = AllocateBitMap(bus, 8, 4);
		Assert.True(boot.CyberGraphics.TryGetBitMapSurface(copyBitMap, out var copy));
		const uint copyRastPort = 0x2D00;
		bus.WriteLong(copyRastPort + RastPortBitMapOffset, copyBitMap);
		bus.WriteByte(copyRastPort + 0x18, 0xFF, 0);
		boot.CyberGraphics.RegisterRastPort(copyRastPort, copy);
		var clipBlit = new M68kCpuState();
		clipBlit.A[0] = rastPort;
		clipBlit.A[1] = copyRastPort;
		clipBlit.D[4] = 8;
		clipBlit.D[5] = 4;
		clipBlit.D[6] = 0xC0;
		Assert.True(patches.TryInvokeGraphicsLibraryPatch(-552, clipBlit));
		Assert.Equal(1u, clipBlit.D[0]);
		for (var y = 0; y < 4; y++)
		{
			Assert.Equal(
				new byte[] { 1, 1, 1, 0, 0, 1, 1, 1 },
				Enumerable.Range(0, 8)
					.Select(x => bus.ReadByte(copy.GuestBaseAddress + (uint)(y * copy.BytesPerRow + x))));
		}
	}

	[Fact]
	public void RtgToPlanarBltBitMapRastPortUsesLayerClipRectCoordinates()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithRtgVram(16L * 1024 * 1024)
			.WithCpu(AmigaM68kCoreFactory.Default, M68kBackendKind.AccurateM68040)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		var patches = Assert.IsAssignableFrom<ICyberGraphicsGuestServices>(boot);
		const uint sourceBitMap = 0x2500;
		var source = Assert.IsType<CyberGraphicsSurface>(
			boot.CyberGraphics.AllocateRtgSurface(2, 1, CyberGraphicsPixelFormat.Lut8));
		boot.CyberGraphics.RegisterBitMap(sourceBitMap, source);
		bus.WriteByte(source.GuestBaseAddress, 1, 0);
		bus.WriteByte(source.GuestBaseAddress + 1, 2, 0);

		const uint destinationBitMap = 0x2B00;
		const uint plane0 = 0x2C00;
		const uint plane1 = 0x2D00;
		bus.WriteWord(destinationBitMap + BitMapBytesPerRowOffset, 2);
		bus.WriteWord(destinationBitMap + BitMapRowsOffset, 1);
		bus.WriteByte(destinationBitMap + BitMapDepthOffset, 2, 0);
		bus.WriteLong(destinationBitMap + BitMapPlanesOffset, plane0);
		bus.WriteLong(destinationBitMap + BitMapPlanesOffset + 4, plane1);
		const uint rastPort = 0x2800;
		const uint layer = 0x2900;
		const uint clipRect = 0x2A00;
		bus.WriteLong(rastPort, layer);
		bus.WriteLong(rastPort + RastPortBitMapOffset, destinationBitMap);
		bus.WriteByte(rastPort + 0x18, 0xFF, 0);
		bus.WriteLong(layer + 0x08, clipRect);
		bus.WriteWord(layer + 0x10, 10);
		bus.WriteWord(layer + 0x12, 4);
		bus.WriteWord(layer + 0x14, 11);
		bus.WriteWord(layer + 0x16, 4);
		bus.WriteLong(clipRect + 0x0C, destinationBitMap);
		bus.WriteWord(clipRect + 0x10, 10);
		bus.WriteWord(clipRect + 0x12, 4);
		bus.WriteWord(clipRect + 0x14, 11);
		bus.WriteWord(clipRect + 0x16, 4);

		var blit = new M68kCpuState();
		blit.A[0] = sourceBitMap;
		blit.A[1] = rastPort;
		blit.D[4] = 2;
		blit.D[5] = 1;
		blit.D[6] = 0xC0;
		Assert.True(patches.TryInvokeGraphicsLibraryPatch(-606, blit));
		Assert.Equal(1u, blit.D[0]);
		Assert.Equal(0x80, bus.ReadByte(plane0));
		Assert.Equal(0x40, bus.ReadByte(plane1));
	}

	[Fact]
	public void GraphicsPatchesPublishRtgDisplayDatabaseAndBestMode()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithRtgVram(256L * 1024 * 1024)
			.WithCpu(AmigaM68kCoreFactory.Default, M68kBackendKind.AccurateM68040)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var patches = Assert.IsAssignableFrom<ICyberGraphicsGuestServices>(boot);
		var next = new M68kCpuState();
		next.D[0] = uint.MaxValue;

		Assert.True(patches.TryInvokeGraphicsLibraryPatch(-732, next));
		Assert.Equal(0x4350_0011u, next.D[0]);
		var find = new M68kCpuState();
		find.D[0] = next.D[0];
		Assert.True(patches.TryInvokeGraphicsLibraryPatch(-726, find));
		Assert.Equal(next.D[0], find.D[0]);

		const uint nameInfo = 0x2800;
		var info = new M68kCpuState();
		info.A[0] = next.D[0];
		info.A[1] = nameInfo;
		info.D[0] = 0x38;
		info.D[1] = 0x8000_3000;
		Assert.True(patches.TryInvokeGraphicsLibraryPatch(-756, info));
		Assert.Equal(0x38u, info.D[0]);
		Assert.Equal(0x8000_3000u, machine.Bus.ReadLong(nameInfo));
		Assert.Equal(0x4350_0011u, machine.Bus.ReadLong(nameInfo + 4));
		Assert.StartsWith("Copper RTG 640x480 LUT8", ReadCString(machine.Bus, nameInfo + 0x10, 32));

		const uint bestTags = 0x2900;
		machine.Bus.WriteLong(bestTags + 0x00, 0x8000_0004);
		machine.Bus.WriteLong(bestTags + 0x04, 640);
		machine.Bus.WriteLong(bestTags + 0x08, 0x8000_0005);
		machine.Bus.WriteLong(bestTags + 0x0C, 480);
		machine.Bus.WriteLong(bestTags + 0x10, 0x8000_0008);
		machine.Bus.WriteLong(bestTags + 0x14, 8);
		machine.Bus.WriteLong(bestTags + 0x18, 0);
		var best = new M68kCpuState();
		best.A[0] = bestTags;
		Assert.True(patches.TryInvokeGraphicsLibraryPatch(-1050, best));
		Assert.Equal(0x4350_0011u, best.D[0]);

		var available = new M68kCpuState();
		available.D[0] = best.D[0];
		Assert.True(patches.TryInvokeGraphicsLibraryPatch(-798, available));
		Assert.Equal(0u, available.D[0]);
	}

	[Fact]
	public void OpenScreenTagListOnlyWrapsRegisteredRtgModeAndAssociatesIntuitionObjects()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithRtgVram(256L * 1024 * 1024)
			.WithCpu(AmigaM68kCoreFactory.Default, M68kBackendKind.AccurateM68040)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		var patches = Assert.IsAssignableFrom<ICyberGraphicsGuestServices>(boot);
		const uint tags = 0x2200;
		const uint stack = 0x2300;
		const uint originalTarget = 0x0001_8000;
		const uint callerReturn = 0x0001_9000;
		const uint screen = 0x3000;
		const uint screenBitMapShell = 0x3500;

		bus.WriteLong(tags, 0x8000_0032); // SA_DisplayID
		bus.WriteLong(tags + 4, 0x0002_1000); // PAL_MONITOR_ID: native
		bus.WriteLong(tags + 8, 0);
		bus.WriteLong(stack, callerReturn);
		var open = new M68kCpuState();
		open.A[1] = tags;
		open.A[7] = stack;
		Assert.False(patches.TryInvokeIntuitionLibraryPatch(-612, originalTarget, open));
		Assert.Equal(callerReturn, bus.ReadLong(stack));

		bus.WriteLong(tags + 4, 0x4350_0011); // Copper RTG 640x480 LUT8
		Assert.True(patches.TryInvokeIntuitionLibraryPatch(-612, originalTarget, open));
		Assert.Equal(originalTarget, open.ProgramCounter);
		var continuation = bus.ReadLong(stack);
		Assert.NotEqual(callerReturn, continuation);
		Assert.True(bus.HasHostGateway(continuation));

		var alloc = new M68kCpuState();
		alloc.D[0] = 640;
		alloc.D[1] = 480;
		alloc.D[2] = 8;
		alloc.D[3] = 1;
		Assert.True(patches.TryInvokeGraphicsLibraryPatch(-918, alloc));
		var bitMap = alloc.D[0];
		Assert.NotEqual(0u, bitMap);
		var vram = bus.ReadLong(bitMap + BitMapPlanesOffset);
		Assert.Equal(0x8000_0000u, vram);

		// A CyberGraphX screen may expose only an opaque/empty BitMap shell.
		// Association must not depend on Planes[0] containing the VRAM address.
		bus.WriteLong(screen + ScreenRastPortOffset + RastPortBitMapOffset, screenBitMapShell);
		const uint rasInfo = 0x3400;
		bus.WriteLong(screen + ScreenViewPortOffset + ViewPortRasInfoOffset, rasInfo);
		bus.WriteLong(rasInfo + 4, screenBitMapShell);
		const uint colorMap = 0x3700;
		const uint highColors = 0x3740;
		const uint lowColors = 0x3780;
		bus.WriteLong(screen + ScreenViewPortOffset + 4, colorMap);
		bus.WriteByte(colorMap + 1, 1, 0); // V36+ ColorMap with LowColorBits
		bus.WriteWord(colorMap + 2, 2);
		bus.WriteLong(colorMap + 4, highColors);
		bus.WriteLong(colorMap + 0x0C, lowColors);
		bus.WriteWord(highColors + 2, 0x0123);
		bus.WriteWord(lowColors + 2, 0x0456);
		Assert.Equal(0u, bus.ReadLong(screenBitMapShell + BitMapPlanesOffset));
		var completed = new M68kCpuState();
		completed.A[7] = stack + 4;
		completed.D[0] = screen;
		Assert.True(InvokeHostTrap(bus, continuation, completed));
		Assert.Equal(callerReturn, completed.ProgramCounter);
		Assert.True(boot.CyberGraphics.TryGetBitMapSurface(screenBitMapShell, out var screenSurface));
		Assert.Equal(colorMap, screenSurface.ColorMapAddress);
		Assert.Equal(0xFF14_2536u, screenSurface.Palette[1]);
		var setRgb32 = new M68kCpuState();
		setRgb32.A[0] = screen + ScreenViewPortOffset;
		setRgb32.D[0] = 1;
		setRgb32.D[1] = 0xAA00_0000;
		setRgb32.D[2] = 0xBB00_0000;
		setRgb32.D[3] = 0xCC00_0000;
		Assert.False(patches.TryInvokeGraphicsLibraryPatch(-852, setRgb32));
		Assert.Equal(0xFFAA_BBCCu, screenSurface.Palette[1]);

		var friendAlloc = new M68kCpuState();
		friendAlloc.A[0] = screenBitMapShell;
		friendAlloc.D[0] = 16;
		friendAlloc.D[1] = 1;
		friendAlloc.D[2] = 8;
		friendAlloc.D[3] = 1;
		Assert.True(patches.TryInvokeGraphicsLibraryPatch(-918, friendAlloc));
		Assert.True(boot.CyberGraphics.TryGetBitMapSurface(friendAlloc.D[0], out var friendSurface));
		Assert.Equal(colorMap, friendSurface.ColorMapAddress);
		Assert.Same(screenSurface.Palette, friendSurface.Palette);

		// Intuition must retain ownership of ScreenBuffer allocation,
		// freeing, swap refusal, and DBufInfo message-port signalling.
		var screenBufferCall = new M68kCpuState();
		screenBufferCall.A[0] = screen;
		Assert.False(patches.TryInvokeIntuitionLibraryPatch(-768, originalTarget, screenBufferCall));
		Assert.False(patches.TryInvokeIntuitionLibraryPatch(-774, originalTarget, screenBufferCall));
		Assert.False(patches.TryInvokeIntuitionLibraryPatch(-780, originalTarget, screenBufferCall));

		var changeBuffer = new M68kCpuState();
		changeBuffer.A[0] = screen + ScreenViewPortOffset;
		changeBuffer.A[1] = friendAlloc.D[0];
		changeBuffer.A[2] = 0x37C0; // Native graphics.library DBufInfo remains untouched.
		Assert.False(patches.TryInvokeGraphicsLibraryPatch(-942, changeBuffer));
		Assert.True(boot.CyberGraphics.TryGetViewPortSurface(changeBuffer.A[0], out var selectedSurface));
		Assert.Same(friendSurface, selectedSurface);
		changeBuffer.A[1] = screenBitMapShell;
		Assert.False(patches.TryInvokeGraphicsLibraryPatch(-942, changeBuffer));
		Assert.True(boot.CyberGraphics.TryGetViewPortSurface(changeBuffer.A[0], out selectedSurface));
		Assert.Same(screenSurface, selectedSurface);
		const uint view = 0x3600;
		bus.WriteLong(view, screen + ScreenViewPortOffset);
		bus.WriteWord(screen + ScreenViewPortOffset + ViewPortDWidthOffset, 640);
		bus.WriteWord(screen + ScreenViewPortOffset + ViewPortDHeightOffset, 480);
		var loadView = new M68kCpuState();
		loadView.A[1] = view;
		Assert.False(patches.TryInvokeGraphicsLibraryPatch(-222, loadView));
		Assert.True(boot.TryGetRtgComposition(out var composition));
		Assert.Equal(640, composition.Width);
		Assert.Equal(480, composition.Height);
		var getShellWidth = new M68kCpuState();
		getShellWidth.A[0] = screenBitMapShell;
		getShellWidth.D[1] = 8; // BMA_WIDTH
		Assert.True(patches.TryInvokeGraphicsLibraryPatch(-960, getShellWidth));
		Assert.Equal(640u, getShellWidth.D[0]);

		bus.WriteLong(vram, 0x2A00_0000);
		Assert.True(boot.TryRenderRtgFrame(out var frame));
		Assert.Equal(screen + ScreenViewPortOffset, frame.ViewPortAddress);
		Assert.Equal((byte)0x2A, bus.ReadByte(vram));
	}

	[Fact]
	public void LegacyOpenScreenRequiresNsExtendedDisplayIdForRtgSelection()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithRtgVram(16L * 1024 * 1024)
			.WithCpu(AmigaM68kCoreFactory.Default, M68kBackendKind.AccurateM68040)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		var patches = Assert.IsAssignableFrom<ICyberGraphicsGuestServices>(boot);
		const uint newScreen = 0x2200;
		const uint extension = 0x2300;
		const uint stack = 0x2400;
		bus.WriteWord(newScreen + NewScreenWidthOffset, 640);
		bus.WriteWord(newScreen + NewScreenHeightOffset, 480);
		bus.WriteByte(newScreen + NewScreenDepthOffset, 8, 0);
		bus.WriteLong(newScreen + 0x20, extension);
		bus.WriteLong(extension, 0x8000_0032); // SA_DisplayID
		bus.WriteLong(extension + 4, 0x4350_0011);
		bus.WriteLong(extension + 8, 0);
		bus.WriteLong(stack, 0x0001_9000);
		var open = new M68kCpuState();
		open.A[0] = newScreen;
		open.A[7] = stack;

		Assert.False(patches.TryInvokeIntuitionLibraryPatch(-198, 0x0001_8000, open));
		bus.WriteWord(newScreen + 0x0E, 0x1000); // NS_EXTENDED
		Assert.True(patches.TryInvokeIntuitionLibraryPatch(-198, 0x0001_8000, open));
		Assert.Equal(0x0001_8000u, open.ProgramCounter);
	}

	[Fact]
	public void LoadViewPublishesCopperListFromKickstartViewOffsets()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		bus.EnableLiveAgnusDma();
		const uint view = 0x2200;
		const uint lofCprList = 0x2300;
		const uint shfCprList = 0x2320;
		WriteCopperColorList(bus, 0x2400, 0x0F00);
		WriteCopperColorList(bus, 0x2600, 0x00F0);
		bus.WriteLong(lofCprList + CprListStartOffset, 0x2400);
		bus.WriteLong(shfCprList + CprListStartOffset, 0x2600);
		bus.WriteLong(view + ViewLofCprListOffset, lofCprList);
		bus.WriteLong(view + ViewShfCprListOffset, shfCprList);
		var state = new M68kCpuState();
		state.A[1] = view;
		var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
		var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;

		bus.Display.BeginPresentationFrame(new PresentationFrameTarget(frame), 0, frameCycles);
		try
		{
			Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE), state));
			bus.Display.CompletePresentationFrame(frameCycles);
		}
		catch
		{
			bus.Display.AbortPresentationFrame();
			throw;
		}
		Assert.Equal(0xFFFF0000u, Pixel(frame, 0, 0));

		bus.WriteLong(view + ViewLofCprListOffset, 0);
		state.Cycles = frameCycles;
		bus.Display.BeginPresentationFrame(new PresentationFrameTarget(frame), frameCycles, 2 * frameCycles);
		try
		{
			Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE), state));
			bus.Display.CompletePresentationFrame(2 * frameCycles);
		}
		catch
		{
			bus.Display.AbortPresentationFrame();
			throw;
		}
		Assert.Equal(0xFF00FF00u, Pixel(frame, 0, 0));
	}

	[Fact]
	public void LoadViewDefersMidFrameCopperListPublicationUntilFrameBoundary()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint lofCprList = 0x2300;
		const uint copperList = 0x2400;
		WriteCopperColorList(bus, copperList, 0x0F00);
		bus.WriteLong(lofCprList + CprListStartOffset, copperList);
		bus.WriteLong(view + ViewLofCprListOffset, lofCprList);
		var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
		var state = new M68kCpuState { Cycles = frameCycles / 2 };
		state.A[1] = view;

		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE), state));
		Assert.Equal((ushort)0, bus.ReadWord(0x00DFF080));
		Assert.Equal((ushort)0, bus.ReadWord(0x00DFF082));
		Assert.Equal(frameCycles, InvokeGetNextSyntheticVBlankBoundaryCycle(boot, state.Cycles, frameCycles * 2));

		InvokeAdvanceSyntheticVBlankInterruptServers(boot, state.Cycles, frameCycles);

		Assert.Equal((ushort)(copperList >> 16), bus.ReadWord(0x00DFF080));
		Assert.Equal((ushort)copperList, bus.ReadWord(0x00DFF082));
	}

	[Fact]
	public void ExecFindNameResolvesHostBridgeLibraryBases()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;

		Assert.Equal(AmigaKickstartHost.DosLibraryBase, InvokeFindName(bus, "dos.library"));
		Assert.Equal(AmigaKickstartHost.GraphicsLibraryBase, InvokeFindName(bus, "graphics.library"));
		Assert.Equal(AmigaKickstartHost.IntuitionLibraryBase, InvokeFindName(bus, "intuition.library"));
		Assert.Equal(AmigaKickstartHost.ExpansionLibraryBase, InvokeFindName(bus, "expansion.library"));
		Assert.Equal(AmigaKickstartHost.CiaAResourceBase, InvokeFindName(bus, "ciaa.resource"));
		Assert.Equal(AmigaKickstartHost.CiaBResourceBase, InvokeFindName(bus, "ciab.resource"));
		Assert.Equal(AmigaKickstartHost.IconLibraryBase, InvokeFindName(bus, "icon.library"));
		Assert.Equal(AmigaKickstartHost.WorkbenchLibraryBase, InvokeFindName(bus, "workbench.library"));
		Assert.Equal(0u, InvokeFindName(bus, "MathIEEE.resource"));
	}

	[Fact]
	public void IconGatewayDispatchesDiskObjectAndToolTypeOperations()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var getObject = new M68kCpuState();
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IconLibraryBase, -78), getObject));
		Assert.NotEqual(0u, getObject.D[0]);

		var toolTypes = InvokeAllocMem(bus, 8, 0);
		var value = InvokeAllocMem(bus, 16, 0);
		var key = InvokeAllocMem(bus, 8, 0);
		WriteCString(bus, value, "STACK=4096");
		WriteCString(bus, key, "STACK");
		bus.WriteLong(toolTypes, value);
		bus.WriteLong(toolTypes + 4, 0);
		var find = new M68kCpuState();
		find.A[0] = toolTypes;
		find.A[1] = key;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IconLibraryBase, -96), find));
		Assert.Equal(value + 6, find.D[0]);
	}

	[Fact]
	public void ExpansionGatewayReturnsCompatibilityObject()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var state = new M68kCpuState();
		Assert.True(InvokeHostTrap(machine.Bus, Lvo(AmigaKickstartHost.ExpansionLibraryBase, -30), state));
		Assert.NotEqual(0u, state.D[0]);
	}

	[Fact]
	public void ExecListLvosMaintainGuestNodeLinks()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var list = InvokeAllocMem(bus, 14, 0);
		var first = InvokeAllocMem(bus, 16, 0);
		var second = InvokeAllocMem(bus, 16, 0);
		var third = InvokeAllocMem(bus, 16, 0);
		InitializeExecList(bus, list);

		InvokeExecList(bus, -240, list, first); // AddHead
		InvokeExecList(bus, -246, list, second); // AddTail
		Assert.Equal(first, bus.ReadLong(list));
		Assert.Equal(second, bus.ReadLong(list + 8));
		Assert.Equal(second, bus.ReadLong(first));
		Assert.Equal(first, bus.ReadLong(second + 4));

		var insert = new M68kCpuState();
		insert.A[0] = list;
		insert.A[1] = third;
		insert.A[2] = first;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -234), insert)); // Insert
		Assert.Equal(third, bus.ReadLong(first));
		Assert.Equal(second, bus.ReadLong(third));

		var remove = new M68kCpuState();
		remove.A[1] = third;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -252), remove)); // Remove
		Assert.Equal(third, remove.D[0]);
		Assert.Equal(second, bus.ReadLong(first));

		Assert.Equal(first, InvokeExecList(bus, -258, list, 0)); // RemHead
		Assert.Equal(second, InvokeExecList(bus, -264, list, 0)); // RemTail
		Assert.Equal(list + 4, bus.ReadLong(list));
		Assert.Equal(list, bus.ReadLong(list + 8));
	}

	[Fact]
	public void ExecAllocVecTracksItsGuestAllocationForFreeVec()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var before = InvokeAvailMem(bus, MemfPublic);
		var alloc = new M68kCpuState();
		alloc.D[0] = 64;
		alloc.D[1] = MemfPublic | MemfClear;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -684), alloc));
		Assert.NotEqual(0u, alloc.D[0]);
		Assert.Equal(0u, bus.ReadLong(alloc.D[0]));
		var free = new M68kCpuState();
		free.A[1] = alloc.D[0];
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -690), free));
		Assert.Equal(before, InvokeAvailMem(bus, MemfPublic));
	}

	[Fact]
	public void ExecMemoryPoolReleasesGuestPuddlesOnDelete()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var before = InvokeAvailMem(bus, MemfPublic);
		var create = new M68kCpuState(); create.D[0] = MemfPublic | MemfClear; create.D[1] = 256; create.D[2] = 128;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -696), create));
		Assert.NotEqual(0u, create.D[0]);
		var alloc = new M68kCpuState(); alloc.A[0] = create.D[0]; alloc.D[0] = 64;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -708), alloc));
		Assert.NotEqual(0u, alloc.D[0]);
		Assert.Equal(0u, bus.ReadLong(alloc.D[0]));
		var delete = new M68kCpuState(); delete.A[0] = create.D[0];
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -702), delete));
		Assert.Equal(before, InvokeAvailMem(bus, MemfPublic));
	}

	[Fact]
	public void ExecCopyMemHandlesOverlappingGuestRanges()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint source = 0x4000;
		bus.WriteByte(source, 1, 0); bus.WriteByte(source + 1, 2, 0); bus.WriteByte(source + 2, 3, 0);
		var state = new M68kCpuState(); state.A[0] = source; state.A[1] = source + 1; state.D[0] = 3;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -624), state));
		Assert.Equal((byte)1, bus.ReadByte(source + 1));
		Assert.Equal((byte)2, bus.ReadByte(source + 2));
		Assert.Equal((byte)3, bus.ReadByte(source + 3));
	}

	[Fact]
	public void RomExecMessagePortLvosUseGuestPortAndMessageLinks()
	{
		var machine = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		var execBase = InvokeAllocMem(bus, 0x240, 0);
		var task = InvokeAllocMem(bus, 0x60, 0);
		bus.WriteLong(execBase + ExecThisTaskOffset, task);
		InitializeExecList(bus, execBase + ExecPortListOffset);
		ActivateRomExec(boot, execBase);

		const uint port = 0x0000_4000;
		const uint replyPort = 0x0000_4040;
		const uint portName = 0x0000_4080;
		WriteCString(bus, portName, "worker.port");
		bus.WriteLong(port + MemNodeNameOffset, portName);
		bus.WriteLong(port + MsgPortSigTaskOffset, task);
		bus.WriteByte(port + MsgPortSigBitOffset, 5, 0);
		bus.WriteLong(replyPort + MsgPortSigTaskOffset, task);
		bus.WriteByte(replyPort + MsgPortSigBitOffset, 6, 0);
		InvokeExecPort(bus, -354, 0, port); // AddPort
		InvokeExecPort(bus, -354, 0, replyPort);
		Assert.Equal(port, InvokeExecPort(bus, -390, 0, portName)); // FindPort
		Assert.Equal(port, bus.ReadLong(execBase + ExecPortListOffset));

		const uint message = 0x0000_40A0;
		bus.WriteLong(message + MessageReplyPortOffset, replyPort);
		InvokeExecPort(bus, -366, port, message); // PutMsg
		Assert.Equal(message, bus.ReadLong(port + MsgPortMsgListOffset));
		Assert.NotEqual(0u, bus.ReadLong(task + TaskSigRecvdOffset) & (1u << 5));
		Assert.Equal(message, InvokeExecPort(bus, -384, port, 0)); // WaitPort
		Assert.Equal(message, InvokeExecPort(bus, -372, port, 0)); // GetMsg
		Assert.Equal(port + MsgPortMsgListOffset + 4, bus.ReadLong(port + MsgPortMsgListOffset));
		Assert.Equal(0u, bus.ReadLong(task + TaskSigRecvdOffset) & (1u << 5));

		InvokeExecPort(bus, -378, 0, message); // ReplyMsg
		Assert.Equal(message, bus.ReadLong(replyPort + MsgPortMsgListOffset));
		InvokeExecPort(bus, -360, 0, port); // RemPort
		Assert.NotEqual(port, bus.ReadLong(execBase + ExecPortListOffset));
	}

	[Fact]
	public void RomExecTaskLvosMaintainGuestReadyWaitListsAndNesting()
	{
		var machine = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		const uint execBase = 0x3000, current = 0x3400, added = 0x3500, stack = 0x3700, entry = 0x3800;
		bus.WriteLong(execBase + ExecThisTaskOffset, current);
		InitializeExecList(bus, execBase + ExecTaskReadyOffset);
		InitializeExecList(bus, execBase + ExecTaskWaitOffset);
		bus.WriteWord(execBase + 0x120, 1);
		bus.WriteLong(added + TaskStackPointerOffset, stack);
		bus.WriteWord(entry, 0x4E75);
		ActivateRomExec(boot, execBase);

		var add = new M68kCpuState();
		add.A[1] = added; add.A[2] = entry; add.A[3] = entry;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -282), add));
		Assert.Equal(added, add.D[0]);
		Assert.Equal(added, bus.ReadLong(execBase + ExecTaskReadyOffset));
		Assert.Equal((byte)3, bus.ReadByte(added + TaskStateOffset));
		Assert.Equal(stack - 4, bus.ReadLong(added + TaskStackPointerOffset));
		Assert.Equal(entry, bus.ReadLong(stack - 4));

		var wait = new M68kCpuState();
		wait.D[0] = 1u << 4; wait.A[7] = 0x3F00;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -318), wait));
		Assert.Equal(current, bus.ReadLong(execBase + ExecTaskWaitOffset));
		Assert.Equal(1u << 4, bus.ReadLong(current + TaskSigWaitOffset));
		Assert.Equal(Lvo(execBase, -54), wait.ProgramCounter);
		Assert.Equal(ExecWaitResumeGatewayAddress, bus.ReadLong(wait.A[7]));
		var signal = new M68kCpuState(); signal.A[1] = current; signal.D[0] = 1u << 4;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -324), signal));
		Assert.Equal(current, bus.ReadLong(execBase + ExecTaskReadyOffset + 8));
		Assert.Equal(0u, bus.ReadLong(current + TaskSigWaitOffset));

		Assert.Equal(0u, InvokeExecPort(bus, -132, 0, 0));
		Assert.Equal((byte)1, bus.ReadByte(execBase + 0x127));
		Assert.Equal(0u, InvokeExecPort(bus, -138, 0, 0));
		Assert.Equal((byte)0, bus.ReadByte(execBase + 0x127));
	}

	[Fact]
	public void RomExecCurrentRemTaskEntersNativeSwitchWithoutReturningToTheRemovedTask()
	{
		var machine = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		const uint execBase = 0x3000, current = 0x3400;
		bus.WriteLong(execBase + ExecThisTaskOffset, current);
		InitializeExecList(bus, execBase + ExecTaskReadyOffset);
		InitializeExecList(bus, execBase + ExecTaskWaitOffset);
		ActivateRomExec(boot, execBase);

		var remove = new M68kCpuState();
		remove.A[7] = 0x3F00;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -288), remove));
		Assert.Equal(Lvo(execBase, -54), remove.ProgramCounter);
		Assert.Equal(ExecWaitResumeGatewayAddress, bus.ReadLong(remove.A[7]));
	}

	[Fact]
	public void RomExecTaskSignalAndTrapLvosUseOnlyTheActiveTaskFields()
	{
		var machine = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		const uint execBase = 0x3000, current = 0x3400, target = 0x3500, currentName = 0x3600, targetName = 0x3640;
		WriteCString(bus, currentName, "current.task");
		WriteCString(bus, targetName, "target.task");
		bus.WriteLong(execBase + ExecThisTaskOffset, current);
		bus.WriteLong(current + MemNodeNameOffset, currentName);
		bus.WriteLong(target + MemNodeNameOffset, targetName);
		bus.WriteByte(target + 9, 1, 0);
		InitializeExecList(bus, execBase + ExecTaskReadyOffset);
		InitializeExecList(bus, execBase + ExecTaskWaitOffset);
		bus.WriteLong(target, execBase + ExecTaskReadyOffset + 4);
		bus.WriteLong(target + 4, execBase + ExecTaskReadyOffset);
		bus.WriteLong(execBase + ExecTaskReadyOffset, target);
		bus.WriteLong(execBase + ExecTaskReadyOffset + 8, target);
		ActivateRomExec(boot, execBase);

		var find = new M68kCpuState();
		find.A[1] = targetName;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -294), find));
		Assert.Equal(target, find.D[0]);

		var priority = new M68kCpuState();
		priority.A[1] = target; priority.D[0] = unchecked((uint)-3);
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -300), priority));
		Assert.Equal(1u, priority.D[0]);
		Assert.Equal(-3, unchecked((sbyte)bus.ReadByte(target + 9)));

		var set = new M68kCpuState();
		set.D[0] = 0x0000_0005; set.D[1] = 0x0000_000F;
		bus.WriteLong(current + TaskSigRecvdOffset, 0x0000_00F0);
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -306), set));
		Assert.Equal(0x0000_00F0u, set.D[0]);
		Assert.Equal(0x0000_00F5u, bus.ReadLong(current + TaskSigRecvdOffset));

		var allocSignal = new M68kCpuState();
		allocSignal.D[0] = 6;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -330), allocSignal));
		Assert.Equal(6u, allocSignal.D[0]);
		Assert.NotEqual(0u, bus.ReadLong(current + 0x12) & (1u << 6));
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -336), allocSignal));
		Assert.Equal(0u, bus.ReadLong(current + 0x12) & (1u << 6));

		var trap = new M68kCpuState();
		trap.D[0] = 3;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -342), trap));
		Assert.Equal(3u, trap.D[0]);
		Assert.Equal(0x0008, bus.ReadWord(current + TaskTrapAllocOffset));
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -348), trap));
		Assert.Equal(0, bus.ReadWord(current + TaskTrapAllocOffset));
	}

	[Fact]
	public void RomExecSemaphoreLvosBlockThroughWaitAndReleaseTheFirstQueuedTask()
	{
		var machine = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		const uint execBase = 0x3000, owner = 0x3400, waiter = 0x3500, semaphore = 0x3600;
		bus.WriteLong(execBase + ExecThisTaskOffset, owner);
		InitializeExecList(bus, execBase + ExecTaskReadyOffset);
		InitializeExecList(bus, execBase + ExecTaskWaitOffset);
		ActivateRomExec(boot, execBase);

		var initialize = new M68kCpuState();
		initialize.A[0] = semaphore;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -558), initialize));
		Assert.Equal(semaphore + SemaphoreWaitQueueOffset + 4, bus.ReadLong(semaphore + SemaphoreWaitQueueOffset));
		Assert.Equal(0, unchecked((short)bus.ReadWord(semaphore + SemaphoreNestCountOffset)));

		var obtain = new M68kCpuState();
		obtain.A[0] = semaphore;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -564), obtain));
		Assert.Equal(owner, bus.ReadLong(semaphore + SemaphoreOwnerOffset));
		Assert.Equal(1, unchecked((short)bus.ReadWord(semaphore + SemaphoreNestCountOffset)));

		// Recursive obtains remain owned by the same task.
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -564), obtain));
		Assert.Equal(2, unchecked((short)bus.ReadWord(semaphore + SemaphoreNestCountOffset)));

		bus.WriteLong(execBase + ExecThisTaskOffset, waiter);
		var attempt = new M68kCpuState();
		attempt.A[0] = semaphore;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -576), attempt));
		Assert.Equal(0u, attempt.D[0]);

		var blocked = new M68kCpuState();
		blocked.A[0] = semaphore;
		blocked.A[7] = 0x3F00;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -564), blocked));
		Assert.Equal(1, unchecked((short)bus.ReadWord(semaphore + SemaphoreQueueCountOffset)));
		Assert.Equal(waiter, bus.ReadLong(execBase + ExecTaskWaitOffset));
		Assert.Equal(Lvo(execBase, -54), blocked.ProgramCounter);
		Assert.Equal(ExecWaitResumeGatewayAddress, bus.ReadLong(blocked.A[7]));

		bus.WriteLong(execBase + ExecThisTaskOffset, owner);
		var release = new M68kCpuState();
		release.A[0] = semaphore;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -570), release));
		Assert.Equal(1, unchecked((short)bus.ReadWord(semaphore + SemaphoreNestCountOffset)));
		Assert.Equal(owner, bus.ReadLong(semaphore + SemaphoreOwnerOffset));
		Assert.Equal(1, unchecked((short)bus.ReadWord(semaphore + SemaphoreQueueCountOffset)));

		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -570), release));
		Assert.Equal(waiter, bus.ReadLong(semaphore + SemaphoreOwnerOffset));
		Assert.Equal(1, unchecked((short)bus.ReadWord(semaphore + SemaphoreNestCountOffset)));
		Assert.Equal(0, unchecked((short)bus.ReadWord(semaphore + SemaphoreQueueCountOffset)));
		Assert.Equal(waiter, bus.ReadLong(execBase + ExecTaskReadyOffset));
	}

	[Fact]
	public void RomExecSharedSemaphoreLvosTrackSharedOwnersAndAttemptExclusively()
	{
		var machine = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		const uint execBase = 0x3000, first = 0x3400, second = 0x3500, exclusive = 0x3600, semaphore = 0x3700;
		bus.WriteLong(execBase + ExecThisTaskOffset, first);
		InitializeExecList(bus, execBase + ExecTaskReadyOffset);
		InitializeExecList(bus, execBase + ExecTaskWaitOffset);
		ActivateRomExec(boot, execBase);

		var initialize = new M68kCpuState();
		initialize.A[0] = semaphore;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -558), initialize));
		var shared = new M68kCpuState();
		shared.A[0] = semaphore;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -678), shared));
		Assert.Equal(-1, unchecked((short)bus.ReadWord(semaphore + SemaphoreNestCountOffset)));
		Assert.Equal(0u, bus.ReadLong(semaphore + SemaphoreOwnerOffset));

		bus.WriteLong(execBase + ExecThisTaskOffset, second);
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -678), shared));
		Assert.Equal(-2, unchecked((short)bus.ReadWord(semaphore + SemaphoreNestCountOffset)));

		bus.WriteLong(execBase + ExecThisTaskOffset, exclusive);
		var attempt = new M68kCpuState();
		attempt.A[0] = semaphore;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -576), attempt));
		Assert.Equal(0u, attempt.D[0]);

		var release = new M68kCpuState();
		release.A[0] = semaphore;
		bus.WriteLong(execBase + ExecThisTaskOffset, first);
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -570), release));
		Assert.Equal(-1, unchecked((short)bus.ReadWord(semaphore + SemaphoreNestCountOffset)));
		bus.WriteLong(execBase + ExecThisTaskOffset, second);
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -570), release));
		Assert.Equal(0, unchecked((short)bus.ReadWord(semaphore + SemaphoreNestCountOffset)));

		bus.WriteLong(execBase + ExecThisTaskOffset, exclusive);
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -576), attempt));
		Assert.Equal(1u, attempt.D[0]);
		Assert.Equal(exclusive, bus.ReadLong(semaphore + SemaphoreOwnerOffset));

		bus.WriteLong(execBase + ExecThisTaskOffset, first);
		var sharedAttempt = new M68kCpuState();
		sharedAttempt.A[0] = semaphore;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -720), sharedAttempt));
		Assert.Equal(0u, sharedAttempt.D[0]);

		bus.WriteLong(execBase + ExecThisTaskOffset, exclusive);
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -570), release));
		bus.WriteLong(execBase + ExecThisTaskOffset, first);
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -720), sharedAttempt));
		Assert.Equal(1u, sharedAttempt.D[0]);
	}

	[Fact]
	public void RomExecSetExceptMutatesOnlyTheSelectedTaskSignalBits()
	{
		var machine = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		const uint execBase = 0x3000, task = 0x3400;
		bus.WriteLong(execBase + ExecThisTaskOffset, task);
		bus.WriteLong(task + TaskSigExceptOffset, 0x0000_00F0);
		ActivateRomExec(boot, execBase);
		var state = new M68kCpuState(); state.A[1] = task; state.D[0] = 0x0000_0003; state.D[1] = 0x0000_000F;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -312), state));
		Assert.Equal(0x0000_00F0u, state.D[0]);
		Assert.Equal(0x0000_00F3u, bus.ReadLong(task + TaskSigExceptOffset));
	}

	[Fact]
	public void RomExecLibraryDeviceAndResourceLvosMutateOnlyGuestLists()
	{
		var machine = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		const uint execBase = 0x3000, library = 0x3400, device = 0x3500, resource = 0x3600;
		const uint libraryName = 0x3700, resourceName = 0x3740;
		InitializeExecList(bus, execBase + ExecLibListOffset);
		InitializeExecList(bus, execBase + ExecDeviceListOffset);
		InitializeExecList(bus, execBase + ExecResourceListOffset);
		WriteCString(bus, libraryName, "test.library");
		WriteCString(bus, resourceName, "test.resource");
		bus.WriteLong(library + MemNodeNameOffset, libraryName);
		bus.WriteWord(library + LibraryVersionOffset, 40);
		bus.WriteLong(device + MemNodeNameOffset, libraryName);
		bus.WriteLong(resource + MemNodeNameOffset, resourceName);
		ActivateRomExec(boot, execBase);

		Assert.Equal(0u, InvokeExecPort(bus, -396, 0, library)); // AddLibrary
		Assert.Equal(library, bus.ReadLong(execBase + ExecLibListOffset));
		var open = new M68kCpuState();
		open.A[1] = libraryName;
		open.D[0] = 40;
		open.A[7] = 0x3900;
		bus.WriteLong(open.A[7], 0x3A00);
		bus.WriteWord(library - 6, 0x4E75); // library Open vector
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -552), open));
		Assert.Equal(library - 6, open.ProgramCounter);
		Assert.Equal(0x38FCu, open.A[7]);
		Assert.NotEqual(0x3A00u, bus.ReadLong(open.A[7]));
		Assert.Equal(0u, InvokeExecPort(bus, -432, 0, device)); // AddDevice
		Assert.Equal(device, bus.ReadLong(execBase + ExecDeviceListOffset));
		Assert.Equal(0u, InvokeExecPort(bus, -486, 0, resource)); // AddResource
		Assert.Equal(resource, bus.ReadLong(execBase + ExecResourceListOffset));
		Assert.Equal(resource, InvokeExecPort(bus, -498, 0, resourceName)); // OpenResource

		Assert.Equal(0u, InvokeExecPort(bus, -492, 0, resource)); // RemResource
		Assert.Equal(execBase + ExecResourceListOffset + 4, bus.ReadLong(execBase + ExecResourceListOffset));
		Assert.Equal(0u, InvokeExecPort(bus, -402, 0, library)); // RemLibrary
		Assert.Equal(execBase + ExecLibListOffset + 4, bus.ReadLong(execBase + ExecLibListOffset));
	}

	[Fact]
	public void TrackdiskDeviceOverlaysOnlyItsLiveDeviceVectorsAndCompletesReadAndMotorRequests()
	{
		var machine = new Machine(MachineOptions.ForProfile(MachineProfile.A500Pal512KBoot).WithLiveAgnusDma(false));
		var bus = machine.Bus;
		const uint execBase = 0x3000, device = 0x3500, name = 0x3600, request = 0x3700, destination = 0x3800;
		var disk = Enumerable.Range(0, 1024).Select(value => (byte)value).ToArray();
		var rawTrack = Enumerable.Range(0, 32).Select(value => (byte)(0xA0 + value)).ToArray();
		TrackdiskRawTrack? writtenRawTrack = null;
		ulong changeVersion = 1;
		var diskPresent = true;
		var motorOn = false;
		var writeProtected = false;
		var replies = new List<uint>();
		InitializeExecList(bus, execBase + ExecDeviceListOffset);
		WriteCString(bus, name, "trackdisk.device");
		bus.WriteLong(device + MemNodeNameOffset, name);
		bus.WriteLong(device, execBase + ExecDeviceListOffset + 4);
		bus.WriteLong(device + 4, execBase + ExecDeviceListOffset);
		bus.WriteLong(execBase + ExecDeviceListOffset, device);
		bus.WriteLong(execBase + ExecDeviceListOffset + 8, device);

		using var trackdisk = new TrackdiskDeviceServices(
			bus,
			unit => unit == 0 && diskPresent ? disk : null,
			(unit, offset, source) =>
			{
				if (unit != 0 || offset < 0 || offset > disk.Length || source.Length > disk.Length - offset)
				{
					return false;
				}

				source.CopyTo(disk.AsSpan(offset, source.Length));
				return true;
			},
			unit => unit == 0 && diskPresent ? new TrackdiskRawTrack(rawTrack, rawTrack.Length * 8) : null,
			(unit, track) =>
			{
				if (unit != 0)
				{
					return false;
				}

				writtenRawTrack = track;
				return true;
			},
			unit => unit == 0 ? changeVersion : 0,
			unit => { if (unit == 0) { diskPresent = false; changeVersion++; } },
			unit => unit == 0 && writeProtected,
			unit => unit == 0 && motorOn,
			(unit, enabled, _) => { if (unit == 0) motorOn = enabled; },
			reply => replies.Add(reply),
			_ => { });
		Assert.True(trackdisk.TryInstall(execBase));
		Assert.True(bus.HasHostGateway(device - 6));
		Assert.True(bus.HasHostGateway(device - 30));

		var open = new M68kCpuState();
		open.A[1] = request;
		Assert.True(InvokeHostTrap(bus, device - 6, open));
		Assert.Equal(0u, open.D[0]);
		Assert.Equal(device, bus.ReadLong(request + 0x14));

		bus.WriteWord(request + 0x1C, 2);
		bus.WriteByte(request + 0x1E, 1, 0); // native DoIO sets IOF_QUICK
		bus.WriteLong(request + 0x24, 4);
		bus.WriteLong(request + 0x28, destination);
		bus.WriteLong(request + 0x2C, 8);
		var begin = new M68kCpuState();
		begin.A[1] = request;
		Assert.True(InvokeHostTrap(bus, device - 30, begin));
		Assert.Equal(0, bus.ReadByte(request + 0x1F));
		Assert.Equal(4u, bus.ReadLong(request + 0x20));
		Assert.Equal(0x08090A0Bu, bus.ReadLong(destination));
		Assert.NotEqual(0, bus.ReadByte(request + 0x1E) & 1);

		// TD64 offsets use io_Actual as the high longword and io_Offset as the
		// low longword. Standard DD media accepts the in-range low-32-bit form.
		bus.WriteWord(request + 0x1C, 24); // TD_READ64
		bus.WriteLong(request + 0x20, 0);
		bus.WriteLong(request + 0x28, destination + 12);
		bus.WriteLong(request + 0x2C, 8);
		Assert.True(InvokeHostTrap(bus, device - 30, begin));
		Assert.Equal(0, bus.ReadByte(request + 0x1F));
		Assert.Equal(4u, bus.ReadLong(request + 0x20));
		Assert.Equal(0x08090A0Bu, bus.ReadLong(destination + 12));
		bus.WriteLong(request + 0x20, 1); // offset 0x00000001_00000008 is outside DD media.
		Assert.True(InvokeHostTrap(bus, device - 30, begin));
		Assert.Equal(0xFD, bus.ReadByte(request + 0x1F));

		bus.WriteByte(request + 0x1E, 0, 0); // SendIO path: completion is deferred to a boundary.
		bus.WriteWord(request + 0x1C, 2); // CMD_READ
		bus.WriteLong(request + 0x28, destination + 4);
		Assert.True(InvokeHostTrap(bus, device - 30, begin));
		Assert.Equal(0u, bus.ReadLong(destination + 4));
		trackdisk.ProcessPending(0);
		Assert.Equal(0x08090A0Bu, bus.ReadLong(destination + 4));
		Assert.Equal([request], replies);

		// TD_RAWREAD reads encoded MFM bytes from the live drive track instead
		// of exposing the logical ADF sector image.
		bus.WriteByte(request + 0x1E, 1, 0);
		bus.WriteWord(request + 0x1C, 16); // TD_RAWREAD
		bus.WriteLong(request + 0x24, 4);
		bus.WriteLong(request + 0x28, destination + 8);
		bus.WriteLong(request + 0x2C, 3);
		Assert.True(InvokeHostTrap(bus, device - 30, begin));
		Assert.Equal(0, bus.ReadByte(request + 0x1F));
		Assert.Equal(4u, bus.ReadLong(request + 0x20));
		Assert.Equal(0xA3A4A5A6u, bus.ReadLong(destination + 8));

		// TD_RAWWRITE routes a new encoded stream through the drive callback;
		// it never writes the logical sector image directly.
		const uint rawSource = 0x3A00;
		bus.WriteLong(rawSource, 0x11223344u);
		bus.WriteWord(request + 0x1C, 17); // TD_RAWWRITE
		bus.WriteLong(request + 0x24, 4);
		bus.WriteLong(request + 0x28, rawSource);
		bus.WriteLong(request + 0x2C, 0);
		Assert.True(InvokeHostTrap(bus, device - 30, begin));
		Assert.Equal(0, bus.ReadByte(request + 0x1F));
		Assert.Equal(4u, bus.ReadLong(request + 0x20));
		Assert.True(writtenRawTrack.HasValue);
		Assert.Equal(32, writtenRawTrack.Value.BitLength);
		Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 0x44 }, writtenRawTrack.Value.Data.ToArray());

		// Change-interrupt registrations are keyed by the caller's Interrupt
		// structure. Removal prevents a later media-generation notification;
		// a new registration launches guest code at the next outer boundary.
		const uint changeInterrupt = 0x3B00, changeData = 0x3B40, changeCode = 0x3B80;
		bus.WriteLong(changeInterrupt + 0x0E, changeData);
		bus.WriteLong(changeInterrupt + 0x12, changeCode);
		bus.WriteWord(request + 0x1C, 20); // TD_ADDCHANGEINT
		bus.WriteLong(request + 0x28, changeInterrupt);
		Assert.True(InvokeHostTrap(bus, device - 30, begin));
		Assert.Equal(0, bus.ReadByte(request + 0x1F));
		bus.WriteWord(request + 0x1C, 21); // TD_REMCHANGEINT
		Assert.True(InvokeHostTrap(bus, device - 30, begin));
		changeVersion++;
		var changeState = new M68kCpuState { ProgramCounter = 0x3C00 };
		changeState.A[7] = 0x3E00;
		trackdisk.ProcessPending(changeState);
		Assert.Equal(0x3C00u, changeState.ProgramCounter);

		bus.WriteWord(request + 0x1C, 20); // TD_ADDCHANGEINT
		Assert.True(InvokeHostTrap(bus, device - 30, begin));
		changeVersion++;
		trackdisk.ProcessPending(changeState);
		Assert.Equal(changeCode, changeState.ProgramCounter);
		Assert.Equal(changeData, changeState.A[1]);
		Assert.True(bus.HasHostGateway(TrackdiskDeviceServices.ChangeInterruptContinuationAddress));
		Assert.True(InvokeHostTrap(bus, TrackdiskDeviceServices.ChangeInterruptContinuationAddress, changeState));
		bus.WriteWord(request + 0x1C, 21); // TD_REMCHANGEINT
		Assert.True(InvokeHostTrap(bus, device - 30, begin));

		bus.WriteWord(request + 0x1C, 9);
		bus.WriteByte(request + 0x1E, 1, 0);
		bus.WriteLong(request + 0x24, 1);
		Assert.True(InvokeHostTrap(bus, device - 30, begin));
		Assert.True(motorOn);
		Assert.Equal(0u, bus.ReadLong(request + 0x20));

		// Removable-media status and standard DD geometry are exposed
		// through the same BeginIO path as a ROM caller would use.
		bus.WriteWord(request + 0x1C, 15); // TD_PROTSTATUS
		Assert.True(InvokeHostTrap(bus, device - 30, begin));
		Assert.Equal(0, bus.ReadByte(request + 0x1F));
		Assert.Equal(0u, bus.ReadLong(request + 0x20));

		bus.WriteWord(request + 0x1C, 18); // TD_GETDRIVETYPE
		Assert.True(InvokeHostTrap(bus, device - 30, begin));
		Assert.Equal(0, bus.ReadByte(request + 0x1F));
		Assert.Equal(0u, bus.ReadLong(request + 0x20)); // DRV_35_DD

		bus.WriteWord(request + 0x1C, 19); // TD_GETNUMTRACKS
		Assert.True(InvokeHostTrap(bus, device - 30, begin));
		Assert.Equal(0, bus.ReadByte(request + 0x1F));
		Assert.Equal(160u, bus.ReadLong(request + 0x20));

		bus.WriteWord(request + 0x1C, 14); // TD_CHANGESTATE
		Assert.True(InvokeHostTrap(bus, device - 30, begin));
		Assert.Equal(0u, bus.ReadLong(request + 0x20));

		bus.WriteWord(request + 0x1C, 10); // TD_SEEK
		bus.WriteLong(request + 0x2C, 512);
		Assert.True(InvokeHostTrap(bus, device - 30, begin));
		Assert.Equal(512u, bus.ReadLong(request + 0x20));

		const uint geometry = 0x3900;
		bus.WriteWord(request + 0x1C, 22); // TD_GETGEOMETRY
		bus.WriteLong(request + 0x28, geometry);
		Assert.True(InvokeHostTrap(bus, device - 30, begin));
		Assert.Equal(0, bus.ReadByte(request + 0x1F));
		Assert.Equal(48u, bus.ReadLong(request + 0x20));
		Assert.Equal(512u, bus.ReadLong(geometry));
		Assert.Equal(2u, bus.ReadLong(geometry + 4));
		Assert.Equal(2u, bus.ReadLong(geometry + 44));

		// CMD_WRITE updates the logical image atomically. A protected drive
		// rejects the same request with the standard write-protect error.
		const uint source = 0x3A00;
		bus.WriteLong(source, 0xDEADBEEFu);
		bus.WriteWord(request + 0x1C, 3); // CMD_WRITE
		bus.WriteLong(request + 0x24, 4);
		bus.WriteLong(request + 0x28, source);
		bus.WriteLong(request + 0x2C, 16);
		Assert.True(InvokeHostTrap(bus, device - 30, begin));
		Assert.Equal(0, bus.ReadByte(request + 0x1F));
		Assert.Equal(4u, bus.ReadLong(request + 0x20));
		Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, disk[16..20]);

		// TD_FORMAT uses the same logical-media path. It is intentionally not a
		// raw-MFM operation; that requires the encoded-track layer.
		bus.WriteLong(source, 0x01020304u);
		bus.WriteWord(request + 0x1C, 11); // TD_FORMAT
		bus.WriteLong(request + 0x2C, 24);
		Assert.True(InvokeHostTrap(bus, device - 30, begin));
		Assert.Equal(0, bus.ReadByte(request + 0x1F));
		Assert.Equal(4u, bus.ReadLong(request + 0x20));
		Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04 }, disk[24..28]);

		bus.WriteLong(source, 0x55667788u);
		bus.WriteWord(request + 0x1C, 25); // TD_WRITE64
		bus.WriteLong(request + 0x20, 0);
		bus.WriteLong(request + 0x2C, 32);
		Assert.True(InvokeHostTrap(bus, device - 30, begin));
		Assert.Equal(0, bus.ReadByte(request + 0x1F));
		Assert.Equal(4u, bus.ReadLong(request + 0x20));
		Assert.Equal(new byte[] { 0x55, 0x66, 0x77, 0x88 }, disk[32..36]);

		bus.WriteLong(source, 0x99AABBCCu);
		bus.WriteWord(request + 0x1C, 27); // TD_FORMAT64
		bus.WriteLong(request + 0x20, 0);
		bus.WriteLong(request + 0x2C, 40);
		Assert.True(InvokeHostTrap(bus, device - 30, begin));
		Assert.Equal(0, bus.ReadByte(request + 0x1F));
		Assert.Equal(4u, bus.ReadLong(request + 0x20));
		Assert.Equal(new byte[] { 0x99, 0xAA, 0xBB, 0xCC }, disk[40..44]);

		bus.WriteWord(request + 0x1C, 26); // TD_SEEK64
		bus.WriteLong(request + 0x20, 0);
		bus.WriteLong(request + 0x2C, 512);
		Assert.True(InvokeHostTrap(bus, device - 30, begin));
		Assert.Equal(512u, bus.ReadLong(request + 0x20));

		// New-style-device commands are deliberately not aliases for TD64.
		bus.WriteWord(request + 0x1C, 0xC000); // NSCMD_TD_READ64
		Assert.True(InvokeHostTrap(bus, device - 30, begin));
		Assert.Equal(0xFB, bus.ReadByte(request + 0x1F));

		writeProtected = true;
		bus.WriteWord(request + 0x1C, 15); // TD_PROTSTATUS
		Assert.True(InvokeHostTrap(bus, device - 30, begin));
		Assert.Equal(uint.MaxValue, bus.ReadLong(request + 0x20));
		bus.WriteWord(request + 0x1C, 11); // TD_FORMAT
		bus.WriteLong(request + 0x2C, 20);
		Assert.True(InvokeHostTrap(bus, device - 30, begin));
		Assert.Equal(0xFC, bus.ReadByte(request + 0x1F));
		Assert.Equal(0u, bus.ReadLong(request + 0x20));
		Assert.Equal(20, disk[20]);

		// TD_REMOVE retains the legacy single change-interrupt pointer. TD_EJECT
		// changes media state and generation, then dispatches that guest handler
		// at the following host-device boundary.
		bus.WriteWord(request + 0x1C, 12); // TD_REMOVE
		bus.WriteLong(request + 0x28, changeInterrupt);
		Assert.True(InvokeHostTrap(bus, device - 30, begin));
		Assert.Equal(0u, bus.ReadLong(request + 0x20));
		bus.WriteWord(request + 0x1C, 13); // TD_CHANGENUM
		Assert.True(InvokeHostTrap(bus, device - 30, begin));
		Assert.Equal((uint)changeVersion, bus.ReadLong(request + 0x20));
		bus.WriteWord(request + 0x1C, 23); // TD_EJECT
		Assert.True(InvokeHostTrap(bus, device - 30, begin));
		Assert.False(diskPresent);
		bus.WriteWord(request + 0x1C, 14); // TD_CHANGESTATE
		Assert.True(InvokeHostTrap(bus, device - 30, begin));
		Assert.Equal(uint.MaxValue, bus.ReadLong(request + 0x20));
		bus.WriteWord(request + 0x1C, 13); // TD_CHANGENUM
		Assert.True(InvokeHostTrap(bus, device - 30, begin));
		Assert.Equal((uint)changeVersion, bus.ReadLong(request + 0x20));
		changeState.ProgramCounter = 0x3C00;
		trackdisk.ProcessPending(changeState);
		Assert.Equal(changeCode, changeState.ProgramCounter);

		trackdisk.Dispose();
		Assert.False(bus.HasHostGateway(device - 6));
	}

	[Fact]
	public void SetWindowTitlesPublishesSyntheticIntuitionTitleBitmap()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		bus.EnableLiveAgnusDma();
		var openScreenState = new M68kCpuState();
		var openWindowState = new M68kCpuState();
		var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
		var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

		bus.Display.BeginPresentationFrame(new PresentationFrameTarget(frame), 0, frameCycles);
		try
		{
			Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198), openScreenState));
			Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -204), openWindowState));
			bus.Display.CompletePresentationFrame(frameCycles);
		}
		catch
		{
			bus.Display.AbortPresentationFrame();
			throw;
		}
		AssertSyntheticScreenBitMapFields(bus, openScreenState.D[0]);
		var windowRastPort = bus.ReadLong(openWindowState.D[0] + WindowRPortOffset);
		Assert.NotEqual(0u, windowRastPort);
		Assert.Equal(
			openScreenState.D[0] + ScreenBitMapOffset,
			bus.ReadLong(windowRastPort + RastPortBitMapOffset));
		Assert.True(
			CountPixelsExcept(frame, 0xFF000000u) > 100,
			"OpenScreen/OpenWindow should publish a visible synthetic screen before any title update.");

		var titleAddress = InvokeAllocMem(bus, 64, 0);
		WriteCString(bus, titleAddress, "Loading Hired Guns");

		var titleState = new M68kCpuState();
		titleState.A[0] = openWindowState.D[0];
		titleState.A[1] = titleAddress;
		titleState.Cycles = frameCycles;
		long titleInvocationCycle;
		bus.Display.BeginPresentationFrame(new PresentationFrameTarget(frame), frameCycles, 2 * frameCycles);
		try
		{
			Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -276), titleState));
			titleInvocationCycle = titleState.Cycles;
			bus.Display.CompletePresentationFrame(2 * frameCycles);
		}
		catch
		{
			bus.Display.AbortPresentationFrame();
			throw;
		}

		Assert.NotEqual(0u, openScreenState.D[0]);
		Assert.NotEqual(0u, openWindowState.D[0]);
		Assert.InRange(titleInvocationCycle, frameCycles, 2 * frameCycles);
		var display = bus.Display.CaptureSnapshot();
		var nonBlackPixels = CountPixelsExcept(frame, 0xFF000000u);
		var whitePixels = CountColorPixels(frame, 0xFFFFFFFFu);
		Assert.True(
			nonBlackPixels > 100 && whitePixels > 100,
			$"Expected a visible synthetic title bitmap; nonBlack={nonBlackPixels}, white={whitePixels}, " +
			$"bplcon0=0x{display.Bplcon0:X4}, color00=0x{display.Colors[0]:X4}, bitplanePixels={display.LastBitplaneNonZeroPixels}.");
	}

	[Fact]
	public void LoadRgb4UpdatesSyntheticScreenCopperPalette()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var openScreenState = new M68kCpuState();
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198), openScreenState));
		var screen = openScreenState.D[0];
		var colors = InvokeAllocMem(bus, 8, 0);
		bus.WriteWord(colors, 0x0888);
		bus.WriteWord(colors + 2, 0x000F);
		bus.WriteWord(colors + 4, 0x00F0);
		bus.WriteWord(colors + 6, 0x0F00);
		var loadRgbState = new M68kCpuState();
		loadRgbState.A[0] = screen + ScreenViewPortOffset;
		loadRgbState.A[1] = colors;
		loadRgbState.D[0] = 4;

		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -192), loadRgbState));

		var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
		bus.Display.RenderFrame(frame);
		Assert.Equal(0xFF888888u, Pixel(frame, 0, 80));
	}

	[Fact]
	public void OpenScreenHonorsHighResolutionNewScreenGeometry()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var newScreen = InvokeAllocMem(bus, 0x20, 0);
		bus.WriteWord(newScreen + NewScreenWidthOffset, 640);
		bus.WriteWord(newScreen + NewScreenHeightOffset, 200);
		bus.WriteByte(newScreen + NewScreenDepthOffset, 2, 0);
		bus.WriteWord(newScreen + NewScreenViewModesOffset, ViewModeHires);
		var openScreenState = new M68kCpuState();
		openScreenState.A[0] = newScreen;

		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198), openScreenState));

		var screen = openScreenState.D[0];
		var bitMap = screen + ScreenBitMapOffset;
		var viewPort = screen + ScreenViewPortOffset;
		Assert.Equal(640, bus.ReadWord(screen + ScreenWidthOffset));
		Assert.Equal(200, bus.ReadWord(screen + ScreenHeightOffset));
		Assert.Equal(80, bus.ReadWord(bitMap + BitMapBytesPerRowOffset));
		Assert.Equal(200, bus.ReadWord(bitMap + BitMapRowsOffset));
		Assert.Equal(2, bus.ReadByte(bitMap + BitMapDepthOffset));
		Assert.Equal(640, bus.ReadWord(viewPort + ViewPortDWidthOffset));
		Assert.Equal(200, bus.ReadWord(viewPort + ViewPortDHeightOffset));
		Assert.Equal(ViewModeHires, bus.ReadWord(viewPort + ViewPortModesOffset));
	}

	[Fact]
	public void SyntheticMouseClickQueuesGadgetUpIntuiMessage()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		var openScreenState = new M68kCpuState();
		var openWindowState = new M68kCpuState();
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198), openScreenState));
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -204), openWindowState));
		var gadget = InvokeAllocMem(bus, 0x40, 0);
		bus.WriteLong(gadget + GadgetNextOffset, 0);
		bus.WriteWord(gadget + GadgetLeftEdgeOffset, 40);
		bus.WriteWord(gadget + GadgetTopEdgeOffset, 50);
		bus.WriteWord(gadget + GadgetWidthOffset, 80);
		bus.WriteWord(gadget + GadgetHeightOffset, 16);
		bus.WriteWord(gadget + GadgetIdOffset, 0x1234);
		var addGListState = new M68kCpuState();
		addGListState.A[0] = openWindowState.D[0];
		addGListState.A[1] = gadget;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -438), addGListState));

		boot.SetSyntheticMousePosition(60, 55);
		boot.SetSyntheticMouseButtons(primaryPressed: true, secondPressed: false);
		boot.SetSyntheticMouseButtons(primaryPressed: false, secondPressed: false);
		var getMsgState = new M68kCpuState();

		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -372), getMsgState));

		var message = getMsgState.D[0];
		Assert.NotEqual(0u, message);
		Assert.Equal(IdcmpGadgetUp, bus.ReadLong(message + IntuiMessageClassOffset));
		Assert.Equal(0x1234, bus.ReadWord(message + IntuiMessageCodeOffset));
		Assert.Equal(gadget, bus.ReadLong(message + IntuiMessageIAddressOffset));
		Assert.Equal(60, unchecked((short)bus.ReadWord(message + IntuiMessageMouseXOffset)));
		Assert.Equal(55, unchecked((short)bus.ReadWord(message + IntuiMessageMouseYOffset)));
	}

	[Fact]
	public void SyntheticMouseClickQueuesGadgetDownAndUpWhenIdcmpRequestsBoth()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		var openScreenState = new M68kCpuState();
		var openWindowState = new M68kCpuState();
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198), openScreenState));
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -204), openWindowState));
		var gadget = InvokeAllocMem(bus, 0x40, 0);
		bus.WriteWord(gadget + GadgetLeftEdgeOffset, 40);
		bus.WriteWord(gadget + GadgetTopEdgeOffset, 50);
		bus.WriteWord(gadget + GadgetWidthOffset, 80);
		bus.WriteWord(gadget + GadgetHeightOffset, 16);
		bus.WriteWord(gadget + GadgetIdOffset, 0x0007);
		var addGListState = new M68kCpuState();
		addGListState.A[0] = openWindowState.D[0];
		addGListState.A[1] = gadget;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -438), addGListState));
		var modifyIdcmpState = new M68kCpuState();
		modifyIdcmpState.A[0] = openWindowState.D[0];
		modifyIdcmpState.D[0] = IdcmpGadgetDown | IdcmpGadgetUp;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -150), modifyIdcmpState));

		boot.SetSyntheticMousePosition(60, 55);
		boot.SetSyntheticMouseButtons(primaryPressed: true, secondPressed: false);
		boot.SetSyntheticMouseButtons(primaryPressed: false, secondPressed: false);
		var getDownState = new M68kCpuState();
		var getUpState = new M68kCpuState();

		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -372), getDownState));
		Assert.Equal(IdcmpGadgetDown, bus.ReadLong(getDownState.D[0] + IntuiMessageClassOffset));
		Assert.Equal(0x0007, bus.ReadWord(getDownState.D[0] + IntuiMessageCodeOffset));
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -372), getUpState));

		Assert.Equal(IdcmpGadgetUp, bus.ReadLong(getUpState.D[0] + IntuiMessageClassOffset));
		Assert.Equal(0x0007, bus.ReadWord(getUpState.D[0] + IntuiMessageCodeOffset));
	}

	[Fact]
	public void EmptySyntheticWaitPortRetriesTrapAtNextFrame()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		var waitPort = Lvo(AmigaKickstartHost.ExecLibraryBase, -384);
		var state = new M68kCpuState
		{
			LastInstructionProgramCounter = waitPort,
			ProgramCounter = waitPort + 4,
			Cycles = 1234
		};

		Assert.True(InvokeHostTrap(bus, waitPort, state));

		Assert.Equal(0u, state.D[0]);
		Assert.Equal(waitPort, state.ProgramCounter);
		Assert.True(state.Cycles >= AmigaConstants.A500PalCpuCyclesPerFrame);
	}

	[Fact]
	public void ExecAddIntServerTicksSyntheticVBlankCounterAtFrameBoundaries()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		var interrupt = InvokeAllocMem(bus, 0x20, 0);
		var counter = InvokeAllocMem(bus, 4, 0);
		bus.WriteLong(interrupt + InterruptDataOffset, counter);
		bus.WriteLong(interrupt + InterruptCodeOffset, 0x0000_2000);
		var addState = new M68kCpuState();
		addState.D[0] = VBlankInterruptNumber;
		addState.A[1] = interrupt;

		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -168), addState));
		Assert.Equal(
			AmigaConstants.A500PalCpuCyclesPerFrame,
			InvokeGetNextSyntheticVBlankBoundaryCycle(
				boot,
				0,
				AmigaConstants.A500PalCpuCyclesPerFrame * 3L + 42));
		InvokeAdvanceSyntheticVBlankInterruptServers(
			boot,
			0,
			AmigaConstants.A500PalCpuCyclesPerFrame * 3L + 42);

		Assert.Equal(3u, bus.ReadLong(counter));

		var remState = new M68kCpuState();
		remState.D[0] = VBlankInterruptNumber;
		remState.A[1] = interrupt;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -174), remState));
		InvokeAdvanceSyntheticVBlankInterruptServers(
			boot,
			AmigaConstants.A500PalCpuCyclesPerFrame * 3L + 42,
			AmigaConstants.A500PalCpuCyclesPerFrame * 5L);

		Assert.Equal(3u, bus.ReadLong(counter));
	}

	[Fact]
	public void GraphicsWaitTofAdvancesToNextFrameBoundary()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var state = new M68kCpuState();
		state.Cycles = 1234;

		Assert.True(InvokeHostTrap(machine.Bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -270), state));

		Assert.Equal(0u, state.D[0]);
		Assert.Equal(AmigaConstants.A500PalCpuCyclesPerFrame, state.Cycles);
	}

	[Fact]
	public void GraphicsOpenFontReturnsSyntheticFontWithMetrics()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var state = new M68kCpuState();

		Assert.True(InvokeHostTrap(machine.Bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -72), state));

		var font = state.D[0];
		Assert.NotEqual(0u, font);
		Assert.Equal(8, machine.Bus.ReadWord(font + 0x14));
		Assert.Equal((byte)7, machine.Bus.ReadByte(font + 0x16));
		Assert.Equal((byte)8, machine.Bus.ReadByte(font + 0x17));
	}

	[Fact]
	public void SyntheticPresentationClickMapsToHighResolutionGadgetCoordinates()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		var newScreen = InvokeAllocMem(bus, 0x20, 0);
		bus.WriteWord(newScreen + NewScreenWidthOffset, 640);
		bus.WriteWord(newScreen + NewScreenHeightOffset, 256);
		bus.WriteByte(newScreen + NewScreenDepthOffset, 2, 0);
		bus.WriteWord(newScreen + NewScreenViewModesOffset, ViewModeHires);
		var openScreenState = new M68kCpuState();
		openScreenState.A[0] = newScreen;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198), openScreenState));
		var openWindowState = new M68kCpuState();
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -204), openWindowState));
		var gadget = InvokeAllocMem(bus, 0x40, 0);
		bus.WriteWord(gadget + GadgetLeftEdgeOffset, 500);
		bus.WriteWord(gadget + GadgetTopEdgeOffset, 178);
		bus.WriteWord(gadget + GadgetWidthOffset, 76);
		bus.WriteWord(gadget + GadgetHeightOffset, 18);
		bus.WriteWord(gadget + GadgetIdOffset, 0x0002);
		var addGListState = new M68kCpuState();
		addGListState.A[0] = openWindowState.D[0];
		addGListState.A[1] = gadget;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -438), addGListState));

		boot.SetSyntheticMousePresentationPosition(
			(AmigaConstants.PalLowResOverscanBorderX * 2) + 520,
			(AmigaConstants.PalLowResOverscanBorderY * 2) + (184 * 2));
		boot.SetSyntheticMouseButtons(primaryPressed: true, secondPressed: false);
		boot.SetSyntheticMouseButtons(primaryPressed: false, secondPressed: false);
		var getMsgState = new M68kCpuState();

		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -372), getMsgState));

		var message = getMsgState.D[0];
		Assert.NotEqual(0u, message);
		Assert.Equal(0x0002, bus.ReadWord(message + IntuiMessageCodeOffset));
		Assert.Equal(gadget, bus.ReadLong(message + IntuiMessageIAddressOffset));
		Assert.Equal(520, unchecked((short)bus.ReadWord(message + IntuiMessageMouseXOffset)));
		Assert.Equal(184, unchecked((short)bus.ReadWord(message + IntuiMessageMouseYOffset)));
	}

	[Fact]
	public void OpenWindowPublishesSyntheticUserPortAndModifyIdcmpFlags()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		var openScreenState = new M68kCpuState();
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198), openScreenState));
		var openWindowState = new M68kCpuState();
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -204), openWindowState));
		var window = openWindowState.D[0];
		var userPort = bus.ReadLong(window + WindowUserPortOffset);
		var signalBit = bus.ReadByte(userPort + MsgPortSigBitOffset);

		Assert.NotEqual(0u, userPort);
		Assert.True(signalBit < 32);
		Assert.Equal(bus.ReadLong(AmigaKickstartHost.ExecLibraryBase + ExecThisTaskOffset), bus.ReadLong(userPort + MsgPortSigTaskOffset));
		Assert.Equal(userPort + MsgPortMsgListOffset + 4, bus.ReadLong(userPort + MsgPortMsgListOffset));
		Assert.Equal(userPort + MsgPortMsgListOffset, bus.ReadLong(userPort + MsgPortMsgListOffset + 8));

		var modifyState = new M68kCpuState();
		modifyState.A[0] = window;
		modifyState.D[0] = IdcmpGadgetUp;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -150), modifyState));

		Assert.Equal(IdcmpGadgetUp, bus.ReadLong(window + WindowIdcmpFlagsOffset));
		Assert.Equal(1u, modifyState.D[0]);
	}

	[Fact]
	public void MakeVPortWritesCprListsToKickstartViewOffsets()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint viewPort = 0x2300;
		bus.WriteLong(view + 0x0C, 0xDEAD_BEEFu);
		bus.WriteLong(view + 0x10, 0xCAFE_BABEu);
		WriteMinimalViewPort(bus, viewPort);
		var state = new M68kCpuState();
		state.A[0] = view;
		state.A[1] = viewPort;

		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD8), state));

		var lofCprList = bus.ReadLong(view + ViewLofCprListOffset);
		var shfCprList = bus.ReadLong(view + ViewShfCprListOffset);
		Assert.Equal(viewPort, bus.ReadLong(view + ViewViewPortOffset));
		Assert.NotEqual(0u, lofCprList);
		Assert.Equal(lofCprList, shfCprList);
		Assert.Equal(0xDEAD_BEEFu, bus.ReadLong(view + 0x0C));
		Assert.Equal(0xCAFE_BABEu, bus.ReadLong(view + 0x10));
		Assert.Equal(bus.ReadLong(lofCprList + CprListStartOffset), bus.ReadLong(viewPort + ViewPortDspInsOffset));
	}

	[Fact]
	public void MakeVPortHonorsRasInfoSourceOffsetsInBitplanePointers()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint viewPort = 0x2300;
		const uint rasInfo = 0x2380;
		const uint bitMap = 0x23A0;
		const uint plane = 0x3000;
		bus.WriteWord(viewPort + 0x18, 16);
		bus.WriteWord(viewPort + 0x1A, 1);
		bus.WriteLong(viewPort + 0x24, rasInfo);
		bus.WriteLong(rasInfo + 0x04, bitMap);
		bus.WriteWord(rasInfo + 0x0A, 1);
		bus.WriteWord(bitMap + 0x00, 2);
		bus.WriteWord(bitMap + 0x02, 2);
		bus.WriteByte(bitMap + 0x05, 1, 0);
		bus.WriteLong(bitMap + 0x08, plane);
		bus.WriteWord(plane, 0x0000);
		bus.WriteWord(plane + 2, 0x8000);
		var state = new M68kCpuState();
		state.A[0] = view;
		state.A[1] = viewPort;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD8), state));

		var copperList = bus.ReadLong(bus.ReadLong(view + ViewLofCprListOffset) + CprListStartOffset);
		Assert.Equal(0x0000, ReadCopperMoveValue(bus, copperList, 0x00E0));
		Assert.Equal(0x3002, ReadCopperMoveValue(bus, copperList, 0x00E2));
	}

	[Fact]
	public void InitViewClearsKickstartViewHeaderOnly()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		for (var offset = 0; offset < 0x20; offset++)
		{
			bus.WriteByte(view + (uint)offset, 0xAA, 0);
		}

		var state = new M68kCpuState();
		state.A[1] = view;

		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0x168), state));
		for (var offset = 0; offset < ViewStructSize; offset++)
		{
			Assert.Equal((byte)0, bus.ReadByte(view + (uint)offset));
		}

		Assert.Equal((byte)0xAA, bus.ReadByte(view + ViewStructSize));
		Assert.Equal((byte)0xAA, bus.ReadByte(view + 0x15));
	}

	[Fact]
	public void ExecMakeFunctionsBuildsAbsoluteAndRelativeJumpTables()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint target = 0x2400;
		const uint absoluteTable = 0x2200;
		const uint absoluteFunction = 0x2800;
		bus.WriteWord(absoluteFunction, 0x4E75);
		bus.WriteLong(absoluteTable, absoluteFunction);
		bus.WriteLong(absoluteTable + 4, 0xFFFF_FFFF);
		var absolute = new M68kCpuState();
		absolute.A[0] = target;
		absolute.A[1] = absoluteTable;

		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -90), absolute));
		Assert.Equal(6u, absolute.D[0]);
		Assert.Equal((ushort)0x4EF9, bus.ReadWord(target - 6));
		Assert.Equal(absoluteFunction, bus.ReadLong(target - 4));

		const uint relativeTable = 0x2220;
		const uint relativeBase = 0x2900;
		bus.WriteWord(relativeBase + 8, 0x4E75);
		bus.WriteWord(relativeTable, 8);
		bus.WriteWord(relativeTable + 2, 0xFFFF);
		var relative = new M68kCpuState();
		relative.A[0] = target;
		relative.A[1] = relativeTable;
		relative.A[2] = relativeBase;

		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -90), relative));
		Assert.Equal(6u, relative.D[0]);
		Assert.Equal(relativeBase + 8, bus.ReadLong(target - 4));
	}

	[Fact]
	public void ExecMakeLibraryCreatesAlignedLibraryBaseAndVectorSpace()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint vectors = 0x2200;
		const uint function = 0x2300;
		bus.WriteWord(function, 0x4E75);
		bus.WriteLong(vectors, function);
		bus.WriteLong(vectors + 4, 0xFFFF_FFFF);
		var state = new M68kCpuState();
		state.A[0] = vectors;
		state.D[0] = 0x31;

		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -84), state));
		var library = state.D[0];
		Assert.NotEqual(0u, library);
		Assert.Equal((ushort)0x4EF9, bus.ReadWord(library - 6));
		Assert.Equal(function, bus.ReadLong(library - 4));
		Assert.Equal((ushort)6, bus.ReadWord(library + 0x10));
		Assert.Equal((ushort)0x34, bus.ReadWord(library + 0x12));
	}

	[Theory]
	[InlineData(9, ExecLibListOffset)]
	[InlineData(3, ExecDeviceListOffset)]
	public void ExecInitResidentCreatesAndLinksAutoinitLibraryOrDevice(byte residentType, int listOffset)
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint resident = 0x2200;
		const uint initTable = 0x2300;
		const uint vectors = 0x2400;
		const uint function = 0x2500;
		bus.WriteWord(function, 0x4E75);
		bus.WriteLong(vectors, function);
		bus.WriteLong(vectors + 4, 0xFFFF_FFFF);
		bus.WriteWord(resident, 0x4AFC);
		bus.WriteLong(resident + 2, resident);
		bus.WriteLong(resident + 6, resident + 0x20);
		bus.WriteByte(resident + 0x0A, 0x80, 0);
		bus.WriteByte(resident + 0x0C, residentType, 0);
		bus.WriteLong(resident + 0x16, initTable);
		bus.WriteLong(initTable, 0x30);
		bus.WriteLong(initTable + 4, vectors);
		bus.WriteLong(initTable + 8, 0);
		bus.WriteLong(initTable + 12, 0);
		var state = new M68kCpuState();
		state.A[1] = resident;

		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -102), state));
		var library = state.D[0];
		Assert.NotEqual(0u, library);
		var libraryList = AmigaKickstartHost.ExecLibraryBase + (uint)listOffset;
		Assert.Equal(library, bus.ReadLong(libraryList));
		Assert.Equal(libraryList + 4, bus.ReadLong(library));
	}

	private static Machine StartBootShim(MachineProfile profile)
	{
		var machine = new Machine(MachineOptions
			.ForProfile(profile)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		return machine;
	}

	private static uint InvokeAllocMem(AmigaBus bus, uint byteCount, uint flags)
	{
		var state = new M68kCpuState();
		state.D[0] = byteCount;
		state.D[1] = flags;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -198), state));
		return state.D[0];
	}

	private static uint InvokeAllocAbs(AmigaBus bus, uint byteCount, uint location)
	{
		var state = new M68kCpuState();
		state.D[0] = byteCount;
		state.A[1] = location;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -204), state));
		return state.D[0];
	}

	private static uint InvokeFindName(AmigaBus bus, string name)
	{
		var nameAddress = InvokeAllocMem(bus, (uint)name.Length + 1, 0);
		WriteCString(bus, nameAddress, name);
		var state = new M68kCpuState();
		state.A[1] = nameAddress;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -276), state));
		return state.D[0];
	}

	private static void InitializeExecList(AmigaBus bus, uint list)
	{
		bus.WriteLong(list, list + 4);
		bus.WriteLong(list + 4, 0);
		bus.WriteLong(list + 8, list);
	}

	private static uint InvokeExecList(AmigaBus bus, int lvo, uint list, uint node)
	{
		var state = new M68kCpuState();
		state.A[0] = list;
		state.A[1] = node;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, lvo), state));
		return state.D[0];
	}

	private static uint InvokeExecPort(AmigaBus bus, int lvo, uint a0, uint a1)
	{
		var state = new M68kCpuState();
		state.A[0] = a0;
		state.A[1] = a1;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, lvo), state));
		return state.D[0];
	}

	private static void ActivateRomExec(AmigaBootController boot, uint execBase)
	{
		var type = typeof(AmigaBootController);
		type.GetField("_activeExecBase", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(boot, execBase);
		var stateField = type.GetField("_kickstartRomExecTakeoverState", BindingFlags.Instance | BindingFlags.NonPublic)!;
		stateField.SetValue(boot, Enum.Parse(stateField.FieldType, "Active"));
	}

	private static void AssertSyntheticScreenBitMapFields(AmigaBus bus, uint screenAddress)
	{
		Assert.NotEqual(0u, screenAddress);
		Assert.Equal(AmigaConstants.PalLowResWidth, bus.ReadWord(screenAddress + ScreenWidthOffset));
		Assert.Equal(256, bus.ReadWord(screenAddress + ScreenHeightOffset));

		var bitMapAddress = screenAddress + ScreenBitMapOffset;
		Assert.Equal(((AmigaConstants.PalLowResWidth + 15) & ~15) / 8, bus.ReadWord(bitMapAddress + BitMapBytesPerRowOffset));
		Assert.Equal(256, bus.ReadWord(bitMapAddress + BitMapRowsOffset));
		Assert.Equal((byte)2, bus.ReadByte(bitMapAddress + BitMapDepthOffset));
		Assert.InRange(bus.ReadLong(bitMapAddress + BitMapPlanesOffset), 1u, (uint)bus.ChipRam.Length - 1);
		Assert.InRange(bus.ReadLong(bitMapAddress + BitMapPlanesOffset + 4), 1u, (uint)bus.ChipRam.Length - 1);
	}

	private static uint InvokeAvailMem(AmigaBus bus, uint flags)
	{
		var state = new M68kCpuState();
		state.D[1] = flags;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -216), state));
		return state.D[0];
	}

	private static void InvokeFreeMem(AmigaBus bus, uint address, uint byteCount)
	{
		var state = new M68kCpuState();
		state.A[1] = address;
		state.D[0] = byteCount;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -210), state));
		Assert.Equal(0u, state.D[0]);
	}

	private static void InvokeInstallBootHostTraps(AmigaBootController boot)
	{
		var method = typeof(AmigaBootController).GetMethod(
			"InstallBootHostTraps",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(method);
		method.Invoke(boot, Array.Empty<object>());
	}

	private static void InvokeEnsureTaskTrapVectorsCurrent(AmigaBootController boot)
	{
		var method = typeof(AmigaBootController).GetMethod(
			"EnsureTaskTrapVectorsCurrent",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(method);
		method.Invoke(boot, Array.Empty<object>());
	}

	private static void InvokeAdvanceSyntheticVBlankInterruptServers(
		AmigaBootController boot,
		long previousCycle,
		long currentCycle)
	{
		var method = typeof(AmigaBootController).GetMethod(
			"AdvanceSyntheticVBlankInterruptServers",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(method);
		method.Invoke(boot, new object[] { previousCycle, currentCycle });
	}

	private static long InvokeGetNextSyntheticVBlankBoundaryCycle(
		AmigaBootController boot,
		long currentCycle,
		long targetCycle)
	{
		var method = typeof(AmigaBootController).GetMethod(
			"GetNextSyntheticVBlankBoundaryCycle",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(method);
		return (long)method.Invoke(boot, new object[] { currentCycle, targetCycle })!;
	}

	private static bool InvokeRecoverHostTaskTrapFromZeroVector(AmigaBootController boot)
	{
		var method = typeof(AmigaBootController).GetMethod(
			"TryRecoverHostTaskTrapFromZeroVector",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(method);
		return (bool)method.Invoke(boot, Array.Empty<object>())!;
	}

	private static void AssertExecBaseStaticFields(AmigaBus bus, uint maxLocalMemory, uint maxExtendedMemory)
	{
		var execBase = AmigaKickstartHost.ExecLibraryBase;
		Assert.Equal(34, bus.ReadWord(execBase + ExecSoftVerOffset));
		Assert.Equal(ComputeLowMemoryVectorChecksum(bus), bus.ReadWord(execBase + ExecLowMemChkSumOffset));
		Assert.Equal(~execBase, bus.ReadLong(execBase + ExecChkBaseOffset));
		Assert.Equal(0x400u, bus.ReadLong(execBase + ExecSysStkUpperOffset));
		Assert.Equal(0u, bus.ReadLong(execBase + ExecSysStkLowerOffset));
		Assert.Equal(maxLocalMemory, bus.ReadLong(execBase + ExecMaxLocMemOffset));
		Assert.Equal(maxExtendedMemory, bus.ReadLong(execBase + ExecMaxExtMemOffset));
		Assert.Equal(0, SumExecBaseStaticWords(bus));
	}

	private static void AssertMemoryHeader(
		AmigaBus bus,
		uint header,
		uint successor,
		uint predecessor,
		uint attributes,
		uint lower,
		uint upper,
		string name)
	{
		var freeBytes = upper - lower;
		Assert.Equal(successor, bus.ReadLong(header));
		Assert.Equal(predecessor, bus.ReadLong(header + 4));
		Assert.Equal((ushort)attributes, bus.ReadWord(header + MemHeaderAttributesOffset));
		Assert.Equal(lower, bus.ReadLong(header + MemHeaderLowerOffset));
		Assert.Equal(upper, bus.ReadLong(header + MemHeaderUpperOffset));
		Assert.Equal(freeBytes, bus.ReadLong(header + MemHeaderFreeOffset));
		Assert.Equal(lower, bus.ReadLong(header + MemHeaderFirstChunkOffset));
		Assert.Equal(0u, bus.ReadLong(lower + MemChunkNextOffset));
		Assert.Equal(freeBytes, bus.ReadLong(lower + MemChunkBytesOffset));
		Assert.Equal(name, ReadCString(bus, bus.ReadLong(header + MemNodeNameOffset), 16));
	}

	private static string ReadCString(AmigaBus bus, uint address, int maxLength)
	{
		var chars = new char[maxLength];
		var count = 0;
		for (; count < chars.Length; count++)
		{
			var value = bus.ReadByte(address + (uint)count);
			if (value == 0)
			{
				break;
			}

			chars[count] = (char)value;
		}

		return new string(chars, 0, count);
	}

	private static ushort ComputeLowMemoryVectorChecksum(AmigaBus bus)
	{
		var sum = 0;
		for (var address = 0u; address < 0x400; address += 2)
		{
			sum = (sum + bus.ReadWord(address)) & 0xFFFF;
		}

		return unchecked((ushort)-sum);
	}

	private static int SumExecBaseStaticWords(AmigaBus bus)
	{
		var execBase = AmigaKickstartHost.ExecLibraryBase;
		var sum = 0;
		for (var offset = ExecSoftVerOffset; offset <= ExecChkSumOffset; offset += 2)
		{
			sum = (sum + bus.ReadWord(execBase + (uint)offset)) & 0xFFFF;
		}

		return sum;
	}

	private static AmigaDiskImage CreateBootableDisk()
	{
		var data = new byte[AmigaDiskImage.StandardAdfSize];
		data[0] = (byte)'D';
		data[1] = (byte)'O';
		data[2] = (byte)'S';
		BigEndian.WriteUInt32(data, 4, CalculateBootChecksum(data.AsSpan(0, 1024)));
		return AmigaDiskImage.FromAdfBytes(data);
	}

	private static byte[] CreateMinimalKickstartRom()
	{
		var rom = new byte[512 * 1024];
		BigEndian.WriteUInt32(rom, 0, 0x0000_0400);
		BigEndian.WriteUInt32(rom, 4, 0x00F8_0000);
		return rom;
	}

	private static AmigaDiskImage CreateTrapVectorToStackPageDisk()
	{
		var data = new byte[AmigaDiskImage.StandardAdfSize];
		data[0] = (byte)'D';
		data[1] = (byte)'O';
		data[2] = (byte)'S';

		var offset = 0x0C;
		WriteWord(data, ref offset, 0x23FC); // MOVE.L #handler,$90
		WriteLong(data, ref offset, 0x0007_C030);
		WriteLong(data, ref offset, 0x0000_0090);
		WriteWord(data, ref offset, 0x4E44); // TRAP #4
		WriteWord(data, ref offset, 0x4EF9); // JMP $0
		WriteLong(data, ref offset, 0x0000_0000);

		offset = 0x30;
		WriteWord(data, ref offset, 0x23FC); // MOVE.L #$33FCBEEF,$400
		WriteLong(data, ref offset, 0x33FC_BEEF);
		WriteLong(data, ref offset, 0x0000_0400);
		WriteWord(data, ref offset, 0x23FC); // MOVE.L #$00000500,$404
		WriteLong(data, ref offset, 0x0000_0500);
		WriteLong(data, ref offset, 0x0000_0404);
		WriteWord(data, ref offset, 0x33FC); // MOVE.W #RTE,$408
		WriteWord(data, ref offset, 0x4E73);
		WriteLong(data, ref offset, 0x0000_0408);
		WriteWord(data, ref offset, 0x4EF9); // JMP $400
		WriteLong(data, ref offset, 0x0000_0400);

		BigEndian.WriteUInt32(data, 4, CalculateBootChecksum(data.AsSpan(0, 1024)));
		return AmigaDiskImage.FromAdfBytes(data);
	}

	private static AmigaDiskImage CreateCurrentTaskTrapCodeDisk(uint currentTaskAddress)
	{
		var data = new byte[AmigaDiskImage.StandardAdfSize];
		data[0] = (byte)'D';
		data[1] = (byte)'O';
		data[2] = (byte)'S';

		var offset = 0x0C;
		WriteWord(data, ref offset, 0x23FC); // MOVE.L #handler,tc_TrapCode(current task)
		WriteLong(data, ref offset, 0x0007_C030);
		WriteLong(data, ref offset, currentTaskAddress + TaskTrapCodeOffset);
		WriteWord(data, ref offset, 0x4E44); // TRAP #4
		WriteWord(data, ref offset, 0x4EF9); // JMP $0
		WriteLong(data, ref offset, 0x0000_0000);

		offset = 0x30;
		WriteWord(data, ref offset, 0x33FC); // MOVE.W #$BEEF,$500
		WriteWord(data, ref offset, 0xBEEF);
		WriteLong(data, ref offset, 0x0000_0500);
		WriteWord(data, ref offset, 0x588F); // ADDQ.L #4,A7
		WriteWord(data, ref offset, 0x4E73); // RTE

		BigEndian.WriteUInt32(data, 4, CalculateBootChecksum(data.AsSpan(0, 1024)));
		return AmigaDiskImage.FromAdfBytes(data);
	}

	private static void WriteWord(byte[] data, ref int offset, ushort value)
	{
		BigEndian.WriteUInt16(data, offset, value);
		offset += 2;
	}

	private static void WriteLong(byte[] data, ref int offset, uint value)
	{
		BigEndian.WriteUInt32(data, offset, value);
		offset += 4;
	}

	private static uint CalculateBootChecksum(ReadOnlySpan<byte> bootBlock)
	{
		var sum = 0u;
		for (var offset = 0; offset < 1024; offset += 4)
		{
			var value = BigEndian.ReadUInt32(bootBlock, offset, "boot checksum word");
			var previous = sum;
			sum += value;
			if (sum < previous)
			{
				sum++;
			}
		}

		return ~sum;
	}

	private static uint Lvo(uint libraryBase, int displacement)
	{
		return unchecked((uint)((int)libraryBase + displacement));
	}

	private static bool InvokeHostTrap(AmigaBus bus, uint address, M68kCpuState state)
	{
		if (bus.ReadWord(address) != 0xFF00)
		{
			return false;
		}

		return bus.TryInvokeHostGateway(address, bus.ReadLong(address + 2), state);
	}

	private static void CompleteInitialHostTrackdiskRead(Machine machine)
	{
		var bus = machine.Bus;
		var io = AmigaBootController.BootIoRequestAddress;
		bus.WriteWord(io + BootIoCommandOffset, AmigaBootController.CmdRead);
		bus.WriteLong(io + BootIoLengthOffset, 1024);
		bus.WriteLong(io + BootIoDataOffset, AmigaBootController.BootBlockAddress);
		bus.WriteLong(io + BootIoOffsetOffset, 0);
		var state = new M68kCpuState();
		state.A[1] = io;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -456), state));
		Assert.Equal(0u, state.D[0]);
	}

	private static void SetPrivateBoolean(AmigaBootController boot, string fieldName, bool value)
	{
		var field = typeof(AmigaBootController).GetField(
			fieldName,
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(field);
		field.SetValue(boot, value);
	}

	private static void WriteCopperColorList(AmigaBus bus, uint address, ushort color)
	{
		bus.WriteWord(address, 0x0180);
		bus.WriteWord(address + 2, color);
		bus.WriteWord(address + 4, 0xFFFF);
		bus.WriteWord(address + 6, 0xFFFE);
	}

	private static ushort ReadCopperMoveValue(AmigaBus bus, uint copperList, ushort register)
	{
		for (var offset = 0u; offset < 0x100; offset += 4)
		{
			var first = bus.ReadWord(copperList + offset);
			var second = bus.ReadWord(copperList + offset + 2);
			if (first == 0xFFFF && second == 0xFFFE)
			{
				break;
			}

			if ((first & 0x01FE) == register)
			{
				return second;
			}
		}

		throw new InvalidOperationException($"Copper MOVE ${register:X4} was not emitted.");
	}

	private static void WriteMinimalViewPort(AmigaBus bus, uint viewPort)
	{
		const uint rasInfo = 0x2380;
		const uint bitMap = 0x23A0;
		const uint plane = 0x23C0;
		bus.WriteWord(viewPort + 0x18, 16);
		bus.WriteWord(viewPort + 0x1A, 1);
		bus.WriteLong(viewPort + 0x24, rasInfo);
		bus.WriteLong(rasInfo + 0x04, bitMap);
		bus.WriteWord(bitMap + 0x00, 2);
		bus.WriteWord(bitMap + 0x02, 1);
		bus.WriteByte(bitMap + 0x05, 1, 0);
		bus.WriteLong(bitMap + 0x08, plane);
		bus.WriteWord(plane, 0x8000);
	}

	private static void WriteCString(AmigaBus bus, uint address, string value)
	{
		for (var i = 0; i < value.Length; i++)
		{
			bus.WriteByte(address + (uint)i, (byte)value[i], 0);
		}

		bus.WriteByte(address + (uint)value.Length, 0, 0);
	}

	private static uint Pixel(uint[] frame, int x, int y)
	{
		return frame[(y * AmigaConstants.PalLowResWidth) + x];
	}

	private static int CountColorPixels(uint[] frame, uint color)
	{
		var count = 0;
		for (var i = 0; i < frame.Length; i++)
		{
			if (frame[i] == color)
			{
				count++;
			}
		}

		return count;
	}

	private static int CountPixelsExcept(uint[] frame, uint color)
	{
		var count = 0;
		for (var i = 0; i < frame.Length; i++)
		{
			if (frame[i] != color)
			{
				count++;
			}
		}

		return count;
	}
}
