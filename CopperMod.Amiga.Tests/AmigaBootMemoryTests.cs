using System.Reflection;
using CopperMod.Amiga;

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
	private const int MemNodeNameOffset = 0x0A;
	private const int MemHeaderAttributesOffset = 0x0E;
	private const int MemHeaderFirstChunkOffset = 0x10;
	private const int MemHeaderLowerOffset = 0x14;
	private const int MemHeaderUpperOffset = 0x18;
	private const int MemHeaderFreeOffset = 0x1C;
	private const int ExecThisTaskOffset = 0x114;
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
	private const ushort ViewModeHires = 0x8000;
	private const uint IdcmpGadgetDown = 0x0000_0020;
	private const uint IdcmpGadgetUp = 0x0000_0040;
	private const uint MemfPublic = 0x0000_0001;
	private const uint MemfChip = 0x0000_0002;
	private const uint MemfFast = 0x0000_0004;
	private const uint MemfClear = 0x0001_0000;

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
		var machine = StartBootShim(AmigaMachineProfile.A500Pal512KBoot);
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
	public void BootShimAddsRealFastBeforePseudoFastInKickstartMemList()
	{
		var machine = new AmigaMachine(AmigaMachineOptions
			.ForProfile(AmigaMachineProfile.A500Pal512KBoot)
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
		var machine = new AmigaMachine(AmigaMachineOptions
			.ForProfile(AmigaMachineProfile.A500Pal512KBoot)
			.WithRealFastRam(8 * 1024 * 1024)
			.WithKickstart(AmigaKickstartConfiguration.FromRomImage(
				AmigaKickstartVersion.Kickstart20,
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
		var machine = new AmigaMachine(AmigaMachineOptions
			.ForProfile(AmigaMachineProfile.A500Pal512KBoot)
			.WithKickstart(AmigaKickstartConfiguration.FromRomImage(AmigaKickstartVersion.Kickstart13, new byte[8]))
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);

		boot.StartWorkbenchSession(CreateBootableDisk());

		Assert.Equal(AmigaKickstartBackendKind.RomImage, machine.Kickstart.Configuration.Backend);
		Assert.Equal(AmigaKickstartHost.ExecStructAddress, machine.Bus.ReadLong(0));
		Assert.Equal(AmigaKickstartHost.ExecLibraryBase, machine.Bus.ReadLong(4));
		Assert.Equal(AmigaKickstartHost.ExecLibraryBase, machine.Cpu.State.A[6]);
		Assert.True(machine.Bus.HasHostTrapStub(Lvo(AmigaKickstartHost.ExecLibraryBase, -408)));
		Assert.True(machine.Bus.HasHostTrapStub(Lvo(AmigaKickstartHost.DosLibraryBase, -30)));
		Assert.True(machine.Bus.HasHostTrapStub(Lvo(AmigaKickstartHost.DosLibraryBase, -798)));
	}

	[Fact]
	public void ChipOnlyBootProfileKeepsMemListMetadataOutOfPublicLowMemory()
	{
		var machine = StartBootShim(AmigaMachineProfile.A500Pal512KChipOnlyBoot);
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
		var machine = StartBootShim(AmigaMachineProfile.A500Pal512KBoot);
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
		var machine = StartBootShim(AmigaMachineProfile.A500Pal512KChipOnlyBoot);
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
		var machine = StartBootShim(AmigaMachineProfile.A500Pal512KBoot);
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
		var machine = StartBootShim(AmigaMachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var expectedAddress = ChipPublicLowerAddress;
		bus.WriteLong(expectedAddress, 0xAABBCCDD);

		var allocation = InvokeAllocMem(bus, 0x10, MemfPublic | MemfChip);

		Assert.Equal(expectedAddress, allocation);
		Assert.Equal(0xAABBCCDDu, bus.ReadLong(allocation));

		machine = StartBootShim(AmigaMachineProfile.A500Pal512KBoot);
		bus = machine.Bus;
		bus.WriteLong(expectedAddress, 0xAABBCCDD);

		allocation = InvokeAllocMem(bus, 0x10, MemfPublic | MemfChip | MemfClear);

		Assert.Equal(expectedAddress, allocation);
		Assert.Equal(0u, bus.ReadLong(allocation));
	}

	[Fact]
	public void AllocAbsReservesFixedAddressFromKickstartMemList()
	{
		var machine = StartBootShim(AmigaMachineProfile.A500Pal512KBoot);
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
		var machine = StartBootShim(AmigaMachineProfile.A500Pal512KBoot);
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
		var machine = StartBootShim(AmigaMachineProfile.A500Pal512KBoot);
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
		var machine = StartBootShim(AmigaMachineProfile.A500Pal512KBoot);
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
		var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));
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
		var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));
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
		var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;

		bus.WriteLong(2u * 4u, 0);
		bus.WriteLong(11u * 4u, 0);
		bus.WriteLong(32u * 4u, 0);
		machine.Cpu.State.VectorBaseRegister = 0x00F8_0000;

		InvokeEnsureTaskTrapVectorsCurrent(boot);

		Assert.Equal(0u, machine.Cpu.State.VectorBaseRegister);
		Assert.True(bus.HasHostTrapStub(bus.ReadLong(2u * 4u)));
		Assert.True(bus.HasHostTrapStub(bus.ReadLong(11u * 4u)));
		Assert.True(bus.HasHostTrapStub(bus.ReadLong(32u * 4u)));
		Assert.NotEqual(0u, bus.ReadLong(2u * 4u));
		Assert.NotEqual(0u, bus.ReadLong(11u * 4u));
		Assert.NotEqual(0u, bus.ReadLong(32u * 4u));
	}

	[Fact]
	public void HostLineFTaskTrapSkipsDecodedM68040FpuProbe()
	{
		var machine = StartBootShim(AmigaMachineProfile.A500Pal512KBoot);
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
		var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));
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
		var machine = StartBootShim(AmigaMachineProfile.A500Pal512KBoot);
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
		var machine = StartBootShim(AmigaMachineProfile.A500Pal512KBoot);
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
		var machine = StartBootShim(AmigaMachineProfile.A500Pal512KBoot);
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
		var machine = StartBootShim(AmigaMachineProfile.A500Pal512KBoot);
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
		var machine = StartBootShim(AmigaMachineProfile.A500Pal512KBoot);
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
		var machine = StartBootShim(AmigaMachineProfile.A500Pal512KBoot);
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
		var machine = StartBootShim(AmigaMachineProfile.A500Pal512KBoot);
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
		var machine = StartBootShim(AmigaMachineProfile.A500Pal512KBoot);
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
	public void LoadViewPublishesCopperListFromKickstartViewOffsets()
	{
		var machine = StartBootShim(AmigaMachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
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

		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE), state));
		bus.Display.RenderFrame(frame);
		Assert.Equal(0xFFFF0000u, Pixel(frame, 0, 0));

		bus.WriteLong(view + ViewLofCprListOffset, 0);
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE), state));
		bus.Display.RenderFrame(frame);
		Assert.Equal(0xFF00FF00u, Pixel(frame, 0, 0));
	}

	[Fact]
	public void ExecFindNameResolvesHostBridgeLibraryBases()
	{
		var machine = StartBootShim(AmigaMachineProfile.A500Pal512KBoot);
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
	public void SetWindowTitlesPublishesSyntheticIntuitionTitleBitmap()
	{
		var machine = StartBootShim(AmigaMachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var openScreenState = new M68kCpuState();
		var openWindowState = new M68kCpuState();

		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198), openScreenState));
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -204), openWindowState));
		AssertSyntheticScreenBitMapFields(bus, openScreenState.D[0]);
		var windowRastPort = bus.ReadLong(openWindowState.D[0] + WindowRPortOffset);
		Assert.NotEqual(0u, windowRastPort);
		Assert.Equal(
			openScreenState.D[0] + ScreenBitMapOffset,
			bus.ReadLong(windowRastPort + RastPortBitMapOffset));
		var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
		bus.Display.RenderFrame(frame);
		Assert.True(
			CountPixelsExcept(frame, 0xFF000000u) > 100,
			"OpenScreen/OpenWindow should publish a visible synthetic screen before any title update.");

		var titleAddress = InvokeAllocMem(bus, 64, 0);
		WriteCString(bus, titleAddress, "Loading Hired Guns");

		var titleState = new M68kCpuState();
		titleState.A[0] = openWindowState.D[0];
		titleState.A[1] = titleAddress;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -276), titleState));

		bus.Display.RenderFrame(frame);

		Assert.NotEqual(0u, openScreenState.D[0]);
		Assert.NotEqual(0u, openWindowState.D[0]);
		Assert.Equal(AmigaConstants.A500PalCpuCyclesPerFrame, titleState.Cycles);
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
		var machine = StartBootShim(AmigaMachineProfile.A500Pal512KBoot);
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
		var machine = StartBootShim(AmigaMachineProfile.A500Pal512KBoot);
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
		var machine = new AmigaMachine(AmigaMachineOptions
			.ForProfile(AmigaMachineProfile.A500Pal512KBoot)
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
		var machine = new AmigaMachine(AmigaMachineOptions
			.ForProfile(AmigaMachineProfile.A500Pal512KBoot)
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
		var machine = new AmigaMachine(AmigaMachineOptions
			.ForProfile(AmigaMachineProfile.A500Pal512KBoot)
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
		var machine = new AmigaMachine(AmigaMachineOptions
			.ForProfile(AmigaMachineProfile.A500Pal512KBoot)
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
		var machine = new AmigaMachine(AmigaMachineOptions
			.ForProfile(AmigaMachineProfile.A500Pal512KBoot)
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
		var machine = new AmigaMachine(AmigaMachineOptions
			.ForProfile(AmigaMachineProfile.A500Pal512KBoot)
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
		var machine = new AmigaMachine(AmigaMachineOptions
			.ForProfile(AmigaMachineProfile.A500Pal512KBoot)
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

		boot.SetSyntheticMousePresentationPosition(32 + 520, 32 + (184 * 2));
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
		var machine = new AmigaMachine(AmigaMachineOptions
			.ForProfile(AmigaMachineProfile.A500Pal512KBoot)
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
		var machine = StartBootShim(AmigaMachineProfile.A500Pal512KBoot);
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
		var machine = StartBootShim(AmigaMachineProfile.A500Pal512KBoot);
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
		var machine = StartBootShim(AmigaMachineProfile.A500Pal512KBoot);
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

	private static AmigaMachine StartBootShim(AmigaMachineProfile profile)
	{
		var machine = new AmigaMachine(AmigaMachineOptions
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

	private static void AssertSyntheticScreenBitMapFields(AmigaBus bus, uint screenAddress)
	{
		Assert.NotEqual(0u, screenAddress);
		Assert.Equal(AmigaConstants.PalLowResWidth, bus.ReadWord(screenAddress + ScreenWidthOffset));
		Assert.Equal(256, bus.ReadWord(screenAddress + ScreenHeightOffset));

		var bitMapAddress = screenAddress + ScreenBitMapOffset;
		Assert.Equal(AmigaConstants.PalLowResWidth / 8, bus.ReadWord(bitMapAddress + BitMapBytesPerRowOffset));
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

		return bus.TryInvokeHostTrap(address, bus.ReadWord(address + 2), state);
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
