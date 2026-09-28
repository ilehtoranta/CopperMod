using System.Reflection;
using CopperMod.Amiga;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CopperStart.Devices.Trackdisk;
using CopperMod.Amiga.CopperStart.Exec;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using PortableExec = CopperStart.Exec;

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
	private const int GfxBaseTextFontsOffset = 0x8C;
	private const int GfxBaseTextFontsTailOffset = 0x90;
	private const int GfxBaseDefaultFontOffset = 0x9A;
	private const int GfxBaseChipRevBits0Offset = 0xEC;
	private const int MemNodeNameOffset = 0x0A;
	private const int MemHeaderAttributesOffset = 0x0E;
	private const int MemHeaderFirstChunkOffset = 0x10;
	private const int MemHeaderLowerOffset = 0x14;
	private const int MemHeaderUpperOffset = 0x18;
	private const int MemHeaderFreeOffset = 0x1C;
	private const int ExecThisTaskOffset = 0x114;
	private const int ExecFirstLvo = -6;
	private const int ExecAddHeadLvo = -240;
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
	private const int ScreenWidthOffset = GraphicsLayouts.ScreenWidth;
	private const int ScreenHeightOffset = GraphicsLayouts.ScreenHeight;
	private const int ScreenLeftEdgeOffset = GraphicsLayouts.ScreenLeftEdge;
	private const int ScreenTopEdgeOffset = GraphicsLayouts.ScreenTopEdge;
	private const int NewScreenLeftOffset = 0x00;
	private const int NewScreenTopOffset = 0x02;
	private const int ScreenFlagsOffset = GraphicsLayouts.ScreenFlags;
	private const int ScreenTitleOffset = GraphicsLayouts.ScreenTitle;
	private const int ScreenDefaultTitleOffset = GraphicsLayouts.ScreenDefaultTitle;
	private const int ScreenFontOffset = GraphicsLayouts.ScreenFont;
	private const int ScreenDetailPenOffset = GraphicsLayouts.ScreenDetailPen;
	private const int ScreenBlockPenOffset = GraphicsLayouts.ScreenBlockPen;
	private const int ScreenStructSize = GraphicsLayouts.ScreenSize;
	private const int ScreenFirstWindowOffset = GraphicsLayouts.ScreenFirstWindow;
	private const int ScreenViewPortOffset = GraphicsLayouts.ScreenViewPort;
	private const int ScreenRastPortOffset = GraphicsLayouts.ScreenRastPort;
	private const int WindowNextOffset = GraphicsLayouts.WindowNext;
	private const int WindowWScreenOffset = GraphicsLayouts.WindowWScreen;
	private const int WindowRPortOffset = 0x32;
	private const int WindowIdcmpFlagsOffset = 0x52;
	private const int WindowUserPortOffset = 0x56;
	private const int MsgPortSigBitOffset = 0x0F;
	private const int MsgPortSigTaskOffset = 0x10;
	private const int MsgPortMsgListOffset = 0x14;
	private const int RastPortBitMapOffset = 0x04;
	private const int ScreenBitMapOffset = GraphicsLayouts.ScreenBitMap;
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
	private const int ViewDyOffset = 0x0C;
	private const int ViewDxOffset = 0x0E;
	private const int ViewStructSize = 0x12;
	private const int BitMapBytesPerRowOffset = 0x00;
	private const int BitMapRowsOffset = 0x02;
	private const int BitMapFlagsOffset = 0x04;
	private const int BitMapDepthOffset = 0x05;
	private const int BitMapPlanesOffset = 0x08;
	private const int CprListStartOffset = 0x04;
	private const int NewScreenWidthOffset = 0x04;
	private const int NewScreenHeightOffset = 0x06;
	private const int NewScreenDepthOffset = 0x08;
	private const int NewScreenViewModesOffset = 0x0C;
	private const int NewScreenFontOffset = GraphicsLayouts.NewScreenFont;
	private const int NewScreenDefaultTitleOffset = GraphicsLayouts.NewScreenDefaultTitle;
	private const int NewWindowScreenOffset = GraphicsLayouts.NewWindowScreen;
	private const int NewWindowTypeOffset = GraphicsLayouts.NewWindowType;
	private const ushort CustomScreenType = 0x000F;
	private const ushort WorkbenchScreenType = 0x0001;
	private const int ViewPortDspInsOffset = 0x08;
	private const int ViewPortDWidthOffset = 0x18;
	private const int ViewPortDHeightOffset = 0x1A;
	private const int ViewPortModesOffset = 0x20;
	private const int ViewPortExtendedModesOffset = GraphicsLayouts.ViewPortExtendedModes;
	private const int ViewPortRasInfoOffset = 0x24;
	private const int ViewPortDxOffset = 0x1C;
	private const int ViewPortDyOffset = 0x1E;
	private const uint ScreenTagDClip = 0x8000_0033;
	private const uint ScreenTagColors = 0x8000_0029;
	private const uint ScreenTagColors32 = 0x8000_0043;
	private const uint ScreenTagTitle = 0x8000_0028;
	private const uint ScreenTagOverscan = 0x8000_0034;
	private const uint ScreenTagDisplayId = 0x8000_0032;
	private const uint ScreenTagBitMap = 0x8000_002E;
	private const uint ScreenTagPubName = 0x8000_002F;
	private const uint ScreenTagWidth = 0x8000_0023;
	private const uint ScreenTagHeight = 0x8000_0024;
	private const uint ScreenTagDepth = 0x8000_0025;
	private const uint ScreenTagErrorCode = 0x8000_002A;
	private const uint ScreenTagFont = 0x8000_002B;
	private const uint ScreenTagSysFont = 0x8000_002C;
	private const uint ScreenTagSharePens = 0x8000_0040;
	private const uint ScreenTagDetailPen = 0x8000_0026;
	private const uint ScreenTagBlockPen = 0x8000_0027;
	private const uint ScreenTagPens = 0x8000_004A;
	private const uint ScreenTagQuiet = 0x8000_0046;
	private const uint CanonicalScreenTagShowTitle = 0x8000_0036;
	private const uint CanonicalScreenTagPens = 0x8000_003A;
	private const uint ScreenTagFullPalette = 0x8000_003B;
	private const uint ScreenTagColorMapEntries = 0x8000_003C;
	private const uint ScreenTagParent = 0x8000_003D;
	private const uint ScreenTagDraggable = 0x8000_003E;
	private const uint ScreenTagExclusive = 0x8000_003F;
	private const uint ScreenTagBackFill = 0x8000_0041;
	private const uint ScreenTagVideoControl = 0x8000_0044;
	private const uint ScreenTagFrontChild = 0x8000_0045;
	private const uint ScreenTagBackChild = 0x8000_0046;
	private const uint ScreenTagLikeWorkbench = 0x8000_0047;
	private const uint ScreenTagReserved = 0x8000_0048;
	private const uint ScreenTagMinimizeIsg = 0x8000_0049;
	private const uint NoTitleChange = uint.MaxValue;
	private const ushort ViewModeHires = 0x8000;
	private const ushort ViewModeInterlace = 0x0004;
	private const ushort ViewModeExtraHalfBrite = 0x0080;
	private const ushort ViewModeSuperHires = 0x0020;
	private const ushort ViewModeHam = 0x0800;
	private const uint IdcmpGadgetDown = 0x0000_0020;
	private const uint IdcmpGadgetUp = 0x0000_0040;
	private const uint MemfPublic = 0x0000_0001;
	private const uint MemfChip = 0x0000_0002;
	private const uint MemfFast = 0x0000_0004;
	private const uint MemfClear = 0x0001_0000;
	// Classic CopperStart boot persistently allocates the Layers library/root
	// pair from the first public header: 304 allocator bytes for the combined
	// negative/positive library allocation and 48 for the 44-byte root.
	private const uint CopperStartLayersPersistentBytes = 352;

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
		var openState = new M68kCpuState { A = { [7] = 0x2000 } };
		openState.A[1] = nameAddress;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -408), openState));

		var libraryBase = openState.D[0];
		var libraryHead = bus.ReadLong(
			AmigaKickstartHost.ExecLibraryBase + ExecLibListOffset);
		Assert.True(
			libraryBase != 0,
			$"CyberGraphics base=0x{boot.CyberGraphics.LibraryBase:X8}, " +
			$"library head=0x{libraryHead:X8}, " +
			$"head next=0x{bus.ReadLong(libraryHead):X8}.");
		Assert.NotEqual(AmigaKickstartHost.GraphicsLibraryBase, libraryBase);
		Assert.Equal(libraryBase, boot.CyberGraphics.LibraryBase);
		Assert.Equal(boot.CopperStartLayersLibraryBase, libraryHead);
		Assert.Equal(libraryBase, bus.ReadLong(libraryHead));

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
	public void KickstartRomExecReadinessWaitsForNativeAddHeadVector()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		var bus = machine.Bus;
		const uint execBase = 0x0000_2000;
		const uint task = 0x0000_3000;

		bus.WriteLong(execBase + ExecChkBaseOffset, ~execBase);
		bus.WriteLong(execBase + ExecThisTaskOffset, task);
		bus.WriteLong(execBase + ExecMemListOffset, 0);
		WriteExecVector(bus, execBase, ExecFirstLvo, 0x0000_4000);

		Assert.False(InvokeIsValidKickstartRomExecBase(boot, execBase));

		WriteExecVector(bus, execBase, ExecAddHeadLvo, 0x0000_4100);
		Assert.True(InvokeIsValidKickstartRomExecBase(boot, execBase));
	}

	[Fact]
	public void ActiveKickstartRomExecTakeoverWithdrawsStaleGatewaysDuringRomReinitialization()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		var bus = machine.Bus;
		const uint execBase = 0x0000_2000;
		const uint task = 0x0000_3000;

		bus.WriteLong(4, execBase);
		bus.WriteLong(execBase + ExecChkBaseOffset, ~execBase);
		bus.WriteLong(execBase + ExecThisTaskOffset, task);
		bus.WriteLong(execBase + ExecMemListOffset, 0);
		WriteExecVector(bus, execBase, ExecFirstLvo, 0x0000_4000);
		WriteExecVector(bus, execBase, ExecAddHeadLvo, 0x0000_4100);
		SetKickstartRomExecTakeoverState(boot, "Pending");

		InvokeTryActivateKickstartRomExecServices(boot);
		Assert.Equal("Active", GetKickstartRomExecTakeoverState(boot));
		foreach (var lvo in new[] { -276, -198, -204, -210, -216 })
			Assert.True(bus.HasHostGateway(unchecked((uint)((int)execBase + lvo))), $"Expected ROM-safe overlay at LVO {lvo}.");
		foreach (var lvo in new[]
		{
			-30, -120, -126, -132, -138, -144, -150, -156,
			-78, -84, -90, -96, -102,
			-234, -240, -246, -252, -258, -264, -270, -282,
			-288, -294, -300, -306, -312, -318, -324, -330, -336, -342, -348,
			-396, -402, -408, -414, -420, -426, -486, -492, -498, -552, -612,
			-528, -732,
			-162, -168, -174, -180, -354, -360, -366, -372, -378, -384, -390,
			-540, -546, -558, -564, -570, -576, -582, -588, -594, -600, -606,
			-666, -672, -678, -720, -786, -1062,
			-444, -450, -456, -462, -468, -474, -480, -504, -510, -516,
			-522, -624, -630, -654, -660,
			-684, -696, -858, -930, -972, -984, -1050,
			global::Amiga.ExecLvo.NewGetTaskAttrsA, global::Amiga.ExecLvo.NewSetTaskAttrsA,
			global::Amiga.ExecLvo.NewSetFunction, global::Amiga.ExecLvo.NewCreateLibrary,
			global::Amiga.ExecLvo.NewPPCStackSwap, global::Amiga.ExecLvo.TaggedOpenLibrary,
			global::Amiga.ExecLvo.ReadGayle, global::Amiga.ExecLvo.CacheFlushDataArea,
			global::Amiga.ExecLvo.CacheInvalidInstArea, global::Amiga.ExecLvo.CacheInvalidDataArea,
			global::Amiga.ExecLvo.CacheFlushDataInstArea, global::Amiga.ExecLvo.CacheTrashCacheArea,
			global::Amiga.ExecLvo.NewGetSystemAttrsA, global::Amiga.ExecLvo.NewSetSystemAttrsA,
			global::Amiga.ExecLvo.NewCreateTaskA, global::Amiga.ExecLvo.FindExecNode,
			global::Amiga.ExecLvo.AddExecNodeA, global::Amiga.ExecLvo.AddResident,
			global::Amiga.ExecLvo.DumpTaskState, global::Amiga.ExecLvo.NewGetTaskPIDAttrsA,
			global::Amiga.ExecLvo.NewSetTaskPIDAttrsA
		})
			Assert.False(bus.HasHostGateway(unchecked((uint)((int)execBase + lvo))), $"External ROM must retain ownership of LVO {lvo}.");

		for (var offset = 0u; offset < 6; offset++)
		{
			bus.WriteByte(unchecked(execBase - 240u + offset), 0, 0);
		}

		InvokeTryActivateKickstartRomExecServices(boot);
		Assert.Equal("Pending", GetKickstartRomExecTakeoverState(boot));
		foreach (var lvo in new[] { -276, -198, -204, -210, -216 })
			Assert.False(bus.HasHostGateway(unchecked((uint)((int)execBase + lvo))), $"Stale overlay remained at LVO {lvo}.");
	}

	[Fact]
	public void ExternalLayersVectorSlabHashSurvivesTakeoverRtgActivityAndReinitialization()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithCpu(AmigaM68kCoreFactory.Default, M68kBackendKind.AccurateM68040)
			.WithLiveAgnusDma(false)
			.WithRtgVram(16L * 1024 * 1024));
		var boot = new AmigaBootController(machine);
		var bus = machine.Bus;
		const uint execBase = 0x0000_2000;
		const uint task = 0x0000_3000;
		const uint layersBase = 0x0000_6000;
		const uint layersName = 0x0000_6200;
		const int negativeSize = 216;
		int[] patchedOffsets =
		[
			-36, -42, -60, -66, -72, -90, -126, -174, -180, -186
		];

		bus.WriteLong(4, execBase);
		bus.WriteLong(execBase + ExecChkBaseOffset, ~execBase);
		bus.WriteLong(execBase + ExecThisTaskOffset, task);
		bus.WriteLong(execBase + ExecMemListOffset, 0);
		WriteExecVector(bus, execBase, ExecFirstLvo, 0x0000_4000);
		WriteExecVector(bus, execBase, ExecAddHeadLvo, 0x0000_4100);
		InitializeExecList(bus, execBase + ExecLibListOffset);
		WriteCString(bus, layersName, "layers.library");
		bus.WriteLong(layersBase + MemNodeNameOffset, layersName);
		bus.WriteWord(layersBase + LibraryVersionOffset, 40);
		bus.WriteLong(layersBase, execBase + ExecLibListOffset + 4);
		bus.WriteLong(layersBase + 4, execBase + ExecLibListOffset);
		bus.WriteLong(execBase + ExecLibListOffset, layersBase);
		bus.WriteLong(execBase + ExecLibListOffset + 8, layersBase);

		for (var index = 0; index < negativeSize; index++)
			bus.WriteByte(
				layersBase - negativeSize + checked((uint)index),
				unchecked((byte)(index * 37 + 11)),
				0);
		for (var index = 0; index < patchedOffsets.Length; index++)
		{
			var vector = unchecked((uint)((int)layersBase + patchedOffsets[index]));
			bus.WriteWord(vector, 0x4EF9);
			bus.WriteLong(vector + 2, 0x0000_8000u + checked((uint)index * 0x20u));
		}

		string HashVectorSlab()
		{
			var bytes = new byte[negativeSize];
			for (var index = 0; index < bytes.Length; index++)
				bytes[index] = bus.ReadByte(
					layersBase - negativeSize + checked((uint)index));
			return Convert.ToHexString(
				System.Security.Cryptography.SHA256.HashData(bytes));
		}

		var initialHash = HashVectorSlab();
		Assert.Equal(
			"8B6C41A8BD45FC6526007B169F79587ACBF91E4E806D1A47CDD2512BFC508215",
			initialHash);
		Assert.True(
			InvokeIsValidKickstartRomExecBase(boot, execBase),
			$"exec=0x{bus.ReadLong(4):X8}, chk=0x{bus.ReadLong(execBase + ExecChkBaseOffset):X8}, " +
			$"task=0x{bus.ReadLong(execBase + ExecThisTaskOffset):X8}, " +
			$"first=0x{bus.ReadWord(unchecked(execBase + (uint)ExecFirstLvo)):X4}, " +
			$"addHead=0x{bus.ReadWord(unchecked(execBase + (uint)ExecAddHeadLvo)):X4}.");
		SetKickstartRomExecTakeoverState(boot, "Pending");
		InvokeTryActivateKickstartRomExecServices(boot);
		Assert.Equal("Active", GetKickstartRomExecTakeoverState(boot));
		Assert.False(boot.HasCopperStartLayers);
		Assert.Equal(initialHash, HashVectorSlab());

		Assert.Equal(patchedOffsets.Length, boot.CyberGraphics.InstallSystemPatches(execBase));
		Assert.Equal(initialHash, HashVectorSlab());
		var scrollVector = unchecked((uint)((int)layersBase - 72));
		var activity = new M68kCpuState { ProgramCounter = scrollVector + 6 };
		Assert.True(bus.TryInvokeHostGatewayAt(scrollVector, activity));
		Assert.Equal(0x0000_8080u, activity.ProgramCounter);
		Assert.Equal(initialHash, HashVectorSlab());

		for (var offset = 0u; offset < 6; offset++)
			bus.WriteByte(unchecked(execBase - 240u + offset), 0, 0);
		InvokeTryActivateKickstartRomExecServices(boot);
		Assert.Equal("Pending", GetKickstartRomExecTakeoverState(boot));
		Assert.False(boot.HasCopperStartLayers);
		Assert.Equal(initialHash, HashVectorSlab());
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
		AssertMemoryHeader(bus, fastHeader, chipHeader, listAddress, MemfPublic | MemfFast, fastLower, fastUpper, "pseudo-fast", reservedPrefix: 104 + CopperStartLayersPersistentBytes);
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
			"chip",
			reservedPrefix: 104 + CopperStartLayersPersistentBytes);
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
		AssertMemoryHeader(bus, realHeader, pseudoHeader, listAddress, MemfPublic | MemfFast, realLower, realUpper, "real-fast", reservedPrefix: 104 + CopperStartLayersPersistentBytes);
		AssertMemoryHeader(bus, pseudoHeader, chipHeader, realHeader, MemfPublic | MemfFast, pseudoLower, pseudoUpper, "pseudo-fast");
		AssertMemoryHeader(bus, chipHeader, listAddress + 4, pseudoHeader, MemfPublic | MemfChip, chipLower, chipUpper, "chip");
		Assert.Equal(
			realLower + 104 + CopperStartLayersPersistentBytes,
			InvokeAllocMem(bus, 0x1000, MemfPublic | MemfFast));
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
	public void HostShimPublishesGfxBaseFontListForCompatibilityFont()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var gfxBase = AmigaKickstartHost.GraphicsLibraryBase;

		Assert.True(bus.IsMappedMemoryRange(gfxBase, AmigaKickstartHost.GraphicsLibraryImageSize));
		Assert.Equal((ushort)40, bus.ReadWord(gfxBase + LibraryVersionOffset));
		Assert.Equal((ushort)0x7C, bus.ReadWord(gfxBase + 0x12));
		Assert.Equal(gfxBase + GfxBaseTextFontsTailOffset, bus.ReadLong(gfxBase + GfxBaseTextFontsOffset));

		var state = new M68kCpuState();
		Assert.True(InvokeHostTrap(bus, Lvo(gfxBase, -72), state)); // OpenFont
		var font = state.D[0];
		Assert.NotEqual(0u, font);
		Assert.Equal(font, bus.ReadLong(gfxBase + GfxBaseDefaultFontOffset));
		Assert.Equal(font, bus.ReadLong(gfxBase + GfxBaseTextFontsOffset));
		Assert.Equal(gfxBase + GfxBaseTextFontsTailOffset, bus.ReadLong(font));
		Assert.Equal(gfxBase + GfxBaseTextFontsOffset, bus.ReadLong(font + 4));
	}

	[Theory]
	[InlineData((int)MachineProfile.A500Pal512KBoot, 0u)]
	[InlineData((int)MachineProfile.A500PlusEcsPal, 3u)]
	public void HostShimSetChipRevPublishesOcsAndEcsCapabilities(int profile, uint expectedBits)
	{
		var machine = StartBootShim((MachineProfile)profile);
		var bus = machine.Bus;
		var state = new M68kCpuState { D = { [0] = 0xFFFF_FFFFu } };

		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -888), state));
		Assert.Equal(expectedBits, state.D[0]);
		Assert.Equal((byte)expectedBits, bus.ReadByte(
			AmigaKickstartHost.GraphicsLibraryBase + (uint)GfxBaseChipRevBits0Offset));
	}

	[Fact]
	public void HostShimSetChipRevLeavesReadOnlyCapabilityByteForProvider()
	{
		var machine = StartBootShim(MachineProfile.A500PlusEcsPal);
		var bus = machine.Bus;
		var chipRevAddress =
			AmigaKickstartHost.GraphicsLibraryBase + (uint)GfxBaseChipRevBits0Offset;
		const byte sentinel = 0x5A;
		bus.MapReadOnlyMemory(chipRevAddress, new[] { sentinel });

		var state = new M68kCpuState { D = { [0] = GraphicsChipRevision.SetBest } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -888),
			state));
		Assert.Equal(GraphicsChipRevision.SetEcs, state.D[0]);
		Assert.Equal(sentinel, bus.ReadByte(chipRevAddress));
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
		AssertMemoryHeader(bus, chipHeader, listAddress + 4, listAddress, MemfPublic | MemfChip, chipLower, chipUpper, "chip", reservedPrefix: 104 + CopperStartLayersPersistentBytes);
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
		var initialFastChunk = bus.ReadLong(fastHeader + MemHeaderFirstChunkOffset);
		var initialChipChunk = bus.ReadLong(chipHeader + MemHeaderFirstChunkOffset);

		var publicAllocation = InvokeAllocMem(bus, 0x1000, MemfPublic);
		var chipAllocation = InvokeAllocMem(bus, 0x2000, MemfPublic | MemfChip);

		Assert.Equal(initialFastChunk, publicAllocation);
		Assert.Equal(initialChipChunk, chipAllocation);
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
		Assert.Equal(initialFastChunk, bus.ReadLong(fastHeader + MemHeaderFirstChunkOffset));
		Assert.Equal(initialChipChunk, bus.ReadLong(chipHeader + MemHeaderFirstChunkOffset));
	}

	[Fact]
	public void ChipOnlyAllocMemCanReturnLowChipMemoryAboveSupervisorStack()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KChipOnlyBoot);
		var bus = machine.Bus;
		var chipHeader = (uint)bus.ChipRam.Length - PrivateMetadataSize + ChipOnlyMemHeaderOffset;
		var allocatableBytes = bus.ReadLong(chipHeader + MemHeaderFreeOffset);
		var firstChunk = bus.ReadLong(chipHeader + MemHeaderFirstChunkOffset);

		var allocation = InvokeAllocMem(bus, allocatableBytes, MemfPublic | MemfChip);

		Assert.Equal(firstChunk, allocation);
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
	public void TlsfAllocMemInvokesGuestLowMemoryHandlerWithDocumentedRegisters()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(
			machine,
			memoryAllocator: global::CopperStart.Exec.ExecMemoryAllocatorKind.Tlsf);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		var interrupt = InvokeAllocMem(bus, global::Amiga.Interrupt.Size, MemfPublic | MemfClear);
		var code = InvokeAllocMem(bus, 24, MemfPublic);
		var capture = InvokeAllocMem(bus, 16, MemfPublic | MemfClear);
		var stack = InvokeAllocMem(bus, 128, MemfPublic);
		const uint interruptData = 0x1234_5678;

		bus.WriteByte(interrupt + (uint)global::Amiga.ExecLayout.Node.Priority, 0, 0);
		bus.WriteLong(interrupt + (uint)global::Amiga.ExecLayout.Interrupt.Data, interruptData);
		bus.WriteLong(interrupt + (uint)global::Amiga.ExecLayout.Interrupt.Code, code);
		WriteMoveAddressRegisterToAbsolute(bus, code, 0, capture);
		WriteMoveAddressRegisterToAbsolute(bus, code + 6, 1, capture + 4);
		WriteMoveAddressRegisterToAbsolute(bus, code + 12, 6, capture + 8);
		bus.WriteWord(code + 18, 0x7000); // MOVEQ #DidNothing,D0
		bus.WriteWord(code + 20, 0x4E75); // RTS

		var add = machine.Cpu.State;
		add.A[1] = interrupt;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, global::Amiga.ExecLvo.AddMemHandler), add));
		var extension = bus.ReadLong(AmigaKickstartHost.ExecLibraryBase +
			(uint)global::Amiga.ExecLayout.ExecBase.ExReserved2);
		Assert.NotEqual(0u, extension);

		add.D[0] = 0x7FFF_FFFF;
		add.D[1] = MemfPublic;
		add.A[6] = AmigaKickstartHost.ExecLibraryBase;
		add.A[7] = stack + 128;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, global::Amiga.ExecLvo.AllocMem), add));

		var handlerData = bus.ReadLong(capture);
		Assert.Equal(0u, add.D[0]);
		Assert.NotEqual(0u, handlerData);
		Assert.Equal(interruptData, bus.ReadLong(capture + 4));
		Assert.Equal(AmigaKickstartHost.ExecLibraryBase, bus.ReadLong(capture + 8));
		Assert.Equal(0x7FFF_FFFFu,
			bus.ReadLong(handlerData + (uint)global::Amiga.ExecLayout.MemHandlerData.RequestSize));
		Assert.Equal(MemfPublic,
			bus.ReadLong(handlerData + (uint)global::Amiga.ExecLayout.MemHandlerData.RequestFlags));
		Assert.Equal(AmigaKickstartHost.ExecLibraryBase, add.A[6]);
		Assert.Equal(stack + 128, add.A[7]);
	}

	[Fact]
	public void TlsfLowMemoryRecoveryInvokesDelayedLibraryExpungeUnlessSuppressed()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(
			machine,
			memoryAllocator: global::CopperStart.Exec.ExecMemoryAllocatorKind.Tlsf);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		var interrupt = InvokeAllocMem(bus, global::Amiga.Interrupt.Size, MemfPublic | MemfClear);
		var handlerCode = InvokeAllocMem(bus, 4, MemfPublic);
		var libraryStorage = InvokeAllocMem(bus, 128, MemfPublic | MemfClear);
		var counter = InvokeAllocMem(bus, 4, MemfPublic | MemfClear);
		var stack = InvokeAllocMem(bus, 128, MemfPublic);
		var library = libraryStorage + 18;
		var libraryList = AmigaKickstartHost.ExecLibraryBase +
			(uint)global::Amiga.ExecLayout.ExecBase.LibraryList;
		var libraryTail = libraryList + (uint)global::Amiga.ExecLayout.List.Tail;

		bus.WriteByte(interrupt + (uint)global::Amiga.ExecLayout.Node.Priority, 0xFF, 0); // negative phase
		bus.WriteLong(interrupt + (uint)global::Amiga.ExecLayout.Interrupt.Code, handlerCode);
		bus.WriteWord(handlerCode, 0x7000); // MOVEQ #DidNothing,D0
		bus.WriteWord(handlerCode + 2, 0x4E75); // RTS
		var add = machine.Cpu.State;
		add.A[1] = interrupt;
		Assert.True(InvokeHostTrap(bus,
			Lvo(AmigaKickstartHost.ExecLibraryBase, global::Amiga.ExecLvo.AddMemHandler), add));

		bus.WriteLong(libraryList + (uint)global::Amiga.ExecLayout.List.Head, library);
		bus.WriteLong(libraryList + (uint)global::Amiga.ExecLayout.List.Tail, 0);
		bus.WriteLong(libraryList + (uint)global::Amiga.ExecLayout.List.TailPred, library);
		bus.WriteLong(library + (uint)global::Amiga.ExecLayout.Node.Successor, libraryTail);
		bus.WriteLong(library + (uint)global::Amiga.ExecLayout.Node.Predecessor, libraryList);
		bus.WriteByte(library + (uint)global::Amiga.ExecLayout.Library.Flags,
			(byte)global::Amiga.LibraryFlags.DelayedExpunge, 0);
		bus.WriteWord(library + (uint)global::Amiga.ExecLayout.Library.OpenCount, 0);
		bus.WriteWord(libraryStorage, 0x23FC); // MOVE.L #marker,(counter).L
		bus.WriteLong(libraryStorage + 2, 0xC0DE_CAFEu);
		bus.WriteLong(libraryStorage + 6, counter);
		bus.WriteWord(libraryStorage + 10, 0x4E75); // RTS

		add.D[0] = 0x7FFF_FFFF;
		add.D[1] = MemfPublic;
		add.A[6] = AmigaKickstartHost.ExecLibraryBase;
		add.A[7] = stack + 128;
		Assert.True(InvokeHostTrap(bus,
			Lvo(AmigaKickstartHost.ExecLibraryBase, global::Amiga.ExecLvo.AllocMem), add));
		Assert.Equal(0xC0DE_CAFEu, bus.ReadLong(counter));

		bus.WriteLong(counter, 0);
		add.D[0] = 0x7FFF_FFFF;
		add.D[1] = MemfPublic | 0x8000_0000u; // MEMF_NO_EXPUNGE
		Assert.True(InvokeHostTrap(bus,
			Lvo(AmigaKickstartHost.ExecLibraryBase, global::Amiga.ExecLvo.AllocMem), add));
		Assert.Equal(0u, bus.ReadLong(counter));
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
		Assert.Equal(
			0x2200u,
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + GraphicsLayouts.GfxBaseActiView));
		var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
		bus.Display.RenderFrame(frame);

		Assert.Equal(0xFF00FF00u, Pixel(frame, 0, 0));
	}

	[Fact]
	public void RethinkDisplayRefusesReadOnlyNativeActiViewBeforeRebuildingCopper()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint cprList = 0x2300;
		const uint copperList = 0x2400;
		var actiView = AmigaKickstartHost.GraphicsLibraryBase +
			(uint)GraphicsLayouts.GfxBaseActiView;

		WriteCopperColorList(bus, copperList, 0x0F00);
		bus.WriteLong(cprList + CprListStartOffset, copperList);
		bus.WriteLong(view + ViewLofCprListOffset, cprList);
		var load = new M68kCpuState { A = { [1] = view } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -0x186),
			load));
		Assert.Equal(view, bus.ReadLong(actiView));

		bus.MapReadOnlyMemory(
			actiView,
			new byte[]
			{
				unchecked((byte)(view >> 24)),
				unchecked((byte)(view >> 16)),
				unchecked((byte)(view >> 8)),
				unchecked((byte)view)
			});

		var rethink = new M68kCpuState { A = { [1] = view } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -0x186),
			rethink));

		Assert.Equal(1u, rethink.D[0]);
		Assert.Equal(view, bus.ReadLong(actiView));
		Assert.Equal(cprList, bus.ReadLong(view + ViewLofCprListOffset));
	}

	[Fact]
	public void RethinkDisplayKeepsThePublishedCopperListWhenViewPortRebuildFails()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint cprList = 0x2300;
		const uint oldCopperList = 0x2400;
		const uint newCprList = 0x2500;
		const uint newCopperList = 0x2600;
		const uint malformedViewPort = 0x2800;
		WriteCopperColorList(bus, oldCopperList, 0x0F00);
		WriteCopperColorList(bus, newCopperList, 0x00F0);
		bus.WriteLong(cprList + CprListStartOffset, oldCopperList);
		bus.WriteLong(newCprList + CprListStartOffset, newCopperList);
		bus.WriteLong(view + ViewLofCprListOffset, cprList);

		var state = new M68kCpuState { A = { [1] = view } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE), state));

		// A cyclic ViewPort chain makes the rebuild fail before any new copper
		// list is allocated.  RethinkDisplay must keep the list already loaded
		// by LoadView instead of publishing the newly edited cprlist.
		bus.WriteLong(view + ViewViewPortOffset, malformedViewPort);
		bus.WriteLong(malformedViewPort, malformedViewPort);
		bus.WriteLong(view + ViewLofCprListOffset, newCprList);
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -0x186), state));
		Assert.Equal(1u, state.D[0]); // RethinkDisplay failure (V39+)
		Assert.Equal(
			view,
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + GraphicsLayouts.GfxBaseActiView));

		var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
		bus.Display.RenderFrame(frame);
		Assert.Equal(0xFFFF0000u, Pixel(frame, 0, 0));
	}

	[Fact]
	public void RethinkDisplayRejectsAnExtraRasInfoPlayfieldBeforeReplacingCopper()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint cprList = 0x2300;
		const uint oldCopperList = 0x2400;
		const uint viewPort = 0x2800;
		const uint firstRasInfo = 0x2900;
		const uint secondRasInfo = 0x2940;
		const uint unexpectedThirdRasInfo = 0x2980;

		WriteCopperColorList(bus, oldCopperList, 0x0F00);
		bus.WriteLong(cprList + CprListStartOffset, oldCopperList);
		bus.WriteLong(view + ViewLofCprListOffset, cprList);
		var state = new M68kCpuState { A = { [1] = view } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE), state));

		// Start with a readable DUALPF chain whose third node is the only
		// malformed part.  RethinkDisplay must reject before allocating a new
		// compatibility CPR/raw list and leave the currently loaded list active.
		bus.WriteLong(view + ViewViewPortOffset, viewPort);
		bus.WriteLong(viewPort + (uint)GraphicsLayouts.ViewPortNext, 0);
		bus.WriteWord(viewPort + ViewPortModesOffset, GraphicsModeIds.DualPlayfieldMode);
		bus.WriteLong(viewPort + ViewPortRasInfoOffset, firstRasInfo);
		bus.WriteLong(
			firstRasInfo + (uint)GraphicsLayouts.RasInfoNext,
			secondRasInfo);
		bus.WriteLong(
			secondRasInfo + (uint)GraphicsLayouts.RasInfoNext,
			unexpectedThirdRasInfo);
		bus.WriteLong(unexpectedThirdRasInfo + (uint)GraphicsLayouts.RasInfoNext, 0);
		foreach (var rasInfo in new[] { firstRasInfo, secondRasInfo, unexpectedThirdRasInfo })
		{
			bus.WriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, 0);
			bus.WriteWord(rasInfo + (uint)GraphicsLayouts.RasInfoRxOffset, 0);
			bus.WriteWord(rasInfo + (uint)GraphicsLayouts.RasInfoRyOffset, 0);
		}

		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -0x186), state));
		Assert.Equal(
			view,
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + GraphicsLayouts.GfxBaseActiView));

		var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
		bus.Display.RenderFrame(frame);
		Assert.Equal(0xFFFF0000u, Pixel(frame, 0, 0));
	}

	[Fact]
	public void RethinkDisplayKeepsThePublishedCopperListForAnAllHiddenViewPortChain()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint cprList = 0x2300;
		const uint oldCopperList = 0x2400;
		const uint viewPort = 0x2800;
		const uint hiddenDspIns = 0xDEAD_BEEFu;

		WriteCopperColorList(bus, oldCopperList, 0x0F00);
		bus.WriteLong(cprList + CprListStartOffset, oldCopperList);
		bus.WriteLong(view + ViewLofCprListOffset, cprList);
		var state = new M68kCpuState { A = { [1] = view } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE), state));

		// VP_HIDE is a display-time control.  RethinkDisplay must validate the
		// chain but leave the currently loaded stream alone when every node is
		// hidden; a later visible viewport can still rebuild from this View.
		bus.WriteLong(view + ViewViewPortOffset, viewPort);
		bus.WriteLong(viewPort + (uint)GraphicsLayouts.ViewPortNext, 0);
		bus.WriteWord(viewPort + ViewPortModesOffset, 0x2000); // VP_HIDE
		bus.WriteLong(viewPort + ViewPortDspInsOffset, hiddenDspIns);
		bus.WriteLong(viewPort + ViewPortRasInfoOffset, 0);

		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -0x186),
			state));
		Assert.Equal(0u, state.D[0]); // all-hidden chain is a successful no-op
		Assert.Equal(view, bus.ReadLong(
			AmigaKickstartHost.GraphicsLibraryBase + GraphicsLayouts.GfxBaseActiView));
		Assert.Equal(hiddenDspIns, bus.ReadLong(viewPort + ViewPortDspInsOffset));

		var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
		bus.Display.RenderFrame(frame);
		Assert.Equal(0xFFFF0000u, Pixel(frame, 0, 0));
	}

	[Fact]
	public void RethinkDisplayPublishesTheFirstVisibleViewPortAfterAHiddenChainHead()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint cprList = 0x2300;
		const uint oldCopperList = 0x2400;
		const uint hiddenViewPort = 0x2800;
		const uint visibleViewPort = 0x2A00;
		const uint hiddenDspIns = 0xDEAD_BEEFu;

		WriteCopperColorList(bus, oldCopperList, 0x0F00);
		bus.WriteLong(cprList + CprListStartOffset, oldCopperList);
		bus.WriteLong(view + ViewLofCprListOffset, cprList);
		var state = new M68kCpuState { A = { [1] = view } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE),
			state));

		// The hidden head contributes only its public Modes/Next links. Its
		// payload is intentionally malformed; the later visible node remains
		// subject to the normal ViewPort/RasInfo/BitMap admission path.
		bus.WriteLong(hiddenViewPort + (uint)GraphicsLayouts.ViewPortNext, visibleViewPort);
		bus.WriteWord(
			hiddenViewPort + ViewPortModesOffset,
			GraphicsModeIds.ViewPortHidden);
		bus.WriteLong(hiddenViewPort + ViewPortRasInfoOffset, 1);
		bus.WriteLong(hiddenViewPort + ViewPortDspInsOffset, hiddenDspIns);

		WriteMinimalViewPort(bus, visibleViewPort);
		bus.WriteLong(visibleViewPort + (uint)GraphicsLayouts.ViewPortNext, 0);
		bus.WriteWord(visibleViewPort + ViewPortModesOffset, 0);
		bus.WriteLong(view + ViewViewPortOffset, hiddenViewPort);

		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -0x186),
			state));
		Assert.Equal(0u, state.D[0]);
		Assert.Equal(
			view,
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase +
				GraphicsLayouts.GfxBaseActiView));
		Assert.Equal(hiddenDspIns, bus.ReadLong(hiddenViewPort + ViewPortDspInsOffset));
		Assert.NotEqual(0u, bus.ReadLong(visibleViewPort + ViewPortDspInsOffset));
		Assert.Equal(hiddenViewPort, bus.ReadLong(view + ViewViewPortOffset));
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
	public void LoadViewNullLeavesIntegratedRtgScanoutToCyberGraphX()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithRtgVram(16L * 1024 * 1024)
			.WithCpu(AmigaM68kCoreFactory.Default, M68kBackendKind.AccurateM68040)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		const uint bitMap = 0x5000;
		const uint viewPort = 0x5100;
		const uint rasInfo = 0x5200;
		const uint view = 0x5300;
		var surface = Assert.IsType<CyberGraphicsSurface>(
			boot.CyberGraphics.AllocateRtgSurface(2, 1, CyberGraphicsPixelFormat.Lut8));
		boot.CyberGraphics.RegisterBitMap(bitMap, surface);
		boot.CyberGraphics.RegisterViewPort(viewPort, surface);
		bus.WriteLong(view + ViewViewPortOffset, viewPort);
		bus.WriteLong(viewPort + ViewPortRasInfoOffset, rasInfo);
		bus.WriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, bitMap);
		bus.WriteLong(
			AmigaKickstartHost.GraphicsLibraryBase +
				(uint)GraphicsLayouts.GfxBaseActiView,
			view);
		boot.CyberGraphics.SelectFrontViewPort(viewPort);

		Assert.True(boot.TryRenderRtgFrame(out var before));
		Assert.Equal(viewPort, before.ViewPortAddress);
		Assert.Equal(bitMap, before.BitMapAddress);

		var nullLoad = new M68kCpuState
		{
			A = { [1] = 0 },
			D = { [0] = 0xCAFE_BABEu },
			Cycles = 733,
			ProgramCounter = 0x0012_3456
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE),
			nullLoad));

		// The status-aware graphics callback must decline without consuming the
		// classic void-call frame; CyberGraphX still owns the selected viewport.
		Assert.Equal(0xCAFE_BABEu, nullLoad.D[0]);
		Assert.Equal(733, nullLoad.Cycles);
		Assert.Equal(0x0012_3456u, nullLoad.ProgramCounter);
		Assert.Equal(
			view,
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase +
				(uint)GraphicsLayouts.GfxBaseActiView));
		Assert.True(boot.TryRenderRtgFrame(out var after));
		Assert.Equal(viewPort, after.ViewPortAddress);
		Assert.Equal(bitMap, after.BitMapAddress);
	}

	[Fact]
	public void RethinkDisplayLeavesIntegratedRtgScanoutToCyberGraphX()
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
		const uint bitMap = 0x5600;
		const uint viewPort = 0x5700;
		const uint rasInfo = 0x5800;
		const uint view = 0x5900;
		var surface = Assert.IsType<CyberGraphicsSurface>(
			boot.CyberGraphics.AllocateRtgSurface(2, 1, CyberGraphicsPixelFormat.Lut8));
		boot.CyberGraphics.RegisterBitMap(bitMap, surface);
		boot.CyberGraphics.RegisterViewPort(viewPort, surface);
		bus.WriteLong(view + ViewViewPortOffset, viewPort);
		bus.WriteLong(viewPort + ViewPortRasInfoOffset, rasInfo);
		bus.WriteLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, bitMap);
		bus.WriteLong(
			AmigaKickstartHost.GraphicsLibraryBase +
				(uint)GraphicsLayouts.GfxBaseActiView,
			view);
		boot.CyberGraphics.SelectFrontViewPort(viewPort);

		// The CyberGraphX patch observes the provider View before the native
		// Intuition gateway is allowed to run.  This mirrors the real patched
		// LoadView/RethinkDisplay chain without asking the provider to build a
		// planar copper list.
		var observed = new M68kCpuState { A = { [1] = view } };
		Assert.False(patches.TryInvokeGraphicsLibraryPatch(-222, observed));
		var viewLof = bus.ReadLong(view + ViewLofCprListOffset);
		var viewShf = bus.ReadLong(view + ViewShfCprListOffset);
		var viewPortDspIns = bus.ReadLong(viewPort + ViewPortDspInsOffset);
		Assert.True(boot.TryRenderRtgFrame(out var before));

		var rethink = new M68kCpuState
		{
			D = { [0] = 0xA5A5_5A5Au },
			Cycles = 911,
			ProgramCounter = 0x0012_6789
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -390),
			rethink));

		// Direct AmigaBoot RethinkDisplay is a no-op success for a selected RTG
		// scanout: no planar resources or provider-owned frame state are touched.
		Assert.Equal(0u, rethink.D[0]);
		Assert.Equal(911, rethink.Cycles);
		Assert.Equal(0x0012_6789u, rethink.ProgramCounter);
		Assert.Equal(view, bus.ReadLong(
			AmigaKickstartHost.GraphicsLibraryBase +
			(uint)GraphicsLayouts.GfxBaseActiView));
		Assert.Equal(viewLof, bus.ReadLong(view + ViewLofCprListOffset));
		Assert.Equal(viewShf, bus.ReadLong(view + ViewShfCprListOffset));
		Assert.Equal(viewPortDspIns, bus.ReadLong(viewPort + ViewPortDspInsOffset));
		Assert.True(boot.TryRenderRtgFrame(out var after));
		Assert.Equal(before.ViewPortAddress, after.ViewPortAddress);
		Assert.Equal(before.BitMapAddress, after.BitMapAddress);
	}

	[Fact]
	public void RethinkDisplayReclaimsPlanarViewWhenRtgFrontReusesViewport()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithRtgVram(16L * 1024 * 1024)
			.WithCpu(AmigaM68kCoreFactory.Default, M68kBackendKind.AccurateM68040)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;

		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));
		var screen = open.D[0];
		Assert.NotEqual(0u, screen);
		var front = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -252),
			front));
		var view = bus.ReadLong(
			AmigaKickstartHost.GraphicsLibraryBase +
			(uint)GraphicsLayouts.GfxBaseActiView);
		var viewPort = screen + ScreenViewPortOffset;
		WriteMinimalViewPort(bus, viewPort);
		bus.WriteLong(view + ViewViewPortOffset, viewPort);
		var beforeDspIns = bus.ReadLong(viewPort + ViewPortDspInsOffset);
		Assert.NotEqual(0u, view);

		// Keep the standard-planar guest ViewPort and RasInfo/BitMap links
		// intact, but leave a stale CyberGraphX registration selected for the
		// same embedded ViewPort.  A provider registration is not ownership of
		// the current guest View; the public bitmap link is the discriminator.
		const uint rtgBitMap = 0x5A00;
		var surface = Assert.IsType<CyberGraphicsSurface>(
			boot.CyberGraphics.AllocateRtgSurface(2, 1, CyberGraphicsPixelFormat.Lut8));
		boot.CyberGraphics.RegisterBitMap(rtgBitMap, surface);
		boot.CyberGraphics.RegisterViewPort(viewPort, surface);
		boot.CyberGraphics.SelectFrontViewPort(viewPort);
		Assert.True(boot.CyberGraphics.RtgScanoutSelected);

		var rethink = new M68kCpuState
		{
			D = { [0] = 0xA5A5_5A5Au },
			Cycles = 919,
			ProgramCounter = 0x0012_6799
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -390),
			rethink));

		// The current public planar View owns this shared ViewPort, so the
		// host must rebuild its copper publication and clear the stale provider
		// front selection rather than treating the old registration as active.
		Assert.Equal(0u, rethink.D[0]);
		Assert.Equal(919, rethink.Cycles);
		Assert.Equal(0x0012_6799u, rethink.ProgramCounter);
		Assert.NotEqual(beforeDspIns, bus.ReadLong(viewPort + ViewPortDspInsOffset));
		Assert.Equal(
			view,
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase +
				(uint)GraphicsLayouts.GfxBaseActiView));
	Assert.False(boot.CyberGraphics.RtgScanoutSelected);
	}

	[Fact]
	public void LoadViewReclaimsPlanarViewWhenRtgFrontReusesViewport()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithRtgVram(16L * 1024 * 1024)
			.WithCpu(AmigaM68kCoreFactory.Default, M68kBackendKind.AccurateM68040)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;

		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));
		var screen = open.D[0];
		Assert.NotEqual(0u, screen);
		var front = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -252),
			front));

		var view = bus.ReadLong(
			AmigaKickstartHost.GraphicsLibraryBase +
			(uint)GraphicsLayouts.GfxBaseActiView);
		var viewPort = screen + ScreenViewPortOffset;
		WriteMinimalViewPort(bus, viewPort);
		bus.WriteLong(view + ViewViewPortOffset, viewPort);

		// Leave a selected provider registration on the shared guest ViewPort,
		// while the public chain still describes the standard planar bitmap.
		const uint rtgBitMap = 0x5B00;
		var surface = Assert.IsType<CyberGraphicsSurface>(
			boot.CyberGraphics.AllocateRtgSurface(2, 1, CyberGraphicsPixelFormat.Lut8));
		boot.CyberGraphics.RegisterBitMap(rtgBitMap, surface);
		boot.CyberGraphics.RegisterViewPort(viewPort, surface);
		boot.CyberGraphics.SelectFrontViewPort(viewPort);
		Assert.True(boot.CyberGraphics.RtgScanoutSelected);
		var initialRethink = new M68kCpuState { Cycles = 921 };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -390),
			initialRethink));
		var beforeDspIns = bus.ReadLong(viewPort + ViewPortDspInsOffset);
		Assert.NotEqual(0u, beforeDspIns);

		var load = new M68kCpuState
		{
			A = { [1] = view },
			D = { [0] = 0x5A5A_A5A5u },
			Cycles = 923,
			ProgramCounter = 0x0012_67B1
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -222),
			load));

		// The graphics.library path must reclaim the standard-planar View before
		// publishing the provider front selection, preserving the void ABI while
		// replacing the planar copper projection.
		Assert.Equal(0u, load.D[0]);
		Assert.Equal(923, load.Cycles);
		Assert.Equal(0x0012_67B1u, load.ProgramCounter);
		Assert.Equal(beforeDspIns, bus.ReadLong(viewPort + ViewPortDspInsOffset));
		Assert.Equal(
			view,
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase +
				(uint)GraphicsLayouts.GfxBaseActiView));
		Assert.False(boot.CyberGraphics.RtgScanoutSelected);
	}

	[Fact]
	public void LoadViewAssemblingViewClearsStaleRtgFrontSelection()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithRtgVram(16L * 1024 * 1024)
			.WithCpu(AmigaM68kCoreFactory.Default, M68kBackendKind.AccurateM68040)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;

		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));
		var screen = open.D[0];
		Assert.NotEqual(0u, screen);
		var front = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -252),
			front));

		var viewPort = screen + ScreenViewPortOffset;
		const uint rtgBitMap = 0x5D00;
		var surface = Assert.IsType<CyberGraphicsSurface>(
			boot.CyberGraphics.AllocateRtgSurface(2, 1, CyberGraphicsPixelFormat.Lut8));
		boot.CyberGraphics.RegisterBitMap(rtgBitMap, surface);
		boot.CyberGraphics.RegisterViewPort(viewPort, surface);
		boot.CyberGraphics.SelectFrontViewPort(viewPort);
		Assert.True(boot.CyberGraphics.RtgScanoutSelected);

		const uint assemblingView = 0x5C00;
		bus.WriteLong(assemblingView + ViewViewPortOffset, 0);
		bus.WriteLong(assemblingView + ViewLofCprListOffset, 0);
		bus.WriteLong(assemblingView + ViewShfCprListOffset, 0);
		var load = new M68kCpuState
		{
			A = { [1] = assemblingView },
			D = { [0] = 0xA5A5_5A5Au },
			Cycles = 927,
			ProgramCounter = 0x0012_67C9
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -222),
			load));

		Assert.Equal(0u, load.D[0]);
		Assert.Equal(927, load.Cycles);
		Assert.Equal(0x0012_67C9u, load.ProgramCounter);
		Assert.Equal(
			assemblingView,
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase +
				(uint)GraphicsLayouts.GfxBaseActiView));
		Assert.False(boot.CyberGraphics.RtgScanoutSelected);
	}

	[Fact]
	public void ScreenToFrontReclaimsStaleRtgFrontSelectionOnIdempotentStandardView()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithRtgVram(16L * 1024 * 1024)
			.WithCpu(AmigaM68kCoreFactory.Default, M68kBackendKind.AccurateM68040)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;

		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));
		var screen = open.D[0];
		Assert.NotEqual(0u, screen);
		var initialFront = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -252),
			initialFront));
		var view = bus.ReadLong(
			AmigaKickstartHost.GraphicsLibraryBase +
			(uint)GraphicsLayouts.GfxBaseActiView);
		Assert.NotEqual(0u, view);
		var viewPort = screen + ScreenViewPortOffset;
		WriteMinimalViewPort(bus, viewPort);
		bus.WriteLong(view + ViewViewPortOffset, viewPort);

		// Register a provider surface against the same guest ViewPort while the
		// public RasInfo/BitMap chain still describes the standard planar screen.
		const uint rtgBitMap = 0x5E00;
		var surface = Assert.IsType<CyberGraphicsSurface>(
			boot.CyberGraphics.AllocateRtgSurface(2, 1, CyberGraphicsPixelFormat.Lut8));
		boot.CyberGraphics.RegisterBitMap(rtgBitMap, surface);
		boot.CyberGraphics.RegisterViewPort(viewPort, surface);
		boot.CyberGraphics.SelectFrontViewPort(viewPort);
		Assert.True(boot.CyberGraphics.RtgScanoutSelected);

		var front = new M68kCpuState
		{
			A = { [0] = screen, [1] = 0xCAFE_BABEu },
			D = { [0] = 0xA5A5_5A5Au },
			Cycles = 1500,
			ProgramCounter = 0x0012_67E1
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -252),
			front));

		// The idempotent ScreenToFront path must still revalidate ownership.  It
		// clears only the stale provider selection and preserves the caller frame.
		Assert.Equal(0xCAFE_BABEu, front.A[1]);
		Assert.Equal(0xA5A5_5A5Au, front.D[0]);
		Assert.Equal(1500, front.Cycles);
		Assert.Equal(0x0012_67E1u, front.ProgramCounter);
		Assert.Equal(
			view,
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase +
				(uint)GraphicsLayouts.GfxBaseActiView));
		Assert.False(boot.CyberGraphics.RtgScanoutSelected);
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
		Assert.Equal(view, bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));

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

		state.A[1] = 0;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE), state));
		Assert.Equal(0u, bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));
	}

	[Fact]
	public void LoadViewActivatesAValidatedPreallocatedBlankCopperStream()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint viewPort = 0x2300;
		const uint rasInfo = 0x2380;
		const uint bitMap = 0x23A0;
		const uint plane = 0x23C0;
		const uint copList = 0x2500;
		const uint rawCopper = 0x2580;
		const uint lofCprList = 0x2600;
		const uint shfCprList = 0x2620;

		bus.WriteLong(view + ViewViewPortOffset, viewPort);
		bus.WriteLong(view + ViewLofCprListOffset, lofCprList);
		bus.WriteLong(view + ViewShfCprListOffset, shfCprList);
		bus.WriteLong(viewPort, 0);
		bus.WriteLong(viewPort + ViewPortDspInsOffset, copList);
		bus.WriteWord(viewPort + ViewPortDWidthOffset, 16);
		bus.WriteWord(viewPort + ViewPortDHeightOffset, 1);
		bus.WriteWord(viewPort + ViewPortDxOffset, 0);
		bus.WriteWord(viewPort + ViewPortDyOffset, 0);
		bus.WriteWord(viewPort + ViewPortModesOffset, 0);
		bus.WriteByte(viewPort + ViewPortExtendedModesOffset, 0, 0);
		bus.WriteLong(viewPort + ViewPortRasInfoOffset, rasInfo);

		bus.WriteLong(rasInfo, 0);
		bus.WriteLong(rasInfo + 4, bitMap);
		bus.WriteWord(bitMap + BitMapBytesPerRowOffset, 2);
		bus.WriteWord(bitMap + BitMapRowsOffset, 1);
		bus.WriteByte(bitMap + BitMapFlagsOffset, 0, 0);
		bus.WriteByte(bitMap + BitMapDepthOffset, 1, 0);
		bus.WriteLong(bitMap + BitMapPlanesOffset, plane);
		bus.WriteWord(plane, 0x8000);

		bus.WriteLong(copList + GraphicsLayouts.CopListViewPort, viewPort);
		bus.WriteLong(copList + GraphicsLayouts.CopListCopPtr, rawCopper);
		bus.WriteLong(copList + GraphicsLayouts.CopListCopLStart, rawCopper);
		bus.WriteLong(copList + GraphicsLayouts.CopListCopSStart, rawCopper);
		bus.WriteWord(copList + GraphicsLayouts.CopListCount, 0);
		bus.WriteWord(copList + GraphicsLayouts.CopListMaxCount, 1);
		bus.WriteWord(rawCopper, GraphicsLayouts.CopperEndWait);
		bus.WriteWord(rawCopper + 2, GraphicsLayouts.CopperEndMask);

		foreach (var cprList in new[] { lofCprList, shfCprList })
		{
			bus.WriteLong(cprList + GraphicsLayouts.CprListNext, 0);
			bus.WriteLong(cprList + CprListStartOffset, rawCopper);
			bus.WriteWord(cprList + GraphicsLayouts.CprListMaxCount, 1);
		}

		var state = new M68kCpuState
		{
			A = { [1] = view },
			D = { [0] = 0xA5A5_5A5Au }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE),
			state));
		Assert.Equal(0u, state.D[0]);
		Assert.Equal(
			view,
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + GraphicsLayouts.GfxBaseActiView));
		Assert.Equal((ushort)(rawCopper >> 16), bus.ReadWord(0x00DFF080));
		Assert.Equal((ushort)rawCopper, bus.ReadWord(0x00DFF082));
		Assert.Equal(
			(ushort)0x0380,
			(ushort)(bus.ReadWord(0x00DFF002) & 0x0380));

		// The native MakeVPort construction arm emits this bounded 16x1
		// standard-planar program.  LoadView must admit the exact generated
		// stream as well as the marker-only blank form above.
		var generatedWords = new ushort[]
		{
			0x008E, 0x2C81,
			0x0090, 0x2D00,
			0x0092, 0x0038,
			0x0094, 0x0038,
			0x0108, 0x0000,
			0x010A, 0x0000,
			0x0100, 0x1000,
			0x0102, 0x0000,
			0x0104, 0x0000,
			0x00E0, (ushort)(plane >> 16),
			0x00E2, (ushort)plane,
			GraphicsLayouts.CopperEndWait, GraphicsLayouts.CopperEndMask
		};
		for (var index = 0; index < generatedWords.Length; index++)
			bus.WriteWord(rawCopper + (uint)(index * sizeof(ushort)), generatedWords[index]);
		bus.WriteWord(copList + GraphicsLayouts.CopListCount, 12);
		bus.WriteWord(copList + GraphicsLayouts.CopListMaxCount, 12);
		bus.WriteWord(lofCprList + GraphicsLayouts.CprListMaxCount, 12);
		bus.WriteWord(shfCprList + GraphicsLayouts.CprListMaxCount, 12);
		state = new M68kCpuState
		{
			A = { [1] = view },
			D = { [0] = 0x2468_ACEDu },
			Cycles = 911
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE),
			state));
		Assert.Equal(0u, state.D[0]);
		Assert.Equal(
			view,
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + GraphicsLayouts.GfxBaseActiView));
		Assert.Equal((ushort)(rawCopper >> 16), bus.ReadWord(0x00DFF080));
		Assert.Equal((ushort)rawCopper, bus.ReadWord(0x00DFF082));

		// A split wrapper must fail before the host callback can replace the
		// active View or copper pointer/DMA state.
		bus.WriteLong(shfCprList + CprListStartOffset, rawCopper + 2);
		state = new M68kCpuState
		{
			A = { [1] = view },
			D = { [0] = 0x1357_9BDFu },
			Cycles = 733
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE),
			state));
		Assert.Equal(0x1357_9BDFu, state.D[0]);
		Assert.Equal(
			view,
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + GraphicsLayouts.GfxBaseActiView));
		Assert.Equal((ushort)(rawCopper >> 16), bus.ReadWord(0x00DFF080));
		Assert.Equal((ushort)rawCopper, bus.ReadWord(0x00DFF082));
	}

	[Fact]
	public void LoadViewDeclinesAValidatedViewWhoseCopperStreamIsInFastRam()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithRealFastRam(8 * 1024 * 1024)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint replacementView = 0x2800;
		const uint cprList = 0x2300;
		const uint chipCopper = 0x2400;
		var fastCopper = bus.RealFastRamBase + 0x100u;

		WriteCopperColorList(bus, chipCopper, 0x0F00);
		bus.WriteLong(cprList + CprListStartOffset, chipCopper);
		bus.WriteLong(view + ViewLofCprListOffset, cprList);
		bus.WriteLong(view + ViewShfCprListOffset, cprList);
		var state = new M68kCpuState { A = { [1] = view }, D = { [0] = 0xA5A5_5A5Au } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE),
			state));
		Assert.Equal(0u, state.D[0]);
		Assert.Equal(
			(ushort)(chipCopper >> 16),
			bus.ReadWord(0x00DFF080));
		Assert.Equal((ushort)chipCopper, bus.ReadWord(0x00DFF082));

		// The fast-RAM marker is CPU-readable and mapped, but it is not a
		// native OCS/ECS display-DMA source.  LoadView must decline before
		// replacing the active View or COP1 publication.
		bus.WriteWord(fastCopper, GraphicsLayouts.CopperEndWait);
		bus.WriteWord(fastCopper + 2, GraphicsLayouts.CopperEndMask);
		bus.WriteLong(cprList + CprListStartOffset, fastCopper);
		bus.WriteLong(replacementView + ViewLofCprListOffset, cprList);
		bus.WriteLong(replacementView + ViewShfCprListOffset, cprList);
		state = new M68kCpuState { A = { [1] = replacementView }, D = { [0] = 0x1357_9BDFu }, Cycles = 733 };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE),
			state));
		Assert.Equal(0x1357_9BDFu, state.D[0]);
		Assert.Equal(
			(ushort)(chipCopper >> 16),
			bus.ReadWord(0x00DFF080));
		Assert.Equal((ushort)chipCopper, bus.ReadWord(0x00DFF082));
	}

	[Fact]
	public void LoadViewSkipsAnUnmappedRasInfoBehindAHiddenViewPort()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint cprList = 0x2300;
		const uint copperList = 0x2400;
		const uint hiddenViewPort = 0x00F0_0000;

		WriteCopperColorList(bus, copperList, 0x0F00);
		bus.WriteLong(cprList + CprListStartOffset, copperList);
		bus.WriteLong(view + ViewViewPortOffset, hiddenViewPort);
		bus.WriteLong(view + ViewLofCprListOffset, cprList);
		bus.WriteLong(view + ViewShfCprListOffset, cprList);
		bus.MapWritableMemory(hiddenViewPort, new byte[ViewPortModesOffset + sizeof(ushort)]);
		bus.WriteLong(hiddenViewPort + (uint)GraphicsLayouts.ViewPortNext, 0);
		bus.WriteWord(hiddenViewPort + ViewPortModesOffset, 0x2000); // VP_HIDE
		Assert.False(bus.IsMappedMemoryRange(
			hiddenViewPort + (uint)ViewPortRasInfoOffset,
			sizeof(uint)));

		var state = new M68kCpuState
		{
			A = { [1] = view },
			D = { [0] = 0x1357_9BDFu },
			Cycles = 77
		};

		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE),
			state));
		Assert.Equal(0u, state.D[0]);
		Assert.Equal(
			view,
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + GraphicsLayouts.GfxBaseActiView));

		// The publication fingerprint is based on the same Modes/Next-only
		// hidden envelope, so an unchanged second LoadView remains idempotent.
		state.Cycles = 101;
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE),
			state));
		Assert.Equal(0u, state.D[0]);
	}

	[Fact]
	public void GraphicsWaitTofUsesTheSelectedNtscBeamClockBoundary()
	{
		var machine = StartBootShim(MachineProfile.A500PlusEcsNtsc);
		var bus = machine.Bus;
		const long cycle = 1_000;
		var expected = bus.GetNextFrameStartCycle(cycle);
		var palBoundary = ((cycle / AmigaConstants.A500PalCpuCyclesPerFrame) + 1) *
			AmigaConstants.A500PalCpuCyclesPerFrame;
		var state = new M68kCpuState { Cycles = cycle, D = { [0] = 0xCAFE_BABEu } };

		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -270), state));
		Assert.Equal(0u, state.D[0]);
		Assert.Equal(expected, state.Cycles);
		Assert.NotEqual(palBoundary, state.Cycles);
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
	public void LoadViewUsesTheSelectedNtscBeamBoundaryForCopperPublication()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500PlusEcsNtsc)
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

		var firstFrameCycles = bus.GetNextFrameStartCycle(0);
		var midFrameCycle = firstFrameCycles / 2;
		var expectedBoundary = bus.GetNextFrameStartCycle(midFrameCycle);
		var state = new M68kCpuState { Cycles = midFrameCycle };
		state.A[1] = view;

		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE), state));
		Assert.Equal((ushort)0, bus.ReadWord(0x00DFF080));
		Assert.Equal((ushort)0, bus.ReadWord(0x00DFF082));
		Assert.Equal(expectedBoundary, InvokeGetNextSyntheticVBlankBoundaryCycle(boot, state.Cycles, expectedBoundary + firstFrameCycles));
		Assert.NotEqual(AmigaConstants.A500PalCpuCyclesPerFrame, expectedBoundary);

		InvokeAdvanceSyntheticVBlankInterruptServers(boot, state.Cycles, expectedBoundary);

		Assert.Equal((ushort)(copperList >> 16), bus.ReadWord(0x00DFF080));
		Assert.Equal((ushort)copperList, bus.ReadWord(0x00DFF082));
	}

	[Fact]
	public void LoadViewSelectsTheShortFrameCopperListAtTheAlternatingFieldBoundary()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500PlusEcsNtsc)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		const uint longView = 0x2200;
		const uint shortView = 0x2280;
		const uint lofCprList = 0x2300;
		const uint shfCprList = 0x2320;
		const uint lofCopperList = 0x2400;
		const uint shfCopperList = 0x2600;

		WriteCopperColorList(bus, lofCopperList, 0x0F00);
		WriteCopperColorList(bus, shfCopperList, 0x00F0);
		bus.WriteLong(lofCprList + CprListStartOffset, lofCopperList);
		bus.WriteLong(shfCprList + CprListStartOffset, shfCopperList);
		foreach (var view in new[] { longView, shortView })
		{
			bus.WriteLong(view + ViewViewPortOffset, 0);
			bus.WriteLong(view + ViewLofCprListOffset, lofCprList);
			bus.WriteLong(view + ViewShfCprListOffset, shfCprList);
		}

		var longFrame = bus.GetBeamPosition(0).FrameCycles;
		var firstLoad = new M68kCpuState { A = { [1] = longView } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE),
			firstLoad));
		Assert.Equal((ushort)(lofCopperList >> 16), bus.ReadWord(0x00DFF080));
		Assert.Equal((ushort)lofCopperList, bus.ReadWord(0x00DFF082));

		var midLongFrame = longFrame / 2;
		var secondLoad = new M68kCpuState
		{
			A = { [1] = shortView },
			Cycles = midLongFrame
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE),
			secondLoad));
		Assert.Equal(longFrame, InvokeGetNextSyntheticVBlankBoundaryCycle(
			boot,
			secondLoad.Cycles,
			longFrame * 2));

		InvokeAdvanceSyntheticVBlankInterruptServers(boot, secondLoad.Cycles, longFrame);

		Assert.Equal((ushort)(shfCopperList >> 16), bus.ReadWord(0x00DFF080));
		Assert.Equal((ushort)shfCopperList, bus.ReadWord(0x00DFF082));
	}

	[Fact]
	public void LoadViewNullCancelsAQueuedCopperListAtTheNextFrameBoundary()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint cprList = 0x2300;
		const uint copperList = 0x2400;
		WriteCopperColorList(bus, copperList, 0x0F00);
		bus.WriteLong(cprList + CprListStartOffset, copperList);
		bus.WriteLong(view + ViewLofCprListOffset, cprList);
		bus.WriteLong(view + ViewShfCprListOffset, cprList);

		var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
		var state = new M68kCpuState { Cycles = frameCycles / 2 };
		state.A[1] = view;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE), state));
		Assert.Equal((ushort)0, bus.ReadWord(0x00DFF080));
		Assert.Equal(frameCycles, InvokeGetNextSyntheticVBlankBoundaryCycle(boot, state.Cycles, frameCycles * 2));

		// Replacing the pending view with NULL must queue the blanking request,
		// not leave the old CPR list eligible for the next frame.
		state.A[1] = 0;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE), state));
		Assert.Equal(0u, bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));

		InvokeAdvanceSyntheticVBlankInterruptServers(boot, state.Cycles, frameCycles);

		Assert.Equal((ushort)0, bus.ReadWord(0x00DFF080));
		Assert.Equal((ushort)0, bus.ReadWord(0x00DFF082));
	}

	[Fact]
	public void LoadViewNullPreservesExistingDisplayDmaEnablesAndSpriteDma()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;

		// PrimeBootController leaves the normal boot DMA mask active. Enable
		// sprites explicitly, but keep bitplane DMA off so the NULL handoff can
		// prove it does not reuse the synthetic display-on mask.
		bus.WriteWord(0x00DFF096, 0x8020, 0);
		var before = bus.ReadWord(0x00DFF002);
		Assert.NotEqual((ushort)0, (ushort)(before & 0x0020));
		Assert.Equal((ushort)0, (ushort)(before & 0x0100));

		var blank = new M68kCpuState
		{
			A = { [1] = 0 },
			Cycles = frameCycles / 2
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE),
			blank));

		// The copper pointer changes only at the next frame boundary. The DMA
		// register must retain the exact pre-blank enable state at that same
		// boundary: sprites remain eligible, and bitplanes are not resurrected.
		InvokeAdvanceSyntheticVBlankInterruptServers(boot, blank.Cycles, frameCycles);
		var after = bus.ReadWord(0x00DFF002);
		Assert.Equal((ushort)(before & 0x07FF), (ushort)(after & 0x07FF));
		Assert.NotEqual((ushort)0, (ushort)(after & 0x0020));
		Assert.Equal((ushort)0, (ushort)(after & 0x0100));
	}

	[Fact]
	public void LoadViewNullDoesNotRequeueAnAlreadyBlankStreamButReclaimsAReplacedActiveView()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));
		Assert.NotEqual(0u, open.D[0]);

		var pendingValid = typeof(AmigaBootController).GetField(
			"_pendingCopperListValid",
			BindingFlags.Instance | BindingFlags.NonPublic);
		var pendingCycle = typeof(AmigaBootController).GetField(
			"_pendingCopperListCycle",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(pendingValid);
		Assert.NotNull(pendingCycle);
		var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
		var firstBlank = new M68kCpuState { A = { [1] = 0 }, Cycles = frameCycles / 2 };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE),
			firstBlank));
		var finalizedAfterFirstBlank = bus.Display.LiveFinalizedPresentationThroughCycle;
		Assert.True(finalizedAfterFirstBlank < frameCycles / 2);
		Assert.True((bool)pendingValid!.GetValue(boot)!);
		Assert.Equal(frameCycles, (long)pendingCycle!.GetValue(boot)!);

		// The stream is already blank and the guest sidecar still agrees. A
		// repeated NULL must not replace the existing blanking request with a
		// later frame-boundary handoff.
		var repeatedBlank = new M68kCpuState
		{
			A = { [1] = 0 },
			Cycles = frameCycles + frameCycles / 2
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE),
			repeatedBlank));
		Assert.True((bool)pendingValid.GetValue(boot)!);
		Assert.Equal(frameCycles, (long)pendingCycle.GetValue(boot)!);

		InvokeAdvanceSyntheticVBlankInterruptServers(boot, firstBlank.Cycles, frameCycles);
		Assert.False((bool)pendingValid.GetValue(boot)!);
		Assert.Equal((ushort)0, bus.ReadWord(0x00DFF080));
		Assert.Equal((ushort)0, bus.ReadWord(0x00DFF082));

		// A provider/native owner can replace GfxBase->ActiView directly. The
		// host cache must not turn that explicit NULL request into a no-op.
		var foreignView = InvokeAllocMem(
			bus,
			GraphicsLayouts.ViewSize,
			MemfPublic | MemfClear);
		Assert.NotEqual(0u, foreignView);
		bus.WriteLong(
			AmigaKickstartHost.GraphicsLibraryBase +
				(uint)GraphicsLayouts.GfxBaseActiView,
			foreignView);
		Assert.Equal(
			foreignView,
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase +
				(uint)GraphicsLayouts.GfxBaseActiView));
		var reclaim = new M68kCpuState
		{
			A = { [1] = 0 },
			Cycles = frameCycles * 2 + frameCycles / 2
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE),
			reclaim));
		Assert.Equal(
			0u,
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase +
				(uint)GraphicsLayouts.GfxBaseActiView));
	}

	[Fact]
	public void LoadViewNullDoesNotTreatAnUnreadableNativeActiViewAsBlank()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;

		// Establish the compatibility owner's already-blank publication.
		var firstBlank = new M68kCpuState
		{
			A = { [1] = 0 },
			Cycles = 0
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE),
			firstBlank));

		var nativeBaseField = typeof(AmigaBootController).GetField(
			"_nativeGraphicsLibraryBase",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(nativeBaseField);
		// Keep the discovered native base even and deliberately leave its
		// ActiView LONG unmapped.  An unknown native field must remain visible
		// to the resident owner instead of being treated as a zero pointer.
		const uint unreadableNativeBase = 0xFFFF_F000;
		nativeBaseField!.SetValue(boot, unreadableNativeBase);
		Assert.False(bus.IsMappedMemoryRange(
			unreadableNativeBase + (uint)GraphicsLayouts.GfxBaseActiView,
			sizeof(uint)));

		var pendingValid = typeof(AmigaBootController).GetField(
			"_pendingCopperListValid",
			BindingFlags.Instance | BindingFlags.NonPublic);
		var pendingCycle = typeof(AmigaBootController).GetField(
			"_pendingCopperListCycle",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(pendingValid);
		Assert.NotNull(pendingCycle);

		var secondBlank = new M68kCpuState
		{
			A = { [1] = 0 },
			Cycles = frameCycles / 2
		};
		var tryLoadView = typeof(AmigaBootController).GetMethod(
			"TryLoadView",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(tryLoadView);
		Assert.True((bool)tryLoadView!.Invoke(boot, new object[] { secondBlank })!);
		Assert.Equal(
			unreadableNativeBase,
			(uint)nativeBaseField.GetValue(boot)!);
		Assert.Equal(frameCycles / 2, secondBlank.Cycles);
		var currentViewField = typeof(AmigaBootController).GetField(
			"_currentViewAddress",
			BindingFlags.Instance | BindingFlags.NonPublic);
		var publishedSignatureField = typeof(AmigaBootController).GetField(
			"_hasPublishedViewSignature",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(currentViewField);
		Assert.NotNull(publishedSignatureField);
		Assert.Equal(0u, (uint)currentViewField!.GetValue(boot)!);
		Assert.True((bool)publishedSignatureField!.GetValue(boot)!);
		Assert.True((bool)pendingValid!.GetValue(boot)!);
		Assert.Equal(frameCycles, (long)pendingCycle!.GetValue(boot)!);
	}

	[Fact]
	public void LoadViewRefusesReadOnlyNativeActiViewBeforeCopperHandoff()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		const uint nativeGraphicsBase = 0x3000;
		const uint view = 0x2200;
		const uint viewPort = 0x2300;
		const uint activeViewSentinel = 0xDEAD_BEEFu;

		WriteMinimalViewPort(bus, viewPort);
		bus.WriteLong(view + ViewViewPortOffset, viewPort);
		bus.MapWritableMemory(nativeGraphicsBase, new byte[0x100]);
		var actiView = nativeGraphicsBase + (uint)GraphicsLayouts.GfxBaseActiView;
		bus.WriteLong(actiView, activeViewSentinel);
		bus.MapReadOnlyMemory(
			actiView,
			new byte[]
			{
				unchecked((byte)(activeViewSentinel >> 24)),
				unchecked((byte)(activeViewSentinel >> 16)),
				unchecked((byte)(activeViewSentinel >> 8)),
				unchecked((byte)activeViewSentinel)
			});

		var nativeBaseField = typeof(AmigaBootController).GetField(
			"_nativeGraphicsLibraryBase",
			BindingFlags.Instance | BindingFlags.NonPublic);
		var currentViewField = typeof(AmigaBootController).GetField(
			"_currentViewAddress",
			BindingFlags.Instance | BindingFlags.NonPublic);
		var pendingValidField = typeof(AmigaBootController).GetField(
			"_pendingCopperListValid",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(nativeBaseField);
		Assert.NotNull(currentViewField);
		Assert.NotNull(pendingValidField);
		nativeBaseField!.SetValue(boot, nativeGraphicsBase);

		var tryLoadView = typeof(AmigaBootController).GetMethod(
			"TryLoadView",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(tryLoadView);
		var state = new M68kCpuState { A = { [1] = view }, Cycles = 1500 };
		Assert.False((bool)tryLoadView!.Invoke(boot, new object[] { state })!);
		Assert.Equal(activeViewSentinel, bus.ReadLong(actiView));
		Assert.Equal(0u, (uint)currentViewField!.GetValue(boot)!);
		Assert.False((bool)pendingValidField!.GetValue(boot)!);
	}

	[Fact]
	public void LoadViewNullRejectsWrappedNativeActiViewFieldBeforeLowAliasProbe()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;

		// Establish the compatibility owner's already-blank publication so the
		// second NULL request would be idempotent if the wrapped field were
		// incorrectly read through address 2.
		var firstBlank = new M68kCpuState { A = { [1] = 0 }, Cycles = 0 };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE),
			firstBlank));

		const uint wrappedNativeBase = 0xFFFF_FFE0;
		const uint lowAlias = 0x0000_0002;
		const uint sentinel = 0xA5A5_5A5A;
		bus.WriteLong(lowAlias, sentinel);
		var nativeBaseField = typeof(AmigaBootController).GetField(
			"_nativeGraphicsLibraryBase",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(nativeBaseField);
		nativeBaseField!.SetValue(boot, wrappedNativeBase);

		// GfxBase->ActiView is at +0x22, which wraps this malformed base to
		// address 2.  That low alias is deliberately readable so an unchecked
		// addition would falsely claim the native display is already blank.
		Assert.Equal(sentinel, bus.ReadLong(lowAlias));

		var pendingValid = typeof(AmigaBootController).GetField(
			"_pendingCopperListValid",
			BindingFlags.Instance | BindingFlags.NonPublic);
		var pendingCycle = typeof(AmigaBootController).GetField(
			"_pendingCopperListCycle",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(pendingValid);
		Assert.NotNull(pendingCycle);

		var secondBlank = new M68kCpuState
		{
			A = { [1] = 0 },
			Cycles = frameCycles / 2
		};
		var tryLoadView = typeof(AmigaBootController).GetMethod(
			"TryLoadView",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(tryLoadView);
		Assert.True((bool)tryLoadView!.Invoke(boot, new object[] { secondBlank })!);

		// The malformed native field remains visible to the native/provider
		// owner: the host queues the real blank handoff and never writes address
		// 2 through the wrapped GfxBase arithmetic.
		Assert.Equal(sentinel, bus.ReadLong(lowAlias));
		Assert.True((bool)pendingValid!.GetValue(boot)!);
		Assert.Equal(frameCycles, (long)pendingCycle!.GetValue(boot)!);
	}

	[Fact]
	public void CloseScreenRefusesWrappedNativeActiViewReconciliationBeforeTeardown()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));
		var screen = open.D[0];
		Assert.NotEqual(0u, screen);
		var activeViewBeforeClose = bus.ReadLong(
			AmigaKickstartHost.GraphicsLibraryBase +
			(uint)GraphicsLayouts.GfxBaseActiView);
		Assert.NotEqual(0u, activeViewBeforeClose);

		const uint wrappedNativeBase = 0xFFFF_FFE0;
		const uint lowAlias = 0x0000_0002;
		const uint sentinel = 0x5A5A_A5A5;
		bus.WriteLong(lowAlias, sentinel);
		var nativeBaseField = typeof(AmigaBootController).GetField(
			"_nativeGraphicsLibraryBase",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(nativeBaseField);
		nativeBaseField!.SetValue(boot, wrappedNativeBase);

		var close = new M68kCpuState
		{
			A = { [0] = screen },
			D = { [0] = 0xCAFE_BABEu },
			Cycles = 1234
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));

		// CloseScreen must leave the compatibility screen and active-view
		// publication intact when native GfxBase reconciliation would wrap to a
		// low alias.  The malformed owner can retry after repairing its base.
		Assert.Equal(0u, close.D[0]);
		Assert.Equal(
			activeViewBeforeClose,
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase +
				(uint)GraphicsLayouts.GfxBaseActiView));
		Assert.Equal(sentinel, bus.ReadLong(lowAlias));
	}

	[Fact]
	public void LoadViewAssemblingViewCancelsAStaleQueuedCopperListWithoutChangingTheCurrentStream()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		const uint preparedView = 0x2200;
		const uint cprList = 0x2300;
		const uint copperList = 0x2400;
		const uint assemblingView = 0x2600;
		var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
		var midFrame = frameCycles / 2;

		WriteCopperColorList(bus, copperList, 0x0F00);
		bus.WriteLong(cprList + CprListStartOffset, copperList);
		bus.WriteLong(preparedView + ViewLofCprListOffset, cprList);
		bus.WriteLong(preparedView + ViewShfCprListOffset, cprList);

		var preparedLoad = new M68kCpuState
		{
			A = { [1] = preparedView },
			Cycles = midFrame
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE),
			preparedLoad));
		Assert.Equal((ushort)0, bus.ReadWord(0x00DFF080));
		Assert.Equal(frameCycles, InvokeGetNextSyntheticVBlankBoundaryCycle(
			boot,
			midFrame,
			frameCycles * 2));

		// An assembling View is allowed to become the guest publication while
		// another owner prepares its viewport.  It must retire the old queued
		// copper request, however; otherwise the prepared view can unexpectedly
		// take over the custom chips at the next frame boundary.
		var assemblingLoad = new M68kCpuState
		{
			A = { [1] = assemblingView },
			Cycles = midFrame
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE),
			assemblingLoad));
		Assert.Equal(assemblingView, bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));
		Assert.Equal(frameCycles * 2, InvokeGetNextSyntheticVBlankBoundaryCycle(
			boot,
			midFrame,
			frameCycles * 2));

		InvokeAdvanceSyntheticVBlankInterruptServers(boot, midFrame, frameCycles);

		Assert.Equal((ushort)0, bus.ReadWord(0x00DFF080));
		Assert.Equal((ushort)0, bus.ReadWord(0x00DFF082));
	}

	[Fact]
	public void LoadViewKeepsThePreviouslyPublishedViewWhenCopperResolutionFails()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		bus.EnableLiveAgnusDma();
		const uint view = 0x2200;
		const uint cprList = 0x2300;
		const uint copperList = 0x2400;
		const uint failedView = 0x2800;
		const uint malformedCprList = 0x2900;

		WriteCopperColorList(bus, copperList, 0x0F00);
		bus.WriteLong(cprList + CprListStartOffset, copperList);
		bus.WriteLong(view + ViewLofCprListOffset, cprList);
		bus.WriteLong(view + ViewShfCprListOffset, cprList);

		var load = new M68kCpuState { A = { [1] = view } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE), load));
		Assert.Equal(view, bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));
		var oldCopperHigh = bus.ReadWord(0x00DFF080);
		var oldCopperLow = bus.ReadWord(0x00DFF082);

		// The portable View envelope is valid, but its nonzero CPR pointer does
		// not resolve to a wrapped or raw copper list.  LoadView must decline
		// without changing the active guest publication or hardware stream.
		bus.WriteLong(failedView + ViewLofCprListOffset, malformedCprList);
		bus.WriteLong(failedView + ViewShfCprListOffset, 0);
		var failed = new M68kCpuState
		{
			A = { [1] = failedView },
			D = { [0] = 0xA5A5_5A5A },
			Cycles = AmigaConstants.A500PalCpuCyclesPerFrame
		};

		// GraphicsServices owns the compatibility trap even when the portable
		// adapter declines; the observable contract is that the failed call is
		// a no-op and leaves the native/provider boundary available.
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE), failed));
		Assert.Equal(0xA5A5_5A5Au, failed.D[0]);
		Assert.Equal(view, bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));
		Assert.Equal(oldCopperHigh, bus.ReadWord(0x00DFF080));
		Assert.Equal(oldCopperLow, bus.ReadWord(0x00DFF082));
	}

	[Fact]
	public void LoadViewRejectsAHighCopperListWrapperBeforeLowAliasPublication()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint wrappedCprList = 0xFFFF_FFFEu;
		const uint rawCopperList = 0x2400;

		WriteCopperColorList(bus, rawCopperList, 0x0F00);
		bus.WriteLong(view + ViewLofCprListOffset, wrappedCprList);
		bus.WriteLong(view + ViewShfCprListOffset, 0);

		// An unchecked cprlist + cpr_Start addition would wrap to address 2.
		// Populate that low alias with a valid raw copper pointer so the old
		// host path would incorrectly publish it.
		bus.WriteLong(2, rawCopperList);
		var state = new M68kCpuState
		{
			A = { [1] = view },
			D = { [0] = 0xCAFE_BABEu },
			Cycles = 733
		};

		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE),
			state));
		Assert.Equal(0xCAFE_BABEu, state.D[0]);
		Assert.Equal(0u, bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));
	}

	[Fact]
	public void LoadViewRejectsAViewEnvelopeThatWrapsIntoLowMemory()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint wrappedView = 0xFFFF_FFFCu;

		// The first View longword is mapped at the end of the guest address
		// space; the following two would wrap to addresses 0 and 4.  Populate
		// those low aliases with an empty view so an unchecked host read would
		// incorrectly claim the call and publish the wrapped pointer.
		bus.MapWritableMemory(wrappedView, new byte[4]);
		bus.WriteLong(0, 0);
		bus.WriteLong(4, 0);
		var state = new M68kCpuState
		{
			A = { [1] = wrappedView },
			D = { [0] = 0xDEAD_BEEFu },
			Cycles = 731
		};

		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE),
			state));
		Assert.Equal(0xDEAD_BEEFu, state.D[0]);
		Assert.Equal(0u, bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));
	}

	[Fact]
	public void LoadViewLeavesOddViewAndViewportChainsForNativeOwnership()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint oddView = 0x2A01;
		const uint evenView = 0x2B00;
		const uint oddViewPort = 0x2C01;

		bus.MapWritableMemory(oddView, new byte[ViewStructSize]);
		for (var index = 0; index < sizeof(uint) * 3; index++)
		{
			bus.WriteByte(oddView + (uint)index, 0, 0);
		}

		bus.MapWritableMemory(evenView, new byte[ViewStructSize]);
		bus.MapWritableMemory(oddViewPort, new byte[0x40]);
		WriteLongBytes(bus, evenView + (uint)ViewViewPortOffset, oddViewPort);
		WriteLongBytes(bus, evenView + (uint)ViewLofCprListOffset, 0);
		WriteLongBytes(bus, evenView + (uint)ViewShfCprListOffset, 0);

		var oddViewState = new M68kCpuState
		{
			A = { [1] = oddView },
			D = { [0] = 0xCAFE_BABEu },
			Cycles = 733
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE),
			oddViewState));
		Assert.Equal(0xCAFE_BABEu, oddViewState.D[0]);
		Assert.Equal(oddView, oddViewState.A[1]);
		Assert.Equal(0u, bus.ReadLong(
			AmigaKickstartHost.GraphicsLibraryBase +
			(uint)GraphicsLayouts.GfxBaseActiView));

		var oddViewPortState = new M68kCpuState
		{
			A = { [1] = evenView },
			D = { [0] = 0x1357_9BDFu },
			Cycles = 811
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE),
			oddViewPortState));
		Assert.Equal(0x1357_9BDFu, oddViewPortState.D[0]);
		Assert.Equal(evenView, oddViewPortState.A[1]);
		Assert.Equal(0u, bus.ReadLong(
			AmigaKickstartHost.GraphicsLibraryBase +
			(uint)GraphicsLayouts.GfxBaseActiView));
	}

	[Fact]
	public void ChangeVPBitMapRequestsCopperRebuildForTheActiveView()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint viewPort = 0x2300;
		const uint nextBitMap = 0x2500;
		const uint nextPlane = 0x2520;
		WriteMinimalViewPort(bus, viewPort);
		bus.WriteLong(view + ViewViewPortOffset, viewPort);
		bus.WriteWord(nextBitMap + BitMapBytesPerRowOffset, 2);
		bus.WriteWord(nextBitMap + BitMapRowsOffset, 1);
		bus.WriteByte(nextBitMap + BitMapDepthOffset, 1, 0);
		bus.WriteLong(nextBitMap + BitMapPlanesOffset, nextPlane);
		bus.WriteWord(nextPlane, 0x4000);

		var make = new M68kCpuState { A = { [0] = view, [1] = viewPort } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD8), make));
		var firstCopper = bus.ReadLong(viewPort + ViewPortDspInsOffset);
		Assert.NotEqual(0u, firstCopper);

		var load = new M68kCpuState { A = { [1] = view } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE), load));

		var change = new M68kCpuState
		{
			A = { [0] = viewPort, [1] = nextBitMap, [2] = 0 },
		};
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -942), change));
		Assert.NotEqual(firstCopper, bus.ReadLong(viewPort + ViewPortDspInsOffset));
		var rasInfo = bus.ReadLong(viewPort + ViewPortRasInfoOffset);
		Assert.Equal(nextBitMap, bus.ReadLong(rasInfo + 4));
	}

	[Fact]
	public void NativeChangeVPBitMapRepliesDBufInfoMessagesAtFrameBoundaries()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		const uint viewPort = 0x2300;
		const uint view = 0x2200;
		const uint nextBitMap = 0x2500;
		const uint nextPlane = 0x2520;
		const uint dbufInfo = 0x2800;
		const uint replyPort = 0x2A00;
		const uint task = 0x2B00;
		const int safeMessageOffset = 0x08;
		const int displayMessageOffset = 0x28;

		WriteMinimalViewPort(bus, viewPort);
		bus.WriteLong(view + ViewViewPortOffset, viewPort);
		bus.WriteWord(nextBitMap + BitMapBytesPerRowOffset, 2);
		bus.WriteWord(nextBitMap + BitMapRowsOffset, 1);
		bus.WriteByte(nextBitMap + BitMapDepthOffset, 1, 0);
		bus.WriteLong(nextBitMap + BitMapPlanesOffset, nextPlane);
		bus.WriteWord(nextPlane, 0x4000);

		for (var offset = 0; offset < 0x54; offset++)
			bus.WriteByte(dbufInfo + (uint)offset, 0, 0);
		for (var offset = 0; offset < 0x40; offset++)
			bus.WriteByte(task + (uint)offset, 0, 0);
		InitializeExecList(bus, replyPort + MsgPortMsgListOffset);
		bus.WriteLong(replyPort + MsgPortSigTaskOffset, task);
		bus.WriteByte(replyPort + MsgPortSigBitOffset, 3, 0);
		bus.WriteLong(dbufInfo + (uint)safeMessageOffset + MessageReplyPortOffset, replyPort);
		bus.WriteLong(dbufInfo + (uint)displayMessageOffset + MessageReplyPortOffset, replyPort);

		var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
		var change = new M68kCpuState
		{
			A = { [0] = viewPort, [1] = nextBitMap, [2] = dbufInfo },
			Cycles = frameCycles / 2
		};
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -942), change));
		var messageList = replyPort + MsgPortMsgListOffset;
		Assert.Equal(messageList + 4, bus.ReadLong(messageList));
		Assert.Equal(replyPort, bus.ReadLong(safeMessageOffset + (uint)dbufInfo + MessageReplyPortOffset));
		var execPortServices = typeof(AmigaBootController)
			.GetField("_execPortServices", BindingFlags.Instance | BindingFlags.NonPublic)!
			.GetValue(boot)!;

		InvokeAdvanceSyntheticVBlankInterruptServers(boot, change.Cycles, frameCycles);
		var safeMessage = dbufInfo + (uint)safeMessageOffset;
		Assert.Equal(safeMessage, bus.ReadLong(messageList));
		var getMessageMethod = execPortServices.GetType().GetMethod("GetMsg")!;
		var safeGetState = new M68kCpuState { A = { [0] = replyPort } };
		Assert.Equal(safeMessage, (uint)getMessageMethod.Invoke(execPortServices, new object[] { safeGetState })!);
		Assert.Equal(messageList + 4, bus.ReadLong(messageList));

		InvokeAdvanceSyntheticVBlankInterruptServers(boot, frameCycles, frameCycles * 2);
		var displayMessage = dbufInfo + (uint)displayMessageOffset;
		Assert.Equal(displayMessage, bus.ReadLong(messageList));
		var displayGetState = new M68kCpuState { A = { [0] = replyPort } };
		Assert.Equal(displayMessage, (uint)getMessageMethod.Invoke(execPortServices, new object[] { displayGetState })!);
	}

	[Fact]
	public void NativeChangeVPBitMapRepliesDBufInfoMessagesAtAlternatingNtscFrameBoundaries()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500PlusEcsNtsc)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		const uint viewPort = 0x2300;
		const uint view = 0x2200;
		const uint nextBitMap = 0x2500;
		const uint nextPlane = 0x2520;
		const uint dbufInfo = 0x2800;
		const uint replyPort = 0x2A00;
		const uint task = 0x2B00;
		const int safeMessageOffset = 0x08;
		const int displayMessageOffset = 0x28;

		WriteMinimalViewPort(bus, viewPort);
		bus.WriteLong(view + ViewViewPortOffset, viewPort);
		bus.WriteWord(nextBitMap + BitMapBytesPerRowOffset, 2);
		bus.WriteWord(nextBitMap + BitMapRowsOffset, 1);
		bus.WriteByte(nextBitMap + BitMapDepthOffset, 1, 0);
		bus.WriteLong(nextBitMap + BitMapPlanesOffset, nextPlane);
		bus.WriteWord(nextPlane, 0x4000);

		for (var offset = 0; offset < 0x54; offset++)
			bus.WriteByte(dbufInfo + (uint)offset, 0, 0);
		for (var offset = 0; offset < 0x40; offset++)
			bus.WriteByte(task + (uint)offset, 0, 0);
		InitializeExecList(bus, replyPort + MsgPortMsgListOffset);
		bus.WriteLong(replyPort + MsgPortSigTaskOffset, task);
		bus.WriteByte(replyPort + MsgPortSigBitOffset, 3, 0);
		bus.WriteLong(dbufInfo + (uint)safeMessageOffset + MessageReplyPortOffset, replyPort);
		bus.WriteLong(dbufInfo + (uint)displayMessageOffset + MessageReplyPortOffset, replyPort);

		var firstFrame = bus.GetNextFrameStartCycle(0);
		var midFrame = firstFrame / 2;
		var safeCycle = bus.GetNextFrameStartCycle(midFrame);
		var displayCycle = bus.GetNextFrameStartCycle(safeCycle);
		var change = new M68kCpuState
		{
			A = { [0] = viewPort, [1] = nextBitMap, [2] = dbufInfo },
			Cycles = midFrame
		};
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -942), change));
		var messageList = replyPort + MsgPortMsgListOffset;
		Assert.Equal(messageList + 4, bus.ReadLong(messageList));

		InvokeAdvanceSyntheticVBlankInterruptServers(boot, midFrame, safeCycle);
		var safeMessage = dbufInfo + (uint)safeMessageOffset;
		Assert.Equal(safeMessage, bus.ReadLong(messageList));
		var execPortServices = typeof(AmigaBootController)
			.GetField("_execPortServices", BindingFlags.Instance | BindingFlags.NonPublic)!
			.GetValue(boot)!;
		var getMessageMethod = execPortServices.GetType().GetMethod("GetMsg")!;
		var safeGetState = new M68kCpuState { A = { [0] = replyPort } };
		Assert.Equal(safeMessage, (uint)getMessageMethod.Invoke(execPortServices, new object[] { safeGetState })!);
		Assert.Equal(messageList + 4, bus.ReadLong(messageList));

		InvokeAdvanceSyntheticVBlankInterruptServers(boot, safeCycle, displayCycle);
		var displayMessage = dbufInfo + (uint)displayMessageOffset;
		Assert.Equal(displayMessage, bus.ReadLong(messageList));
		var displayGetState = new M68kCpuState { A = { [0] = replyPort } };
		Assert.Equal(displayMessage, (uint)getMessageMethod.Invoke(execPortServices, new object[] { displayGetState })!);
	}

	[Fact]
	public void NativeChangeVPBitMapPreservesSafeBeforeDisplayWhenAHostAdvanceSkipsBothBoundaries()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		const uint viewPort = 0x2300;
		const uint view = 0x2200;
		const uint nextBitMap = 0x2500;
		const uint nextPlane = 0x2520;
		const uint dbufInfo = 0x2800;
		const uint replyPort = 0x2A00;
		const uint task = 0x2B00;
		const int safeMessageOffset = 0x08;
		const int displayMessageOffset = 0x28;

		WriteMinimalViewPort(bus, viewPort);
		bus.WriteLong(view + ViewViewPortOffset, viewPort);
		bus.WriteWord(nextBitMap + BitMapBytesPerRowOffset, 2);
		bus.WriteWord(nextBitMap + BitMapRowsOffset, 1);
		bus.WriteByte(nextBitMap + BitMapDepthOffset, 1, 0);
		bus.WriteLong(nextBitMap + BitMapPlanesOffset, nextPlane);
		bus.WriteWord(nextPlane, 0x4000);

		for (var offset = 0; offset < 0x54; offset++)
			bus.WriteByte(dbufInfo + (uint)offset, 0, 0);
		for (var offset = 0; offset < 0x40; offset++)
			bus.WriteByte(task + (uint)offset, 0, 0);
		InitializeExecList(bus, replyPort + MsgPortMsgListOffset);
		bus.WriteLong(replyPort + MsgPortSigTaskOffset, task);
		bus.WriteByte(replyPort + MsgPortSigBitOffset, 3, 0);
		bus.WriteLong(dbufInfo + (uint)safeMessageOffset + MessageReplyPortOffset, replyPort);
		bus.WriteLong(dbufInfo + (uint)displayMessageOffset + MessageReplyPortOffset, replyPort);

		var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
		var midFrame = frameCycles / 2;
		var safeCycle = bus.GetNextFrameStartCycle(midFrame);
		var displayCycle = bus.GetNextFrameStartCycle(safeCycle);
		var change = new M68kCpuState
		{
			A = { [0] = viewPort, [1] = nextBitMap, [2] = dbufInfo },
			Cycles = midFrame
		};
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -942), change));

		// Deliver both due messages in one outer scheduler quantum.  The
		// earlier safe reply must remain ahead of the later display reply even
		// though the host did not stop exactly at the first frame boundary.
		InvokeAdvanceSyntheticVBlankInterruptServers(boot, midFrame, displayCycle);

		var execPortServices = typeof(AmigaBootController)
			.GetField("_execPortServices", BindingFlags.Instance | BindingFlags.NonPublic)!
			.GetValue(boot)!;
		var getMessageMethod = execPortServices.GetType().GetMethod("GetMsg")!;
		var safeMessage = dbufInfo + (uint)safeMessageOffset;
		var displayMessage = dbufInfo + (uint)displayMessageOffset;
		var firstGetState = new M68kCpuState { A = { [0] = replyPort } };
		Assert.Equal(safeMessage, (uint)getMessageMethod.Invoke(execPortServices, new object[] { firstGetState })!);
		var secondGetState = new M68kCpuState { A = { [0] = replyPort } };
		Assert.Equal(displayMessage, (uint)getMessageMethod.Invoke(execPortServices, new object[] { secondGetState })!);
	}

	[Fact]
	public void ScrollVPortRequestsCopperRebuildForTheActiveView()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint viewPort = 0x2300;
		WriteMinimalViewPort(bus, viewPort);
		bus.WriteLong(view + ViewViewPortOffset, viewPort);

		var make = new M68kCpuState { A = { [0] = view, [1] = viewPort } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD8), make));
		var load = new M68kCpuState { A = { [1] = view } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE), load));
		var firstCopper = bus.ReadLong(viewPort + ViewPortDspInsOffset);

		var scroll = new M68kCpuState { A = { [0] = viewPort } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -588), scroll));
		Assert.NotEqual(firstCopper, bus.ReadLong(viewPort + ViewPortDspInsOffset));
	}

	[Fact]
	public void ScrollVPortUsesTheGuestCallCycleForMidFrameCopperPublication()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint viewPort = 0x2300;
		WriteMinimalViewPort(bus, viewPort);
		bus.WriteLong(view + ViewViewPortOffset, viewPort);

		var make = new M68kCpuState { A = { [0] = view, [1] = viewPort } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD8), make));
		var load = new M68kCpuState { A = { [1] = view } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE), load));
		var oldCopper = ((uint)bus.ReadWord(0x00DFF080) << 16) | bus.ReadWord(0x00DFF082);
		Assert.NotEqual(0u, oldCopper);

		var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
		var midFrame = frameCycles / 2;
		var scroll = new M68kCpuState
		{
			A = { [0] = viewPort },
			Cycles = midFrame
		};
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -588), scroll));
		Assert.Equal(midFrame, scroll.Cycles);
		var newCopper = bus.ReadLong(viewPort + ViewPortDspInsOffset);
		Assert.NotEqual(oldCopper, newCopper);

		// The rebuilt guest DspIns is visible immediately, but the custom-chip
		// copper pointer remains on the current stream until the next frame.
		Assert.Equal(oldCopper, ((uint)bus.ReadWord(0x00DFF080) << 16) | bus.ReadWord(0x00DFF082));

		var nextFrame = bus.GetNextFrameStartCycle(midFrame);
		InvokeAdvanceSyntheticVBlankInterruptServers(boot, midFrame, nextFrame);
		Assert.Equal(newCopper, ((uint)bus.ReadWord(0x00DFF080) << 16) | bus.ReadWord(0x00DFF082));
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
		var guest = new HostGuestMemory(bus);
		var platform = new HostGuestMemoryExecPlatform(guest);
		global::Amiga.ExecTaskCodec.Write(ref platform,
			global::Amiga.APTR.FromPointer(added), new global::Amiga.Task
		{
			Node = new global::Amiga.Node
			{
				Type = (byte)global::Amiga.NodeType.Task,
			},
			State = global::Amiga.TaskState.Removed,
			StackPointer = global::Amiga.APTR.FromPointer(stack),
			StackLower = global::Amiga.APTR.FromPointer(0x3600),
			StackUpper = global::Amiga.APTR.FromPointer(stack),
		});
		bus.WriteWord(entry, 0x4E75);
		ActivateRomExec(boot, execBase);

		var add = new M68kCpuState();
		add.A[1] = added; add.A[2] = entry; add.A[3] = entry;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -282), add));
		Assert.Equal(added, add.D[0]);
		Assert.Equal(added, bus.ReadLong(execBase + ExecTaskReadyOffset));
		var task = global::Amiga.ExecTaskCodec.Read(ref platform,
			global::Amiga.APTR.FromPointer(added));
		Assert.Equal(global::Amiga.TaskState.Ready, task.State);
		Assert.Equal(stack - 4 - global::Amiga.TaskFrame68k.Size,
			task.StackPointer.Raw);
		Assert.Equal(entry, PortableExec.ExecTaskFrame68kCodec.ReadProgramCounter(
			ref platform, task.StackPointer).Raw);
		Assert.Equal(0, PortableExec.ExecTaskFrame68kCodec.ReadStatusRegister(
			ref platform, task.StackPointer));
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
		Assert.Equal(unchecked((byte)(sbyte)global::Amiga.IoError.BadAddress), bus.ReadByte(request + 0x1F));

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
		Assert.Equal((uint)global::Amiga.TrackDiskDriveType.Drive35, bus.ReadLong(request + 0x20));

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
		Assert.Equal(global::Amiga.DriveGeometry.Size, bus.ReadLong(request + 0x20));
		Assert.Equal(512u, bus.ReadLong(geometry));
		Assert.Equal(2u, bus.ReadLong(geometry + 4));
		Assert.Equal(2u, bus.ReadLong(geometry + global::Amiga.DriveGeometryLayout.Heads));

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
		Assert.Equal(unchecked((byte)(sbyte)global::Amiga.IoError.NoCommand), bus.ReadByte(request + 0x1F));

		writeProtected = true;
		bus.WriteWord(request + 0x1C, 15); // TD_PROTSTATUS
		Assert.True(InvokeHostTrap(bus, device - 30, begin));
		Assert.Equal(uint.MaxValue, bus.ReadLong(request + 0x20));
		bus.WriteWord(request + 0x1C, 11); // TD_FORMAT
		bus.WriteLong(request + 0x2C, 20);
		Assert.True(InvokeHostTrap(bus, device - 30, begin));
		Assert.Equal((byte)global::Amiga.TrackDiskError.WriteProtected, bus.ReadByte(request + 0x1F));
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

		// A title update owns only the title-bar rows. Keep a marker below that
		// bar so a later SetWindowTitles call cannot erase application pixels.
		var screenBitMap = openScreenState.D[0] + ScreenBitMapOffset;
		var markerPlane = bus.ReadLong(screenBitMap + BitMapPlanesOffset);
		var markerByte = markerPlane + (uint)(40 * bus.ReadWord(screenBitMap + BitMapBytesPerRowOffset));
		bus.WriteByte(markerByte, 0x01, 0);

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
		Assert.Equal((byte)0x01, bus.ReadByte(markerByte));
	}

	[Fact]
	public void SetWindowTitlesRejectsWrappedTitleBeforeLowAliasScan()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var openScreen = new M68kCpuState();
		var openWindow = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			openScreen));
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -204),
			openWindow));

		var screen = openScreen.D[0];
		var stableTitle = InvokeAllocMem(bus, 32, 0);
		WriteCString(bus, stableTitle, "Stable title");
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -276),
			new M68kCpuState { A = { [0] = openWindow.D[0], [1] = NoTitleChange, [2] = stableTitle } }));
		Assert.Equal(stableTitle, bus.ReadLong(screen + ScreenTitleOffset));

		const uint wrappedTitle = 0xFFFF_FFFCu;
		bus.MapWritableMemory(wrappedTitle, new byte[4] { (byte)'X', (byte)'X', (byte)'X', (byte)'X' });
		var lowAliasBefore = bus.ReadByte(0);
		bus.WriteByte(0, (byte)'Y', 0);
		try
		{
			var malformed = new M68kCpuState
			{
				A = { [0] = openWindow.D[0], [1] = wrappedTitle, [2] = NoTitleChange },
				D = { [0] = 0x1357_9BDFu },
				Cycles = 77
			};
			Assert.True(InvokeHostTrap(
				bus,
				Lvo(AmigaKickstartHost.IntuitionLibraryBase, -276),
				malformed));
			Assert.Equal(stableTitle, bus.ReadLong(screen + ScreenTitleOffset));
			Assert.Equal(0x1357_9BDFu, malformed.D[0]);
			Assert.Equal(77, malformed.Cycles);
			Assert.Equal((byte)'Y', bus.ReadByte(0));
		}
		finally
		{
			bus.WriteByte(0, lowAliasBefore, 0);
		}
	}

	[Fact]
	public void SetWindowTitlesRefusesReadOnlyScreenTitleBeforeRepaint()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var openScreen = new M68kCpuState();
		var openWindow = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			openScreen));
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -204),
			openWindow));

		var screen = openScreen.D[0];
		var stableTitle = InvokeAllocMem(bus, 32, 0);
		WriteCString(bus, stableTitle, "Stable title");
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -276),
			new M68kCpuState
			{
				A = { [0] = openWindow.D[0], [1] = NoTitleChange, [2] = stableTitle }
			}));
		Assert.Equal(stableTitle, bus.ReadLong(screen + ScreenTitleOffset));

		bus.MapReadOnlyMemory(
			screen + ScreenTitleOffset,
			new byte[]
			{
				(byte)(stableTitle >> 24),
				(byte)(stableTitle >> 16),
				(byte)(stableTitle >> 8),
				(byte)stableTitle
			});
		var replacementTitle = InvokeAllocMem(bus, 32, 0);
		WriteCString(bus, replacementTitle, "Replacement title");
		var declined = new M68kCpuState
		{
			A = { [0] = openWindow.D[0], [1] = NoTitleChange, [2] = replacementTitle },
			D = { [0] = 0x1357_9BDFu },
			Cycles = 77
		};

		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -276),
			declined));
		Assert.Equal(stableTitle, bus.ReadLong(screen + ScreenTitleOffset));
		Assert.Equal(0x1357_9BDFu, declined.D[0]);
		Assert.Equal(77, declined.Cycles);
	}

	[Fact]
	public void QuietSyntheticScreenKeepsTitlePointerButSuppressesHostTitleRendering()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var tags = InvokeAllocMem(bus, 16, 0);
		bus.WriteLong(tags, ScreenTagQuiet);
		bus.WriteLong(tags + 4, 1);
		bus.WriteLong(tags + 8, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));
		var screen = open.D[0];
		Assert.NotEqual(0u, screen);

		var bitMap = screen + ScreenBitMapOffset;
		var plane = bus.ReadLong(bitMap + BitMapPlanesOffset);
		var bytesPerRow = bus.ReadWord(bitMap + BitMapBytesPerRowOffset);
		for (var row = 0; row < 24; row++)
		{
			for (var column = 0; column < bytesPerRow; column++)
				Assert.Equal((byte)0, bus.ReadByte(plane + (uint)(row * bytesPerRow + column)));
		}

		var title = InvokeAllocMem(bus, 32, 0);
		WriteCString(bus, title, "Quiet title must not paint");
		var setTitles = new M68kCpuState { A = { [2] = title } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -276),
			setTitles));
		Assert.Equal(title, bus.ReadLong(screen + ScreenTitleOffset));
		for (var row = 0; row < 24; row++)
		{
			for (var column = 0; column < bytesPerRow; column++)
				Assert.Equal((byte)0, bus.ReadByte(plane + (uint)(row * bytesPerRow + column)));
		}
	}

	[Fact]
	public void SetWindowTitlesIgnoresForeignWindowOwnership()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var openScreen = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			openScreen));

		var openWindow = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -204),
			openWindow));

		var screen = openScreen.D[0];
		var originalTitle = bus.ReadLong(screen + ScreenTitleOffset);
		var foreignTitle = InvokeAllocMem(bus, 32, 0);
		WriteCString(bus, foreignTitle, "Foreign window must not claim synthetic screen");

		var foreignCall = new M68kCpuState
		{
			A = { [0] = 0x2A00, [2] = foreignTitle }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -276),
			foreignCall));

		Assert.Equal(originalTitle, bus.ReadLong(screen + ScreenTitleOffset));
		Assert.NotEqual(0u, openWindow.D[0]);
	}

	[Fact]
	public void SetWindowTitlesNullScreenTitleClearsOnlyTitleBar()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var openScreen = new M68kCpuState();
		var openWindow = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			openScreen));
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -204),
			openWindow));

		var screen = openScreen.D[0];
		var bitMap = screen + ScreenBitMapOffset;
		var plane = bus.ReadLong(bitMap + BitMapPlanesOffset);
		var bytesPerRow = bus.ReadWord(bitMap + BitMapBytesPerRowOffset);
		var markerByte = plane + (uint)(40 * bytesPerRow);
		bus.WriteByte(markerByte, 0x01, 0);

		var title = InvokeAllocMem(bus, 32, 0);
		WriteCString(bus, title, "Title to clear");
		var setTitle = new M68kCpuState
		{
			A = { [0] = openWindow.D[0], [1] = NoTitleChange, [2] = title }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -276),
			setTitle));

		var clearTitle = new M68kCpuState
		{
			A = { [0] = openWindow.D[0], [1] = 0, [2] = 0 }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -276),
			clearTitle));

		Assert.Equal(0u, bus.ReadLong(screen + ScreenTitleOffset));
		for (var row = 0; row < 24; row++)
		{
			for (var column = 0; column < bytesPerRow; column++)
				Assert.Equal((byte)0, bus.ReadByte(plane + (uint)(row * bytesPerRow + column)));
		}
		Assert.Equal((byte)0x01, bus.ReadByte(markerByte));
	}

	[Fact]
	public void SetWindowTitlesNullWindowBeforeOpenDoesNotAllocateSyntheticScreen()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var title = InvokeAllocMem(bus, 32, 0);
		WriteCString(bus, title, "Must not create a screen");

		var beforeOpen = new M68kCpuState { A = { [2] = title } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -276),
			beforeOpen));

		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));
		Assert.Equal(0u, bus.ReadLong(open.D[0] + ScreenTitleOffset));
	}

	[Fact]
	public void ShowTitleFalseRetainsTitlePixelsOnSyntheticSingleSurface()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var openScreen = new M68kCpuState();
		var openWindow = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			openScreen));
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -204),
			openWindow));

		var screen = openScreen.D[0];
		var bitMap = screen + ScreenBitMapOffset;
		var plane = bus.ReadLong(bitMap + BitMapPlanesOffset);
		var bytesPerRow = bus.ReadWord(bitMap + BitMapBytesPerRowOffset);
		var title = InvokeAllocMem(bus, 32, 0);
		WriteCString(bus, title, "Layered title");
		var setTitle = new M68kCpuState
		{
			A = { [0] = openWindow.D[0], [1] = NoTitleChange, [2] = title }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -276),
			setTitle));

		var paintedBefore = 0;
		for (var row = 0; row < 24; row++)
		{
			for (var column = 0; column < bytesPerRow; column++)
				if (bus.ReadByte(plane + (uint)(row * bytesPerRow + column)) != 0)
					paintedBefore++;
		}
		Assert.True(paintedBefore > 0);

		var behind = new M68kCpuState { A = { [0] = screen }, D = { [0] = 0 } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -282),
			behind));
		Assert.Equal(0, bus.ReadWord(screen + ScreenFlagsOffset) & 0x0010);

		var paintedAfter = 0;
		for (var row = 0; row < 24; row++)
		{
			for (var column = 0; column < bytesPerRow; column++)
				if (bus.ReadByte(plane + (uint)(row * bytesPerRow + column)) != 0)
					paintedAfter++;
		}
		Assert.Equal(paintedBefore, paintedAfter);
	}

	[Fact]
	public void ShowTitleRejectsWrappedScreenTitleBeforeFlagPublication()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));

		var screen = open.D[0];
		var flagsBefore = bus.ReadWord(screen + ScreenFlagsOffset);
		const uint wrappedTitle = 0xFFFF_FFFCu;
		bus.MapWritableMemory(wrappedTitle, new byte[4] { (byte)'X', (byte)'X', (byte)'X', (byte)'X' });
		var lowAliasBefore = bus.ReadByte(0);
		bus.WriteByte(0, (byte)'Y', 0);
		bus.WriteLong(screen + ScreenTitleOffset, wrappedTitle, 0);
		try
		{
			var show = new M68kCpuState
			{
				A = { [0] = screen },
				D = { [0] = 0u },
				Cycles = 91
			};
			Assert.True(InvokeHostTrap(
				bus,
				Lvo(AmigaKickstartHost.IntuitionLibraryBase, -282),
				show));
			Assert.Equal(flagsBefore, bus.ReadWord(screen + ScreenFlagsOffset));
			Assert.Equal(wrappedTitle, bus.ReadLong(screen + ScreenTitleOffset));
			Assert.Equal(0u, show.D[0]);
			Assert.Equal(91, show.Cycles);
			Assert.Equal((byte)'Y', bus.ReadByte(0));
		}
		finally
		{
			bus.WriteByte(0, lowAliasBefore, 0);
		}
	}

	[Fact]
	public void ShowTitleRefusesReadOnlyFlagsBeforeRepaint()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));

		var screen = open.D[0];
		var flagsBefore = bus.ReadWord(screen + ScreenFlagsOffset);
		bus.MapReadOnlyMemory(
			screen + ScreenFlagsOffset,
			new byte[] { (byte)(flagsBefore >> 8), (byte)flagsBefore });

		var show = new M68kCpuState
		{
			A = { [0] = screen },
			D = { [0] = 0u },
			Cycles = 91
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -282),
			show));
		Assert.Equal(flagsBefore, bus.ReadWord(screen + ScreenFlagsOffset));
		Assert.Equal(0u, show.D[0]);
		Assert.Equal(91, show.Cycles);
	}

	[Fact]
	public void SyntheticRethinkDisplayLeavesExtendedModesViewportsToTheProvider()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var openScreen = new M68kCpuState();
		var openWindow = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			openScreen));
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -204),
			openWindow));

		var screen = openScreen.D[0];
		var viewPort = screen + ScreenViewPortOffset;
		var viewState = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -294),
			viewState));
		var view = viewState.D[0];
		var originalLof = bus.ReadLong(view + ViewLofCprListOffset);
		var originalShf = bus.ReadLong(view + ViewShfCprListOffset);
		var originalDspIns = bus.ReadLong(viewPort + ViewPortDspInsOffset);
		Assert.NotEqual(0u, originalLof);
		Assert.NotEqual(0u, originalDspIns);

		// A nonzero vp_ExtendedModes belongs to the monitor/provider owner.
		// Trigger the synthetic Intuition rethink path through a title update;
		// it must leave the active CPR/DspIns publication intact.
		bus.WriteByte(
			viewPort + (uint)ViewPortExtendedModesOffset,
			1,
			0);
		var title = InvokeAllocMem(bus, 32, 0);
		WriteCString(bus, title, "Provider-owned viewport");
		var setTitle = new M68kCpuState
		{
			A = { [0] = openWindow.D[0], [1] = title, [2] = NoTitleChange }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -276),
			setTitle));

		Assert.Equal(originalLof, bus.ReadLong(view + ViewLofCprListOffset));
		Assert.Equal(originalShf, bus.ReadLong(view + ViewShfCprListOffset));
		Assert.Equal(originalDspIns, bus.ReadLong(viewPort + ViewPortDspInsOffset));
		Assert.Equal(1, bus.ReadByte(viewPort + (uint)ViewPortExtendedModesOffset));
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
	public void OpenScreenDoesNotReadWrappedNewScreenOptionalFieldsThroughLowAliases()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		const uint wrappedNewScreen = 0xFFFF_FFF0u;
		const uint lowAliasTitleField = 0x0000_0004u;
		bus.MapWritableMemory(wrappedNewScreen, new byte[0x10]);
		Assert.True(bus.IsMappedMemoryRange(wrappedNewScreen, 0x10));
		bus.WriteWord(wrappedNewScreen + NewScreenWidthOffset, 320);
		bus.WriteWord(wrappedNewScreen + NewScreenHeightOffset, 200);
		bus.WriteByte(wrappedNewScreen + NewScreenDepthOffset, 2, 0);
		bus.WriteWord(wrappedNewScreen + NewScreenViewModesOffset, 0);
		bus.WriteWord(wrappedNewScreen + 0x0E, 0x000F); // CUSTOMSCREEN

		// NewScreen.Font and NewScreen.DefaultTitle would wrap to addresses 0 and
		// 4.  Make the title alias fully valid so an unchecked host addition would
		// visibly claim it; the checked path must leave it to native Intuition.
		var aliasTitle = InvokeAllocMem(bus, 6, 0);
		bus.WriteByte(aliasTitle, (byte)'A', 0);
		bus.WriteByte(aliasTitle + 1, (byte)'L', 0);
		bus.WriteByte(aliasTitle + 2, (byte)'I', 0);
		bus.WriteByte(aliasTitle + 3, (byte)'A', 0);
		bus.WriteByte(aliasTitle + 4, (byte)'S', 0);
		bus.WriteByte(aliasTitle + 5, 0, 0);
		bus.WriteLong(lowAliasTitleField, aliasTitle);

		var configure = typeof(AmigaBootController).GetMethod(
			"ConfigureSyntheticScreenFromNewScreen",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(configure);
		configure!.Invoke(boot, new object[] { wrappedNewScreen, false, false, false });

		var defaultTitleField = typeof(AmigaBootController).GetProperty(
			"_syntheticScreenDefaultTitleAddress",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(defaultTitleField);
		Assert.Equal(0u, (uint)defaultTitleField!.GetValue(boot)!);
	}

	[Fact]
	public void OpenScreenResolvesLegacyStandardDimensionsFromTheDisplayMode()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var newScreen = InvokeAllocMem(bus, 0x20, 0);
		bus.WriteWord(newScreen + NewScreenLeftOffset, unchecked((ushort)-7));
		bus.WriteWord(newScreen + NewScreenTopOffset, 3);
		bus.WriteWord(newScreen + NewScreenWidthOffset, ushort.MaxValue); // STDSCREENWIDTH (-1)
		bus.WriteWord(newScreen + NewScreenHeightOffset, ushort.MaxValue); // STDSCREENHEIGHT (-1)
		bus.WriteByte(newScreen + NewScreenDepthOffset, 2, 0);
		bus.WriteWord(newScreen + NewScreenViewModesOffset, ViewModeHires);

		var openScreenState = new M68kCpuState { A = { [0] = newScreen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			openScreenState));

		var screen = openScreenState.D[0];
		Assert.NotEqual(0u, screen);
		Assert.Equal(AmigaConstants.PalLowResStandardWidth * 2, bus.ReadWord(screen + ScreenWidthOffset));
		Assert.Equal(AmigaConstants.PalLowResStandardHeight, bus.ReadWord(screen + ScreenHeightOffset));
		Assert.Equal((short)-7, unchecked((short)bus.ReadWord(screen + ScreenLeftEdgeOffset)));
		Assert.Equal((short)3, unchecked((short)bus.ReadWord(screen + ScreenTopEdgeOffset)));

		var close = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
	}

	[Fact]
	public void OpenScreenTagListPublishesSaErrorCodeFromExtendedNewScreenTags()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var newScreen = InvokeAllocMem(bus, 0x24, 0);
		var extension = InvokeAllocMem(bus, 3 * 8, 0);
		var errorCode = InvokeAllocMem(bus, 4, 0);

		bus.WriteWord(newScreen + NewScreenWidthOffset, 320);
		bus.WriteWord(newScreen + NewScreenHeightOffset, 200);
		bus.WriteByte(newScreen + NewScreenDepthOffset, 2, 0);
		bus.WriteWord(newScreen + 0x0E, 0x1000); // NS_EXTENDED
		bus.WriteLong(newScreen + 0x20, extension);
		bus.WriteLong(extension, ScreenTagErrorCode);
		bus.WriteLong(extension + 4, errorCode);
		bus.WriteLong(extension + 8, 0);
		bus.WriteLong(errorCode, 0xA5A5_A5A5);

		var open = new M68kCpuState { A = { [0] = newScreen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		Assert.NotEqual(0u, open.D[0]);
		Assert.Equal(0u, bus.ReadLong(errorCode));

		var close = new M68kCpuState { A = { [0] = open.D[0] } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
	}

	[Fact]
	public void OpenScreenTagListPrefersExplicitSaErrorCodeOverExtendedTags()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var newScreen = InvokeAllocMem(bus, 0x24, 0);
		var extension = InvokeAllocMem(bus, 3 * 8, 0);
		var explicitTags = InvokeAllocMem(bus, 3 * 8, 0);
		var extensionError = InvokeAllocMem(bus, 4, 0);
		var explicitError = InvokeAllocMem(bus, 4, 0);

		bus.WriteWord(newScreen + NewScreenWidthOffset, 320);
		bus.WriteWord(newScreen + NewScreenHeightOffset, 200);
		bus.WriteByte(newScreen + NewScreenDepthOffset, 2, 0);
		bus.WriteWord(newScreen + 0x0E, 0x1000); // NS_EXTENDED
		bus.WriteLong(newScreen + 0x20, extension);
		bus.WriteLong(extension, ScreenTagErrorCode);
		bus.WriteLong(extension + 4, extensionError);
		bus.WriteLong(extension + 8, 0);
		bus.WriteLong(extensionError, 0xE1E1_E1E1);

		bus.WriteLong(explicitTags, ScreenTagErrorCode);
		bus.WriteLong(explicitTags + 4, explicitError);
		bus.WriteLong(explicitTags + 8, 0);
		bus.WriteLong(explicitError, 0xA5A5_A5A5);

		var open = new M68kCpuState
		{
			A = { [0] = newScreen, [1] = explicitTags }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		Assert.NotEqual(0u, open.D[0]);
		Assert.Equal(0u, bus.ReadLong(explicitError));
		Assert.Equal(0xE1E1_E1E1u, bus.ReadLong(extensionError));

		var close = new M68kCpuState { A = { [0] = open.D[0] } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
	}

	[Fact]
	public void OpenScreenTagListAppliesNativeGeometryAndDisplayIdTags()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var newScreen = InvokeAllocMem(bus, 0x20, 0);
		bus.WriteWord(newScreen + NewScreenWidthOffset, 320);
		bus.WriteWord(newScreen + NewScreenHeightOffset, 256);
		bus.WriteByte(newScreen + NewScreenDepthOffset, 2, 0);

		// SA_Width, SA_Height, SA_Depth, SA_DisplayID, TAG_DONE.
		var tags = InvokeAllocMem(bus, 5 * 8, 0);
		bus.WriteLong(tags, 0x8000_0023);
		bus.WriteLong(tags + 4, 640);
		bus.WriteLong(tags + 8, 0x8000_0024);
		bus.WriteLong(tags + 12, 200);
		bus.WriteLong(tags + 16, 0x8000_0025);
		bus.WriteLong(tags + 20, 3);
		bus.WriteLong(tags + 24, 0x8000_0032);
		bus.WriteLong(tags + 28, 0x0002_9000); // PAL monitor + HIRES_KEY.
		bus.WriteLong(tags + 32, 0);

		var open = new M68kCpuState { A = { [0] = newScreen, [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		var screen = open.D[0];
		Assert.NotEqual(0u, screen);
		Assert.Equal(640, bus.ReadWord(screen + ScreenWidthOffset));
		Assert.Equal(200, bus.ReadWord(screen + ScreenHeightOffset));
		Assert.Equal(3, bus.ReadByte(screen + ScreenBitMapOffset + BitMapDepthOffset));
		Assert.Equal(
			ViewModeHires,
			bus.ReadWord(screen + ScreenViewPortOffset + ViewPortModesOffset));
	}

	[Fact]
	public void OpenScreenTagListResolvesStandardDimensionsFromTheActiveDisplayClip()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var tags = InvokeAllocMem(bus, 4 * 8, 0);
		bus.WriteLong(tags, ScreenTagDisplayId);
		bus.WriteLong(tags + 4, GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey);
		bus.WriteLong(tags + 8, ScreenTagWidth);
		bus.WriteLong(tags + 12, uint.MaxValue); // STDSCREENWIDTH (-1)
		bus.WriteLong(tags + 16, ScreenTagHeight);
		bus.WriteLong(tags + 20, uint.MaxValue); // STDSCREENHEIGHT (-1)
		bus.WriteLong(tags + 24, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		var screen = open.D[0];
		Assert.NotEqual(0u, screen);
		Assert.Equal(AmigaConstants.PalLowResStandardWidth * 2, bus.ReadWord(screen + ScreenWidthOffset));
		Assert.Equal(AmigaConstants.PalLowResStandardHeight, bus.ReadWord(screen + ScreenHeightOffset));
		Assert.Equal((short)0, unchecked((short)bus.ReadWord(screen + ScreenLeftEdgeOffset)));
		Assert.Equal((short)0, unchecked((short)bus.ReadWord(screen + ScreenTopEdgeOffset)));

		var close = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
	}

	[Fact]
	public void OpenScreenTagListKeepsLegacyPositionWhenStandardDimensionsOverrideSize()
	{
		var machine = StartBootShim(MachineProfile.A500PlusEcsNtsc);
		var bus = machine.Bus;
		var newScreen = InvokeAllocMem(bus, 0x20, 0);
		bus.WriteWord(newScreen + NewScreenLeftOffset, unchecked((ushort)-12));
		bus.WriteWord(newScreen + NewScreenTopOffset, 5);
		bus.WriteWord(newScreen + NewScreenWidthOffset, 320);
		bus.WriteWord(newScreen + NewScreenHeightOffset, 100);
		bus.WriteByte(newScreen + NewScreenDepthOffset, 2, 0);
		bus.WriteWord(newScreen + NewScreenViewModesOffset, ViewModeHires);

		var tags = InvokeAllocMem(bus, 3 * 8, 0);
		bus.WriteLong(tags, ScreenTagWidth);
		bus.WriteLong(tags + 4, uint.MaxValue); // STDSCREENWIDTH (-1)
		bus.WriteLong(tags + 8, ScreenTagHeight);
		bus.WriteLong(tags + 12, uint.MaxValue); // STDSCREENHEIGHT (-1)
		bus.WriteLong(tags + 16, 0);

		var open = new M68kCpuState { A = { [0] = newScreen, [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		var screen = open.D[0];
		Assert.NotEqual(0u, screen);
		Assert.Equal(AmigaConstants.PalLowResStandardWidth * 2, bus.ReadWord(screen + ScreenWidthOffset));
		Assert.Equal(AmigaConstants.NtscLowResStandardHeight, bus.ReadWord(screen + ScreenHeightOffset));
		Assert.Equal((short)-12, unchecked((short)bus.ReadWord(screen + ScreenLeftEdgeOffset)));
		Assert.Equal((short)5, unchecked((short)bus.ReadWord(screen + ScreenTopEdgeOffset)));

		var close = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
	}

	[Fact]
	public void OpenScreenTagListPreservesLegacyModeWhenDisplayIdTagIsAbsent()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var newScreen = InvokeAllocMem(bus, 0x20, 0);
		bus.WriteWord(newScreen + NewScreenWidthOffset, 640);
		bus.WriteWord(newScreen + NewScreenHeightOffset, 200);
		bus.WriteByte(newScreen + NewScreenDepthOffset, 2, 0);
		bus.WriteWord(newScreen + NewScreenViewModesOffset, ViewModeHires);
		var tags = InvokeAllocMem(bus, 2 * 8, 0);
		bus.WriteLong(tags, ScreenTagSharePens);
		bus.WriteLong(tags + 4, 1);
		bus.WriteLong(tags + 8, 0);

		var open = new M68kCpuState { A = { [0] = newScreen, [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		var screen = open.D[0];
		Assert.NotEqual(0u, screen);
		Assert.Equal(640, bus.ReadWord(screen + ScreenWidthOffset));
		Assert.Equal(
			ViewModeHires,
			bus.ReadWord(screen + ScreenViewPortOffset + ViewPortModesOffset));
	}

	[Fact]
	public void OpenScreenTagListLeavesUnknownDisplayIdToProvider()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var errorCode = InvokeAllocMem(bus, 4, 0);
		bus.WriteLong(errorCode, 0xA5A5A5A5);
		var tags = InvokeAllocMem(bus, 3 * 8, 0);
		bus.WriteLong(tags, ScreenTagDisplayId);
		bus.WriteLong(tags + 4, 0x7FFF_1234);
		bus.WriteLong(tags + 8, ScreenTagErrorCode);
		bus.WriteLong(tags + 12, errorCode);
		bus.WriteLong(tags + 16, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		Assert.Equal(0u, open.D[0]);
		Assert.Equal(0xA5A5A5A5u, bus.ReadLong(errorCode));
	}

	[Fact]
	public void OpenScreenTagListBorrowsStandardPlanarCustomBitmapWithoutTakingOwnership()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var plane0State = new M68kCpuState { D = { [0] = 320, [1] = 256 } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -492),
			plane0State));
		var plane0 = plane0State.D[0];
		var plane1State = new M68kCpuState { D = { [0] = 320, [1] = 256 } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -492),
			plane1State));
		var plane1 = plane1State.D[0];
		Assert.NotEqual(0u, plane0);
		Assert.NotEqual(0u, plane1);

		var customBitMap = InvokeAllocMem(bus, GraphicsLayouts.BitMapSize, 0);
		bus.WriteWord(customBitMap + BitMapBytesPerRowOffset, 40);
		bus.WriteWord(customBitMap + BitMapRowsOffset, 256);
		bus.WriteByte(customBitMap + BitMapFlagsOffset, 8, 0); // BMF_STANDARD
		bus.WriteByte(customBitMap + BitMapDepthOffset, 2, 0);
		bus.WriteLong(customBitMap + BitMapPlanesOffset, plane0);
		bus.WriteLong(customBitMap + BitMapPlanesOffset + 4, plane1);
		bus.WriteByte(plane0, 0xA5, 0);
		bus.WriteByte(plane1, 0x5A, 0);
		var originalHeader = Enumerable.Range(0, GraphicsLayouts.BitMapSize)
			.Select(offset => bus.ReadByte(customBitMap + (uint)offset))
			.ToArray();
		var errorCode = InvokeAllocMem(bus, 4, 0);
		bus.WriteLong(errorCode, 0x5A5A5A5A);
		var tags = InvokeAllocMem(bus, 7 * 8, 0);
		bus.WriteLong(tags, ScreenTagBitMap);
		bus.WriteLong(tags + 4, customBitMap);
		bus.WriteLong(tags + 8, ScreenTagWidth);
		bus.WriteLong(tags + 12, 320);
		bus.WriteLong(tags + 16, ScreenTagHeight);
		bus.WriteLong(tags + 20, 256);
		bus.WriteLong(tags + 24, ScreenTagDepth);
		bus.WriteLong(tags + 28, 2);
		bus.WriteLong(tags + 32, ScreenTagErrorCode);
		bus.WriteLong(tags + 36, errorCode);
		bus.WriteLong(tags + 40, ScreenTagQuiet);
		bus.WriteLong(tags + 44, 1);
		bus.WriteLong(tags + 48, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		var screen = open.D[0];
		Assert.NotEqual(0u, screen);
		Assert.Equal(customBitMap,
			bus.ReadLong(screen + ScreenRastPortOffset + RastPortBitMapOffset));
		var viewPort = screen + ScreenViewPortOffset;
		var rasInfo = bus.ReadLong(viewPort + ViewPortRasInfoOffset);
		Assert.Equal(customBitMap, bus.ReadLong(rasInfo + 4));
		Assert.Equal(0u, bus.ReadLong(errorCode));

		var close = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
		Assert.Equal(0xA5, bus.ReadByte(plane0));
		Assert.Equal(0x5A, bus.ReadByte(plane1));
		Assert.Equal(originalHeader,
			Enumerable.Range(0, GraphicsLayouts.BitMapSize)
				.Select(offset => bus.ReadByte(customBitMap + (uint)offset))
				.ToArray());
	}

	[Fact]
	public void OpenScreenTagListBorrowsCompactMinPlanesCustomBitmap()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var plane0State = new M68kCpuState { D = { [0] = 320, [1] = 256 } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -492),
			plane0State));
		var plane0 = plane0State.D[0];
		var plane1State = new M68kCpuState { D = { [0] = 320, [1] = 256 } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -492),
			plane1State));
		var plane1 = plane1State.D[0];

		// BMF_MINPLANES permits a declared-depth BitMap envelope: depth two
		// needs only the header prefix plus two plane pointers (0x10 bytes), not
		// the unused six-pointer tail of the full 0x28-byte structure.
		const uint compactBitMapSpan = BitMapPlanesOffset + (2 * sizeof(uint));
		var customBitMap = InvokeAllocMem(bus, compactBitMapSpan, 0);
		bus.WriteWord(customBitMap + BitMapBytesPerRowOffset, 40);
		bus.WriteWord(customBitMap + BitMapRowsOffset, 256);
		bus.WriteByte(
			customBitMap + BitMapFlagsOffset,
			(byte)(8 | (1 << 4)),
			0); // BMF_STANDARD | BMF_MINPLANES
		bus.WriteByte(customBitMap + BitMapDepthOffset, 2, 0);
		bus.WriteLong(customBitMap + BitMapPlanesOffset, plane0);
		bus.WriteLong(customBitMap + BitMapPlanesOffset + 4, plane1);
		var originalHeader = Enumerable.Range(0, (int)compactBitMapSpan)
			.Select(offset => bus.ReadByte(customBitMap + (uint)offset))
			.ToArray();

		var tags = InvokeAllocMem(bus, 6 * 8, 0);
		bus.WriteLong(tags, ScreenTagBitMap);
		bus.WriteLong(tags + 4, customBitMap);
		bus.WriteLong(tags + 8, ScreenTagWidth);
		bus.WriteLong(tags + 12, 320);
		bus.WriteLong(tags + 16, ScreenTagHeight);
		bus.WriteLong(tags + 20, 256);
		bus.WriteLong(tags + 24, ScreenTagDepth);
		bus.WriteLong(tags + 28, 2);
		bus.WriteLong(tags + 32, ScreenTagQuiet);
		bus.WriteLong(tags + 36, 1);
		bus.WriteLong(tags + 40, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		var screen = open.D[0];
		Assert.NotEqual(0u, screen);
		Assert.Equal(
			customBitMap,
			bus.ReadLong(screen + ScreenRastPortOffset + RastPortBitMapOffset));

		var close = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
		Assert.Equal(
			originalHeader,
			Enumerable.Range(0, (int)compactBitMapSpan)
				.Select(offset => bus.ReadByte(customBitMap + (uint)offset))
				.ToArray());
	}

	[Fact]
	public void OpenScreenTagListLeavesMalformedCustomBitmapToProvider()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var customBitMap = InvokeAllocMem(bus, GraphicsLayouts.BitMapSize, 0);
		bus.WriteWord(customBitMap + BitMapBytesPerRowOffset, 40);
		bus.WriteWord(customBitMap + BitMapRowsOffset, 256);
		bus.WriteByte(customBitMap + BitMapFlagsOffset, 8, 0);
		bus.WriteByte(customBitMap + BitMapDepthOffset, 2, 0);
		// Leave the declared plane links null: a standard-planar custom screen
		// cannot be claimed safely without the caller-owned display storage.
		var errorCode = InvokeAllocMem(bus, 4, 0);
		bus.WriteLong(errorCode, 0x5A5A5A5A);
		var tags = InvokeAllocMem(bus, 3 * 8, 0);
		bus.WriteLong(tags, ScreenTagBitMap);
		bus.WriteLong(tags + 4, customBitMap);
		bus.WriteLong(tags + 8, ScreenTagErrorCode);
		bus.WriteLong(tags + 12, errorCode);
		bus.WriteLong(tags + 16, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));
		Assert.Equal(0u, open.D[0]);
		Assert.Equal(0x5A5A5A5Au, bus.ReadLong(errorCode));
	}

	[Fact]
	public void OpenScreenTagListLeavesNonStandardCustomBitmapToProvider()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var plane0State = new M68kCpuState { D = { [0] = 320, [1] = 256 } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -492),
			plane0State));
		var plane1State = new M68kCpuState { D = { [0] = 320, [1] = 256 } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -492),
			plane1State));

		var customBitMap = InvokeAllocMem(bus, GraphicsLayouts.BitMapSize, 0);
		bus.WriteWord(customBitMap + BitMapBytesPerRowOffset, 40);
		bus.WriteWord(customBitMap + BitMapRowsOffset, 256);
		// Valid geometry and plane storage are not sufficient for the portable
		// screen path: a non-standard bitmap belongs to the native/provider
		// owner (including CyberGraphX), not the OCS/ECS planar bridge.
		bus.WriteByte(customBitMap + BitMapFlagsOffset, 0, 0);
		bus.WriteByte(customBitMap + BitMapDepthOffset, 2, 0);
		bus.WriteLong(customBitMap + BitMapPlanesOffset, plane0State.D[0]);
		bus.WriteLong(customBitMap + BitMapPlanesOffset + 4, plane1State.D[0]);

		var errorCode = InvokeAllocMem(bus, 4, 0);
		bus.WriteLong(errorCode, 0x6C6C6C6Cu);
		var tags = InvokeAllocMem(bus, 3 * 8, 0);
		bus.WriteLong(tags, ScreenTagBitMap);
		bus.WriteLong(tags + 4, customBitMap);
		bus.WriteLong(tags + 8, ScreenTagErrorCode);
		bus.WriteLong(tags + 12, errorCode);
		bus.WriteLong(tags + 16, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));
		Assert.Equal(0u, open.D[0]);
		Assert.Equal(0x6C6C6C6Cu, bus.ReadLong(errorCode));
	}

	[Fact]
	public void OpenScreenTagListLeavesPublicScreenOwnershipToProvider()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var publicName = InvokeAllocMem(bus, 8, 0);
		var errorCode = InvokeAllocMem(bus, 4, 0);
		bus.WriteLong(errorCode, 0x6B6B6B6B);
		var tags = InvokeAllocMem(bus, 3 * 8, 0);
		bus.WriteLong(tags, ScreenTagPubName);
		bus.WriteLong(tags + 4, publicName);
		bus.WriteLong(tags + 8, ScreenTagErrorCode);
		bus.WriteLong(tags + 12, errorCode);
		bus.WriteLong(tags + 16, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		// Public-screen list and notification ownership stays with Intuition;
		// this shim must not consume the request as a private custom screen.
		Assert.Equal(0u, open.D[0]);
		Assert.Equal(0x6B6B6B6Bu, bus.ReadLong(errorCode));
	}

	[Theory]
	[InlineData((ushort)2)]
	[InlineData((ushort)0x0040)]
	public void OpenScreenDeclinesLegacyPublicAndCustomBitmapOwnership(ushort screenType)
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var newScreen = InvokeAllocMem(bus, 0x20, 0);
		bus.WriteWord(newScreen + 0x0E, screenType);

		var open = new M68kCpuState { A = { [0] = newScreen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));
		Assert.Equal(0u, open.D[0]);

		var retry = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			retry));
		Assert.NotEqual(0u, retry.D[0]);
		var close = new M68kCpuState { A = { [0] = retry.D[0] } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
	}

	[Fact]
	public void OpenScreenTagListRejectsUnreadableLegacyNewScreen()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var errorCode = InvokeAllocMem(bus, 4, 0);
		bus.WriteLong(errorCode, 0x7C7C7C7C);
		var tags = InvokeAllocMem(bus, 3 * 8, 0);
		bus.WriteLong(tags, ScreenTagWidth);
		bus.WriteLong(tags + 4, 640);
		bus.WriteLong(tags + 8, ScreenTagErrorCode);
		bus.WriteLong(tags + 12, errorCode);
		bus.WriteLong(tags + 16, 0);

		var open = new M68kCpuState
		{
			A = { [0] = 0xFFFF_F000, [1] = tags }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		// A supplied but unreadable NewScreen is not equivalent to NULL.  The
		// provider boundary must see the original request intact.
		Assert.Equal(0u, open.D[0]);
		Assert.Equal(0x7C7C7C7Cu, bus.ReadLong(errorCode));
	}

	[Fact]
	public void OpenScreenTagListUsesNtscNativeDisplayIdForZeroHeight()
	{
		var machine = StartBootShim(MachineProfile.A500PlusEcsNtsc);
		var bus = machine.Bus;
		var tags = InvokeAllocMem(bus, 4 * 8, 0);
		bus.WriteLong(tags, 0x8000_0023); // SA_Width
		bus.WriteLong(tags + 4, 640);
		bus.WriteLong(tags + 8, 0x8000_0032); // SA_DisplayID
		bus.WriteLong(tags + 12, 0x0001_9000); // NTSC monitor + HIRES_KEY.
		bus.WriteLong(tags + 16, 0x8000_0024); // SA_Height
		bus.WriteLong(tags + 20, 0);
		bus.WriteLong(tags + 24, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		var screen = open.D[0];
		Assert.Equal(640, bus.ReadWord(screen + ScreenWidthOffset));
		Assert.Equal(AmigaConstants.NtscLowResStandardHeight, bus.ReadWord(screen + ScreenHeightOffset));
		Assert.Equal(
			ViewModeHires,
			bus.ReadWord(screen + ScreenViewPortOffset + ViewPortModesOffset));
	}

	[Fact]
	public void OpenScreenTagListUsesExplicitPalDisplayIdOnNtscHost()
	{
		var machine = StartBootShim(MachineProfile.A500PlusEcsNtsc);
		var bus = machine.Bus;
		var tags = InvokeAllocMem(bus, 4 * 8, 0);
		bus.WriteLong(tags, 0x8000_0032); // SA_DisplayID
		bus.WriteLong(tags + 4, 0x0002_9000); // PAL monitor + HIRES_KEY
		bus.WriteLong(tags + 8, 0x8000_0023); // SA_Width
		bus.WriteLong(tags + 12, 640);
		bus.WriteLong(tags + 16, 0x8000_0024); // SA_Height
		bus.WriteLong(tags + 20, 0);
		bus.WriteLong(tags + 24, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		var screen = open.D[0];
		Assert.NotEqual(0u, screen);
		Assert.Equal(640, bus.ReadWord(screen + ScreenWidthOffset));
		Assert.Equal(AmigaConstants.PalLowResStandardHeight,
			bus.ReadWord(screen + ScreenHeightOffset));
		Assert.Equal(
			ViewModeHires,
			bus.ReadWord(screen + ScreenViewPortOffset + ViewPortModesOffset));
	}

	[Fact]
	public void OpenScreenTagListCapsSuperHiresDepthAtEcsTwoPlanes()
	{
		var machine = StartBootShim(MachineProfile.A500PlusEcsNtsc);
		var bus = machine.Bus;
		var tags = InvokeAllocMem(bus, 3 * 8, 0);
		bus.WriteLong(tags, 0x8000_0032); // SA_DisplayID
		bus.WriteLong(tags + 4, GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresKey);
		bus.WriteLong(tags + 8, 0x8000_0025); // SA_Depth
		bus.WriteLong(tags + 12, 6);
		bus.WriteLong(tags + 16, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		var screen = open.D[0];
		Assert.NotEqual(0u, screen);
		Assert.Equal(
			2,
			bus.ReadByte(screen + ScreenBitMapOffset + BitMapDepthOffset));
		Assert.Equal(
			(ushort)(ViewModeHires | ViewModeSuperHires),
			bus.ReadWord(screen + ScreenViewPortOffset + ViewPortModesOffset));
	}

	[Fact]
	public void OpenScreenTagListLeavesEcsSuperHiresToProviderOnOcs()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var newScreen = InvokeAllocMem(bus, 0x20, 0);
		bus.WriteWord(newScreen + NewScreenWidthOffset, 640);
		bus.WriteWord(newScreen + NewScreenHeightOffset, 200);
		bus.WriteByte(newScreen + NewScreenDepthOffset, 5, 0);
		var errorCode = InvokeAllocMem(bus, 4, 0xA5);
		bus.WriteLong(errorCode, 0xA5A5A5A5);
		var tags = InvokeAllocMem(bus, 3 * 8, 0);
		bus.WriteLong(tags, 0x8000_0032); // SA_DisplayID
		bus.WriteLong(tags + 4, GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey);
		bus.WriteLong(tags + 8, 0x8000_002A); // SA_ErrorCode
		bus.WriteLong(tags + 12, errorCode);
		bus.WriteLong(tags + 16, 0);

		var open = new M68kCpuState { A = { [0] = newScreen, [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		Assert.Equal(0u, open.D[0]);
		Assert.Equal(0xA5A5A5A5u, bus.ReadLong(errorCode));
	}

	[Fact]
	public void OpenScreenTagListPublishesTheInterleavedBitmapRequest()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var tags = InvokeAllocMem(bus, 2 * 8, 0);
		bus.WriteLong(tags, 0x8000_0042); // SA_Interleaved
		bus.WriteLong(tags + 4, 1);
		bus.WriteLong(tags + 8, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		var screen = open.D[0];
		Assert.NotEqual(0u, screen);
		Assert.Equal(
			0x08,
			bus.ReadByte(screen + ScreenBitMapOffset + BitMapFlagsOffset));
	}

	[Fact]
	public void OpenScreenTagListPublishesAggregateInterleavedRowsOnEcsDisplayHardware()
	{
		var machine = StartBootShim(MachineProfile.A500PlusEcsPal);
		var bus = machine.Bus;
		var tags = InvokeAllocMem(bus, 5 * 8, 0);
		bus.WriteLong(tags, 0x8000_0023); // SA_Width
		bus.WriteLong(tags + 4, 64);
		bus.WriteLong(tags + 8, 0x8000_0024); // SA_Height
		bus.WriteLong(tags + 12, 16);
		bus.WriteLong(tags + 16, 0x8000_0025); // SA_Depth
		bus.WriteLong(tags + 20, 2);
		bus.WriteLong(tags + 24, 0x8000_0042); // SA_Interleaved
		bus.WriteLong(tags + 28, 1);
		bus.WriteLong(tags + 32, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		var screen = open.D[0];
		Assert.NotEqual(0u, screen);
		var bitMap = screen + ScreenBitMapOffset;
		Assert.Equal(64, bus.ReadWord(screen + ScreenWidthOffset));
		Assert.Equal(16, bus.ReadWord(bitMap + BitMapBytesPerRowOffset));
		Assert.Equal(0x0C, bus.ReadByte(bitMap + BitMapFlagsOffset));

		var plane0 = bus.ReadLong(bitMap + BitMapPlanesOffset);
		var plane1 = bus.ReadLong(bitMap + BitMapPlanesOffset + 4);
		Assert.Equal(8u, plane1 - plane0);

		// SetRast exercises the host renderer's row addressing rather than only
		// the published header.  With an interleaved depth-two map, row one is
		// one aggregate stride (16 bytes) after each plane's start.
		var setRast = new M68kCpuState
		{
			A = { [1] = screen + ScreenRastPortOffset },
			// Pen 3 sets both declared planes; the assertions below are
			// specifically checking the aggregate row stride, not pen decoding.
			D = { [0] = 3 }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -234),
			setRast));
		Assert.Equal(0xFF, bus.ReadByte(plane0 + 16));
		Assert.Equal(0xFF, bus.ReadByte(plane1 + 16));

		var close = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
	}

	[Fact]
	public void OpenScreenTagListPublishesSharePensScreenFlag()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var tags = InvokeAllocMem(bus, 2 * 8, 0);
		bus.WriteLong(tags, 0x8000_0040); // SA_SharePens
		bus.WriteLong(tags + 4, 1);
		bus.WriteLong(tags + 8, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		Assert.NotEqual(0u, open.D[0]);
		Assert.NotEqual(0, bus.ReadWord(open.D[0] + ScreenFlagsOffset) & 0x0400);
	}

	[Fact]
	public void OpenScreenTagListAcceptsLargeTagSkipBeforeFollowingItem()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint tags = 0x0003_0000;
		const uint skipItems = 4096;
		const uint followingTag = tags + 8u + (skipItems * 8u);

		// TAG_SKIP carries a ULONG item count.  The following item is deliberately
		// beyond the old portable-parser cap of 4095 items.
		bus.MapWritableMemory(tags, new byte[0x8020]);
		bus.WriteLong(tags, 3); // TAG_SKIP
		bus.WriteLong(tags + 4, skipItems);
		bus.WriteLong(followingTag, 0x8000_0023); // SA_Width
		bus.WriteLong(followingTag + 4, 640);
		bus.WriteLong(followingTag + 8, 0); // TAG_END

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		Assert.NotEqual(0u, open.D[0]);
		Assert.Equal(640, bus.ReadWord(open.D[0] + ScreenWidthOffset));

		var close = new M68kCpuState { A = { [0] = open.D[0] } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
	}

	[Fact]
	public void OpenScreenTagListAcceptsACompleteTagItemAtTheGuestAddressEnd()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint finalTag = 0xFFFF_FFF8;

		// The final aligned TagItem occupies exactly $FFFF_FFF8..$FFFF_FFFF.
		// A TAG_END at that address is a complete, valid empty list.
		bus.MapWritableMemory(finalTag, new byte[8]);
		bus.WriteLong(finalTag, 0);
		bus.WriteLong(finalTag + 4, 0);

		var open = new M68kCpuState { A = { [1] = finalTag } };
        Assert.True(InvokeHostTrap(
            bus,
            Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
            open));

		Assert.NotEqual(0u, open.D[0]);
		var close = new M68kCpuState { A = { [0] = open.D[0] } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
	}

	[Fact]
	public void OpenScreenTagListRejectsATagItemThatCrossesTheGuestAddressBoundary()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint wrappedTags = 0xFFFF_FFFE;

		// The host map is deliberately permissive; the portable decoder must
		// reject the two-byte prefix before it can observe wrapped low memory.
		bus.MapWritableMemory(wrappedTags, new byte[8]);
		bus.WriteLong(wrappedTags, 0);
		bus.WriteLong(2, 0);

		var open = new M68kCpuState { A = { [1] = wrappedTags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));
		Assert.Equal(0u, open.D[0]);
	}

	[Fact]
	public void OpenScreenTagListDoesNotConsumeWrappedLowMemoryAfterTheFinalItem()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint finalTag = 0xFFFF_FFF8;

		// Deliberately map both sides of the wrap.  A cursor that advances from
		// the final complete item to zero must still be rejected; it must not
		// consume a low-memory TAG_END and publish a screen.
		bus.MapWritableMemory(finalTag, new byte[8]);
		bus.WriteLong(finalTag, 0x8000_0023); // SA_Width, not TAG_END
		bus.WriteLong(finalTag + 4, 640);
		bus.MapWritableMemory(0, new byte[8]);
		bus.WriteLong(0, 0);
		bus.WriteLong(4, 0);

		var open = new M68kCpuState { A = { [1] = finalTag } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));
		Assert.Equal(0u, open.D[0]);
	}

	[Fact]
	public void OpenScreenTagListRejectsOddTagItemAddresses()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint oddTags = 0x2601;

		bus.MapWritableMemory(oddTags, new byte[8]);
		bus.WriteLong(oddTags, 0x8000_0023); // SA_Width
		bus.WriteLong(oddTags + 4, 640);

		var open = new M68kCpuState { A = { [1] = oddTags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));
		Assert.Equal(0u, open.D[0]);
	}

	[Fact]
	public void OpenScreenTagListRejectsCyclicMoreChains()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint tags = 0x2600;
		const uint continuation = 0x2620;

		// TAG_MORE cycles are rejected before any prefix can be applied.
		bus.MapWritableMemory(tags, new byte[8]);
		bus.MapWritableMemory(continuation, new byte[8]);
		bus.WriteLong(tags, 2); // TAG_MORE
		bus.WriteLong(tags + 4, continuation);
		bus.WriteLong(continuation, 2); // TAG_MORE
		bus.WriteLong(continuation + 4, tags);
		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));
		Assert.Equal(0u, open.D[0]);
	}

	[Theory]
	[InlineData(ScreenTagDClip)]
	[InlineData(ScreenTagPens)]
	[InlineData(ScreenTagColors)]
	[InlineData(ScreenTagColors32)]
	public void OpenScreenTagListDeclinesUnreadableOptionalPayloadsBeforePublishing(
		uint optionalTag)
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var errorCode = InvokeAllocMem(bus, 4, 0);
		bus.WriteLong(errorCode, 0xA5A5_A5A5);
		var tags = InvokeAllocMem(bus, 3 * 8, 0);
		bus.WriteLong(tags, optionalTag);
		bus.WriteLong(tags + 4, 0xFFFF_F000);
		bus.WriteLong(tags + 8, ScreenTagErrorCode);
		bus.WriteLong(tags + 12, errorCode);
		bus.WriteLong(tags + 16, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		// The malformed caller-owned payload must remain with native/provider
		// ownership.  In particular, SA_ErrorCode is not a success write and no
		// synthetic screen may be published from the remaining tags.
		Assert.Equal(0u, open.D[0]);
		Assert.Equal(0xA5A5_A5A5u, bus.ReadLong(errorCode));

		// A declined request must not poison the reset-scoped compatibility path.
		var retry = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			retry));
		Assert.NotEqual(0u, retry.D[0]);

		var close = new M68kCpuState { A = { [0] = retry.D[0] } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
	}

	[Fact]
	public void OpenScreenTagListDeclinesAnUnreadableSaErrorCodeDestination()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var tags = InvokeAllocMem(bus, 3 * 8, 0);
		bus.WriteLong(tags, ScreenTagErrorCode);
		bus.WriteLong(tags + 4, 0xFFFF_F000);
		bus.WriteLong(tags + 8, ScreenTagWidth);
		bus.WriteLong(tags + 12, 320);
		bus.WriteLong(tags + 16, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		// The output pointer is part of the caller-owned request envelope.  A
		// malformed destination must not be silently ignored after a synthetic
		// Screen has been published.
		Assert.Equal(0u, open.D[0]);

		var retry = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			retry));
		Assert.NotEqual(0u, retry.D[0]);

		var close = new M68kCpuState { A = { [0] = retry.D[0] } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
	}

	[Fact]
	public void OpenScreenTagListDeclinesAReadOnlySaErrorCodeDestination()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint sentinel = 0xA5A5_A5A5;
		var errorCode = InvokeAllocMem(bus, 4, 0);
		bus.WriteLong(errorCode, sentinel);
		bus.MapReadOnlyMemory(
			errorCode,
			new byte[]
			{
				unchecked((byte)(sentinel >> 24)),
				unchecked((byte)(sentinel >> 16)),
				unchecked((byte)(sentinel >> 8)),
				unchecked((byte)sentinel)
			});

		var tags = InvokeAllocMem(bus, 3 * 8, 0);
		bus.WriteLong(tags, ScreenTagErrorCode);
		bus.WriteLong(tags + 4, errorCode);
		bus.WriteLong(tags + 8, ScreenTagWidth);
		bus.WriteLong(tags + 12, 320);
		bus.WriteLong(tags + 16, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		// A readable but read-only output pointer is still caller/provider
		// owned.  Do not publish a synthetic Screen whose SA_ErrorCode result
		// cannot be committed.
		Assert.Equal(0u, open.D[0]);
		Assert.Equal(sentinel, bus.ReadLong(errorCode));

		var retry = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			retry));
		Assert.NotEqual(0u, retry.D[0]);

		var close = new M68kCpuState { A = { [0] = retry.D[0] } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void OpenScreenTagListDeclinesUnterminatedColorPayloadsBeforePublishing(
		bool colors32)
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const int recordCount = 256;
		var payload = InvokeAllocMem(
			bus,
			colors32 ? (uint)recordCount * 16u : (uint)recordCount * 8u,
			0);
		for (var index = 0; index < recordCount; index++)
		{
			if (colors32)
			{
				var record = payload + (uint)(index * 16);
				bus.WriteLong(record, 0x0001_0000); // one entry starting at COLOR0
				bus.WriteLong(record + 4, 0);
				bus.WriteLong(record + 8, 0);
				bus.WriteLong(record + 12, 0);
			}
			else
			{
				var record = payload + (uint)(index * 8);
				bus.WriteWord(record, 0);
				bus.WriteWord(record + 2, 0);
				bus.WriteWord(record + 4, 0);
				bus.WriteWord(record + 6, 0);
			}
		}

		var errorCode = InvokeAllocMem(bus, 4, 0);
		bus.WriteLong(errorCode, 0x5A5A_5A5A);
		var tags = InvokeAllocMem(bus, 3 * 8, 0);
		bus.WriteLong(tags, colors32 ? ScreenTagColors32 : ScreenTagColors);
		bus.WriteLong(tags + 4, payload);
		bus.WriteLong(tags + 8, ScreenTagErrorCode);
		bus.WriteLong(tags + 12, errorCode);
		bus.WriteLong(tags + 16, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));
		Assert.Equal(0u, open.D[0]);
		Assert.Equal(0x5A5A_5A5Au, bus.ReadLong(errorCode));
	}

	[Fact]
	public void GraphicsDisplayInfoDefaultModeUsesSelectedNtscProfileThroughHostGateway()
	{
		var machine = StartBootShim(MachineProfile.A500PlusEcsNtsc);
		var bus = machine.Bus;
		const uint buffer = 0x2600;
		const uint defaultHires = 0x0000_8000;

		var dimensions = new M68kCpuState
		{
			A = { [0] = 0, [1] = buffer },
			D = { [0] = 0x58, [1] = 0x8000_1000u, [2] = defaultHires }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -756),
			dimensions));
		Assert.Equal(0x58u, dimensions.D[0]);
		Assert.Equal((ushort)200, bus.ReadWord(buffer + 0x14));
		Assert.Equal((ushort)241, bus.ReadWord(buffer + 0x18));

		var monitor = new M68kCpuState
		{
			A = { [0] = 0, [1] = buffer },
			D = { [0] = 0x60, [1] = 0x8000_2000u, [2] = defaultHires }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -756),
			monitor));
		Assert.Equal(0x60u, monitor.D[0]);
		Assert.Equal((ushort)262, bus.ReadWord(buffer + 0x24));
		Assert.Equal((ushort)21, bus.ReadWord(buffer + 0x28));
	}

	[Fact]
	public void GraphicsDisplayInfoKeepsEcsSuperHiresRecordButReportsNoChipsOnOcsHost()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint buffer = 0x2600;
		const uint palSuperHires = 0x0002_9020;

		var find = new M68kCpuState { D = { [0] = palSuperHires } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -726),
			find));
		Assert.Equal(palSuperHires, find.D[0]);

		var unavailable = new M68kCpuState { D = { [0] = palSuperHires } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -798),
			unavailable));
		Assert.Equal(1u, unavailable.D[0]);

		var data = new M68kCpuState
		{
			A = { [0] = find.D[0], [1] = buffer },
			D = { [0] = 0x30, [1] = 0x8000_0000u, [2] = palSuperHires }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -756),
			data));
		Assert.Equal(0x30u, data.D[0]);
		Assert.Equal((ushort)1, bus.ReadWord(buffer + 0x10));
	}

	[Fact]
	public void GraphicsDisplayInfoAdvertisesAgaPlanarPrecisionAndDepth()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A1200AgaPal)
			.WithKickstart(KickstartConfiguration.FromRomImage(
				KickstartVersion.Kickstart30,
				new byte[512 * 1024]))
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		const uint buffer = 0x2600;
		Assert.Equal(
			(byte)GraphicsChipRevision.SetAa,
			bus.ReadByte(
				AmigaKickstartHost.GraphicsLibraryBase +
				(uint)GfxBaseChipRevBits0Offset));

		var display = new M68kCpuState
		{
			A = { [0] = 0, [1] = buffer },
			D = { [0] = 0x38, [1] = 0x8000_0000u, [2] = 0 }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -756),
			display));
		Assert.Equal(0x38u, display.D[0]);
		Assert.Equal((ushort)0, bus.ReadWord(buffer + 0x10));
		Assert.NotEqual(0u, bus.ReadLong(buffer + 0x12) & 0x0001_0000u);
		Assert.Equal(ushort.MaxValue, bus.ReadWord(buffer + 0x1E));
		Assert.Equal((byte)8, bus.ReadByte(buffer + 0x28));
		Assert.Equal((byte)8, bus.ReadByte(buffer + 0x29));
		Assert.Equal((byte)8, bus.ReadByte(buffer + 0x2A));

		var dimensions = new M68kCpuState
		{
			A = { [0] = 0, [1] = buffer },
			D = { [0] = 0x58, [1] = 0x8000_1000u, [2] = 0 }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -756),
			dimensions));
		Assert.Equal(0x58u, dimensions.D[0]);
		Assert.Equal((ushort)8, bus.ReadWord(buffer + 0x10));
	}

	[Fact]
	public void A1200BestModeIdAcceptsEightPlaneRgb8Request()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A1200AgaPal)
			.WithKickstart(KickstartConfiguration.FromRomImage(
				KickstartVersion.Kickstart30,
				new byte[512 * 1024]))
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		var tags = InvokeAllocMem(bus, 9 * 8, 0);
		bus.WriteLong(tags + 0, GraphicsDisplayDatabase.BidTagNominalWidth);
		bus.WriteLong(tags + 4, 640);
		bus.WriteLong(tags + 8, GraphicsDisplayDatabase.BidTagNominalHeight);
		bus.WriteLong(tags + 12, 256);
		bus.WriteLong(tags + 16, GraphicsDisplayDatabase.BidTagDesiredWidth);
		bus.WriteLong(tags + 20, 640);
		bus.WriteLong(tags + 24, GraphicsDisplayDatabase.BidTagDesiredHeight);
		bus.WriteLong(tags + 28, 256);
		bus.WriteLong(tags + 32, GraphicsDisplayDatabase.BidTagDepth);
		bus.WriteLong(tags + 36, 8);
		bus.WriteLong(tags + 40, GraphicsDisplayDatabase.BidTagRedBits);
		bus.WriteLong(tags + 44, 8);
		bus.WriteLong(tags + 48, GraphicsDisplayDatabase.BidTagGreenBits);
		bus.WriteLong(tags + 52, 8);
		bus.WriteLong(tags + 56, GraphicsDisplayDatabase.BidTagBlueBits);
		bus.WriteLong(tags + 60, 8);
		bus.WriteLong(tags + 64, GraphicsDisplayDatabase.BidTagMonitorId);
		bus.WriteLong(tags + 68, GraphicsModeIds.PalMonitor);
		bus.WriteLong(tags + 72, 0);

		var best = new M68kCpuState { A = { [0] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, (int)GraphicsLvo.BestModeIDA),
			best));
		Assert.Equal(GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey, best.D[0]);
	}

	[Fact]
	public void OpenScreenTagListAcceptsDefaultMonitorModeIds()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var tags = InvokeAllocMem(bus, 4 * 8, 0);
		bus.WriteLong(tags, 0x8000_0032); // SA_DisplayID
		bus.WriteLong(tags + 4, GraphicsModeIds.DefaultMonitor | GraphicsModeIds.HiresKey);
		bus.WriteLong(tags + 8, 0x8000_0024); // SA_Height
		bus.WriteLong(tags + 12, 0);
		bus.WriteLong(tags + 16, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		var screen = open.D[0];
		Assert.NotEqual(0u, screen);
		Assert.Equal(AmigaConstants.PalHighResWidth, bus.ReadWord(screen + ScreenWidthOffset));
		Assert.Equal(AmigaConstants.PalLowResStandardHeight, bus.ReadWord(screen + ScreenHeightOffset));
		Assert.Equal(
			ViewModeHires,
			bus.ReadWord(screen + ScreenViewPortOffset + ViewPortModesOffset));
	}

	[Fact]
	public void OpenScreenPublishesTheDefaultColorMapForEverySyntheticViewport()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));

		var screen = open.D[0];
		Assert.NotEqual(0u, screen);
		var viewPort = screen + ScreenViewPortOffset;
		var colorMap = bus.ReadLong(viewPort + (uint)GraphicsLayouts.ViewPortColorMap);
		Assert.NotEqual(0u, colorMap);
		Assert.Equal(
			(ushort)32,
			bus.ReadWord(colorMap + (uint)GraphicsLayouts.ColorMapCount));
		Assert.Equal(
			viewPort,
			bus.ReadLong(colorMap + (uint)GraphicsLayouts.ColorMapViewPort));
		Assert.NotEqual(
			0u,
			bus.ReadLong(colorMap + (uint)GraphicsLayouts.ColorMapColorTable));

		var close = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
	}

	[Fact]
	public void OpenScreenTagListAppliesPositionPaletteAndStandardScreenFlags()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var newScreen = InvokeAllocMem(bus, 0x20, 0);
		bus.WriteWord(newScreen + NewScreenWidthOffset, 320);
		bus.WriteWord(newScreen + NewScreenHeightOffset, 200);
		bus.WriteByte(newScreen + NewScreenDepthOffset, 2, 0);

		var colors = InvokeAllocMem(bus, 3 * 8, 0);
		bus.WriteWord(colors, 1);
		bus.WriteWord(colors + 2, 0x000F);
		bus.WriteWord(colors + 4, 0x0000);
		bus.WriteWord(colors + 6, 0x0001);
		bus.WriteWord(colors + 8, ushort.MaxValue);

		var errorCode = InvokeAllocMem(bus, 4, 0xA5);
		// SA_Left, SA_Top, SA_Colors, SA_ShowTitle, SA_Behind,
		// SA_Quiet, SA_AutoScroll, SA_ErrorCode, TAG_DONE.
		var tags = InvokeAllocMem(bus, 9 * 8, 0);
		bus.WriteLong(tags, 0x8000_0021);
		bus.WriteLong(tags + 4, unchecked((uint)-4));
		bus.WriteLong(tags + 8, 0x8000_0022);
		bus.WriteLong(tags + 12, 7);
		bus.WriteLong(tags + 16, 0x8000_0029);
		bus.WriteLong(tags + 20, colors);
		bus.WriteLong(tags + 24, 0x8000_0044);
		bus.WriteLong(tags + 28, 1);
		bus.WriteLong(tags + 32, 0x8000_0045);
		bus.WriteLong(tags + 36, 1);
		bus.WriteLong(tags + 40, 0x8000_0046);
		bus.WriteLong(tags + 44, 1);
		bus.WriteLong(tags + 48, 0x8000_0047);
		bus.WriteLong(tags + 52, 1);
		bus.WriteLong(tags + 56, 0x8000_002A);
		bus.WriteLong(tags + 60, errorCode);
		bus.WriteLong(tags + 64, 0);

		var open = new M68kCpuState { A = { [0] = newScreen, [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		var screen = open.D[0];
		var viewPort = screen + ScreenViewPortOffset;
		Assert.Equal(-4, unchecked((short)bus.ReadWord(screen + ScreenLeftEdgeOffset)));
		Assert.Equal(7, unchecked((short)bus.ReadWord(screen + ScreenTopEdgeOffset)));
		Assert.Equal(-4, unchecked((short)bus.ReadWord(viewPort + ViewPortDxOffset)));
		Assert.Equal(7, unchecked((short)bus.ReadWord(viewPort + ViewPortDyOffset)));
		Assert.Equal(
			(ushort)0x4190,
			(ushort)(bus.ReadWord(screen + ScreenFlagsOffset) & 0x4190));
		Assert.Equal(0u, bus.ReadLong(errorCode));

		var viewState = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -294),
			viewState));
		var view = viewState.D[0];
		var copperList = bus.ReadLong(bus.ReadLong(view + ViewLofCprListOffset) + CprListStartOffset);
		Assert.Equal((ushort)0x0F01, ReadCopperMoveValue(bus, copperList, 0x0182));
	}

	[Fact]
	public void OpenScreenTagListAppliesSaVideoControlToSyntheticColorMap()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint vTagChromaKeySet = 0x8000_0001;
		var videoTags = InvokeAllocMem(bus, 2 * 8, 0);
		bus.WriteLong(videoTags, vTagChromaKeySet);
		bus.WriteLong(videoTags + 4, 1);
		bus.WriteLong(videoTags + 8, 0);

		var errorCode = InvokeAllocMem(bus, 4, 0xA5);
		var tags = InvokeAllocMem(bus, 3 * 8, 0);
		bus.WriteLong(tags, ScreenTagVideoControl);
		bus.WriteLong(tags + 4, videoTags);
		bus.WriteLong(tags + 8, ScreenTagErrorCode);
		bus.WriteLong(tags + 12, errorCode);
		bus.WriteLong(tags + 16, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		var screen = open.D[0];
		Assert.NotEqual(0u, screen);
		var viewPort = screen + ScreenViewPortOffset;
		var colorMap = bus.ReadLong(viewPort + (uint)GraphicsLayouts.ViewPortColorMap);
		Assert.NotEqual(0u, colorMap);
		Assert.Equal(
			1,
			bus.ReadByte(colorMap + (uint)GraphicsLayouts.ColorMapFlags) & 1);
		Assert.Equal(0u, bus.ReadLong(errorCode));

		var close = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
	}

	[Fact]
	public void OpenScreenTagListDeclinesMalformedSaVideoControlBeforePublishing()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var errorCode = InvokeAllocMem(bus, 4, 0xA5);
		bus.WriteLong(errorCode, 0xA5A5_A5A5);
		var tags = InvokeAllocMem(bus, 3 * 8, 0);
		bus.WriteLong(tags, ScreenTagVideoControl);
		bus.WriteLong(tags + 4, 0xFFFF_F001);
		bus.WriteLong(tags + 8, ScreenTagErrorCode);
		bus.WriteLong(tags + 12, errorCode);
		bus.WriteLong(tags + 16, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));
		Assert.Equal(0u, open.D[0]);
		Assert.Equal(0xA5A5_A5A5u, bus.ReadLong(errorCode));
	}

	[Fact]
	public void FailedSaVideoControlRollsBackEarlierPaletteUpdates()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint vTagChromaKeySet = 0x8000_0001;
		var videoTags = InvokeAllocMem(bus, 3 * 8, 0);
		bus.WriteLong(videoTags, vTagChromaKeySet);
		bus.WriteLong(videoTags + 4, 1);
		bus.WriteLong(videoTags + 8, 0xDEAD_BEEF); // unsupported after a valid setter
		bus.WriteLong(videoTags + 12, 0);
		bus.WriteLong(videoTags + 16, 0);

		var colors32 = InvokeAllocMem(bus, 4 + 12 + 4, 0);
		bus.WriteLong(colors32, 0x0001_0001); // one entry at COLOR1
		bus.WriteLong(colors32 + 4, 0xF000_0000);
		bus.WriteLong(colors32 + 8, 0x1000_0000);
		bus.WriteLong(colors32 + 12, 0x1000_0000);
		bus.WriteLong(colors32 + 16, 0);

		var errorCode = InvokeAllocMem(bus, 4, 0xA5);
		bus.WriteLong(errorCode, 0xA5A5_A5A5);
		var tags = InvokeAllocMem(bus, 4 * 8, 0);
		bus.WriteLong(tags, ScreenTagColors32);
		bus.WriteLong(tags + 4, colors32);
		bus.WriteLong(tags + 8, ScreenTagVideoControl);
		bus.WriteLong(tags + 12, videoTags);
		bus.WriteLong(tags + 16, ScreenTagErrorCode);
		bus.WriteLong(tags + 20, errorCode);
		bus.WriteLong(tags + 24, 0);

		var failedOpen = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			failedOpen));
		Assert.Equal(0u, failedOpen.D[0]);
		Assert.Equal(0xA5A5_A5A5u, bus.ReadLong(errorCode));

		var retry = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			retry));
		Assert.NotEqual(0u, retry.D[0]);

		var viewState = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -294),
			viewState));
		var view = viewState.D[0];
		var copperList = bus.ReadLong(bus.ReadLong(view + ViewLofCprListOffset) + CprListStartOffset);
		Assert.Equal((ushort)0x0238, ReadCopperMoveValue(bus, copperList, 0x0182));

		var close = new M68kCpuState { A = { [0] = retry.D[0] } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
	}

	[Fact]
	public void OpenScreenTagListAllocatesRequestedColorMapEntries()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var errorCode = InvokeAllocMem(bus, 4, 0);
		var tags = InvokeAllocMem(bus, 3 * 8, 0);
		bus.WriteLong(tags, ScreenTagColorMapEntries);
		bus.WriteLong(tags + 4, 8);
		bus.WriteLong(tags + 8, ScreenTagErrorCode);
		bus.WriteLong(tags + 12, errorCode);
		bus.WriteLong(tags + 16, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		var screen = open.D[0];
		Assert.NotEqual(0u, screen);
		var viewPort = screen + ScreenViewPortOffset;
		var colorMap = bus.ReadLong(viewPort + (uint)GraphicsLayouts.ViewPortColorMap);
		Assert.NotEqual(0u, colorMap);
		Assert.Equal((ushort)8, bus.ReadWord(colorMap + (uint)GraphicsLayouts.ColorMapCount));
		Assert.Equal(0u, bus.ReadLong(errorCode));

		var close = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
	}

	[Fact]
	public void OpenScreenTagListProjectsColors32IntoOcsPaletteMoves()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var colors32 = InvokeAllocMem(bus, 4 + 12 + 4, 0);
		bus.WriteLong(colors32, 0x0001_0002); // one entry starting at COLOR2
		bus.WriteLong(colors32 + 4, 0xF000_0000);
		bus.WriteLong(colors32 + 8, 0x1000_0000);
		bus.WriteLong(colors32 + 12, 0xA000_0000);
		bus.WriteLong(colors32 + 16, 0);
		var tags = InvokeAllocMem(bus, 2 * 8, 0);
		bus.WriteLong(tags, 0x8000_0043); // SA_Colors32
		bus.WriteLong(tags + 4, colors32);
		bus.WriteLong(tags + 8, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		var viewState = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -294),
			viewState));
		var view = viewState.D[0];
		var copperList = bus.ReadLong(bus.ReadLong(view + ViewLofCprListOffset) + CprListStartOffset);
		Assert.Equal((ushort)0xF1A, ReadCopperMoveValue(bus, copperList, 0x0184));
	}

	[Fact]
	public void OpenScreenPublishesLegacyAndTaggedDefaultScreenTitles()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var title = InvokeAllocMem(bus, 16, 0);
		WriteCString(bus, title, "Legacy");
		var newScreen = InvokeAllocMem(bus, 0x20, 0);
		bus.WriteLong(newScreen + NewScreenDefaultTitleOffset, title);
		var legacyOpen = new M68kCpuState { A = { [0] = newScreen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			legacyOpen));
		Assert.Equal(title, bus.ReadLong(legacyOpen.D[0] + ScreenDefaultTitleOffset));
		var close = new M68kCpuState { A = { [0] = legacyOpen.D[0] } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));

		var taggedTitle = InvokeAllocMem(bus, 16, 0);
		WriteCString(bus, taggedTitle, "Tagged");
		var tags = InvokeAllocMem(bus, 2 * 8, 0);
		bus.WriteLong(tags, 0x8000_0028); // SA_Title
		bus.WriteLong(tags + 4, taggedTitle);
		bus.WriteLong(tags + 8, 0);
		var taggedOpen = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			taggedOpen));
		Assert.Equal(taggedTitle, bus.ReadLong(taggedOpen.D[0] + ScreenDefaultTitleOffset));
		Assert.Equal(0u, bus.ReadLong(taggedOpen.D[0] + ScreenTitleOffset));
		Assert.NotEqual(0, bus.ReadWord(taggedOpen.D[0] + ScreenFlagsOffset) & 0x0010);
		var activeTitle = InvokeAllocMem(bus, 16, 0);
		WriteCString(bus, activeTitle, "Active");
		var setTitles = new M68kCpuState { A = { [2] = activeTitle } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -276),
			setTitles));
		Assert.Equal(activeTitle, bus.ReadLong(taggedOpen.D[0] + ScreenTitleOffset));

		var hideTitle = new M68kCpuState { A = { [0] = taggedOpen.D[0] } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -282),
			hideTitle));
		Assert.Equal(0u, hideTitle.D[0]);
		Assert.Equal(0, bus.ReadWord(taggedOpen.D[0] + ScreenFlagsOffset) & 0x0010);

		var showTitle = new M68kCpuState { A = { [0] = taggedOpen.D[0] }, D = { [0] = 1 } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -282),
			showTitle));
		Assert.NotEqual(0, bus.ReadWord(taggedOpen.D[0] + ScreenFlagsOffset) & 0x0010);
	}

	[Fact]
	public void OpenScreenPublishesCanonicalPrefixAndDefaultScreenFont()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));

		var screen = open.D[0];
		Assert.NotEqual(0u, screen);
		Assert.Equal(0u, bus.ReadLong(screen + ScreenTitleOffset));
		Assert.Equal(0u, bus.ReadLong(screen + ScreenDefaultTitleOffset));

		var screenFont = bus.ReadLong(screen + ScreenFontOffset);
		Assert.NotEqual(0u, screenFont);
		Assert.True(bus.IsMappedMemoryRange(screenFont, 8));
		Assert.Equal((ushort)8, bus.ReadWord(screenFont + 4));
		Assert.Equal((byte)7, bus.ReadByte(screenFont + 6));
		Assert.Equal((byte)8, bus.ReadByte(screenFont + 7));

		var screenRastPortFont = bus.ReadLong(
			screen + ScreenRastPortOffset + GraphicsLayouts.RastPortFont);
		Assert.NotEqual(0u, screenRastPortFont);
		Assert.True(bus.IsMappedMemoryRange(
			screenRastPortFont,
			GraphicsLayouts.TextFontMinimumSize));
		Assert.Equal(
			bus.ReadWord(screenFont + GraphicsLayouts.TextAttrYSize),
			bus.ReadWord(screenRastPortFont + GraphicsLayouts.TextFontYSize));
		Assert.Equal(
			bus.ReadByte(screenFont + GraphicsLayouts.TextAttrStyle),
			bus.ReadByte(screenRastPortFont + GraphicsLayouts.TextFontStyle));
		Assert.Equal(
			bus.ReadByte(screenFont + GraphicsLayouts.TextAttrFlags),
			bus.ReadByte(screenRastPortFont + GraphicsLayouts.TextFontFlags));
		var window = bus.ReadLong(screen + GraphicsLayouts.ScreenFirstWindow);
		Assert.NotEqual(0u, window);
		Assert.Equal(
			screenRastPortFont,
			bus.ReadLong(bus.ReadLong(window + WindowRPortOffset) + GraphicsLayouts.RastPortFont));

		// The public nested structures retain their canonical 68k offsets while
		// the corrected title/font words occupy the Screen prefix above them.
		Assert.Equal(
			screen + ScreenBitMapOffset,
			bus.ReadLong(screen + ScreenRastPortOffset + RastPortBitMapOffset));
		var viewPort = screen + ScreenViewPortOffset;
		Assert.True(bus.IsMappedMemoryRange(viewPort, GraphicsLayouts.ViewPortSize));
		Assert.Equal(0u, bus.ReadLong(viewPort + GraphicsLayouts.ViewPortNext));
		Assert.Equal(0u, bus.ReadLong(viewPort + GraphicsLayouts.ViewPortSprIns));
		Assert.Equal(0u, bus.ReadLong(viewPort + GraphicsLayouts.ViewPortClrIns));
		Assert.Equal(0u, bus.ReadLong(viewPort + GraphicsLayouts.ViewPortUCopIns));
		Assert.Equal((byte)0x24, bus.ReadByte(viewPort + GraphicsLayouts.ViewPortSpritePriorities));

		var viewState = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -294),
			viewState));
		var activeView = viewState.D[0];
		Assert.NotEqual(0u, activeView);
		Assert.Equal(viewPort, bus.ReadLong(activeView + GraphicsLayouts.ViewViewPort));
		Assert.Equal(
			unchecked((ushort)GraphicsLayouts.ViewDefaultDyOffset),
			bus.ReadWord(activeView + GraphicsLayouts.ViewDyOffset));
		Assert.Equal(
			unchecked((ushort)GraphicsLayouts.ViewDefaultDxOffset),
			bus.ReadWord(activeView + GraphicsLayouts.ViewDxOffset));
		Assert.Equal(
			activeView,
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + GraphicsLayouts.GfxBaseActiView));
	}

	[Fact]
	public void OpenScreenCarriesLegacyAndTaggedScreenFontsWithoutReplacingCallerOwnership()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var legacyFont = InvokeAllocMem(bus, 8, 0);
		bus.WriteLong(legacyFont, 0);
		bus.WriteWord(legacyFont + 4, 9);
		bus.WriteByte(legacyFont + 6, 3, 0);
		bus.WriteByte(legacyFont + 7, 0xA5, 0);
		var newScreen = InvokeAllocMem(bus, 0x20, 0);
		bus.WriteLong(newScreen + NewScreenFontOffset, legacyFont);

		var legacyOpen = new M68kCpuState { A = { [0] = newScreen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			legacyOpen));
		Assert.Equal(legacyFont, bus.ReadLong(legacyOpen.D[0] + ScreenFontOffset));

		var close = new M68kCpuState { A = { [0] = legacyOpen.D[0] } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));

		var taggedFont = InvokeAllocMem(bus, 8, 0);
		bus.WriteLong(taggedFont, 0);
		bus.WriteWord(taggedFont + 4, 11);
		bus.WriteByte(taggedFont + 6, 5, 0);
		bus.WriteByte(taggedFont + 7, 0x5A, 0);
		var tags = InvokeAllocMem(bus, 3 * 8, 0);
		bus.WriteLong(tags, ScreenTagFont);
		bus.WriteLong(tags + 4, taggedFont);
		bus.WriteLong(tags + 8, 0);

		var taggedOpen = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			taggedOpen));
		Assert.Equal(taggedFont, bus.ReadLong(taggedOpen.D[0] + ScreenFontOffset));
		Assert.Equal((ushort)11, bus.ReadWord(taggedFont + 4));
		Assert.Equal((byte)5, bus.ReadByte(taggedFont + 6));
		Assert.Equal((byte)0x5A, bus.ReadByte(taggedFont + 7));
	}

	[Fact]
	public void OpenScreenPublishesOpenedFontInEmbeddedRastPortAndClosesItsAccessor()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var firstOpen = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			firstOpen));

		var firstScreen = firstOpen.D[0];
		var callerFont = bus.ReadLong(firstScreen + ScreenFontOffset);
		var openedFont = bus.ReadLong(
			firstScreen + ScreenRastPortOffset + GraphicsLayouts.RastPortFont);
		Assert.NotEqual(0u, openedFont);
		Assert.Equal(
			(ushort)1,
			bus.ReadWord(openedFont + GraphicsLayouts.TextFontAccessors));

		var closeFirst = new M68kCpuState { A = { [0] = firstScreen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			closeFirst));
		Assert.Equal(
			(ushort)0,
			bus.ReadWord(openedFont + GraphicsLayouts.TextFontAccessors));

		var newScreen = InvokeAllocMem(bus, 0x20, 0);
		bus.WriteLong(newScreen + NewScreenFontOffset, callerFont);
		var secondOpen = new M68kCpuState { A = { [0] = newScreen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			secondOpen));

		var secondScreen = secondOpen.D[0];
		Assert.Equal(callerFont, bus.ReadLong(secondScreen + ScreenFontOffset));
		var secondOpenedFont = bus.ReadLong(
			secondScreen + ScreenRastPortOffset + GraphicsLayouts.RastPortFont);
		Assert.Equal(openedFont, secondOpenedFont);
		Assert.Equal(
			bus.ReadWord(secondOpenedFont + GraphicsLayouts.TextFontYSize),
			bus.ReadWord(secondScreen + ScreenRastPortOffset + GraphicsLayouts.RastPortTextHeight));
		Assert.Equal(
			bus.ReadWord(secondOpenedFont + GraphicsLayouts.TextFontXSize),
			bus.ReadWord(secondScreen + ScreenRastPortOffset + GraphicsLayouts.RastPortTextWidth));
		Assert.Equal(
			bus.ReadWord(secondOpenedFont + GraphicsLayouts.TextFontBaseline),
			bus.ReadWord(secondScreen + ScreenRastPortOffset + GraphicsLayouts.RastPortTextBaseline));
		Assert.Equal(
			(ushort)1,
			bus.ReadWord(secondOpenedFont + GraphicsLayouts.TextFontAccessors));

		var closeSecond = new M68kCpuState { A = { [0] = secondScreen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			closeSecond));
		Assert.Equal(
			(ushort)0,
			bus.ReadWord(secondOpenedFont + GraphicsLayouts.TextFontAccessors));
	}

	[Fact]
	public void OpenScreenUsesResidentSelectedFontForScreenAndWindowRastPorts()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var initialOpen = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			initialOpen));

		var initialScreen = initialOpen.D[0];
		var sourceFont = bus.ReadLong(
			initialScreen + ScreenRastPortOffset + GraphicsLayouts.RastPortFont);
		var sourceStrike = bus.ReadLong(sourceFont + GraphicsLayouts.TextFontCharData);
		var sourceModulo = bus.ReadWord(sourceFont + GraphicsLayouts.TextFontModulo);
		var sourceHeight = bus.ReadWord(sourceFont + GraphicsLayouts.TextFontYSize);
		Assert.True(sourceStrike >= sourceFont);
		var copiedFontBytes = checked((int)(sourceStrike - sourceFont) + sourceModulo * sourceHeight);
		var customFont = InvokeAllocMem(bus, (uint)copiedFontBytes, 0);
		for (var offset = 0; offset < copiedFontBytes; offset++)
		{
			bus.WriteByte(
				customFont + (uint)offset,
				bus.ReadByte(sourceFont + (uint)offset),
				0);
		}

		var customName = InvokeAllocMem(bus, 16, 0);
		WriteCString(bus, customName, "custom.font");
		bus.WriteLong(customFont + GraphicsLayouts.TextFontName, customName);
		bus.WriteLong(customFont, 0);
		bus.WriteLong(customFont + 4, 0);
		bus.WriteWord(customFont + GraphicsLayouts.TextFontAccessors, 0);
		bus.WriteByte(customFont + GraphicsLayouts.TextFontStyle, 0, 0);
		bus.WriteLong(
			customFont + GraphicsLayouts.TextFontCharData,
			customFont + (sourceStrike - sourceFont));

		var closeInitial = new M68kCpuState { A = { [0] = initialScreen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			closeInitial));

		var addFont = new M68kCpuState { A = { [1] = customFont }, D = { [0] = 0xDEAD_BEEFu } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, (int)GraphicsLvo.AddFont),
			addFont));
		Assert.Equal(0u, addFont.D[0]);

		var textAttr = InvokeAllocMem(bus, GraphicsLayouts.TextAttrSize, 0);
		bus.WriteLong(textAttr + GraphicsLayouts.TextAttrName, customName);
		bus.WriteWord(textAttr + GraphicsLayouts.TextAttrYSize, sourceHeight);
		bus.WriteByte(textAttr + GraphicsLayouts.TextAttrStyle, 0, 0);
		bus.WriteByte(
			textAttr + GraphicsLayouts.TextAttrFlags,
			bus.ReadByte(sourceFont + GraphicsLayouts.TextFontFlags),
			0);
		var newScreen = InvokeAllocMem(bus, 0x20, 0);
		bus.WriteLong(newScreen + NewScreenFontOffset, textAttr);

		var open = new M68kCpuState { A = { [0] = newScreen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));

		var screen = open.D[0];
		var screenFont = bus.ReadLong(screen + ScreenRastPortOffset + GraphicsLayouts.RastPortFont);
		Assert.Equal(customFont, screenFont);
		Assert.Equal(
			(ushort)1,
			bus.ReadWord(customFont + GraphicsLayouts.TextFontAccessors));
		var window = bus.ReadLong(screen + GraphicsLayouts.ScreenFirstWindow);
		var windowRastPort = bus.ReadLong(window + WindowRPortOffset);
		Assert.Equal(customFont, bus.ReadLong(windowRastPort + GraphicsLayouts.RastPortFont));
		Assert.Equal(
			bus.ReadWord(customFont + GraphicsLayouts.TextFontYSize),
			bus.ReadWord(windowRastPort + GraphicsLayouts.RastPortTextHeight));

		var close = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(
			(ushort)0,
			bus.ReadWord(customFont + GraphicsLayouts.TextFontAccessors));
	}

	[Fact]
	public void OpenScreenLeavesMalformedScreenFontToTheNativeProvider()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var newScreen = InvokeAllocMem(bus, 0x20, 0);
		bus.WriteLong(newScreen + NewScreenFontOffset, 0x00F0_0000);

		var open = new M68kCpuState { A = { [0] = newScreen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));
		Assert.Equal(0u, open.D[0]);
	}

	[Fact]
	public void OpenScreenDeclinesAnOddNewScreenPointerBeforeDisplayPublication()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var aligned = InvokeAllocMem(bus, 0x20, 0);
		var oddNewScreen = aligned + 1;
		bus.WriteWord(oddNewScreen + NewScreenWidthOffset, 320);
		bus.WriteWord(oddNewScreen + NewScreenHeightOffset, 200);
		bus.WriteByte(oddNewScreen + NewScreenDepthOffset, 2, 0);
		var open = new M68kCpuState
		{
			A = { [0] = oddNewScreen },
			D = { [0] = 0xDEAD_BEEFu },
			Cycles = 1234
		};

		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));
		Assert.Equal(0u, open.D[0]);
		Assert.Equal(1234, open.Cycles);
		Assert.Equal(0u, bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));

		// The null NewScreen compatibility path remains independent of the
		// declined odd guest structure.
		var retry = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			retry));
		Assert.NotEqual(0u, retry.D[0]);
		var closeScreen = new M68kCpuState { A = { [0] = retry.D[0] } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			closeScreen));
		Assert.Equal(1u, closeScreen.D[0]);
	}

	[Fact]
	public void OpenScreenDeclinesAnUnreadableLegacyNewScreenBeforeDefaulting()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint unreadableNewScreen = 0xFFFF_F000;
		var rejected = new M68kCpuState
		{
			A = { [0] = unreadableNewScreen },
			D = { [0] = 0xDEAD_BEEFu },
			Cycles = 2345
		};

		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			rejected));
		Assert.Equal(0u, rejected.D[0]);
		Assert.Equal(2345, rejected.Cycles);
		Assert.Equal(
			0u,
			bus.ReadLong(
				AmigaKickstartHost.GraphicsLibraryBase +
				(uint)GraphicsLayouts.GfxBaseActiView));

		// A failed caller-owned legacy envelope must not become a staged default
		// request.  The independent null request remains available and uses the
		// active profile's default geometry.
		var retry = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			retry));
		Assert.NotEqual(0u, retry.D[0]);
		Assert.Equal(
			AmigaConstants.PalLowResWidth,
			bus.ReadWord(retry.D[0] + ScreenWidthOffset));

		var close = new M68kCpuState { A = { [0] = retry.D[0] } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
	}

	[Fact]
	public void OpenScreenDeclinesAnUnreadableLegacyDefaultTitleBeforeDefaulting()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var newScreen = InvokeAllocMem(bus, 0x20, 0);
		bus.WriteLong(newScreen + NewScreenDefaultTitleOffset, 0xFFFF_F000);

		var rejected = new M68kCpuState { A = { [0] = newScreen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			rejected));
		Assert.Equal(0u, rejected.D[0]);

		// A malformed caller-owned title must not leave a staged Screen that
		// changes the independent null-request defaults.
		var retry = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			retry));
		Assert.NotEqual(0u, retry.D[0]);
		Assert.Equal(
			AmigaConstants.PalLowResWidth,
			bus.ReadWord(retry.D[0] + ScreenWidthOffset));

		var close = new M68kCpuState { A = { [0] = retry.D[0] } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
	}

	[Fact]
	public void OpenScreenTagListAllowsSaTitleToOverrideMalformedLegacyDefaultTitle()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var newScreen = InvokeAllocMem(bus, 0x20, 0);
		bus.WriteLong(newScreen + NewScreenDefaultTitleOffset, 0xFFFF_F000);
		var title = InvokeAllocMem(bus, 24, 0);
		WriteCString(bus, title, "Tagged title");

		var tags = InvokeAllocMem(bus, 2 * 8, 0);
		bus.WriteLong(tags, ScreenTagTitle);
		bus.WriteLong(tags + 4, title);
		bus.WriteLong(tags + 8, 0);

		var open = new M68kCpuState { A = { [0] = newScreen, [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));
		Assert.NotEqual(0u, open.D[0]);
		Assert.Equal(title, bus.ReadLong(open.D[0] + ScreenDefaultTitleOffset));

		var close = new M68kCpuState { A = { [0] = open.D[0] } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
	}

	[Fact]
	public void OpenScreenTagListDeclinesAnUnreadableSaTitleBeforePublishing()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var errorCode = InvokeAllocMem(bus, 4, 0);
		bus.WriteLong(errorCode, 0xA5A5_A5A5);
		var tags = InvokeAllocMem(bus, 3 * 8, 0);
		bus.WriteLong(tags, ScreenTagTitle);
		bus.WriteLong(tags + 4, 0xFFFF_F000);
		bus.WriteLong(tags + 8, ScreenTagErrorCode);
		bus.WriteLong(tags + 12, errorCode);
		bus.WriteLong(tags + 16, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));
		Assert.Equal(0u, open.D[0]);
		Assert.Equal(0xA5A5_A5A5u, bus.ReadLong(errorCode));
	}

	[Theory]
	[InlineData(ScreenTagColorMapEntries)]
	[InlineData(ScreenTagParent)]
	[InlineData(ScreenTagDraggable)]
	[InlineData(ScreenTagExclusive)]
	[InlineData(ScreenTagBackFill)]
	[InlineData(ScreenTagFrontChild)]
	[InlineData(ScreenTagBackChild)]
	[InlineData(ScreenTagLikeWorkbench)]
	[InlineData(ScreenTagReserved)]
	[InlineData(ScreenTagMinimizeIsg)]
	public void OpenScreenTagListDeclinesRecognizedUnownedAttributesBeforePublishing(
		uint unsupportedTag)
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var errorCode = InvokeAllocMem(bus, 4, 0);
		bus.WriteLong(errorCode, 0xA5A5_A5A5);
		var tags = InvokeAllocMem(bus, 3 * 8, 0);
		bus.WriteLong(tags, unsupportedTag);
		bus.WriteLong(tags + 4, 0x0000_1234);
		bus.WriteLong(tags + 8, ScreenTagErrorCode);
		bus.WriteLong(tags + 12, errorCode);
		bus.WriteLong(tags + 16, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));
		Assert.Equal(0u, open.D[0]);
		Assert.Equal(0xA5A5_A5A5u, bus.ReadLong(errorCode));

		// The provider-safe decline must not poison the next independent
		// synthetic request.
		var retry = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			retry));
		Assert.NotEqual(0u, retry.D[0]);
		var close = new M68kCpuState { A = { [0] = retry.D[0] } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
	}

	[Fact]
	public void OpenScreenTagListHonorsSaFullPaletteWithDepthDerivedColorMap()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var tags = InvokeAllocMem(bus, 3 * 8, 0);
		bus.WriteLong(tags, ScreenTagFullPalette);
		bus.WriteLong(tags + 4, 1);
		bus.WriteLong(tags + 8, ScreenTagDepth);
		bus.WriteLong(tags + 12, 2);
		bus.WriteLong(tags + 16, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		var screen = open.D[0];
		Assert.NotEqual(0u, screen);
		var viewPort = screen + ScreenViewPortOffset;
		var colorMap = bus.ReadLong(viewPort + (uint)GraphicsLayouts.ViewPortColorMap);
		Assert.NotEqual(0u, colorMap);
		Assert.Equal((ushort)32, bus.ReadWord(colorMap + (uint)GraphicsLayouts.ColorMapCount));

		var close = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
	}

	[Fact]
	public void A1200CompatibilityCopperProjectsRgb32PaletteAsAgaHighAndLowNibbles()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A1200AgaPal)
			.WithKickstart(KickstartConfiguration.FromRomImage(
				KickstartVersion.Kickstart30,
				new byte[512 * 1024]))
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		var colors32 = InvokeAllocMem(bus, 20, 0);
		bus.WriteLong(colors32, (1u << 16) | 1u);
		bus.WriteLong(colors32 + 4, 0xABCD_0000);
		bus.WriteLong(colors32 + 8, 0x1234_0000);
		bus.WriteLong(colors32 + 12, 0xFEDC_0000);
		bus.WriteLong(colors32 + 16, 0);
		var tags = InvokeAllocMem(bus, 3 * 8, 0);
		bus.WriteLong(tags, ScreenTagColors32);
		bus.WriteLong(tags + 4, colors32);
		bus.WriteLong(tags + 8, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));
		var screen = open.D[0];
		Assert.NotEqual(0u, screen);
		var copper = bus.ReadLong(screen + ScreenViewPortOffset + ViewPortDspInsOffset);
		Assert.NotEqual(0u, copper);

		var bplcon3 = ushort.MaxValue;
		var sawHigh = false;
		var sawLow = false;
		var restored = false;
		for (var offset = 0u; offset < 0x400; offset += 4)
		{
			var register = bus.ReadWord(copper + offset);
			var value = bus.ReadWord(copper + offset + 2);
			if (register == 0xFFFF && value == 0xFFFE)
				break;
			if (register == 0x0106)
			{
				bplcon3 = value;
				if (sawLow && value == 0x0C00)
					restored = true;
				continue;
			}

			if (register == 0x0182 && bplcon3 == 0x0C00 && value == 0x0A1F)
				sawHigh = true;
			if (register == 0x0182 && bplcon3 == 0x0E00 && value == 0x0B2E)
				sawLow = true;
		}

		Assert.True(sawHigh);
		Assert.True(sawLow);
		Assert.True(restored);
	}

	[Fact]
	public void A1200EightPlaneScreenPublishesBpu3AndTheSecondAgaPaletteBank()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A1200AgaPal)
			.WithKickstart(KickstartConfiguration.FromRomImage(
				KickstartVersion.Kickstart30,
				new byte[512 * 1024]))
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		var colors32 = InvokeAllocMem(bus, 20, 0);
		// COLOR32 is COLOR00 in BPLCON3's second bank.
		bus.WriteLong(colors32, (1u << 16) | 32u);
		bus.WriteLong(colors32 + 4, 0xABCD_0000);
		bus.WriteLong(colors32 + 8, 0x1234_0000);
		bus.WriteLong(colors32 + 12, 0xFEDC_0000);
		bus.WriteLong(colors32 + 16, 0);
		var tags = InvokeAllocMem(bus, 4 * 8, 0);
		bus.WriteLong(tags, ScreenTagDepth);
		bus.WriteLong(tags + 4, 8);
		bus.WriteLong(tags + 8, ScreenTagFullPalette);
		bus.WriteLong(tags + 12, 1);
		bus.WriteLong(tags + 16, ScreenTagColors32);
		bus.WriteLong(tags + 20, colors32);
		bus.WriteLong(tags + 24, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));
		var screen = open.D[0];
		Assert.NotEqual(0u, screen);
		Assert.Equal((byte)8, bus.ReadByte(screen + ScreenBitMapOffset + BitMapDepthOffset));
		var viewPort = screen + ScreenViewPortOffset;
		var colorMap = bus.ReadLong(viewPort + (uint)GraphicsLayouts.ViewPortColorMap);
		Assert.Equal((ushort)256, bus.ReadWord(colorMap + (uint)GraphicsLayouts.ColorMapCount));
		var copper = bus.ReadLong(viewPort + ViewPortDspInsOffset);
		Assert.Equal((ushort)0x0010, ReadCopperMoveValue(bus, copper, 0x0100));

		var bplcon3 = ushort.MaxValue;
		var sawBank1High = false;
		var sawBank1Low = false;
		var restored = false;
		for (var offset = 0u; offset < 0x1000; offset += 4)
		{
			var register = bus.ReadWord(copper + offset);
			var value = bus.ReadWord(copper + offset + 2);
			if (register == 0xFFFF && value == 0xFFFE)
				break;
			if (register == 0x0106)
			{
				bplcon3 = value;
				if (sawBank1Low && value == 0x0C00)
					restored = true;
				continue;
			}

			if (register == 0x0180 && bplcon3 == 0x2C00 && value == 0x0A1F)
				sawBank1High = true;
			if (register == 0x0180 && bplcon3 == 0x2E00 && value == 0x0B2E)
				sawBank1Low = true;
		}

		Assert.True(sawBank1High);
		Assert.True(sawBank1Low);
		Assert.True(restored);
	}

	[Fact]
	public void A1200EightPlaneScreenPublishesColor255InTheFinalAgaPaletteBank()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A1200AgaPal)
			.WithKickstart(KickstartConfiguration.FromRomImage(
				KickstartVersion.Kickstart30,
				new byte[512 * 1024]))
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		var colors32 = InvokeAllocMem(bus, 20, 0);
		// COLOR255 is COLOR31 in BPLCON3's final (seventh) bank.
		bus.WriteLong(colors32, (1u << 16) | 255u);
		bus.WriteLong(colors32 + 4, 0xABCD_0000);
		bus.WriteLong(colors32 + 8, 0x1234_0000);
		bus.WriteLong(colors32 + 12, 0xFEDC_0000);
		bus.WriteLong(colors32 + 16, 0);
		var tags = InvokeAllocMem(bus, 3 * 8, 0);
		bus.WriteLong(tags, ScreenTagDepth);
		bus.WriteLong(tags + 4, 8);
		bus.WriteLong(tags + 8, ScreenTagColors32);
		bus.WriteLong(tags + 12, colors32);
		bus.WriteLong(tags + 16, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));
		var screen = open.D[0];
		Assert.NotEqual(0u, screen);
		var viewPort = screen + ScreenViewPortOffset;
		var copper = bus.ReadLong(viewPort + ViewPortDspInsOffset);

		var bplcon3 = ushort.MaxValue;
		var sawBank7High = false;
		var sawBank7Low = false;
		var restored = false;
		for (var offset = 0u; offset < 0x1000; offset += 4)
		{
			var register = bus.ReadWord(copper + offset);
			var value = bus.ReadWord(copper + offset + 2);
			if (register == 0xFFFF && value == 0xFFFE)
				break;
			if (register == 0x0106)
			{
				bplcon3 = value;
				if (sawBank7Low && value == 0x0C00)
					restored = true;
				continue;
			}

			if (register == 0x01BE && bplcon3 == 0xEC00 && value == 0x0A1F)
				sawBank7High = true;
			if (register == 0x01BE && bplcon3 == 0xEE00 && value == 0x0B2E)
				sawBank7Low = true;
		}

		Assert.True(sawBank7High);
		Assert.True(sawBank7Low);
		Assert.True(restored);
	}

	[Fact]
	public void A1200SetRgb32RebuildsTheFinalAgaPaletteBankForColor255()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A1200AgaPal)
			.WithKickstart(KickstartConfiguration.FromRomImage(
				KickstartVersion.Kickstart30,
				new byte[512 * 1024]))
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		var tags = InvokeAllocMem(bus, 2 * 8, 0);
		bus.WriteLong(tags, ScreenTagDepth);
		bus.WriteLong(tags + 4, 8);
		bus.WriteLong(tags + 8, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));
		var screen = open.D[0];
		Assert.NotEqual(0u, screen);
		var viewPort = screen + ScreenViewPortOffset;
		var set = new M68kCpuState
		{
			A = { [0] = viewPort },
			D = { [0] = 255, [1] = 0x5A00_0000u, [2] = 0xC300_0000u, [3] = 0x1E00_0000u }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, (int)GraphicsLvo.SetRGB32),
			set));
		Assert.Equal(0u, set.D[0]);
		var copper = bus.ReadLong(viewPort + ViewPortDspInsOffset);

		var bplcon3 = ushort.MaxValue;
		var sawBank7High = false;
		var sawBank7Low = false;
		var restored = false;
		for (var offset = 0u; offset < 0x1000; offset += 4)
		{
			var register = bus.ReadWord(copper + offset);
			var value = bus.ReadWord(copper + offset + 2);
			if (register == 0xFFFF && value == 0xFFFE)
				break;
			if (register == 0x0106)
			{
				bplcon3 = value;
				if (sawBank7Low && value == 0x0C00)
					restored = true;
				continue;
			}

			if (register == 0x01BE && bplcon3 == 0xEC00 && value == 0x05C1)
				sawBank7High = true;
			if (register == 0x01BE && bplcon3 == 0xEE00 && value == 0x0A3E)
				sawBank7Low = true;
		}

		Assert.True(sawBank7High);
		Assert.True(sawBank7Low);
		Assert.True(restored);
	}

	[Fact]
    public void A1200OpenScreenAcceptsCompleteColors32RecordPalette()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A1200AgaPal)
			.WithKickstart(KickstartConfiguration.FromRomImage(
				KickstartVersion.Kickstart30,
				new byte[512 * 1024]))
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
        const int recordCount = 256;
		var colors32 = InvokeAllocMem(bus, (recordCount * 16) + 4, 0);
		for (var record = 0; record < recordCount; record++)
		{
			var offset = colors32 + (uint)(record * 16);
			var color = record == recordCount - 1 ? 255u : (uint)record;
			bus.WriteLong(offset, (1u << 16) | color);
			bus.WriteLong(offset + 4, record == recordCount - 1 ? 0xABCD_0000u : 0u);
			bus.WriteLong(offset + 8, record == recordCount - 1 ? 0x1234_0000u : 0u);
			bus.WriteLong(offset + 12, record == recordCount - 1 ? 0xFEDC_0000u : 0u);
		}
		bus.WriteLong(colors32 + (uint)(recordCount * 16), 0);
		var tags = InvokeAllocMem(bus, 3 * 8, 0);
		bus.WriteLong(tags, ScreenTagDepth);
		bus.WriteLong(tags + 4, 8);
		bus.WriteLong(tags + 8, ScreenTagColors32);
		bus.WriteLong(tags + 12, colors32);
		bus.WriteLong(tags + 16, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));
		var screen = open.D[0];
		Assert.NotEqual(0u, screen);
		var copper = bus.ReadLong(screen + ScreenViewPortOffset + ViewPortDspInsOffset);

		var bplcon3 = ushort.MaxValue;
		var sawBank7High = false;
		var sawBank7Low = false;
		for (var offset = 0u; offset < 0x1000; offset += 4)
		{
			var register = bus.ReadWord(copper + offset);
			var value = bus.ReadWord(copper + offset + 2);
			if (register == 0xFFFF && value == 0xFFFE)
				break;
			if (register == 0x0106)
			{
				bplcon3 = value;
				continue;
			}

			if (register == 0x01BE && bplcon3 == 0xEC00 && value == 0x0A1F)
				sawBank7High = true;
			if (register == 0x01BE && bplcon3 == 0xEE00 && value == 0x0B2E)
				sawBank7Low = true;
		}

		Assert.True(sawBank7High);
		Assert.True(sawBank7Low);
	}

	[Fact]
	public void A1200OpenScreenAcceptsCompleteColorsRecordPalette()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A1200AgaPal)
			.WithKickstart(KickstartConfiguration.FromRomImage(
				KickstartVersion.Kickstart30,
				new byte[512 * 1024]))
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		const int recordCount = 256;
		var colors = InvokeAllocMem(bus, (recordCount * 8) + 8, 0);
		for (var record = 0; record < recordCount; record++)
		{
			var offset = colors + (uint)(record * 8);
			var color = record == recordCount - 1 ? 255 : record;
			bus.WriteWord(offset, (ushort)color);
			bus.WriteWord(offset + 2, record == recordCount - 1 ? (ushort)0x000A : (ushort)0);
			bus.WriteWord(offset + 4, record == recordCount - 1 ? (ushort)0x0001 : (ushort)0);
			bus.WriteWord(offset + 6, record == recordCount - 1 ? (ushort)0x000F : (ushort)0);
		}
		bus.WriteWord(colors + (uint)(recordCount * 8), ushort.MaxValue);
		var tags = InvokeAllocMem(bus, 3 * 8, 0);
		bus.WriteLong(tags, ScreenTagDepth);
		bus.WriteLong(tags + 4, 8);
		bus.WriteLong(tags + 8, ScreenTagColors);
		bus.WriteLong(tags + 12, colors);
		bus.WriteLong(tags + 16, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));
		var screen = open.D[0];
		Assert.NotEqual(0u, screen);
		var copper = bus.ReadLong(screen + ScreenViewPortOffset + ViewPortDspInsOffset);

		var bplcon3 = ushort.MaxValue;
		var sawBank7High = false;
		var sawBank7Low = false;
		for (var offset = 0u; offset < 0x1000; offset += 4)
		{
			var register = bus.ReadWord(copper + offset);
			var value = bus.ReadWord(copper + offset + 2);
			if (register == 0xFFFF && value == 0xFFFE)
				break;
			if (register == 0x0106)
			{
				bplcon3 = value;
				continue;
			}

			if (register == 0x01BE && bplcon3 == 0xEC00 && value == 0x0A1F)
				sawBank7High = true;
			if (register == 0x01BE && bplcon3 == 0xEE00 && value == 0x0A1F)
				sawBank7Low = true;
		}

		Assert.True(sawBank7High);
		Assert.True(sawBank7Low);
	}

	[Theory]
	[InlineData(ScreenTagVideoControl)]
	[InlineData(ScreenTagFrontChild)]
	[InlineData(ScreenTagBackChild)]
	[InlineData(ScreenTagLikeWorkbench)]
	public void OpenScreenTagListDeclinesNullOverlappingPointerAttributesBeforePublishing(
		uint unsupportedTag)
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var errorCode = InvokeAllocMem(bus, 4, 0);
		bus.WriteLong(errorCode, 0xA5A5_A5A5);
		var tags = InvokeAllocMem(bus, 3 * 8, 0);
		bus.WriteLong(tags, unsupportedTag);
		bus.WriteLong(tags + 4, 0);
		bus.WriteLong(tags + 8, ScreenTagErrorCode);
		bus.WriteLong(tags + 12, errorCode);
		bus.WriteLong(tags + 16, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));
		Assert.Equal(0u, open.D[0]);
		Assert.Equal(0xA5A5_A5A5u, bus.ReadLong(errorCode));
	}

	[Fact]
	public void OpenScreenTagListUsesCanonicalKickstartPenAndBooleanTags()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var pens = InvokeAllocMem(bus, 6, 0);
		bus.WriteWord(pens, 5);
		bus.WriteWord(pens + 2, 6);
		bus.WriteWord(pens + 4, ushort.MaxValue);

		var tags = InvokeAllocMem(bus, 4 * 8, 0);
		bus.WriteLong(tags, CanonicalScreenTagPens);
		bus.WriteLong(tags + 4, pens);
		bus.WriteLong(tags + 8, CanonicalScreenTagShowTitle);
		bus.WriteLong(tags + 12, 0);
		bus.WriteLong(tags + 16, 0x8000_0038); // canonical SA_Quiet
		bus.WriteLong(tags + 20, 1);
		bus.WriteLong(tags + 24, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));
		Assert.NotEqual(0u, open.D[0]);
		Assert.Equal((byte)5, bus.ReadByte(open.D[0] + ScreenDetailPenOffset));
		Assert.Equal((byte)6, bus.ReadByte(open.D[0] + ScreenBlockPenOffset));
		Assert.Equal((ushort)0, (ushort)(bus.ReadWord(open.D[0] + ScreenFlagsOffset) & 0x0010));
		Assert.NotEqual((ushort)0, (ushort)(bus.ReadWord(open.D[0] + ScreenFlagsOffset) & 0x0100));

		var close = new M68kCpuState { A = { [0] = open.D[0] } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
	}

	[Fact]
	public void OpenScreenTagListDeclinesPublicScreenTypeBeforePublishing()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var errorCode = InvokeAllocMem(bus, 4, 0);
		bus.WriteLong(errorCode, 0xA5A5_A5A5);
		var tags = InvokeAllocMem(bus, 3 * 8, 0);
		bus.WriteLong(tags, 0x8000_002D); // SA_Type
		bus.WriteLong(tags + 4, 2); // PUBLICSCREEN
		bus.WriteLong(tags + 8, ScreenTagErrorCode);
		bus.WriteLong(tags + 12, errorCode);
		bus.WriteLong(tags + 16, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));
		Assert.Equal(0u, open.D[0]);
		Assert.Equal(0xA5A5_A5A5u, bus.ReadLong(errorCode));
	}

	[Fact]
	public void OpenScreenTagListAllowsSaFontToOverrideMalformedLegacyFont()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var newScreen = InvokeAllocMem(bus, 0x20, 0);
		bus.WriteLong(newScreen + NewScreenFontOffset, 0x00F0_0000);

		var taggedFont = InvokeAllocMem(bus, 8, 0);
		bus.WriteLong(taggedFont, 0);
		bus.WriteWord(taggedFont + 4, 12);
		bus.WriteByte(taggedFont + 6, 6, 0);
		bus.WriteByte(taggedFont + 7, 0x3C, 0);
		var tags = InvokeAllocMem(bus, 2 * 8, 0);
		bus.WriteLong(tags, ScreenTagFont);
		bus.WriteLong(tags + 4, taggedFont);
		bus.WriteLong(tags + 8, 0);

		var open = new M68kCpuState { A = { [0] = newScreen, [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));
		Assert.NotEqual(0u, open.D[0]);
		Assert.Equal(taggedFont, bus.ReadLong(open.D[0] + ScreenFontOffset));
	}

	[Fact]
	public void OpenScreenTagListSysFontOverridesLegacyAndSaFont()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var newScreen = InvokeAllocMem(bus, 0x20, 0);
		bus.WriteLong(newScreen + NewScreenFontOffset, 0x00F0_0000);
		var taggedFont = InvokeAllocMem(bus, 8, 0);
		bus.WriteWord(taggedFont + 4, 12);
		var tags = InvokeAllocMem(bus, 4 * 8, 0);
		bus.WriteLong(tags, ScreenTagFont);
		bus.WriteLong(tags + 4, taggedFont);
		bus.WriteLong(tags + 8, ScreenTagSysFont);
		bus.WriteLong(tags + 12, 0);
		bus.WriteLong(tags + 16, 0);

		var open = new M68kCpuState { A = { [0] = newScreen, [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));
		Assert.NotEqual(0u, open.D[0]);
		var screenFont = bus.ReadLong(open.D[0] + ScreenFontOffset);
		Assert.NotEqual(taggedFont, screenFont);
		Assert.True(bus.IsMappedMemoryRange(screenFont, 8));
		Assert.Equal((ushort)8, bus.ReadWord(screenFont + 4));
		var window = bus.ReadLong(open.D[0] + GraphicsLayouts.ScreenFirstWindow);
		var windowRastPort = bus.ReadLong(window + WindowRPortOffset);
		Assert.Equal(
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + GfxBaseDefaultFontOffset),
			bus.ReadLong(windowRastPort + GraphicsLayouts.RastPortFont));
	}

	[Fact]
	public void OpenScreenTagListRejectsUnsupportedSysFontSelector()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var tags = InvokeAllocMem(bus, 2 * 8, 0);
		bus.WriteLong(tags, ScreenTagSysFont);
		bus.WriteLong(tags + 4, 2);
		bus.WriteLong(tags + 8, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));
		Assert.Equal(0u, open.D[0]);
	}

	[Fact]
	public void OpenScreenTagListSysFontCanOverrideMalformedSaFont()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var tags = InvokeAllocMem(bus, 3 * 8, 0);
		bus.WriteLong(tags, ScreenTagFont);
		bus.WriteLong(tags + 4, 0x00F0_0010);
		bus.WriteLong(tags + 8, ScreenTagSysFont);
		bus.WriteLong(tags + 12, 1);
		bus.WriteLong(tags + 16, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));
		Assert.NotEqual(0u, open.D[0]);
		var screenFont = bus.ReadLong(open.D[0] + ScreenFontOffset);
		Assert.True(bus.IsMappedMemoryRange(screenFont, 8));
		Assert.Equal((ushort)8, bus.ReadWord(screenFont + 4));
	}

	[Fact]
	public void GetScreenDataCopiesOwnedScreenPrefixesForCustomAndStandardQueries()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var newScreen = InvokeAllocMem(bus, 0x20, 0);
		bus.WriteWord(newScreen + NewScreenWidthOffset, 400);
		bus.WriteWord(newScreen + NewScreenHeightOffset, 220);
		var open = new M68kCpuState { A = { [0] = newScreen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));

		var screen = open.D[0];
		var customBuffer = InvokeAllocMem(bus, ScreenStructSize, 0xA5);
		var custom = new M68kCpuState
		{
			A = { [0] = customBuffer, [1] = screen },
			D = { [0] = ScreenStructSize, [1] = 0x000F }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -426),
			custom));
		Assert.Equal(1u, custom.D[0]);
		Assert.Equal((ushort)400, bus.ReadWord(customBuffer + ScreenWidthOffset));
		Assert.Equal((ushort)220, bus.ReadWord(customBuffer + ScreenHeightOffset));
		Assert.Equal(bus.ReadWord(screen + ScreenFlagsOffset), bus.ReadWord(customBuffer + ScreenFlagsOffset));
		Assert.Equal(
			(ushort)0x000F,
			(ushort)(bus.ReadWord(customBuffer + ScreenFlagsOffset) & 0x000F));

		var standardBuffer = InvokeAllocMem(bus, 0x20, 0xCC);
		var standard = new M68kCpuState
		{
			A = { [0] = standardBuffer },
			D = { [0] = 0x20, [1] = 1 }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -426),
			standard));
		Assert.Equal(1u, standard.D[0]);
		Assert.Equal(bus.ReadWord(screen + ScreenWidthOffset), bus.ReadWord(standardBuffer + ScreenWidthOffset));
		Assert.Equal(bus.ReadWord(screen + ScreenHeightOffset), bus.ReadWord(standardBuffer + ScreenHeightOffset));
		Assert.Equal(
			(ushort)0x0001,
			(ushort)(bus.ReadWord(standardBuffer + ScreenFlagsOffset) & 0x000F));

		var publicBuffer = InvokeAllocMem(bus, 0x20, 0xCC);
		var publicScreen = new M68kCpuState
		{
			A = { [0] = publicBuffer },
			D = { [0] = 0x20, [1] = 2 }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -426),
			publicScreen));
		Assert.Equal(1u, publicScreen.D[0]);
		Assert.Equal(
			(ushort)0x0002,
			(ushort)(bus.ReadWord(publicBuffer + ScreenFlagsOffset) & 0x000F));
	}

	[Fact]
	public void GetScreenDataRejectsWrappedDestinationBeforeCopyingThroughLowMemory()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));

		const uint wrappedBuffer = 0xFFFF_FFFCu;
		const uint lowAlias = 0x0000_0000u;
		const uint sentinel = 0xA5A5_5A5Au;
		bus.MapWritableMemory(wrappedBuffer, new byte[4]);
		bus.WriteLong(lowAlias, sentinel);
		var state = new M68kCpuState
		{
			A = { [0] = wrappedBuffer, [1] = open.D[0] },
			D = { [0] = 8, [1] = CustomScreenType }
		};

		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -426),
			state));
		Assert.Equal(0u, state.D[0]);
		Assert.Equal(sentinel, bus.ReadLong(lowAlias));
	}

	[Fact]
	public void GetScreenDataRejectsReadOnlyDestinationBeforeCopying()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));

		var buffer = InvokeAllocMem(bus, 8, 0);
		for (var index = 0u; index < 8; index++)
			bus.WriteByte(buffer + index, 0xA5, 0);
		bus.MapReadOnlyMemory(buffer, new byte[]
		{
			0xA5, 0xA5, 0xA5, 0xA5,
			0xA5, 0xA5, 0xA5, 0xA5
		});
		var state = new M68kCpuState
		{
			A = { [0] = buffer, [1] = open.D[0] },
			D = { [0] = 8, [1] = CustomScreenType }
		};

		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -426),
			state));
		Assert.Equal(0u, state.D[0]);
		for (var index = 0u; index < 8; index++)
			Assert.Equal((byte)0xA5, bus.ReadByte(buffer + index));
	}

	[Fact]
	public void QueryOverscanPublishesProfileAwareNativeModeRectangles()
	{
		var machine = StartBootShim(MachineProfile.A500PlusEcsNtsc);
		var bus = machine.Bus;
		var rectangle = InvokeAllocMem(bus, 16, 0);
		var query = new M68kCpuState
		{
			A = { [0] = GraphicsModeIds.NtscMonitor | GraphicsModeIds.HiresKey, [1] = rectangle },
			D = { [0] = 2 }
		};

		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -474),
			query));
		Assert.Equal(1u, query.D[0]);
		Assert.Equal((short)0, unchecked((short)bus.ReadWord(rectangle)));
		Assert.Equal((short)0, unchecked((short)bus.ReadWord(rectangle + 2)));
		Assert.Equal((short)(AmigaConstants.NtscLowResWidth * 2 - 1), unchecked((short)bus.ReadWord(rectangle + 4)));
		Assert.Equal((short)(AmigaConstants.NtscLowResHeight - 1), unchecked((short)bus.ReadWord(rectangle + 6)));

		var defaultQuery = new M68kCpuState
		{
			A = { [0] = GraphicsModeIds.DefaultMonitor | GraphicsModeIds.HiresKey, [1] = rectangle },
			D = { [0] = 2 }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -474),
			defaultQuery));
		Assert.Equal(1u, defaultQuery.D[0]);
		Assert.Equal(
			(short)(AmigaConstants.NtscLowResWidth * 2 - 1),
			unchecked((short)bus.ReadWord(rectangle + 4)));
		Assert.Equal(
			(short)(AmigaConstants.NtscLowResHeight - 1),
			unchecked((short)bus.ReadWord(rectangle + 6)));

		var foreign = new M68kCpuState
		{
			A = { [0] = 0xDEAD_BEEFu, [1] = rectangle },
			D = { [0] = 2, [2] = 0x1234_5678u }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -474),
			foreign));
		Assert.Equal(2u, foreign.D[0]);
		Assert.Equal(0x1234_5678u, foreign.D[2]);
	}

	[Fact]
	public void QueryOverscanUsesExplicitPalMonitorOnNtscHost()
	{
		var machine = StartBootShim(MachineProfile.A500PlusEcsNtsc);
		var bus = machine.Bus;
		var rectangle = InvokeAllocMem(bus, 8, 0);
		var query = new M68kCpuState
		{
			A = { [0] = GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey, [1] = rectangle },
			D = { [0] = 2 }
		};

		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -474),
			query));

		Assert.Equal(1u, query.D[0]);
		Assert.Equal(
			(short)(AmigaConstants.PalLowResWidth * 2 - 1),
			unchecked((short)bus.ReadWord(rectangle + 4)));
		Assert.Equal(
			(short)(AmigaConstants.PalLowResHeight - 1),
			unchecked((short)bus.ReadWord(rectangle + 6)));
	}

	[Fact]
	public void QueryOverscanLeavesUnavailableEcsSuperHiresToProvider()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var rectangle = InvokeAllocMem(bus, 8, 0);
		bus.WriteLong(rectangle, 0xA5A5_A5A5);
		bus.WriteLong(rectangle + 4, 0x5A5A_5A5A);
		var query = new M68kCpuState
		{
			A = { [0] = GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey, [1] = rectangle },
			D = { [0] = 2, [2] = 0x1234_5678u }
		};

		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -474),
			query));
		Assert.Equal(2u, query.D[0]);
		Assert.Equal(0x1234_5678u, query.D[2]);
		Assert.Equal(0xA5A5_A5A5u, bus.ReadLong(rectangle));
		Assert.Equal(0x5A5A_5A5Au, bus.ReadLong(rectangle + 4));
	}

	[Fact]
	public void QueryOverscanRejectsOddAndWrappedRectangleEnvelopes()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint wrappedRectangle = 0xFFFF_FFFCu;
		const uint lowAlias = 0x0000_0000u;
		const uint sentinel = 0x5A5A_A5A5u;
		bus.MapWritableMemory(wrappedRectangle, new byte[4]);
		bus.WriteLong(lowAlias, sentinel);

		var wrapped = new M68kCpuState
		{
			A = { [0] = GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey, [1] = wrappedRectangle },
			D = { [0] = 2, [2] = 0x1234_5678u }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -474),
			wrapped));
		Assert.Equal(2u, wrapped.D[0]);
		Assert.Equal(0x1234_5678u, wrapped.D[2]);
		Assert.Equal(sentinel, bus.ReadLong(lowAlias));

		var odd = new M68kCpuState
		{
			A = { [0] = GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey, [1] = 0x0000_0201u },
			D = { [0] = 2, [2] = 0x8765_4321u }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -474),
			odd));
		Assert.Equal(2u, odd.D[0]);
		Assert.Equal(0x8765_4321u, odd.D[2]);
	}

	[Fact]
	public void QueryOverscanRejectsReadOnlyRectangleBeforePublication()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var rectangle = InvokeAllocMem(bus, 8, 0);
		const uint firstSentinel = 0xA5A5_A5A5u;
		const uint secondSentinel = 0x5A5A_5A5Au;
		bus.WriteLong(rectangle, firstSentinel);
		bus.WriteLong(rectangle + 4, secondSentinel);
		bus.MapReadOnlyMemory(rectangle, new byte[]
		{
			0xA5, 0xA5, 0xA5, 0xA5,
			0x5A, 0x5A, 0x5A, 0x5A
		});

		var query = new M68kCpuState
		{
			A = { [0] = GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey, [1] = rectangle },
			D = { [0] = 2, [2] = 0x1234_5678u }
		};

		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -474),
			query));
		Assert.Equal(2u, query.D[0]);
		Assert.Equal(0x1234_5678u, query.D[2]);
		Assert.Equal(firstSentinel, bus.ReadLong(rectangle));
		Assert.Equal(secondSentinel, bus.ReadLong(rectangle + 4));
	}

	[Fact]
	public void OpenScreenTagListUsesDisplayClipForUnspecifiedScreenGeometry()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var clip = InvokeAllocMem(bus, 8, 0);
		bus.WriteWord(clip, 10);
		bus.WriteWord(clip + 2, 5);
		bus.WriteWord(clip + 4, 329);
		bus.WriteWord(clip + 6, 204);
		var tags = InvokeAllocMem(bus, 2 * 8, 0);
		bus.WriteLong(tags, ScreenTagDClip);
		bus.WriteLong(tags + 4, clip);
		bus.WriteLong(tags + 8, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		var screen = open.D[0];
		Assert.Equal(10, unchecked((short)bus.ReadWord(screen + ScreenLeftEdgeOffset)));
		Assert.Equal(5, unchecked((short)bus.ReadWord(screen + ScreenTopEdgeOffset)));
		Assert.Equal(320, bus.ReadWord(screen + ScreenWidthOffset));
		Assert.Equal(200, bus.ReadWord(screen + ScreenHeightOffset));
	}

	[Fact]
	public void OpenScreenTagListAppliesDisplayClipWhenLegacyDimensionsAreStandardSentinels()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var newScreen = InvokeAllocMem(bus, 0x20, 0);
		bus.WriteWord(newScreen + NewScreenLeftOffset, unchecked((ushort)-3));
		bus.WriteWord(newScreen + NewScreenTopOffset, 4);
		bus.WriteWord(newScreen + NewScreenWidthOffset, ushort.MaxValue); // STDSCREENWIDTH (-1)
		bus.WriteWord(newScreen + NewScreenHeightOffset, ushort.MaxValue); // STDSCREENHEIGHT (-1)
		bus.WriteByte(newScreen + NewScreenDepthOffset, 2, 0);

		var clip = InvokeAllocMem(bus, 8, 0);
		bus.WriteWord(clip, 10);
		bus.WriteWord(clip + 2, 5);
		bus.WriteWord(clip + 4, 329);
		bus.WriteWord(clip + 6, 204);
		var tags = InvokeAllocMem(bus, 2 * 8, 0);
		bus.WriteLong(tags, ScreenTagDClip);
		bus.WriteLong(tags + 4, clip);
		bus.WriteLong(tags + 8, 0);

		var open = new M68kCpuState { A = { [0] = newScreen, [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		var screen = open.D[0];
		Assert.NotEqual(0u, screen);
		Assert.Equal(320, bus.ReadWord(screen + ScreenWidthOffset));
		Assert.Equal(200, bus.ReadWord(screen + ScreenHeightOffset));
		Assert.Equal((short)-3, unchecked((short)bus.ReadWord(screen + ScreenLeftEdgeOffset)));
		Assert.Equal((short)4, unchecked((short)bus.ReadWord(screen + ScreenTopEdgeOffset)));

		var close = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
	}

	[Fact]
	public void OpenScreenTagListAppliesPensAndProfileOverscanGeometry()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var pens = InvokeAllocMem(bus, 6, 0);
		bus.WriteWord(pens, 5);
		bus.WriteWord(pens + 2, 6);
		bus.WriteWord(pens + 4, ushort.MaxValue);

		var tags = InvokeAllocMem(bus, 3 * 8, 0);
		bus.WriteLong(tags, ScreenTagPens);
		bus.WriteLong(tags + 4, pens);
		bus.WriteLong(tags + 8, ScreenTagOverscan);
		bus.WriteLong(tags + 12, 2); // OSCAN_STANDARD
		bus.WriteLong(tags + 16, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		var screen = open.D[0];
		Assert.Equal((byte)5, bus.ReadByte(screen + ScreenDetailPenOffset));
		Assert.Equal((byte)6, bus.ReadByte(screen + ScreenBlockPenOffset));
		Assert.Equal(AmigaConstants.PalLowResWidth, bus.ReadWord(screen + ScreenWidthOffset));
		Assert.Equal(AmigaConstants.PalLowResHeight, bus.ReadWord(screen + ScreenHeightOffset));
	}

	[Fact]
	public void OpenScreenTagListDetailAndBlockPenTagsOverrideLegacyPens()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var newScreen = InvokeAllocMem(bus, 0x20, 0);
		bus.WriteWord(newScreen + NewScreenWidthOffset, 320);
		bus.WriteWord(newScreen + NewScreenHeightOffset, 200);
		bus.WriteByte(newScreen + NewScreenDepthOffset, 2, 0);
		bus.WriteByte(newScreen + 10, 9, 0); // NewScreen.DetailPen
		bus.WriteByte(newScreen + 11, 10, 0); // NewScreen.BlockPen

		var tags = InvokeAllocMem(bus, 3 * 8, 0);
		bus.WriteLong(tags, ScreenTagDetailPen);
		bus.WriteLong(tags + 4, 11);
		bus.WriteLong(tags + 8, ScreenTagBlockPen);
		bus.WriteLong(tags + 12, 12);
		bus.WriteLong(tags + 16, 0);

		var open = new M68kCpuState { A = { [0] = newScreen, [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		var screen = open.D[0];
		Assert.Equal((byte)11, bus.ReadByte(screen + ScreenDetailPenOffset));
		Assert.Equal((byte)12, bus.ReadByte(screen + ScreenBlockPenOffset));
	}

	[Fact]
	public void OpenScreenTagListUsesNtscOverscanEnvelope()
	{
		var machine = StartBootShim(MachineProfile.A500PlusEcsNtsc);
		var bus = machine.Bus;
		var tags = InvokeAllocMem(bus, 2 * 8, 0);
		bus.WriteLong(tags, ScreenTagOverscan);
		bus.WriteLong(tags + 4, 2); // OSCAN_STANDARD
		bus.WriteLong(tags + 8, 0);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		var screen = open.D[0];
		Assert.Equal(AmigaConstants.NtscLowResWidth, bus.ReadWord(screen + ScreenWidthOffset));
		Assert.Equal(AmigaConstants.NtscLowResHeight, bus.ReadWord(screen + ScreenHeightOffset));
	}

	[Fact]
	public void OpenScreenUsesNtscStandardHeightWhenNewScreenHeightIsZero()
	{
		var machine = StartBootShim(MachineProfile.A500PlusEcsNtsc);
		var bus = machine.Bus;
		var newScreen = InvokeAllocMem(bus, 0x20, 0);
		bus.WriteWord(newScreen + NewScreenWidthOffset, 320);
		bus.WriteWord(newScreen + NewScreenHeightOffset, 0);
		bus.WriteByte(newScreen + NewScreenDepthOffset, 2, 0);
		bus.WriteWord(newScreen + NewScreenViewModesOffset, 0);

		var openScreenState = new M68kCpuState { A = { [0] = newScreen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			openScreenState));

		var screen = openScreenState.D[0];
		Assert.Equal(AmigaConstants.NtscLowResStandardHeight, bus.ReadWord(screen + ScreenHeightOffset));
		Assert.Equal(
			AmigaConstants.NtscLowResStandardHeight,
			bus.ReadWord(screen + ScreenBitMapOffset + BitMapRowsOffset));
		Assert.Equal(
			AmigaConstants.NtscLowResStandardHeight,
			bus.ReadWord(screen + ScreenViewPortOffset + ViewPortDHeightOffset));
	}

	[Fact]
	public void CloseScreenBlanksSyntheticViewAndAllowsFreshOpenScreenGeometry()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		var firstOpen = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			firstOpen));
		var firstScreen = firstOpen.D[0];
		Assert.NotEqual(0u, firstScreen);
		var firstViewPort = firstScreen + ScreenViewPortOffset;
		Assert.NotEqual(0u, bus.ReadLong(firstViewPort + ViewPortDspInsOffset));
		const uint sprInstructions = 0xDEAD_1000;
		const uint clrInstructions = 0xDEAD_2000;
		const uint userInstructions = 0xDEAD_3000;
		bus.WriteLong(firstViewPort + (uint)GraphicsLayouts.ViewPortSprIns, sprInstructions);
		bus.WriteLong(firstViewPort + (uint)GraphicsLayouts.ViewPortClrIns, clrInstructions);
		bus.WriteLong(firstViewPort + (uint)GraphicsLayouts.ViewPortUCopIns, userInstructions);
		var firstViewState = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -294),
			firstViewState));
		var firstView = firstViewState.D[0];
		Assert.NotEqual(0u, bus.ReadLong(firstView + ViewLofCprListOffset));

		var close = new M68kCpuState
		{
			A = { [0] = firstScreen },
			Cycles = 1234
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
		Assert.Equal(
			0u,
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));
		Assert.Equal(0u, bus.ReadLong(firstViewPort + ViewPortDspInsOffset));
		Assert.Equal(0u, bus.ReadLong(firstViewPort + (uint)GraphicsLayouts.ViewPortSprIns));
		Assert.Equal(0u, bus.ReadLong(firstViewPort + (uint)GraphicsLayouts.ViewPortClrIns));
		Assert.Equal(0u, bus.ReadLong(firstViewPort + (uint)GraphicsLayouts.ViewPortUCopIns));
		Assert.Equal(0u, bus.ReadLong(firstView + ViewLofCprListOffset));
		Assert.Equal(0u, bus.ReadLong(firstView + ViewShfCprListOffset));
		var hasPublishedViewSignature = typeof(AmigaBootController).GetField(
			"_hasPublishedViewSignature",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(hasPublishedViewSignature);
		Assert.False((bool)hasPublishedViewSignature!.GetValue(boot)!);

		var newScreen = InvokeAllocMem(bus, 0x20, 0);
		bus.WriteWord(newScreen + NewScreenWidthOffset, 640);
		bus.WriteWord(newScreen + NewScreenHeightOffset, 200);
		bus.WriteByte(newScreen + NewScreenDepthOffset, 2, 0);
		var secondOpen = new M68kCpuState { A = { [0] = newScreen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			secondOpen));
		Assert.NotEqual(firstScreen, secondOpen.D[0]);
		Assert.Equal(640, bus.ReadWord(secondOpen.D[0] + ScreenWidthOffset));
		Assert.Equal(200, bus.ReadWord(secondOpen.D[0] + ScreenHeightOffset));
	}

	[Fact]
	public void BootLoadViewLeavesTheCompactHostOutsideNativeViewExtraSidecars()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));
		var screen = open.D[0];
		Assert.NotEqual(0u, screen);

		var viewLookup = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -294),
			viewLookup));
		var view = viewLookup.D[0];
		Assert.NotEqual(0u, view);

		var newExtra = new M68kCpuState
		{
			D = { [0] = GraphicsExtendedNodeOperations.ViewExtraType }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, (int)GraphicsLvo.GfxNew),
			newExtra));
		var viewExtra = newExtra.D[0];
		Assert.NotEqual(0u, viewExtra);

		bus.WriteWord(
			view + (uint)GraphicsLayouts.ViewModes,
			GraphicsModeIds.ExtendedMode);
		var associate = new M68kCpuState
		{
			A = { [0] = view, [1] = viewExtra }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, (int)GraphicsLvo.GfxAssociate),
			associate));

		bus.WriteLong(
			viewExtra + (uint)GraphicsLayouts.ViewExtraMonitor,
			0x0000_5000);
		bus.WriteWord(
			viewExtra + (uint)GraphicsLayouts.ViewExtraTopLine,
			23);

		var currentMonitorAddress = AmigaKickstartHost.GraphicsLibraryBase +
			(uint)GraphicsLayouts.GfxBaseCurrentMonitor;
		var topLineAddress = AmigaKickstartHost.GraphicsLibraryBase +
			(uint)GraphicsLayouts.GfxBaseTopLine;
		Assert.False(bus.IsMappedMemoryRange(currentMonitorAddress, sizeof(uint)));
		Assert.False(bus.IsMappedMemoryRange(topLineAddress, sizeof(ushort)));

		var load = new M68kCpuState
		{
			A = { [1] = view },
			Cycles = 1500
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, (int)GraphicsLvo.LoadView),
			load));
		// The default CopperScreen host image is intentionally compact; the
		// optional native tail is claimed only after a full native GfxBase
		// envelope is discovered.  Its poison values remain provider-owned here.
		Assert.False(bus.IsMappedMemoryRange(currentMonitorAddress, sizeof(uint)));
		Assert.False(bus.IsMappedMemoryRange(topLineAddress, sizeof(ushort)));

		// The host View topology is unchanged, so this is an idempotent
		// LoadView.  Its optional native tail still follows the edited ViewExtra.
		bus.WriteLong(
			viewExtra + (uint)GraphicsLayouts.ViewExtraMonitor,
			0x0000_6000);
		bus.WriteWord(
			viewExtra + (uint)GraphicsLayouts.ViewExtraTopLine,
			24);
		var reload = new M68kCpuState
		{
			A = { [1] = view },
			Cycles = 1600
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, (int)GraphicsLvo.LoadView),
			reload));
		Assert.False(bus.IsMappedMemoryRange(currentMonitorAddress, sizeof(uint)));
		Assert.False(bus.IsMappedMemoryRange(topLineAddress, sizeof(ushort)));

		var blank = new M68kCpuState
		{
			A = { [1] = 0 },
			Cycles = 1700
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, (int)GraphicsLvo.LoadView),
			blank));
		Assert.False(bus.IsMappedMemoryRange(currentMonitorAddress, sizeof(uint)));
		Assert.False(bus.IsMappedMemoryRange(topLineAddress, sizeof(ushort)));
	}

	[Fact]
	public void ScreenToFrontRepublishesSyntheticViewAfterLoadViewNull()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));
		var screen = open.D[0];
		Assert.NotEqual(0u, screen);

		var viewLookup = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -294),
			viewLookup));
		var view = viewLookup.D[0];
		Assert.NotEqual(0u, view);
		Assert.Equal(
			view,
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase +
				(uint)GraphicsLayouts.GfxBaseActiView));

		// LoadView(NULL) is a real display hand-off: it clears the public
		// active-view pointer immediately while the blanking list waits for the
		// scheduler boundary.  ScreenToFront must be able to reclaim this
		// compatibility Screen without relying on a CyberGraphX viewport.
		var blank = new M68kCpuState
		{
			A = { [1] = 0 },
			Cycles = 1400
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -222),
			blank));
		Assert.Equal(
			0u,
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase +
				(uint)GraphicsLayouts.GfxBaseActiView));

		var front = new M68kCpuState
		{
			A = { [0] = screen, [1] = 0xCAFE_BABEu },
			Cycles = 1500
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -252),
			front));
		Assert.Equal(
			view,
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase +
				(uint)GraphicsLayouts.GfxBaseActiView));
		Assert.NotEqual(0u, bus.ReadLong(
			screen + ScreenViewPortOffset + ViewPortDspInsOffset));
		Assert.Equal(0xCAFE_BABEu, front.A[1]);
	}

	[Fact]
	public void ScreenToFrontDoesNotRequeueAnAlreadyActiveSyntheticView()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;

		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));
		var screen = open.D[0];
		Assert.NotEqual(0u, screen);

		var viewLookup = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -294),
			viewLookup));
		var view = viewLookup.D[0];
		Assert.NotEqual(0u, view);
		Assert.Equal(
			view,
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase +
				(uint)GraphicsLayouts.GfxBaseActiView));

		var pendingValid = typeof(AmigaBootController).GetField(
			"_pendingCopperListValid",
			BindingFlags.Instance | BindingFlags.NonPublic);
		var pendingCycle = typeof(AmigaBootController).GetField(
			"_pendingCopperListCycle",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(pendingValid);
		Assert.NotNull(pendingCycle);
		Assert.False((bool)pendingValid!.GetValue(boot)!);
		Assert.Equal(-1L, (long)pendingCycle!.GetValue(boot)!);

		// A duplicate LoadView at a mid-frame cycle would defer an otherwise
		// stable copper stream until the next frame.  The already-front screen is
		// a successful no-op and must preserve the caller's unrelated A1 value.
		var front = new M68kCpuState
		{
			A = { [0] = screen, [1] = 0xCAFE_BABEu },
			Cycles = 1500
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -252),
			front));
		Assert.Equal(0xCAFE_BABEu, front.A[1]);
		Assert.False((bool)pendingValid.GetValue(boot)!);
		Assert.Equal(-1L, (long)pendingCycle.GetValue(boot)!);
	}

	[Fact]
	public void ScreenToFrontDoesNotInterpretAnOddNativeGraphicsBaseSidecar()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;

		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));
		var screen = open.D[0];
		Assert.NotEqual(0u, screen);

		var viewLookup = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -294),
			viewLookup));
		var view = viewLookup.D[0];
		Assert.NotEqual(0u, view);

		const uint oddGraphicsBase = 0x00F9_0001;
		var oddActiView = oddGraphicsBase + (uint)GraphicsLayouts.GfxBaseActiView;
		bus.MapWritableMemory(oddActiView, new byte[sizeof(uint)]);
		for (var index = 0; index < sizeof(uint); index++)
		{
			bus.WriteByte(
				oddActiView + (uint)index,
				unchecked((byte)(view >> (24 - (index * 8)))),
				0);
		}

		var nativeBaseField = typeof(AmigaBootController).GetField(
			"_nativeGraphicsLibraryBase",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(nativeBaseField);
		nativeBaseField!.SetValue(boot, oddGraphicsBase);

		var pendingValid = typeof(AmigaBootController).GetField(
			"_pendingCopperListValid",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(pendingValid);
		pendingValid!.SetValue(boot, false);

		var front = new M68kCpuState
		{
			A = { [0] = screen, [1] = 0xCAFE_BABEu },
			Cycles = 1500
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -252),
			front));

		// The odd image is byte-readable and contains the active View, but the
		// host must not treat it as a native LONG sidecar.  ScreenToFront must
		// therefore take the normal publication path, and that path must not
		// overwrite the provider-owned bytes.
		Assert.True((bool)pendingValid.GetValue(boot)!);
		for (var index = 0; index < sizeof(uint); index++)
		{
			Assert.Equal(
				unchecked((byte)(view >> (24 - (index * 8)))),
				bus.ReadByte(oddActiView + (uint)index));
		}
		Assert.Equal(0xCAFE_BABEu, front.A[1]);
	}

	[Fact]
	public void ScreenToFrontRequeuesWhenGuestActiViewWasReplaced()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;

		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));
		var screen = open.D[0];
		Assert.NotEqual(0u, screen);

		// A native/provider handoff may replace the guest ActiView sidecar while
		// the compatibility owner still retains its host-side current View.  That
		// mismatch must remain eligible for an explicit ScreenToFront takeover.
		var foreignView = InvokeAllocMem(
			bus,
			GraphicsLayouts.ViewSize,
			MemfPublic | MemfClear);
		Assert.NotEqual(0u, foreignView);
		var actiView = AmigaKickstartHost.GraphicsLibraryBase +
			(uint)GraphicsLayouts.GfxBaseActiView;
		var view = bus.ReadLong(actiView);
		Assert.NotEqual(0u, view);
		bus.WriteLong(actiView, foreignView);

		var pendingValid = typeof(AmigaBootController).GetField(
			"_pendingCopperListValid",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(pendingValid);
		Assert.False((bool)pendingValid!.GetValue(boot)!);

		var front = new M68kCpuState
		{
			A = { [0] = screen, [1] = 0xCAFE_BABEu },
			Cycles = 1500
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -252),
			front));
		Assert.Equal(0xCAFE_BABEu, front.A[1]);
		Assert.True((bool)pendingValid.GetValue(boot)!);
		Assert.Equal(
			bus.GetNextFrameStartCycle(front.Cycles),
			(long)typeof(AmigaBootController).GetField(
				"_pendingCopperListCycle",
				BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(boot)!);
		Assert.Equal(view, bus.ReadLong(actiView));
	}

	[Fact]
	public void LoadViewDoesNotRequeueAnUnchangedActiveViewButRequeuesAfterViewPortEdit()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;

		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));
		var view = bus.ReadLong(
			AmigaKickstartHost.GraphicsLibraryBase +
				(uint)GraphicsLayouts.GfxBaseActiView);
		Assert.NotEqual(0u, view);
		var viewPort = bus.ReadLong(view + ViewViewPortOffset);
		Assert.NotEqual(0u, viewPort);

		var pendingValid = typeof(AmigaBootController).GetField(
			"_pendingCopperListValid",
			BindingFlags.Instance | BindingFlags.NonPublic);
		var pendingCycle = typeof(AmigaBootController).GetField(
			"_pendingCopperListCycle",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(pendingValid);
		Assert.NotNull(pendingCycle);
		Assert.False((bool)pendingValid!.GetValue(boot)!);

		// A repeated LoadView for the same guest topology repairs the public
		// active-view publication without delaying the already visible stream.
		var actiView = AmigaKickstartHost.GraphicsLibraryBase +
			(uint)GraphicsLayouts.GfxBaseActiView;
		bus.WriteLong(actiView, 0xDEAD_BEEFu);
		var repeat = new M68kCpuState
		{
			A = { [1] = view },
			D = { [0] = 0xDEAD_BEEFu },
			Cycles = 1500
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -222),
			repeat));
		Assert.Equal(0u, repeat.D[0]);
		Assert.False((bool)pendingValid.GetValue(boot)!);
		Assert.Equal(-1L, (long)pendingCycle.GetValue(boot)!);
		Assert.Equal(view, bus.ReadLong(actiView));

		// Editing the public ViewPort envelope invalidates the topology
		// signature and must request a fresh frame-boundary handoff.
		var originalWidth = bus.ReadWord(viewPort + ViewPortDWidthOffset);
		bus.WriteWord(viewPort + ViewPortDWidthOffset, (ushort)(originalWidth + 1));
		var edited = new M68kCpuState
		{
			A = { [1] = view },
			D = { [0] = 0xCAFE_BABEu },
			Cycles = 1500
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -222),
			edited));
		Assert.Equal(0u, edited.D[0]);
		Assert.True((bool)pendingValid.GetValue(boot)!);
		Assert.Equal(
			bus.GetNextFrameStartCycle(edited.Cycles),
			(long)pendingCycle.GetValue(boot)!);
	}

	[Fact]
	public void OpenScreenDeclinesASecondSyntheticSessionWithoutReusingTheLiveScreen()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var firstOpen = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			firstOpen));
		var firstScreen = firstOpen.D[0];
		Assert.NotEqual(0u, firstScreen);
		var firstWidth = bus.ReadWord(firstScreen + ScreenWidthOffset);
		var firstHeight = bus.ReadWord(firstScreen + ScreenHeightOffset);
		var activeView = bus.ReadLong(
			AmigaKickstartHost.GraphicsLibraryBase + GraphicsLayouts.GfxBaseActiView);

		// The compatibility Intuition bridge owns one private Screen session;
		// a second legacy OpenScreen must not mutate that live public envelope or
		// return its address as if the caller had opened a new screen.
		var secondNewScreen = InvokeAllocMem(bus, 0x20, 0);
		bus.WriteWord(secondNewScreen + NewScreenWidthOffset, 640);
		bus.WriteWord(secondNewScreen + NewScreenHeightOffset, 200);
		bus.WriteByte(secondNewScreen + NewScreenDepthOffset, 2, 0);
		var secondOpen = new M68kCpuState { A = { [0] = secondNewScreen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			secondOpen));
		Assert.Equal(0u, secondOpen.D[0]);
		Assert.Equal(firstWidth, bus.ReadWord(firstScreen + ScreenWidthOffset));
		Assert.Equal(firstHeight, bus.ReadWord(firstScreen + ScreenHeightOffset));
		Assert.Equal(
			activeView,
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + GraphicsLayouts.GfxBaseActiView));

		// The tag-list form must preserve an explicit SA_ErrorCode destination
		// on the same provider/native decline; it must not report a synthetic
		// success or overwrite caller-owned error state.
		const uint sentinel = 0xA5A5_A5A5;
		var errorCode = InvokeAllocMem(bus, 4, 0);
		bus.WriteLong(errorCode, sentinel);
		var tags = InvokeAllocMem(bus, 3 * 8, 0);
		bus.WriteLong(tags, ScreenTagWidth);
		bus.WriteLong(tags + 4, 640);
		bus.WriteLong(tags + 8, ScreenTagErrorCode);
		bus.WriteLong(tags + 12, errorCode);
		bus.WriteLong(tags + 16, 0);
		bus.WriteLong(tags + 20, 0);
		var taggedOpen = new M68kCpuState { A = { [0] = 0, [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			taggedOpen));
		Assert.Equal(0u, taggedOpen.D[0]);
		Assert.Equal(sentinel, bus.ReadLong(errorCode));
		Assert.Equal(firstWidth, bus.ReadWord(firstScreen + ScreenWidthOffset));

		// The decline is request-scoped.  Once the live session is closed, the
		// normal lifecycle can stage a fresh guest Screen instead of retaining
		// the rejection marker from the refused legacy call.
		var close = new M68kCpuState { A = { [0] = firstScreen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
		var freshOpen = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			freshOpen));
		Assert.NotEqual(0u, freshOpen.D[0]);
		Assert.NotEqual(firstScreen, freshOpen.D[0]);
	}

	[Fact]
	public void CloseScreenRefusesWhileSyntheticWindowIsOpenThenClosesAfterCloseWindow()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var openScreen = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			openScreen));
		var screen = openScreen.D[0];
		Assert.NotEqual(0u, screen);

		var openWindow = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -204),
			openWindow));
		var window = openWindow.D[0];
		Assert.NotEqual(0u, window);
		var secondOpenWindow = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -204),
			secondOpenWindow));
		Assert.Equal(window, secondOpenWindow.D[0]);
		var activeView = bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22);
		Assert.NotEqual(0u, activeView);

		var refusedClose = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			refusedClose));
		Assert.Equal(0u, refusedClose.D[0]);
		Assert.Equal(activeView, bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));
		Assert.Equal(screen, bus.ReadLong(window + WindowWScreenOffset));

		var closeWindow = new M68kCpuState { A = { [0] = window } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -72),
			closeWindow));
		Assert.Equal(window, bus.ReadLong(screen + ScreenFirstWindowOffset));

		var stillRefusedClose = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			stillRefusedClose));
		Assert.Equal(0u, stillRefusedClose.D[0]);

		var closeSecondWindow = new M68kCpuState { A = { [0] = window } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -72),
			closeSecondWindow));
		Assert.Equal(0u, bus.ReadLong(screen + ScreenFirstWindowOffset));

		var closeScreen = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			closeScreen));
		Assert.Equal(1u, closeScreen.D[0]);
		Assert.Equal(0u, bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));
	}

	[Fact]
	public void CloseWindowRefusesReadOnlyScreenChainBeforeRetiringOwnership()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var openScreen = new M68kCpuState();
		var openWindow = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			openScreen));
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -204),
			openWindow));

		var screen = openScreen.D[0];
		var window = openWindow.D[0];
		Assert.Equal(window, bus.ReadLong(screen + ScreenFirstWindowOffset));
		bus.MapReadOnlyMemory(
			screen + ScreenFirstWindowOffset,
			new byte[]
			{
				(byte)(window >> 24),
				(byte)(window >> 16),
				(byte)(window >> 8),
				(byte)window
			});

		var closeWindow = new M68kCpuState { A = { [0] = window } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -72),
			closeWindow));
		Assert.Equal(window, bus.ReadLong(screen + ScreenFirstWindowOffset));

		// The host ownership claim must remain live because the public chain
		// still points at the Window. CloseScreen therefore remains declined.
		var closeScreen = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			closeScreen));
		Assert.Equal(0u, closeScreen.D[0]);
		Assert.Equal(window, bus.ReadLong(screen + ScreenFirstWindowOffset));
	}

	[Fact]
	public void SyntheticIntuitionTeardownLeavesWrappedScreenAndWindowToTheNativeBoundary()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint sentinel = 0x2468_ACEDu;

		var closeScreen = new M68kCpuState
		{
			A = { [0] = 0xFFFF_FFF0u },
			D = { [0] = sentinel },
			Cycles = 61
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			closeScreen));
		Assert.Equal(sentinel, closeScreen.D[0]);
		Assert.Equal(61, closeScreen.Cycles);

		var closeWindow = new M68kCpuState
		{
			A = { [0] = 0xFFFF_FFF0u },
			D = { [0] = sentinel },
			Cycles = 67
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -72),
			closeWindow));
		Assert.Equal(sentinel, closeWindow.D[0]);
		Assert.Equal(67, closeWindow.Cycles);
	}

	[Fact]
	public void SyntheticScreenViewportQueryDoesNotTurnHostOnlyScreenFailureIntoLowAlias()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());

		// The host bridge uses a non-guest sentinel for compatibility objects
		// whose public envelope could not be allocated.  A ViewPort query must
		// decline that state instead of adding Screen.ViewPort ($2c) to the
		// sentinel and exposing an odd low-memory alias to native callers.
		var rejectedField = typeof(AmigaBootController).GetField(
			"_syntheticScreenConfigurationRejected",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(rejectedField);
		rejectedField!.SetValue(boot, true);

		var query = typeof(AmigaBootController).GetMethod(
			"GetSyntheticScreenViewPortAddress",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(query);
		Assert.Equal(0u, (uint)query!.Invoke(boot, null)!);
	}

	[Fact]
	public void CloseScreenRefusesWhenAForeignWindowRemainsInThePublicChain()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var openScreen = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			openScreen));
		var screen = openScreen.D[0];
		Assert.NotEqual(0u, screen);
		var syntheticWindow = bus.ReadLong(screen + ScreenFirstWindowOffset);
		Assert.NotEqual(0u, syntheticWindow);

		// Simulate a native/provider Window linked ahead of the private
		// presentation backing.  CloseScreen must preserve the active display
		// until that public owner removes its link.
		var foreignWindow = InvokeAllocMem(bus, 0x80, MemfPublic);
		Assert.NotEqual(0u, foreignWindow);
		bus.WriteLong(screen + ScreenFirstWindowOffset, foreignWindow);
		var activeView = bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22);

		var refusedClose = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			refusedClose));
		Assert.Equal(0u, refusedClose.D[0]);
		Assert.Equal(activeView, bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));
		Assert.Equal(foreignWindow, bus.ReadLong(screen + ScreenFirstWindowOffset));

		// Once the public chain is restored to the compatibility backing, the
		// normal synthetic close path can retire the screen.
		bus.WriteLong(screen + ScreenFirstWindowOffset, syntheticWindow);
		var closeScreen = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			closeScreen));
		Assert.Equal(1u, closeScreen.D[0]);
	}

	[Fact]
	public void CloseScreenRefusesWhenSyntheticWindowHasALinkedTail()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var openScreen = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			openScreen));
		var screen = openScreen.D[0];
		var syntheticWindow = bus.ReadLong(screen + ScreenFirstWindowOffset);
		Assert.NotEqual(0u, syntheticWindow);

		var foreignWindow = InvokeAllocMem(bus, 0x80, MemfPublic);
		Assert.NotEqual(0u, foreignWindow);
		bus.WriteLong(syntheticWindow + WindowNextOffset, foreignWindow);
		var activeView = bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22);

		var refusedClose = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			refusedClose));
		Assert.Equal(0u, refusedClose.D[0]);
		Assert.Equal(activeView, bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));
		Assert.Equal(foreignWindow, bus.ReadLong(syntheticWindow + WindowNextOffset));

		bus.WriteLong(syntheticWindow + WindowNextOffset, 0);
		var closeScreen = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			closeScreen));
		Assert.Equal(1u, closeScreen.D[0]);
	}

	[Fact]
	public void CloseScreenRefusesWhenSyntheticWindowLosesItsScreenLink()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var openScreen = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			openScreen));
		var screen = openScreen.D[0];
		var syntheticWindow = bus.ReadLong(screen + ScreenFirstWindowOffset);
		Assert.NotEqual(0u, syntheticWindow);

		var foreignScreen = InvokeAllocMem(bus, GraphicsLayouts.ScreenSize, MemfPublic);
		Assert.NotEqual(0u, foreignScreen);
		bus.WriteLong(syntheticWindow + WindowWScreenOffset, foreignScreen);
		var activeView = bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22);

		var refusedClose = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			refusedClose));
		Assert.Equal(0u, refusedClose.D[0]);
		Assert.Equal(activeView, bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));
		Assert.Equal(syntheticWindow, bus.ReadLong(screen + ScreenFirstWindowOffset));

		// Restoring the canonical ownership link makes the same CloseScreen
		// request eligible again.
		bus.WriteLong(syntheticWindow + WindowWScreenOffset, screen);
		var closeScreen = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			closeScreen));
		Assert.Equal(1u, closeScreen.D[0]);
	}

	[Fact]
	public void CloseWindowRefusesWhenSyntheticWindowLosesItsScreenLink()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var openScreen = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			openScreen));
		var screen = openScreen.D[0];
		var syntheticWindow = bus.ReadLong(screen + ScreenFirstWindowOffset);
		Assert.NotEqual(0u, syntheticWindow);

		var foreignScreen = InvokeAllocMem(bus, GraphicsLayouts.ScreenSize, MemfPublic);
		Assert.NotEqual(0u, foreignScreen);
		bus.WriteLong(syntheticWindow + WindowWScreenOffset, foreignScreen);

		var refusedClose = new M68kCpuState { A = { [0] = syntheticWindow } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -72),
			refusedClose));
		Assert.Equal(0u, refusedClose.D[0]);
		Assert.Equal(syntheticWindow, bus.ReadLong(screen + ScreenFirstWindowOffset));

		// The claim remains live until the canonical WScreen link is restored.
		bus.WriteLong(syntheticWindow + WindowWScreenOffset, screen);
		var closeWindow = new M68kCpuState { A = { [0] = syntheticWindow } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -72),
			closeWindow));
		Assert.Equal(0u, bus.ReadLong(screen + ScreenFirstWindowOffset));

		var closeScreen = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			closeScreen));
		Assert.Equal(1u, closeScreen.D[0]);
	}

	[Fact]
	public void CloseScreenRefusesWhenViewportHasAMappedForeignCopperLink()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var openScreen = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			openScreen));
		var screen = openScreen.D[0];
		var viewPort = screen + ScreenViewPortOffset;
		var foreignCopper = InvokeAllocMem(bus, 0x20, MemfPublic);
		Assert.NotEqual(0u, foreignCopper);
		bus.WriteLong(viewPort + (uint)GraphicsLayouts.ViewPortSprIns, foreignCopper);
		var activeView = bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22);

		var refusedClose = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			refusedClose));
		Assert.Equal(0u, refusedClose.D[0]);
		Assert.Equal(activeView, bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));
		Assert.Equal(foreignCopper, bus.ReadLong(viewPort + (uint)GraphicsLayouts.ViewPortSprIns));

		// Once the provider removes its link, the compatibility owner can
		// retire the screen and clear the remaining stale copper fields.
		bus.WriteLong(viewPort + (uint)GraphicsLayouts.ViewPortSprIns, 0);
		var closeScreen = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			closeScreen));
		Assert.Equal(1u, closeScreen.D[0]);
	}

	[Fact]
	public void CloseScreenRefusesWhenViewportCopperSlotIsReadOnlyBeforeOwnershipRelease()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var openScreen = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			openScreen));
		var screen = openScreen.D[0];
		var viewPort = screen + ScreenViewPortOffset;
		var originalDspIns = bus.ReadLong(viewPort + ViewPortDspInsOffset);
		var activeView = bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22);
		Assert.NotEqual(0u, originalDspIns);

		// A newest read-only mapping is a provider/image overlay over the
		// public ViewPort field.  CloseScreen may inspect it, but it must not
		// release the compatibility CPR owner before proving that the field can
		// be cleared.
		bus.MapReadOnlyMemory(
			viewPort + ViewPortDspInsOffset,
			new byte[]
			{
				(byte)(originalDspIns >> 24),
				(byte)(originalDspIns >> 16),
				(byte)(originalDspIns >> 8),
				(byte)originalDspIns
			});

		var refusedClose = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			refusedClose));
		Assert.Equal(0u, refusedClose.D[0]);
		Assert.Equal(activeView, bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));
		Assert.Equal(originalDspIns, bus.ReadLong(viewPort + ViewPortDspInsOffset));

		// The compatibility claim remains live; a second OpenScreen request is
		// still declined rather than observing a prematurely retired session.
		var secondOpen = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			secondOpen));
		Assert.Equal(0u, secondOpen.D[0]);
	}

	[Fact]
	public void CloseWindowPromotesAReadableTailInThePublicScreenChain()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var openScreen = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			openScreen));
		var screen = openScreen.D[0];
		var syntheticWindow = bus.ReadLong(screen + ScreenFirstWindowOffset);
		Assert.NotEqual(0u, syntheticWindow);

		// A provider/native Window may be linked behind the compatibility
		// backing. Retiring the last synthetic OpenWindow claim must detach
		// only the backing head and leave that readable public tail in place.
		var foreignWindow = InvokeAllocMem(bus, 0x80, MemfPublic);
		Assert.NotEqual(0u, foreignWindow);
		bus.WriteLong(syntheticWindow + WindowNextOffset, foreignWindow);
		var activeView = bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22);
		Assert.NotEqual(0u, activeView);

		var closeWindow = new M68kCpuState { A = { [0] = syntheticWindow } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -72),
			closeWindow));
		Assert.Equal(foreignWindow, bus.ReadLong(screen + ScreenFirstWindowOffset));
		Assert.Equal(foreignWindow, bus.ReadLong(syntheticWindow + WindowNextOffset));
		Assert.Equal(activeView, bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));

		// Once the foreign owner removes its link, the synthetic screen can be
		// closed normally; the prior CloseWindow must not have blanked it.
		bus.WriteLong(screen + ScreenFirstWindowOffset, 0);
		bus.WriteLong(syntheticWindow + WindowNextOffset, 0);
		var closeScreen = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			closeScreen));
		Assert.Equal(1u, closeScreen.D[0]);
	}

	[Fact]
	public void OpenWindowPreservesAnExistingPublicWindowTail()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var openScreen = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			openScreen));
		var screen = openScreen.D[0];
		var syntheticWindow = bus.ReadLong(screen + ScreenFirstWindowOffset);
		Assert.NotEqual(0u, syntheticWindow);

		// Leave a provider/native Window at the public head before the
		// compatibility OpenWindow request arrives.  The synthetic backing must
		// be inserted ahead of it rather than dropping that ownership link.
		var foreignWindow = InvokeAllocMem(bus, 0x80, MemfPublic);
		Assert.NotEqual(0u, foreignWindow);
		bus.WriteLong(screen + ScreenFirstWindowOffset, foreignWindow);

		var openWindow = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -204),
			openWindow));
		Assert.Equal(syntheticWindow, openWindow.D[0]);
		Assert.Equal(syntheticWindow, bus.ReadLong(screen + ScreenFirstWindowOffset));
		Assert.Equal(foreignWindow, bus.ReadLong(syntheticWindow + WindowNextOffset));

		var closeWindow = new M68kCpuState { A = { [0] = syntheticWindow } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -72),
			closeWindow));
		Assert.Equal(foreignWindow, bus.ReadLong(screen + ScreenFirstWindowOffset));

		// Simulate the foreign owner removing its link, then retire the screen.
		bus.WriteLong(screen + ScreenFirstWindowOffset, 0);
		bus.WriteLong(syntheticWindow + WindowNextOffset, 0);
		var closeScreen = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			closeScreen));
		Assert.Equal(1u, closeScreen.D[0]);
	}

	[Fact]
	public void OpenWindowRefusesReadOnlyScreenChainBeforePublishingOwnership()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var openScreen = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			openScreen));

		var screen = openScreen.D[0];
		var syntheticWindow = bus.ReadLong(screen + ScreenFirstWindowOffset);
		Assert.NotEqual(0u, syntheticWindow);
		bus.WriteLong(screen + ScreenFirstWindowOffset, 0);
		bus.MapReadOnlyMemory(
			screen + ScreenFirstWindowOffset,
			new byte[] { 0, 0, 0, 0 });

		var openWindow = new M68kCpuState
		{
			D = { [0] = 0x1357_9BDFu },
			Cycles = 77
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -204),
			openWindow));
		Assert.Equal(0u, openWindow.D[0]);
		Assert.Equal(0u, bus.ReadLong(screen + ScreenFirstWindowOffset));

		// No host Window claim was published, so the screen remains closable.
		var closeScreen = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			closeScreen));
		Assert.Equal(1u, closeScreen.D[0]);
	}

	[Fact]
	public void OpenWindowDeclinesAnUnreadableNewWindowWithoutCreatingAScreen()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var malformed = 0x00FF_FFF0u;
		var openWindow = new M68kCpuState
		{
			Cycles = 1234,
			A = { [0] = malformed },
			D = { [0] = 0xCAFE_BABEu }
		};

		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -204),
			openWindow));
		Assert.Equal(0u, openWindow.D[0]);
		Assert.Equal(1234, openWindow.Cycles);
		Assert.Equal(0u, bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));

		// A later null-NewWindow compatibility request remains usable; the
		// malformed provider-owned request must not poison the reset-scoped
		// synthetic bridge.
		var retry = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -204),
			retry));
		Assert.NotEqual(0u, retry.D[0]);
		var screen = bus.ReadLong(retry.D[0] + WindowWScreenOffset);
		Assert.NotEqual(0u, screen);

		var closeWindow = new M68kCpuState { A = { [0] = retry.D[0] } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -72),
			closeWindow));
		var closeScreen = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			closeScreen));
		Assert.Equal(1u, closeScreen.D[0]);
	}

	[Fact]
	public void OpenWindowDeclinesAForeignCustomScreenTargetWithoutChangingTheWindowChain()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var openScreen = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			openScreen));
		var screen = openScreen.D[0];
		var existingWindow = bus.ReadLong(screen + ScreenFirstWindowOffset);
		Assert.NotEqual(0u, existingWindow);

		var foreignScreen = InvokeAllocMem(bus, (uint)ScreenStructSize, MemfPublic);
		var newWindow = InvokeAllocMem(bus, 0x30, MemfPublic);
		Assert.NotEqual(0u, foreignScreen);
		Assert.NotEqual(0u, newWindow);
		bus.WriteWord(newWindow + (uint)NewWindowTypeOffset, CustomScreenType);
		bus.WriteLong(newWindow + (uint)NewWindowScreenOffset, foreignScreen);

		var rejected = new M68kCpuState
		{
			Cycles = 4321,
			A = { [0] = newWindow },
			D = { [0] = 0xCAFE_BABEu }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -204),
			rejected));
		Assert.Equal(0u, rejected.D[0]);
		Assert.Equal(4321, rejected.Cycles);
		Assert.Equal(existingWindow, bus.ReadLong(screen + ScreenFirstWindowOffset));

		// Rejection must not poison the reset-scoped compatibility form.
		var retry = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -204),
			retry));
		Assert.Equal(existingWindow, retry.D[0]);
		Assert.Equal(existingWindow, bus.ReadLong(screen + ScreenFirstWindowOffset));

		var closeWindow = new M68kCpuState { A = { [0] = existingWindow } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -72),
			closeWindow));
		var closeScreen = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			closeScreen));
		Assert.Equal(1u, closeScreen.D[0]);
	}

	[Fact]
	public void OpenWindowDeclinesANonCustomWindowTypeWithoutChangingTheWindowChain()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var openScreen = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			openScreen));
		var screen = openScreen.D[0];
		var existingWindow = bus.ReadLong(screen + ScreenFirstWindowOffset);
		Assert.NotEqual(0u, existingWindow);

		var newWindow = InvokeAllocMem(bus, 0x30, MemfPublic);
		Assert.NotEqual(0u, newWindow);
		bus.WriteWord(newWindow + (uint)NewWindowTypeOffset, WorkbenchScreenType);
		bus.WriteLong(newWindow + (uint)NewWindowScreenOffset, 0);

		var rejected = new M68kCpuState
		{
			Cycles = 8765,
			A = { [0] = newWindow },
			D = { [0] = 0x1357_9BDFu }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -204),
			rejected));
		Assert.Equal(0u, rejected.D[0]);
		Assert.Equal(8765, rejected.Cycles);
		Assert.Equal(existingWindow, bus.ReadLong(screen + ScreenFirstWindowOffset));

		var retry = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -204),
			retry));
		Assert.Equal(existingWindow, retry.D[0]);

		var closeWindow = new M68kCpuState { A = { [0] = existingWindow } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -72),
			closeWindow));
		var closeScreen = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			closeScreen));
		Assert.Equal(1u, closeScreen.D[0]);
	}

	[Fact]
	public void OpenWindowDeclinesOddNewWindowBaseWithoutChangingTheWindowChain()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var openScreen = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			openScreen));
		var screen = openScreen.D[0];
		var existingWindow = bus.ReadLong(screen + ScreenFirstWindowOffset);
		Assert.NotEqual(0u, existingWindow);

		// The envelope is fully mapped, but A0 is intentionally odd.  A native
		// 68000 caller would take an address error before any NewWindow field is
		// consumed; the portable bridge must preserve that ownership boundary.
		var aligned = InvokeAllocMem(bus, 0x31, MemfPublic);
		Assert.NotEqual(0u, aligned);
		Assert.Equal(0u, aligned & 1u);
		var odd = aligned + 1;

		var rejected = new M68kCpuState
		{
			Cycles = 2468,
			A = { [0] = odd },
			D = { [0] = 0xCAFE_BABEu }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -204),
			rejected));
		Assert.Equal(0u, rejected.D[0]);
		Assert.Equal(2468, rejected.Cycles);
		Assert.Equal(existingWindow, bus.ReadLong(screen + ScreenFirstWindowOffset));

		// Rejection must not poison the reset-scoped compatibility form.
		var retry = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -204),
			retry));
		Assert.Equal(existingWindow, retry.D[0]);

		var closeWindow = new M68kCpuState { A = { [0] = existingWindow } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -72),
			closeWindow));
		var closeScreen = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			closeScreen));
		Assert.Equal(1u, closeScreen.D[0]);
	}

	[Fact]
	public void OpenWindowDeclinesWrappedNewWindowEnvelopeBeforeLowAliasReads()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var openScreen = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			openScreen));
		var screen = openScreen.D[0];
		var existingWindow = bus.ReadLong(screen + ScreenFirstWindowOffset);
		Assert.NotEqual(0u, existingWindow);

		// The mandatory NewWindow prefix ends at Type+WORD.  Map the high
		// prefix and place a distinct value at the wrapped low alias; the
		// compatibility bridge must reject the non-addressable 32-bit span
		// before consuming that unrelated low-memory value.
		var wrappedNewWindow = 0xFFFF_FFD2u;
		const int newWindowEnvelopeSize = NewWindowTypeOffset + sizeof(ushort);
		bus.MapWritableMemory(wrappedNewWindow, new byte[newWindowEnvelopeSize]);
		Assert.True(bus.IsMappedMemoryRange(wrappedNewWindow, newWindowEnvelopeSize));
		bus.WriteWord(wrappedNewWindow + (uint)NewWindowTypeOffset, CustomScreenType);
		bus.WriteLong(2, 0xDEAD_BEEFu);

		var rejected = new M68kCpuState
		{
			Cycles = 4321,
			A = { [0] = wrappedNewWindow },
			D = { [0] = 0x1357_9BDFu }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -204),
			rejected));
		Assert.Equal(0u, rejected.D[0]);
		Assert.Equal(4321, rejected.Cycles);
		Assert.Equal(0xDEAD_BEEFu, bus.ReadLong(2));
		Assert.Equal(existingWindow, bus.ReadLong(screen + ScreenFirstWindowOffset));

		// The rejected request must not poison the compatibility session.
		var retry = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -204),
			retry));
		Assert.Equal(existingWindow, retry.D[0]);

		var closeWindow = new M68kCpuState { A = { [0] = existingWindow } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -72),
			closeWindow));
		var closeScreen = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			closeScreen));
		Assert.Equal(1u, closeScreen.D[0]);
	}

	[Fact]
	public void OpenWindowBeforeScreenStillLinksTheWindowAfterScreenCreation()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var openWindow = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -204),
			openWindow));
		var window = openWindow.D[0];
		Assert.NotEqual(0u, window);

		var screen = bus.ReadLong(window + WindowWScreenOffset);
		Assert.NotEqual(0u, screen);
		Assert.Equal(window, bus.ReadLong(screen + ScreenFirstWindowOffset));

		var refusedClose = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			refusedClose));
		Assert.Equal(0u, refusedClose.D[0]);

		var closeWindow = new M68kCpuState { A = { [0] = window } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -72),
			closeWindow));

		var closeScreen = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			closeScreen));
		Assert.Equal(1u, closeScreen.D[0]);
	}

	[Fact]
	public void OpenScreenDeclinesWhenSyntheticBitmapCannotBeAllocated()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;

		// The boot profile has pseudo-fast memory for compatibility objects, so
		// consume chip memory only. Leaving only sub-page fragments makes the
		// standard-planar screen plane allocation fail while the Screen envelope
		// itself can still be staged.
		var chipAllocations = new List<uint>();
		while (true)
		{
			var allocation = InvokeAllocMem(bus, 0x1000, MemfPublic | MemfChip);
			if (allocation == 0)
				break;

			chipAllocations.Add(allocation);
		}

		Assert.NotEmpty(chipAllocations);
		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198), open));

		Assert.Equal(0u, open.D[0]);
		var screenProperty = typeof(AmigaBootController).GetProperty(
			"_syntheticScreenAddress",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(screenProperty);
		Assert.Equal(0u, (uint)screenProperty.GetValue(boot)!);
	}

	[Fact]
	public void OpenScreenDeclinesWhenSyntheticProgramEnvelopeCannotBeAllocated()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;

		// Compatibility structures prefer the profile's pseudo-fast memory.
		// Exhaust that list while leaving chip memory available so the failure is
		// specifically the public Screen envelope, not its planar backing store.
		var fastAllocations = new List<uint>();
		while (true)
		{
			var allocation = InvokeAllocMem(bus, 0x100, MemfPublic | MemfFast);
			if (allocation == 0)
				break;

			fastAllocations.Add(allocation);
		}

		Assert.NotEmpty(fastAllocations);
		Assert.InRange(InvokeAvailMem(bus, MemfPublic | MemfFast), 0u, 0x15Fu);
		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198), open));

		Assert.Equal(0u, open.D[0]);
		var screenProperty = typeof(AmigaBootController).GetProperty(
			"_syntheticScreenAddress",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(screenProperty);
		Assert.Equal(0u, (uint)screenProperty.GetValue(boot)!);
	}

	[Fact]
	public void OpenScreenTagListReportsNoMemoryWhenItsScreenEnvelopeCannotBeAllocated()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;

		var errorCode = InvokeAllocMem(bus, 4, MemfPublic | MemfFast);
		var tags = InvokeAllocMem(bus, 3 * 8, MemfPublic | MemfFast);
		bus.WriteLong(tags, ScreenTagErrorCode);
		bus.WriteLong(tags + 4, errorCode);
		bus.WriteLong(tags + 8, ScreenTagWidth);
		bus.WriteLong(tags + 12, 640);
		bus.WriteLong(tags + 16, 0);
		bus.WriteLong(errorCode, 0xA5A5A5A5);

		while (InvokeAllocMem(bus, 0x100, MemfPublic | MemfFast) != 0)
		{
		}

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612), open));

		Assert.Equal(0u, open.D[0]);
		Assert.Equal(3u, bus.ReadLong(errorCode));
	}

	[Fact]
	public void FailedOpenScreenTagListDoesNotPoisonTheNextScreenRequest()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;

		var errorCode = InvokeAllocMem(bus, 4, MemfPublic | MemfFast);
		var tags = InvokeAllocMem(bus, 3 * 8, MemfPublic | MemfFast);
		bus.WriteLong(tags, ScreenTagErrorCode);
		bus.WriteLong(tags + 4, errorCode);
		bus.WriteLong(tags + 8, ScreenTagWidth);
		bus.WriteLong(tags + 12, 640);
		bus.WriteLong(tags + 16, 0);
		bus.WriteLong(errorCode, 0xA5A5A5A5);

		var fastAllocations = new List<uint>();
		while (true)
		{
			var allocation = InvokeAllocMem(bus, 0x100, MemfPublic | MemfFast);
			if (allocation == 0)
				break;

			fastAllocations.Add(allocation);
		}

		var failedOpen = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			failedOpen));
		Assert.Equal(0u, failedOpen.D[0]);
		Assert.Equal(3u, bus.ReadLong(errorCode));

		// Restore the allocator only after the failed request.  The retry has no
		// tags and must therefore return the reset-scoped default geometry rather
		// than the width staged by the failed provider-facing request.
		for (var index = fastAllocations.Count - 1; index >= 0; index--)
			InvokeFreeMem(bus, fastAllocations[index], 0x100);
		InvokeFreeMem(bus, tags, 3 * 8);
		InvokeFreeMem(bus, errorCode, 4);

		var retry = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			retry));
		Assert.NotEqual(0u, retry.D[0]);
		Assert.Equal(
			AmigaConstants.PalLowResWidth,
			bus.ReadWord(retry.D[0] + ScreenWidthOffset));

		var close = new M68kCpuState { A = { [0] = retry.D[0] } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
	}

	[Fact]
	public void FailedOpenScreenTagListRollsBackItsPaletteUpdates()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;

		var errorCode = InvokeAllocMem(bus, 4, MemfPublic | MemfFast);
		var colors32 = InvokeAllocMem(bus, 4 + 12 + 4, MemfPublic | MemfFast);
		bus.WriteLong(colors32, 0x0001_0001); // one entry starting at COLOR1
		bus.WriteLong(colors32 + 4, 0xF000_0000);
		bus.WriteLong(colors32 + 8, 0);
		bus.WriteLong(colors32 + 12, 0);
		bus.WriteLong(colors32 + 16, 0);
		var tags = InvokeAllocMem(bus, 3 * 8, MemfPublic | MemfFast);
		bus.WriteLong(tags, ScreenTagColors32);
		bus.WriteLong(tags + 4, colors32);
		bus.WriteLong(tags + 8, ScreenTagErrorCode);
		bus.WriteLong(tags + 12, errorCode);
		bus.WriteLong(tags + 16, 0);
		bus.WriteLong(errorCode, 0xA5A5_A5A5);

		var fastAllocations = new List<uint>();
		while (true)
		{
			var allocation = InvokeAllocMem(bus, 0x100, MemfPublic | MemfFast);
			if (allocation == 0)
				break;

			fastAllocations.Add(allocation);
		}

		var failedOpen = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			failedOpen));
		Assert.Equal(0u, failedOpen.D[0]);
		Assert.Equal(3u, bus.ReadLong(errorCode));

		for (var index = fastAllocations.Count - 1; index >= 0; index--)
			InvokeFreeMem(bus, fastAllocations[index], 0x100);
		InvokeFreeMem(bus, tags, 3 * 8);
		InvokeFreeMem(bus, colors32, 4 + 12 + 4);
		InvokeFreeMem(bus, errorCode, 4);

		// A failed tagged request must not leave COLOR1 selected in the
		// reset-scoped palette. The untagged retry therefore publishes the
		// standard synthetic default color in its freshly rebuilt copper list.
		var retry = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			retry));
		Assert.NotEqual(0u, retry.D[0]);

		var viewState = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -294),
			viewState));
		var view = viewState.D[0];
		var copperList = bus.ReadLong(
			bus.ReadLong(view + ViewLofCprListOffset) + CprListStartOffset);
		Assert.Equal((ushort)0x0238, ReadCopperMoveValue(bus, copperList, 0x0182));

		var close = new M68kCpuState { A = { [0] = retry.D[0] } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
	}

	[Fact]
	public void ProviderDeclineAfterLegacyScreenConfigurationDoesNotPoisonTheNextScreenRequest()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var newScreen = InvokeAllocMem(bus, 0x20, MemfPublic | MemfClear);
		bus.WriteWord(newScreen + NewScreenWidthOffset, 640);
		bus.WriteWord(newScreen + NewScreenHeightOffset, 200);
		bus.WriteByte(newScreen + NewScreenDepthOffset, 2, 0);

		var tags = InvokeAllocMem(bus, 2 * 8, MemfPublic | MemfClear);
		bus.WriteLong(tags, ScreenTagFont);
		bus.WriteLong(tags + 4, 0xFFFF_F000);
		bus.WriteLong(tags + 8, 0);

		var declined = new M68kCpuState { A = { [0] = newScreen, [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			declined));
		Assert.Equal(0u, declined.D[0]);

		var retry = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			retry));
		Assert.NotEqual(0u, retry.D[0]);
		Assert.Equal(
			AmigaConstants.PalLowResWidth,
			bus.ReadWord(retry.D[0] + ScreenWidthOffset));

		var close = new M68kCpuState { A = { [0] = retry.D[0] } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
	}

	[Fact]
	public void OpenScreenTagListReportsNoChipMemoryWhenItsPlanarBackingCannotBeAllocated()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;

		var errorCode = InvokeAllocMem(bus, 4, MemfPublic | MemfFast);
		var tags = InvokeAllocMem(bus, 3 * 8, MemfPublic | MemfFast);
		bus.WriteLong(tags, ScreenTagErrorCode);
		bus.WriteLong(tags + 4, errorCode);
		bus.WriteLong(tags + 8, ScreenTagWidth);
		bus.WriteLong(tags + 12, 640);
		bus.WriteLong(tags + 16, 0);
		bus.WriteLong(errorCode, 0x5A5A5A5A);

		while (InvokeAllocMem(bus, 0x1000, MemfPublic | MemfChip) != 0)
		{
		}

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612), open));

		Assert.Equal(0u, open.D[0]);
		Assert.Equal(4u, bus.ReadLong(errorCode));
	}

	[Fact]
	public void OpenScreenTagListReportsTooDeepForAnOcsEcsDepthBeyondSixPlanes()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		var errorCode = InvokeAllocMem(bus, 4, MemfPublic | MemfFast);
		var tags = InvokeAllocMem(bus, 4 * 8, MemfPublic | MemfFast);
		bus.WriteLong(tags, ScreenTagDepth);
		bus.WriteLong(tags + 4, 7);
		bus.WriteLong(tags + 8, ScreenTagErrorCode);
		bus.WriteLong(tags + 12, errorCode);
		bus.WriteLong(tags + 16, ScreenTagWidth);
		bus.WriteLong(tags + 20, 640);
		bus.WriteLong(tags + 24, 0);
		bus.WriteLong(errorCode, 0xC3C3C3C3);

		var open = new M68kCpuState { A = { [1] = tags } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612), open));

		Assert.Equal(0u, open.D[0]);
		Assert.Equal(7u, bus.ReadLong(errorCode));
		var screenProperty = typeof(AmigaBootController).GetProperty(
			"_syntheticScreenAddress",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(screenProperty);
		Assert.Equal(0u, (uint)screenProperty.GetValue(boot)!);
	}

	[Fact]
	public void LegacyOpenScreenRejectsAnOcsEcsDepthBeyondSixPlanesBeforeStaging()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint newScreen = 0x2200;
		bus.WriteWord(newScreen + NewScreenWidthOffset, 640);
		bus.WriteWord(newScreen + NewScreenHeightOffset, 200);
		bus.WriteByte(newScreen + NewScreenDepthOffset, 7, 0);

		var rejected = new M68kCpuState { A = { [0] = newScreen } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198), rejected));
		Assert.Equal(0u, rejected.D[0]);

		// The rejection is request-scoped. Repairing the NewScreen and retrying
		// must be able to stage a normal synthetic session.
		bus.WriteByte(newScreen + NewScreenDepthOffset, 2, 0);
		var opened = new M68kCpuState { A = { [0] = newScreen } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198), opened));
		Assert.NotEqual(0u, opened.D[0]);
	}

	[Fact]
	public void LegacyOpenScreenDepthRejectionDoesNotPoisonAnUntaggedRetry()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var newScreen = InvokeAllocMem(bus, 0x20, MemfPublic | MemfClear);
		bus.WriteWord(newScreen + NewScreenWidthOffset, 640);
		bus.WriteWord(newScreen + NewScreenHeightOffset, 200);
		bus.WriteByte(newScreen + NewScreenDepthOffset, 7, 0);

		var rejected = new M68kCpuState { A = { [0] = newScreen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			rejected));
		Assert.Equal(0u, rejected.D[0]);

		var retry = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			retry));
		Assert.NotEqual(0u, retry.D[0]);
		Assert.Equal(
			AmigaConstants.PalLowResWidth,
			bus.ReadWord(retry.D[0] + ScreenWidthOffset));

		var close = new M68kCpuState { A = { [0] = retry.D[0] } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
	}

	[Fact]
	public void OpenScreenTagListDoesNotHideAnOverDepthLegacyNewScreen()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var newScreen = InvokeAllocMem(bus, 0x20, 0);
		bus.WriteWord(newScreen + NewScreenWidthOffset, 640);
		bus.WriteWord(newScreen + NewScreenHeightOffset, 200);
		bus.WriteByte(newScreen + NewScreenDepthOffset, 7, 0);
		var errorCode = InvokeAllocMem(bus, 4, MemfPublic | MemfFast);
		var tags = InvokeAllocMem(bus, 3 * 8, MemfPublic | MemfFast);
		bus.WriteLong(tags, ScreenTagErrorCode);
		bus.WriteLong(tags + 4, errorCode);
		bus.WriteLong(tags + 8, ScreenTagWidth);
		bus.WriteLong(tags + 12, 640);
		bus.WriteLong(tags + 16, 0);
		bus.WriteLong(errorCode, 0xA5A5A5A5);

		var open = new M68kCpuState { A = { [0] = newScreen, [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		// SA_Width is unrelated to the invalid legacy depth.  The synthetic
		// standard-planar path must decline instead of silently clamping the
		// request and publishing a screen with a different depth.
		Assert.Equal(0u, open.D[0]);
		Assert.Equal(7u, bus.ReadLong(errorCode));
	}

	[Fact]
	public void OpenScreenTagListAllowsAnExplicitDepthToReplaceLegacyNewScreenDepth()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var newScreen = InvokeAllocMem(bus, 0x20, 0);
		bus.WriteWord(newScreen + NewScreenWidthOffset, 640);
		bus.WriteWord(newScreen + NewScreenHeightOffset, 200);
		bus.WriteByte(newScreen + NewScreenDepthOffset, 7, 0);
		var tags = InvokeAllocMem(bus, 3 * 8, MemfPublic | MemfFast);
		bus.WriteLong(tags, ScreenTagDepth);
		bus.WriteLong(tags + 4, 2);
		bus.WriteLong(tags + 8, ScreenTagWidth);
		bus.WriteLong(tags + 12, 640);
		bus.WriteLong(tags + 16, 0);

		var open = new M68kCpuState { A = { [0] = newScreen, [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));

		Assert.NotEqual(0u, open.D[0]);
		Assert.Equal(2, bus.ReadByte(open.D[0] + ScreenBitMapOffset + BitMapDepthOffset));
	}

	[Fact]
	public void OpenScreenRollsBackWhenAfterScreenEnvelopeAllocationFails()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var availableFast = InvokeAvailMem(bus, MemfPublic | MemfFast);
		Assert.True(availableFast > 0x200);

		// Leave enough fast memory for the Screen prefix, but less than the
		// RasInfo envelope that follows it. The planar backing store remains in
		// chip memory, isolating the later guest-structure failure.
		var consume = checked((uint)availableFast - 0x168u);
		Assert.NotEqual(0u, InvokeAllocMem(bus, consume, MemfPublic | MemfFast));
		Assert.InRange(InvokeAvailMem(bus, MemfPublic | MemfFast), 0x160u, 0x16Fu);

		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198), open));

		Assert.Equal(0u, open.D[0]);
	}

	[Fact]
	public void CloseScreenLeavesAForeignActiveViewPublished()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));
		var syntheticScreen = open.D[0];
		Assert.NotEqual(0u, syntheticScreen);

		// A prepared View with no viewport/copper chain is a valid guest
		// publication while another provider assembles its display.  Loading it
		// makes the foreign view the active owner without replacing the current
		// hardware list.
		var foreignView = InvokeAllocMem(bus, 0x20, MemfPublic | MemfClear);
		var load = new M68kCpuState { A = { [1] = foreignView } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -222),
			load));
		Assert.Equal(
			foreignView,
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));

		var close = new M68kCpuState { A = { [0] = syntheticScreen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
		Assert.Equal(
			foreignView,
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));
	}

	[Fact]
	public void CloseScreenDoesNotBlankForeignViewThatReferencesSyntheticViewport()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));
		var syntheticScreen = open.D[0];
		Assert.NotEqual(0u, syntheticScreen);

		// A provider may publish a separate View while reusing a Screen-owned
		// ViewPort.  ViewPort equality alone must not transfer View ownership to
		// the synthetic compatibility session during CloseScreen.
		var viewPort = syntheticScreen + ScreenViewPortOffset;
		var foreignView = InvokeAllocMem(bus, GraphicsLayouts.ViewSize, MemfPublic | MemfClear);
		bus.WriteLong(foreignView + ViewViewPortOffset, viewPort);
		bus.WriteLong(foreignView + ViewLofCprListOffset, 0);
		bus.WriteLong(foreignView + ViewShfCprListOffset, 0);

		var load = new M68kCpuState { A = { [1] = foreignView } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -222),
			load));
		Assert.Equal(
			foreignView,
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));

		var close = new M68kCpuState { A = { [0] = syntheticScreen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
		Assert.Equal(
			foreignView,
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));
	}

	[Fact]
	public void CloseScreenRefusesWhenViewportColorMapIsReplacedByAProvider()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var tags = InvokeAllocMem(bus, 2 * 8, MemfPublic | MemfClear);
		bus.WriteLong(tags, ScreenTagColorMapEntries);
		bus.WriteLong(tags + 4, 32);
		bus.WriteLong(tags + 8, 0);
		bus.WriteLong(tags + 12, 0);

		var open = new M68kCpuState { A = { [0] = 0, [1] = tags } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -612),
			open));
		var screen = open.D[0];
		Assert.NotEqual(0u, screen);

		var viewPort = screen + ScreenViewPortOffset;
		var originalColorMap = bus.ReadLong(
			viewPort + (uint)GraphicsLayouts.ViewPortColorMap);
		Assert.NotEqual(0u, originalColorMap);
		var foreignColorMap = InvokeAllocMem(bus, GraphicsLayouts.ColorMapSize, MemfPublic | MemfClear);
		Assert.NotEqual(0u, foreignColorMap);
		bus.WriteLong(viewPort + (uint)GraphicsLayouts.ViewPortColorMap, foreignColorMap);
		var activeView = bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22);

		var close = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(0u, close.D[0]);
		Assert.Equal(
			foreignColorMap,
			bus.ReadLong(viewPort + (uint)GraphicsLayouts.ViewPortColorMap));
		Assert.Equal(
			activeView,
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));

		// Once the provider detaches its palette, the original compatibility
		// map restores the ownership chain and CloseScreen can complete.
		bus.WriteLong(viewPort + (uint)GraphicsLayouts.ViewPortColorMap, originalColorMap);
		var retry = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			retry));
		Assert.Equal(1u, retry.D[0]);
	}

	[Fact]
	public void CloseScreenRefusesWhenEmbeddedScreenBitmapPlaneIsReplacedByAProvider()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));
		var screen = open.D[0];
		Assert.NotEqual(0u, screen);

		var screenBitMap = screen + (uint)ScreenBitMapOffset;
		var plane = screenBitMap + (uint)GraphicsLayouts.BitMapPlanes;
		var originalPlane = bus.ReadLong(plane);
		var foreignPlane = InvokeAllocMem(bus, 2, MemfPublic | MemfClear);
		Assert.NotEqual(0u, foreignPlane);
		bus.WriteLong(plane, foreignPlane);
		var activeView = bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22);

		var close = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(0u, close.D[0]);
		Assert.Equal(foreignPlane, bus.ReadLong(plane));
		Assert.Equal(
			activeView,
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));

		// Restoring the embedded plane link re-establishes compatibility
		// ownership and lets the same Screen close normally.
		bus.WriteLong(plane, originalPlane);
		var retry = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			retry));
		Assert.Equal(1u, retry.D[0]);
	}

	[Fact]
	public void CloseScreenRefusesWhenViewportHasALinkedTail()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));
		var screen = open.D[0];
		var viewPort = screen + ScreenViewPortOffset;
		var foreignViewPort = InvokeAllocMem(bus, GraphicsLayouts.ViewPortSize, MemfPublic | MemfClear);
		Assert.NotEqual(0u, foreignViewPort);
		bus.WriteLong(viewPort + GraphicsLayouts.ViewPortNext, foreignViewPort);
		var activeView = bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22);

		var close = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(0u, close.D[0]);
		Assert.Equal(foreignViewPort, bus.ReadLong(viewPort + GraphicsLayouts.ViewPortNext));
		Assert.Equal(activeView, bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));
	}

	[Fact]
	public void CloseScreenRefusesWhenViewportLosesItsRasInfoLink()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));
		var screen = open.D[0];
		var viewPort = screen + ScreenViewPortOffset;
		var foreignRasInfo = InvokeAllocMem(bus, 0x10, MemfPublic | MemfClear);
		Assert.NotEqual(0u, foreignRasInfo);
		bus.WriteLong(viewPort + ViewPortRasInfoOffset, foreignRasInfo);
		var activeView = bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22);

		var close = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(0u, close.D[0]);
		Assert.Equal(foreignRasInfo, bus.ReadLong(viewPort + ViewPortRasInfoOffset));
		Assert.Equal(activeView, bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));
	}

	[Fact]
	public void CloseScreenRefusesWhenRasInfoHasAProviderTail()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));
		var screen = open.D[0];
		var viewPort = screen + ScreenViewPortOffset;
		var rasInfo = bus.ReadLong(viewPort + ViewPortRasInfoOffset);
		var foreignTail = InvokeAllocMem(bus, 0x10, MemfPublic | MemfClear);
		Assert.NotEqual(0u, rasInfo);
		Assert.NotEqual(0u, foreignTail);
		bus.WriteLong(rasInfo, foreignTail);
		var activeView = bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22);

		var close = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(0u, close.D[0]);
		Assert.Equal(foreignTail, bus.ReadLong(rasInfo));
		Assert.Equal(activeView, bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));
	}

	[Fact]
	public void CloseDualPlayfieldScreenRefusesWhenSecondRasInfoHasProviderTail()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var newScreen = InvokeAllocMem(bus, 0x20, 0);
		bus.WriteWord(newScreen + NewScreenWidthOffset, 320);
		bus.WriteWord(newScreen + NewScreenHeightOffset, 200);
		bus.WriteByte(newScreen + NewScreenDepthOffset, 2, 0);
		bus.WriteWord(
			newScreen + NewScreenViewModesOffset,
			(ushort)(GraphicsModeIds.DualPlayfieldMode | GraphicsModeIds.PlayfieldBitAssignment));

		var open = new M68kCpuState { A = { [0] = newScreen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));
		var screen = open.D[0];
		var viewPort = screen + ScreenViewPortOffset;
		var firstRasInfo = bus.ReadLong(viewPort + ViewPortRasInfoOffset);
		var secondRasInfo = bus.ReadLong(firstRasInfo + 0);
		var foreignTail = InvokeAllocMem(bus, 0x10, MemfPublic | MemfClear);
		Assert.NotEqual(0u, secondRasInfo);
		Assert.NotEqual(0u, foreignTail);
		bus.WriteLong(secondRasInfo + 0, foreignTail);
		var activeView = bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22);

		var close = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(0u, close.D[0]);
		Assert.Equal(foreignTail, bus.ReadLong(secondRasInfo + 0));
		Assert.Equal(activeView, bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));

		bus.WriteLong(secondRasInfo + 0, 0);
		var retry = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			retry));
		Assert.Equal(1u, retry.D[0]);
	}

	[Fact]
	public void CloseScreenRefusesWhenSyntheticViewHasAMappedForeignCopperLink()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));
		var screen = open.D[0];
		var view = bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22);
		Assert.NotEqual(0u, view);
		var originalLof = bus.ReadLong(view + ViewLofCprListOffset);
		var foreignCpr = InvokeAllocMem(bus, 0x10, MemfPublic | MemfClear);
		Assert.NotEqual(0u, foreignCpr);
		bus.WriteLong(view + ViewLofCprListOffset, foreignCpr);
		var activeView = bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22);

		var close = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(0u, close.D[0]);
		Assert.Equal(foreignCpr, bus.ReadLong(view + ViewLofCprListOffset));
		Assert.Equal(activeView, bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));

		bus.WriteLong(view + ViewLofCprListOffset, originalLof);
		var retry = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			retry));
		Assert.Equal(1u, retry.D[0]);
	}

	[Fact]
	public void CloseScreenPreservesDirectForeignActiViewPublication()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));
		var screen = open.D[0];
		var foreignView = InvokeAllocMem(bus, GraphicsLayouts.ViewSize, MemfPublic | MemfClear);
		Assert.NotEqual(0u, foreignView);

		// Model a native/provider handoff that publishes ActiView directly at
		// the guest GfxBase boundary instead of entering the host LoadView trap.
		bus.WriteLong(
			AmigaKickstartHost.GraphicsLibraryBase + 0x22,
			foreignView);

		var close = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
		Assert.Equal(
			foreignView,
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));
	}

	[Fact]
	public void CloseScreenRefusesWhenSyntheticViewLosesItsViewportLink()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));
		var screen = open.D[0];
		var view = bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22);
		var viewPort = screen + ScreenViewPortOffset;
		var foreignViewPort = InvokeAllocMem(bus, GraphicsLayouts.ViewPortSize, MemfPublic | MemfClear);
		Assert.NotEqual(0u, view);
		Assert.NotEqual(0u, foreignViewPort);
		bus.WriteLong(view + ViewViewPortOffset, foreignViewPort);
		var activeView = bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22);

		var close = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(0u, close.D[0]);
		Assert.Equal(foreignViewPort, bus.ReadLong(view + ViewViewPortOffset));
		Assert.Equal(activeView, bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));

		bus.WriteLong(view + ViewViewPortOffset, viewPort);
		var retry = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			retry));
		Assert.Equal(1u, retry.D[0]);
	}

	[Fact]
	public void CloseScreenRefusesWhenRasInfoLosesItsBitmapLink()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));
		var screen = open.D[0];
		var rasInfo = bus.ReadLong(screen + ScreenViewPortOffset + ViewPortRasInfoOffset);
		var originalBitMap = bus.ReadLong(rasInfo + 4);
		var foreignBitMap = InvokeAllocMem(bus, GraphicsLayouts.BitMapPlanes + 4, MemfPublic | MemfClear);
		Assert.NotEqual(0u, rasInfo);
		Assert.NotEqual(0u, foreignBitMap);
		bus.WriteLong(rasInfo + 4, foreignBitMap);
		var activeView = bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22);

		var close = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(0u, close.D[0]);
		Assert.Equal(foreignBitMap, bus.ReadLong(rasInfo + 4));
		Assert.Equal(activeView, bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));

		bus.WriteLong(rasInfo + 4, originalBitMap);
		var retry = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			retry));
		Assert.Equal(1u, retry.D[0]);
	}

	[Fact]
	public void CloseScreenRefusesWhenScreenRastPortLosesItsBitmapLink()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));
		var screen = open.D[0];
		var rastPortBitMap = screen + ScreenRastPortOffset + RastPortBitMapOffset;
		var originalBitMap = bus.ReadLong(rastPortBitMap);
		var foreignBitMap = InvokeAllocMem(bus, GraphicsLayouts.BitMapPlanes + 4, MemfPublic | MemfClear);
		Assert.NotEqual(0u, foreignBitMap);
		bus.WriteLong(rastPortBitMap, foreignBitMap);
		var activeView = bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22);

		var close = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(0u, close.D[0]);
		Assert.Equal(foreignBitMap, bus.ReadLong(rastPortBitMap));
		Assert.Equal(activeView, bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + 0x22));

		bus.WriteLong(rastPortBitMap, originalBitMap);
		var retry = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			retry));
		Assert.Equal(1u, retry.D[0]);
	}

	[Fact]
	public void CloseScreenQueuesTheBlankingCopperListUntilTheNextFrameBoundary()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		var open = new M68kCpuState();

		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));
		var screen = open.D[0];
		Assert.NotEqual(0u, screen);
		var initialCopper = ((uint)bus.ReadWord(0x00DFF080) << 16) |
			bus.ReadWord(0x00DFF082);
		Assert.NotEqual(0u, initialCopper);

		var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
		var midFrame = frameCycles / 2;
		var close = new M68kCpuState
		{
			A = { [0] = screen },
			Cycles = midFrame
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
		Assert.Equal((ushort)(initialCopper >> 16), bus.ReadWord(0x00DFF080));
		Assert.Equal((ushort)initialCopper, bus.ReadWord(0x00DFF082));

		InvokeAdvanceSyntheticVBlankInterruptServers(boot, midFrame, frameCycles);

		Assert.Equal((ushort)0, bus.ReadWord(0x00DFF080));
		Assert.Equal((ushort)0, bus.ReadWord(0x00DFF082));
	}

	[Fact]
	public void OpenScreenPreservesHamAndEhbModesWithSixPlaneDepth()
	{
		var hamMachine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var hamBus = hamMachine.Bus;
		var hamNewScreen = InvokeAllocMem(hamBus, 0x20, 0);
		hamBus.WriteWord(hamNewScreen + NewScreenWidthOffset, 320);
		hamBus.WriteWord(hamNewScreen + NewScreenHeightOffset, 200);
		hamBus.WriteByte(hamNewScreen + NewScreenDepthOffset, 2, 0);
		hamBus.WriteWord(hamNewScreen + NewScreenViewModesOffset, ViewModeHam);
		var hamOpen = new M68kCpuState { A = { [0] = hamNewScreen } };

		Assert.True(InvokeHostTrap(
			hamBus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			hamOpen));

		var hamScreen = hamOpen.D[0];
		Assert.Equal(6, hamBus.ReadByte(hamScreen + ScreenBitMapOffset + BitMapDepthOffset));
		Assert.Equal(
			ViewModeHam,
			hamBus.ReadWord(hamScreen + ScreenViewPortOffset + ViewPortModesOffset));

		var ehbMachine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var ehbBus = ehbMachine.Bus;
		var ehbNewScreen = InvokeAllocMem(ehbBus, 0x20, 0);
		ehbBus.WriteWord(ehbNewScreen + NewScreenWidthOffset, 320);
		ehbBus.WriteWord(ehbNewScreen + NewScreenHeightOffset, 200);
		ehbBus.WriteByte(ehbNewScreen + NewScreenDepthOffset, 2, 0);
		ehbBus.WriteWord(ehbNewScreen + NewScreenViewModesOffset, ViewModeExtraHalfBrite);
		var ehbOpen = new M68kCpuState { A = { [0] = ehbNewScreen } };

		Assert.True(InvokeHostTrap(
			ehbBus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			ehbOpen));

		var ehbScreen = ehbOpen.D[0];
		Assert.Equal(6, ehbBus.ReadByte(ehbScreen + ScreenBitMapOffset + BitMapDepthOffset));
		Assert.Equal(
			ViewModeExtraHalfBrite,
			ehbBus.ReadWord(ehbScreen + ScreenViewPortOffset + ViewPortModesOffset));

		var superMachine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var superBus = superMachine.Bus;
		var superNewScreen = InvokeAllocMem(superBus, 0x20, 0);
		superBus.WriteWord(superNewScreen + NewScreenWidthOffset, 1280);
		superBus.WriteWord(superNewScreen + NewScreenHeightOffset, 200);
		superBus.WriteByte(superNewScreen + NewScreenDepthOffset, 2, 0);
		superBus.WriteWord(superNewScreen + NewScreenViewModesOffset, ViewModeSuperHires);
		var superOpen = new M68kCpuState { A = { [0] = superNewScreen } };

		Assert.True(InvokeHostTrap(
			superBus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			superOpen));

		var superScreen = superOpen.D[0];
		Assert.Equal(1280, superBus.ReadWord(superScreen + ScreenWidthOffset));
		Assert.Equal(160, superBus.ReadWord(superScreen + ScreenBitMapOffset + BitMapBytesPerRowOffset));
		Assert.Equal(
			(ushort)(ViewModeHires | ViewModeSuperHires),
			superBus.ReadWord(superScreen + ScreenViewPortOffset + ViewPortModesOffset));
	}

	[Fact]
	public void OpenScreenBuildsSharedDualPlayfieldRasInfoChain()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var newScreen = InvokeAllocMem(bus, 0x20, 0);
		var requestedModes = (ushort)(GraphicsModeIds.DualPlayfieldMode | GraphicsModeIds.PlayfieldBitAssignment);
		bus.WriteWord(newScreen + NewScreenWidthOffset, 320);
		bus.WriteWord(newScreen + NewScreenHeightOffset, 200);
		bus.WriteByte(newScreen + NewScreenDepthOffset, 2, 0);
		bus.WriteWord(newScreen + NewScreenViewModesOffset, requestedModes);
		var openScreenState = new M68kCpuState { A = { [0] = newScreen } };

		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			openScreenState));

		var screen = openScreenState.D[0];
		var bitMap = screen + ScreenBitMapOffset;
		var viewPort = screen + ScreenViewPortOffset;
		var firstRasInfo = bus.ReadLong(viewPort + ViewPortRasInfoOffset);
		var secondRasInfo = bus.ReadLong(firstRasInfo);
		Assert.NotEqual(0u, firstRasInfo);
		Assert.NotEqual(0u, secondRasInfo);
		var firstBitMap = bus.ReadLong(firstRasInfo + 4);
		Assert.NotEqual(0u, firstBitMap);
		Assert.Equal(firstBitMap, bus.ReadLong(secondRasInfo + 4));
		Assert.Equal(2, bus.ReadByte(bitMap + BitMapDepthOffset));
		Assert.Equal(requestedModes, bus.ReadWord(viewPort + ViewPortModesOffset));

		var getViewState = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -294),
			getViewState));
		var view = getViewState.D[0];
		var cprList = bus.ReadLong(view + ViewLofCprListOffset);
		var copperList = bus.ReadLong(cprList + CprListStartOffset);
		Assert.NotEqual(0u, copperList);
		Assert.Equal((ushort)0x2400, ReadCopperMoveValue(bus, copperList, 0x0100));
		Assert.Equal((ushort)0x0040, ReadCopperMoveValue(bus, copperList, 0x0104));
	}

	[Fact]
	public void A1200OpenScreenAdmitsSharedEightPlaneDualPlayfieldAndSelectsColor16()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A1200AgaPal)
			.WithKickstart(KickstartConfiguration.FromRomImage(
				KickstartVersion.Kickstart30,
				new byte[512 * 1024]))
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		var newScreen = InvokeAllocMem(bus, 0x20, 0);
		bus.WriteWord(newScreen + NewScreenWidthOffset, 320);
		bus.WriteWord(newScreen + NewScreenHeightOffset, 200);
		bus.WriteByte(newScreen + NewScreenDepthOffset, 8, 0);
		bus.WriteWord(newScreen + NewScreenViewModesOffset, GraphicsModeIds.DualPlayfieldMode);
		var open = new M68kCpuState { A = { [0] = newScreen } };

		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198), open));
		var screen = open.D[0];
		Assert.NotEqual(0u, screen);
		Assert.Equal((byte)8, bus.ReadByte(screen + ScreenBitMapOffset + BitMapDepthOffset));
		var viewPort = screen + ScreenViewPortOffset;
		var firstRasInfo = bus.ReadLong(viewPort + ViewPortRasInfoOffset);
		Assert.NotEqual(0u, firstRasInfo);
		Assert.Equal(
			bus.ReadLong(firstRasInfo + 4),
			bus.ReadLong(bus.ReadLong(firstRasInfo) + 4));
		var copper = bus.ReadLong(viewPort + ViewPortDspInsOffset);
		Assert.Equal((ushort)0x0410, ReadCopperMoveValue(bus, copper, 0x0100));

		var sawColor16Offset = false;
		var sawColor16LowNibbles = false;
		for (var offset = 0u; offset < 0x1000; offset += 4)
		{
			var register = bus.ReadWord(copper + offset);
			var value = bus.ReadWord(copper + offset + 2);
			if (register == 0xFFFF && value == 0xFFFE)
				break;
			if (register == 0x0106 && value == 0x1000)
				sawColor16Offset = true;
			if (register == 0x0106 && value == 0x1200)
				sawColor16LowNibbles = true;
		}

		Assert.True(sawColor16Offset);
		Assert.True(sawColor16LowNibbles);
	}

	[Fact]
	public void CloseDualPlayfieldScreenDoesNotLeakItsSecondRasInfoIntoTheNextOpen()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var dualNewScreen = InvokeAllocMem(bus, 0x20, 0);
		bus.WriteWord(dualNewScreen + NewScreenWidthOffset, 320);
		bus.WriteWord(dualNewScreen + NewScreenHeightOffset, 200);
		bus.WriteByte(dualNewScreen + NewScreenDepthOffset, 2, 0);
		bus.WriteWord(
			dualNewScreen + NewScreenViewModesOffset,
			(ushort)(GraphicsModeIds.DualPlayfieldMode | GraphicsModeIds.PlayfieldBitAssignment));

		var dualOpen = new M68kCpuState { A = { [0] = dualNewScreen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			dualOpen));
		var dualScreen = dualOpen.D[0];
		var dualViewPort = dualScreen + ScreenViewPortOffset;
		var firstRasInfo = bus.ReadLong(dualViewPort + ViewPortRasInfoOffset);
		Assert.NotEqual(0u, firstRasInfo);
		Assert.NotEqual(0u, bus.ReadLong(firstRasInfo + 0));

		var close = new M68kCpuState { A = { [0] = dualScreen }, Cycles = 321 };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);

		var singleNewScreen = InvokeAllocMem(bus, 0x20, 0);
		bus.WriteWord(singleNewScreen + NewScreenWidthOffset, 320);
		bus.WriteWord(singleNewScreen + NewScreenHeightOffset, 200);
		bus.WriteByte(singleNewScreen + NewScreenDepthOffset, 2, 0);
		bus.WriteWord(singleNewScreen + NewScreenViewModesOffset, 0);

		var singleOpen = new M68kCpuState { A = { [0] = singleNewScreen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			singleOpen));
		var singleViewPort = singleOpen.D[0] + ScreenViewPortOffset;
		var singleRasInfo = bus.ReadLong(singleViewPort + ViewPortRasInfoOffset);
		Assert.NotEqual(0u, singleRasInfo);
		Assert.Equal(0u, bus.ReadLong(singleRasInfo + 0));
	}

	[Fact]
	public void OpenScreenPreservesInterlaceGeometryAndCopperMode()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var newScreen = InvokeAllocMem(bus, 0x20, 0);
		bus.WriteWord(newScreen + NewScreenWidthOffset, 320);
		bus.WriteWord(newScreen + NewScreenHeightOffset, 512);
		bus.WriteByte(newScreen + NewScreenDepthOffset, 2, 0);
		bus.WriteWord(newScreen + NewScreenViewModesOffset, ViewModeInterlace);
		var openScreenState = new M68kCpuState { A = { [0] = newScreen } };

		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			openScreenState));

		var screen = openScreenState.D[0];
		var viewPort = screen + ScreenViewPortOffset;
		Assert.Equal(512, bus.ReadWord(screen + ScreenHeightOffset));
		Assert.Equal(512, bus.ReadWord(screen + ScreenBitMapOffset + BitMapRowsOffset));
		Assert.Equal(ViewModeInterlace, bus.ReadWord(viewPort + ViewPortModesOffset));

		var getViewState = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -294),
			getViewState));
		var view = getViewState.D[0];
		var cprList = bus.ReadLong(view + ViewLofCprListOffset);
		var copperList = bus.ReadLong(cprList + CprListStartOffset);
		Assert.Equal((ushort)0x2004, ReadCopperMoveValue(bus, copperList, 0x0100));
	}

	[Fact]
	public void AddGListRefusesReadOnlyWindowFirstGadgetBeforePublication()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var openScreen = new M68kCpuState();
		var openWindow = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			openScreen));
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -204),
			openWindow));

		var window = openWindow.D[0];
		var gadget = InvokeAllocMem(bus, 0x40, 0);
		bus.WriteLong(gadget + GadgetNextOffset, 0);
		bus.WriteWord(gadget + GadgetLeftEdgeOffset, 40);
		bus.WriteWord(gadget + GadgetTopEdgeOffset, 50);
		bus.WriteWord(gadget + GadgetWidthOffset, 80);
		bus.WriteWord(gadget + GadgetHeightOffset, 16);

		const uint previousGadget = 0xA5A5_5A5Au;
		var firstGadget = window + GraphicsLayouts.WindowFirstGadget;
		bus.WriteLong(firstGadget, previousGadget);
		bus.MapReadOnlyMemory(firstGadget, new byte[]
		{
			0xA5, 0xA5, 0x5A, 0x5A
		});
		var addGList = new M68kCpuState
		{
			A = { [0] = window, [1] = gadget },
			D = { [0] = 0xCAFE_BABEu }
		};

		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -438),
			addGList));
		Assert.Equal(0xCAFE_BABEu, addGList.D[0]);
		Assert.Equal(previousGadget, bus.ReadLong(firstGadget));

		var closeWindow = new M68kCpuState { A = { [0] = window } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -72),
			closeWindow));
		var closeScreen = new M68kCpuState { A = { [0] = openScreen.D[0] } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			closeScreen));
		Assert.Equal(1u, closeScreen.D[0]);
	}

	[Fact]
	public void DrawRefusesReadOnlyCursorBeforeFallbackRasterPublication()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var openScreen = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			openScreen));

		var screen = openScreen.D[0];
		var rastPort = screen + ScreenRastPortOffset;
		var bitMap = bus.ReadLong(rastPort + RastPortBitMapOffset);
		var plane = bus.ReadLong(bitMap + GraphicsLayouts.BitMapPlanes);
		var originalPlaneByte = bus.ReadByte(plane);
		var currentX = rastPort + (uint)GraphicsLayouts.RastPortCurrentX;
		bus.MapReadOnlyMemory(currentX, new byte[] { 0x00, 0x00 });

		var draw = new M68kCpuState
		{
			A = { [1] = rastPort },
			D = { [0] = 3, [1] = 0xCAFE_BABEu }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -246),
			draw));

		Assert.Equal(3u, draw.D[0]);
		Assert.Equal((ushort)0, bus.ReadWord(currentX));
		Assert.Equal(originalPlaneByte, bus.ReadByte(plane));

		var close = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
	}

	[Fact]
	public void TextRefusesReadOnlyCursorBeforeFallbackRasterPublication()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var openScreen = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			openScreen));

		var screen = openScreen.D[0];
		var rastPort = screen + ScreenRastPortOffset;
		var bitMap = bus.ReadLong(rastPort + RastPortBitMapOffset);
		var plane = bus.ReadLong(bitMap + GraphicsLayouts.BitMapPlanes);
		var originalPlaneByte = bus.ReadByte(plane);
		var text = InvokeAllocMem(bus, 1, 0);
		bus.WriteByte(text, (byte)'A', 0);
		var currentX = rastPort + (uint)GraphicsLayouts.RastPortCurrentX;
		bus.MapReadOnlyMemory(currentX, new byte[] { 0x00, 0x00 });

		var drawText = new M68kCpuState
		{
			A = { [0] = text, [1] = rastPort },
			D = { [0] = 1, [1] = 0xFACE_CAFEu }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -60),
			drawText));

		Assert.Equal(1u, drawText.D[0]);
		Assert.Equal((ushort)0, bus.ReadWord(currentX));
		Assert.Equal(originalPlaneByte, bus.ReadByte(plane));

		var close = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -66),
			close));
		Assert.Equal(1u, close.D[0]);
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
	public void EmptySyntheticWaitPortUsesTheSelectedNtscFrameBoundary()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500PlusEcsNtsc)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		var waitPort = Lvo(AmigaKickstartHost.ExecLibraryBase, -384);
		const long cycle = 1234;
		var expected = bus.GetNextFrameStartCycle(cycle);
		var palBoundary = ((cycle / AmigaConstants.A500PalCpuCyclesPerFrame) + 1) *
			AmigaConstants.A500PalCpuCyclesPerFrame;
		var state = new M68kCpuState
		{
			LastInstructionProgramCounter = waitPort,
			ProgramCounter = waitPort + 4,
			Cycles = cycle
		};

		Assert.True(InvokeHostTrap(bus, waitPort, state));

		Assert.Equal(0u, state.D[0]);
		Assert.Equal(waitPort, state.ProgramCounter);
		Assert.Equal(expected, state.Cycles);
		Assert.NotEqual(palBoundary, state.Cycles);
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
	public void ExecAddIntServerTicksSyntheticVBlankAtNtscBeamBoundaries()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500PlusEcsNtsc)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		var interrupt = InvokeAllocMem(bus, 0x20, 0);
		var counter = InvokeAllocMem(bus, 4, 0);
		bus.WriteLong(interrupt + InterruptDataOffset, counter);
		bus.WriteLong(interrupt + InterruptCodeOffset, 0x0000_2000);
		var addState = new M68kCpuState
		{
			D = { [0] = VBlankInterruptNumber },
			A = { [1] = interrupt }
		};

		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -168), addState));
		var firstFrame = bus.GetNextFrameStartCycle(0);
		var thirdFrame = bus.GetNextFrameStartCycle(
			bus.GetNextFrameStartCycle(firstFrame));
		Assert.Equal(firstFrame, InvokeGetNextSyntheticVBlankBoundaryCycle(boot, 0, thirdFrame + 42));

		InvokeAdvanceSyntheticVBlankInterruptServers(boot, 0, thirdFrame + 42);

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
	public void ModifyIdcmpRefusesReadOnlyWindowFlagsBeforePublication()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var openScreen = new M68kCpuState();
		var openWindow = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			openScreen));
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -204),
			openWindow));

		var window = openWindow.D[0];
		var originalFlags = bus.ReadLong(window + WindowIdcmpFlagsOffset);
		var originalUserPort = bus.ReadLong(window + WindowUserPortOffset);
		bus.MapReadOnlyMemory(
			window + WindowIdcmpFlagsOffset,
			new byte[]
			{
				(byte)(originalFlags >> 24),
				(byte)(originalFlags >> 16),
				(byte)(originalFlags >> 8),
				(byte)originalFlags
			});

		var declined = new M68kCpuState
		{
			A = { [0] = window },
			D = { [0] = 0x1357_9BDFu }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -150),
			declined));
		Assert.Equal(0x1357_9BDFu, declined.D[0]);
		Assert.Equal(originalFlags, bus.ReadLong(window + WindowIdcmpFlagsOffset));
		Assert.Equal(originalUserPort, bus.ReadLong(window + WindowUserPortOffset));
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

		var freeViewPortState = new M68kCpuState();
		freeViewPortState.A[0] = viewPort;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -540), freeViewPortState));
		Assert.Equal(0u, freeViewPortState.D[0]);
		Assert.Equal(0u, bus.ReadLong(viewPort + ViewPortDspInsOffset));

		// FreeVPortCopLists owns the viewport's intermediate list, while the
		// View's hardware cprlist remains an explicit FreeCprList operation.
		var freeCprState = new M68kCpuState();
		freeCprState.A[0] = lofCprList;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -564), freeCprState));
		Assert.Equal(0u, freeCprState.D[0]);
	}

	[Fact]
	public void MakeVPortRebuildsANonHeadViewportWithoutRetargetingTheView()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint firstViewPort = 0x2300;
		const uint secondViewPort = 0x2600;

		WriteMinimalViewPort(bus, firstViewPort);
		WriteMinimalViewPort(bus, secondViewPort);
		bus.WriteLong(view + ViewViewPortOffset, firstViewPort);
		bus.WriteLong(firstViewPort, secondViewPort);
		bus.WriteLong(secondViewPort, 0);

		var state = new M68kCpuState { A = { [0] = view, [1] = firstViewPort } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD8), state));
		var publishedLof = bus.ReadLong(view + ViewLofCprListOffset);
		var publishedShf = bus.ReadLong(view + ViewShfCprListOffset);
		var firstDisplayInstructions = bus.ReadLong(firstViewPort + ViewPortDspInsOffset);
		Assert.NotEqual(0u, publishedLof);
		Assert.Equal(publishedLof, publishedShf);

		state = new M68kCpuState { A = { [0] = view, [1] = secondViewPort } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD8), state));

		Assert.Equal(firstViewPort, bus.ReadLong(view + ViewViewPortOffset));
		Assert.Equal(publishedLof, bus.ReadLong(view + ViewLofCprListOffset));
		Assert.Equal(publishedShf, bus.ReadLong(view + ViewShfCprListOffset));
		Assert.Equal(firstDisplayInstructions, bus.ReadLong(firstViewPort + ViewPortDspInsOffset));
		Assert.NotEqual(0u, bus.ReadLong(secondViewPort + ViewPortDspInsOffset));
	}

	[Fact]
	public void MakeVPortKeepsThePreviousCopperWrapperWhenReplacementAllocationFails()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false)
			.WithRealFastRam(0x20_0000));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint viewPort = 0x2300;
		WriteMinimalViewPort(bus, viewPort);

		var state = new M68kCpuState { A = { [0] = view, [1] = viewPort } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD8), state));
		Assert.Equal(0u, state.D[0]);
		var originalDspIns = bus.ReadLong(viewPort + ViewPortDspInsOffset);
		var originalLofCprList = bus.ReadLong(view + ViewLofCprListOffset);
		Assert.NotEqual(0u, originalDspIns);
		Assert.NotEqual(0u, originalLofCprList);

		// Remove the fast-memory capability from the installed header.  This
		// leaves chip memory available for the raw display list but makes the
		// program-memory CPR wrapper allocation fail deterministically.
		var memoryList = AmigaKickstartHost.ExecLibraryBase + (uint)ExecMemListOffset;
		for (var fastHeader = bus.ReadLong(memoryList);
			 fastHeader != 0 && fastHeader != memoryList + 4;
			 fastHeader = bus.ReadLong(fastHeader))
		{
			var fastAttributes = bus.ReadWord(fastHeader + MemHeaderAttributesOffset);
			if ((fastAttributes & MemfFast) != 0)
				bus.WriteWord(fastHeader + MemHeaderAttributesOffset, (ushort)(fastAttributes & ~MemfFast));
		}
		var chipFreeBeforeRetry = InvokeAvailMem(bus, MemfPublic | MemfChip);

		state = new M68kCpuState { A = { [0] = view, [1] = viewPort } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD8), state));
		Assert.Equal(4u, state.D[0]); // MVP_NO_DISPLAY
		Assert.Equal(originalDspIns, bus.ReadLong(viewPort + ViewPortDspInsOffset));
		Assert.Equal(originalLofCprList, bus.ReadLong(view + ViewLofCprListOffset));
		Assert.Equal(chipFreeBeforeRetry, InvokeAvailMem(bus, MemfPublic | MemfChip));
	}

	[Fact]
	public void MakeVPortComposesViewAndViewPortOffsetsIntoDisplayOrigin()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint viewPort = 0x2300;
		WriteMinimalViewPort(bus, viewPort);
		bus.WriteWord(view + ViewDyOffset, 3);
		bus.WriteWord(view + ViewDxOffset, 5);
		bus.WriteWord(viewPort + 0x1C, 2);
		bus.WriteWord(viewPort + 0x1E, 4);

		var state = new M68kCpuState { A = { [0] = view, [1] = viewPort } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD8), state));

		var copperList = bus.ReadLong(bus.ReadLong(view + ViewLofCprListOffset) + CprListStartOffset);
		// InitView's standard origin is (H=$81,V=$2C); the ViewPort offsets
		// are relative to it, so (dx=5+2, dy=3+4) yields DIWSTRT=$3388.
		Assert.Equal((ushort)0x3388, ReadCopperMoveValue(bus, copperList, 0x008E));
	}

	[Fact]
	public void MakeVPortProjectsFeatureModesIntoBplcon0()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint viewPort = 0x2300;
		const uint rasInfo = 0x2380;
		const uint secondRasInfo = 0x2400;
		const uint bitMap = 0x23A0;
		const uint planeBase = 0x3000;
		bus.WriteWord(viewPort + 0x18, 16);
		bus.WriteWord(viewPort + 0x1A, 1);
		bus.WriteLong(viewPort + 0x24, rasInfo);
		// Single-playfield MakeVPort requires the primary RasInfo chain to
		// terminate. The DUALPF cases below attach the documented companion
		// immediately before switching the viewport mode.
		bus.WriteLong(rasInfo, 0);
		bus.WriteLong(rasInfo + 0x04, bitMap);
		bus.WriteLong(secondRasInfo, 0);
		bus.WriteLong(secondRasInfo + 0x04, bitMap);
		bus.WriteWord(bitMap, 2);
		bus.WriteWord(bitMap + 0x02, 1);
		bus.WriteByte(bitMap + 0x05, 6, 0);
		for (var plane = 0; plane < 6; plane++)
			bus.WriteLong(bitMap + 0x08u + (uint)(plane * 4), planeBase + (uint)(plane * 0x20));

		bus.WriteWord(viewPort + 0x20, GraphicsModeIds.HamMode);
		var state = new M68kCpuState { A = { [0] = view, [1] = viewPort } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD8), state));
		var copperList = bus.ReadLong(bus.ReadLong(view + ViewLofCprListOffset) + CprListStartOffset);
		Assert.Equal((ushort)0x6800, ReadCopperMoveValue(bus, copperList, 0x0100));

		bus.WriteLong(rasInfo, secondRasInfo);
		bus.WriteByte(bitMap + 0x05, 4, 0);
		bus.WriteWord(viewPort + 0x20, (ushort)(GraphicsModeIds.HiresMode | GraphicsModeIds.DualPlayfieldMode));
		state = new M68kCpuState { A = { [0] = view, [1] = viewPort } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD8), state));
		copperList = bus.ReadLong(bus.ReadLong(view + ViewLofCprListOffset) + CprListStartOffset);
		Assert.Equal((ushort)0xC400, ReadCopperMoveValue(bus, copperList, 0x0100));

		bus.WriteWord(viewPort + 0x20, (ushort)(GraphicsModeIds.HiresMode | GraphicsModeIds.DualPlayfieldMode | GraphicsModeIds.PlayfieldBitAssignment));
		state = new M68kCpuState { A = { [0] = view, [1] = viewPort } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD8), state));
		copperList = bus.ReadLong(bus.ReadLong(view + ViewLofCprListOffset) + CprListStartOffset);
		Assert.Equal((ushort)0xC400, ReadCopperMoveValue(bus, copperList, 0x0100));
		Assert.Equal((ushort)0x0040, ReadCopperMoveValue(bus, copperList, 0x0104));

		bus.WriteLong(rasInfo, 0);
		bus.WriteByte(bitMap + 0x05, 2, 0);
		bus.WriteWord(viewPort + 0x20, (ushort)(GraphicsModeIds.HiresMode | GraphicsModeIds.SuperHiresMode));
		state = new M68kCpuState { A = { [0] = view, [1] = viewPort } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD8), state));
		copperList = bus.ReadLong(bus.ReadLong(view + ViewLofCprListOffset) + CprListStartOffset);
		Assert.Equal((ushort)0xA040, ReadCopperMoveValue(bus, copperList, 0x0100));
		Assert.Equal((ushort)0x0000, ReadCopperMoveValue(bus, copperList, 0x0104));
	}

	[Fact]
	public void MakeVPortProjectsDistinctDualPlayfieldRasInfosIntoAllPlanePointers()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint viewPort = 0x2300;
		const uint firstRasInfo = 0x2380;
		const uint secondRasInfo = 0x2400;
		const uint firstBitMap = 0x2500;
		const uint secondBitMap = 0x2600;
		const uint colorMap = 0x2700;
		const uint colorTable = 0x2740;
		const uint firstPlane0 = 0x3000;
		const uint firstPlane1 = 0x3020;
		const uint secondPlane0 = 0x3040;
		const uint secondPlane1 = 0x3060;

		bus.WriteWord(viewPort + ViewPortDWidthOffset, 16);
		bus.WriteWord(viewPort + ViewPortDHeightOffset, 1);
		bus.WriteWord(viewPort + ViewPortModesOffset, GraphicsModeIds.DualPlayfieldMode);
		bus.WriteLong(viewPort + ViewPortRasInfoOffset, firstRasInfo);
		bus.WriteLong(viewPort + GraphicsLayouts.ViewPortColorMap, colorMap);
		bus.WriteWord(colorMap + GraphicsLayouts.ColorMapCount, 16);
		bus.WriteLong(colorMap + GraphicsLayouts.ColorMapColorTable, colorTable);
		for (var color = 0; color < 16; color++)
			bus.WriteWord(colorTable + (uint)(color * 2), (ushort)(0x010 + color));
		bus.WriteLong(firstRasInfo + 0x00, secondRasInfo);
		bus.WriteLong(firstRasInfo + 0x04, firstBitMap);
		bus.WriteWord(firstRasInfo + 0x08, 3);
		bus.WriteLong(secondRasInfo + 0x00, 0);
		bus.WriteLong(secondRasInfo + 0x04, secondBitMap);
		bus.WriteWord(secondRasInfo + 0x08, 9);

		foreach (var bitMap in new[] { firstBitMap, secondBitMap })
		{
			bus.WriteWord(bitMap + 0x00, (ushort)(bitMap == firstBitMap ? 4 : 6));
			bus.WriteWord(bitMap + 0x02, 1);
			bus.WriteByte(bitMap + 0x05, 2, 0);
		}

		bus.WriteLong(firstBitMap + 0x08, firstPlane0);
		bus.WriteLong(firstBitMap + 0x0C, firstPlane1);
		bus.WriteLong(secondBitMap + 0x08, secondPlane0);
		bus.WriteLong(secondBitMap + 0x0C, secondPlane1);

		var state = new M68kCpuState { A = { [0] = view, [1] = viewPort } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD8), state));
		Assert.Equal(0u, state.D[0]);

		var copperList = bus.ReadLong(bus.ReadLong(view + ViewLofCprListOffset) + CprListStartOffset);
		Assert.Equal((ushort)0x4400, ReadCopperMoveValue(bus, copperList, 0x0100));
		Assert.Equal((ushort)0x0093, ReadCopperMoveValue(bus, copperList, 0x0102));
		Assert.Equal((ushort)0x0002, ReadCopperMoveValue(bus, copperList, 0x0108));
		Assert.Equal((ushort)0x0004, ReadCopperMoveValue(bus, copperList, 0x010A));
		Assert.Equal((ushort)(firstPlane0 >> 16), ReadCopperMoveValue(bus, copperList, 0x00E0));
		Assert.Equal((ushort)firstPlane0, ReadCopperMoveValue(bus, copperList, 0x00E2));
		Assert.Equal((ushort)(secondPlane0 >> 16), ReadCopperMoveValue(bus, copperList, 0x00E4));
		Assert.Equal((ushort)secondPlane0, ReadCopperMoveValue(bus, copperList, 0x00E6));
		Assert.Equal((ushort)(firstPlane1 >> 16), ReadCopperMoveValue(bus, copperList, 0x00E8));
		Assert.Equal((ushort)firstPlane1, ReadCopperMoveValue(bus, copperList, 0x00EA));
		Assert.Equal((ushort)(secondPlane1 >> 16), ReadCopperMoveValue(bus, copperList, 0x00EC));
		Assert.Equal((ushort)secondPlane1, ReadCopperMoveValue(bus, copperList, 0x00EE));
		Assert.Equal((ushort)0x010, ReadCopperMoveValue(bus, copperList, 0x0180));
		Assert.Equal((ushort)0x011, ReadCopperMoveValue(bus, copperList, 0x0182));
		Assert.Equal((ushort)0x018, ReadCopperMoveValue(bus, copperList, 0x0190));
		Assert.Equal((ushort)0x019, ReadCopperMoveValue(bus, copperList, 0x0192));

		bus.WriteWord(firstRasInfo + 0x08, unchecked((ushort)-3));
		state = new M68kCpuState { A = { [0] = view, [1] = viewPort } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD8), state));
		copperList = bus.ReadLong(bus.ReadLong(view + ViewLofCprListOffset) + CprListStartOffset);
		Assert.Equal((ushort)0x009D, ReadCopperMoveValue(bus, copperList, 0x0102));
		Assert.Equal((ushort)((firstPlane0 - 2) >> 16), ReadCopperMoveValue(bus, copperList, 0x00E0));
		Assert.Equal((ushort)(firstPlane0 - 2), ReadCopperMoveValue(bus, copperList, 0x00E2));
	}

	[Fact]
	public void A1200MakeVPortProjectsFourPlusFourDualPlayfieldWithTheColor16PaletteOffset()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A1200AgaPal)
			.WithKickstart(KickstartConfiguration.FromRomImage(
				KickstartVersion.Kickstart30,
				new byte[512 * 1024]))
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		const uint chipMemory = MemfPublic | MemfChip;
		var view = InvokeAllocMem(bus, (uint)ViewStructSize, chipMemory);
		var viewPort = InvokeAllocMem(bus, (uint)GraphicsLayouts.ViewPortSize, chipMemory);
		var firstRasInfo = InvokeAllocMem(bus, 0x0C, chipMemory);
		var secondRasInfo = InvokeAllocMem(bus, 0x0C, chipMemory);
		var firstBitMap = InvokeAllocMem(bus, (uint)GraphicsLayouts.BitMapSize, chipMemory);
		var secondBitMap = InvokeAllocMem(bus, (uint)GraphicsLayouts.BitMapSize, chipMemory);
		var colorMap = InvokeAllocMem(bus, (uint)GraphicsLayouts.ColorMapSize, chipMemory);
		var colorTable = InvokeAllocMem(bus, 32 * sizeof(ushort), chipMemory);
		var firstPlanes = Enumerable.Range(0, 4)
			.Select(_ => InvokeAllocMem(bus, 2, chipMemory))
			.ToArray();
		var secondPlanes = Enumerable.Range(0, 4)
			.Select(_ => InvokeAllocMem(bus, 2, chipMemory))
			.ToArray();

		Assert.All(firstPlanes, plane => Assert.NotEqual(0u, plane));
		Assert.All(secondPlanes, plane => Assert.NotEqual(0u, plane));
		bus.WriteWord(viewPort + ViewPortDWidthOffset, 16);
		bus.WriteWord(viewPort + ViewPortDHeightOffset, 1);
		bus.WriteWord(viewPort + ViewPortModesOffset, GraphicsModeIds.DualPlayfieldMode);
		bus.WriteLong(viewPort + ViewPortRasInfoOffset, firstRasInfo);
		bus.WriteLong(viewPort + GraphicsLayouts.ViewPortColorMap, colorMap);
		bus.WriteWord(colorMap + GraphicsLayouts.ColorMapCount, 32);
		bus.WriteLong(colorMap + GraphicsLayouts.ColorMapColorTable, colorTable);
		for (var color = 0; color < 32; color++)
			bus.WriteWord(colorTable + (uint)(color * sizeof(ushort)), color == 31 ? (ushort)0x0F0 : (ushort)0);

		bus.WriteLong(firstRasInfo, secondRasInfo);
		bus.WriteLong(firstRasInfo + 0x04, firstBitMap);
		bus.WriteLong(secondRasInfo, 0);
		bus.WriteLong(secondRasInfo + 0x04, secondBitMap);
		foreach (var bitMap in new[] { firstBitMap, secondBitMap })
		{
			bus.WriteWord(bitMap + BitMapBytesPerRowOffset, 2);
			bus.WriteWord(bitMap + BitMapRowsOffset, 1);
			bus.WriteByte(bitMap + BitMapDepthOffset, 4, 0);
		}

		for (var plane = 0; plane < 4; plane++)
		{
			bus.WriteLong(firstBitMap + BitMapPlanesOffset + (uint)(plane * sizeof(uint)), firstPlanes[plane]);
			bus.WriteLong(secondBitMap + BitMapPlanesOffset + (uint)(plane * sizeof(uint)), secondPlanes[plane]);
		}

		var state = new M68kCpuState { A = { [0] = view, [1] = viewPort } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD8), state));
		Assert.Equal(0u, state.D[0]);

		var copperList = bus.ReadLong(bus.ReadLong(view + ViewLofCprListOffset) + CprListStartOffset);
		Assert.Equal((ushort)0x0410, ReadCopperMoveValue(bus, copperList, 0x0100));
		var interleavedPlanes = new[]
		{
			firstPlanes[0], secondPlanes[0], firstPlanes[1], secondPlanes[1],
			firstPlanes[2], secondPlanes[2], firstPlanes[3], secondPlanes[3]
		};
		for (var plane = 0; plane < interleavedPlanes.Length; plane++)
		{
			var register = (ushort)(0x00E0 + (plane * 4));
			Assert.Equal((ushort)(interleavedPlanes[plane] >> 16), ReadCopperMoveValue(bus, copperList, register));
			Assert.Equal((ushort)interleavedPlanes[plane], ReadCopperMoveValue(bus, copperList, (ushort)(register + 2)));
		}

		var bplcon3 = ushort.MaxValue;
		var sawColor31High = false;
		var sawColor31Low = false;
		for (var offset = 0u; offset < 0x1000; offset += 4)
		{
			var register = bus.ReadWord(copperList + offset);
			var value = bus.ReadWord(copperList + offset + 2);
			if (register == 0xFFFF && value == 0xFFFE)
				break;
			if (register == 0x0106)
			{
				bplcon3 = value;
				continue;
			}

			if (register == 0x01BE && bplcon3 == 0x1000 && value == 0x00F0)
				sawColor31High = true;
			if (register == 0x01BE && bplcon3 == 0x1200 && value == 0x0000)
				sawColor31Low = true;
		}

		Assert.True(sawColor31High);
		Assert.True(sawColor31Low);
		Assert.Equal((ushort)0x1000, bplcon3);
	}

	[Fact]
	public void MakeVPortProjectsAsymmetricDualPlayfieldDepthsWithoutGaps()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint viewPort = 0x2300;
		const uint firstRasInfo = 0x2380;
		const uint secondRasInfo = 0x2400;
		const uint firstBitMap = 0x2500;
		const uint secondBitMap = 0x2600;
		const uint firstPlane0 = 0x3000;
		const uint firstPlane1 = 0x3020;
		const uint secondPlane0 = 0x3040;

		bus.WriteWord(viewPort + ViewPortDWidthOffset, 16);
		bus.WriteWord(viewPort + ViewPortDHeightOffset, 2);
		bus.WriteWord(viewPort + ViewPortModesOffset, GraphicsModeIds.DualPlayfieldMode);
		bus.WriteLong(viewPort + ViewPortRasInfoOffset, firstRasInfo);
		bus.WriteLong(firstRasInfo + 0x00, secondRasInfo);
		bus.WriteLong(firstRasInfo + 0x04, firstBitMap);
		bus.WriteLong(secondRasInfo + 0x00, 0);
		bus.WriteLong(secondRasInfo + 0x04, secondBitMap);

		bus.WriteWord(firstBitMap + 0x00, 2);
		bus.WriteWord(firstBitMap + 0x02, 2);
		bus.WriteByte(firstBitMap + 0x05, 2, 0);
		bus.WriteLong(firstBitMap + 0x08, firstPlane0);
		bus.WriteLong(firstBitMap + 0x0C, firstPlane1);
		bus.WriteWord(secondBitMap + 0x00, 2);
		bus.WriteWord(secondBitMap + 0x02, 1);
		bus.WriteByte(secondBitMap + 0x05, 1, 0);
		bus.WriteLong(secondBitMap + 0x08, secondPlane0);

		var state = new M68kCpuState { A = { [0] = view, [1] = viewPort } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD8), state));
		Assert.Equal(0u, state.D[0]);

		var copperList = bus.ReadLong(bus.ReadLong(view + ViewLofCprListOffset) + CprListStartOffset);
		Assert.Equal((ushort)0x2D00, ReadCopperMoveValue(bus, copperList, 0x0090));
		Assert.Equal((ushort)0x3400, ReadCopperMoveValue(bus, copperList, 0x0100));
		Assert.Equal((ushort)(firstPlane0 >> 16), ReadCopperMoveValue(bus, copperList, 0x00E0));
		Assert.Equal((ushort)firstPlane0, ReadCopperMoveValue(bus, copperList, 0x00E2));
		Assert.Equal((ushort)(secondPlane0 >> 16), ReadCopperMoveValue(bus, copperList, 0x00E4));
		Assert.Equal((ushort)secondPlane0, ReadCopperMoveValue(bus, copperList, 0x00E6));
		Assert.Equal((ushort)(firstPlane1 >> 16), ReadCopperMoveValue(bus, copperList, 0x00E8));
		Assert.Equal((ushort)firstPlane1, ReadCopperMoveValue(bus, copperList, 0x00EA));
	}

	[Fact]
	public void MakeVPortProjectsGuestColorMapIntoCopperPalette()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint viewPort = 0x2300;
		const uint colorMap = 0x2500;
		const uint colorTable = 0x2540;
		WriteMinimalViewPort(bus, viewPort);

		bus.WriteLong(viewPort + (uint)GraphicsLayouts.ViewPortColorMap, colorMap);
		bus.WriteWord(colorMap + (uint)GraphicsLayouts.ColorMapCount, 2);
		bus.WriteLong(colorMap + (uint)GraphicsLayouts.ColorMapColorTable, colorTable);
		bus.WriteWord(colorTable, 0x0123);
		bus.WriteWord(colorTable + 2, 0x0A5);

		var state = new M68kCpuState { A = { [0] = view, [1] = viewPort } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD8), state));

		var copperList = bus.ReadLong(bus.ReadLong(view + ViewLofCprListOffset) + CprListStartOffset);
		Assert.Equal((ushort)0x0123, ReadCopperMoveValue(bus, copperList, 0x0180));
		Assert.Equal((ushort)0x00A5, ReadCopperMoveValue(bus, copperList, 0x0182));
	}

	[Fact]
	public void SetRgb4RefreshesTheActiveGuestColorMapCopperList()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint viewPort = 0x2300;
		WriteMinimalViewPort(bus, viewPort);

		var mapState = new M68kCpuState { D = { [0] = 2 } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -570), mapState));
		Assert.NotEqual(0u, mapState.D[0]);
		bus.WriteLong(viewPort + (uint)GraphicsLayouts.ViewPortColorMap, mapState.D[0]);

		var makeState = new M68kCpuState { A = { [0] = view, [1] = viewPort } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD8), makeState));
		var loadState = new M68kCpuState { A = { [1] = view } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE), loadState));

		var setState = new M68kCpuState
		{
			A = { [0] = viewPort },
			D = { [0] = 1, [1] = 0x0F, [2] = 0, [3] = 0 }
		};
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0x120), setState));

		var copperList = bus.ReadLong(bus.ReadLong(view + ViewLofCprListOffset) + CprListStartOffset);
		Assert.Equal((ushort)0x0F00, ReadCopperMoveValue(bus, copperList, 0x0182));
	}

	[Fact]
	public void MakeVPortProjectsUserCopperMoveAndWaitInstructions()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint viewPort = 0x2300;
		const uint userList = 0x2500;
		const uint copList = 0x2520;
		const uint instructions = 0x2560;
		WriteMinimalViewPort(bus, viewPort);

		bus.WriteLong(viewPort + (uint)GraphicsLayouts.ViewPortUCopIns, userList);
		bus.WriteLong(userList + (uint)GraphicsLayouts.UCopListFirstCopList, copList);
		bus.WriteLong(userList + (uint)GraphicsLayouts.UCopListCopList, copList);
		bus.WriteLong(userList + (uint)GraphicsLayouts.UCopListNext, 0);
		bus.WriteLong(copList + (uint)GraphicsLayouts.CopListCopIns, instructions);
		bus.WriteLong(copList + (uint)GraphicsLayouts.CopListNext, 0);
		bus.WriteWord(copList + (uint)GraphicsLayouts.CopListCount, 2);
		bus.WriteWord(copList + (uint)GraphicsLayouts.CopListMaxCount, 2);
		bus.WriteWord(instructions + (uint)GraphicsLayouts.CopInsOpCode, 0);
		bus.WriteWord(instructions + (uint)GraphicsLayouts.CopInsArg0, 0x0180);
		bus.WriteWord(instructions + (uint)GraphicsLayouts.CopInsArg1, 0x00F0);
		bus.WriteWord(instructions + (uint)GraphicsLayouts.CopInsSize + (uint)GraphicsLayouts.CopInsOpCode, 1);
		bus.WriteWord(instructions + (uint)GraphicsLayouts.CopInsSize + (uint)GraphicsLayouts.CopInsArg0, 42);
		bus.WriteWord(instructions + (uint)GraphicsLayouts.CopInsSize + (uint)GraphicsLayouts.CopInsArg1, 12);

		var state = new M68kCpuState { A = { [0] = view, [1] = viewPort } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD8), state));

		var copperList = bus.ReadLong(bus.ReadLong(view + ViewLofCprListOffset) + CprListStartOffset);
		var foundMove = false;
		var foundWait = false;
		for (var offset = 0u; offset < 0x100; offset += 4)
		{
			var first = bus.ReadWord(copperList + offset);
			var second = bus.ReadWord(copperList + offset + 2);
			if (first == 0xFFFF && second == 0xFFFE)
				break;

			if (first == 0x0180 && second == 0x00F0)
				foundMove = true;
			if (first == (ushort)((42 << 8) | 12) && second == 0xFFFE)
				foundWait = true;
		}

		Assert.True(foundMove);
		Assert.True(foundWait);
	}

	[Fact]
	public void MakeVPortRejectsAHighUserCopperWrapperBeforeReplacingCopper()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint viewPort = 0x2300;
		const uint wrappedUserList = 0xFFFF_FFFCu;
		const uint copList = 0x2520;
		const uint instructions = 0x2560;
		WriteMinimalViewPort(bus, viewPort);
		bus.WriteLong(view + ViewViewPortOffset, viewPort);

		var first = new M68kCpuState { A = { [0] = view, [1] = viewPort } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD8), first));
		Assert.Equal(0u, first.D[0]);
		var originalDspIns = bus.ReadLong(viewPort + ViewPortDspInsOffset);
		var originalLofCprList = bus.ReadLong(view + ViewLofCprListOffset);

		// The old host walker would read FirstCopList and CopList through the
		// low aliases at 0 and 4 after adding fields to the high wrapper.
		bus.MapWritableMemory(wrappedUserList, new byte[4]);
		bus.WriteLong(wrappedUserList, 0);
		bus.WriteLong(0, copList);
		bus.WriteLong(4, copList);
		bus.WriteLong(copList + (uint)GraphicsLayouts.CopListNext, 0);
		bus.WriteLong(copList + (uint)GraphicsLayouts.CopListCopIns, instructions);
		bus.WriteWord(copList + (uint)GraphicsLayouts.CopListCount, 1);
		bus.WriteWord(copList + (uint)GraphicsLayouts.CopListMaxCount, 1);
		bus.WriteWord(instructions + (uint)GraphicsLayouts.CopInsOpCode, 0);
		bus.WriteWord(instructions + (uint)GraphicsLayouts.CopInsArg0, 0x0180);
		bus.WriteWord(instructions + (uint)GraphicsLayouts.CopInsArg1, 0x00F0);
		bus.WriteLong(viewPort + (uint)GraphicsLayouts.ViewPortUCopIns, wrappedUserList);

		var failed = new M68kCpuState { A = { [0] = view, [1] = viewPort } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD8), failed));
		Assert.Equal(4u, failed.D[0]); // MVP_NO_DISPLAY
		Assert.Equal(originalDspIns, bus.ReadLong(viewPort + ViewPortDspInsOffset));
		Assert.Equal(originalLofCprList, bus.ReadLong(view + ViewLofCprListOffset));
	}

	[Fact]
	public void MakeVPortDoesNotReadAWrappedColorMapFieldThroughLowMemory()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint viewPort = 0x2300;
		const uint wrappedColorMap = 0xFFFF_FFFCu;
		const uint colorTable = 0x3200;
		WriteMinimalViewPort(bus, viewPort);
		bus.WriteLong(view + ViewViewPortOffset, viewPort);

		// ColorMap.ColorTable is at +4.  Populate the wrapped low alias with a
		// vivid color so an unchecked host read is observable in the copper list.
		bus.MapWritableMemory(wrappedColorMap, new byte[4]);
		bus.WriteWord(wrappedColorMap + (uint)GraphicsLayouts.ColorMapCount, 1);
		bus.WriteLong(0, colorTable);
		bus.WriteWord(colorTable, 0x0ABC);
		bus.WriteLong(viewPort + (uint)GraphicsLayouts.ViewPortColorMap, wrappedColorMap);

		var state = new M68kCpuState { A = { [0] = view, [1] = viewPort } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD8), state));
		Assert.Equal(0u, state.D[0]);

		var copperList = bus.ReadLong(
			bus.ReadLong(view + ViewLofCprListOffset) + CprListStartOffset);
		// A malformed present ColorMap remains on the compatibility fallback:
		// COLOR0 is black, not the low-alias 0x0ABC entry.
		Assert.Equal((ushort)0x000, ReadCopperMoveValue(bus, copperList, 0x0180));
	}

	[Fact]
	public void MakeVPortRejectsMalformedOrOverCapacityUserCopperLists()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint viewPort = 0x2300;
		const uint userList = 0x2500;
		const uint copList = 0x2520;
		const uint instructions = 0x2560;
		WriteMinimalViewPort(bus, viewPort);
		bus.WriteLong(viewPort + (uint)GraphicsLayouts.ViewPortUCopIns, userList);
		bus.WriteLong(userList + (uint)GraphicsLayouts.UCopListFirstCopList, copList);
		bus.WriteLong(userList + (uint)GraphicsLayouts.UCopListCopList, copList);
		bus.WriteLong(userList + (uint)GraphicsLayouts.UCopListNext, 0);
		bus.WriteLong(copList + (uint)GraphicsLayouts.CopListCopIns, instructions);
		bus.WriteWord(copList + (uint)GraphicsLayouts.CopListCount, 1);
		bus.WriteWord(copList + (uint)GraphicsLayouts.CopListMaxCount, 1);
		bus.WriteWord(instructions + (uint)GraphicsLayouts.CopInsOpCode, 2);
		bus.WriteLong(copList + (uint)GraphicsLayouts.CopListNext, copList);
		const uint lofSentinel = 0xDEAD_BEEFu;
		bus.WriteLong(view + ViewLofCprListOffset, lofSentinel);

		var state = new M68kCpuState { A = { [0] = view, [1] = viewPort } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD8), state));
		Assert.Equal(4u, state.D[0]); // MVP_NO_DISPLAY
		Assert.Equal(lofSentinel, bus.ReadLong(view + ViewLofCprListOffset));

		// A syntactically valid list that cannot fit in the fixed compatibility
		// stream is rejected just like a malformed list.
		bus.WriteLong(copList + (uint)GraphicsLayouts.CopListNext, 0);
		// The host compatibility span is 0x400 bytes.  Keep this regression
		// genuinely over-capacity after widening that span: 300 six-byte guest
		// instructions expand past the generated display program plus terminator.
		const ushort overCapacityInstructionCount = 300;
		bus.WriteWord(copList + (uint)GraphicsLayouts.CopListCount, overCapacityInstructionCount);
		bus.WriteWord(copList + (uint)GraphicsLayouts.CopListMaxCount, overCapacityInstructionCount);
		for (var index = 0; index < overCapacityInstructionCount; index++)
		{
			var instruction = instructions + (uint)(index * GraphicsLayouts.CopInsSize);
			bus.WriteWord(instruction + (uint)GraphicsLayouts.CopInsOpCode, 0);
			bus.WriteWord(instruction + (uint)GraphicsLayouts.CopInsArg0, 0);
			bus.WriteWord(instruction + (uint)GraphicsLayouts.CopInsArg1, 0);
		}

		state = new M68kCpuState { A = { [0] = view, [1] = viewPort } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD8), state));
		Assert.Equal(4u, state.D[0]); // MVP_NO_DISPLAY
		Assert.Equal(lofSentinel, bus.ReadLong(view + ViewLofCprListOffset));
	}

	[Fact]
	public void MrgCopBuildsEveryLinkedViewPortAndKeepsTheFirstListActive()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint firstViewPort = 0x2300;
		const uint secondViewPort = 0x2400;
		const uint secondRasInfo = 0x2480;
		const uint secondBitMap = 0x24A0;
		const uint secondPlane = 0x24C0;

		WriteMinimalViewPort(bus, firstViewPort);
		bus.WriteLong(firstViewPort, secondViewPort);
		bus.WriteWord(secondViewPort + 0x18, 16);
		bus.WriteWord(secondViewPort + 0x1A, 1);
		bus.WriteLong(secondViewPort + 0x24, secondRasInfo);
		bus.WriteLong(secondRasInfo + 0x04, secondBitMap);
		bus.WriteWord(secondBitMap, 2);
		bus.WriteWord(secondBitMap + 0x02, 1);
		bus.WriteByte(secondBitMap + 0x05, 1, 0);
		bus.WriteLong(secondBitMap + 0x08, secondPlane);
		bus.WriteWord(secondPlane, 0x4000);
		bus.WriteLong(view + ViewViewPortOffset, firstViewPort);

		var state = new M68kCpuState { A = { [1] = view } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD2), state));
		Assert.Equal(0u, state.D[0]);

		var firstDspIns = bus.ReadLong(firstViewPort + ViewPortDspInsOffset);
		var secondDspIns = bus.ReadLong(secondViewPort + ViewPortDspInsOffset);
		Assert.NotEqual(0u, firstDspIns);
		Assert.NotEqual(0u, secondDspIns);
		Assert.NotEqual(firstDspIns, secondDspIns);
		Assert.Equal(firstViewPort, bus.ReadLong(view + ViewViewPortOffset));

		var activeCpr = bus.ReadLong(view + ViewLofCprListOffset);
		Assert.NotEqual(0u, activeCpr);
		Assert.Equal(firstDspIns, bus.ReadLong(activeCpr + CprListStartOffset));
		Assert.Equal(activeCpr, bus.ReadLong(view + ViewShfCprListOffset));

		// MrgCop's active CPR must contain the linked viewport's display stream,
		// not only the first viewport's setup.  The compatibility merger inserts
		// a vertical wait before the second viewport and preserves its BPL pointer.
		var mergedCopper = bus.ReadLong(activeCpr + CprListStartOffset);
		var sawSecondPlane = false;
		var sawSecondViewportWait = false;
		for (var offset = 0u; offset < 0x100; offset += 4)
		{
			var first = bus.ReadWord(mergedCopper + offset);
			var second = bus.ReadWord(mergedCopper + offset + 2);
			if (first == 0xFFFF && second == 0xFFFE)
				break;

			if ((first & 0x01FE) == 0x00E2 && second == (ushort)secondPlane)
				sawSecondPlane = true;
			if ((first & 1) == 0 && second == 0xFFFE && (first & 0xFF00) != 0)
				sawSecondViewportWait = true;
		}

		Assert.True(sawSecondPlane);
		Assert.True(sawSecondViewportWait);
	}

	[Fact]
	public void MrgCopReportsSuccessAfterRefreshingAnActiveViewPublication()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint viewPort = 0x2300;

		WriteMinimalViewPort(bus, viewPort);
		bus.WriteLong(view + ViewViewPortOffset, viewPort);

		var merge = new M68kCpuState { A = { [1] = view } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD2),
			merge));
		Assert.Equal(0u, merge.D[0]);

		var load = new M68kCpuState { A = { [1] = view } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE),
			load));
		Assert.Equal(view, bus.ReadLong(
			AmigaKickstartHost.GraphicsLibraryBase +
			(uint)GraphicsLayouts.GfxBaseActiView));

		// The second merge takes the active-view publication branch.  Its
		// result must remain the status-bearing MCOP_OK value after refreshing
		// the scheduler-facing copper handoff.
		merge = new M68kCpuState { A = { [1] = view }, D = { [0] = 0xA5A5_5A5Au } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD2),
			merge));
		Assert.Equal(0u, merge.D[0]);
		Assert.Equal(view, bus.ReadLong(
			AmigaKickstartHost.GraphicsLibraryBase +
			(uint)GraphicsLayouts.GfxBaseActiView));
	}

	[Fact]
	public void MrgCopReclaimsStaleRtgFrontSelectionOnActiveStandardView()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithRtgVram(16L * 1024 * 1024)
			.WithCpu(AmigaM68kCoreFactory.Default, M68kBackendKind.AccurateM68040)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;

		var open = new M68kCpuState();
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -198),
			open));
		var screen = open.D[0];
		Assert.NotEqual(0u, screen);
		var front = new M68kCpuState { A = { [0] = screen } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.IntuitionLibraryBase, -252),
			front));

		var view = bus.ReadLong(
			AmigaKickstartHost.GraphicsLibraryBase +
			(uint)GraphicsLayouts.GfxBaseActiView);
		var viewPort = screen + ScreenViewPortOffset;
		WriteMinimalViewPort(bus, viewPort);
		bus.WriteLong(view + ViewViewPortOffset, viewPort);

		// Keep the public chain planar while a provider registration for the same
		// guest ViewPort is selected.  MrgCop must classify the chain again after
		// rebuilding and publishing the active copper stream.
		const uint rtgBitMap = 0x5F00;
		var surface = Assert.IsType<CyberGraphicsSurface>(
			boot.CyberGraphics.AllocateRtgSurface(2, 1, CyberGraphicsPixelFormat.Lut8));
		boot.CyberGraphics.RegisterBitMap(rtgBitMap, surface);
		boot.CyberGraphics.RegisterViewPort(viewPort, surface);
		boot.CyberGraphics.SelectFrontViewPort(viewPort);
		Assert.True(boot.CyberGraphics.RtgScanoutSelected);

		var merge = new M68kCpuState
		{
			A = { [1] = view },
			D = { [0] = 0xA5A5_5A5Au },
			Cycles = 931,
			ProgramCounter = 0x0012_67E9
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD2),
			merge));

		// The active merge remains MCOP_OK and scheduler-transparent, but the
		// standard-planar public chain reclaims front ownership from the provider.
		Assert.Equal(0u, merge.D[0]);
		Assert.Equal(931, merge.Cycles);
		Assert.Equal(0x0012_67E9u, merge.ProgramCounter);
		Assert.Equal(
			view,
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase +
				(uint)GraphicsLayouts.GfxBaseActiView));
		Assert.False(boot.CyberGraphics.RtgScanoutSelected);
	}

	[Fact]
	public void MrgCopReportsNoMemoryAndRollsBackWhenTheMergedStreamDoesNotFit()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint firstViewPort = 0x2300;
		const uint secondViewPort = 0x2600;
		const uint firstRasInfo = 0x2380;
		const uint secondRasInfo = 0x2680;
		const uint firstBitMap = 0x23A0;
		const uint secondBitMap = 0x26A0;
		const uint firstPlaneBase = 0x3000;
		const uint secondPlaneBase = 0x3200;

		WriteWideViewPort(firstViewPort, firstRasInfo, firstBitMap, firstPlaneBase);
		WriteWideViewPort(secondViewPort, secondRasInfo, secondBitMap, secondPlaneBase);
		bus.WriteLong(firstViewPort, secondViewPort);
		bus.WriteLong(secondViewPort, 0);
		bus.WriteLong(view + ViewViewPortOffset, firstViewPort);
		const uint lofSentinel = 0xDEAD_BEEFu;
		const uint shfSentinel = 0xCAFE_BABEu;
		bus.WriteLong(view + ViewLofCprListOffset, lofSentinel);
		bus.WriteLong(view + ViewShfCprListOffset, shfSentinel);

		var chipFreeBefore = InvokeAvailMem(bus, MemfPublic | MemfChip);
		var state = new M68kCpuState { A = { [1] = view } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD2), state));

		Assert.Equal(1u, state.D[0]); // MCOP_NO_MEM
		Assert.Equal(lofSentinel, bus.ReadLong(view + ViewLofCprListOffset));
		Assert.Equal(shfSentinel, bus.ReadLong(view + ViewShfCprListOffset));
		Assert.Equal(0u, bus.ReadLong(firstViewPort + ViewPortDspInsOffset));
		Assert.Equal(0u, bus.ReadLong(secondViewPort + ViewPortDspInsOffset));
		Assert.Equal(chipFreeBefore, InvokeAvailMem(bus, MemfPublic | MemfChip));

		void WriteWideViewPort(uint address, uint rasInfo, uint bitMap, uint planeBase)
		{
			bus.WriteWord(address + ViewPortDWidthOffset, 16);
			bus.WriteWord(address + ViewPortDHeightOffset, 1);
			bus.WriteLong(address + ViewPortRasInfoOffset, rasInfo);
			bus.WriteLong(rasInfo + 4, bitMap);
			bus.WriteWord(bitMap + BitMapBytesPerRowOffset, 2);
			bus.WriteWord(bitMap + BitMapRowsOffset, 1);
			bus.WriteByte(bitMap + BitMapDepthOffset, 6, 0);
			for (var plane = 0; plane < 6; plane++)
			{
				var planeAddress = planeBase + (uint)(plane * 0x20);
				bus.WriteLong(bitMap + BitMapPlanesOffset + (uint)(plane * 4), planeAddress);
				bus.WriteWord(planeAddress, 0x8000);
			}

			// Keep each source stream below the expanded 0x400-byte host span,
			// while making their combined merge exceed it.  This preserves the
			// transactional no-memory assertion without depending on the old
			// singleton 0x100-byte allocation.
			var userList = address == firstViewPort ? 0x4000u : 0x5000u;
			var copList = userList + 0x20;
			var instructions = userList + 0x60;
			const ushort userInstructionCount = 100;
			bus.WriteLong(address + (uint)GraphicsLayouts.ViewPortUCopIns, userList);
			bus.WriteLong(userList + (uint)GraphicsLayouts.UCopListFirstCopList, copList);
			bus.WriteLong(userList + (uint)GraphicsLayouts.UCopListCopList, copList);
			bus.WriteLong(userList + (uint)GraphicsLayouts.UCopListNext, 0);
			bus.WriteLong(copList + (uint)GraphicsLayouts.CopListNext, 0);
			bus.WriteLong(copList + (uint)GraphicsLayouts.CopListCopIns, instructions);
			bus.WriteWord(copList + (uint)GraphicsLayouts.CopListCount, userInstructionCount);
			bus.WriteWord(copList + (uint)GraphicsLayouts.CopListMaxCount, userInstructionCount);
			for (var index = 0; index < userInstructionCount; index++)
			{
				var instruction = instructions +
					(uint)(index * GraphicsLayouts.CopInsSize);
				bus.WriteWord(
					instruction + (uint)GraphicsLayouts.CopInsOpCode,
					0);
				bus.WriteWord(
					instruction + (uint)GraphicsLayouts.CopInsArg0,
					(ushort)(0x0180 + ((index % 32) * 2)));
				bus.WriteWord(
					instruction + (uint)GraphicsLayouts.CopInsArg1,
					(ushort)index);
			}
		}
	}

	[Fact]
	public void MrgCopReportsNoMemoryWhenHostProjectionFailsAfterPortableValidation()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint viewPort = 0x2300;
		const uint rasInfo = 0x2380;
		WriteMinimalViewPort(bus, viewPort);
		bus.WriteLong(view + ViewViewPortOffset, viewPort);
		// ValidateView permits an assembling RasInfo without a bitmap.  The
		// host copper projector cannot emit a display list for that surface and
		// therefore reports MCOP_NOMEM while leaving the guest links untouched.
		bus.WriteLong(rasInfo + 4, 0);

		var state = new M68kCpuState { A = { [1] = view } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD2), state));
		Assert.Equal(1u, state.D[0]); // MCOP_NOMEM
		Assert.Equal(0u, bus.ReadLong(viewPort + ViewPortDspInsOffset));
	}

	[Fact]
	public void MrgCopRollsBackWhenALaterViewportCannotBeProjected()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint firstViewPort = 0x2300;
		const uint secondViewPort = 0x2600;
		const uint secondRasInfo = 0x2A00;
		const uint secondBitMap = 0x2A20;
		const uint secondPlane = 0x2A40;
		WriteMinimalViewPort(bus, firstViewPort);
		WriteMinimalViewPort(bus, secondViewPort);
		bus.WriteLong(firstViewPort, secondViewPort);
		bus.WriteLong(secondViewPort, 0);
		bus.WriteLong(secondViewPort + ViewPortRasInfoOffset, secondRasInfo);
		bus.WriteLong(secondRasInfo, 0);
		bus.WriteLong(secondRasInfo + 4, secondBitMap);
		bus.WriteWord(secondBitMap, 2);
		bus.WriteWord(secondBitMap + 2, 1);
		bus.WriteByte(secondBitMap + 5, 1, 0);
		bus.WriteLong(secondBitMap + 8, secondPlane);
		bus.WriteWord(secondPlane, 0x8000);
		bus.WriteLong(view + ViewViewPortOffset, firstViewPort);

		var state = new M68kCpuState { A = { [1] = view } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD2), state));
		Assert.Equal(0u, state.D[0]);
		var originalLofCprList = bus.ReadLong(view + ViewLofCprListOffset);
		var originalShfCprList = bus.ReadLong(view + ViewShfCprListOffset);
		var originalFirstDspIns = bus.ReadLong(firstViewPort + ViewPortDspInsOffset);
		var originalSecondDspIns = bus.ReadLong(secondViewPort + ViewPortDspInsOffset);

		// The portable envelope still accepts an assembling RasInfo with no
		// bitmap, but the host projection cannot emit that later viewport.
		bus.WriteLong(secondRasInfo + 4, 0);
		state = new M68kCpuState { A = { [1] = view } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD2), state));
		Assert.Equal(1u, state.D[0]); // MCOP_NOMEM
		Assert.Equal(originalLofCprList, bus.ReadLong(view + ViewLofCprListOffset));
		Assert.Equal(originalShfCprList, bus.ReadLong(view + ViewShfCprListOffset));
		Assert.Equal(originalFirstDspIns, bus.ReadLong(firstViewPort + ViewPortDspInsOffset));
		Assert.Equal(originalSecondDspIns, bus.ReadLong(secondViewPort + ViewPortDspInsOffset));
	}

	[Fact]
	public void MrgCopLeavesExistingViewListsWhenTheViewPortChainCycles()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint viewPort = 0x2300;
		WriteMinimalViewPort(bus, viewPort);
		bus.WriteLong(viewPort, viewPort);
		bus.WriteLong(view + ViewViewPortOffset, viewPort);
		const uint sentinel = 0xDEAD_BEEFu;
		bus.WriteLong(view + ViewLofCprListOffset, sentinel);
		bus.WriteLong(view + ViewShfCprListOffset, sentinel + 4);

		var state = new M68kCpuState { A = { [1] = view } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD2), state));
		Assert.Equal(0u, state.D[0]); // malformed Views remain unclaimed for native fallback.
		Assert.Equal(sentinel, bus.ReadLong(view + ViewLofCprListOffset));
		Assert.Equal(sentinel + 4, bus.ReadLong(view + ViewShfCprListOffset));
		Assert.Equal(0u, bus.ReadLong(viewPort + ViewPortDspInsOffset));
	}

	[Fact]
	public void MrgCopReturnsNoOpForAnAllHiddenViewPortChain()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint viewPort = 0x2300;
		const uint sentinel = 0xDEAD_BEEFu;

		WriteMinimalViewPort(bus, viewPort);
		bus.WriteWord(viewPort + ViewPortModesOffset, 0x2000); // VP_HIDE
		bus.WriteLong(view + ViewViewPortOffset, viewPort);
		bus.WriteLong(view + ViewLofCprListOffset, sentinel);
		bus.WriteLong(view + ViewShfCprListOffset, sentinel + 4);

		var state = new M68kCpuState { A = { [1] = view }, D = { [0] = 0xA5A5_5A5Au } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD2), state));

		Assert.Equal(2u, state.D[0]); // MCOP_NOP
		Assert.Equal(sentinel, bus.ReadLong(view + ViewLofCprListOffset));
		Assert.Equal(sentinel + 4, bus.ReadLong(view + ViewShfCprListOffset));
		Assert.Equal(0u, bus.ReadLong(viewPort + ViewPortDspInsOffset));
	}

	[Fact]
	public void MrgCopIgnoresMalformedPayloadBehindAHiddenViewPort()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint viewPort = 0x2300;
		const uint sentinel = 0xDEAD_BEEFu;

		WriteMinimalViewPort(bus, viewPort);
		bus.WriteWord(viewPort + ViewPortModesOffset, 0x2000); // VP_HIDE
		// MrgCop reads only the public mode/next links for a hidden node.  The
		// RasInfo pointer is intentionally odd and unavailable to the host path.
		bus.WriteLong(viewPort + ViewPortRasInfoOffset, 1);
		bus.WriteLong(view + ViewViewPortOffset, viewPort);
		bus.WriteLong(view + ViewLofCprListOffset, sentinel);
		bus.WriteLong(view + ViewShfCprListOffset, sentinel + 4);

		var state = new M68kCpuState { A = { [1] = view }, D = { [0] = 0xA5A5_5A5Au } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD2), state));

		Assert.Equal(2u, state.D[0]); // MCOP_NOP
		Assert.Equal(sentinel, bus.ReadLong(view + ViewLofCprListOffset));
		Assert.Equal(sentinel + 4, bus.ReadLong(view + ViewShfCprListOffset));
		Assert.Equal(0u, bus.ReadLong(viewPort + ViewPortDspInsOffset));
	}

	[Fact]
	public void MrgCopPublishesTheFirstVisibleViewPortWhenTheChainHeadIsHidden()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint hiddenViewPort = 0x2300;
		const uint visibleViewPort = 0x2600;
		const uint visibleRasInfo = 0x2680;
		const uint visibleBitMap = 0x26A0;
		const uint visiblePlane = 0x26C0;
		const uint hiddenDspIns = 0xDEAD_BEEFu;

		WriteMinimalViewPort(bus, hiddenViewPort);
		bus.WriteLong(hiddenViewPort, visibleViewPort);
		bus.WriteWord(hiddenViewPort + ViewPortModesOffset, 0x2000); // VP_HIDE
		bus.WriteLong(hiddenViewPort + ViewPortDspInsOffset, hiddenDspIns);

		bus.WriteLong(visibleViewPort, 0);
		bus.WriteWord(visibleViewPort + ViewPortDWidthOffset, 16);
		bus.WriteWord(visibleViewPort + ViewPortDHeightOffset, 1);
		bus.WriteLong(visibleViewPort + ViewPortRasInfoOffset, visibleRasInfo);
		bus.WriteLong(visibleRasInfo + 0x04, visibleBitMap);
		bus.WriteWord(visibleBitMap + 0x00, 2);
		bus.WriteWord(visibleBitMap + 0x02, 1);
		bus.WriteByte(visibleBitMap + 0x05, 1, 0);
		bus.WriteLong(visibleBitMap + 0x08, visiblePlane);
		bus.WriteWord(visiblePlane, 0x4000);

		bus.WriteLong(view + ViewViewPortOffset, hiddenViewPort);

		var state = new M68kCpuState { A = { [1] = view } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD2), state));
		Assert.Equal(0u, state.D[0]);

		var visibleDspIns = bus.ReadLong(visibleViewPort + ViewPortDspInsOffset);
		Assert.NotEqual(0u, visibleDspIns);
		Assert.Equal(hiddenDspIns, bus.ReadLong(hiddenViewPort + ViewPortDspInsOffset));
		Assert.Equal(hiddenViewPort, bus.ReadLong(view + ViewViewPortOffset));

		var activeCpr = bus.ReadLong(view + ViewLofCprListOffset);
		Assert.NotEqual(0u, activeCpr);
		Assert.Equal(visibleDspIns, bus.ReadLong(activeCpr + CprListStartOffset));
		Assert.Equal(activeCpr, bus.ReadLong(view + ViewShfCprListOffset));
	}

	[Fact]
	public void MrgCopMergesFiveVisibleViewPortsWithinTheExpandedHostSpan()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		var viewPorts = new[]
		{
			0x2300u,
			0x2800u,
			0x2D00u,
			0x3200u,
			0x3700u
		};

		for (var index = 0; index < viewPorts.Length; index++)
		{
			var viewPort = viewPorts[index];
			var rasInfo = 0x4000u + (uint)(index * 0x40);
			var bitMap = 0x4200u + (uint)(index * 0x40);
			var plane = 0x4400u + (uint)(index * 0x40);
			WriteVisibleViewPort(viewPort, rasInfo, bitMap, plane, (ushort)(index * 12));
			bus.WriteLong(viewPort + (uint)GraphicsLayouts.ViewPortNext,
				index + 1 < viewPorts.Length ? viewPorts[index + 1] : 0);
		}

		bus.WriteLong(view + ViewViewPortOffset, viewPorts[0]);
		var merge = new M68kCpuState
		{
			A = { [1] = view },
			D = { [0] = 0xA5A5_5A5Au }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD2),
			merge));

		Assert.Equal(0u, merge.D[0]);
		var activeCpr = bus.ReadLong(view + ViewLofCprListOffset);
		Assert.NotEqual(0u, activeCpr);
		Assert.Equal(activeCpr, bus.ReadLong(view + ViewShfCprListOffset));
		for (var index = 0; index < viewPorts.Length; index++)
			Assert.NotEqual(0u, bus.ReadLong(viewPorts[index] + ViewPortDspInsOffset));

		var mergedRaw = bus.ReadLong(activeCpr + CprListStartOffset);
		Assert.NotEqual(0u, mergedRaw);
		Assert.True(bus.ReadWord(
			activeCpr + (uint)GraphicsLayouts.CprListMaxCount) > 64);
		var terminatorOffset = 0u;
		for (; terminatorOffset < 0x400; terminatorOffset += 4)
		{
			if (bus.ReadWord(mergedRaw + terminatorOffset) == 0xFFFF &&
				bus.ReadWord(mergedRaw + terminatorOffset + 2) == 0xFFFE)
				break;
		}

		// Five generated streams require more than the historical singleton
		// 0x100-byte compatibility span once four separators are inserted.
		Assert.True(terminatorOffset > 0x100, $"merged terminator at 0x{terminatorOffset:X}");


		void WriteVisibleViewPort(
			uint viewPort,
			uint rasInfo,
			uint bitMap,
			uint plane,
			ushort dy)
		{
			bus.WriteWord(viewPort + ViewPortDWidthOffset, 16);
			bus.WriteWord(viewPort + ViewPortDHeightOffset, 1);
			bus.WriteWord(viewPort + ViewPortDxOffset, 0);
			bus.WriteWord(viewPort + ViewPortDyOffset, dy);
			bus.WriteWord(viewPort + ViewPortModesOffset, 0);
			bus.WriteByte(viewPort + ViewPortExtendedModesOffset, 0, 0);
			bus.WriteLong(viewPort + ViewPortRasInfoOffset, rasInfo);
			bus.WriteLong(viewPort + (uint)GraphicsLayouts.ViewPortColorMap, 0);
			bus.WriteLong(viewPort + ViewPortDspInsOffset, 0);
			bus.WriteLong(viewPort + (uint)GraphicsLayouts.ViewPortSprIns, 0);
			bus.WriteLong(viewPort + (uint)GraphicsLayouts.ViewPortClrIns, 0);
			bus.WriteLong(viewPort + (uint)GraphicsLayouts.ViewPortUCopIns, 0);
			bus.WriteLong(rasInfo + 0, 0);
			bus.WriteLong(rasInfo + 4, bitMap);
			bus.WriteWord(bitMap + BitMapBytesPerRowOffset, 2);
			bus.WriteWord(bitMap + BitMapRowsOffset, 1);
			bus.WriteByte(bitMap + BitMapDepthOffset, 1, 0);
			bus.WriteByte(bitMap + BitMapFlagsOffset, 0, 0);
			bus.WriteLong(bitMap + BitMapPlanesOffset, plane);
			bus.WriteWord(plane, 0x8000);
		}
	}

	[Fact]
	public void MrgCopRefusesReadOnlyViewPublicationBeforeRebuildingCopper()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint hiddenViewPort = 0x2300;
		const uint visibleViewPort = 0x2600;
		const uint visibleRasInfo = 0x2680;
		const uint visibleBitMap = 0x26A0;
		const uint visiblePlane = 0x26C0;

		bus.WriteLong(hiddenViewPort, visibleViewPort);
		bus.WriteWord(hiddenViewPort + ViewPortModesOffset, 0x2000); // VP_HIDE
		bus.WriteLong(visibleViewPort, 0);
		bus.WriteWord(visibleViewPort + ViewPortDWidthOffset, 16);
		bus.WriteWord(visibleViewPort + ViewPortDHeightOffset, 1);
		bus.WriteLong(visibleViewPort + ViewPortRasInfoOffset, visibleRasInfo);
		bus.WriteLong(visibleRasInfo + 0x04, visibleBitMap);
		bus.WriteWord(visibleBitMap + 0x00, 2);
		bus.WriteWord(visibleBitMap + 0x02, 1);
		bus.WriteByte(visibleBitMap + 0x05, 1, 0);
		bus.WriteLong(visibleBitMap + 0x08, visiblePlane);
		bus.WriteWord(visiblePlane, 0x4000);
		bus.WriteLong(view + ViewViewPortOffset, hiddenViewPort);

		var firstMerge = new M68kCpuState { A = { [1] = view } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD2),
			firstMerge));
		Assert.Equal(0u, firstMerge.D[0]);
		var originalLofCprList = bus.ReadLong(view + ViewLofCprListOffset);
		var originalShfCprList = bus.ReadLong(view + ViewShfCprListOffset);
		var originalVisibleDspIns = bus.ReadLong(visibleViewPort + ViewPortDspInsOffset);
		Assert.NotEqual(0u, originalLofCprList);
		Assert.NotEqual(0u, originalVisibleDspIns);

		bus.MapReadOnlyMemory(
			view + ViewViewPortOffset,
			new byte[]
			{
				(byte)(hiddenViewPort >> 24),
				(byte)(hiddenViewPort >> 16),
				(byte)(hiddenViewPort >> 8),
				unchecked((byte)hiddenViewPort)
			});

		var refusedMerge = new M68kCpuState
		{
			A = { [1] = view },
			D = { [0] = 0xA5A5_5A5Au }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD2),
			refusedMerge));
		Assert.Equal(1u, refusedMerge.D[0]); // MCOP_NO_MEM
		Assert.Equal(hiddenViewPort, bus.ReadLong(view + ViewViewPortOffset));
		Assert.Equal(originalLofCprList, bus.ReadLong(view + ViewLofCprListOffset));
		Assert.Equal(originalShfCprList, bus.ReadLong(view + ViewShfCprListOffset));
		Assert.Equal(originalVisibleDspIns, bus.ReadLong(visibleViewPort + ViewPortDspInsOffset));
	}

	[Fact]
	public void MrgCopRefusesReadOnlyMergedCopperDestinationBeforeAppendingLinkedStream()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint firstViewPort = 0x2300;
		const uint secondViewPort = 0x2400;
		const uint secondRasInfo = 0x2480;
		const uint secondBitMap = 0x24A0;
		const uint secondPlane = 0x24C0;

		WriteMinimalViewPort(bus, firstViewPort);
		bus.WriteLong(firstViewPort, secondViewPort);
		bus.WriteWord(secondViewPort + ViewPortDWidthOffset, 16);
		bus.WriteWord(secondViewPort + ViewPortDHeightOffset, 1);
		bus.WriteLong(secondViewPort + ViewPortRasInfoOffset, secondRasInfo);
		bus.WriteLong(secondRasInfo + 0x04, secondBitMap);
		bus.WriteWord(secondBitMap + 0x00, 2);
		bus.WriteWord(secondBitMap + 0x02, 1);
		bus.WriteByte(secondBitMap + 0x05, 1, 0);
		bus.WriteLong(secondBitMap + 0x08, secondPlane);
		bus.WriteWord(secondPlane, 0x4000);
		bus.WriteLong(view + ViewViewPortOffset, firstViewPort);

		var state = new M68kCpuState { A = { [1] = view } };
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD2),
			state));
		Assert.Equal(0u, state.D[0]);

		var cprByViewPortField = typeof(AmigaBootController).GetField(
			"_compatibilityCprListByViewPort",
			BindingFlags.Instance | BindingFlags.NonPublic);
		var copperByCprField = typeof(AmigaBootController).GetField(
			"_compatibilityCopperByCprList",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(cprByViewPortField);
		Assert.NotNull(copperByCprField);
		var cprByViewPort = (Dictionary<uint, uint>)cprByViewPortField!.GetValue(boot)!;
		var copperByCpr = (Dictionary<uint, uint>)copperByCprField!.GetValue(boot)!;
		var firstCprList = cprByViewPort[firstViewPort];
		var secondCprList = cprByViewPort[secondViewPort];
		var firstRawCopperList = copperByCpr[firstCprList];
		Assert.NotEqual(0u, firstRawCopperList);

		var readOnlyStream = new byte[0x100];
		for (var offset = 0; offset < readOnlyStream.Length; offset++)
			readOnlyStream[offset] = bus.ReadByte(firstRawCopperList + (uint)offset);
		bus.MapReadOnlyMemory(firstRawCopperList, readOnlyStream);

		var mergeMethod = typeof(AmigaBootController).GetMethod(
			"TryMergeCompatibilityCopperLists",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(mergeMethod);
		var merged = (bool)mergeMethod!.Invoke(
			boot,
			new object[] { new List<uint> { firstCprList, secondCprList } })!;

		Assert.False(merged);
	}

	[Fact]
	public void MakeVPortReportsNoDisplayWhenTheValidatedViewportHasNoBitmap()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint viewPort = 0x2300;
		const uint rasInfo = 0x2380;
		bus.WriteLong(view + ViewViewPortOffset, viewPort);
		bus.WriteWord(viewPort + ViewPortDWidthOffset, 16);
		bus.WriteWord(viewPort + ViewPortDHeightOffset, 1);
		bus.WriteWord(viewPort + 0x1C, 0);
		bus.WriteWord(viewPort + 0x1E, 0);
		bus.WriteWord(viewPort + ViewPortModesOffset, 0);
		bus.WriteLong(viewPort + ViewPortRasInfoOffset, rasInfo);
		bus.WriteLong(rasInfo + 0x00, 0);
		bus.WriteLong(rasInfo + 0x04, 0);
		bus.WriteWord(rasInfo + 0x08, 0);
		bus.WriteWord(rasInfo + 0x0A, 0);

		var state = new M68kCpuState { A = { [0] = view, [1] = viewPort } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD8), state));
		Assert.Equal(4u, state.D[0]); // MVP_NO_DISPLAY
	}

	[Fact]
	public void MakeVPortRejectsUnmappedStandardPlanarPlaneStorage()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint viewPort = 0x2300;
		const uint rasInfo = 0x2380;
		const uint bitMap = 0x23A0;
		const uint unmappedPlane = 0x00F0_0000;

		bus.WriteWord(viewPort + ViewPortDWidthOffset, 16);
		bus.WriteWord(viewPort + ViewPortDHeightOffset, 2);
		bus.WriteLong(viewPort + ViewPortRasInfoOffset, rasInfo);
		bus.WriteLong(rasInfo + 4, bitMap);
		bus.WriteWord(bitMap + BitMapBytesPerRowOffset, 2);
		bus.WriteWord(bitMap + BitMapRowsOffset, 2);
		bus.WriteByte(bitMap + BitMapDepthOffset, 1, 0);
		bus.WriteLong(bitMap + BitMapPlanesOffset, unmappedPlane);

		Assert.False(bus.IsMappedMemoryRange(unmappedPlane, 4));
		var state = new M68kCpuState { A = { [0] = view, [1] = viewPort } };
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD8), state));
		Assert.Equal(4u, state.D[0]); // MVP_NO_DISPLAY
		Assert.Equal(0u, bus.ReadLong(viewPort + ViewPortDspInsOffset));
	}

	[Fact]
	public void MakeVPortSynthesizesNonCanonicalViewportGeometryAndRejectsUnrepresentableFetch()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint viewPort = 0x2300;
		const uint rasInfo = 0x2380;
		const uint bitMap = 0x23A0;
		const uint plane = 0x000F_0000;
		const int width = 352;
		const int height = 240;
		const int bytesPerRow = width / 8;

		bus.MapWritableMemory(plane, new byte[bytesPerRow * height]);
		bus.WriteWord(plane, 0x8000);
		bus.WriteLong(view + ViewViewPortOffset, viewPort);
		bus.WriteWord(viewPort + ViewPortDWidthOffset, width);
		bus.WriteWord(viewPort + ViewPortDHeightOffset, height);
		bus.WriteLong(viewPort + ViewPortRasInfoOffset, rasInfo);
		bus.WriteLong(rasInfo + 0x04, bitMap);
		bus.WriteWord(bitMap + BitMapBytesPerRowOffset, bytesPerRow);
		bus.WriteWord(bitMap + BitMapRowsOffset, height);
		bus.WriteByte(bitMap + BitMapDepthOffset, 1, 0);
		bus.WriteLong(bitMap + BitMapPlanesOffset, plane);

		var buildMethod = typeof(AmigaBootController).GetMethod(
			"TryBuildViewPortCopperList",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(buildMethod);
		var buildArguments = new object[] { view, viewPort, 0u, true };
		Assert.True((bool)buildMethod!.Invoke(boot, buildArguments)!);

		var cprWrapper = (uint)buildArguments[2];
		Assert.NotEqual(0u, cprWrapper);
		var copperList = bus.ReadLong(cprWrapper + CprListStartOffset);
		Assert.Equal((ushort)0x2C81, ReadCopperMoveValue(bus, copperList, 0x008E));
		Assert.Equal((ushort)0x1CE1, ReadCopperMoveValue(bus, copperList, 0x0090));
		Assert.Equal((ushort)0x00E0, ReadCopperMoveValue(bus, copperList, 0x0094));
		Assert.Equal((ushort)0x0000, ReadCopperMoveValue(bus, copperList, 0x0108));
		Assert.Equal((ushort)0x1000, ReadCopperMoveValue(bus, copperList, 0x0100));
		Assert.Equal((ushort)(plane >> 16), ReadCopperMoveValue(bus, copperList, 0x00E0));
		Assert.Equal((ushort)(plane & 0xFFFF), ReadCopperMoveValue(bus, copperList, 0x00E2));

		var loadView = new M68kCpuState
		{
			A = { [1] = view },
			D = { [0] = 0xA5A5_5A5Au }
		};
		Assert.True(InvokeHostTrap(
			bus,
			Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xDE),
			loadView));
		Assert.Equal(0u, loadView.D[0]);
		Assert.Equal(
			view,
			bus.ReadLong(AmigaKickstartHost.GraphicsLibraryBase + GraphicsLayouts.GfxBaseActiView));
		Assert.Equal((ushort)(copperList >> 16), bus.ReadWord(0x00DFF080));
		Assert.Equal((ushort)copperList, bus.ReadWord(0x00DFF082));

		var originalDspIns = bus.ReadLong(viewPort + ViewPortDspInsOffset);
		var originalLofCprList = bus.ReadLong(view + ViewLofCprListOffset);
		var originalShfCprList = bus.ReadLong(view + ViewShfCprListOffset);

		// DDFSTOP has only six fetch-word bits.  A width beyond 1024 pixels
		// must decline before replacing the already-published compatibility
		// list, rather than silently clamping to 64 words.
		bus.WriteWord(viewPort + ViewPortDWidthOffset, 1041);
		buildArguments = new object[] { view, viewPort, 0u, true };
		Assert.False((bool)buildMethod.Invoke(boot, buildArguments)!);
		Assert.Equal(originalDspIns, bus.ReadLong(viewPort + ViewPortDspInsOffset));
		Assert.Equal(originalLofCprList, bus.ReadLong(view + ViewLofCprListOffset));
		Assert.Equal(originalShfCprList, bus.ReadLong(view + ViewShfCprListOffset));

		bus.WriteWord(viewPort + ViewPortDWidthOffset, width);
		bus.WriteWord(viewPort + ViewPortDHeightOffset, 513);
		buildArguments = new object[] { view, viewPort, 0u, true };
		Assert.False((bool)buildMethod.Invoke(boot, buildArguments)!);
		Assert.Equal(originalDspIns, bus.ReadLong(viewPort + ViewPortDspInsOffset));
		Assert.Equal(originalLofCprList, bus.ReadLong(view + ViewLofCprListOffset));
		Assert.Equal(originalShfCprList, bus.ReadLong(view + ViewShfCprListOffset));

		bus.WriteWord(viewPort + ViewPortDWidthOffset, width);
		bus.WriteByte(bitMap + BitMapDepthOffset, 0, 0);
		buildArguments = new object[] { view, viewPort, 0u, true };
		Assert.False((bool)buildMethod.Invoke(boot, buildArguments)!);
		Assert.Equal(originalDspIns, bus.ReadLong(viewPort + ViewPortDspInsOffset));

		bus.WriteByte(bitMap + BitMapDepthOffset, 1, 0);
		bus.WriteWord(bitMap + BitMapBytesPerRowOffset, 2);
		buildArguments = new object[] { view, viewPort, 0u, true };
		Assert.False((bool)buildMethod.Invoke(boot, buildArguments)!);
		Assert.Equal(originalDspIns, bus.ReadLong(viewPort + ViewPortDspInsOffset));
	}

	[Fact]
	public void HostViewportProjectionRejectsWrappedRasInfoBeforeReplacingCopper()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint viewPort = 0x2300;
		const uint wrappedRasInfo = 0xFFFF_FFFCu;
		const uint bitMap = 0x23A0;

		WriteMinimalViewPort(bus, viewPort);
		bus.WriteLong(view + ViewViewPortOffset, viewPort);
		var buildMethod = typeof(AmigaBootController).GetMethod(
			"TryBuildViewPortCopperList",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(buildMethod);

		var buildArguments = new object[] { view, viewPort, 0u, true };
		Assert.True((bool)buildMethod.Invoke(boot, buildArguments)!);
		var originalDspIns = bus.ReadLong(viewPort + ViewPortDspInsOffset);
		var originalLofCprList = bus.ReadLong(view + ViewLofCprListOffset);
		Assert.NotEqual(0u, originalDspIns);
		Assert.NotEqual(0u, originalLofCprList);

		// An unchecked RasInfo + BitMap addition would wrap the BitMap field to
		// address zero.  Populate that low alias with a valid bitmap so the old
		// host projector would incorrectly replace the already published list.
		bus.MapWritableMemory(wrappedRasInfo, new byte[4]);
		bus.WriteLong(wrappedRasInfo, 0);
		bus.WriteLong(0, bitMap);
		bus.WriteLong(viewPort + ViewPortRasInfoOffset, wrappedRasInfo);

		buildArguments = new object[] { view, viewPort, 0u, true };
		Assert.False((bool)buildMethod.Invoke(boot, buildArguments)!);
		Assert.Equal(originalDspIns, bus.ReadLong(viewPort + ViewPortDspInsOffset));
		Assert.Equal(originalLofCprList, bus.ReadLong(view + ViewLofCprListOffset));
		Assert.Equal(originalLofCprList, bus.ReadLong(view + ViewShfCprListOffset));
	}

	[Fact]
	public void HostViewportProjectionRejectsWrappedViewPublicationFields()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		const uint wrappedView = 0xFFFF_FFFCu;
		const uint viewPort = 0x2300;
		const uint lowViewPortSentinel = 0xDEAD_BEEFu;
		const uint lowCprSentinel = 0xCAFE_BABEu;

		WriteMinimalViewPort(bus, viewPort);
		bus.MapWritableMemory(wrappedView, new byte[4]);
		bus.WriteLong(wrappedView, 0);
		bus.WriteLong(0, lowViewPortSentinel);
		bus.WriteLong(4, lowCprSentinel);

		var buildMethod = typeof(AmigaBootController).GetMethod(
			"TryBuildViewPortCopperList",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(buildMethod);
		var arguments = new object[] { wrappedView, viewPort, 0u, true };

		Assert.False((bool)buildMethod.Invoke(boot, arguments)!);
		Assert.Equal(lowViewPortSentinel, bus.ReadLong(0));
		Assert.Equal(lowCprSentinel, bus.ReadLong(4));
	}

	[Fact]
	public void MakeVPortDeclinesReadOnlyViewPublicationBeforeRetiringCopper()
	{
		var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(false));
		var boot = new AmigaBootController(machine);
		boot.StartBootFromDisk(CreateBootableDisk());
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint viewPort = 0x2300;

		WriteMinimalViewPort(bus, viewPort);
		bus.WriteLong(view + ViewViewPortOffset, viewPort);
		var buildMethod = typeof(AmigaBootController).GetMethod(
			"TryBuildViewPortCopperList",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(buildMethod);

		var buildArguments = new object[] { view, viewPort, 0u, true };
		Assert.True((bool)buildMethod.Invoke(boot, buildArguments)!);
		var originalDspIns = bus.ReadLong(viewPort + ViewPortDspInsOffset);
		var originalLofCprList = bus.ReadLong(view + ViewLofCprListOffset);
		var originalShfCprList = bus.ReadLong(view + ViewShfCprListOffset);
		Assert.NotEqual(0u, originalDspIns);
		Assert.NotEqual(0u, originalLofCprList);

		// A visible read-only overlay over View.LOF is inspectable but cannot
		// accept the replacement link.  MakeVPort must decline before it frees
		// the old wrapper or publishes a new DspIns stream.
		bus.MapReadOnlyMemory(
			view + ViewLofCprListOffset,
			new byte[]
			{
				(byte)(originalLofCprList >> 24),
				(byte)(originalLofCprList >> 16),
				(byte)(originalLofCprList >> 8),
				(byte)originalLofCprList
			});

		buildArguments = new object[] { view, viewPort, 0u, true };
		Assert.False((bool)buildMethod.Invoke(boot, buildArguments)!);
		Assert.Equal(originalDspIns, bus.ReadLong(viewPort + ViewPortDspInsOffset));
		Assert.Equal(originalLofCprList, bus.ReadLong(view + ViewLofCprListOffset));
		Assert.Equal(originalShfCprList, bus.ReadLong(view + ViewShfCprListOffset));
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
	public void InitViewClearsKickstartViewHeaderAndPublishesDefaultOrigin()
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
			if (offset >= ViewDyOffset && offset < ViewDxOffset + 2)
			{
				continue;
			}

			Assert.Equal((byte)0, bus.ReadByte(view + (uint)offset));
		}

		Assert.Equal((ushort)0x002C, bus.ReadWord(view + ViewDyOffset));
		Assert.Equal((ushort)0x0081, bus.ReadWord(view + ViewDxOffset));

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
		Assert.Equal(0u, library & 3);
		Assert.Equal((ushort)8, bus.ReadWord(library + 0x10));
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
		if (residentType == (byte)global::Amiga.NodeType.Library)
		{
			var layersLibrary = bus.ReadLong(libraryList);
			Assert.NotEqual(0u, layersLibrary);
			Assert.Equal(library, bus.ReadLong(layersLibrary));
		}
		else
		{
			Assert.Equal(library, bus.ReadLong(libraryList));
		}
		Assert.Equal(libraryList + 4, bus.ReadLong(library));
	}

	[Fact]
	public void CopperStartPublishesCopperOsResidentThroughFindResident()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint nameAddress = 0x0000_1800;
		WriteCString(bus, nameAddress, "CopperOS");
		var state = new M68kCpuState();
		state.A[1] = nameAddress;

		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.ExecLibraryBase, -96), state));
		var resident = state.D[0];
		Assert.NotEqual(0u, resident);
		Assert.Equal(0x4AFC, bus.ReadWord(resident));
		Assert.Equal(resident, bus.ReadLong(resident + 2));
		Assert.Equal("CopperOS", ReadCString(bus, bus.ReadLong(resident + 0x0E), 32));
		Assert.Equal("CopperOS 1.0", ReadCString(bus, bus.ReadLong(resident + 0x12), 32));
	}

	[Fact]
	public void MorphOsExecCompatibilitySlotsProvideFocusedRegistryPoolAndMessageOperations()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		var execBase = AmigaKickstartHost.ExecLibraryBase;
		const uint memfTotal = 0x0008_0000;

		var createPool = new M68kCpuState(); createPool.D[0] = MemfPublic;
		Assert.True(InvokeHostTrap(bus, Lvo(execBase, -696), createPool));
		var pool = createPool.D[0];
		var pooled = new M68kCpuState(); pooled.A[0] = pool; pooled.D[0] = 32;
		Assert.True(InvokeHostTrap(bus, Lvo(execBase, -708), pooled));
		var available = new M68kCpuState(); available.A[0] = pool; available.D[0] = memfTotal;
		Assert.True(InvokeHostTrap(bus, Lvo(execBase, -1050), available));
		Assert.Equal(32u, available.D[0]);

		const uint port = 0x0000_4200, first = 0x0000_4260, second = 0x0000_42A0;
		InvokeExecPort(bus, -366, port, first);
		Assert.Equal(0u, InvokeExecPort(bus, -1062, port, second));
		Assert.Equal(second, bus.ReadLong(port + MsgPortMsgListOffset));
		Assert.Equal(first, bus.ReadLong(second));

		const uint library = 0x0000_4300, libraryName = 0x0000_4380;
		InitializeExecList(bus, execBase + ExecLibListOffset);
		WriteCString(bus, libraryName, "registry.library");
		bus.WriteLong(library + MemNodeNameOffset, libraryName);
		bus.WriteLong(execBase + ExecLibListOffset, library);
		bus.WriteLong(library + 4, execBase + ExecLibListOffset);
		bus.WriteLong(library, execBase + ExecLibListOffset + 4);
		bus.WriteLong(execBase + ExecLibListOffset + 8, library);
		var findNode = new M68kCpuState(); findNode.D[0] = (uint)global::Amiga.ExecNodeListType.Library; findNode.A[0] = libraryName;
		Assert.True(InvokeHostTrap(bus, Lvo(execBase, -960), findNode));
		Assert.Equal(library, findNode.D[0]);

		const uint resident = 0x0000_4400, residentName = 0x0000_4440;
		WriteCString(bus, residentName, "test.resident");
		bus.WriteWord(resident, 0x4AFC); bus.WriteLong(resident + 2, resident); bus.WriteLong(resident + 6, resident + 0x30);
		bus.WriteLong(resident + 0x0E, residentName);
		var addResident = new M68kCpuState(); addResident.A[1] = resident;
		Assert.True(InvokeHostTrap(bus, Lvo(execBase, -990), addResident));
		var findResident = new M68kCpuState(); findResident.A[1] = residentName;
		Assert.True(InvokeHostTrap(bus, Lvo(execBase, -96), findResident));
		Assert.Equal(resident, findResident.D[0]);
	}

	[Fact]
	public void CalcIvgCountsBuiltDisplayCopperAndKeepsTheLegacyOcsFloor()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint viewPort = 0x2300;

		WriteMinimalViewPort(bus, viewPort);
		bus.WriteLong(view + ViewViewPortOffset, viewPort);
		var make = new M68kCpuState();
		make.A[0] = view;
		make.A[1] = viewPort;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD8), make));
		Assert.Equal(0u, make.D[0]);

		var calc = new M68kCpuState();
		calc.A[0] = view;
		calc.A[1] = viewPort;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0x33C), calc));
		Assert.Equal(2u, calc.D[0]);

		// Before MakeVPort there is no DspIns stream to count, so the native
		// zero-on-error result is preserved instead of inventing a gap.
		var secondView = 0x2280u;
		var unbuiltViewPort = 0x2500u;
		WriteMinimalViewPort(bus, unbuiltViewPort);
		bus.WriteLong(secondView + ViewViewPortOffset, unbuiltViewPort);
		var unbuilt = new M68kCpuState();
		unbuilt.A[0] = secondView;
		unbuilt.A[1] = unbuiltViewPort;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0x33C), unbuilt));
		Assert.Equal(0u, unbuilt.D[0]);
	}

	[Fact]
	public void CalcIvgDoublesTheGapForInterlacedNativeModes()
	{
		var machine = StartBootShim(MachineProfile.A500Pal512KBoot);
		var bus = machine.Bus;
		const uint view = 0x2200;
		const uint viewPort = 0x2300;

		WriteMinimalViewPort(bus, viewPort);
		bus.WriteWord(viewPort + ViewPortModesOffset, ViewModeInterlace);
		bus.WriteLong(view + ViewViewPortOffset, viewPort);
		var make = new M68kCpuState();
		make.A[0] = view;
		make.A[1] = viewPort;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0xD8), make));

		var calc = new M68kCpuState();
		calc.A[0] = view;
		calc.A[1] = viewPort;
		Assert.True(InvokeHostTrap(bus, Lvo(AmigaKickstartHost.GraphicsLibraryBase, -0x33C), calc));
		Assert.Equal(4u, calc.D[0]);
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

	private static void WriteMoveAddressRegisterToAbsolute(
		AmigaBus bus,
		uint address,
		int addressRegister,
		uint destination)
	{
		bus.WriteWord(address, (ushort)(0x23C8 | addressRegister));
		bus.WriteLong(address + 2, destination);
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

	private static bool InvokeIsValidKickstartRomExecBase(AmigaBootController boot, uint execBase)
	{
		var method = typeof(AmigaBootController).GetMethod(
			"IsValidKickstartRomExecBase",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(method);
		return (bool)method.Invoke(boot, new object[] { execBase })!;
	}

	private static void InvokeTryActivateKickstartRomExecServices(AmigaBootController boot)
	{
		var method = typeof(AmigaBootController).GetMethod(
			"TryActivateKickstartRomExecServices",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(method);
		method.Invoke(boot, Array.Empty<object>());
	}

	private static string GetKickstartRomExecTakeoverState(AmigaBootController boot)
		=> typeof(AmigaBootController)
			.GetField("_kickstartRomExecTakeoverState", BindingFlags.Instance | BindingFlags.NonPublic)!
			.GetValue(boot)!
			.ToString()!;

	private static void SetKickstartRomExecTakeoverState(AmigaBootController boot, string state)
	{
		var field = typeof(AmigaBootController)
			.GetField("_kickstartRomExecTakeoverState", BindingFlags.Instance | BindingFlags.NonPublic)!;
		field.SetValue(boot, Enum.Parse(field.FieldType, state));
	}

	private static void WriteExecVector(AmigaBus bus, uint execBase, int lvo, uint target)
	{
		var address = unchecked(execBase + (uint)lvo);
		bus.WriteWord(address, 0x4EF9, 0);
		bus.WriteLong(address + 2, target);
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
		// FreeMem has no public return value. Callers verify restored free-list
		// capacity or successful reuse; D0's ROM-specific residue is not status.
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
		string name,
		uint reservedPrefix = 0)
	{
		var freeBytes = upper - lower - reservedPrefix;
		var firstFree = lower + reservedPrefix;
		Assert.Equal(successor, bus.ReadLong(header));
		Assert.Equal(predecessor, bus.ReadLong(header + 4));
		Assert.Equal((ushort)attributes, bus.ReadWord(header + MemHeaderAttributesOffset));
		Assert.Equal(lower, bus.ReadLong(header + MemHeaderLowerOffset));
		Assert.Equal(upper, bus.ReadLong(header + MemHeaderUpperOffset));
		Assert.Equal(freeBytes, bus.ReadLong(header + MemHeaderFreeOffset));
		Assert.Equal(firstFree, bus.ReadLong(header + MemHeaderFirstChunkOffset));
		Assert.Equal(0u, bus.ReadLong(firstFree + MemChunkNextOffset));
		Assert.Equal(freeBytes, bus.ReadLong(firstFree + MemChunkBytesOffset));
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

	private static void WriteLongBytes(AmigaBus bus, uint address, uint value)
	{
		for (var index = 0; index < sizeof(uint); index++)
		{
			bus.WriteByte(
				address + (uint)index,
				unchecked((byte)(value >> (24 - (index * 8)))),
				0);
		}
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
