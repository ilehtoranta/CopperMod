using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class AmigaBootMemoryTests
{
	private const uint ChipPublicLowerAddress = 0x0000_0400;
	private const uint PrivateMetadataSize = 0x0000_1000;
	private const uint PseudoFastMetadataSize = 0x0000_0200;
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

		Assert.True(bus.TryInvokeHost(Lvo(AmigaKickstartHost.ExecLibraryBase, -456), state));

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

		Assert.True(machine.Bus.TryInvokeHost(Lvo(AmigaKickstartHost.ExecLibraryBase, -150), state));

		Assert.Equal(0x400u, state.D[0]);
		Assert.True(state.GetFlag(M68kCpuState.Supervisor));
		Assert.Equal(0x1FFCu, state.A[7]);

		state.D[0] = 0x400;
		Assert.True(machine.Bus.TryInvokeHost(Lvo(AmigaKickstartHost.ExecLibraryBase, -156), state));

		Assert.False(state.GetFlag(M68kCpuState.Supervisor));
		Assert.Equal(0x1FFCu, state.A[7]);
		Assert.Equal(0x400u, state.SupervisorStackPointer);
	}

	[Fact]
	public void FindTaskAndTrapAllocationUseCurrentTask()
	{
		var machine = StartBootShim(AmigaMachineProfile.A500Pal512KBoot);
		var state = new M68kCpuState();

		Assert.True(machine.Bus.TryInvokeHost(Lvo(AmigaKickstartHost.ExecLibraryBase, -294), state));

		var currentTask = machine.Bus.ReadLong(AmigaKickstartHost.ExecLibraryBase + ExecThisTaskOffset);
		Assert.Equal(currentTask, state.D[0]);

		state.D[0] = 4;
		Assert.True(machine.Bus.TryInvokeHost(Lvo(AmigaKickstartHost.ExecLibraryBase, -342), state));

		Assert.Equal(4u, state.D[0]);
		Assert.Equal(0x0010, machine.Bus.ReadWord(currentTask + TaskTrapAllocOffset));
		Assert.Equal(0x0010, machine.Bus.ReadWord(currentTask + TaskTrapAbleOffset));

		state.D[0] = 4;
		Assert.True(machine.Bus.TryInvokeHost(Lvo(AmigaKickstartHost.ExecLibraryBase, -342), state));

		Assert.Equal(0xFFFF_FFFFu, state.D[0]);

		state.D[0] = 4;
		Assert.True(machine.Bus.TryInvokeHost(Lvo(AmigaKickstartHost.ExecLibraryBase, -348), state));

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
	public void RethinkDisplayPublishesCurrentViewCopperList()
	{
		var machine = StartBootShim(AmigaMachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		WriteCopperColorList(bus, 0x2400, 0x0F00);
		WriteCopperColorList(bus, 0x2600, 0x00F0);
		bus.WriteLong(0x2304, 0x2400);
		bus.WriteLong(0x220C, 0x2300);
		var state = new M68kCpuState();
		state.A[1] = 0x2200;

		Assert.True(bus.TryInvokeHost(Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE), state));
		bus.WriteLong(0x2304, 0x2600);
		Assert.True(bus.TryInvokeHost(Lvo(AmigaKickstartHost.IntuitionLibraryBase, -0x186), state));
		var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
		bus.Display.RenderFrame(frame);

		Assert.Equal(0xFF00FF00u, Pixel(frame, 0, 0));
	}

	private static AmigaMachine StartBootShim(AmigaMachineProfile profile)
	{
		var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(profile));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		return machine;
	}

	private static uint InvokeAllocMem(AmigaBus bus, uint byteCount, uint flags)
	{
		var state = new M68kCpuState();
		state.D[0] = byteCount;
		state.D[1] = flags;
		Assert.True(bus.TryInvokeHost(Lvo(AmigaKickstartHost.ExecLibraryBase, -198), state));
		return state.D[0];
	}

	private static uint InvokeAllocAbs(AmigaBus bus, uint byteCount, uint location)
	{
		var state = new M68kCpuState();
		state.D[0] = byteCount;
		state.A[1] = location;
		Assert.True(bus.TryInvokeHost(Lvo(AmigaKickstartHost.ExecLibraryBase, -204), state));
		return state.D[0];
	}

	private static uint InvokeAvailMem(AmigaBus bus, uint flags)
	{
		var state = new M68kCpuState();
		state.D[1] = flags;
		Assert.True(bus.TryInvokeHost(Lvo(AmigaKickstartHost.ExecLibraryBase, -216), state));
		return state.D[0];
	}

	private static void InvokeFreeMem(AmigaBus bus, uint address, uint byteCount)
	{
		var state = new M68kCpuState();
		state.A[1] = address;
		state.D[0] = byteCount;
		Assert.True(bus.TryInvokeHost(Lvo(AmigaKickstartHost.ExecLibraryBase, -210), state));
		Assert.Equal(0u, state.D[0]);
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

	private static void WriteCopperColorList(AmigaBus bus, uint address, ushort color)
	{
		bus.WriteWord(address, 0x0180);
		bus.WriteWord(address + 2, color);
		bus.WriteWord(address + 4, 0xFFFF);
		bus.WriteWord(address + 6, 0xFFFE);
	}

	private static uint Pixel(uint[] frame, int x, int y)
	{
		return frame[(y * AmigaConstants.PalLowResWidth) + x];
	}
}
