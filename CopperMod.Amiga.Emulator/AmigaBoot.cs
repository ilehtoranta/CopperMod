/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DosLvo = Amiga.DosLvo;
using IconLvo = Amiga.IconLvo;
using CopperStartExecServices = CopperMod.Amiga.CopperStart.Exec.ExecServices;
using CopperStartExecLibraryServices = CopperMod.Amiga.CopperStart.Exec.ExecLibraryServices;
using CopperStartGuestMemory = CopperMod.Amiga.Bus.HostGuestMemory;
using CopperStartExecSignalServices = CopperMod.Amiga.CopperStart.Exec.ExecSignalServices;
using CopperStartExecSemaphoreServices = CopperMod.Amiga.CopperStart.Exec.ExecSemaphoreServices;
using CopperStartExecContext = CopperMod.Amiga.CopperStart.Exec.CopperStartExecContext;
using CopperStartExecPortServices = CopperMod.Amiga.CopperStart.Exec.ExecPortServices;
using CopperStartExecTaskServices = CopperMod.Amiga.CopperStart.Exec.ExecTaskServices;
using CopperStartExecTrapServices = CopperMod.Amiga.CopperStart.Exec.ExecTrapServices;
using CopperStartExecNameServices = CopperMod.Amiga.CopperStart.Exec.ExecNameServices;
using CopperStartExecMemoryOperations = CopperMod.Amiga.CopperStart.Exec.ExecMemoryOperations;
using CopperStartExecPoolServices = CopperMod.Amiga.CopperStart.Exec.ExecPoolServices;
using CopperStartExecResidentServices = CopperMod.Amiga.CopperStart.Exec.ExecResidentServices;
using CopperStartCopperOsResidentServices = CopperMod.Amiga.CopperStart.Exec.CopperOsResidentServices;
using CopperStartExecMemoryServices = CopperMod.Amiga.CopperStart.Exec.ExecMemoryServices;
using CopperStartExecMemoryContext = CopperMod.Amiga.CopperStart.Exec.ExecMemoryContext;
using CopperStartExecGatewayServices = CopperMod.Amiga.CopperStart.Exec.ExecGatewayServices;
using CopperStartExecLibraryGatewayServices = CopperMod.Amiga.CopperStart.Exec.ExecLibraryGatewayServices;
using CopperStartExecFormatServices = CopperMod.Amiga.CopperStart.Exec.ExecFormatServices;
using CopperStartExecInitStructServices = CopperMod.Amiga.CopperStart.Exec.ExecInitStructServices;
using PortableIntuition = CopperStart.Intuition;
using GraphicsRasterOperations = CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsRasterOperations;
using CopperStartExecMakeLibraryServices = CopperMod.Amiga.CopperStart.Exec.ExecMakeLibraryServices;
using CopperStartExecIoServices = CopperMod.Amiga.CopperStart.Exec.ExecIoServices;
using AmigaBusExecMemoryPlatform = CopperMod.Amiga.CopperStart.Exec.AmigaBusExecMemoryPlatform;
using HostGuestMemoryExecPlatform = CopperMod.Amiga.CopperStart.Exec.HostGuestMemoryExecPlatform;
using PortableExecMemory = CopperStart.Exec;
using CopperStartTrackdiskDeviceServices = CopperMod.Amiga.CopperStart.Devices.Trackdisk.TrackdiskDeviceServices;
using CopperStartTrackdiskRawTrack = CopperMod.Amiga.CopperStart.Devices.Trackdisk.TrackdiskRawTrack;
using CopperStartTimerDeviceServices = CopperMod.Amiga.CopperStart.Devices.Timer.TimerDeviceServices;
using CopperStartAudioDeviceServices = CopperMod.Amiga.CopperStart.Devices.Audio.AudioDeviceServices;
using CopperStartKeyboardDeviceServices = CopperMod.Amiga.CopperStart.Devices.Keyboard.KeyboardDeviceServices;
using CopperStartInputDeviceServices = CopperMod.Amiga.CopperStart.Devices.Input.InputDeviceServices;
using CopperStartGameportDeviceServices = CopperMod.Amiga.CopperStart.Devices.Gameport.GameportDeviceServices;
using CopperStartConsoleDeviceServices = CopperMod.Amiga.CopperStart.Devices.Console.ConsoleDeviceServices;
using CopperStartConsoleContext = CopperMod.Amiga.CopperStart.Devices.Console.CopperStartConsoleContext;
using CopperStartClipboardDeviceServices = CopperMod.Amiga.CopperStart.Devices.Clipboard.ClipboardDeviceServices;
using CopperStartClipboardImage = CopperMod.Amiga.CopperStart.Devices.Clipboard.ClipboardImage;
using CopperStartUtilityContext = CopperMod.Amiga.CopperStart.Utility.CopperStartUtilityContext;
using CopperStartUtilityLibraryServices = CopperMod.Amiga.CopperStart.Utility.UtilityLibraryServices;
using CopperMod.Amiga.Input;
using CopperStartEncodedTrack = CopperMod.Amiga.Storage.Floppy.AmigaEncodedTrack;
using CopperStartDiskImage = CopperMod.Amiga.Storage.Floppy.IAmigaDiskImage;
using CopperStartRuntime = CopperMod.Amiga.CopperStart.CopperStartRuntime;
using CopperStartWorkbenchContext = CopperMod.Amiga.CopperStart.Workbench.CopperStartWorkbenchContext;
using CopperStartWorkbenchServices = CopperMod.Amiga.CopperStart.Workbench.WorkbenchServices;
using CopperStartIntuitionContext = CopperMod.Amiga.CopperStart.Intuition.CopperStartIntuitionContext;
using CopperStartIntuitionServices = CopperMod.Amiga.CopperStart.Intuition.IntuitionServices;
using CopperStartSyntheticUiInputState = CopperMod.Amiga.CopperStart.Intuition.SyntheticUiInputState;
using CopperStartSyntheticIntuiMessage = CopperMod.Amiga.CopperStart.Intuition.SyntheticIntuiMessage;
using CopperStartSyntheticUiDisplayState = CopperMod.Amiga.CopperStart.Intuition.SyntheticUiDisplayState;
using CopperStartExecListServices = CopperMod.Amiga.CopperStart.Exec.ExecListServices;
using CopperStartTaskScheduler = CopperMod.Amiga.CopperStart.Exec.ExecTaskScheduler;
using CopperStartGraphicsContext = CopperMod.Amiga.CopperStart.Graphics.CopperStartGraphicsContext;
using CopperStartGraphicsServices = CopperMod.Amiga.CopperStart.Graphics.GraphicsServices;
using CopperStartLayersHostServices = CopperMod.Amiga.CopperStart.Layers.LayersHostServices;
using PortableLayers = CopperStart.Layers;
using CopperStartSyntheticDisplayServices = CopperMod.Amiga.CopperStart.Graphics.SyntheticDisplayServices;
using CopperStartGraphicsMemoryAdapter = CopperMod.Amiga.CopperStart.Graphics.CopperStartGraphicsMemoryAdapter;
using CopperStartGraphicsAllocator = CopperMod.Amiga.CopperStart.Graphics.CopperStartGraphicsAllocator;
using CopperStartGraphicsFontList = CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsGuestFontListBackend;
using CopperStartGraphicsFontBackend = CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsMemoryFontBackend;
using CopperStartGraphicsChipRevision = CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsChipRevision;
using CopperStartGraphicsBlitterScheduler = CopperMod.Amiga.CopperStart.Graphics.CopperStartGraphicsBlitterScheduler;
using CopperStartGraphicsLayouts = CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsLayouts;
using CopperStartDosContext = CopperMod.Amiga.CopperStart.Dos.CopperStartDosContext;
using CopperStartDosServices = CopperMod.Amiga.CopperStart.Dos.DosServices;
using CopperStartIconContext = CopperMod.Amiga.CopperStart.Icon.CopperStartIconContext;
using CopperStartIconServices = CopperMod.Amiga.CopperStart.Icon.IconServices;
using CopperStartExpansionContext = CopperMod.Amiga.CopperStart.Expansion.CopperStartExpansionContext;
using CopperStartExpansionServices = CopperMod.Amiga.CopperStart.Expansion.ExpansionServices;
using CopperStartTaskTrapRuntime = CopperMod.Amiga.CopperStart.Runtime.TaskTrapRuntime;
using CopperStartExecutionBoundarySchedule = CopperMod.Amiga.CopperStart.Runtime.ExecutionBoundarySchedule;
using CopperStartTaskTrapRecovery = CopperMod.Amiga.CopperStart.Runtime.TaskTrapRecovery;
using CopperStartRuntimeInstructionBoundary = CopperMod.Amiga.CopperStart.Runtime.RuntimeInstructionBoundary;
using CopperStartRuntimeInstructionBoundaryContext = CopperMod.Amiga.CopperStart.Runtime.RuntimeInstructionBoundaryContext;
using CopperStartBootInstructionBoundary = CopperMod.Amiga.CopperStart.Runtime.BootInstructionBoundary;
using CopperStartBootInstructionBoundaryContext = CopperMod.Amiga.CopperStart.Runtime.BootInstructionBoundaryContext;

namespace CopperMod.Amiga
{
    internal enum AmigaBootRunMode
    {
        StopAfterBootDiskRead,
        ContinueAfterBootDiskRead
    }

    internal enum KickstartRomExecTakeoverState
    {
        Disabled,
        Pending,
        Active,
        Unavailable
    }

    internal sealed partial class AmigaBootController :
        ICyberGraphicsGuestServices,
        CopperMod.Amiga.CopperStart.Graphics.Portable.IGraphicsTransactionalBitMapBackend,
        CopperMod.Amiga.CopperStart.Graphics.Portable.IGraphicsValidatedLayerRasterBackend
    {
        public const uint BootBlockAddress = 0x0007_C000;
        public const uint BootEntryAddress = BootBlockAddress + 0x0C;
        public const uint BootIoRequestAddress = 0x0000_0800;
        public const int CmdRead = 2;
        private const int TdMotor = 9;
        private const int IoCommandOffset = 0x1C;
        private const int IoErrorOffset = 0x1F;
        private const int IoActualOffset = 0x20;
        private const int IoLengthOffset = 0x24;
        private const int IoDataOffset = 0x28;
        private const int IoOffsetOffset = 0x2C;
        private const uint DosResidentAddress = 0x0000_3400;
        private const uint DosResidentNameAddress = DosResidentAddress + 0x40;
        private const uint DosResidentIdAddress = DosResidentAddress + 0x50;
        private const uint DosResidentInitAddress = 0x00F2_0100;
        private const uint WorkbenchRootLock = 0x00F8_0000;
        // Exec's LibList header begins at 0x17A and occupies 14 bytes.
		private const int ExecBaseImageSize = (int)global::Amiga.ExecBase.Size;
        private const int ExecSoftVerOffset = 0x22;
        private const int ExecLowMemChkSumOffset = 0x24;
        private const int ExecChkBaseOffset = 0x26;
        private const int ExecSysStkUpperOffset = 0x36;
        private const int ExecSysStkLowerOffset = 0x3A;
        private const int ExecMaxLocMemOffset = 0x3E;
        private const int ExecMaxExtMemOffset = 0x4E;
        private const int ExecChkSumOffset = 0x52;
        private const int ExecThisTaskOffset = 0x114;
        private const int ExecTaskTrapCodeOffset = 0x130;
        private const int ExecTaskTrapAllocOffset = 0x140;
        private const int ExecResModulesOffset = 0x12C;
        private const int ExecTaskReadyOffset = 0x196;
        private const int ExecTaskWaitOffset = 0x1A4;
        private const byte ExecNestingEnabled = 0xFF;
        private const int ExecPortListOffset = 0x188;
        private const int ExecIntrListOffset = 0x16C;
        private const int ExecMemListOffset = 0x142;
        private const int ExecFirstLvo = -6;
        private const int ExecAddHeadLvo = -240;
        private const int ExecResourceListOffset = 0x150;
        private const int ExecDeviceListOffset = 0x15E;
        private const int ExecLibListOffset = 0x17A;
        private const int TaskNodeTypeOffset = 0x08;
        private const int TaskNodeNameOffset = 0x0A;
        private const int TaskSigAllocOffset = 0x12;
        private const int TaskSigWaitOffset = 0x16;
        private const int TaskSigRecvdOffset = 0x1A;
        private const int TaskStateOffset = 0x0F;
        private const int TaskTrapAllocOffset = 0x22;
        private const int TaskTrapAbleOffset = 0x24;
        private const int TaskTrapCodeOffset = 0x32;
        private const int TaskStackPointerOffset = 0x36;
        private const int TaskStackLowerOffset = 0x3A;
        private const int TaskStackUpperOffset = 0x3E;
        private const int MemNodeNameOffset = 0x0A;
        private const int MemHeaderAttributesOffset = 0x0E;
        private const int MemHeaderFirstChunkOffset = 0x10;
        private const int MemHeaderLowerOffset = 0x14;
        private const int MemHeaderUpperOffset = 0x18;
        private const int MemHeaderFreeOffset = 0x1C;
        private const int MemChunkNextOffset = 0x00;
        private const int MemChunkBytesOffset = 0x04;
        private const uint MemfPublic = 0x0000_0001;
        private const uint MemfChip = 0x0000_0002;
        private const uint MemfFast = 0x0000_0004;
        private const uint Memf24BitDma = 0x0000_0200;
        private const uint MemfKick = 0x0000_0400;
        private const uint MemfClear = 0x0001_0000;
        private const uint MemfLargest = 0x0002_0000;
        private const uint MemfReverse = 0x0004_0000;
        private const uint MemfTotal = 0x0008_0000;
        private const uint MemfNoExpunge = 0x8000_0000;
        private const uint AbsExecBaseAddress = 0x0000_0004;
        private const uint BootChipPublicLowerAddress = 0x0000_0400;
        private const uint BootSupervisorStackTopAddress = 0x0000_0400;
        private const uint DosProgramReturnAddress = 0x00FF_FFFC;
        private const uint SafeInterruptReturnAddress = 0x00F0_7F00;
        private const uint TaskTrapDispatcherBaseAddress = 0x00F0_8000;
        private const uint DefaultTaskTrapCodeAddress = 0x00F0_8100;
        private const uint ExecInterruptContinuationAddress = 0x00F0_8200;
        private const uint ExecLibraryCallContinuationAddress = 0x00F0_8300;
        // Keep library completion distinct from the formatter callback below:
        // both owners may be registered during the same boot session.
        private const uint ExecMakeLibraryContinuationAddress = 0x00F0_8410;
        private const uint RawDoFmtContinuationAddress = 0x00F0_8400;
        private const uint ExecWaitResumeGatewayAddress = 0x00F0_8500;
        private const uint ExecDefaultTaskFinalizerAddress = 0x00F0_8520;
        private const uint ClipboardHookContinuationAddress = 0x00F0_8930;
		private const uint ExecSupervisorContinuationAddress = 0x00F0_8B00;
		private const uint MemoryHandlerReturnSentinel = 0x00F0_8940;
		private const int MaximumMemoryHandlerInstructions = 1_000_000;
        private const int BusErrorVector = 2;
        private const int AddressErrorVector = 3;
        private const int IllegalInstructionVector = 4;
        private const int PrivilegeViolationVector = 8;
        private const int LineAVector = 10;
        private const int LineFVector = 11;
        private const uint BootPseudoFastMetadataSize = 0x0000_0200;
        private const uint BootKickstartRomPseudoFastReserve = 0x0001_0000;
        private const uint BootPseudoFastStackReserve = 0x0000_1000;
        private const uint BootRealFastMetadataSize = 0x0000_0200;
        private const uint BootChipOnlyPrivateMetadataSize = 0x0000_1000;
        private const uint BootPseudoFastCurrentTaskOffset = 0x0000_0100;
        private const uint BootChipOnlyMemHeaderOffset = 0x0000_0100;
        private const uint BootChipOnlyMemNameOffset = 0x0000_0180;
        private const ushort Kickstart13SoftVer = 34;
        private const int ViewViewPortOffset = 0x00;
        private const int ViewLofCprListOffset = 0x04;
        private const int ViewShfCprListOffset = 0x08;
        private const int ViewDyOffsetOffset = CopperStartGraphicsLayouts.ViewDyOffset;
        private const int ViewDxOffsetOffset = CopperStartGraphicsLayouts.ViewDxOffset;
        private const short ViewDefaultDyOffset = CopperStartGraphicsLayouts.ViewDefaultDyOffset;
        private const short ViewDefaultDxOffset = CopperStartGraphicsLayouts.ViewDefaultDxOffset;
        private const int GfxBaseActiViewOffset = CopperStartGraphicsLayouts.GfxBaseActiView;
        private const int GfxBaseChipRevBits0Offset = CopperStartGraphicsLayouts.GfxBaseChipRevBits0;
        private const int ViewStructSize = 0x12;
        private const int CprListStartOffset = 0x04;
        // Keep enough chip-program space for a complete bounded ViewPort
        // chain.  A single generated OCS/ECS viewport normally fits in the
        // historical 0x100-byte span, but MrgCop composes the source streams
        // in-place and inserts one WAIT separator per additional viewport.
        // The host path admits up to 64 linked nodes, so the per-list buffer
        // must not make an otherwise valid five-or-more-node chain fail just
        // because the first stream was allocated at the old singleton size.
        private const int CompatibilityCopperListSize = 0x400;
        // AGA eight-plane screens publish eight 32-colour banks twice
        // (high/low nibbles), in addition to the normal display stream.
        private const int AgaCompatibilityCopperListSize = 0x1000;
        private const int MaxUserCopperLists = 64;
        private const int MaxUserCopperInstructions = 4096;
        private const int ScreenFirstWindowOffset = CopperStartGraphicsLayouts.ScreenFirstWindow;
        private const int ScreenLeftEdgeOffset = CopperStartGraphicsLayouts.ScreenLeftEdge;
        private const int ScreenTopEdgeOffset = CopperStartGraphicsLayouts.ScreenTopEdge;
        private const int ScreenWidthOffset = CopperStartGraphicsLayouts.ScreenWidth;
        private const int ScreenHeightOffset = CopperStartGraphicsLayouts.ScreenHeight;
        private const int ScreenFlagsOffset = CopperStartGraphicsLayouts.ScreenFlags;
        // Amiga 68k pointers are word-aligned in the public Screen prefix.
        // Keep the canonical title/default-title words and expose Screen.Font
        // before the existing ViewPort/RastPort/BitMap offsets.
        private const int ScreenTitleOffset = CopperStartGraphicsLayouts.ScreenTitle;
        private const int ScreenDefaultTitleOffset = CopperStartGraphicsLayouts.ScreenDefaultTitle;
        private const int ScreenFontOffset = CopperStartGraphicsLayouts.ScreenFont;
        private const int ScreenStructSize = CopperStartGraphicsLayouts.ScreenSize;
        private const int ScreenViewPortOffset = CopperStartGraphicsLayouts.ScreenViewPort;
        private const int ScreenRastPortOffset = CopperStartGraphicsLayouts.ScreenRastPort;
        private const int ScreenBitMapOffset = CopperStartGraphicsLayouts.ScreenBitMap;
        private const int ScreenDetailPenOffset = CopperStartGraphicsLayouts.ScreenDetailPen;
        private const int ScreenBlockPenOffset = CopperStartGraphicsLayouts.ScreenBlockPen;
        private const int NewWindowLeftEdgeOffset = CopperStartGraphicsLayouts.NewWindowLeftEdge;
        private const int NewWindowTopEdgeOffset = CopperStartGraphicsLayouts.NewWindowTopEdge;
        private const int NewWindowWidthOffset = CopperStartGraphicsLayouts.NewWindowWidth;
        private const int NewWindowHeightOffset = CopperStartGraphicsLayouts.NewWindowHeight;
        private const int NewWindowIdcmpFlagsOffset = CopperStartGraphicsLayouts.NewWindowIdcmpFlags;
        private const int NewWindowFirstGadgetOffset = CopperStartGraphicsLayouts.NewWindowFirstGadget;
        private const int NewWindowScreenOffset = CopperStartGraphicsLayouts.NewWindowScreen;
        private const int NewWindowTypeOffset = CopperStartGraphicsLayouts.NewWindowType;
        private const int WindowNextOffset = CopperStartGraphicsLayouts.WindowNext;
        private const int WindowWScreenOffset = CopperStartGraphicsLayouts.WindowWScreen;
        private const int WindowRPortOffset = CopperStartGraphicsLayouts.WindowRPort;
        private const int WindowFirstGadgetOffset = CopperStartGraphicsLayouts.WindowFirstGadget;
        private const int WindowIdcmpFlagsOffset = CopperStartGraphicsLayouts.WindowIdcmpFlags;
        private const int WindowUserPortOffset = CopperStartGraphicsLayouts.WindowUserPort;
        private const int MsgPortTypeOffset = 0x08;
        private const int MsgPortFlagsOffset = 0x0E;
        private const int MsgPortSigBitOffset = 0x0F;
        private const int MsgPortSigTaskOffset = 0x10;
        private const int MsgPortMsgListOffset = 0x14;
        private const int NodeSuccessorOffset = 0x00;
        private const int NodePredecessorOffset = 0x04;
        private const int NodeNameOffset = 0x0A;
        private const int LibraryVersionOffset = 0x14;
        private const int LibraryOpenCountOffset = 0x20;
        private const int MessageReplyPortOffset = 0x0E;
        private const int GadgetNextOffset = 0x00;
        private const int GadgetLeftEdgeOffset = 0x04;
        private const int GadgetTopEdgeOffset = 0x06;
        private const int GadgetWidthOffset = 0x08;
        private const int GadgetHeightOffset = 0x0A;
        private const int GadgetIdOffset = 0x26;
        private const int RastPortBitMapOffset = 0x04;
        private const int RastPortMaskOffset = 0x18;
        private const int RastPortFgPenOffset = 0x19;
        private const int RastPortBgPenOffset = 0x1A;
        private const int RastPortDrawModeOffset = 0x1C;
        private const int RastPortLinePatternOffset = 0x22;
        private const int RastPortCurrentXOffset = 0x24;
        private const int RastPortCurrentYOffset = 0x26;
        private const int RastPortPenWidthOffset = 0x30;
        private const int RastPortPenHeightOffset = 0x32;
        private const int RastPortFontOffset = 0x34;
        private const int RastPortTextHeightOffset = 0x3A;
        private const int RastPortTextWidthOffset = 0x3C;
        private const int RastPortTextBaselineOffset = 0x3E;
        private const int RastPortTextSpacingOffset = 0x40;
        private const int ViewPortDspInsOffset = 0x08;
        private const int ViewPortSprInsOffset = 0x0C;
        private const int ViewPortClrInsOffset = 0x10;
        private const int ViewPortUCopInsOffset = 0x14;
        private const int ViewPortNextOffset = CopperStartGraphicsLayouts.ViewPortNext;
        private const int ViewPortDWidthOffset = 0x18;
        private const int ViewPortDHeightOffset = 0x1A;
        private const int ViewPortDxOffsetOffset = 0x1C;
        private const int ViewPortDyOffsetOffset = 0x1E;
        private const int ViewPortModesOffset = 0x20;
        private const int ViewPortRasInfoOffset = 0x24;
        private const int RasInfoNextOffset = 0x00;
        private const int RasInfoBitMapOffset = 0x04;
        private const int RasInfoRxOffsetOffset = 0x08;
        private const int RasInfoRyOffsetOffset = 0x0A;
        private const int BitMapBytesPerRowOffset = 0x00;
        private const int BitMapRowsOffset = 0x02;
        private const int BitMapFlagsOffset = 0x04;
        private const int BitMapDepthOffset = 0x05;
        private const int BitMapPlanesOffset = 0x08;
        private const uint BitMapAttributeHeight = 0;
        private const uint BitMapAttributeDepth = 4;
        private const uint BitMapAttributeWidth = 8;
        private const uint BitMapAttributeFlags = 12;
        private const uint BitMapFlagClear = 1;
        private const uint BitMapFlagStandard = 1u << 3;
        private const uint BitMapFlagMinPlanes = 1u << 4;
        private const int NewScreenLeftOffset = 0x00;
        private const int NewScreenTopOffset = 0x02;
        private const int NewScreenWidthOffset = 0x04;
        private const int NewScreenHeightOffset = 0x06;
        private const int NewScreenDepthOffset = 0x08;
        private const int NewScreenDetailPenOffset = 0x0A;
        private const int NewScreenBlockPenOffset = 0x0B;
        private const int NewScreenViewModesOffset = 0x0C;
        private const int NewScreenFontOffset = CopperStartGraphicsLayouts.NewScreenFont;
        private const int NewScreenDefaultTitleOffset = CopperStartGraphicsLayouts.NewScreenDefaultTitle;
        private const int NewScreenStructMinimumSize = 0x10;
        private const ushort ViewModeHires = 0x8000;
        private const ushort ViewModeInterlace = 0x0004;
        private const ushort ViewModePlayfieldBitAssignment = 0x0040;
        private const ushort ViewModeExtraHalfBrite = 0x0080;
        private const ushort ViewModeSuperHires = 0x0020;
        private const ushort ViewModeDualPlayfield = 0x0400;
        private const ushort ViewModeHam = 0x0800;
        private const ushort SyntheticScreenBehind = 0x0080;
        private const ushort SyntheticScreenQuiet = 0x0100;
        private const ushort SyntheticScreenAutoScroll = 0x4000;
        private const ushort SyntheticScreenShowTitle = 0x0010;
        private const ushort SyntheticScreenPenShared = 0x0400;
        // intuition/screens.h screen-type values.  CUSTOMSCREEN is not zero:
        // the low nibble is published in Screen.Flags and callers use it when
        // cloning a screen through GetScreenData().
        private const ushort CustomScreenType = 0x000F;
        private const ushort WorkbenchScreenType = 1;
        private const ushort PublicScreenType = 2;
        private const ushort SyntheticScreenHires = 0x0200;
        private const ushort SyntheticScreenSupportedFlags = 0x479F;
        private const ushort Bplcon0SuperHires = 0x0040;
        private const ushort Bplcon0DualPlayfield = 0x0400;
        private const ushort Bplcon0Ham = 0x0800;
        // AGA extends the BPU field with BPLCON0 bit 4. Bits 14..12 still
        // carry BPU0..2, so an eight-plane playfield encodes as $0010.
        private const ushort Bplcon0AgaBitplane8 = 0x0010;
        // AGA BPLCON3 resets PF2OF2..0 to %011, preserving the classic
        // playfield-two COLOR08 offset. Four-plus-four dual playfields use
        // %100 so PF2 starts at COLOR16.
        private const ushort AgaBplcon3Default = 0x0C00;
        private const ushort AgaBplcon3DualPlayfield16ColorOffset = 0x1000;
        // The standard OCS/ECS display DMA fetch field is six bits wide:
        // at most 64 words (1024 lo-res pixels) can be fetched per line.
        // Keep this boundary explicit so a malformed ViewPort cannot be
        // silently clamped into a different display geometry.
        private const int MaxCopperFetchWords = 64;
        private const int MaxCopperDisplayWidth = MaxCopperFetchWords * 16;
        private const int MaxCopperSuperHiresWidth = MaxCopperDisplayWidth * 2;
        private const int MaxCopperDisplayHeight = 512;
        private const ushort Bplcon2Playfield2Priority = 0x0040;
        private const int SyntheticScreenDefaultDepth = 2;
        private const int SyntheticScreenDefaultHeight = 256;
        private const int SyntheticScreenTitleHeight = 24;
        // SA_Colors and SA_Colors32 may describe every native palette entry
        // individually.  Admit 256 data records plus the required terminal
        // record, rather than treating the old 64-record host convenience
        // limit as a public screen-open constraint.
        private const int SyntheticPaletteMaximumEntries = 256;
        private const int SyntheticPaletteMaximumRecords =
            SyntheticPaletteMaximumEntries + 1;
        private const int SyntheticScreenTitleMaxLength = 80;
        private const int SyntheticPresentationLeft = AmigaConstants.PalLowResOverscanBorderX * 2;
        private const int SyntheticPresentationTop = AmigaConstants.PalLowResOverscanBorderY * 2;
        private const uint SyntheticTagDone = 0;
        private const uint SyntheticTagIgnore = 1;
        private const uint SyntheticTagMore = 2;
        private const uint SyntheticTagSkip = 3;
        private const uint ScreenTagLeft = 0x8000_0021;
        private const uint ScreenTagTop = 0x8000_0022;
        private const uint ScreenTagErrorCode = 0x8000_002A;
        private const uint ScreenTagTitle = 0x8000_0028;
        private const uint ScreenTagFont = 0x8000_002B;
        private const uint ScreenTagSysFont = 0x8000_002C;
        private const uint ScreenTagColors = 0x8000_0029;
        private const uint ScreenTagCustomBitMap = 0x8000_002E;
        // Public-screen tags carry Intuition list/task ownership that is not
        // representable by the reset-scoped synthetic screen session.
        private const uint ScreenTagPubName = 0x8000_002F;
        private const uint ScreenTagPubSig = 0x8000_0030;
        private const uint ScreenTagPubTask = 0x8000_0031;
        private const uint ScreenTagDClip = 0x8000_0033;
        private const uint ScreenTagOverscan = 0x8000_0034;
        private const uint ScreenTagObsolete1 = 0x8000_0035;
        private const uint ScreenTagShowTitle = 0x8000_0036;
        private const uint ScreenTagBehind = 0x8000_0037;
        private const uint ScreenTagQuiet = 0x8000_0038;
        private const uint ScreenTagAutoScroll = 0x8000_0039;
        private const uint ScreenTagPens = 0x8000_003A;
        private const uint ScreenTagFullPalette = 0x8000_003B;
        private const uint ScreenTagColorMapEntries = 0x8000_003C;
        private const uint ScreenTagParent = 0x8000_003D;
        private const uint ScreenTagDraggable = 0x8000_003E;
        private const uint ScreenTagExclusive = 0x8000_003F;
        private const uint ScreenTagDetailPen = 0x8000_0026;
        private const uint ScreenTagBlockPen = 0x8000_0027;
        private const uint ScreenTagBackFill = 0x8000_0041;
        private const uint ScreenTagColors32 = 0x8000_0043;
        private const uint ScreenTagVideoControl = 0x8000_0044;
        private const uint ScreenTagFrontChild = 0x8000_0045;
        private const uint ScreenTagBackChild = 0x8000_0046;
        private const uint ScreenTagLikeWorkbench = 0x8000_0047;
        private const uint ScreenTagReserved = 0x8000_0048;
        private const uint ScreenTagMinimizeIsg = 0x8000_0049;
        private const uint ScreenTagInterleaved = 0x8000_0042;
        private const uint ScreenTagSharePens = 0x8000_0040;
        // The first implementation of this host shim used these five
        // post-boolean values.  Keep accepting them at the tag boundary so
        // existing guests continue to work while new callers use the
        // Kickstart values above.
        private const uint LegacyScreenTagPens = 0x8000_004A;
        private const uint LegacyScreenTagShowTitle = 0x8000_0044;
        private const uint LegacyScreenTagBehind = 0x8000_0045;
        private const uint LegacyScreenTagQuiet = 0x8000_0046;
        private const uint LegacyScreenTagAutoScroll = 0x8000_0047;
        private const uint NoTitleChange = 0xFFFF_FFFF;
        private const uint OpenScreenErrorNoMemory = 3;
        private const uint OpenScreenErrorNoChipMemory = 4;
        private const uint OpenScreenErrorTooDeep = 7;
        private const uint IdcmpGadgetDown = 0x0000_0020;
        private const uint IdcmpGadgetUp = 0x0000_0040;
        private const int VBlankInterruptNumber = 5;
        private const uint CustomRegisterBaseAddress = 0x00DF_F000;
        private const uint CustomInterruptRequestAddress =
            CustomRegisterBaseAddress + 0x009C;
        private const int InterruptDataOffset = 0x0E;
        private const int InterruptCodeOffset = 0x12;
        private const int GraphicsDBufInfoSafeMessageOffset = 0x08;
        private const int GraphicsDBufInfoDispMessageOffset = 0x28;
        private const int GraphicsDBufInfoSize = 0x54;
        private const int ExecMessageReplyPortOffset = 0x0E;

        private readonly Machine _machine;
        private readonly CyberGraphicsLibrary? _cyberGraphics;
        private readonly CyberGraphicsRtgFirmware? _cyberGraphicsFirmware;
        private readonly IAmigaDiskDmaEngine _diskDma;
        private readonly CopperStartBootInstructionBoundary _instructionBoundary;
        private readonly List<AmigaBootDiagnostic> _diagnostics = new List<AmigaBootDiagnostic>(16);
        private readonly Dictionary<string, string> _dosAssigns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _ramDirectorySources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<uint, uint> _taskPendingSignals = new Dictionary<uint, uint>();
        private readonly Dictionary<uint, uint> _taskAllocatedSignals = new Dictionary<uint, uint>();
        private readonly CopperStartSyntheticUiInputState _syntheticUiInput = new();
        private readonly CopperStartSyntheticUiDisplayState _syntheticUiDisplay = new(
            AmigaConstants.PalLowResWidth, SyntheticScreenDefaultHeight, SyntheticScreenDefaultDepth);
        private readonly List<SyntheticInterruptServer> _syntheticVBlankInterruptServers = new List<SyntheticInterruptServer>();
        private readonly List<PendingGraphicsDoubleBufferMessage> _pendingGraphicsDoubleBufferMessages = new List<PendingGraphicsDoubleBufferMessage>();
        private readonly Queue<int> _pendingExecInterruptSources = new Queue<int>();
        private ushort _observedExecInterruptBits;
        private int _activeExecInterruptSource = -1;
        private int _activeExecInterruptServerIndex;
        private List<SyntheticInterruptServer>? _activeExecInterruptServers;
        private M68kCpuState? _execInterruptReturnState;
        private uint _execInterruptReturnProgramCounter;
        private readonly Dictionary<uint, int> _allocatedRtgBitMaps = new Dictionary<uint, int>();
        private readonly Dictionary<uint, uint> _compatibilityCopperByCprList = new Dictionary<uint, uint>();
        private readonly Dictionary<uint, uint> _compatibilityCprListByViewPort = new Dictionary<uint, uint>();
        // CalcIVG counts the display (DspIns) copper list only.  The raw
        // compatibility stream also carries optional UCopIns instructions,
        // so retain the generated prefix count beside each raw list instead
        // of accidentally charging user copper to the inter-viewport gap.
        private readonly Dictionary<uint, int> _compatibilityDisplayInstructionCountByCopper =
            new Dictionary<uint, int>();
        private bool _bootDiskReadCompleted;
        private bool _copperStartRuntimeHandoffPrepared;
        private long _copperStartRuntimeHandoffCount;
        private bool _dosBootContinuationStarted;
        private bool _dosBootBlockHeaderProbeEnabled;
        private int _hostAllocationDiagnosticCount;
        private int _openLibraryDiagnosticCount;
        private int _iconDiagnosticCount;
        private int _uiDiagnosticCount;
        private int _execDiagnosticCount;
        private int _hostFreeDiagnosticCount;
        private int _intuitionTitleDiagnosticCount;
        private IReadOnlyList<string> _workbenchToolTypes = Array.Empty<string>();
        private string _workbenchDefaultToolPath = "C/SystemTakeover";
        private string _workbenchCurrentDirectory = string.Empty;
        private int _workbenchStackSize = 4096;
        private int? _workbenchLanguageSelectionIndex;
        private bool _workbenchLanguageSelectionApplied;
        private uint _workbenchDiskObjectAddress;
        private uint _syntheticScreenAddress { get => _syntheticUiDisplay.ScreenAddress; set => _syntheticUiDisplay.ScreenAddress = value; }
        private uint _syntheticScreenErrorCodeAddress;
        private uint _syntheticScreenOpenErrorCode;
        // CompleteSyntheticScreenOpen stages the optional SA_ErrorCode
        // destination before the final display reconstruction. Keep a
        // request-scoped copy until Intuition's OpenScreen boundary commits
        // or rolls back that reconstruction.
        private uint _syntheticScreenFinalErrorCodeAddress;
        private uint _syntheticScreenFinalErrorCode;
        private uint _syntheticScreenVideoControlTags;
        private uint _syntheticScreenColorMapEntries;
        private bool _syntheticScreenConfigurationRejected;
        private bool _syntheticWindowConfigurationRejected;
        private uint _syntheticWindowScreenTargetAddress;
        private uint _syntheticWindowAddress { get => _syntheticUiDisplay.WindowAddress; set => _syntheticUiDisplay.WindowAddress = value; }
        private uint _syntheticUserPortAddress { get => _syntheticUiDisplay.UserPortAddress; set => _syntheticUiDisplay.UserPortAddress = value; }
        private uint _syntheticMessageAddress { get => _syntheticUiDisplay.MessageAddress; set => _syntheticUiDisplay.MessageAddress = value; }
        private uint _syntheticHostObjectAddress { get => _syntheticUiDisplay.HostObjectAddress; set => _syntheticUiDisplay.HostObjectAddress = value; }
        private uint _syntheticViewAddress { get => _syntheticUiDisplay.ViewAddress; set => _syntheticUiDisplay.ViewAddress = value; }
        private uint _syntheticRasInfoAddress { get => _syntheticUiDisplay.RasInfoAddress; set => _syntheticUiDisplay.RasInfoAddress = value; }
        private uint _syntheticSecondRasInfoAddress { get => _syntheticUiDisplay.SecondRasInfoAddress; set => _syntheticUiDisplay.SecondRasInfoAddress = value; }
        private uint _syntheticBitMapAddress { get => _syntheticUiDisplay.BitMapAddress; set => _syntheticUiDisplay.BitMapAddress = value; }
        private uint _syntheticCustomBitMapAddress { get => _syntheticUiDisplay.CustomBitMapAddress; set => _syntheticUiDisplay.CustomBitMapAddress = value; }
        private uint _syntheticColorMapAddress { get => _syntheticUiDisplay.ColorMapAddress; set => _syntheticUiDisplay.ColorMapAddress = value; }
        private uint _syntheticRastPortAddress { get => _syntheticUiDisplay.RastPortAddress; set => _syntheticUiDisplay.RastPortAddress = value; }
        private uint _syntheticFontAddress { get => _syntheticUiDisplay.FontAddress; set => _syntheticUiDisplay.FontAddress = value; }
        private uint _syntheticScreenFontAttrAddress { get => _syntheticUiDisplay.ScreenFontAttrAddress; set => _syntheticUiDisplay.ScreenFontAttrAddress = value; }
        private uint _syntheticScreenTextFontAddress { get => _syntheticUiDisplay.ScreenTextFontAddress; set => _syntheticUiDisplay.ScreenTextFontAddress = value; }
        private bool _syntheticScreenTextFontOpened { get => _syntheticUiDisplay.ScreenTextFontOpened; set => _syntheticUiDisplay.ScreenTextFontOpened = value; }
        private uint _syntheticPlaneAddress { get => _syntheticUiDisplay.PlaneAddress; set => _syntheticUiDisplay.PlaneAddress = value; }
        private uint _syntheticGadgetListAddress { get => _syntheticUiDisplay.GadgetListAddress; set => _syntheticUiDisplay.GadgetListAddress = value; }
        private uint _syntheticUserPortSignalMask { get => _syntheticUiDisplay.UserPortSignalMask; set => _syntheticUiDisplay.UserPortSignalMask = value; }
        private uint _syntheticIdcmpFlags { get => _syntheticUiDisplay.IdcmpFlags; set => _syntheticUiDisplay.IdcmpFlags = value; }
        private int _syntheticScreenWidth { get => _syntheticUiDisplay.ScreenWidth; set => _syntheticUiDisplay.ScreenWidth = value; }
        private int _syntheticScreenHeight { get => _syntheticUiDisplay.ScreenHeight; set => _syntheticUiDisplay.ScreenHeight = value; }
        private int _syntheticScreenDepth { get => _syntheticUiDisplay.ScreenDepth; set => _syntheticUiDisplay.ScreenDepth = value; }
        private int _syntheticScreenLeft { get => _syntheticUiDisplay.ScreenLeft; set => _syntheticUiDisplay.ScreenLeft = value; }
        private int _syntheticScreenTop { get => _syntheticUiDisplay.ScreenTop; set => _syntheticUiDisplay.ScreenTop = value; }
        private byte _syntheticScreenDetailPen { get => _syntheticUiDisplay.ScreenDetailPen; set => _syntheticUiDisplay.ScreenDetailPen = value; }
        private byte _syntheticScreenBlockPen { get => _syntheticUiDisplay.ScreenBlockPen; set => _syntheticUiDisplay.ScreenBlockPen = value; }
        private ushort _syntheticScreenFlags { get => _syntheticUiDisplay.ScreenFlags; set => _syntheticUiDisplay.ScreenFlags = value; }
        private bool _syntheticScreenInterleaved { get => _syntheticUiDisplay.ScreenInterleaved; set => _syntheticUiDisplay.ScreenInterleaved = value; }
        private uint _syntheticScreenDefaultTitleAddress { get => _syntheticUiDisplay.ScreenDefaultTitleAddress; set => _syntheticUiDisplay.ScreenDefaultTitleAddress = value; }
        private int _syntheticWindowLeft { get => _syntheticUiDisplay.WindowLeft; set => _syntheticUiDisplay.WindowLeft = value; }
        private int _syntheticWindowTop { get => _syntheticUiDisplay.WindowTop; set => _syntheticUiDisplay.WindowTop = value; }
        private int _syntheticWindowWidth { get => _syntheticUiDisplay.WindowWidth; set => _syntheticUiDisplay.WindowWidth = value; }
        private int _syntheticWindowHeight { get => _syntheticUiDisplay.WindowHeight; set => _syntheticUiDisplay.WindowHeight = value; }
        private ushort _syntheticScreenViewModes { get => _syntheticUiDisplay.ScreenViewModes; set => _syntheticUiDisplay.ScreenViewModes = value; }
        private uint _currentViewAddress;
        // Keep a reset-scoped topology fingerprint for the last successful
        // LoadView publication.  The portable graphics core uses the same
        // bounded View/ViewPort/RasInfo/BitMap envelope so a repeated request
        // can repair the guest ActiView sidecar without queueing a duplicate
        // custom-chip handoff.  A missing or sparse envelope simply disables
        // this optimization; the normal resolver remains authoritative.
        private uint _publishedViewAddress;
        private ulong _publishedViewSignature;
        private bool _hasPublishedViewSignature;
        private uint _graphicsChipRevBits0;
        // LoadView is a display ownership hand-off, not an ordinary register
        // poke.  Keep the most recent request until the next complete frame so
        // an HLE caller cannot splice two Copper lists into one presentation.
        private uint _pendingCopperList;
        private uint _pendingLongFrameCopperList;
        private uint _pendingShortFrameCopperList;
        private bool _pendingCopperListValid;
        private bool _pendingCopperListHasFieldVariants;
        private long _pendingCopperListCycle = -1;
        private ushort[] _syntheticPalette => _syntheticUiDisplay.Palette;
        private uint[] _syntheticPaletteRgb32 => _syntheticUiDisplay.PaletteRgb32;
        private bool _syntheticPaletteLoaded { get => _syntheticUiDisplay.PaletteLoaded; set => _syntheticUiDisplay.PaletteLoaded = value; }
        private bool _syntheticPaletteRgb32Loaded { get => _syntheticUiDisplay.PaletteRgb32Loaded; set => _syntheticUiDisplay.PaletteRgb32Loaded = value; }
        // SA_Colors/SA_Colors32 are decoded before the public Screen envelope
        // is allocated. Keep their mutation request-scoped until the complete
        // OpenScreen/RethinkDisplay transaction commits; a failed open must
        // not leak a caller's palette into the next synthetic session.
        private ushort[]? _syntheticPaletteTransactionSnapshot;
        private uint[]? _syntheticPaletteRgb32TransactionSnapshot;
        private bool _syntheticPaletteTransactionLoaded;
        private bool _syntheticPaletteRgb32TransactionLoaded;
        private uint _chipMemHeaderAddress;
        private uint _fastMemHeaderAddress;
        private uint _chipMemNameAddress;
        private uint _fastMemNameAddress;
        private uint _pseudoFastMemHeaderAddress;
        private uint _pseudoFastMemNameAddress;
        private uint _currentTaskAddress;
        private uint _chipMemLower;
        private uint _chipMemUpper;
        private uint _fastMemLower;
        private uint _fastMemUpper;
        private uint _pseudoFastMemLower;
        private uint _pseudoFastMemUpper;
        private uint _tlsfChipControlAddress;
        private uint _tlsfFastControlAddress;
        private uint _tlsfPseudoFastControlAddress;
        private bool _memoryListInstalled;
        private readonly AmigaDosFileSystem?[] _dosFileSystems = new AmigaDosFileSystem?[4];
        private readonly List<StartupSequenceCommand> _startupSequenceCommands = new List<StartupSequenceCommand>();
        private int _startupSequenceCommandIndex;
        private bool _startupSequenceActive;
        private int _startupSequenceFailAt = 10;
        private bool _kickstartRomBootActive;
        private bool _nativeGraphicsOpenPending;
        private uint _nativeGraphicsLibraryBase;
        private CopperStartExecLibraryServices? _romExecLibraryServices;
        private CopperStartExecListServices? _execListServices;
        private readonly CopperStartTaskScheduler _taskScheduler = new CopperStartTaskScheduler();
        private readonly CopperStartExecTaskServices _execTaskServices;
        private readonly CopperStartExecPortServices _execPortServices;
        private readonly CopperStartExecMemoryServices _execMemoryServices;
        private readonly CopperStartExecPoolServices _execPoolServices;
        private readonly CopperStartGuestMemory _guestMemory;
        private readonly CopperStartGraphicsMemoryAdapter _portableGraphicsMemory;
        private readonly CopperStartGraphicsFontList _graphicsFontList;
        private readonly CopperStartGraphicsFontBackend _syntheticFontBackend;
        private readonly CopperStartExecContext _execContext;
        private readonly CopperStartGraphicsBlitterScheduler _graphicsBlitterScheduler;
        private readonly CopperStartGraphicsContext _graphicsContext;
        private readonly CopperStartLayersHostServices _layersHostServices;
        private readonly CopperStartGraphicsServices _graphicsServices;
        private readonly CopperStartSyntheticDisplayServices _syntheticDisplayServices;
        private readonly CopperStartDosServices _dosServices;
        private readonly CopperStartExecSignalServices _execSignalServices;
        private readonly CopperStartExecTrapServices _execTrapServices;
        private readonly CopperStartExecNameServices _execNameServices;
        private readonly CopperStartExecResidentServices _execResidentServices;
        private readonly CopperStartCopperOsResidentServices _copperOsResidentServices;
        private readonly CopperStartRuntime _copperStartRuntime;
        private readonly CopperStartWorkbenchServices _workbenchServices;
        private readonly CopperStartIntuitionServices _intuitionServices;
        private readonly CopperStartIconServices _iconServices;
        private readonly CopperStartExpansionServices _expansionServices;
        private readonly CopperStartExecGatewayServices _execGatewayServices;
        private readonly CopperStartExecLibraryGatewayServices _execLibraryGatewayServices;
        private readonly CopperStartTaskTrapRuntime _taskTrapRuntime;
        private readonly CopperStartTaskTrapRecovery _taskTrapRecovery;
        private readonly CopperStartExecFormatServices _execFormatServices;
        private readonly CopperStartExecInitStructServices _execInitStructServices;
        private readonly CopperStartExecMakeLibraryServices _execMakeLibraryServices;
        private readonly CopperStartExecSemaphoreServices _execSemaphoreServices;
        private readonly CopperStartExecIoServices _execIoServices;
        private readonly CopperStartTrackdiskDeviceServices _trackdiskDeviceServices;
        private readonly CopperStartTimerDeviceServices _timerDeviceServices;
        private readonly CopperStartAudioDeviceServices _audioDeviceServices;
        private readonly CopperStartKeyboardDeviceServices _keyboardDeviceServices;
        private readonly CopperStartInputDeviceServices _inputDeviceServices;
        private bool _suppressInputDeviceOverlayForTest;
        private readonly CopperStartGameportDeviceServices _gameportDeviceServices;
        private readonly CopperStartConsoleDeviceServices _consoleDeviceServices;
        private readonly CopperStartClipboardDeviceServices _clipboardDeviceServices;
        private readonly CopperStartUtilityLibraryServices _utilityLibraryServices;
        private uint _activeExecBase;
        private KickstartRomExecTakeoverState _kickstartRomExecTakeoverState;
        private readonly CopperStartRuntimeInstructionBoundary _runtimeInstructionBoundary;
        private readonly PortableExecMemory.ExecMemoryAllocatorKind _memoryAllocator;
		private readonly Func<uint> _portableCurrentTask;
		private readonly Action<uint> _portableMemoryAlert;
		private readonly Func<M68kCpuState?, global::Amiga.APTR,
			global::Amiga.APTR, global::Amiga.APTR, global::Amiga.APTR,
			global::Amiga.MemoryHandlerResult> _portableMemoryHandler;
		private readonly Action<global::Amiga.APTR> _portableExpungeLibraries;
		private M68kCpuState? _activeExecMemoryGatewayState;

		private static CopperStart.Dos.ConfiguredDosVolumeSet?
			BuildConfiguredDosVolumes(
				IReadOnlyList<CopperStart.Dos.DosHostVolumeMount>? mounts)
		{
			if (mounts is null || mounts.Count == 0) return null;
			var result = new CopperStart.Dos.ConfiguredDosVolumeSet();
			foreach (var mount in mounts)
			{
				if (!CopperStart.Dos.ConfiguredDosVolume.TryCreate(
					mount.Name, mount.Root,
					mount.Writable
						? CopperStart.Dos.ConfiguredDosVolumeAccess.ReadWrite
						: CopperStart.Dos.ConfiguredDosVolumeAccess.ReadOnly,
					mount.MetadataRoot, out var volume, out var status) ||
					volume is null || !result.TryAdd(volume, out status))
					throw new ArgumentException(
						$"Invalid configured DOS volume '{mount.Name}': {status}.",
						nameof(mounts));
			}
			return result;
		}

        public AmigaBootController(Machine machine, IAmigaDiskDmaEngine? diskDma = null,
            PortableExecMemory.ExecMemoryAllocatorKind memoryAllocator = PortableExecMemory.ExecMemoryAllocatorKind.Classic,
            IReadOnlyList<CopperStart.Dos.DosHostVolumeMount>? configuredDosVolumes = null)
        {
            _machine = machine ?? throw new ArgumentNullException(nameof(machine));
			var configuredVolumeSet = BuildConfiguredDosVolumes(configuredDosVolumes);
            _memoryAllocator = memoryAllocator;
			// Portable allocator calls are hot for Layers topology. Cache the
			// instance delegates once; recreating the lightweight platform struct
			// must not allocate four delegate objects per guest allocation/free.
			_portableCurrentTask = GetCurrentTaskAddress;
			_portableMemoryAlert = alert => _diagnostics.Add(
				new AmigaBootDiagnostic(
					"AMIGA_EXEC_MEMORY_ALERT",
					$"Exec allocator alert ${alert:X8}."));
			_portableMemoryHandler = InvokeGuestMemoryHandler;
			_portableExpungeLibraries = ExpungeDelayedLibraries;
            _validatedRasterVisibility = IsValidatedRasterPointVisible;
            _validatedRasterPolyFirstDotPolicy =
                ShouldConsumeLayerPolyFirstDot;
            _guestMemory = new CopperStartGuestMemory(_machine.Bus);
			_portableGraphicsMemory = new CopperStartGraphicsMemoryAdapter(_guestMemory);
			_graphicsBlitterScheduler = new CopperStartGraphicsBlitterScheduler(
				_machine.Bus,
				StartGuestExecSubroutine);
			_validatedLayerRasterMemory = new ValidatedLayersRasterMemory(this);
			_validatedLayerDrawBoundsBackend =
				new ValidatedLayerDrawBoundsBackend(this);
			_syntheticUiInput.Bind(_guestMemory, TryAllocateProgramMemory);
            _graphicsFontList = new CopperStartGraphicsFontList(
                _portableGraphicsMemory,
                AmigaKickstartHost.GraphicsLibraryBase);
            _syntheticFontBackend = new CopperStartGraphicsFontBackend(
                _portableGraphicsMemory,
                defaultFont: EnsureSyntheticFont,
                fontList: _graphicsFontList);
            _syntheticDisplayServices = new CopperStartSyntheticDisplayServices(_guestMemory, _syntheticUiDisplay, SyntheticGlyph);
            _execContext = new CopperStartExecContext(
                _guestMemory, GetActiveExecBase, GetCurrentTaskAddress, ReadNullTerminatedString,
                MoveTaskToList, _taskScheduler.RequestDispatch, SuspendCurrentTaskThroughNativeExecScheduler, ExecWaitResumeGatewayAddress,
                address => _machine.Bus.IsCpuPhysicalAddressMapped(address, 2, AmigaBusAccessKind.CpuInstructionFetch),
				_taskScheduler.Register, RemovePortableTask, StartGuestExecSubroutine,
                () => _kickstartRomExecTakeoverState == KickstartRomExecTakeoverState.Active,
                () => _syntheticScreenAddress != 0,
                (name, nameAddress) => TryGetHostLibraryBase(name, nameAddress, out var libraryBase) ? libraryBase : 0,
                new CopperStartExecMemoryOperations(
                    AllocatePortableMemory,
                    FreePortableMemory,
                    (address, byteCount) => _machine.Bus.ClearMemory(address, byteCount)),
                () => { EnsureDosResident(); return DosResidentAddress; });
            _execContext.BindTaskExecutionOwner(_taskScheduler, _machine.Cpu,
                () => _activeExecInterruptSource, _machine.Options.CpuBackend);
            _execContext.SystemStackBounds = () =>
            {
                var execBase = GetActiveExecBase();
                if (_execContext.UsesRomExec() || execBase == 0 ||
                    !_machine.Bus.IsMappedMemoryRange(execBase + ExecSysStkLowerOffset, 4) ||
                    !_machine.Bus.IsMappedMemoryRange(execBase + ExecSysStkUpperOffset, 4) ||
                    _machine.Bus.ReadLong(execBase + ExecSysStkLowerOffset) != 0 ||
                    _machine.Bus.ReadLong(execBase + ExecSysStkUpperOffset) != BootSupervisorStackTopAddress)
                    return null;
                return (0u, BootSupervisorStackTopAddress);
            };
            _graphicsContext = new CopperStartGraphicsContext(
                _guestMemory,
                state => _ = WaitForNextFrame(state),
                (address, value, cycles) => _machine.Bus.WriteWord(address, value, cycles),
                viewPort => _cyberGraphics?.SelectFrontViewPort(viewPort),
                () =>
                {
                    // CyberGraphX owns RTG scanout and its own display
                    // publication.  Only the native planar path reaches the
                    // CopperStart copper rebuild boundary here.
                    if (IsSelectedRtgViewActive())
                    {
                        return;
                    }

                    _ = HostRethinkDisplay(_machine.Cpu.State.Cycles);
                },
                InitializeSyntheticViewPort,
                IsMappedRastPort,
                EnsureSyntheticFont,
                LogUiCall,
                BltBitMap,
                ClipBlit,
                BltBitMapRastPort,
                state => AllocateBitMap(state),
                FreeBitMap,
                GetBitMapAttr,
                (viewPort, bitMap) => _cyberGraphics?.ChangeViewPortBitMap(viewPort, bitMap) == true ? 0u : 1u,
                MergeCopperLists,
                MakeViewPort,
                LoadView,
                LoadRgb4,
                SetRgb4,
                EnsureSyntheticHostObject,
                DrawRastPort,
                DrawRastPortText,
                SetRastPort,
                FillRastPort,
                cycle => unchecked((ushort)(_machine.Bus.GetBeamPosition(cycle).BeamLine & 0x01FF)),
                WaitForViewportBottom,
                _execContext.MemoryOperations.Allocate,
                _execContext.MemoryOperations.Free,
                GetViewPortModeId,
                bitMap => _cyberGraphics?.IsRtgBitMap(bitMap) == true,
                rastPort => _cyberGraphics?.IsRtgRastPort(rastPort) == true,
                freeCprList: FreeCompatibilityCprList,
                freeVPortCopLists: FreeCompatibilityViewPortCopLists,
                fontList: _graphicsFontList,
                calcIvg: CalcIvg,
                setChipRev: SetChipRev,
                scheduleDoubleBufferMessages: ScheduleDoubleBufferMessages,
                cancelDoubleBufferMessages: CancelDoubleBufferMessages,
                tryLoadView: TryLoadView,
                defaultMonitorNtsc: _machine.Bus.Chipset.VideoStandard == VideoStandard.Ntsc,
                supportsEcsDisplay: _machine.Bus.Chipset.SupportsEcsDisplayRegisters,
                supportsAgaDisplay: SupportsAgaPlanarDisplay(),
                requestViewportDisplayRebuild: _ =>
                {
                    // ScrollVPort has already been classified as a native
                    // planar viewport by the graphics gateway.  Keep the
                    // viewport-aware seam explicit for CopperScreen/native
                    // hosts while retaining the same causal rethink point.
                    if (IsSelectedRtgViewActive())
                        return;

                    _ = HostRethinkDisplay(_machine.Cpu.State.Cycles);
                },
                graphicsLibraryBase: GetGraphicsLibraryBase(),
                tryFreeCprList: TryFreeCompatibilityCprList,
                tryFreeVPortCopLists: TryFreeCompatibilityViewPortCopLists,
                requestDisplayRebuildTimed: cycle =>
                {
                    // Timed graphics vectors carry the guest call cycle
                    // explicitly.  Do not substitute the host CPU state's
                    // last observed cycle when the gateway is invoked
                    // directly by a native-overlay or CopperScreen caller.
                    if (IsSelectedRtgViewActive())
                        return;

                    _ = HostRethinkDisplay(cycle);
                },
                requestViewportDisplayRebuildTimed: (_, cycle) =>
                {
                    if (IsSelectedRtgViewActive())
                        return;

                    _ = HostRethinkDisplay(cycle);
                },
                transactionalBitMaps: this,
                validatedLayerRaster: this,
                preferProviderBitMapAllocations: () => _machine.Bus.RtgVram.Active,
                timedBlitter: _graphicsBlitterScheduler,
                currentTask: GetCurrentTaskAddress,
                isDisplayDmaRange: (address, byteCount) =>
                    byteCount <= int.MaxValue &&
                    _guestMemory.IsDisplayDmaRange(address, (int)byteCount),
                supportsMonitorNameAllocation: true,
                isCompatibilityLoadView: IsCompatibilityLoadView,
                setRgb32: SetRgb32);
            _validatedRasterGraphicsAllocator =
                new CopperStartGraphicsAllocator(_graphicsContext);
            _layersHostServices = new CopperStartLayersHostServices(
                _guestMemory,
                _graphicsContext,
                _execContext.MemoryOperations.Allocate,
                _execContext.MemoryOperations.Free,
                GetActiveExecBase,
                GetCurrentTaskAddress,
                SuspendLayersTask,
                WakeLayersTask,
                message => AddExecLikeDiagnostic("AMIGA_BOOT_LAYERS_CALLBACK_RETURN", message));
            _graphicsServices = new CopperStartGraphicsServices(
                _graphicsContext,
                allowGuestOwnedDbufInfo: true,
                layers: _layersHostServices);
            _dosServices = new CopperStartDosServices(new CopperStartDosContext(
                _guestMemory,
                WorkbenchRootLock,
                ReadDosPath,
                path => TryReadDosFile(path, out var data) ? data : null,
                path => TryFindDosEntry(path, out var entry) ? entry : null,
				ListDosDirectory,
                WriteFileInfoBlock,
				AllocatePortableMemory,
				_execContext.MemoryOperations.Free,
				GetCurrentTaskAddress,
				SuspendLayersTask,
				WakeLayersTask,
				global::CopperStart.Dos.DosAbiProfile.Unified,
				ReadMemoryText,
				(code, message) => _diagnostics.Add(new AmigaBootDiagnostic(code, message)),
				StartGuestExecSubroutine,
				TerminateCurrentDosTask,
				configuredVolumeSet,
				PutDosGuestMessage,
				TakeDosGuestMessage,
				ReplyQueuedDosGuestMessage,
				getExecMemoryContext: GetDosExecMemoryContext,
				registerDosTask: RegisterDosTaskContext,
				removeDosTask: RemoveDosTaskContext,
				requestTaskDispatch: _taskScheduler.RequestDispatch,
				replyGuestMessage: ReplyDosGuestMessage));
			_execTaskServices = new CopperStartExecTaskServices(_execContext, GetExecListServices());
            _execSignalServices = new CopperStartExecSignalServices(_execContext,
                () => UsesGuestExecTasks);
            _execMemoryServices = new CopperStartExecMemoryServices(new CopperStartExecMemoryContext(
                _machine.Bus, AllocatePortableMemory, AllocatePortableAbsoluteMemory, FreePortableMemory,
                QueryPortableAvailableMemory, AllocatePortableFromHeader, DeallocatePortableToHeader, TypeOfPortableMemory,
                RecordAllocDiagnostic, RecordAllocAbsDiagnostic, RecordFreeDiagnostic,
                GetActiveExecBase, _memoryAllocator, GetCurrentTaskAddress,
                alert => _diagnostics.Add(new AmigaBootDiagnostic("AMIGA_EXEC_MEMORY_ALERT", $"Exec allocator alert ${alert:X8}.")),
                InvokeGuestMemoryHandler, ExpungeDelayedLibraries, state => _activeExecMemoryGatewayState = state));
			_execPortServices = new CopperStartExecPortServices(
				_execContext, GetExecListServices(), _execSignalServices, _execMemoryServices.Context);
			_execSemaphoreServices = new CopperStartExecSemaphoreServices(_execContext, _execSignalServices, _execPortServices);
			_execTaskServices.SetTaskRetirementCleanup(task =>
				_ = _layersHostServices.TaskDied(task));
			_execTaskServices.SetTaskPoolCleanup(task =>
				_execMemoryServices.TaskRemoved(task));
			_execTaskServices.SetTaskRetirementBarrier(_dosServices.ReleaseTask);
			_execTaskServices.SetMemoryContext(_execMemoryServices.Context);
            _execPoolServices = new CopperStartExecPoolServices(_execMemoryServices);
            _execTrapServices = new CopperStartExecTrapServices(_execContext);
            _execNameServices = new CopperStartExecNameServices(_execContext, GetExecListServices());
            _execInitStructServices = new CopperStartExecInitStructServices(_execContext);
            _execMakeLibraryServices = new CopperStartExecMakeLibraryServices(
                _execContext, _execMemoryServices.Context, ExecMakeLibraryContinuationAddress);
            _execResidentServices = new CopperStartExecResidentServices(
                _execContext, GetExecListServices(), _execMemoryServices.Context, ExecMakeLibraryContinuationAddress);
            _copperOsResidentServices = new CopperStartCopperOsResidentServices(_execContext);
            _utilityLibraryServices = new CopperStartUtilityLibraryServices(new CopperStartUtilityContext(
                _machine.Bus, _guestMemory, _execContext.MemoryOperations.Allocate, _execContext.MemoryOperations.Free,
                StartGuestExecSubroutine, _execPortServices.ReplyMessage));
			_execIoServices = new CopperStartExecIoServices(
				_execContext, _execSignalServices, _execPortServices, _execMemoryServices.Context,
				HostDoIo, HostAbortIo);
            _execSignalServices.SetIoActiveProbe(_execIoServices.IsActive, _execIoServices.CompleteIo);
            _trackdiskDeviceServices = new CopperStartTrackdiskDeviceServices(
                _machine.Bus,
				_execMemoryServices.Context,
                GetTrackdiskData,
                TryWriteTrackdiskData,
                GetTrackdiskRawTrack,
                TryWriteTrackdiskRawTrack,
                GetTrackdiskChangeVersion,
                EjectTrackdiskDrive,
                IsTrackdiskWriteProtected,
                IsTrackdiskMotorOn,
                SetTrackdiskMotor,
                ReplyTrackdiskMessage,
                message => _diagnostics.Add(new AmigaBootDiagnostic("AMIGA_TRACKDISK", message)));
            _timerDeviceServices = new CopperStartTimerDeviceServices(
                _machine.Bus,
				_execMemoryServices.Context,
                ReplyTrackdiskMessage,
                message => _diagnostics.Add(new AmigaBootDiagnostic("AMIGA_TIMER", message)));
            _audioDeviceServices = new CopperStartAudioDeviceServices(
                _machine.Bus,
				_execMemoryServices.Context,
                ReplyTrackdiskMessage,
                message => _diagnostics.Add(new AmigaBootDiagnostic("AMIGA_AUDIO", message)));
            _keyboardDeviceServices = new CopperStartKeyboardDeviceServices(
                _machine.Bus,
				_execMemoryServices.Context,
                ReplyTrackdiskMessage,
                message => _diagnostics.Add(new AmigaBootDiagnostic("AMIGA_KEYBOARD", message)),
                StartGuestExecSubroutine,
                RequestKeyboardReset);
            _inputDeviceServices = new CopperStartInputDeviceServices(_machine.Bus, _execMemoryServices.Context, ReplyTrackdiskMessage, StartGuestExecSubroutine, _keyboardDeviceServices.ConfigureKeyRepeat);
            _gameportDeviceServices = new CopperStartGameportDeviceServices(_machine.Bus, _execMemoryServices.Context, ReplyTrackdiskMessage);
            _consoleDeviceServices = new CopperStartConsoleDeviceServices(new CopperStartConsoleContext(
                _machine.Bus, _execContext.MemoryOperations, _inputDeviceServices, ReplyTrackdiskMessage, DrawConsoleText, StartGuestExecSubroutine));
            _clipboardDeviceServices = new CopperStartClipboardDeviceServices(
                _machine.Bus, _execContext.MemoryOperations, ReplyTrackdiskMessage, _execPortServices.PutMessage,
                StartGuestExecSubroutine, ClipboardHookContinuationAddress);
            _dosServices.AttachConsole(_consoleDeviceServices);
            _execFormatServices = new CopperStartExecFormatServices(
				_machine.Bus, _guestMemory, RawDoFmtContinuationAddress);
            _execGatewayServices = new CopperStartExecGatewayServices(
                LogExecCall, _execMemoryServices, _execTaskServices, GetExecListServices(), _execSignalServices,
                _execSemaphoreServices, _execTrapServices, _execPortServices, _execIoServices, _execPoolServices,
                new CopperMod.Amiga.CopperStart.Exec.ExecRegistryServices(
                    _execContext, GetExecListServices(), _execSignalServices, _execSemaphoreServices),
                new CopperMod.Amiga.CopperStart.Exec.ExecLibraryVectorServices(_execMemoryServices.Context),
                _execInitStructServices,
                _copperOsResidentServices.AddResident, AddInterruptServer, RemoveInterruptServer,
				SetInterruptVector, CauseSoftInterrupt,
                GetMessage, WaitPort, _execFormatServices.RawDoFmt,
				_execFormatServices.RawIoInit, _execFormatServices.RawMayGetChar,
				_execFormatServices.RawPutChar, StartGuestExecSubroutine, ExecSupervisorContinuationAddress,
				(startClass, version) => _diagnostics.Add(new AmigaBootDiagnostic("AMIGA_EXEC_INITCODE",
					$"InitCode class ${startClass:X8}, version {version} requested.")),
				alert => _diagnostics.Add(new AmigaBootDiagnostic("AMIGA_EXEC_ALERT", $"Exec Alert ${alert:X8}.")),
				flags => _diagnostics.Add(new AmigaBootDiagnostic("AMIGA_EXEC_DEBUG", $"Exec Debug ${flags:X8}.")),
				RequestKeyboardReset);
            _execLibraryGatewayServices = new CopperStartExecLibraryGatewayServices(
                () => _kickstartRomExecTakeoverState == KickstartRomExecTakeoverState.Active,
                OpenRomLibrary, CloseRomLibrary, AddRomLibrary, RemoveRomLibrary, AddRomDevice,
                RemoveRomDevice, OpenRomDevice, CloseRomDevice,
                AddRomResource, RemoveRomResource, OpenRomResource, OpenCompatibilityResource, OpenCompatibilityLibrary,
                AddCompatibilityLibrary, RemoveCompatibilityLibrary,
                CloseCompatibilityLibrary,
                AmigaKickstartHost.DosLibraryBase);
            _copperStartRuntime = new CopperStartRuntime(_machine.Bus, CreateExecServices);
            _workbenchServices = new CopperStartWorkbenchServices(new CopperStartWorkbenchContext(
                lvo => LogUiCall("workbench.library", lvo), EnsureSyntheticScreen, EnsureSyntheticHostObject));
            _intuitionServices = new CopperStartIntuitionServices(new CopperStartIntuitionContext(
                lvo => LogUiCall("intuition.library", lvo),
                screen => ConfigureSyntheticScreenFromNewScreen(screen),
                ConfigureSyntheticWindowFromNewWindow,
                CloseSyntheticScreen,
                ConfigureSyntheticScreenFromTagList,
                CompleteSyntheticScreenOpen,
                EnsureSyntheticScreen, EnsureSyntheticWindow, EnsureSyntheticView, EnsureSyntheticHostObject,
                OpenSyntheticWindow, CloseSyntheticWindow,
                () => _currentViewAddress, GetSyntheticScreenViewPortAddress, viewPort => CyberGraphics.SelectFrontViewPort(viewPort),
                AddSyntheticGadgetList, ModifySyntheticIdcmp, SetSyntheticWindowTitles, ShowSyntheticScreenTitle,
                GetSyntheticScreenData,
                QuerySyntheticOverscan,
                HostRethinkDisplay, _execMemoryServices.AllocMemAndStore,
                TryActivateSyntheticScreenToFront,
                TryActivateSyntheticScreenToFrontState,
                FinalizeSyntheticScreenOpen,
                GetSyntheticWindowViewPortAddress,
                validateNewScreenAddress: address =>
                    CanAddressField(address, ExtNewScreenExtensionOffset, sizeof(uint)),
                validateNewWindowAddress: address =>
                    CanAddressField(address, NewWindowTypeOffset, sizeof(ushort)),
                validateScreenAddress: address =>
                    CanAddressField(address, 0, ScreenStructSize),
                validateWindowAddress: address =>
                    CanAddressField(address, 0, CopperStartGraphicsLayouts.WindowSize),
                tryModifyIdcmp: TryModifySyntheticIdcmp,
                nextObject: address =>
                {
                    var platform = new CopperMod.Amiga.CopperStart.Intuition.IntuitionHostMemoryPlatform(
                        new CopperStartGuestMemory(_machine.Bus));
                    return PortableIntuition.BoopsiNextObjectCore.NextObject(
                        ref platform, global::Amiga.APTR.FromPointer(address)).Raw;
                },
                rethinkDisplayWithState: HostRethinkDisplayWithState));
            _iconServices = new CopperStartIconServices(new CopperStartIconContext(
                LogIconCall, new HostGuestMemory(_machine.Bus), EnsureWorkbenchDiskObject, FindToolTypeValue, ReadNullTerminatedString));
            _expansionServices = new CopperStartExpansionServices(new CopperStartExpansionContext(
                lvo => LogUiCall("expansion.library", lvo), EnsureSyntheticHostObject,
                MakeExpansionDosNode, AddExpansionDosNode));
            _taskTrapRuntime = new CopperStartTaskTrapRuntime(
                _machine.Bus, _machine.Cpu.State, () => _memoryListInstalled,
                GetCurrentTaskAddress, GetActiveExecBase, GetTaskTrapDispatcherAddress,
                HandleDefaultTaskTrap, DefaultTaskTrapCodeAddress,
                TaskTrapCodeOffset, ExecTaskTrapCodeOffset);
            _taskTrapRecovery = new CopperStartTaskTrapRecovery(
                _machine.Bus, IsZeroFilledInstructionTarget,
                message => _diagnostics.Add(new AmigaBootDiagnostic("AMIGA_BOOT_TASK_TRAP_INVALID", message)));
            _diskDma = diskDma ?? new ImmediateDiskDmaEngine();
            _instructionBoundary = new CopperStartBootInstructionBoundary(
                new CopperStartBootInstructionBoundaryContext(
                    _machine.Bus, _machine.Cpu.State, DosProgramReturnAddress,
                    () => _kickstartRomBootActive, TryActivateKickstartRomExecServices,
                    TryDispatchCopperStartTaskScheduler, CanExecuteCurrentCopperStartTask,
                    TryDispatchPendingExecInterruptServer,
                    _taskTrapRuntime.EnsureVectorsCurrent, EnsureHostLowMemoryPointersCurrent, EnsureSafeAutovectorsCurrent,
                    TryRecoverHostTaskTrapFromZeroVector, TryStartDosBootContinuation, RecordNativeKickstartNullPc,
                    TryContinueStartupSequence, SkipDosBootBlockHeaderIfNeeded, ApplyWorkbenchLanguageSelectionIfNeeded,
                    () => _bootDiskReadCompleted, () => _ = _machine.DispatchPendingHardwareInterrupt(),
                    GetNextSyntheticVBlankBoundaryCycle, AdvanceSyntheticVBlankInterruptServers,
                    GetNextHostDeviceBoundary));
            _runtimeInstructionBoundary = new CopperStartRuntimeInstructionBoundary(
                new CopperStartRuntimeInstructionBoundaryContext(
                    _machine.Bus, _machine.Cpu.State, () => _ = _machine.DispatchPendingHardwareInterrupt(),
                    GetNextSyntheticVBlankBoundaryCycle, AdvanceSyntheticVBlankInterruptServers,
                    ProcessHostDevices, TryDispatchCopperStartTaskScheduler,
                    CanExecuteCurrentCopperStartTask, GetNextHostDeviceBoundary));
            if (_machine.Bus.RtgVram.IsPresent)
            {
                _cyberGraphics = new CyberGraphicsLibrary(_machine.Bus);
                _cyberGraphicsFirmware = new CyberGraphicsRtgFirmware(_cyberGraphics);
                _machine.Bus.AttachRtgFirmware(_cyberGraphicsFirmware);
                _cyberGraphics.AttachGuestServices(this);
            }
        }

        public AmigaFloppyDrive Drive0 => _machine.Bus.Disk.Drive0;

        public AmigaFloppyDrive Drive1 => _machine.Bus.Disk.Drive1;

        public AmigaFloppyDrive Drive2 => _machine.Bus.Disk.Drive2;

        public AmigaFloppyDrive Drive3 => _machine.Bus.Disk.Drive3;

        internal void MixHostAudioSample(long cycle, Span<float> destination, int frameIndex, int channels)
            => _audioDeviceServices.MixSample(cycle, destination, frameIndex, channels);

        /// <summary>Accepts primary clipboard text from the host UI without touching guest state on the UI thread.</summary>
        public void QueueHostClipboardText(string text) => _clipboardDeviceServices.QueuePrimaryTextFromHost(text);

        /// <summary>Accepts a primary clipboard image from the host UI at the next safe boundary.</summary>
        public void QueueHostClipboardImage(CopperStartClipboardImage image) => _clipboardDeviceServices.QueuePrimaryImageFromHost(image);

        /// <summary>Retrieves one guest primary-clipboard update for publication by the host UI.</summary>
        public bool TryTakeHostClipboardText(out string text) => _clipboardDeviceServices.TryTakePrimaryTextForHost(out text);

        /// <summary>Retrieves one primary clipboard image decoded from guest ILBM data.</summary>
        public bool TryTakeHostClipboardImage(out CopperStartClipboardImage? image) => _clipboardDeviceServices.TryTakePrimaryImageForHost(out image);

        public IReadOnlyList<AmigaBootDiagnostic> Diagnostics => _diagnostics;

        internal CyberGraphicsLibrary CyberGraphics
            => _cyberGraphics ?? throw new InvalidOperationException("CyberGraphX is not enabled for this machine.");

        internal bool HasCyberGraphics => _cyberGraphics != null;

        internal bool HasCopperStartLayers => _layersHostServices.IsInstalled;

        internal void SuppressCopperStartInputDeviceOverlayForTest()
        {
            _suppressInputDeviceOverlayForTest = true;
            _inputDeviceServices.Reset();
        }

        internal uint CopperStartLayersLibraryBase => _layersHostServices.LibraryBase;

        internal uint CopperStartLayersRootAddress => _layersHostServices.RootAddress;

        internal int CopperStartLayersGatewayCount => _layersHostServices.GatewayRegistrationCount;
        internal int CopperStartLayersOpaqueAllocationCountForTest
            => _layersHostServices.OpaqueAllocationCountForTest;
        internal int CopperStartLayersOpaqueAllocationCapacityForTest
            => _layersHostServices.OpaqueAllocationCapacityForTest;
        internal int CopperStartLayersPendingWaitCountForTest
            => _layersHostServices.PendingWaitCountForTest;
        internal CopperMod.Amiga.CopperStart.Layers.LayersCallbackReturnFault
            CopperStartLayersLastCallbackReturnFaultForTest
            => _layersHostServices.LastCallbackReturnFault;
        internal bool CopperStartLayersHasPendingCallbackForTest
            => _layersHostServices.HasPendingCallbackForTest;
        internal PortableLayers.LayersCallbackIdentity CopperStartLayersPendingCallbackIdentityForTest
            => _layersHostServices.PendingCallbackIdentityForTest;
        internal uint CopperStartLayersPendingCallbackNonceForTest
            => _layersHostServices.PendingCallbackNonceForTest;
        internal uint AllocateCopperStartLayersOpaqueForTest(uint byteSize)
            => _layersHostServices.AllocateOpaqueForTest(byteSize);
        internal bool IsCopperStartLayersOpaqueAllocationForTest(
            uint address,
            uint expectedSize)
            => _layersHostServices.IsOpaqueAllocationForTest(
                address,
                expectedSize);
        internal void FreeCopperStartLayersOpaqueForTest(uint address)
            => _layersHostServices.FreeOpaqueForTest(address);
        internal PortableLayers.LayersAbiProfile CopperStartLayersProfile
            => _layersHostServices.Profile;
        internal bool CopperStartTaskDispatchPending => _taskScheduler.DispatchPending;
        internal uint CopperStartAvailablePublicMemory
            => QueryPortableAvailableMemory((uint)global::Amiga.Exec.MemoryFlags.Public);
        internal bool TryCopperStartLayersWritePixelForTest(
            uint rastPort,
            short x,
            short y)
            => _layersHostServices.TryWritePixel(rastPort, x, y);
        internal void ResetCopperStartLayersForTest()
            => _layersHostServices.Reset();
        internal bool ReinstallCopperStartLayersForTest(
            PortableLayers.LayersAbiProfile profile =
                PortableLayers.LayersAbiProfile.Unified)
            => _layersHostServices.TryInstall(
                GetActiveExecBase(),
                profile);
        internal void FailNextCopperStartLayersPixelTransactionAfterApplyForTest()
            => _layersHostServices.FailNextPixelTransactionAfterApplyForTest();
        internal int CopperStartLayersPixelTransactionCountForTest
            => _layersHostServices.PixelTransactionCountForTest;
        internal int CopperStartLayersPixelTransactionPoolCountForTest
            => _layersHostServices.PixelTransactionPoolCountForTest;
        internal long CopperStartLayersPixelTransactionRetainedSnapshotBytesForTest
            => _layersHostServices.PixelTransactionRetainedSnapshotBytesForTest;
        internal CopperStartLayersHostServices.AllocationFaultScope
            BeginCopperStartLayersMemoryAllocationFaultForTest(int failOrdinal)
            => _layersHostServices.BeginMemoryAllocationFaultForTest(failOrdinal);
        internal CopperStartLayersHostServices.AllocationFaultScope
            BeginCopperStartLayersBitMapAllocationFaultForTest(int failOrdinal)
            => _layersHostServices.BeginBitMapAllocationFaultForTest(failOrdinal);
        internal void FailNextCopperStartLayersOpaqueFreeForTest()
            => _layersHostServices.FailNextOpaqueFreeForTest();
        internal void FailNextCopperStartLayersRasterRetireLinkReadForTest(
            uint layerInfo)
            => _layersHostServices.FailNextRasterRetireLinkReadForTest(layerInfo);
        internal uint BeginCopperStartLayersPixelTransactionForTest(int operationCapacity)
            => _layersHostServices.BeginPixelTransactionForTest(operationCapacity);
        internal bool StageCopperStartLayersPixelBackfillForTest(
            uint transaction,
            uint rastPort,
            uint destinationBitMap,
            uint hook,
            int destinationX,
            int destinationY,
            int width,
            int height,
            int offsetX = 0,
            int offsetY = 0)
            => _layersHostServices.StagePixelBackfillForTest(
                transaction,
                rastPort,
                destinationBitMap,
                hook,
                destinationX,
                destinationY,
                width,
                height,
                offsetX,
                offsetY);
        internal bool ApplyCopperStartLayersPixelTransactionForTest(uint transaction)
            => _layersHostServices.ApplyPixelTransactionForTest(transaction);
        internal void CancelCopperStartLayersPixelTransactionForTest(uint transaction)
            => _layersHostServices.CancelPixelTransactionForTest(transaction);
        internal void CompleteCopperStartLayersPixelTransactionForTest(uint transaction)
            => _layersHostServices.CompletePixelTransactionForTest(transaction);
        internal void EnableCopperStartLayersProviderOperationTraceForTest()
            => _layersHostServices.EnableProviderOperationTraceForTest();
        internal int CaptureCopperStartLayersProviderOperationTraceForTest()
            => _layersHostServices.CaptureProviderOperationTraceForTest();
        internal int CopperStartLayersLastRasterProviderGuardCountForTest
            => _layersHostServices.LastRasterProviderGuardCountForTest;
        internal ushort CopperStartLayersLastRasterProviderPrimaryGuardDepthForTest
            => _layersHostServices.LastRasterProviderPrimaryGuardDepthForTest;
        internal ushort CopperStartLayersLastRasterProviderSecondaryGuardDepthForTest
            => _layersHostServices.LastRasterProviderSecondaryGuardDepthForTest;
        internal ulong CopperStartLayersRasterProviderExecutionMaskForTest
            => _layersHostServices.RasterProviderExecutionMaskForTest;
        internal ulong CopperStartLayersCompleteRasterProviderExecutionMaskForTest
            => CopperStartLayersHostServices.CompleteRasterProviderExecutionMaskForTest;
        internal int CopperStartLayersCompatibilityRasterDispatchCountForTest
            => _layersHostServices.CompatibilityRasterDispatchCountForTest;
        internal string DescribeCopperStartLayersProviderOperationTraceForTest(int start)
            => _layersHostServices.DescribeProviderOperationTraceForTest(start);
        internal string DescribeCopperStartLayersProviderOperationOrderForTest(int start)
            => _layersHostServices.DescribeProviderOperationOrderForTest(start);

        internal KickstartRomExecTakeoverState KickstartRomExecTakeoverState => _kickstartRomExecTakeoverState;

        internal bool TryRenderRtgFrame(out CyberGraphicsRtgFrame frame)
        {
            if (_cyberGraphics != null)
            {
                return _cyberGraphics.TryRenderRtgFrame(out frame);
            }

            frame = default;
            return false;
        }

        internal bool TryGetRtgComposition(out CyberGraphicsDisplayComposition composition)
        {
            if (_cyberGraphics != null)
            {
                return _cyberGraphics.TryBuildDisplayComposition(
                    _currentViewAddress,
                    _machine.Bus.Display.Width,
                    _machine.Bus.Display.Height,
                    out composition);
            }

            composition = default!;
            return false;
        }

        internal bool RtgScanoutSelected
            => TryGetRtgComposition(out _) || _cyberGraphics?.RtgScanoutSelected == true;

        internal void GetRtgPointerPosition(out int x, out int y)
        {
            x = _syntheticUiInput.MouseX;
            y = _syntheticUiInput.MouseY;
        }

        public bool AutoStartWorkbenchDefaultTool { get; set; } = true;

        public bool AutoRunStartupSequence { get; set; }

        public bool QueueHostKeyDown(AmigaRawKey key) => _keyboardDeviceServices.QueueKeyDown(key, _machine.Cpu.State.Cycles);

        public bool QueueHostKeyUp(AmigaRawKey key) => _keyboardDeviceServices.QueueKeyUp(key, _machine.Cpu.State.Cycles);

        public AmigaProgramLaunchRequest? PendingWorkbenchLaunchRequest { get; private set; }

        internal long CopperStartRuntimeHandoffCount => _copperStartRuntimeHandoffCount;

        internal bool TryPrepareCopperStartRuntimeHandoff()
        {
            if (_copperStartRuntimeHandoffPrepared ||
                _kickstartRomBootActive ||
                !_bootDiskReadCompleted ||
                _dosBootContinuationStarted ||
                _startupSequenceActive ||
                PendingWorkbenchLaunchRequest != null ||
                !HasEnteredLoadedProgram)
            {
                return false;
            }

            _taskTrapRuntime.EnsureVectorsCurrent();
            EnsureHostLowMemoryPointersCurrent();
            EnsureSafeAutovectorsCurrent();
            _copperStartRuntimeHandoffPrepared = true;
            _copperStartRuntimeHandoffCount++;
            return true;
        }

        private bool HasEnteredLoadedProgram
        {
            get
            {
                var pc = _machine.Cpu.State.ProgramCounter;
                return pc != 0 &&
                    (pc < BootBlockAddress || pc >= BootBlockAddress + 1024);
            }
        }

        public AmigaBootResult BootFromDisk(
            AmigaDiskImage disk,
            int maxInstructions = 20_000,
            AmigaBootRunMode runMode = AmigaBootRunMode.StopAfterBootDiskRead)
        {
            StartBootFromDisk(disk);
            return ExecuteBootBlock(maxInstructions, runMode);
        }

        public void StartBootFromDisk(AmigaDiskImage disk)
        {
            ArgumentNullException.ThrowIfNull(disk);
            ResetBootState(disk);
            ValidateBootBlock(disk.BootBlock);
            _machine.Bus.CopyToChipRam(BootBlockAddress, disk.BootBlock);
            _machine.Bus.WriteWord(BootIoRequestAddress + IoCommandOffset, CmdRead);
            var userStackTop = GetBootStackTopAddress();
            _machine.Cpu.Reset(BootEntryAddress, userStackTop);
            _machine.Cpu.State.ResetStackPointers(BootSupervisorStackTopAddress, userStackTop, supervisorMode: false);
            _machine.Cpu.State.A[1] = BootIoRequestAddress;
            _machine.Cpu.State.A[6] = AmigaKickstartHost.ExecLibraryBase;
        }

        public void SetSyntheticMousePosition(int x, int y)
        {
            _syntheticUiInput.SetMousePosition(x, y, _syntheticScreenWidth, _syntheticScreenHeight);
        }

        public void SetSyntheticMousePresentationPosition(int x, int y)
        {
            var screenX = ((_syntheticScreenViewModes & ViewModeHires) != 0 || _syntheticScreenWidth > AmigaConstants.PalLowResWidth)
                ? x - SyntheticPresentationLeft
                : (int)Math.Floor((x - SyntheticPresentationLeft) / 2.0);
            var screenY = (int)Math.Floor((y - SyntheticPresentationTop) / 2.0);
            SetSyntheticMousePosition(screenX - _syntheticWindowLeft, screenY - _syntheticWindowTop);
        }

        public void MoveSyntheticMouse(int deltaX, int deltaY)
        {
            SetSyntheticMousePosition(_syntheticUiInput.MouseX + deltaX, _syntheticUiInput.MouseY + deltaY);
        }

        public void SetSyntheticMouseButtons(bool primaryPressed, bool secondPressed)
        {
            if (!_syntheticUiInput.PrimaryMousePressed && primaryPressed && ShouldQueueSyntheticIdcmp(IdcmpGadgetDown, requireExplicitFlag: true))
            {
                QueueSyntheticGadgetMessageAtMouse(IdcmpGadgetDown);
            }

            if (_syntheticUiInput.PrimaryMousePressed && !primaryPressed)
            {
                QueueSyntheticGadgetMessageAtMouse(IdcmpGadgetUp);
            }

            _syntheticUiInput.SetPrimaryMousePressed(primaryPressed);
        }

        public AmigaBootResult BootFromKickstartRom(
            AmigaDiskImage disk,
            int maxInstructions = 20_000,
            AmigaBootRunMode runMode = AmigaBootRunMode.ContinueAfterBootDiskRead)
        {
            StartKickstartRomBoot(disk);
            return ExecuteBootBlock(maxInstructions, runMode);
        }

        public void StartKickstartRomBoot(AmigaDiskImage disk)
        {
            ArgumentNullException.ThrowIfNull(disk);
            StartKickstartRomBootCore(disk);
        }

        public void StartKickstartRomBoot()
        {
            StartKickstartRomBootCore(null);
        }

        private void StartKickstartRomBootCore(AmigaDiskImage? disk)
        {
            if (_machine.Kickstart.Configuration.Backend != KickstartBackendKind.RomImage)
            {
                throw new InvalidOperationException("Kickstart ROM boot requires a ROM-backed Kickstart configuration.");
            }

            ResetBootState(disk, installHostShim: false);
            _kickstartRomBootActive = true;
            _kickstartRomExecTakeoverState = _machine.Kickstart.Configuration.Version == KickstartVersion.Kickstart31
                ? KickstartRomExecTakeoverState.Pending
                : KickstartRomExecTakeoverState.Disabled;
            _dosBootBlockHeaderProbeEnabled = false;
            _machine.Kickstart.InstallRomImage(_machine.Bus);
            var rom = _machine.Kickstart.Configuration.RomImage.Span;
            if (rom.Length < 8)
            {
                throw new AmigaEmulationException("The Kickstart ROM image is too small to contain reset vectors.");
            }

            var supervisorStack = BigEndian.ReadUInt32(rom, 0, "Kickstart reset stack pointer");
            var resetProgramCounter = BigEndian.ReadUInt32(rom, 4, "Kickstart reset program counter");
            _machine.Cpu.Reset(resetProgramCounter, supervisorStack);
        }

        public void StartWorkbenchSession(AmigaDiskImage disk)
        {
            ResetBootState(disk);
            var userStackTop = GetBootStackTopAddress();
            _machine.Cpu.Reset(0, userStackTop);
            _machine.Cpu.State.ResetStackPointers(BootSupervisorStackTopAddress, userStackTop, supervisorMode: false);
            _machine.Cpu.State.A[6] = AmigaKickstartHost.ExecLibraryBase;
        }

        public void StartApplicationSession()
        {
            ResetBootState(null, installHostShim: true);
            var userStackTop = GetBootStackTopAddress();
            _machine.Cpu.Reset(0, userStackTop);
            _machine.Cpu.State.ResetStackPointers(BootSupervisorStackTopAddress, userStackTop, supervisorMode: false);
            _machine.Cpu.State.A[6] = AmigaKickstartHost.ExecLibraryBase;
        }

        private void ResetBootState(AmigaDiskImage disk)
        {
            ResetBootState(disk, installHostShim: true);
        }

        private void ResetBootState(CopperStartDiskImage? disk, bool installHostShim)
        {
            ResetNativeDosBootState();
            _layersHostServices.Reset();
            _graphicsBlitterScheduler.Reset();
            ResetValidatedRasterFragmentBuffers();
            _trackdiskDeviceServices.Reset();
            _timerDeviceServices.Reset();
            _audioDeviceServices.Reset();
            _keyboardDeviceServices.Reset();
            _inputDeviceServices.Reset();
            _gameportDeviceServices.Reset();
            _consoleDeviceServices.Reset();
            _clipboardDeviceServices.Reset();
            _utilityLibraryServices.Reset();
            _copperOsResidentServices.Reset();
            _copperStartRuntime.Reset();
            _graphicsServices.Dispose();
            _romExecLibraryServices = null;
            _execListServices = null;
            _execPoolServices.Reset();
            _execMakeLibraryServices.Reset();
            _execResidentServices.Reset();
            _execIoServices.Reset();
            _dosServices.Reset();
            _activeExecBase = 0;
            _kickstartRomExecTakeoverState = KickstartRomExecTakeoverState.Disabled;
            _nativeGraphicsOpenPending = false;
            _nativeGraphicsLibraryBase = 0;
            _diagnostics.Clear();
            _dosAssigns.Clear();
            _ramDirectorySources.Clear();
            _taskPendingSignals.Clear();
            _taskAllocatedSignals.Clear();
            _dosAssigns["ENVARC"] = "Prefs/Env-Archive";
            _dosAssigns["SYS"] = string.Empty;
            _syntheticVBlankInterruptServers.Clear();
            _pendingGraphicsDoubleBufferMessages.Clear();
            _pendingExecInterruptSources.Clear();
            _observedExecInterruptBits = 0;
            _activeExecInterruptSource = -1;
            _taskScheduler.Reset();
            _activeExecInterruptServerIndex = 0;
            _activeExecInterruptServers = null;
            _execInterruptReturnState = null;
            _execInterruptReturnProgramCounter = 0;
            _allocatedRtgBitMaps.Clear();
            _rtgOpenScreenContexts.Clear();
            _rtgIntuitionScreens.Clear();
            _rtgOpenScreenContinuationAddress = 0;
            _bootDiskReadCompleted = false;
            _copperStartRuntimeHandoffPrepared = false;
            _copperStartRuntimeHandoffCount = 0;
            _dosBootContinuationStarted = false;
            _dosBootBlockHeaderProbeEnabled = true;
            _hostAllocationDiagnosticCount = 0;
            _openLibraryDiagnosticCount = 0;
            _iconDiagnosticCount = 0;
            _uiDiagnosticCount = 0;
            _execDiagnosticCount = 0;
            _hostFreeDiagnosticCount = 0;
            _intuitionTitleDiagnosticCount = 0;
            _execTaskServices.Reset();
            _execSignalServices.Reset();
            _execSemaphoreServices.Reset();
            _workbenchToolTypes = Array.Empty<string>();
            _workbenchDefaultToolPath = "C/SystemTakeover";
            _workbenchCurrentDirectory = string.Empty;
            _workbenchStackSize = 4096;
            _workbenchLanguageSelectionIndex = null;
            _workbenchLanguageSelectionApplied = false;
            _workbenchDiskObjectAddress = 0;
            _syntheticUiDisplay.Reset();
            _syntheticWindowConfigurationRejected = false;
            _syntheticUiInput.Reset(AmigaConstants.PalLowResStandardWidth / 2, SyntheticScreenDefaultHeight / 2);
            _currentViewAddress = 0;
            _publishedViewAddress = 0;
            _publishedViewSignature = 0;
            _hasPublishedViewSignature = false;
            _graphicsChipRevBits0 = 0;
            _pendingCopperList = 0;
            _pendingLongFrameCopperList = 0;
            _pendingShortFrameCopperList = 0;
            _pendingCopperListValid = false;
            _pendingCopperListHasFieldVariants = false;
            _pendingCopperListCycle = -1;
            _chipMemHeaderAddress = 0;
            _fastMemHeaderAddress = 0;
            _chipMemNameAddress = 0;
            _fastMemNameAddress = 0;
            _pseudoFastMemHeaderAddress = 0;
            _pseudoFastMemNameAddress = 0;
            _currentTaskAddress = 0;
            _chipMemLower = 0;
            _chipMemUpper = 0;
            _fastMemLower = 0;
            _fastMemUpper = 0;
            _pseudoFastMemLower = 0;
            _pseudoFastMemUpper = 0;
            _tlsfChipControlAddress = 0;
            _tlsfFastControlAddress = 0;
            _tlsfPseudoFastControlAddress = 0;
            _memoryListInstalled = false;
            Array.Clear(_dosFileSystems);
            _startupSequenceCommands.Clear();
            _startupSequenceCommandIndex = 0;
            _startupSequenceActive = false;
            _startupSequenceFailAt = 10;
            _kickstartRomBootActive = false;
            PendingWorkbenchLaunchRequest = null;
            if (disk == null)
            {
                Drive0.Eject();
            }
            else
            {
                Drive0.Insert(disk);
            }

            Drive1.Eject();
            Drive2.Eject();
            Drive3.Eject();
            _machine.ResetHardware();
            _machine.Bus.StrictCpuPhysicalDataMapping = false;
            if (installHostShim)
            {
                PrimeBootDiskController();
                InstallBootHostTraps();
            }
        }

        private void PrimeBootDiskController()
        {
            _machine.Bus.WriteByte(0x00BFD100, 0xFF, 0);
            _machine.Bus.WriteByte(0x00BFD300, 0xFF, 0);
            _machine.Bus.WriteByte(0x00BFD100, 0x77, 0);
            _machine.Bus.WriteWord(0x00DFF096, 0x82D0, 0);
            _machine.Bus.WriteWord(0x00DFF024, 0x4000, 0);
            _machine.Bus.SynchronizePaulaThrough(0);
        }

        public AmigaBootResult ContinueExecution(int maxInstructions = 20_000)
        {
            return ExecuteBootBlock(maxInstructions, AmigaBootRunMode.ContinueAfterBootDiskRead);
        }

        public AmigaBootResult ContinueExecutionUntilCycle(
            long targetCycle,
            int maxInstructions = 100_000,
            Action<long, long>? beforeDeviceAdvance = null)
        {
            return ExecuteBootBlock(
                maxInstructions,
                AmigaBootRunMode.ContinueAfterBootDiskRead,
                targetCycle,
                reportOverrun: false,
                beforeDeviceAdvance,
                boundarySchedule: null);
        }

        /// <summary>
        /// A keyboard-originated reset must reset the CopperStart lifecycle as
        /// well as the machine.  Calling Machine.ResetHardware directly would
        /// clear bus gateway registrations while leaving their services marked
        /// installed, so the next ROM boot could lose its host device layer.
        /// </summary>
        private void RequestKeyboardReset()
        {
            // keyboard.device is active only after the corresponding boot
            // mode is known. Preserve DF0 media across the hardware reset.
            ResetBootState(Drive0.Disk, installHostShim: !_kickstartRomBootActive);
        }

        internal AmigaBootResult ContinueExecutionUntilCycle(
            long targetCycle,
            int maxInstructions,
            IAmigaExecutionBoundarySchedule boundarySchedule)
            => ExecuteBootBlock(
                maxInstructions,
                AmigaBootRunMode.ContinueAfterBootDiskRead,
                targetCycle,
                reportOverrun: false,
                beforeDeviceAdvance: null,
                boundarySchedule);

        public AmigaBootResult ContinueCopperStartRuntimeUntilCycle(
            long targetCycle,
            int maxInstructions = 100_000,
            Action<long, long>? beforeDeviceAdvance = null)
        {
            return ExecuteRuntime(
                maxInstructions,
                targetCycle,
                beforeDeviceAdvance,
                boundarySchedule: null);
        }

        internal AmigaBootResult ContinueCopperStartRuntimeUntilCycle(
            long targetCycle,
            int maxInstructions,
            IAmigaExecutionBoundarySchedule boundarySchedule)
            => ExecuteRuntime(
                maxInstructions,
                targetCycle,
                beforeDeviceAdvance: null,
                boundarySchedule);

        public static bool HasBootableShape(ReadOnlySpan<byte> bootBlock)
        {
            return bootBlock.Length >= 1024 &&
                bootBlock[0] == (byte)'D' &&
                bootBlock[1] == (byte)'O' &&
                bootBlock[2] == (byte)'S' &&
                IsBootBlockChecksumValid(bootBlock);
        }

        public static bool IsBootBlockChecksumValid(ReadOnlySpan<byte> bootBlock)
        {
            if (bootBlock.Length < 1024)
            {
                return false;
            }

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

            return sum == 0xFFFF_FFFF;
        }

        private void ValidateBootBlock(ReadOnlySpan<byte> bootBlock)
        {
            if (!HasBootableShape(bootBlock))
            {
                throw new AmigaEmulationException("The inserted disk does not contain a valid Amiga boot block.");
            }
        }

        private void InstallBootHostTraps()
        {
            var bus = _machine.Bus;
            // DOS is installed below as one complete shared-router surface.
            // Do not publish the host shim's four legacy compatibility
            // callbacks even transiently in this boot path.
            _machine.Kickstart.InstallHostShim(bus, CreateHostTrapTable(),
                installDosLibraryCallbacks: false);
            // ChipRevBits0 is the profile-facing capability source for both
            // the portable graphics database and generated 68k vectors. A
            // fresh AGA host shim must therefore publish its available bits
            // before a caller can query display information without first
            // issuing SetChipRev itself.
            _ = SetChipRev(CopperStartGraphicsChipRevision.SetBest);
            // The host shim now exposes the public GfxBase prefix.  Initialize
            // its TextFonts list before any compatibility font can be opened.
            _ = _graphicsFontList.Initialize(0);
            InstallSafeAutovectors(bus);
            InstallCopperStartExecGateways();
            InstallExecLibraryContinuations();
            bus.RegisterHostGateway(RawDoFmtContinuationAddress, _execFormatServices.Continue);
            bus.RegisterHostGateway(ExecWaitResumeGatewayAddress, ContinueHostWait);
			bus.RegisterHostGateway(CopperStartDosServices.BlockContinuationAddress,
				_dosServices.ContinueBlocked);
			bus.RegisterHostGateway(CopperStartDosServices.CallbackContinuationAddress,
				_dosServices.ContinueCallback);
            bus.RegisterHostGateway(ClipboardHookContinuationAddress, _clipboardDeviceServices.ContinueHook);
			bus.RegisterHostGateway(ExecSupervisorContinuationAddress, _execGatewayServices.ContinueSupervisor);
            _taskTrapRuntime.Install();
            for (var displacement = -6; displacement >= -1200; displacement -= 6)
            {
                var captured = displacement;
                bus.RegisterHostGateway(Lvo(AmigaKickstartHost.DosLibraryBase, captured), state => _dosServices.InvokeGeneric(state, captured));
            }
            for (var displacement = -6; displacement >= -1200; displacement -= 6)
            {
                var captured = displacement;
                bus.RegisterHostGateway(Lvo(AmigaKickstartHost.IconLibraryBase, captured), state => _iconServices.Invoke(state, captured));
            }

            for (var displacement = -6; displacement >= -1200; displacement -= 6)
            {
                var captured = displacement;
                bus.RegisterHostGateway(Lvo(AmigaKickstartHost.WorkbenchLibraryBase, captured), state => _workbenchServices.Invoke(state, captured));
            }

            for (var displacement = -6; displacement >= -1200; displacement -= 6)
            {
                var captured = displacement;
                bus.RegisterHostGateway(Lvo(AmigaKickstartHost.GraphicsLibraryBase, captured), state => _graphicsServices.InvokeGateway(state, captured));
                bus.RegisterHostGateway(Lvo(AmigaKickstartHost.IntuitionLibraryBase, captured), state => _intuitionServices.Invoke(state, captured));
                bus.RegisterHostGateway(Lvo(AmigaKickstartHost.ExpansionLibraryBase, captured), state => _expansionServices.Invoke(state, captured));
            }

            bus.RegisterHostGateway(Lvo(AmigaKickstartHost.IconLibraryBase, -78), _iconServices.GetDiskObject);
            bus.RegisterHostGateway(Lvo(AmigaKickstartHost.IconLibraryBase, IconLvo.FreeDiskObject), _iconServices.FreeDiskObject);
            bus.RegisterHostGateway(Lvo(AmigaKickstartHost.IconLibraryBase, -96), _iconServices.FindToolType);
            bus.RegisterHostGateway(Lvo(AmigaKickstartHost.IconLibraryBase, -102), _iconServices.MatchToolValue);
            bus.RegisterHostGateway(DosResidentInitAddress, _execLibraryGatewayServices.InitResident);
            bus.ConfigureAutoconfigFastRamForHost();
            bus.ConfigureAutoconfigRtgForHost();
            InstallKickstartMemoryList();
            if (!_layersHostServices.TryInstall(
                    GetActiveExecBase()))
            {
                throw new AmigaEmulationException(
                    "CopperStart could not install its layers.library owner.");
            }
            _ = _copperOsResidentServices.Install();
            if (bus.RtgVram.IsPresent)
            {
                var diagnosticCopy = AllocateMemoryFromMemList(
                    CyberGraphicsRtgFirmware.DiagAreaCopySize,
                    MemfPublic | MemfClear);
                if (diagnosticCopy == 0)
                {
                    throw new AmigaEmulationException("CopperStart could not allocate CyberGraphX diagnostic memory.");
                }

                _cyberGraphicsFirmware!.InstallHostShimResident(
                    bus,
                    diagnosticCopy,
                    AmigaKickstartHost.ExecLibraryBase);
            }
            InstallHostSupervisorStack();
        }

        private void InstallCopperStartExecGateways()
        {
            _activeExecBase = AmigaKickstartHost.ExecLibraryBase;
            _copperStartRuntime.InstallSyntheticExec(AmigaKickstartHost.ExecLibraryBase);
        }

        private void InstallExecLibraryContinuations()
        {
            // AddTask's default final PC is a terminal entry, not a direct
            // RemTask vector: a returned program may leave arbitrary A1/A6.
            _execContext.DefaultTaskFinalizerAddress = ExecDefaultTaskFinalizerAddress;
            _machine.Bus.RegisterHostGateway(ExecDefaultTaskFinalizerAddress, state =>
            {
                state.A[1] = 0;
                state.A[6] = GetActiveExecBase();
                var result = _execTaskServices.RemTask(state);
                // There is no caller return address above a task's initial stack.
                return result == M68kHostGatewayResult.Completed
                    ? M68kHostGatewayResult.BlockCurrentTask : result;
            });
            // Native library callbacks need the same completion owner in both
            // synthetic and ROM sessions. In particular, AUTOINIT publication
            // happens after its guest initializer returns, not in InitResident.
            _machine.Bus.RegisterHostGateway(ExecLibraryCallContinuationAddress, ContinueExecLibraryCall);
            _machine.Bus.RegisterHostGateway(ExecMakeLibraryContinuationAddress, state =>
            {
                _execMakeLibraryServices.Continue(state);
                _execResidentServices.Continue(state);
            });
        }

        private void TryActivateKickstartRomExecServices()
        {
            if (_kickstartRomExecTakeoverState != KickstartRomExecTakeoverState.Pending)
            {
                if (_kickstartRomExecTakeoverState == KickstartRomExecTakeoverState.Active)
                {
                    var publishedExecBase = _machine.Bus.ReadLong(AbsExecBaseAddress);
                    if (publishedExecBase != _activeExecBase ||
                        !IsValidKickstartRomExecBase(_activeExecBase))
                    {
                        ResetKickstartRomExecTakeoverForRomReinitialization();
                        return;
                    }

                    _trackdiskDeviceServices.TryInstall(_activeExecBase);
                    _timerDeviceServices.TryInstall(_activeExecBase);
                    if (!_suppressInputDeviceOverlayForTest)
                        _inputDeviceServices.TryInstall(_activeExecBase);
                    _keyboardDeviceServices.TryInstall(_activeExecBase);
                    _gameportDeviceServices.TryInstall(_activeExecBase);
                    _consoleDeviceServices.TryInstall(_activeExecBase);
                    _clipboardDeviceServices.TryInstall(_activeExecBase);
                    _utilityLibraryServices.TryInstall(_activeExecBase);
                    ProcessHostDevices();
                }

                return;
            }

            var execBase = _machine.Bus.ReadLong(AbsExecBaseAddress);
            if (execBase == 0)
            {
                return;
            }

            if (!IsValidKickstartRomExecBase(execBase))
            {
                // KS 3.1 publishes AbsExecBase before all of the structures
                // used by validation (notably ThisTask and MemList) are live.
                // Treat that as an in-progress ROM initialization and retry
                // at later instruction boundaries.  Marking it unavailable
                // here permanently prevented every later host-device install.
                return;
            }

            _copperStartRuntime.ActivateRomExec(execBase);
            _machine.Bus.RegisterHostGateway(ExecInterruptContinuationAddress, ContinueExecInterruptServer);
            InstallExecLibraryContinuations();
            _machine.Bus.RegisterHostGateway(RawDoFmtContinuationAddress, _execFormatServices.Continue);
            _machine.Bus.RegisterHostGateway(ExecWaitResumeGatewayAddress, ContinueHostWait);
			_machine.Bus.RegisterHostGateway(
				CopperStartDosServices.BlockContinuationAddress,
				_dosServices.ContinueBlocked);
			_machine.Bus.RegisterHostGateway(
				CopperStartDosServices.CallbackContinuationAddress,
				_dosServices.ContinueCallback);
            _machine.Bus.RegisterHostGateway(ClipboardHookContinuationAddress, _clipboardDeviceServices.ContinueHook);
			_machine.Bus.RegisterHostGateway(ExecSupervisorContinuationAddress, _execGatewayServices.ContinueSupervisor);
            _activeExecBase = execBase;
            _ = GetRomExecLibraryServices();
            _memoryListInstalled = true;
            _kickstartRomExecTakeoverState = KickstartRomExecTakeoverState.Active;
            TryInstallNativeGraphicsOverlayFromLibraryList(execBase);
            _trackdiskDeviceServices.TryInstall(execBase);
            _timerDeviceServices.TryInstall(execBase);
            _audioDeviceServices.TryInstall(execBase);
            if (!_suppressInputDeviceOverlayForTest)
                _inputDeviceServices.TryInstall(execBase);
            _keyboardDeviceServices.TryInstall(execBase);
            _gameportDeviceServices.TryInstall(execBase);
            _consoleDeviceServices.TryInstall(execBase);
            _clipboardDeviceServices.TryInstall(execBase);
            _utilityLibraryServices.TryInstall(execBase);
            _ = _copperOsResidentServices.Install();
        }

        private void ResetKickstartRomExecTakeoverForRomReinitialization()
        {
            // Kickstart can restart its resident initialization without returning
            // through StartKickstartRomBoot.  Host overlays belong to the previous
            // Exec instance and must disappear before the ROM clears and rebuilds
            // that instance's vector table.
            _layersHostServices.Reset();
            _graphicsBlitterScheduler.Reset();
            _trackdiskDeviceServices.Reset();
            _timerDeviceServices.Reset();
            _audioDeviceServices.Reset();
            _keyboardDeviceServices.Reset();
            _inputDeviceServices.Reset();
            _gameportDeviceServices.Reset();
            _consoleDeviceServices.Reset();
            _clipboardDeviceServices.Reset();
            _utilityLibraryServices.Reset();
            _copperOsResidentServices.Reset();
            _copperStartRuntime.Reset();
            _graphicsServices.Dispose();
            _romExecLibraryServices = null;
            _execListServices = null;
            _execPoolServices.Reset();
            _execMakeLibraryServices.Reset();
            _execResidentServices.Reset();
            _execIoServices.Reset();
            _activeExecBase = 0;
            _memoryListInstalled = false;
            _nativeGraphicsOpenPending = false;
            _nativeGraphicsLibraryBase = 0;
            _kickstartRomExecTakeoverState = KickstartRomExecTakeoverState.Pending;
        }

        private void ProcessHostDevices()
        {
            _trackdiskDeviceServices.ProcessPending(_machine.Cpu.State);
            _timerDeviceServices.ProcessPending(_machine.Cpu.State);
            _audioDeviceServices.ProcessPending(_machine.Cpu.State);
            _keyboardDeviceServices.ProcessPending(_machine.Cpu.State);
            _inputDeviceServices.ProcessPending();
            _gameportDeviceServices.ProcessPending(_machine.Cpu.State);
            _consoleDeviceServices.ProcessPending(_machine.Cpu.State);
            _clipboardDeviceServices.ProcessPending(_machine.Cpu.State);
            _graphicsBlitterScheduler.ProcessPending(_machine.Cpu.State);
			_dosServices.ProcessPending(_machine.Cpu.State);
        }

        private void DrawConsoleText(M68kCpuState state, uint rastPort, uint text, uint length)
        {
            var oldA0 = state.A[0]; var oldA1 = state.A[1]; var oldD0 = state.D[0];
            state.A[0] = text; state.A[1] = rastPort; state.D[0] = length;
            _graphicsServices.Text(state);
            state.A[0] = oldA0; state.A[1] = oldA1; state.D[0] = oldD0;
        }

        private long GetNextHostDeviceBoundary(long currentCycle, long targetCycle)
            => _dosServices.GetNextDeadline(currentCycle,
				_graphicsBlitterScheduler.GetNextBoundaryCycle(
                currentCycle,
                _gameportDeviceServices.GetNextDeadline(
                    currentCycle,
                    _keyboardDeviceServices.GetNextDeadline(
                        currentCycle,
                        _audioDeviceServices.GetNextDeadline(
                            currentCycle,
                            _timerDeviceServices.GetNextDeadline(
                                currentCycle,
								targetCycle))))));

        private bool IsValidKickstartRomExecBase(uint execBase)
        {
            if ((execBase & 3) != 0 ||
                !_machine.Bus.IsMappedMemoryRange(execBase, ExecMemListOffset + 14) ||
                _machine.Bus.ReadLong(execBase + ExecChkBaseOffset) != ~execBase)
            {
                return false;
            }

            var task = _machine.Bus.ReadLong(execBase + ExecThisTaskOffset);
            var list = execBase + ExecMemListOffset;
            var firstHeader = _machine.Bus.ReadLong(list);
            if (task == 0 || !_machine.Bus.IsMappedMemoryRange(task, TaskStackUpperOffset + 4) ||
                (firstHeader != 0 && !_machine.Bus.IsMappedMemoryRange(firstHeader, MemHeaderFreeOffset + 4)))
            {
                return false;
            }

            // AbsExecBase, ThisTask, and MemList become usable before MakeLibrary has
            // necessarily finished publishing Exec's negative vector table.  Taking
            // over in that interval leaves native-only calls such as AddHead jumping
            // into zero-filled RAM.  The first vector alone is not a sufficient
            // completion marker; AddHead is the first native-only vector used by the
            // remaining KS 3.1 startup path after the conservative host overlays.
            return IsInitializedKickstartRomExecVector(execBase, ExecFirstLvo) &&
                   IsInitializedKickstartRomExecVector(execBase, ExecAddHeadLvo);
        }

        private bool IsInitializedKickstartRomExecVector(uint execBase, int lvo)
        {
            var vectorAddress = unchecked(execBase + (uint)lvo);
            if (!_machine.Bus.IsCpuPhysicalAddressMapped(
                    vectorAddress, 6, AmigaBusAccessKind.CpuInstructionFetch))
            {
                return false;
            }

            // Keep this format-agnostic: ROM libraries may use a normal JMP entry,
            // an optimized relative entry, or a SetFunction replacement.  An all-zero
            // six-byte entry is the observable incomplete MakeLibrary state.
            return _machine.Bus.ReadWord(vectorAddress) != 0 ||
                   _machine.Bus.ReadLong(vectorAddress + 2) != 0;
        }

        private CopperStartExecServices CreateExecServices(uint execBase)
            => new CopperStartExecServices(
                _machine.Bus, execBase, _execGatewayServices.Invoke,
                _execIoServices.DoIo, _execIoServices.SendIo,
                _execIoServices.CheckIo, _execIoServices.WaitIo, _execIoServices.AbortIo,
                _execResidentServices.FindResident, HostOk,
                _execNameServices.FindName, _execMemoryServices.AllocMem, _execMemoryServices.AllocMemAndStore, _execMemoryServices.AllocAbs, _execMemoryServices.FreeMem,
                _execMemoryServices.AvailMem, _execLibraryGatewayServices.OldOpenLibrary,
                _execLibraryGatewayServices.OpenLibrary, _execLibraryGatewayServices.CloseLibrary,
                _execLibraryGatewayServices.AddLibrary, _execLibraryGatewayServices.RemLibrary,
                _execLibraryGatewayServices.AddDevice, _execLibraryGatewayServices.RemDevice, _execLibraryGatewayServices.OpenDevice, _execLibraryGatewayServices.CloseDevice,
                _execLibraryGatewayServices.AddResource, _execLibraryGatewayServices.RemResource, _execLibraryGatewayServices.OpenResource,
                state => state.D[0] = _execMakeLibraryServices.MakeFunctions(state), state => _execMakeLibraryServices.MakeLibrary(state), _execResidentServices.InitResident,
                _execTaskServices.Reschedule);

        private CopperStartExecLibraryServices GetRomExecLibraryServices()
            => _romExecLibraryServices ??= new CopperStartExecLibraryServices(
                _machine.Bus,
                GetActiveExecBase,
                StartGuestExecSubroutine,
                AddExecNodeAtomically,
                RemoveExecNodeAtomically);

        private CopperStartExecListServices GetExecListServices()
            => _execListServices ??= new CopperStartExecListServices(_guestMemory, ReadNullTerminatedString);

        private void InstallHostSupervisorStack()
        {
            var stackTop = GetBootStackTopAddress();
            _machine.Cpu.State.SetInterruptStackPointer(stackTop);
            _machine.Cpu.State.SetMasterStackPointer(stackTop);
        }

        private static void InstallSafeAutovectors(AmigaBus bus)
        {
            ReadOnlySpan<byte> clearIntreqAndReturn =
            [
                0x33, 0xFC, 0x7F, 0xFF, 0x00, 0xDF, 0xF0, 0x9C,
                0x4E, 0x73
            ];
            bus.MapReadOnlyMemory(SafeInterruptReturnAddress, clearIntreqAndReturn);
            for (var level = 1; level <= 7; level++)
            {
                bus.WriteLong((uint)((24 + level) * 4), SafeInterruptReturnAddress);
            }
        }

        private void EnsureSafeAutovectorsCurrent()
        {
            if (!_memoryListInstalled)
            {
                return;
            }

            for (var level = 1; level <= 7; level++)
            {
                var vectorAddress = (uint)((24 + level) * 4);
                var target = _machine.Bus.ReadLong(vectorAddress);
                if (AutovectorTargetNeedsRefresh(target))
                {
                    _machine.Bus.WriteLong(vectorAddress, SafeInterruptReturnAddress);
                }
            }
        }

        private bool AutovectorTargetNeedsRefresh(uint target)
        {
            if (target == SafeInterruptReturnAddress)
            {
                return false;
            }

            if (_kickstartRomBootActive)
            {
                return false;
            }

            if (target == 0 ||
                target > 0x00FF_FFFFu ||
                (target & 1) != 0 ||
                !_machine.Bus.IsCpuPhysicalAddressMapped(
                    target,
                    2,
                    AmigaBusAccessKind.CpuInstructionFetch))
            {
                return true;
            }

            return IsZeroFilledInstructionTarget(target);
        }

        private bool IsZeroFilledInstructionTarget(uint target)
        {
            if (!_machine.Bus.IsCpuPhysicalAddressMapped(
                    target,
                8,
                AmigaBusAccessKind.CpuInstructionFetch))
            {
                return false;
            }

            for (var offset = 0u; offset < 8; offset += 2)
            {
                if (_machine.Bus.ReadHostWord(target + offset) != 0)
                {
                    return false;
                }
            }

            return true;
        }

        private KickstartTrapTable CreateHostTrapTable()
        {
            return new KickstartTrapTable(
                0,
                HostNullCallback,
                HostOk,
                _execLibraryGatewayServices.OpenLibrary,
                _execLibraryGatewayServices.CloseLibrary,
                _execMemoryServices.AllocMem,
                _execMemoryServices.AllocMemAndStore,
                _execMemoryServices.FreeMem,
                HostOk,
                HostOk,
                HostOk,
                HostAbleIcr,
                HostSetIcr,
                _dosServices.Open,
                _dosServices.Close,
                _dosServices.Read,
                 _dosServices.Seek);
        }

        private static uint GetTaskTrapDispatcherAddress(int vector)
        {
            return vector switch
            {
                BusErrorVector => TaskTrapDispatcherBaseAddress + 16u * 6u,
                AddressErrorVector => TaskTrapDispatcherBaseAddress + 17u * 6u,
                IllegalInstructionVector => TaskTrapDispatcherBaseAddress + 18u * 6u,
                PrivilegeViolationVector => TaskTrapDispatcherBaseAddress + 19u * 6u,
                LineAVector => TaskTrapDispatcherBaseAddress + 20u * 6u,
                LineFVector => TaskTrapDispatcherBaseAddress + 21u * 6u,
                >= 32 and < 48 => TaskTrapDispatcherBaseAddress + (uint)((vector - 32) * 6),
                _ => throw new ArgumentOutOfRangeException(nameof(vector), vector, "Unsupported host task trap vector.")
            };
        }

        private AmigaBootResult ExecuteBootBlock(
            int maxInstructions,
            AmigaBootRunMode runMode,
            long? targetCycle = null,
            bool reportOverrun = true,
            Action<long, long>? beforeDeviceAdvance = null,
            IAmigaExecutionBoundarySchedule? boundarySchedule = null)
        {
            var instructions = 0;
            var boundary = _instructionBoundary;
            boundary.Reset(runMode, beforeDeviceAdvance, boundarySchedule);
            try
            {
                if (_machine.Cpu is IM68kBatchCore batchCore)
                {
                    instructions = batchCore.ExecuteInstructions(maxInstructions, targetCycle, boundary);
                }
                else
                {
                    while (!_machine.Cpu.State.Halted &&
                        instructions < maxInstructions &&
                        (!targetCycle.HasValue || _machine.Cpu.State.Cycles < targetCycle.Value) &&
                        boundary.BeforeInstruction())
                    {
                        var previousCycle = _machine.Cpu.State.Cycles;
                        _machine.Cpu.ExecuteInstruction();
                        boundary.AfterInstruction(previousCycle, _machine.Cpu.State.Cycles);
                        instructions++;
                    }
                }

                if (reportOverrun && instructions >= maxInstructions)
                {
                    var pc = _machine.Cpu.State.ProgramCounter;
                    _diagnostics.Add(new AmigaBootDiagnostic(
                        "AMIGA_BOOT_OVERRUN",
                        $"Boot block execution exceeded the instruction budget at PC=0x{pc:X6}, opcode=0x{_machine.Bus.ReadWord(pc):X4}, D0=0x{_machine.Cpu.State.D[0]:X8}, D1=0x{_machine.Cpu.State.D[1]:X8}, A0=0x{_machine.Cpu.State.A[0]:X8}, A1=0x{_machine.Cpu.State.A[1]:X8}, cycles={_machine.Cpu.State.Cycles}."));
                }
            }
            catch (UnsupportedM68kOpcodeException ex)
            {
                _diagnostics.Add(new AmigaBootDiagnostic("AMIGA_BOOT_UNSUPPORTED_OPCODE", DescribeCpuFault(ex.Message)));
                _machine.Cpu.State.Halted = true;
            }
            catch (AmigaEmulationException ex)
            {
                _diagnostics.Add(new AmigaBootDiagnostic("AMIGA_BOOT_FAULT", DescribeCpuFault(ex.Message)));
                _machine.Cpu.State.Halted = true;
            }

            return new AmigaBootResult(
                BootBlockAddress,
                BootEntryAddress,
                _machine.Cpu.State.ProgramCounter,
                instructions,
                boundary.Completed,
                _diagnostics);
        }

        private AmigaBootResult ExecuteRuntime(
            int maxInstructions,
            long targetCycle,
            Action<long, long>? beforeDeviceAdvance,
            IAmigaExecutionBoundarySchedule? boundarySchedule)
        {
            var instructions = 0;
            var boundary = _runtimeInstructionBoundary;
            boundary.Reset(beforeDeviceAdvance, boundarySchedule);
            try
            {
                if (_machine.Cpu is IM68kBatchCore batchCore)
                {
                    instructions = batchCore.ExecuteInstructions(maxInstructions, targetCycle, boundary);
                }
                else
                {
                    while (!_machine.Cpu.State.Halted &&
                        instructions < maxInstructions &&
                        _machine.Cpu.State.Cycles < targetCycle &&
                        boundary.BeforeInstruction())
                    {
                        var previousCycle = _machine.Cpu.State.Cycles;
                        _machine.Cpu.ExecuteInstruction();
                        boundary.AfterInstruction(previousCycle, _machine.Cpu.State.Cycles);
                        instructions++;
                    }
                }
            }
            catch (UnsupportedM68kOpcodeException ex)
            {
                _diagnostics.Add(new AmigaBootDiagnostic("AMIGA_BOOT_UNSUPPORTED_OPCODE", DescribeCpuFault(ex.Message)));
                _machine.Cpu.State.Halted = true;
            }
            catch (AmigaEmulationException ex)
            {
                _diagnostics.Add(new AmigaBootDiagnostic("AMIGA_BOOT_FAULT", DescribeCpuFault(ex.Message)));
                _machine.Cpu.State.Halted = true;
            }

            return new AmigaBootResult(
                BootBlockAddress,
                BootEntryAddress,
                _machine.Cpu.State.ProgramCounter,
                instructions,
                completedBootBlock: false,
                _diagnostics);
        }

        private void RecordNativeKickstartNullPc()
        {
            if (_diagnostics.Any(diagnostic => diagnostic.Code == "AMIGA_BOOT_NULL_PC"))
            {
                return;
            }

            _diagnostics.Add(new AmigaBootDiagnostic(
                "AMIGA_BOOT_NULL_PC",
                "Native Kickstart ROM execution reached PC zero; host DOS continuation is disabled for ROM-backed profiles. " +
                DescribeMostRecentRteFrame() + " " +
                DescribeLastCpuException() + " " +
                DescribeLastFpuStateFrame() + " " +
                DescribeNativeStackState() + " " +
                DescribeCpuFault("")));
            _machine.Cpu.State.Halted = true;
        }

        private string DescribeMostRecentRteFrame()
        {
            var state = _machine.Cpu.State;
            var frame = state.A[7] >= 8 ? state.A[7] - 8 : 0u;
            if (frame == 0 || !_machine.Bus.IsMappedMemoryRange(frame, 8))
            {
                return $"RTE frame unavailable at 0x{frame:X8}.";
            }

            var status = _machine.Bus.ReadWord(frame);
            var pc = _machine.Bus.ReadLong(frame + 2);
            var format = _machine.Bus.ReadWord(frame + 6);
            return $"RTE frame at 0x{frame:X8}: SR=0x{status:X4}, PC=0x{pc:X8}, format=0x{format:X4}.";
        }

        private string DescribeLastCpuException()
        {
            var state = _machine.Cpu.State;
            if (state.LastExceptionVector < 0)
            {
                return "Last exception: none.";
            }

            return
                $"Last exception: vector={state.LastExceptionVector}, stackedPC=0x{state.LastExceptionStackedProgramCounter:X8}, " +
                $"savedSR=0x{state.LastExceptionStatusRegister:X4}, opcode=0x{state.LastExceptionOpcode:X4} " +
                $"at PC=0x{state.LastExceptionInstructionProgramCounter:X8}.";
        }

        private string DescribeLastFpuStateFrame()
        {
            var fpu = _machine.Cpu.State.M68040Fpu;
            if (fpu.LastStateFrameSize == 0)
            {
                return "Last FPU frame: none.";
            }

            return
                $"Last FPU frame: {(fpu.LastStateFrameRestore ? "FRESTORE" : "FSAVE")} " +
                $"address=0x{fpu.LastStateFrameAddress:X8}, header=0x{fpu.LastStateFrameHeader:X4}, " +
                $"size=0x{fpu.LastStateFrameSize:X}, data={DescribeMemoryWords(fpu.LastStateFrameAddress, 32)}.";
        }

        private string DescribeNativeStackState()
        {
            var state = _machine.Cpu.State;
            return
                $"Stacks: A7=0x{state.A[7]:X8}, SSP=0x{state.SupervisorStackPointer:X8}, " +
                $"USP=0x{state.UserStackPointer:X8}, MSP=0x{state.MasterStackPointer:X8}, VBR=0x{state.VectorBaseRegister:X8}; " +
                $"A7-16={DescribeMemoryWords(state.A[7] - 16, 16)}, SSP-16={DescribeMemoryWords(state.SupervisorStackPointer - 16, 16)}, " +
                $"A4={DescribeMemoryWords(state.A[4], 16)}.";
        }

        private string DescribeMemoryWords(uint address, int byteCount)
        {
            if (!_machine.Bus.IsMappedMemoryRange(address, byteCount))
            {
                return $"unmapped@0x{address:X8}";
            }

            var builder = new StringBuilder();
            builder.Append("0x");
            builder.Append(address.ToString("X8"));
            builder.Append('[');
            for (var offset = 0; offset < byteCount; offset += 2)
            {
                if (offset != 0)
                {
                    builder.Append(' ');
                }

                builder.Append(_machine.Bus.ReadWord(address + (uint)offset).ToString("X4"));
            }

            builder.Append(']');
            return builder.ToString();
        }

        private void SkipDosBootBlockHeaderIfNeeded()
        {
            if (!_dosBootBlockHeaderProbeEnabled)
            {
                return;
            }

            var pc = _machine.Cpu.State.ProgramCounter;
            if (TrySkipDosBootBlockHeader(pc, pc))
            {
                _dosBootBlockHeaderProbeEnabled = false;
                return;
            }

            if (pc >= 4)
            {
                if (TrySkipDosBootBlockHeader(pc - 4, pc))
                {
                    _dosBootBlockHeaderProbeEnabled = false;
                    return;
                }
            }

            _dosBootBlockHeaderProbeEnabled = false;
        }

        private bool TrySkipDosBootBlockHeader(uint headerAddress, uint currentProgramCounter)
        {
            if (!_machine.Bus.IsMappedMemoryRange(headerAddress, 1024) ||
                _machine.Bus.ReadLong(headerAddress) != 0x444F_5300)
            {
                return false;
            }

            var rootBlock = _machine.Bus.ReadLong(headerAddress + 8);
            if (rootBlock is >= 880 and <= 1760)
            {
                _machine.Cpu.State.ProgramCounter = headerAddress + 12;
                return true;
            }

            var sum = 0u;
            for (var offset = 0u; offset < 1024; offset += 4)
            {
                var value = _machine.Bus.ReadLong(headerAddress + offset);
                var previous = sum;
                sum += value;
                if (sum < previous)
                {
                    sum++;
                }
            }

            if (sum != 0xFFFF_FFFF)
            {
                return false;
            }

            _ = currentProgramCounter;
            _machine.Cpu.State.ProgramCounter = headerAddress + 12;
            return true;
        }

        private void HostDoIo(M68kCpuState state)
        {
            var io = state.A[1];
            var command = _machine.Bus.ReadWord(io + IoCommandOffset);
            var length = _machine.Bus.ReadLong(io + IoLengthOffset);
            var destination = _machine.Bus.ReadLong(io + IoDataOffset);
            var offset = _machine.Bus.ReadLong(io + IoOffsetOffset);
            if (command == TdMotor)
            {
                var previousMotorOn = Drive0.MotorOn ? 1u : 0u;
                if (length != 0)
                {
                    _machine.Bus.WriteByte(0x00BFD100, 0x77, state.Cycles);
                    _machine.Bus.WriteByte(0x00BFD300, 0xFF, state.Cycles);
                }
                else
                {
                    _machine.Bus.WriteByte(0x00BFD100, 0xFF, state.Cycles);
                    _machine.Bus.WriteByte(0x00BFD300, 0xFF, state.Cycles);
                }

                _machine.Bus.WriteByte(io + IoErrorOffset, 0, state.Cycles);
                _machine.Bus.WriteLong(io + IoActualOffset, previousMotorOn, state.Cycles);
                state.D[0] = 0;
                return;
            }

            if (command != CmdRead)
            {
                _diagnostics.Add(new AmigaBootDiagnostic(
                    "AMIGA_BOOT_UNSUPPORTED_IO",
                    $"Unsupported boot IO command {command} at IO request 0x{io:X8}, length 0x{length:X8}, data 0x{destination:X8}, offset 0x{offset:X8}."));
                _machine.Bus.WriteByte(io + IoErrorOffset, 1, state.Cycles);
                _machine.Bus.WriteLong(io + IoActualOffset, 0, state.Cycles);
                state.D[0] = 1;
                return;
            }

            ReadBootDiskBytesToChipRam(checked((int)offset), checked((int)length), destination, state.Cycles);
            CompleteTrackdiskReadDriveState(state.Cycles);
            _machine.Bus.WriteByte(io + IoErrorOffset, 0, state.Cycles);
            _machine.Bus.WriteLong(io + IoActualOffset, length, state.Cycles);
            _bootDiskReadCompleted = true;
            state.D[0] = 0;
        }

        private void CompleteTrackdiskReadDriveState(long cycle)
        {
            SetTrackdiskMotor(0, true, cycle);
        }

		private void HostAbortIo(M68kCpuState state)
		{
			var request = state.A[1];
			if (request == 0 || !_machine.Bus.IsMappedMemoryRange(
				request + (uint)global::Amiga.ExecLayout.IORequest.Device, 4)) return;
			var device = _machine.Bus.ReadLong(request + (uint)global::Amiga.ExecLayout.IORequest.Device);
			if (device == 0) return;
			var vector = unchecked(device - 36u);
			state.A[6] = device;
			state.ProgramCounter = vector + 6;
			_ = _machine.Bus.TryInvokeHostGatewayAt(vector, state);
		}

        private byte[]? GetTrackdiskData(int unit)
            => unit switch
            {
                0 => Drive0.Disk?.Data,
                1 => Drive1.Disk?.Data,
                2 => Drive2.Disk?.Data,
                3 => Drive3.Disk?.Data,
                _ => null
            };

        private bool TryWriteTrackdiskData(int unit, int byteOffset, ReadOnlySpan<byte> source)
        {
            var disk = unit switch
            {
                0 => Drive0.Disk,
                1 => Drive1.Disk,
                2 => Drive2.Disk,
                3 => Drive3.Disk,
                _ => null
            };
            return disk?.TryWriteBytes(byteOffset, source) == true;
        }

        private CopperStartTrackdiskRawTrack? GetTrackdiskRawTrack(int unit)
        {
            var drive = unit switch
            {
                0 => Drive0,
                1 => Drive1,
                2 => Drive2,
                3 => Drive3,
                _ => null
            };
            if (drive?.Disk is null)
            {
                return null;
            }

            var track = drive.ReadEncodedTrack(drive.Cylinder, drive.Head);
            return new CopperStartTrackdiskRawTrack(track.EncodedData, track.BitLength);
        }

        private bool TryWriteTrackdiskRawTrack(int unit, CopperStartTrackdiskRawTrack track)
        {
            var drive = unit switch
            {
                0 => Drive0,
                1 => Drive1,
                2 => Drive2,
                3 => Drive3,
                _ => null
            };
            return drive is { Disk: not null, WriteProtected: false } &&
                drive.TryWriteEncodedTrack(
                    drive.Cylinder,
                    drive.Head,
                    new CopperStartEncodedTrack(track.Data, track.BitLength));
        }

        private ulong GetTrackdiskChangeVersion(int unit)
            => unit switch
            {
                0 => Drive0.ChangeVersion,
                1 => Drive1.ChangeVersion,
                2 => Drive2.ChangeVersion,
                3 => Drive3.ChangeVersion,
                _ => 0
            };

        private void EjectTrackdiskDrive(int unit)
        {
            switch (unit)
            {
                case 0: Drive0.Eject(); break;
                case 1: Drive1.Eject(); break;
                case 2: Drive2.Eject(); break;
                case 3: Drive3.Eject(); break;
            }
        }

        private bool IsTrackdiskMotorOn(int unit)
            => unit switch
            {
                0 => Drive0.MotorOn,
                1 => Drive1.MotorOn,
                2 => Drive2.MotorOn,
                3 => Drive3.MotorOn,
                _ => false
            };

        private bool IsTrackdiskWriteProtected(int unit)
            => unit switch
            {
                0 => Drive0.WriteProtected,
                1 => Drive1.WriteProtected,
                2 => Drive2.WriteProtected,
                3 => Drive3.WriteProtected,
                _ => false
            };

        private void SetTrackdiskMotor(int unit, bool enabled, long cycle)
        {
            var selectMask = unit is >= 0 and <= 3 ? 1 << (unit + 3) : 0;
            var value = enabled ? (byte)(0x7F & ~selectMask) : (byte)0xFF;
            _machine.Bus.WriteByte(0x00BFD100, value, cycle);
            _machine.Bus.WriteByte(0x00BFD300, 0xFF, cycle);
        }

        private void ReplyTrackdiskMessage(uint request)
        {
            _execPortServices.ReplyMessage(request);
        }

		private bool PutDosGuestMessage(uint port, uint message,
			M68kCpuState state) =>
			_execPortServices.TryPutMessage(port, message, state);

		private uint TakeDosGuestMessage(uint port) =>
			_execPortServices.TakeMessage(port);

		private bool ReplyQueuedDosGuestMessage(uint port, uint message) =>
			_execPortServices.TryReplyQueuedMessage(port, message);

		private CopperMod.Amiga.CopperStart.Exec.ExecMemoryContext?
			GetDosExecMemoryContext() => _execMemoryServices?.Context;

		private bool RegisterDosTaskContext(uint task) =>
			_execTaskServices.RegisterCreatedTask(task);

		private void RemoveDosTaskContext(uint task) =>
			_execTaskServices.RemoveCreatedTask(task);

		private void ReplyDosGuestMessage(uint message) =>
			_execPortServices.ReplyMessage(message);

        private void ReadBootDiskBytesToChipRam(int diskByteOffset, int byteCount, uint destination, long cycle)
        {
            if (Drive0.Disk == null)
            {
                throw new AmigaEmulationException("No disk is inserted in DF0:.");
            }

            if (diskByteOffset >= 0 && byteCount >= 0 && diskByteOffset + byteCount <= Drive0.Disk.Data.Length)
            {
                _diskDma.ReadBytesToChipRam(Drive0, _machine.Bus, diskByteOffset, byteCount, destination, cycle);
                return;
            }

            if (diskByteOffset < 0 || byteCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(diskByteOffset), "Boot disk read range is invalid.");
            }

            _diagnostics.Add(new AmigaBootDiagnostic(
                "AMIGA_BOOT_WRAPPED_DISK_READ",
                $"Wrapped boot disk read from offset 0x{diskByteOffset:X} for 0x{byteCount:X} bytes."));
            var disk = Drive0.Disk.Data;
            var buffer = new byte[byteCount];
            for (var i = 0; i < buffer.Length; i++)
            {
                buffer[i] = disk[(diskByteOffset + i) % disk.Length];
            }

            _machine.Bus.CopyToChipRam(destination, buffer);
        }

        private string DescribeCpuFault(string message)
        {
            var state = _machine.Cpu.State;
            return message +
                $" Last opcode 0x{state.LastOpcode:X4} at PC 0x{state.LastInstructionProgramCounter:X8}, " +
                $"current PC 0x{state.ProgramCounter:X8}, SR 0x{state.StatusRegister:X4}, " +
                $"D0 0x{state.D[0]:X8}, D1 0x{state.D[1]:X8}, A0 0x{state.A[0]:X8}, A1 0x{state.A[1]:X8}, " +
                $"A3 0x{state.A[3]:X8}, A4 0x{state.A[4]:X8}, A7 0x{state.A[7]:X8}.";
        }

        private void RecordAllocDiagnostic(int size, uint flags, uint address)
        {
            if (_hostAllocationDiagnosticCount < 16)
            {
                _diagnostics.Add(new AmigaBootDiagnostic(
                    "AMIGA_BOOT_ALLOC_MEM",
                    $"AllocMem requested 0x{size:X} bytes with flags 0x{flags:X8} and returned 0x{address:X8}."));
                _hostAllocationDiagnosticCount++;
            }
        }

        private void RecordAllocAbsDiagnostic(int size, uint location, uint address)
        {
            if (_hostAllocationDiagnosticCount < 16)
            {
                _diagnostics.Add(new AmigaBootDiagnostic(
                    "AMIGA_BOOT_ALLOC_ABS",
                    $"AllocAbs requested 0x{size:X} bytes at 0x{location:X8} and returned 0x{address:X8}."));
                _hostAllocationDiagnosticCount++;
            }
        }

        private void RecordFreeDiagnostic(uint address, int size)
        {
            if (_hostFreeDiagnosticCount < 16)
            {
                _diagnostics.Add(new AmigaBootDiagnostic(
                    "AMIGA_BOOT_FREE_MEM",
                    $"FreeMem released 0x{size:X} bytes at 0x{address:X8}."));
                _hostFreeDiagnosticCount++;
            }
        }

        private void OpenRomLibrary(M68kCpuState state)
        {
            _nativeGraphicsOpenPending = state.A[1] != 0 &&
                _machine.Bus.IsMappedMemoryRange(state.A[1], "graphics.library".Length + 1) &&
                MatchesNullTerminatedString(
                null,
                state.A[1],
                96,
                "graphics.library");
            GetRomExecLibraryServices().OpenLibrary(state, ExecLibraryCallContinuationAddress);
            // OpenLibrary reports a missing library synchronously and does not
            // enter the continuation.  Do not let that stale request arm a
            // later CloseLibrary/OpenDevice continuation.
            if (state.D[0] == 0)
                _nativeGraphicsOpenPending = false;
        }

        private void OpenCompatibilityLibrary(M68kCpuState state)
        {
            var shouldRecordDiagnostic = _openLibraryDiagnosticCount < 24;
            var name = shouldRecordDiagnostic ? ReadNullTerminatedString(state.A[1], 96) : null;
            if (_openLibraryDiagnosticCount < 24)
            {
                _diagnostics.Add(new AmigaBootDiagnostic("AMIGA_BOOT_OPEN_LIBRARY", $"OpenLibrary requested '{name}'."));
                _openLibraryDiagnosticCount++;
            }

            if (GetRomExecLibraryServices().FindLibrary(state.A[1], state.D[0]) != 0)
            {
                GetRomExecLibraryServices().OpenLibrary(state, ExecLibraryCallContinuationAddress);
            }
            else if (TryGetHostLibraryBase(name, state.A[1], out var libraryBase))
            {
                state.D[0] = libraryBase;
            }
            else
            {
                state.D[0] = AmigaKickstartHost.DummyLibraryBase;
            }
        }

        private void AddCompatibilityLibrary(M68kCpuState state)
            => GetRomExecLibraryServices().AddLibrary(state);

        private void RemoveCompatibilityLibrary(M68kCpuState state)
            => GetRomExecLibraryServices().RemLibrary(state);

        private bool TryGetHostLibraryBase(string? cachedName, uint nameAddress, out uint libraryBase)
        {
            if (MatchesNullTerminatedString(cachedName, nameAddress, 96, "cybergraphics.library"))
            {
                libraryBase = _cyberGraphicsFirmware?.LibraryBase ?? 0;
                return libraryBase != 0 && _machine.Bus.RtgVram.Active;
            }

            if (MatchesNullTerminatedString(cachedName, nameAddress, 96, "graphics.library"))
            {
                libraryBase = GetGraphicsLibraryBase();
                return true;
            }

            if (MatchesNullTerminatedString(cachedName, nameAddress, 96, "layers.library"))
            {
                libraryBase = _layersHostServices.LibraryBase;
                return _layersHostServices.IsInstalled && libraryBase != 0;
            }

            if (MatchesNullTerminatedString(cachedName, nameAddress, 96, "intuition.library"))
            {
                libraryBase = AmigaKickstartHost.IntuitionLibraryBase;
                return true;
            }

            if (MatchesNullTerminatedString(cachedName, nameAddress, 96, "expansion.library"))
            {
                libraryBase = AmigaKickstartHost.ExpansionLibraryBase;
                return true;
            }

            if (MatchesNullTerminatedString(cachedName, nameAddress, 96, "dos.library"))
            {
                libraryBase = AmigaKickstartHost.DosLibraryBase;
                return true;
            }

            if (MatchesNullTerminatedString(cachedName, nameAddress, 96, "ciaa.resource"))
            {
                libraryBase = AmigaKickstartHost.CiaAResourceBase;
                return true;
            }

            if (MatchesNullTerminatedString(cachedName, nameAddress, 96, "ciab.resource"))
            {
                libraryBase = AmigaKickstartHost.CiaBResourceBase;
                return true;
            }

            if (MatchesNullTerminatedString(cachedName, nameAddress, 96, "misc.resource"))
            {
                libraryBase = AmigaKickstartHost.MiscResourceBase;
                return true;
            }

            if (MatchesNullTerminatedString(cachedName, nameAddress, 96, "icon.library"))
            {
                libraryBase = AmigaKickstartHost.IconLibraryBase;
                return true;
            }

            if (MatchesNullTerminatedString(cachedName, nameAddress, 96, "workbench.library"))
            {
                libraryBase = AmigaKickstartHost.WorkbenchLibraryBase;
                return true;
            }

            libraryBase = 0;
            return false;
        }

        private uint AddInterruptServer(M68kCpuState state)
        {
            var interruptNumber = unchecked((int)state.D[0]);
            var interrupt = state.A[1];
            if (interruptNumber is < 0 or > 31 || interrupt == 0 ||
                !_machine.Bus.IsMappedMemoryRange(interrupt, InterruptCodeOffset + 4))
            {
                return 0;
            }

            var data = _machine.Bus.ReadLong(interrupt + InterruptDataOffset);
            if (data == 0 || !_machine.Bus.IsMappedMemoryRange(data, 4))
            {
                return 0;
            }

			var platform = new HostGuestMemoryExecPlatform(_guestMemory);
			PortableExecMemory.ExecInterruptCore.AddServer(ref platform,
				global::Amiga.APTR.FromPointer(GetActiveExecBase()), interruptNumber,
				global::Amiga.APTR.FromPointer(interrupt));

			if (_kickstartRomExecTakeoverState == KickstartRomExecTakeoverState.Active) return 0;

            _syntheticVBlankInterruptServers.Add(new SyntheticInterruptServer(interrupt, data));
            return 0;
        }

        private uint RemoveInterruptServer(M68kCpuState state)
        {
            var interruptNumber = unchecked((int)state.D[0]);
            var interrupt = state.A[1];
            if (interruptNumber is < 0 or > 31 || interrupt == 0)
            {
                return 0;
            }

			var platform = new HostGuestMemoryExecPlatform(_guestMemory);
			PortableExecMemory.ExecInterruptCore.RemoveServer(ref platform,
				global::Amiga.APTR.FromPointer(GetActiveExecBase()), interruptNumber,
				global::Amiga.APTR.FromPointer(interrupt));

			if (_kickstartRomExecTakeoverState == KickstartRomExecTakeoverState.Active) return 0;

            for (var i = _syntheticVBlankInterruptServers.Count - 1; i >= 0; i--)
            {
                if (_syntheticVBlankInterruptServers[i].InterruptAddress == interrupt)
                {
                    _syntheticVBlankInterruptServers.RemoveAt(i);
                }
            }

            return 0;
        }

        private void AdvanceSyntheticVBlankInterruptServers(long previousCycle, long currentCycle)
        {
            if (currentCycle <= previousCycle)
            {
                return;
            }

            ApplyPendingCopperListAtFrameBoundary(currentCycle);
            AdvancePendingGraphicsDoubleBufferMessages(currentCycle);

            if (_syntheticVBlankInterruptServers.Count == 0)
            {
                return;
            }

            var previousFrame = _machine.Bus.GetBeamPosition(Math.Max(0, previousCycle)).FrameNumber;
            var currentFrame = _machine.Bus.GetBeamPosition(Math.Max(0, currentCycle)).FrameNumber;
            var ticks = (long)currentFrame - previousFrame;
            if (ticks <= 0)
            {
                return;
            }

            var increment = ticks > uint.MaxValue ? uint.MaxValue : (uint)ticks;
            for (var i = _syntheticVBlankInterruptServers.Count - 1; i >= 0; i--)
            {
                var server = _syntheticVBlankInterruptServers[i];
                if (!_machine.Bus.IsMappedMemoryRange(server.DataAddress, 4))
                {
                    _syntheticVBlankInterruptServers.RemoveAt(i);
                    continue;
                }

                var value = _machine.Bus.ReadLong(server.DataAddress);
                _machine.Bus.WriteLong(server.DataAddress, value + increment);
            }
        }

		private uint SetInterruptVector(M68kCpuState state)
		{
			var platform = new HostGuestMemoryExecPlatform(_guestMemory);
			return PortableExecMemory.ExecInterruptCore.SetVector(ref platform,
				global::Amiga.APTR.FromPointer(GetActiveExecBase()), unchecked((int)state.D[0]),
				global::Amiga.APTR.FromPointer(state.A[1])).Raw;
		}

		private uint CauseSoftInterrupt(M68kCpuState state)
		{
			var platform = new HostGuestMemoryExecPlatform(_guestMemory);
			if (PortableExecMemory.ExecInterruptCore.Cause(ref platform,
				global::Amiga.APTR.FromPointer(GetActiveExecBase()),
				global::Amiga.APTR.FromPointer(state.A[1])))
				_execContext.RequestDispatch();
			return 0;
		}

        private bool TryDispatchCopperStartTaskScheduler()
        {
            // Exec may make a higher-priority task ready from an interrupt
            // server, but the interrupted CPU context remains current until
            // interrupt exit.  Entering Schedule while a bridged server is on
            // the stack mixes two task frames and eventually corrupts tc_SPReg.
            if (_activeExecInterruptSource != -1)
                return false;

            if (_kickstartRomExecTakeoverState == KickstartRomExecTakeoverState.Active)
            {
                // The reaper independently requires execution proof. Native
                // current-task Switch completion is not certified here; those
                // allocations remain pinned pending a native completion gate.
                _execTaskServices.ReapDeferredTasks();

                if (!_taskScheduler.DispatchPending ||
                    _machine.Bus.ReadByte(GetActiveExecBase() +
                        (uint)global::Amiga.ExecLayout.ExecBase.IDNestCount) != ExecNestingEnabled ||
                    _machine.Bus.ReadByte(GetActiveExecBase() +
                        (uint)global::Amiga.ExecLayout.ExecBase.TaskDisableNestCount) != ExecNestingEnabled)
                {
                    return false;
                }

                // A host gateway only latches dispatch. Enter the original
                // Schedule vector at this outer CPU boundary; it owns tc_SPReg,
                // ready/wait lists and tasks that pre-date takeover.
                if (!EnterNativeExecScheduler(
                        _machine.Cpu.State,
                        _machine.Cpu.State.ProgramCounter))
                {
                    return false;
                }

                _taskScheduler.AcknowledgeDispatch();
                return true;
            }

            if (_kickstartRomExecTakeoverState == KickstartRomExecTakeoverState.Disabled &&
                _memoryListInstalled)
                _execTaskServices.ReapDeferredTasks();

            // External-ROM discovery never consumes managed task frames. The
            // synthetic path exists only after CopperStart has published its
            // Exec lists and is selected exclusively at this outer boundary.
            if (_kickstartRomExecTakeoverState != KickstartRomExecTakeoverState.Disabled ||
                !_memoryListInstalled || !_taskScheduler.DispatchPending)
            {
                return false;
            }

            var execBase = GetActiveExecBase();
            if (_machine.Bus.ReadByte(execBase +
                    (uint)global::Amiga.ExecLayout.ExecBase.IDNestCount) != ExecNestingEnabled ||
                _machine.Bus.ReadByte(execBase +
                    (uint)global::Amiga.ExecLayout.ExecBase.TaskDisableNestCount) != ExecNestingEnabled)
            {
                return false;
            }

            var current = GetCurrentTaskAddress();
            var readyList = execBase +
                (uint)global::Amiga.ExecLayout.ExecBase.TaskReady;
            var ready = _machine.Bus.ReadLong(
                readyList + (uint)global::Amiga.ExecLayout.List.Head);
            var tail = readyList + (uint)global::Amiga.ExecLayout.List.Tail;
            var currentState = current != 0 && _machine.Bus.IsMappedMemoryRange(
                    current + (uint)global::Amiga.ExecLayout.Task.State,
                    1)
                ? (global::Amiga.TaskState)_machine.Bus.ReadByte(
                    current + (uint)global::Amiga.ExecLayout.Task.State)
                : global::Amiga.TaskState.Invalid;

            if (ready == 0 || ready == tail)
            {
                if (currentState == global::Amiga.TaskState.Running)
                    _taskScheduler.AcknowledgeDispatch();
                return false;
            }

            // Every ready synthetic task must have been registered by AddTask
            // or captured while it was running. Preflight before the portable
            // list mutation so a foreign list node cannot strand ThisTask.
            if (ready != current && !_taskScheduler.HasSyntheticContext(ready))
            {
                AddExecLikeDiagnostic(
                    "AMIGA_BOOT_EXEC_SYNTHETIC_CONTEXT",
                    $"Synthetic ready task 0x{ready:X8} has no complete CPU frame.");
                return false;
            }

            if (currentState != global::Amiga.TaskState.Removed)
                _taskScheduler.CaptureCurrent(current, _machine.Cpu.State);
            var platform = new HostGuestMemoryExecPlatform(_guestMemory);
            var next = PortableExecMemory.ExecSchedulerCore.Dispatch(
                ref platform,
                global::Amiga.APTR.FromPointer(execBase)).Raw;
            if (next == 0)
                return false;
            if (next == current)
            {
                _taskScheduler.AcknowledgeDispatch();
                return false;
            }
            if (!_taskScheduler.TryGetSyntheticContext(next, out var nextContext))
            {
                AddExecLikeDiagnostic(
                    "AMIGA_BOOT_EXEC_SYNTHETIC_CONTEXT",
                    $"Selected synthetic task 0x{next:X8} has no complete CPU frame.");
                return false;
            }

            _taskScheduler.InstallSyntheticContext(next);
            // STOP is a machine run state rather than part of the saved task
            // image. A runnable replacement task must not inherit it.
            _machine.Cpu.State.Stopped = false;
            _taskScheduler.AcknowledgeDispatch();
            _execTaskServices.ReapDeferredTasks();
            return true;
        }

        private bool CanExecuteCurrentCopperStartTask()
        {
            if (_kickstartRomExecTakeoverState != KickstartRomExecTakeoverState.Disabled ||
                !_memoryListInstalled)
            {
                return true;
            }

            var task = GetCurrentTaskAddress();
            return task != 0 && _machine.Bus.IsMappedMemoryRange(
                    task + (uint)global::Amiga.ExecLayout.Task.State,
                    1) &&
                (global::Amiga.TaskState)_machine.Bus.ReadByte(
                    task + (uint)global::Amiga.ExecLayout.Task.State) ==
                    global::Amiga.TaskState.Running;
        }

        private bool UsesGuestExecTasks =>
            _kickstartRomExecTakeoverState == KickstartRomExecTakeoverState.Active ||
            NativeDosLibraryBase != 0;

        private bool SuspendCurrentTaskThroughNativeExecScheduler(M68kCpuState state)
        {
            if (_kickstartRomExecTakeoverState != KickstartRomExecTakeoverState.Active)
            {
                // Native DOS uses the real task lists even in an application
                // session. The service has already committed Wait or RemTask;
                // only the outer instruction boundary may exchange CPU state.
                return NativeDosLibraryBase != 0 &&
                    TryDeferSyntheticTask(state, ExecWaitResumeGatewayAddress, moveToWait: false);
            }

            // The blocked gateway has not performed its implicit RTS yet.  A
            // return through this small gateway rechecks Wait/WaitPort/WaitIO
            // before returning to the original 68k caller.  Switch is the
            // native path for a task that has already committed itself to the
            // wait list; Schedule is the later priority/quantum decision path.
			return EnterNativeExecVector(state, ExecWaitResumeGatewayAddress, global::Amiga.ExecLvo.Switch);
        }

        private bool SuspendLayersTask(M68kCpuState state, uint continuationAddress)
        {
            if (_kickstartRomExecTakeoverState != KickstartRomExecTakeoverState.Active)
            {
                return TryDeferSyntheticTask(state, continuationAddress, moveToWait: true);
            }

            var task = GetCurrentTaskAddress();
            if (task == 0)
                return false;
            MoveTaskToList(task, GetActiveExecBase() + ExecTaskWaitOffset, state);
            if (EnterNativeExecVector(state, continuationAddress, global::Amiga.ExecLvo.Switch))
                return true;
            MoveTaskToList(task, GetActiveExecBase() + ExecTaskReadyOffset, state);
            return false;
        }

        private bool TryDeferSyntheticTask(M68kCpuState state, uint continuationAddress, bool moveToWait)
        {
            // Capture/dispatch/install belongs to TryDispatchCopperStartTask at
            // the instruction boundary, after the blocking gateway retires.
            if (_kickstartRomExecTakeoverState != KickstartRomExecTakeoverState.Disabled ||
                !_memoryListInstalled || !ReferenceEquals(state, _machine.Cpu.State))
                return false;

            var execBase = GetActiveExecBase();
            if (execBase == 0 ||
                !_machine.Bus.IsMappedMemoryRange(execBase +
                    (uint)global::Amiga.ExecLayout.ExecBase.IDNestCount, 2) ||
                _machine.Bus.ReadByte(execBase +
                    (uint)global::Amiga.ExecLayout.ExecBase.IDNestCount) != ExecNestingEnabled ||
                _machine.Bus.ReadByte(execBase +
                    (uint)global::Amiga.ExecLayout.ExecBase.TaskDisableNestCount) != ExecNestingEnabled)
                return false;

            var task = GetCurrentTaskAddress();
            if (task == 0 || !_machine.Bus.IsMappedMemoryRange(task +
                    (uint)global::Amiga.ExecLayout.Task.State, 1))
                return false;

            // Wait/RemTask callers already own their list transition. In
            // particular, an exiting task must never be reinserted in TaskWait.
            if (moveToWait)
                MoveTaskToList(task, execBase + ExecTaskWaitOffset, state);
            state.ProgramCounter = continuationAddress;
            _taskScheduler.RequestDispatch();
            return true;
        }

        private void WakeLayersTask(uint task)
        {
            if (task == 0 || GetActiveExecBase() == 0)
                return;
            var platform = new HostGuestMemoryExecPlatform(_guestMemory);
            PortableExecMemory.ExecTaskCore.MoveToReady(
                ref platform,
                global::Amiga.APTR.FromPointer(GetActiveExecBase()),
                global::Amiga.APTR.FromPointer(task));
            _taskScheduler.RequestDispatch();
        }

		private void RemovePortableTask(uint task)
		{
			_taskScheduler.Remove(task);
		}

        private bool EnterNativeExecScheduler(M68kCpuState state, uint returnAddress)
			=> EnterNativeExecVector(state, returnAddress, global::Amiga.ExecLvo.Schedule);

        private bool EnterNativeExecVector(M68kCpuState state, uint returnAddress, int vectorOffset)
        {
            if (_kickstartRomExecTakeoverState != KickstartRomExecTakeoverState.Active)
            {
                return false;
            }

            var execBase = GetActiveExecBase();
            var scheduleAddress = unchecked((uint)((int)execBase + vectorOffset));
            var stack = state.A[7];
            if (stack < 4 ||
                !_machine.Bus.IsMappedMemoryRange(stack - 4, 4) ||
                !_machine.Bus.IsCpuPhysicalAddressMapped(scheduleAddress, 2, AmigaBusAccessKind.CpuInstructionFetch))
            {
                AddExecLikeDiagnostic("AMIGA_BOOT_EXEC_SCHEDULE", "Unable to enter a native Exec task-switch vector.");
                return false;
            }

            _machine.Bus.WriteLong(stack - 4, returnAddress, state.Cycles);
            state.A[7] = stack - 4;
            state.A[6] = execBase;
            state.ProgramCounter = scheduleAddress;
            return true;
        }

        private bool TryDispatchPendingExecInterruptServer()
        {
            if (_kickstartRomBootActive &&
				_kickstartRomExecTakeoverState != KickstartRomExecTakeoverState.Active)
            {
                return false;
            }
			if (_activeExecInterruptSource < 0)
			{
				var platform = new HostGuestMemoryExecPlatform(_guestMemory);
				for (var queue = 4; queue >= 0; queue--)
				{
					var interrupt = PortableExecMemory.ExecInterruptCore.TakeSoftInterrupt(ref platform,
						global::Amiga.APTR.FromPointer(GetActiveExecBase()), queue).Raw;
					if (interrupt == 0) continue;
					_activeExecInterruptSource = -2;
					CaptureExecInterruptReturnState(_machine.Cpu.State);
					if (StartExecInterruptCallback(_machine.Cpu.State, interrupt)) return true;
					_activeExecInterruptSource = -1;
					_execInterruptReturnState = null;
					_execInterruptReturnProgramCounter = 0;
				}
			}

            var activeBits = _machine.Bus.Paula.ActiveInterruptBits;
            var newlyAsserted = (ushort)(activeBits & ~_observedExecInterruptBits);
            _observedExecInterruptBits = activeBits;
            for (var bit = 0; bit < 14; bit++)
            {
                if ((newlyAsserted & (1 << bit)) != 0 && HasGuestInterruptServers(bit))
                {
                    _pendingExecInterruptSources.Enqueue(bit);
                }
            }

            if (_activeExecInterruptSource >= 0 || _pendingExecInterruptSources.Count == 0)
            {
                return false;
            }

            _activeExecInterruptSource = _pendingExecInterruptSources.Dequeue();
            _activeExecInterruptServerIndex = 0;
            _activeExecInterruptServers = GetGuestInterruptServers(_activeExecInterruptSource);
            AcknowledgeExecInterruptSource(_activeExecInterruptSource);
            CaptureExecInterruptReturnState(_machine.Cpu.State);
            return StartNextExecInterruptServer(_machine.Cpu.State);
        }

        internal void AcknowledgeExecInterruptSource(int interruptSource)
        {
            if ((uint)interruptSource >= 14)
                return;
            var mask = (ushort)(1 << interruptSource);
            var acknowledgementCycle = Math.Max(
                _machine.Cpu.State.Cycles,
                _machine.Bus.CausalBusExecutor.ExecutedThroughCycle);
            _machine.Bus.WriteWord(
                CustomInterruptRequestAddress,
                mask,
                acknowledgementCycle);
            _observedExecInterruptBits &= unchecked((ushort)~mask);
        }

        private void ContinueExecInterruptServer(M68kCpuState state)
        {
			if (_activeExecInterruptSource == -2)
			{
				FinishExecInterruptDispatch(state);
				return;
			}
            _activeExecInterruptServerIndex++;
            _ = StartNextExecInterruptServer(state);
        }

        private bool StartNextExecInterruptServer(M68kCpuState state)
        {
            var servers = _activeExecInterruptServers;
            if (servers == null)
            {
                FinishExecInterruptDispatch(state);
                return false;
            }
            if (servers.Count == 0)
            {
                FinishExecInterruptDispatch(state);
                return false;
            }

            while (_activeExecInterruptServerIndex < servers.Count)
            {
                var server = servers[_activeExecInterruptServerIndex];
                if (!StartExecInterruptDescriptorCallback(state, server))
                {
                    _activeExecInterruptServerIndex++;
                    continue;
                }
                return true;
            }

            FinishExecInterruptDispatch(state);
            return false;
        }

		private bool StartExecInterruptCallback(M68kCpuState state, uint interrupt)
			=> StartExecInterruptDescriptorCallback(
				state,
				new SyntheticInterruptServer(
					interrupt,
					_machine.Bus.ReadLong(interrupt + InterruptDataOffset),
					_machine.Bus.ReadLong(interrupt + InterruptCodeOffset)));

		private bool StartExecInterruptDescriptorCallback(
			M68kCpuState state,
			SyntheticInterruptServer interrupt)
		{
			var code = interrupt.CodeAddress;
			if (code == 0 || !_machine.Bus.IsCpuPhysicalAddressMapped(code, 2, AmigaBusAccessKind.CpuInstructionFetch))
				return false;
			// Classic hardware interrupt servers receive the custom-register
			// base in A0 and their is_Data value in A1.  They also execute in
			// supervisor mode at the interrupt source's IPL; the host bridge does
			// not enter through a CPU exception vector, so establish that state
			// explicitly before using the supervisor stack.  Software interrupts
			// do not have a custom-chip source, so preserve their caller A0.
			if (_activeExecInterruptSource >= 0)
			{
				EnterHardwareInterruptContext(state, _activeExecInterruptSource);
				state.A[0] = CustomRegisterBaseAddress;
			}
			state.A[1] = interrupt.DataAddress;
			state.A[6] = GetActiveExecBase();
			state.A[7] -= 4;
			_machine.Bus.WriteLong(state.A[7], ExecInterruptContinuationAddress, state.Cycles);
			state.ProgramCounter = code + 6;
			if (_machine.Bus.TryInvokeHostGatewayAt(code, state))
			{
				if (state.ProgramCounter == code + 6)
				{
					state.ProgramCounter = _machine.Bus.ReadLong(state.A[7]);
					state.A[7] += 4;
				}
				return true;
			}
			state.ProgramCounter = code;
			return true;
		}

		private static void EnterHardwareInterruptContext(
			M68kCpuState state,
			int interruptSource)
		{
			state.Stopped = false;
			var level = interruptSource switch
			{
				<= 2 => 1,
				3 => 2,
				<= 6 => 3,
				<= 10 => 4,
				<= 12 => 5,
				_ => 6,
			};
			state.StatusRegister = (ushort)(
				(state.StatusRegister & ~M68kCpuState.Trace & 0xF8FF) |
				M68kCpuState.Supervisor |
				(level << 8));
		}

        private bool HasGuestInterruptServers(int interruptNumber)
            => GetGuestInterruptServers(interruptNumber).Count != 0;

        private List<SyntheticInterruptServer> GetGuestInterruptServers(int interruptNumber)
        {
            var result = new List<SyntheticInterruptServer>();
			var platform = new HostGuestMemoryExecPlatform(_guestMemory);
			var execBase = GetActiveExecBase();
			var first = PortableExecMemory.ExecInterruptCore.FirstServer(ref platform,
				global::Amiga.APTR.FromPointer(execBase), interruptNumber).Raw;
			if (first == 0)
			{
				var vector = execBase + (uint)global::Amiga.ExecLayout.ExecBase.IntVector0 +
					(uint)interruptNumber * global::Amiga.IntVector.Size;
				var code = _machine.Bus.ReadLong(
					vector + (uint)global::Amiga.ExecLayout.IntVector.Code);
				if (code != 0 && code != uint.MaxValue)
				{
					result.Add(new SyntheticInterruptServer(
						vector,
						_machine.Bus.ReadLong(
							vector + (uint)global::Amiga.ExecLayout.IntVector.Data),
						code));
				}
				return result;
			}
			for (var server = first; server != 0 && IsValidExecNode(server); server = _machine.Bus.ReadLong(server))
            {
                result.Add(new SyntheticInterruptServer(
					server,
					_machine.Bus.ReadLong(server + InterruptDataOffset),
					_machine.Bus.ReadLong(server + InterruptCodeOffset)));
            }
            return result;
        }

        private void FinishExecInterruptDispatch(M68kCpuState state)
        {
            if (_execInterruptReturnState != null)
                state.CopyTaskContextFrom(_execInterruptReturnState);
            else
                state.ProgramCounter = _execInterruptReturnProgramCounter;
            _activeExecInterruptSource = -1;
            _activeExecInterruptServerIndex = 0;
            _activeExecInterruptServers = null;
            _execInterruptReturnState = null;
            _execInterruptReturnProgramCounter = 0;
        }

        private void CaptureExecInterruptReturnState(M68kCpuState state)
        {
            _execInterruptReturnState = new M68kCpuState();
            _execInterruptReturnState.CopyTaskContextFrom(state);
            _execInterruptReturnProgramCounter = state.ProgramCounter;
        }

        private long GetNextSyntheticVBlankBoundaryCycle(long currentCycle, long targetCycle)
        {
            if (targetCycle <= currentCycle)
            {
                return targetCycle;
            }

            var nextFrameCycle = _machine.Bus.GetNextFrameStartCycle(Math.Max(0, currentCycle));
            var nextBoundary = targetCycle;
            if (_pendingCopperListCycle > currentCycle)
            {
                nextBoundary = Math.Min(nextBoundary, _pendingCopperListCycle);
            }

            if (_syntheticVBlankInterruptServers.Count != 0 ||
                _pendingGraphicsDoubleBufferMessages.Count != 0)
            {
                nextBoundary = Math.Min(nextBoundary, nextFrameCycle);
            }

            for (var index = 0; index < _pendingGraphicsDoubleBufferMessages.Count; index++)
            {
                var dueCycle = _pendingGraphicsDoubleBufferMessages[index].DueCycle;
                if (dueCycle > currentCycle)
                    nextBoundary = Math.Min(nextBoundary, dueCycle);
            }

            return nextBoundary;
        }

        private void ScheduleDoubleBufferMessages(
            uint viewPortAddress,
            uint previousBitMapAddress,
            uint bitMapAddress,
            uint dbufInfoAddress,
            long cycle)
        {
            _ = viewPortAddress;
            _ = previousBitMapAddress;
            _ = bitMapAddress;
            if (dbufInfoAddress == 0 ||
                !_machine.Bus.IsCpuPhysicalAddressMapped(dbufInfoAddress, GraphicsDBufInfoSize, AmigaBusAccessKind.CpuDataRead))
            {
                return;
            }

            // The untimed graphics gateway passes zero.  In that path anchor
            // the message schedule to the live CPU boundary rather than to
            // the beginning of the emulated machine's first frame.
            var normalizedCycle = Math.Max(0, cycle != 0 ? cycle : _machine.Cpu.State.Cycles);
            var safeCycle = _machine.Bus.GetNextFrameStartCycle(normalizedCycle);
            var displayCycle = _machine.Bus.GetNextFrameStartCycle(safeCycle);
            QueueDoubleBufferMessage(
                dbufInfoAddress + (uint)GraphicsDBufInfoSafeMessageOffset,
                safeCycle);
            QueueDoubleBufferMessage(
                dbufInfoAddress + (uint)GraphicsDBufInfoDispMessageOffset,
                displayCycle);
        }

        private void CancelDoubleBufferMessages(uint dbufInfoAddress)
        {
            if (dbufInfoAddress == 0)
                return;

            var safeMessage = dbufInfoAddress + (uint)GraphicsDBufInfoSafeMessageOffset;
            var displayMessage = dbufInfoAddress + (uint)GraphicsDBufInfoDispMessageOffset;
            for (var index = _pendingGraphicsDoubleBufferMessages.Count - 1; index >= 0; index--)
            {
                var messageAddress = _pendingGraphicsDoubleBufferMessages[index].MessageAddress;
                if (messageAddress == safeMessage || messageAddress == displayMessage)
                    _pendingGraphicsDoubleBufferMessages.RemoveAt(index);
            }
        }

        private void QueueDoubleBufferMessage(uint messageAddress, long dueCycle)
        {
            for (var index = _pendingGraphicsDoubleBufferMessages.Count - 1; index >= 0; index--)
            {
                if (_pendingGraphicsDoubleBufferMessages[index].MessageAddress == messageAddress)
                    _pendingGraphicsDoubleBufferMessages.RemoveAt(index);
            }

            _pendingGraphicsDoubleBufferMessages.Add(
                new PendingGraphicsDoubleBufferMessage(messageAddress, dueCycle));
        }

        private void AdvancePendingGraphicsDoubleBufferMessages(long currentCycle)
        {
            // A host-side cycle advance may skip more than one frame (for
            // example when a guest wait resumes after a long scheduler
            // quantum).  Deliver replies in due-cycle order rather than in
            // list order: the list is intentionally append-oriented and the
            // display message is appended after the safe message.  Walking it
            // backwards would otherwise enqueue DispMessage before the
            // earlier SafeMessage when both boundaries were crossed at once.
            while (true)
            {
                var pendingIndex = -1;
                var pendingCycle = long.MaxValue;
                for (var index = 0; index < _pendingGraphicsDoubleBufferMessages.Count; index++)
                {
                    var candidate = _pendingGraphicsDoubleBufferMessages[index];
                    if (candidate.DueCycle > currentCycle ||
                        candidate.DueCycle > pendingCycle ||
                        (candidate.DueCycle == pendingCycle && pendingIndex >= 0))
                    {
                        continue;
                    }

                    pendingIndex = index;
                    pendingCycle = candidate.DueCycle;
                }

                if (pendingIndex < 0)
                    return;

                var pending = _pendingGraphicsDoubleBufferMessages[pendingIndex];
                _pendingGraphicsDoubleBufferMessages.RemoveAt(pendingIndex);
                // The message was validated when it was scheduled.  Deliver it
                // through the same Exec service used by the ReplyMsg gateway;
                // do not re-check CPU ownership here because this is an outer
                // frame boundary, not a guest bus access.
                var state = _machine.Cpu.State;
                state.Cycles = Math.Max(state.Cycles, currentCycle);
                _execPortServices.ReplyMessage(pending.MessageAddress);
            }
        }

        private void HandleDefaultTaskTrap(M68kCpuState state)
            => _taskTrapRecovery.HandleDefault(state);

        private bool TryRecoverHostTaskTrapFromZeroVector()
            => _taskTrapRecovery.TryRecoverFromZeroPc(_machine.Cpu.State, _memoryListInstalled);

        private uint WaitForNextFrame(M68kCpuState state)
        {
            // WaitTOF is a graphics.library synchronization primitive, so it
            // must follow the active Agnus beam clock rather than assuming
            // canonical PAL timing.  The profile-aware bus boundary also
            // handles NTSC 262/263-line alternation and ECS geometry changes.
            var nextFrameCycle = _machine.Bus.GetNextFrameStartCycle(Math.Max(0, state.Cycles));
            state.Cycles = Math.Max(state.Cycles + 1, nextFrameCycle);
            return 0;
        }

        private long WaitForViewportBottom(uint viewPort, long cycle)
        {
            if (viewPort == 0 ||
                !TryReadWordField(viewPort, ViewPortDHeightOffset, out var height) ||
                height == 0)
            {
                return cycle;
            }

            var dy = TryReadWordField(viewPort, ViewPortDyOffsetOffset, out var dyWord)
                ? unchecked((short)dyWord)
                : 0;
            // The active DIW start is the chipset/profile-owned source of the
            // vertical display origin. Do not bake PAL geometry into a wait
            // that may run on an NTSC or ECS timing profile.
            var displayStartLine = (_machine.Bus.AgnusRegisters.DiwStart >> 8) & 0x00FF;
            var targetLine = displayStartLine + dy + height;
            var beam = _machine.Bus.GetBeamPosition(cycle);
            var target = GetViewportBottomCycle(beam, targetLine);
            if (target <= cycle)
            {
                var nextFrame = _machine.Bus.GetNextFrameStartCycle(cycle);
                target = GetViewportBottomCycle(_machine.Bus.GetBeamPosition(nextFrame), targetLine);
            }

            return Math.Max(cycle, target);
        }

        private uint GetViewPortModeId(uint viewPort)
        {
            if (viewPort == 0 ||
                !TryReadWordField(viewPort, ViewPortModesOffset, out var viewModes))
            {
                return CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsModeIds.Invalid;
            }

            var ntsc = _machine.Bus.Chipset.VideoStandard == VideoStandard.Ntsc;
            return CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsModeIds.TryGetNativeModeId(
                viewModes,
                ntsc,
                out var modeId)
                ? modeId
                : CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsModeIds.Invalid;
        }

        private long GetViewportBottomCycle(CopperMod.Amiga.CustomChips.Agnus.AgnusBeamPosition beam, int targetLine)
        {
            if (targetLine < 0)
                targetLine = 0;

            if (targetLine >= beam.RasterLines)
                return _machine.Bus.GetFrameStopCycle(beam.FrameStartCycle);

            return _machine.Bus.GetLineStartCycle(beam.FrameStartCycle, targetLine);
        }

        private uint BltBitMap(M68kCpuState state)
        {
            var sourceIsRtg = CyberGraphics.IsRtgBitMap(state.A[0]);
            var destinationIsRtg = CyberGraphics.IsRtgBitMap(state.A[1]);
            var pixels = sourceIsRtg
                ? destinationIsRtg
                    ? CyberGraphics.BlitRtgToRtg(
                        state.A[0],
                        Long(state.D[0]),
                        Long(state.D[1]),
                        state.A[1],
                        Long(state.D[2]),
                        Long(state.D[3]),
                        Long(state.D[4]),
                        Long(state.D[5]),
                        (byte)state.D[6],
                        (byte)state.D[7],
                        state.A[2])
                    : CyberGraphics.BlitRtgToPlanar(
                        state.A[0],
                        Long(state.D[0]),
                        Long(state.D[1]),
                        state.A[1],
                        Long(state.D[2]),
                        Long(state.D[3]),
                        Long(state.D[4]),
                        Long(state.D[5]),
                        (byte)state.D[6],
                        (byte)state.D[7],
                        state.A[2])
                : CyberGraphics.BlitPlanarToRtg(
                    state.A[0],
                    Long(state.D[0]),
                    Long(state.D[1]),
                    state.A[1],
                    Long(state.D[2]),
                    Long(state.D[3]),
                    Long(state.D[4]),
                    Long(state.D[5]),
                    (byte)state.D[6],
                    (byte)state.D[7],
                    state.A[2]);
            return pixels == 0
                ? 0u
                : CyberGraphics.TryGetBitMapSurface(state.A[1], out var destination)
                    ? checked((uint)destination.Depth)
                    : 1u;
        }

        private uint ClipBlit(M68kCpuState state)
        {
            if (!TryGetRastPortBitMap(state.A[0], out var sourceBitMap) ||
                !TryGetRastPortBitMap(state.A[1], out var destinationBitMap))
            {
                return 0;
            }

            return BltBitMapToRastPort(state, sourceBitMap, destinationBitMap);
        }

        private uint BltBitMapRastPort(M68kCpuState state)
        {
            if (!TryGetRastPortBitMap(state.A[1], out var destinationBitMap))
            {
                return 0;
            }

            return BltBitMapToRastPort(state, state.A[0], destinationBitMap);
        }

        private uint BltBitMapToRastPort(
            M68kCpuState state,
            uint sourceBitMap,
            uint destinationBitMap)
        {
            var pixels = CyberGraphics.BlitPlanarToRtg(
                sourceBitMap,
                Long(state.D[0]),
                Long(state.D[1]),
                destinationBitMap,
                Long(state.D[2]),
                Long(state.D[3]),
                Long(state.D[4]),
                Long(state.D[5]),
                (byte)state.D[6],
                0xFF);
            return pixels == 0 ? 0u : 1u;
        }

        private uint AllocateBitMap(
            M68kCpuState state,
            CyberGraphicsPixelFormat? requestedPixelFormat = null)
        {
            if (state.D[0] is 0 or > 32768 || state.D[1] is 0 or > 32768 ||
                state.D[2] is 0 or > 32 || !_machine.Bus.RtgVram.Active)
            {
                return 0;
            }

            var width = (int)state.D[0];
            var height = (int)state.D[1];
            var depth = (int)state.D[2];

            CyberGraphicsPixelFormat pixelFormat;
            CyberGraphicsSurface? friendSurface = null;
            if (requestedPixelFormat.HasValue)
            {
                pixelFormat = requestedPixelFormat.Value;
            }
            else if (state.A[0] != 0 && CyberGraphics.TryGetBitMapSurface(state.A[0], out friendSurface))
            {
                pixelFormat = friendSurface.PixelFormat;
            }
            else
            {
                pixelFormat = depth <= 8
                    ? CyberGraphicsPixelFormat.Lut8
                    : depth <= 16
                        ? CyberGraphicsPixelFormat.Rgb16
                        : CyberGraphicsPixelFormat.Argb32;
            }

            var surface = CyberGraphics.AllocateRtgSurface(width, height, pixelFormat);
            if (surface == null)
            {
                return 0;
            }

            if (friendSurface != null && friendSurface.ColorMapAddress != 0)
            {
                surface.AssociateColorMap(friendSurface.ColorMapAddress, friendSurface.Palette);
            }

            const int bitMapSize = BitMapPlanesOffset + 8 * 4;
            var bitMap = ((ICyberGraphicsGuestServices)this).Allocate(bitMapSize);
            if (bitMap == 0)
            {
                CyberGraphics.FreeRtgSurface(surface);
                return 0;
            }

            WriteRtgBitMap(bitMap, surface);
            CyberGraphics.RegisterBitMap(bitMap, surface);
            _allocatedRtgBitMaps[bitMap] = bitMapSize;
            if ((state.D[3] & BitMapFlagClear) != 0)
            {
                _machine.Bus.ClearMemory(surface.GuestBaseAddress, checked(surface.BytesPerRow * surface.Height));
            }

            return bitMap;
        }

        private void FreeBitMap(uint bitMap)
        {
            if (!CyberGraphics.TryGetBitMapSurface(bitMap, out var surface))
            {
                return;
            }

            CyberGraphics.UnregisterSurface(surface);
            CyberGraphics.FreeRtgSurface(surface);
            if (_allocatedRtgBitMaps.Remove(bitMap, out var byteCount))
            {
                ((ICyberGraphicsGuestServices)this).Free(bitMap, byteCount);
            }
        }

        private void CloseRomLibrary(M68kCpuState state)
        {
            if (_kickstartRomExecTakeoverState == KickstartRomExecTakeoverState.Active ||
                GetRomExecLibraryServices().ContainsLibrary(state.A[1]))
            {
                GetRomExecLibraryServices().CloseLibrary(state, ExecLibraryCallContinuationAddress);
            }
            else
            {
                state.D[0] = 0;
            }
        }

        private void CloseCompatibilityLibrary(M68kCpuState state)
        {
            if (GetRomExecLibraryServices().ContainsLibrary(state.A[1]))
                GetRomExecLibraryServices().CloseLibrary(
                    state, ExecLibraryCallContinuationAddress);
            else
                state.D[0] = 0;
        }

        private void AddRomLibrary(M68kCpuState state)
        {
            GetRomExecLibraryServices().AddLibrary(state);
            state.D[0] = 0;
        }

        private void RemoveRomLibrary(M68kCpuState state)
        {
            GetRomExecLibraryServices().RemLibrary(state);
            state.D[0] = 0;
        }

        private void AddRomDevice(M68kCpuState state)
        {
            GetRomExecLibraryServices().AddDevice(state);
            state.D[0] = 0;
        }

        private void RemoveRomDevice(M68kCpuState state)
        {
            GetRomExecLibraryServices().RemDevice(state);
            state.D[0] = 0;
        }

        private void OpenRomDevice(M68kCpuState state)
        {
            if (_kickstartRomExecTakeoverState == KickstartRomExecTakeoverState.Active)
            {
                GetRomExecLibraryServices().OpenDevice(state, ExecLibraryCallContinuationAddress);
            }
            else
            {
                state.D[0] = 0xFFFF_FFFF;
            }
        }

        private void CloseRomDevice(M68kCpuState state)
        {
            if (_kickstartRomExecTakeoverState == KickstartRomExecTakeoverState.Active)
            {
                GetRomExecLibraryServices().CloseDevice(state, ExecLibraryCallContinuationAddress);
            }
            else
            {
                state.D[0] = 0;
            }
        }

        private void AddRomResource(M68kCpuState state)
        {
            GetRomExecLibraryServices().AddResource(state);
            state.D[0] = 0;
        }

        private void RemoveRomResource(M68kCpuState state)
        {
            GetRomExecLibraryServices().RemResource(state);
            state.D[0] = 0;
        }

        private uint OpenRomResource(M68kCpuState state)
            => GetRomExecLibraryServices().OpenResource(state);

        private uint OpenCompatibilityResource(M68kCpuState state)
        {
            var resource = GetRomExecLibraryServices().OpenResource(state);
            return resource != 0 ? resource :
                TryGetHostLibraryBase(null, state.A[1], out var resourceBase) ? resourceBase : 0;
        }

        private uint GetBitMapAttr(uint bitMap, uint attribute)
        {
            if (!CyberGraphics.TryGetBitMapSurface(bitMap, out var surface))
            {
                return 0;
            }

            return attribute switch
            {
                BitMapAttributeHeight => checked((uint)surface.Height),
                BitMapAttributeDepth => checked((uint)surface.Depth),
                BitMapAttributeWidth => checked((uint)surface.Width),
                BitMapAttributeFlags => 0,
                _ => 0
            };
        }

        // Kept as the boot-coordinator forwarding point for existing lifecycle
        // probes; vector ownership is in CopperStart.Runtime.TaskTrapRuntime.
        private void EnsureTaskTrapVectorsCurrent()
            => _taskTrapRuntime.EnsureVectorsCurrent();

        private void AddSyntheticGadgetList(M68kCpuState state)
        {
            var window = state.A[0];
            var gadget = state.A[1];
            if (window != 0 &&
                (!CanAddressField(window, 0, WindowFirstGadgetOffset + 4) ||
                 !_machine.Bus.IsMappedMemoryRange(window, WindowFirstGadgetOffset + 4) ||
                 !_machine.Bus.IsWritableMemoryRange(
                     window + (uint)WindowFirstGadgetOffset,
                     sizeof(uint))))
            {
                // AddGList publishes Window.FirstGadget.  A readable
                // provider/image overlay is not a writable compatibility
                // chain, so leave the callback result and host gadget claim
                // untouched for native/provider ownership.
                return;
            }

            if (gadget != 0 &&
                (!CanAddressField(gadget, 0, GadgetHeightOffset + 2) ||
                 !_machine.Bus.IsMappedMemoryRange(gadget, GadgetHeightOffset + 2)))
            {
                return;
            }

            if (gadget != 0)
            {
                _syntheticGadgetListAddress = gadget;
                if (window != 0)
                {
                    _machine.Bus.WriteLong(window + WindowFirstGadgetOffset, gadget);
                }
            }

            state.D[0] = 0;
        }

        private void ModifySyntheticIdcmp(M68kCpuState state)
            => _ = TryModifySyntheticIdcmp(state);

        private bool TryModifySyntheticIdcmp(M68kCpuState state)
        {
            var window = state.A[0];
            var userPort = 0u;
            if (window != 0)
            {
                if (!CanAddressField(window, 0, WindowUserPortOffset + 4) ||
                    !_machine.Bus.IsMappedMemoryRange(window, WindowUserPortOffset + 4) ||
                    !_machine.Bus.IsWritableMemoryRange(
                        window + (uint)WindowIdcmpFlagsOffset,
                        sizeof(uint)) ||
                    !TryReadLongField(window, WindowUserPortOffset, out userPort) ||
                    (userPort == 0 &&
                     !_machine.Bus.IsWritableMemoryRange(
                         window + (uint)WindowUserPortOffset,
                         sizeof(uint))))
                {
                    // ModifyIDCMP publishes IdcmpFlags and may lazily publish
                    // a UserPort. Keep the whole operation unclaimed when
                    // either public LONG is readable but masked read-only.
                    return false;
                }
            }

            _syntheticIdcmpFlags = state.D[0];
            if (window != 0)
            {
                _machine.Bus.WriteLong(window + WindowIdcmpFlagsOffset, _syntheticIdcmpFlags);
                if (userPort == 0)
                {
                    _machine.Bus.WriteLong(window + WindowUserPortOffset, EnsureSyntheticUserPort());
                }
            }

            return true;
        }

        private void ConfigureSyntheticScreenFromNewScreen(
            uint newScreen,
            bool allowMalformedFontOverride = false,
            bool allowMalformedTitleOverride = false,
            bool allowScreenTypeOverride = false)
        {
            _syntheticScreenConfigurationRejected = false;
            if (newScreen != 0 &&
                _syntheticScreenAddress == 0 &&
                _syntheticPlaneAddress == 0 &&
                ((newScreen & 1u) != 0 ||
                 !CanAddressField(newScreen, 0, NewScreenStructMinimumSize) ||
                 !_machine.Bus.IsMappedMemoryRange(newScreen, NewScreenStructMinimumSize)))
            {
                // NewScreen contains word fields and must be even-aligned on
                // the 68k ABI.  A mapped odd pointer, or a non-null pointer
                // whose legacy prefix cannot be read, is an address/provider
                // boundary rather than an invitation to manufacture a
                // default synthetic screen.
                ResetSyntheticScreenRequestState();
                _syntheticScreenConfigurationRejected = true;
                return;
            }

            if (_syntheticScreenAddress != 0 ||
                _syntheticPlaneAddress != 0)
            {
                // This compatibility Intuition bridge owns one private
                // synthetic Screen session.  Reusing that public pointer for
                // a second OpenScreen request would silently apply the new
                // caller's geometry to a live Screen and make the original
                // owner indistinguishable from the replacement.  Leave the
                // active session untouched and decline so a native/provider
                // Intuition implementation can own the multi-screen case.
                _syntheticScreenConfigurationRejected = true;
                return;
            }

            if (newScreen == 0 ||
                !CanAddressField(newScreen, 0, NewScreenStructMinimumSize) ||
                !_machine.Bus.IsMappedMemoryRange(newScreen, NewScreenStructMinimumSize))
            {
                return;
            }

            var legacyScreenType = _machine.Bus.ReadWord(newScreen + NewScreenTypeOffset);
            var legacyScreenTypeNibble = (ushort)(legacyScreenType & 0x000F);
            if (!allowScreenTypeOverride &&
                legacyScreenTypeNibble != 0 &&
                legacyScreenTypeNibble != CustomScreenType)
            {
                // OpenScreen on a PUBLICSCREEN (or another non-custom type)
                // requires Intuition's public-screen list and notification
                // ownership. A tag-list caller may explicitly replace this
                // legacy type with SA_Type before the synthetic path stages
                // the request; the ordinary OpenScreen vector may not.
                ResetSyntheticScreenRequestState();
                _syntheticScreenConfigurationRejected = true;
                return;
            }
            if ((legacyScreenType & 0x0040) != 0)
            {
                // CUSTOMBITMAP transfers raster storage ownership to the
                // caller. Even a null/malformed legacy pointer must not be
                // turned into a newly allocated synthetic bitmap.
                ResetSyntheticScreenRequestState();
                _syntheticScreenConfigurationRejected = true;
                return;
            }

            var requestedLeft = ReadSignedWordOrDefault(newScreen + NewScreenLeftOffset, 0);
            var requestedTop = ReadSignedWordOrDefault(newScreen + NewScreenTopOffset, 0);
            var requestedWidth = DecodeLegacySyntheticScreenDimension(
                _machine.Bus.ReadWord(newScreen + NewScreenWidthOffset));
            var requestedHeight = DecodeLegacySyntheticScreenDimension(
                _machine.Bus.ReadWord(newScreen + NewScreenHeightOffset));
            var requestedDepth = _machine.Bus.ReadByte(newScreen + NewScreenDepthOffset);
            var requestedDetailPen = ReadSyntheticPenByte(
                newScreen + NewScreenDetailPenOffset,
                _syntheticScreenDetailPen);
            var requestedBlockPen = ReadSyntheticPenByte(
                newScreen + NewScreenBlockPenOffset,
                _syntheticScreenBlockPen);
            var requestedModes = _machine.Bus.ReadWord(newScreen + NewScreenViewModesOffset);
            var requestedFont = 0u;
            if (CanAddressField(newScreen, NewScreenFontOffset, sizeof(uint)) &&
                _machine.Bus.IsMappedMemoryRange(newScreen + (uint)NewScreenFontOffset, 4))
            {
                requestedFont = _machine.Bus.ReadLong(newScreen + (uint)NewScreenFontOffset);
                if (requestedFont != 0 &&
                    !_machine.Bus.IsMappedMemoryRange(requestedFont, CopperStartGraphicsLayouts.TextAttrSize) &&
                    !allowMalformedFontOverride)
                {
                    // NewScreen.Font is a caller-owned TextAttr envelope. A
                    // present but unreadable pointer belongs to native
                    // Intuition/provider ownership; do not silently replace
                    // it with the synthetic default font.
                    ResetSyntheticScreenRequestState();
                    _syntheticScreenConfigurationRejected = true;
                    return;
                }

                if (requestedFont != 0 &&
                    !_machine.Bus.IsMappedMemoryRange(requestedFont, CopperStartGraphicsLayouts.TextAttrSize))
                {
                    // OpenScreenTagList tags supplement/override the legacy
                    // NewScreen fields. Keep this malformed legacy value out
                    // of staged state so an explicit SA_Font (including the
                    // documented NULL override) can replace it below.
                    requestedFont = 0;
                }
            }
            var acceptsSuperHiresDepthCap =
                (requestedModes & ViewModeSuperHires) != 0 &&
                _machine.Bus.Chipset.SupportsEcsDisplayRegisters;
            var supportsAgaPlanar = SupportsAgaPlanarDisplay();
            var maximumNativeDepth = acceptsSuperHiresDepthCap
                ? 2
                : supportsAgaPlanar ? 8 : 6;
            if (requestedDepth > maximumNativeDepth && !acceptsSuperHiresDepthCap)
            {
                // The legacy OpenScreen vector has no SA_ErrorCode tag, but
                // it still must reject an over-depth native request rather
                // than silently manufacturing a six-plane screen.  Extended
                // or RTG requests are intercepted by their provider before
                // this private synthetic path claims them.
                ResetSyntheticScreenRequestState();
                _syntheticScreenConfigurationRejected = true;
                _syntheticScreenOpenErrorCode = OpenScreenErrorTooDeep;
                return;
            }
            var requestedFlags = (ushort)(
                _machine.Bus.ReadWord(newScreen + NewScreenTypeOffset) |
                SyntheticScreenShowTitle);
            if (CanAddressField(newScreen, NewScreenDefaultTitleOffset, sizeof(uint)) &&
                TryReadLong(newScreen + (uint)NewScreenDefaultTitleOffset, out var defaultTitle))
            {
                if (defaultTitle != 0 &&
                    !TryValidateSyntheticTitle(defaultTitle) &&
                    !allowMalformedTitleOverride)
                {
                    // NewScreen.DefaultTitle is a caller-owned C string. A
                    // present but unreadable title belongs to native
                    // Intuition/provider ownership rather than the synthetic
                    // screen session.
                    ResetSyntheticScreenRequestState();
                    _syntheticScreenConfigurationRejected = true;
                    return;
                }

                if (defaultTitle != 0 &&
                    !TryValidateSyntheticTitle(defaultTitle))
                {
                    // OpenScreenTagList may replace a malformed legacy title
                    // with an explicit SA_Title. Keep the bad legacy pointer
                    // out of staged state until that override is applied.
                    defaultTitle = 0;
                }

                _syntheticScreenDefaultTitleAddress = defaultTitle;
            }

            ApplySyntheticScreenRequest(
                requestedWidth,
                requestedHeight,
                requestedDepth,
                requestedModes,
                requestedLeft,
                requestedTop,
                requestedFlags);
            _syntheticScreenDetailPen = requestedDetailPen;
            _syntheticScreenBlockPen = requestedBlockPen;
            _syntheticScreenFontAttrAddress = requestedFont;
        }

        private bool ConfigureSyntheticScreenFromTagList(uint newScreen, uint tags)
        {
            // A pending SA_ErrorCode belongs only to this one accepted
            // OpenScreenTagList request.  Provider/native declines must leave
            // the caller's destination untouched, and a later unrelated
            // synthetic-screen call must not reuse an old error pointer.
            _syntheticScreenErrorCodeAddress = 0;
            _syntheticScreenOpenErrorCode = 0;
            _syntheticScreenVideoControlTags = 0;
            _syntheticScreenColorMapEntries = 0;
            _syntheticCustomBitMapAddress = 0;
            _syntheticScreenConfigurationRejected = false;

            if (_syntheticScreenAddress != 0 || _syntheticPlaneAddress != 0)
            {
                // A second OpenScreenTagList must not be treated as an
                // idempotent reopen of the existing private Screen.  The
                // synthetic bridge cannot publish Intuition's public-screen
                // list and notification ownership, so preserve the live
                // session and leave this request available to native/provider
                // ownership instead of returning a stale Screen pointer.
                return false;
            }

            // A non-null NewScreen is still a guest structure even when all
            // requested attributes arrive through the tag list.  Do not
            // treat an unreadable legacy prefix as an omitted structure and
            // manufacture a screen from the remaining tags.
            if (newScreen != 0 &&
                ((newScreen & 1u) != 0 ||
                 !CanAddressField(newScreen, 0, NewScreenStructMinimumSize) ||
                 !_machine.Bus.IsMappedMemoryRange(newScreen, NewScreenStructMinimumSize)))
            {
                return false;
            }

            if (!TryReadSyntheticScreenTags(tags, out var explicitTags))
                return false;

            NormalizeSyntheticScreenTagAliases(explicitTags);

            Dictionary<uint, uint>? extensionTags = null;
            if (newScreen != 0 &&
                CanAddressField(newScreen, 0, ExtNewScreenExtensionOffset + sizeof(uint)) &&
                _machine.Bus.IsMappedMemoryRange(newScreen, ExtNewScreenExtensionOffset + 4) &&
                (_machine.Bus.ReadWord(newScreen + NewScreenTypeOffset) & NewScreenExtended) != 0)
            {
                var extension = _machine.Bus.ReadLong(newScreen + ExtNewScreenExtensionOffset);
                if (!TryReadSyntheticScreenTags(extension, out extensionTags))
                    return false;

                NormalizeSyntheticScreenTagAliases(extensionTags);
            }

            var hasScreenTypeOverride = HasSyntheticScreenTag(
                explicitTags,
                extensionTags,
                ScreenTagType);
            if (newScreen != 0 &&
                CanAddressField(newScreen, NewScreenTypeOffset, sizeof(ushort)) &&
                _machine.Bus.IsMappedMemoryRange(newScreen, NewScreenTypeOffset + 2))
            {
                var legacyScreenType = _machine.Bus.ReadWord(newScreen + NewScreenTypeOffset);
                if ((legacyScreenType & 0x0040) != 0 ||
                    (!hasScreenTypeOverride &&
                     (legacyScreenType & 0x000F) != 0 &&
                     (legacyScreenType & 0x000F) != CustomScreenType))
                {
                    // A tag list may replace the legacy screen type, but it
                    // cannot make a caller-owned CUSTOMBITMAP or a public
                    // screen private without implementing the corresponding
                    // Intuition ownership contract.
                    return false;
                }
            }

            // A recognized screen attribute whose ownership is not modeled by
            // this standard-planar bridge must fall through to native Intuition
            // or a provider. Unknown/private tags remain forward-compatible and
            // are intentionally ignored by the tag decoder.
            if (HasUnsupportedSyntheticScreenTag(explicitTags, extensionTags))
                return false;

            // SA_VideoControl is a pointer to a graphics.library TagItem
            // list.  It is accepted only when the complete guest list can be
            // inspected before the synthetic screen starts staging.  The
            // graphics core performs the semantic/ownership validation after
            // a ColorMap has been attached to the new ViewPort.
            var videoControlTags = GetSyntheticScreenTag(
                explicitTags,
                ScreenTagVideoControl,
                GetSyntheticScreenTag(extensionTags, ScreenTagVideoControl, 0));
            if (HasSyntheticScreenTag(explicitTags, extensionTags, ScreenTagVideoControl))
            {
                if (videoControlTags == 0 ||
                    !TryReadSyntheticScreenTags(videoControlTags, out _))
                {
                    return false;
                }

                _syntheticScreenVideoControlTags = videoControlTags;
            }

            var colorMapEntries = GetSyntheticScreenTag(
                explicitTags,
                ScreenTagColorMapEntries,
                GetSyntheticScreenTag(extensionTags, ScreenTagColorMapEntries, 0));
            if (HasSyntheticScreenTag(explicitTags, extensionTags, ScreenTagColorMapEntries))
            {
                // ColorMap entries are bounded by the portable ColorMap
                // implementation's V39-compatible maximum.  A zero or
                // oversized request is provider-owned rather than a reason
                // to silently substitute the depth-derived palette size.
                if (colorMapEntries == 0 || colorMapEntries > 256)
                    return false;

                _syntheticScreenColorMapEntries = colorMapEntries;
            }

            var requestedTypeTag = GetSyntheticScreenTag(
                explicitTags,
                ScreenTagType,
                GetSyntheticScreenTag(extensionTags, ScreenTagType, uint.MaxValue));
            if (requestedTypeTag != uint.MaxValue &&
                (((requestedTypeTag & ~0x000Fu) != 0) ||
                 ((requestedTypeTag & 0x000F) != 0 &&
                  (requestedTypeTag & 0x000F) != CustomScreenType)))
            {
                // PUBLICSCREEN and all other non-custom screen types require
                // Intuition's public-screen list/depth ownership. The
                // synthetic session only owns private CUSTOMSCREEN objects.
                return false;
            }

            // OpenScreenTagList applies the explicit list over the legacy
            // NewScreen/ExtNewScreen request.  Decode the display capability
            // before staging any legacy fields so an unsupported native mode
            // can fall through without leaving a partial synthetic request
            // behind for a later provider/native implementation.
            var hasDisplayIdTag = HasSyntheticScreenTag(
                explicitTags,
                extensionTags,
                ScreenTagDisplayId);
            var displayId = GetSyntheticScreenTag(
                explicitTags,
                ScreenTagDisplayId,
                GetSyntheticScreenTag(extensionTags, ScreenTagDisplayId, 0));
            var displayModes = (ushort)0;
            var hasNativeDisplayMode = hasDisplayIdTag &&
                TryDecodeNativeScreenMode(displayId, out displayModes);
            if (hasDisplayIdTag && !hasNativeDisplayMode)
            {
                // An explicit non-native ModeID belongs to a native monitor
                // or CyberGraphX provider.  Do not turn an unknown request
                // into a default OCS/ECS screen, and do not publish legacy
                // NewScreen fields before the provider gets its chance.
                return false;
            }
            if (HasSyntheticScreenTag(explicitTags, extensionTags, ScreenTagCustomBitMap))
            {
                // SA_BitMap transfers storage ownership to the caller, but a
                // standard-planar screen can still be presented by this
                // bridge when the complete bitmap header and declared plane
                // spans are readable.  Keep the pointer as a borrowed
                // session handle; EnsureSyntheticScreenBitmap will publish
                // the same planes without allocating or later freeing them.
                var customBitMap = GetSyntheticScreenTag(
                    explicitTags,
                    ScreenTagCustomBitMap,
                    GetSyntheticScreenTag(extensionTags, ScreenTagCustomBitMap, 0));
                if (!TryValidateSyntheticCustomBitMap(customBitMap))
                    return false;

                _syntheticCustomBitMapAddress = customBitMap;
            }
            if (HasSyntheticScreenTag(explicitTags, extensionTags, ScreenTagPubName) ||
                HasSyntheticScreenTag(explicitTags, extensionTags, ScreenTagPubSig) ||
                HasSyntheticScreenTag(explicitTags, extensionTags, ScreenTagPubTask))
            {
                // A public screen is linked into Intuition's public-screen
                // list and may signal a task when its visitors are gone.
                // Keep those list and task semantics with native Intuition
                // instead of claiming a private synthetic screen.
                return false;
            }
            if (hasNativeDisplayMode &&
                (displayModes & ViewModeSuperHires) != 0 &&
                !_machine.Bus.Chipset.SupportsEcsDisplayRegisters)
            {
                // The display database keeps ECS SuperHires discoverable on
                // OCS machines and reports DI_AVAIL_NO_CHIPS, but Intuition
                // must not publish an executable screen for that mode.  A
                // separate native/RTG provider may still claim the request.
                return false;
            }

            var hasTitleOverride = HasSyntheticScreenTag(
                explicitTags,
                extensionTags,
                ScreenTagTitle);
            var legacyTitle = 0u;
            if (newScreen != 0 &&
                CanAddressField(newScreen, NewScreenDefaultTitleOffset, sizeof(uint)) &&
                TryReadLong(newScreen + (uint)NewScreenDefaultTitleOffset, out var legacyDefaultTitle))
            {
                legacyTitle = legacyDefaultTitle;
            }
            var requestedTitleBeforeStaging = GetSyntheticScreenTag(
                explicitTags,
                ScreenTagTitle,
                GetSyntheticScreenTag(extensionTags, ScreenTagTitle, legacyTitle));
            if (requestedTitleBeforeStaging != 0 &&
                !TryValidateSyntheticTitle(requestedTitleBeforeStaging))
            {
                // Validate the effective title before ConfigureSyntheticScreen
                // can stage legacy geometry or guest font state. An explicit
                // SA_Title therefore has the same provider-safe boundary as
                // the other caller-owned screen payloads.
                return false;
            }

            // Optional payload tags are caller-owned pointers, not advisory
            // values. Decode them completely before ConfigureSyntheticScreen
            // stages any legacy NewScreen fields or publishes screen state.
            // A native/RTG provider must retain ownership of a request whose
            // payload cannot be read atomically.
            var dclip = GetSyntheticScreenTag(
                explicitTags,
                ScreenTagDClip,
                GetSyntheticScreenTag(extensionTags, ScreenTagDClip, 0));
            var hasDclip = TryReadSyntheticDisplayClip(
                dclip,
                out var dclipLeft,
                out var dclipTop,
                out var dclipWidth,
                out var dclipHeight);
            if (dclip != 0 && !hasDclip)
                return false;

            var pens = GetSyntheticScreenTag(
                explicitTags,
                ScreenTagPens,
                GetSyntheticScreenTag(extensionTags, ScreenTagPens, 0));
            byte pensDetail = 0;
            byte pensBlock = 1;
            if (pens != 0 && !TryReadSyntheticPens(pens, out pensDetail, out pensBlock))
                return false;

            var colors = GetSyntheticScreenTag(
                explicitTags,
                ScreenTagColors,
                GetSyntheticScreenTag(extensionTags, ScreenTagColors, 0));
            if (!TryReadSyntheticColors(colors, out var colorUpdates))
                return false;

            var colors32 = GetSyntheticScreenTag(
                explicitTags,
                ScreenTagColors32,
                GetSyntheticScreenTag(extensionTags, ScreenTagColors32, 0));
            if (!TryReadSyntheticColors32(colors32, out var color32Updates))
                return false;

            // SA_ErrorCode is an output pointer owned by the caller.  An
            // unreadable destination cannot participate in the accepted
            // synthetic session: decline before ConfigureSyntheticScreen
            // stages legacy fields or publishes a Screen, leaving the
            // native/provider implementation free to handle the request.
            var errorCode = GetSyntheticScreenTag(
                explicitTags,
                ScreenTagErrorCode,
                GetSyntheticScreenTag(extensionTags, ScreenTagErrorCode, 0));
            if (errorCode != 0 &&
                ((errorCode & 1u) != 0 ||
                 !_machine.Bus.IsMappedMemoryRange(errorCode, 4) ||
                 !_machine.Bus.IsWritableMemoryRange(errorCode, 4)))
            {
                // SA_ErrorCode is a caller-owned output envelope.  A
                // readable ROM/image overlay is not an acceptable synthetic
                // destination: decline before the Screen allocation can
                // consume an OpenScreenTagList request whose result cannot
                // be published.
                return false;
            }

            // Keep the legacy zero-pointer compatibility path, then layer
            // only the standard OCS/ECS geometry and native display-ID tags
            // over it; CyberGraphX owns RTG mode and custom-bitmap selection
            // in its own patch module.
            var hasFontOverride = HasSyntheticScreenTag(
                explicitTags,
                extensionTags,
                ScreenTagFont);
            var hasSysFontOverride = HasSyntheticScreenTag(
                explicitTags,
                extensionTags,
                ScreenTagSysFont);
            var requestedSysFont = GetSyntheticScreenTag(
                explicitTags,
                ScreenTagSysFont,
                GetSyntheticScreenTag(extensionTags, ScreenTagSysFont, uint.MaxValue));
            if (hasSysFontOverride && requestedSysFont > 1)
            {
                // The compatibility provider has only the two documented
                // system-font selectors. Leave future preference selectors to
                // native Intuition rather than silently choosing a font.
                return false;
            }
            ConfigureSyntheticScreenFromNewScreen(
                newScreen,
                allowMalformedFontOverride: hasFontOverride || hasSysFontOverride,
                allowMalformedTitleOverride: hasTitleOverride,
                allowScreenTypeOverride: hasScreenTypeOverride);
            if (_syntheticScreenConfigurationRejected)
            {
                // The legacy NewScreen fields are the base request for
                // OpenScreenTagList.  A tag list may explicitly replace the
                // rejected depth, but an unrelated tag (for example
                // SA_Width or SA_SysFont) must not make an over-depth native
                // request disappear.  Keep this rejection request-scoped and
                // publish the same SA_ErrorCode value as the explicit tag
                // path before yielding to native/provider ownership.
                if (!HasSyntheticScreenTag(explicitTags, extensionTags, ScreenTagDepth))
                {
                    TryWriteSyntheticScreenError(
                        explicitTags,
                        extensionTags,
                        OpenScreenErrorTooDeep);
                    ResetSyntheticScreenRequestState();
                    return false;
                }

                // An explicit SA_Depth below the native limit replaces the
                // legacy NewScreen value.  Clear the staged rejection before
                // applying the tag-selected geometry below.
                _syntheticScreenConfigurationRejected = false;
            }
            // SA_ErrorCode may arrive from either the explicit tag list or
            // the ExtNewScreen chain.  Keep the same precedence as the
            // other screen attributes so an accepted legacy-extended open
            // publishes its success/failure word instead of silently
            // dropping the extension-owned destination.
            if (errorCode != 0 &&
                _machine.Bus.IsMappedMemoryRange(errorCode, 4))
            {
                // Defer the success write until CompleteSyntheticScreenOpen
                // has committed bitmap, ColorMap, and optional VideoControl
                // state as one lifecycle transaction.
                _syntheticScreenErrorCodeAddress = errorCode;
            }
            var requestedModes = _syntheticScreenViewModes;
            // An explicit PAL/NTSC monitor part selects its own timing
            // family.  The zero monitor part remains the machine's jumper
            // selected profile, while a named monitor must agree with the
            // geometry used by QueryOverscan and the display database.
            bool? displayNtscOverride = null;
            if (hasNativeDisplayMode)
            {
                requestedModes = displayModes;
                displayNtscOverride = GetSyntheticMonitorNtscOverride(displayId);
            }

            var modeWidth = hasNativeDisplayMode
                ? (requestedModes & ViewModeSuperHires) != 0
                ? AmigaConstants.PalHighResWidth * 2
                : (requestedModes & ViewModeHires) != 0
                    ? AmigaConstants.PalHighResWidth
                    : AmigaConstants.PalLowResStandardWidth
                : _syntheticScreenWidth;
            var hasExplicitWidth = HasSyntheticScreenTag(explicitTags, extensionTags, ScreenTagWidth);
            var hasExplicitHeight = HasSyntheticScreenTag(explicitTags, extensionTags, ScreenTagHeight);
            var hasExplicitLeft = HasSyntheticScreenTag(explicitTags, extensionTags, ScreenTagLeft);
            var hasExplicitTop = HasSyntheticScreenTag(explicitTags, extensionTags, ScreenTagTop);
            var hasLegacyGeometry = newScreen != 0 &&
                _machine.Bus.IsMappedMemoryRange(newScreen, NewScreenHeightOffset + 2) &&
                DecodeLegacySyntheticScreenDimension(
                    _machine.Bus.ReadWord(newScreen + NewScreenWidthOffset)) > 0;
            var hasLegacyScreen = newScreen != 0;
            var fallbackLeft = _syntheticScreenLeft;
            var fallbackTop = _syntheticScreenTop;
            var overscan = GetSyntheticScreenTag(
                explicitTags,
                ScreenTagOverscan,
                GetSyntheticScreenTag(extensionTags, ScreenTagOverscan, 0));
            var hasOverscan = TryGetSyntheticOverscanGeometry(
                requestedModes,
                overscan,
                out var overscanLeft,
                out var overscanTop,
                out var overscanWidth,
                out var overscanHeight,
                displayNtscOverride);

            // STDSCREENWIDTH/STDSCREENHEIGHT are encoded as -1 (ULONG
            // $FFFFFFFF) in the public screen ABI.  They are not a zero
            // dimension: they request the active display-clip envelope.  A
            // tag-list open can carry that sentinel explicitly, so resolve a
            // profile/mode-aware clip before the generic zero/default path
            // below.  SA_DClip is authoritative, followed by SA_Overscan;
            // with neither tag, OSCAN_TEXT is the standard profile clip.
            var hasStandardClip = TryGetSyntheticOverscanGeometry(
                requestedModes,
                1,
                out var standardClipLeft,
                out var standardClipTop,
                out var standardClipWidth,
                out var standardClipHeight,
                displayNtscOverride);
            if (!hasStandardClip)
            {
                standardClipLeft = 0;
                standardClipTop = 0;
                standardClipWidth = modeWidth;
                var standardNtsc = displayNtscOverride ??
                    (_machine.Bus.Chipset.VideoStandard == VideoStandard.Ntsc);
                standardClipHeight = standardNtsc
                    ? AmigaConstants.NtscLowResStandardHeight
                    : AmigaConstants.PalLowResStandardHeight;
                if ((requestedModes & ViewModeInterlace) != 0)
                    standardClipHeight *= 2;
            }
            var displayClipLeft = standardClipLeft;
            var displayClipTop = standardClipTop;
            var displayClipWidth = standardClipWidth;
            var displayClipHeight = standardClipHeight;
            if (hasOverscan)
            {
                displayClipLeft = overscanLeft;
                displayClipTop = overscanTop;
                displayClipWidth = overscanWidth;
                displayClipHeight = overscanHeight;
            }
            if (hasDclip)
            {
                displayClipLeft = dclipLeft;
                displayClipTop = dclipTop;
                displayClipWidth = dclipWidth;
                displayClipHeight = dclipHeight;
            }
            if (!hasExplicitWidth && !hasLegacyGeometry && !hasDclip && hasOverscan)
            {
                modeWidth = overscanWidth;
                if (!hasExplicitLeft && !hasLegacyScreen)
                    fallbackLeft = overscanLeft;
                if (!hasExplicitTop && !hasLegacyScreen)
                    fallbackTop = overscanTop;
            }
            if (!hasExplicitWidth && !hasLegacyGeometry && hasDclip)
            {
                modeWidth = dclipWidth;
                if (!hasExplicitLeft && !hasLegacyScreen)
                    fallbackLeft = dclipLeft;
                if (!hasExplicitTop && !hasLegacyScreen)
                    fallbackTop = dclipTop;
            }
            var requestedTitle = GetSyntheticScreenTag(
                explicitTags,
                ScreenTagTitle,
                GetSyntheticScreenTag(extensionTags, ScreenTagTitle, _syntheticScreenDefaultTitleAddress));
            var requestedFont = GetSyntheticScreenTag(
                explicitTags,
                ScreenTagFont,
                GetSyntheticScreenTag(extensionTags, ScreenTagFont, _syntheticScreenFontAttrAddress));
            if (requestedFont != 0 &&
                !_machine.Bus.IsMappedMemoryRange(requestedFont, CopperStartGraphicsLayouts.TextAttrSize) &&
                !hasSysFontOverride)
            {
                // SA_Font is equivalent to NewScreen.Font. Preserve a
                // malformed caller-owned pointer for the native/provider
                // implementation instead of opening a default screen here;
                // SA_SysFont is the documented higher-precedence exception.
                ResetSyntheticScreenRequestState();
                return false;
            }
            if (hasSysFontOverride)
            {
                // SA_SysFont has precedence over both NewScreen.Font and
                // SA_Font. The synthetic host has no separate preferences
                // font registry, so both documented selectors use its
                // reset-scoped default TextAttr.
                requestedFont = 0;
            }
            var requestedWidth = GetSyntheticScreenTag(
                explicitTags,
                ScreenTagWidth,
                GetSyntheticScreenTag(extensionTags, ScreenTagWidth, (uint)modeWidth));
            var requestedHeight = GetSyntheticScreenTag(
                explicitTags,
                ScreenTagHeight,
                GetSyntheticScreenTag(extensionTags, ScreenTagHeight, 0));
            var standardWidthRequested = requestedWidth == uint.MaxValue;
            var standardHeightRequested = requestedHeight == uint.MaxValue;
            if (standardWidthRequested)
                requestedWidth = (uint)Math.Max(0, displayClipWidth);
            else if (!hasExplicitWidth && !hasLegacyGeometry && !hasDclip && hasOverscan)
            {
                requestedWidth = (uint)overscanWidth;
            }
            else if (!hasExplicitWidth && !hasLegacyGeometry && hasDclip)
            {
                requestedWidth = (uint)dclipWidth;
            }
            if (standardHeightRequested)
                requestedHeight = (uint)Math.Max(0, displayClipHeight);
            else if (!hasExplicitHeight && !hasLegacyGeometry && !hasDclip && hasOverscan)
            {
                requestedHeight = (uint)overscanHeight;
            }
            else if (!hasExplicitHeight && !hasLegacyGeometry && hasDclip)
            {
                requestedHeight = (uint)dclipHeight;
            }

            // With no legacy NewScreen, the native contract also derives the
            // corresponding edge from the display clip when a standard
            // dimension was requested and the caller did not supply a
            // position.  Preserve legacy positions when a NewScreen is
            // present; only the dimension sentinel changes in that case.
            if (newScreen == 0)
            {
                if (standardWidthRequested && !hasExplicitLeft)
                    fallbackLeft = displayClipLeft;
                if (standardHeightRequested && !hasExplicitTop)
                    fallbackTop = displayClipTop;
            }
            var requestedLeft = GetSyntheticSignedScreenTag(
                explicitTags,
                ScreenTagLeft,
                GetSyntheticSignedScreenTag(extensionTags, ScreenTagLeft, fallbackLeft));
            var requestedTop = GetSyntheticSignedScreenTag(
                explicitTags,
                ScreenTagTop,
                GetSyntheticSignedScreenTag(extensionTags, ScreenTagTop, fallbackTop));
            var requestedDepth = GetSyntheticScreenTag(
                explicitTags,
                ScreenTagDepth,
                GetSyntheticScreenTag(extensionTags, ScreenTagDepth, (uint)_syntheticScreenDepth));
            var requestedDepthValue = DecodeSyntheticScreenDimension(requestedDepth);
            var acceptsSuperHiresDepthCap =
                (requestedModes & ViewModeSuperHires) != 0 &&
                _machine.Bus.Chipset.SupportsEcsDisplayRegisters;
            var supportsAgaPlanar = SupportsAgaPlanarDisplay();
            var maximumNativeDepth = acceptsSuperHiresDepthCap
                ? 2
                : supportsAgaPlanar ? 8 : 6;
            if (HasSyntheticScreenTag(explicitTags, extensionTags, ScreenTagDepth) &&
                requestedDepthValue > maximumNativeDepth &&
                !acceptsSuperHiresDepthCap)
            {
                // V39+ Intuition reports an over-depth native screen request
                // instead of silently shrinking its bitmap.  The ECS
                // SuperHires two-plane cap is the one compatibility exception
                // retained by this standard display bridge.
                TryWriteSyntheticScreenError(
                    explicitTags,
                    extensionTags,
                    OpenScreenErrorTooDeep);
                ResetSyntheticScreenRequestState();
                return false;
            }
            var requestedInterleaved = GetSyntheticScreenTag(
                explicitTags,
                ScreenTagInterleaved,
                GetSyntheticScreenTag(extensionTags, ScreenTagInterleaved, 0));
            var requestedSharePens = GetSyntheticScreenTag(
                explicitTags,
                ScreenTagSharePens,
                GetSyntheticScreenTag(extensionTags, ScreenTagSharePens, uint.MaxValue));

            var requestedDetailPen = _syntheticScreenDetailPen;
            var requestedBlockPen = _syntheticScreenBlockPen;
            if (pens != 0)
            {
                requestedDetailPen = pensDetail;
                requestedBlockPen = pensBlock;
            }
            var detailPen = GetSyntheticScreenTag(
                explicitTags,
                ScreenTagDetailPen,
                GetSyntheticScreenTag(extensionTags, ScreenTagDetailPen, uint.MaxValue));
            if (detailPen <= byte.MaxValue)
                requestedDetailPen = (byte)detailPen;
            var blockPen = GetSyntheticScreenTag(
                explicitTags,
                ScreenTagBlockPen,
                GetSyntheticScreenTag(extensionTags, ScreenTagBlockPen, uint.MaxValue));
            if (blockPen <= byte.MaxValue)
                requestedBlockPen = (byte)blockPen;

            var requestedFlags = _syntheticScreenFlags;
            var explicitType = GetSyntheticScreenTag(
                explicitTags,
                ScreenTagType,
                GetSyntheticScreenTag(extensionTags, ScreenTagType, uint.MaxValue));
            if (explicitType != uint.MaxValue)
            {
                requestedFlags = (ushort)((requestedFlags & ~0x000Fu) | (explicitType & 0x000Fu));
            }

            ApplySyntheticScreenRequest(
                DecodeSyntheticScreenDimension(requestedWidth),
                DecodeSyntheticScreenDimension(requestedHeight),
                requestedDepthValue,
                requestedModes,
                requestedLeft,
                requestedTop,
                requestedFlags,
                displayNtscOverride);
            // SA_FullPalette is an Intuition preference-selection request,
            // not a provider-owned screen topology.  The compatibility host
            // does not expose a separate Preferences database, but it can
            // still honor the observable graphics contract by allocating the
            // full depth-derived ColorMap.  An explicit SA_ColorMapEntries
            // value remains authoritative when both tags are present.
            if (HasSyntheticScreenTag(explicitTags, extensionTags, ScreenTagFullPalette) &&
                _syntheticScreenColorMapEntries == 0)
            {
                _syntheticScreenColorMapEntries = Math.Max(
                    32u,
                    1u << Math.Clamp(requestedDepthValue, 1, 8));
            }
            _syntheticScreenDetailPen = requestedDetailPen;
            _syntheticScreenBlockPen = requestedBlockPen;
            // Kickstart ignores SA_Interleaved on OCS.  Keep the request in
            // the synthetic screen state only when the active Denise-class
            // capability can publish ECS/AGA interleaved layout; otherwise
            // WriteBitMap emits ordinary separate-plane storage and clears
            // BMF_INTERLEAVED for native-style callers.
            _syntheticScreenInterleaved =
                requestedInterleaved != 0 &&
                _machine.Bus.Chipset.SupportsEcsDisplayRegisters;
            _syntheticScreenDefaultTitleAddress = requestedTitle;
            _syntheticScreenFontAttrAddress = requestedFont;
            // SA_SysFont=1 selects the screen-preference font for the
            // embedded Screen.RastPort but leaves Window.RPort on
            // GfxBase.DefaultFont.  The current compatibility preference is
            // the same reset-scoped Topaz font; retain the ownership bit so a
            // native preference provider can later supply a distinct screen
            // font without changing the public Screen/Window layout.
            _syntheticUiDisplay.WindowUsesDefaultFont =
                hasSysFontOverride && requestedSysFont == 1;

            if (requestedSharePens != uint.MaxValue)
                SetSyntheticScreenFlag(SyntheticScreenPenShared, requestedSharePens != 0);

            var showTitle = GetSyntheticScreenTag(
                explicitTags,
                ScreenTagShowTitle,
                GetSyntheticScreenTag(extensionTags, ScreenTagShowTitle, uint.MaxValue));
            if (showTitle != uint.MaxValue)
                SetSyntheticScreenFlag(SyntheticScreenShowTitle, showTitle != 0);
            var behind = GetSyntheticScreenTag(
                explicitTags,
                ScreenTagBehind,
                GetSyntheticScreenTag(extensionTags, ScreenTagBehind, uint.MaxValue));
            if (behind != uint.MaxValue)
                SetSyntheticScreenFlag(SyntheticScreenBehind, behind != 0);
            var quiet = GetSyntheticScreenTag(
                explicitTags,
                ScreenTagQuiet,
                GetSyntheticScreenTag(extensionTags, ScreenTagQuiet, uint.MaxValue));
            if (quiet != uint.MaxValue)
                SetSyntheticScreenFlag(SyntheticScreenQuiet, quiet != 0);
            var autoScroll = GetSyntheticScreenTag(
                explicitTags,
                ScreenTagAutoScroll,
                GetSyntheticScreenTag(extensionTags, ScreenTagAutoScroll, uint.MaxValue));
            if (autoScroll != uint.MaxValue)
                SetSyntheticScreenFlag(SyntheticScreenAutoScroll, autoScroll != 0);

            ApplySyntheticScreenColorUpdates(colorUpdates);
            ApplySyntheticScreenColorUpdates(color32Updates);

            return true;
        }

        private void TryWriteSyntheticScreenError(
            Dictionary<uint, uint> explicitTags,
            Dictionary<uint, uint>? extensionTags,
            uint errorCode)
        {
            var address = GetSyntheticScreenTag(
                explicitTags,
                ScreenTagErrorCode,
                GetSyntheticScreenTag(extensionTags, ScreenTagErrorCode, 0));
            if (address != 0 &&
                _machine.Bus.IsMappedMemoryRange(address, 4) &&
                _machine.Bus.IsWritableMemoryRange(address, 4))
                _machine.Bus.WriteLong(address, errorCode);
        }

        private bool CompleteSyntheticScreenOpen(uint screen)
        {
            var errorCodeAddress = _syntheticScreenErrorCodeAddress;
            var errorCode = _syntheticScreenOpenErrorCode;
            var videoControlTags = _syntheticScreenVideoControlTags;
            _syntheticScreenFinalErrorCodeAddress = 0;
            _syntheticScreenFinalErrorCode = 0;
            _syntheticScreenErrorCodeAddress = 0;
            _syntheticScreenOpenErrorCode = 0;
            _syntheticScreenVideoControlTags = 0;
            _syntheticScreenColorMapEntries = 0;

            if (screen == 0)
            {
                if (errorCodeAddress != 0 &&
                    _machine.Bus.IsMappedMemoryRange(errorCodeAddress, 4) &&
                    _machine.Bus.IsWritableMemoryRange(errorCodeAddress, 4))
                {
                    _machine.Bus.WriteLong(
                        errorCodeAddress,
                        errorCode != 0 ? errorCode : OpenScreenErrorNoMemory);
                }

                return false;
            }

            if (videoControlTags != 0)
            {
                var colorMap = _syntheticColorMapAddress;
                var control = new M68kCpuState
                {
                    A = { [0] = colorMap, [1] = videoControlTags }
                };
                _graphicsServices.VideoControl(control);
                if (control.D[0] != 0)
                {
                    // A semantically unsupported or malformed VideoControl
                    // list must not leak a partially published Screen.  Keep
                    // SA_ErrorCode untouched so the native/provider owner can
                    // still claim the original request.
                    _syntheticScreenErrorCodeAddress = 0;
                    RollbackSyntheticScreenConstruction();
                    return false;
                }
            }

            // Keep a request-scoped copy until Intuition has completed the
            // display reconstruction. Success is deliberately not written
            // yet: a later RethinkDisplay failure must publish the staged
            // allocation error without exposing a transient zero result.
            _syntheticScreenFinalErrorCodeAddress = errorCodeAddress;
            _syntheticScreenFinalErrorCode = errorCode;

            return true;
        }

        private void FinalizeSyntheticScreenOpen(bool succeeded)
        {
            if (succeeded)
                CommitSyntheticScreenPaletteTransaction();
            else
                RollbackSyntheticScreenPaletteTransaction();

            var errorCodeAddress = _syntheticScreenFinalErrorCodeAddress;
            var errorCode = _syntheticScreenFinalErrorCode;
            _syntheticScreenFinalErrorCodeAddress = 0;
            _syntheticScreenFinalErrorCode = 0;

            if (errorCodeAddress == 0 ||
                !_machine.Bus.IsMappedMemoryRange(errorCodeAddress, 4) ||
                !_machine.Bus.IsWritableMemoryRange(errorCodeAddress, 4))
            {
                return;
            }

            _machine.Bus.WriteLong(
                errorCodeAddress,
                succeeded
                    ? 0u
                    : errorCode != 0 ? errorCode : OpenScreenErrorNoMemory);
        }

        private bool TryReadSyntheticScreenTags(
            uint address,
            out Dictionary<uint, uint> result)
        {
            result = new Dictionary<uint, uint>();
            if (address == 0)
                return true;

            var visited = new HashSet<uint>();
            for (var count = 0; count < 512; count++)
            {
                if ((address & 1u) != 0 ||
                    !visited.Add(address) ||
                    // A TagItem is exactly eight bytes.  $FFFF_FFF8 is the
                    // last complete guest envelope and ends at
                    // $FFFF_FFFF; reject only starts that require a byte
                    // beyond the 32-bit address space.
                    address > uint.MaxValue - 7u ||
                    !_machine.Bus.IsMappedMemoryRange(address, 8))
                {
                    return false;
                }

                var tag = _machine.Bus.ReadLong(address);
                var value = _machine.Bus.ReadLong(address + 4);
                var nextAddress = address + 8u;
                switch (tag)
                {
                    case SyntheticTagDone:
                        return true;
                    case SyntheticTagIgnore:
                        if (nextAddress < address)
                            return false;
                        address = nextAddress;
                        continue;
                    case SyntheticTagMore:
                        if (value == 0)
                            return true;
                        if ((value & 1u) != 0)
                            return false;
                        address = value;
                        continue;
                    case SyntheticTagSkip:
                        var skipBytes = (ulong)value * 8ul;
                        if (skipBytes > uint.MaxValue ||
                            nextAddress < address ||
                            nextAddress > uint.MaxValue - (uint)skipBytes)
                        {
                            return false;
                        }

                        address = nextAddress + (uint)skipBytes;
                        continue;
                    default:
                        if (nextAddress < address)
                            return false;

                        result[tag] = value;
                        address = nextAddress;
                        break;
                }
            }

            return false;
        }

        private static uint GetSyntheticScreenTag(
            Dictionary<uint, uint>? tags,
            uint tag,
            uint fallback)
            => tags != null && tags.TryGetValue(tag, out var value)
                ? value
                : fallback;

        private static void NormalizeSyntheticScreenTagAliases(
            Dictionary<uint, uint> tags)
        {
            // Keep the public parser aligned with intuition/screens.h while
            // accepting the pre-existing host-shim spellings.  The 0x44-0x47
            // aliases overlap real V39/V40 attributes, so only the impossible
            // pointer-shaped values 0/1 are treated as legacy booleans.
            if (tags.TryGetValue(LegacyScreenTagPens, out var legacyPens))
            {
                if (!tags.ContainsKey(ScreenTagPens))
                    tags[ScreenTagPens] = legacyPens;
                tags.Remove(LegacyScreenTagPens);
            }

            NormalizeSyntheticBooleanAlias(
                tags,
                LegacyScreenTagShowTitle,
                ScreenTagShowTitle);
            NormalizeSyntheticBooleanAlias(
                tags,
                LegacyScreenTagBehind,
                ScreenTagBehind);
            NormalizeSyntheticBooleanAlias(
                tags,
                LegacyScreenTagQuiet,
                ScreenTagQuiet);
            NormalizeSyntheticBooleanAlias(
                tags,
                LegacyScreenTagAutoScroll,
                ScreenTagAutoScroll);
        }

        private static void NormalizeSyntheticBooleanAlias(
            Dictionary<uint, uint> tags,
            uint legacyTag,
            uint canonicalTag)
        {
            // The old host shim used 0x44..0x47 for boolean screen tags,
            // while Kickstart assigns those IDs to pointer-valued V39/V40
            // attributes. A null pointer is a valid way to disable such an
            // attribute and must remain provider-owned; only the legacy TRUE
            // spelling is unambiguous enough to normalize.
            if (!tags.TryGetValue(legacyTag, out var value) || value == 0 || value > 1)
                return;

            if (!tags.ContainsKey(canonicalTag))
                tags[canonicalTag] = value;
            tags.Remove(legacyTag);
        }

        private static bool HasUnsupportedSyntheticScreenTag(
            Dictionary<uint, uint>? explicitTags,
            Dictionary<uint, uint>? extensionTags)
        {
            // These attributes carry palette allocation, attachment, layer,
            // preferences, or VideoControl ownership that is not represented
            // by the reset-scoped synthetic screen session.
            uint[] unsupported =
            {
                ScreenTagObsolete1,
                ScreenTagParent,
                ScreenTagDraggable,
                ScreenTagExclusive,
                ScreenTagBackFill,
                ScreenTagFrontChild,
                ScreenTagBackChild,
                ScreenTagLikeWorkbench,
                ScreenTagReserved,
                ScreenTagMinimizeIsg
            };

            foreach (var tag in unsupported)
            {
                if (HasSyntheticScreenTag(explicitTags, extensionTags, tag))
                    return true;
            }

            return false;
        }

        private static int GetSyntheticSignedScreenTag(
            Dictionary<uint, uint>? tags,
            uint tag,
            int fallback)
            => tags != null && tags.TryGetValue(tag, out var value)
                ? unchecked((int)value)
                : fallback;

        private static bool HasSyntheticScreenTag(
            Dictionary<uint, uint>? explicitTags,
            Dictionary<uint, uint>? extensionTags,
            uint tag)
            => (explicitTags?.ContainsKey(tag) ?? false) ||
               (extensionTags?.ContainsKey(tag) ?? false);

        private bool TryReadSyntheticDisplayClip(
            uint address,
            out int left,
            out int top,
            out int width,
            out int height)
        {
            left = top = width = height = 0;
            if (address == 0 ||
                (address & 1u) != 0 ||
                !_machine.Bus.IsMappedMemoryRange(address, 8))
                return false;

            var minX = unchecked((short)_machine.Bus.ReadWord(address));
            var minY = unchecked((short)_machine.Bus.ReadWord(address + 2));
            var maxX = unchecked((short)_machine.Bus.ReadWord(address + 4));
            var maxY = unchecked((short)_machine.Bus.ReadWord(address + 6));
            if (maxX < minX || maxY < minY)
                return false;

            left = minX;
            top = minY;
            width = maxX - minX + 1;
            height = maxY - minY + 1;
            return width >= 16 && height >= 16;
        }

        private bool TryValidateSyntheticCustomBitMap(uint bitMap)
        {
            if (bitMap == 0 ||
                (bitMap & 1u) != 0 ||
                !TryReadWordField(bitMap, BitMapBytesPerRowOffset, out var bytesPerRow) ||
                !TryReadWordField(bitMap, BitMapRowsOffset, out var rows) ||
                !TryReadByteField(bitMap, BitMapFlagsOffset, out var flags) ||
                !TryReadByteField(bitMap, BitMapDepthOffset, out var depth) ||
                bytesPerRow == 0 ||
                (bytesPerRow & 1) != 0 ||
                rows == 0 ||
                depth == 0 ||
                depth > 8 ||
                (flags & BitMapFlagStandard) == 0 ||
                !GraphicsRasterOperations.TryGetPlaneBytesPerRow(
                    bytesPerRow,
                    depth,
                    flags,
                    out var planeBytesPerRow))
            {
                return false;
            }

            // BMF_MINPLANES is a public allocation form: the caller may
            // provide only the header prefix and the declared plane-pointer
            // tail instead of the complete eight-plane BitMap envelope.  Do
            // not require an unmapped unused tail merely because the Screen
            // copy below uses the full embedded compatibility header.
            if (!TryGetSyntheticBitMapHeaderSpan(bitMap, depth, flags, out _))
                return false;

            var bitmap = new GraphicsRasterOperations.BitmapInfo(
                bitMap,
                bytesPerRow,
                planeBytesPerRow,
                rows,
                depth,
                flags);
            var planeSpan = GraphicsRasterOperations.GetBitmapPlaneTouchedSpan(bitmap);
            if (planeSpan == 0 || planeSpan > int.MaxValue)
                return false;

            uint firstPlane = 0;
            for (var plane = 0; plane < depth; plane++)
            {
                var planeOffset = BitMapPlanesOffset + (plane * sizeof(uint));
                if (!TryReadLongField(bitMap, planeOffset, out var planeAddress) ||
                    planeAddress == 0 ||
                    (planeAddress & 1u) != 0 ||
                    !_machine.Bus.IsMappedMemoryRange(planeAddress, checked((int)planeSpan)))
                {
                    return false;
                }

                if (plane == 0)
                {
                    firstPlane = planeAddress;
                }
                else if ((flags & GraphicsRasterOperations.BmfInterleaved) != 0)
                {
                    var expected = (ulong)firstPlane +
                        ((ulong)plane * planeBytesPerRow);
                    if (expected > uint.MaxValue || planeAddress != (uint)expected)
                        return false;
                }
            }

            return true;
        }

        private bool TryGetSyntheticBitMapHeaderSpan(
            uint bitMap,
            byte depth,
            byte flags,
            out int span)
        {
            span = 0;
            if (bitMap == 0 ||
                (bitMap & 1u) != 0 ||
                depth == 0 ||
                depth > 8)
            {
                return false;
            }

            var compactSpan = BitMapPlanesOffset + (depth * sizeof(uint));
            if (compactSpan <= 0 ||
                !_machine.Bus.IsMappedMemoryRange(bitMap, compactSpan))
            {
                return false;
            }

            // Prefer the compact declared-depth envelope for BMF_MINPLANES,
            // but accept a naturally full header for callers that retain one.
            // For ordinary headers, the full envelope remains authoritative;
            // a compact fallback keeps InitBitMap-compatible guest objects
            // usable when their unused plane tail is intentionally unmapped.
            if ((flags & BitMapFlagMinPlanes) != 0)
            {
                span = compactSpan;
                return true;
            }

            if (_machine.Bus.IsMappedMemoryRange(
                    bitMap,
                    CopperStartGraphicsLayouts.BitMapSize))
            {
                span = CopperStartGraphicsLayouts.BitMapSize;
                return true;
            }

            span = compactSpan;
            return true;
        }

        private bool CopySyntheticBitMapHeader(uint source, uint destination)
        {
            const int bitMapSpan = CopperStartGraphicsLayouts.BitMapSize;
            if (source == 0 ||
                destination == 0 ||
                !_machine.Bus.IsMappedMemoryRange(destination, bitMapSpan))
            {
                return false;
            }

            if (!TryReadByteField(source, BitMapFlagsOffset, out var flags) ||
                !TryReadByteField(source, BitMapDepthOffset, out var depth) ||
                !TryGetSyntheticBitMapHeaderSpan(source, depth, flags, out var sourceSpan))
            {
                return false;
            }

            // Clear the unused plane-pointer tail in the embedded copy before
            // copying a compact BMF_MINPLANES header. This keeps teardown
            // comparisons deterministic without reading caller-owned bytes
            // outside the declared structure.
            for (var offset = 0; offset < bitMapSpan; offset++)
            {
                _machine.Bus.WriteByte(
                    destination + (uint)offset,
                    0,
                    0);

            }

            for (var offset = 0; offset < sourceSpan; offset++)
            {
                _machine.Bus.WriteByte(
                    destination + (uint)offset,
                    _machine.Bus.ReadByte(source + (uint)offset),
                    0);
            }

            return true;
        }

        private bool TryValidateSyntheticTitle(uint address)
        {
            if (address == 0 || address == NoTitleChange)
                return address == 0;

            // The synthetic title renderer consumes at most this bounded
            // prefix. Validate the same envelope before screen state is
            // staged, while allowing a longer caller string to be truncated
            // by the host presentation path just as the native UI does.
            for (var offset = 0; offset < SyntheticScreenTitleMaxLength; offset++)
            {
                var delta = (uint)offset;
                if (address > uint.MaxValue - delta)
                    return false;

                var current = address + delta;
                if (!_machine.Bus.IsMappedMemoryRange(current, 1))
                    return false;

                if (_machine.Bus.ReadByte(current) == 0)
                    return true;
            }

            return true;
        }

        private bool TryReadSyntheticPens(uint address, out byte detailPen, out byte blockPen)
        {
            detailPen = 0;
            blockPen = 1;
            if (address == 0 || (address & 1u) != 0)
                return false;

            for (var index = 0; index < 9; index++)
            {
                if (!_machine.Bus.IsMappedMemoryRange(address, 2))
                    return false;

                var pen = _machine.Bus.ReadWord(address);
                if (pen == ushort.MaxValue)
                    return true;

                var value = (byte)Math.Min(pen, byte.MaxValue);
                if (index == 0)
                    detailPen = value;
                else if (index == 1)
                    blockPen = value;

                if (address > uint.MaxValue - 2)
                    return false;
                address += 2;
            }

            return false;
        }

        private bool TryGetSyntheticOverscanGeometry(
            ushort viewModes,
            uint overscan,
            out int left,
            out int top,
            out int width,
            out int height,
            bool? ntscOverride = null)
        {
            left = top = width = height = 0;
            if (overscan is < 1 or > 4)
                return false;

            var ntsc = ntscOverride ?? (_machine.Bus.Chipset.VideoStandard == VideoStandard.Ntsc);
            var horizontalScale = (viewModes & ViewModeSuperHires) != 0
                ? 4
                : (viewModes & ViewModeHires) != 0
                    ? 2
                    : 1;
            var standardWidth = AmigaConstants.PalLowResStandardWidth * horizontalScale;
            var standardHeight = ntsc
                ? AmigaConstants.NtscLowResStandardHeight
                : AmigaConstants.PalLowResStandardHeight;
            var overscanWidth = (ntsc
                ? AmigaConstants.NtscLowResWidth
                : AmigaConstants.PalLowResWidth) * horizontalScale;
            var overscanHeight = ntsc
                ? AmigaConstants.NtscLowResHeight
                : AmigaConstants.PalLowResHeight;
            if ((viewModes & ViewModeInterlace) != 0)
            {
                standardHeight *= 2;
                overscanHeight *= 2;
            }

            // OSCAN_TEXT is the profile's text-safe standard raster.  The
            // remaining standard overscan classes use the profile's existing
            // full raster envelope; display-clip preferences are not a
            // CyberGraphX concern and are intentionally not synthesized here.
            if (overscan == 1)
            {
                width = standardWidth;
                height = standardHeight;
            }
            else
            {
                width = overscanWidth;
                height = overscanHeight;
            }

            return width >= 16 && height >= 16;
        }

        private static int DecodeSyntheticScreenDimension(uint value)
            => value == uint.MaxValue
                ? 0
                : value > int.MaxValue
                    ? int.MaxValue
                    : (int)value;

        private static int DecodeLegacySyntheticScreenDimension(ushort value)
            => value == ushort.MaxValue ? -1 : value;

        private void SetSyntheticScreenFlag(ushort flag, bool enabled)
        {
            _syntheticScreenFlags = enabled
                ? (ushort)(_syntheticScreenFlags | flag)
                : (ushort)(_syntheticScreenFlags & ~flag);
        }

        private bool SupportsAgaPlanarDisplay()
            => _machine.Bus.Chipset.HasAllCapabilities(
                AmigaChipCapabilities.AgaAliceRegisters |
                AmigaChipCapabilities.AgaLisaRegisters);

        private int GetCompatibilityCopperListSize()
            => SupportsAgaPlanarDisplay()
                ? AgaCompatibilityCopperListSize
                : CompatibilityCopperListSize;

        private int GetSyntheticPaletteCapacity()
        {
            // OCS/ECS compatibility screens retain their historical 32-entry
            // colour envelope. AGA exposes the depth-derived palette above
            // that baseline, through COLOR00..COLOR31 across BPLCON3 banks.
            if (!SupportsAgaPlanarDisplay())
                return 32;

            return Math.Max(32, 1 << Math.Clamp(_syntheticScreenDepth, 1, 8));
        }

        private bool TryReadSyntheticColors(
            uint colors,
            out List<(int Index, ushort Color, uint Rgb32)> updates)
        {
            updates = new List<(int Index, ushort Color, uint Rgb32)>();
            if (colors == 0)
                return true;
            if ((colors & 1u) != 0)
                return false;

            var visited = new HashSet<uint>();
            var terminated = false;
            for (var index = 0; index < SyntheticPaletteMaximumRecords; index++)
            {
                if (!visited.Add(colors) ||
                    !_machine.Bus.IsMappedMemoryRange(colors, 8))
                    return false;

                var colorIndex = _machine.Bus.ReadWord(colors);
                if (colorIndex == ushort.MaxValue)
                {
                    terminated = true;
                    break;
                }

                if (colorIndex < SyntheticPaletteMaximumEntries)
                {
                    var red = (ushort)(_machine.Bus.ReadWord(colors + 2) & 0x000F);
                    var green = (ushort)(_machine.Bus.ReadWord(colors + 4) & 0x000F);
                    var blue = (ushort)(_machine.Bus.ReadWord(colors + 6) & 0x000F);
                    updates.Add((
                        colorIndex,
                        (ushort)((red << 8) | (green << 4) | blue),
                        0xFF00_0000u |
                        ((uint)(red * 17) << 16) |
                        ((uint)(green * 17) << 8) |
                        (uint)(blue * 17)));
                }

                if (colors > uint.MaxValue - 8)
                    return false;
                colors += 8;
            }

            return terminated;
        }

        private bool TryReadSyntheticColors32(
            uint table,
            out List<(int Index, ushort Color, uint Rgb32)> updates)
        {
            updates = new List<(int Index, ushort Color, uint Rgb32)>();
            if (table == 0)
                return true;
            if ((table & 1u) != 0)
                return false;

            var cursor = table;
            var terminated = false;
            for (var records = 0; records < SyntheticPaletteMaximumRecords; records++)
            {
                if (!_machine.Bus.IsMappedMemoryRange(cursor, 4))
                    return false;

                var descriptor = _machine.Bus.ReadLong(cursor);
                var count = descriptor >> 16;
                var first = descriptor & 0xFFFF;
                if (count == 0)
                {
                    terminated = true;
                    break;
                }
                if (first >= SyntheticPaletteMaximumEntries ||
                    count > (uint)SyntheticPaletteMaximumEntries - first)
                    return false;

                if (cursor > uint.MaxValue - 4)
                    return false;
                cursor += 4;
                for (uint index = 0; index < count; index++)
                {
                    if (!_machine.Bus.IsMappedMemoryRange(cursor, 12))
                        return false;

                    var red = _machine.Bus.ReadLong(cursor);
                    var green = _machine.Bus.ReadLong(cursor + 4);
                    var blue = _machine.Bus.ReadLong(cursor + 8);
                    updates.Add(((int)(first + index),
                        (ushort)(
                            ((red >> 20) & 0x0F00) |
                            ((green >> 24) & 0x00F0) |
                            ((blue >> 28) & 0x000F)),
                        0xFF00_0000u |
                        ((red >> 8) & 0x00FF_0000u) |
                        ((green >> 16) & 0x0000_FF00u) |
                        ((blue >> 24) & 0x0000_00FFu)));

                    if (cursor > uint.MaxValue - 12)
                        return false;
                    cursor += 12;
                }
            }

            return terminated;
        }

        private void ApplySyntheticScreenColorUpdates(
            List<(int Index, ushort Color, uint Rgb32)> updates)
        {
            var paletteCapacity = GetSyntheticPaletteCapacity();
            var hasApplicableUpdate = false;
            foreach (var update in updates)
            {
                if (update.Index < paletteCapacity)
                {
                    hasApplicableUpdate = true;
                    break;
                }
            }

            if (hasApplicableUpdate && _syntheticPaletteTransactionSnapshot is null)
            {
                _syntheticPaletteTransactionSnapshot = (ushort[])_syntheticPalette.Clone();
                _syntheticPaletteRgb32TransactionSnapshot = (uint[])_syntheticPaletteRgb32.Clone();
                _syntheticPaletteTransactionLoaded = _syntheticPaletteLoaded;
                _syntheticPaletteRgb32TransactionLoaded = _syntheticPaletteRgb32Loaded;
            }

            foreach (var update in updates)
            {
                if (update.Index < paletteCapacity)
                {
                    _syntheticPalette[update.Index] = update.Color;
                    _syntheticPaletteRgb32[update.Index] = update.Rgb32;
                }
            }

            if (hasApplicableUpdate)
                _syntheticPaletteLoaded = true;
            if (hasApplicableUpdate)
                _syntheticPaletteRgb32Loaded = true;
        }

        private void CommitSyntheticScreenPaletteTransaction()
        {
            _syntheticPaletteTransactionSnapshot = null;
            _syntheticPaletteRgb32TransactionSnapshot = null;
        }

        private void RollbackSyntheticScreenPaletteTransaction()
        {
            var snapshot = _syntheticPaletteTransactionSnapshot;
            if (snapshot is null)
                return;

            Array.Copy(snapshot, _syntheticPalette, snapshot.Length);
            var rgb32Snapshot = _syntheticPaletteRgb32TransactionSnapshot;
            if (rgb32Snapshot is not null)
                Array.Copy(rgb32Snapshot, _syntheticPaletteRgb32, rgb32Snapshot.Length);
            _syntheticPaletteLoaded = _syntheticPaletteTransactionLoaded;
            _syntheticPaletteRgb32Loaded = _syntheticPaletteRgb32TransactionLoaded;
            _syntheticPaletteTransactionSnapshot = null;
            _syntheticPaletteRgb32TransactionSnapshot = null;
        }

        private void ApplySyntheticScreenRequest(
            int requestedWidth,
            int requestedHeight,
            int requestedDepth,
            ushort requestedModes,
            int requestedLeft = 0,
            int requestedTop = 0,
            ushort requestedFlags = 0,
            bool? ntscOverride = null)
        {
            _syntheticScreenLeft = Math.Clamp(requestedLeft, short.MinValue, short.MaxValue);
            _syntheticScreenTop = Math.Clamp(requestedTop, short.MinValue, short.MaxValue);
            // NS_EXTENDED and CUSTOMBITMAP are NewScreen request bits, not
            // published Screen flags.  The standard path does not claim a
            // caller-owned bitmap; CyberGraphX owns that provider boundary.
            var screenType = (ushort)(requestedFlags & 0x000F);
            if (screenType == 0)
                screenType = CustomScreenType;
            _syntheticScreenFlags = (ushort)(
                (requestedFlags & SyntheticScreenSupportedFlags & ~0x000F) |
                screenType);

            var ntsc = ntscOverride ??
                (_machine.Bus.Chipset.VideoStandard == VideoStandard.Ntsc);
            var standardWidth = (requestedModes & ViewModeSuperHires) != 0
                ? AmigaConstants.PalLowResStandardWidth * 4
                : (requestedModes & ViewModeHires) != 0
                    ? AmigaConstants.PalLowResStandardWidth * 2
                    : AmigaConstants.PalLowResStandardWidth;
            if (requestedWidth == -1)
            {
                // Legacy NewScreen encodes STDSCREENWIDTH as a signed UWORD
                // -1. Resolve it from the active mode's OSCAN_TEXT geometry,
                // rather than clamping the sentinel to the hardware maximum.
                _syntheticScreenWidth = Math.Clamp(
                    standardWidth,
                    64,
                    (requestedModes & ViewModeSuperHires) != 0
                        ? AmigaConstants.PalHighResWidth * 2
                        : AmigaConstants.PalHighResWidth);
            }
            else if (requestedWidth >= 64)
            {
                var maximumWidth = (requestedModes & ViewModeSuperHires) != 0
                    ? AmigaConstants.PalHighResWidth * 2
                    : AmigaConstants.PalHighResWidth;
                _syntheticScreenWidth = Math.Clamp(
                    (int)requestedWidth,
                    64,
                    maximumWidth);
            }

            var defaultHeight = ntsc
                ? AmigaConstants.NtscLowResStandardHeight
                : AmigaConstants.PalLowResStandardHeight;
            var maximumHeight = ntsc
                ? AmigaConstants.NtscLowResHeight
                : AmigaConstants.PalLowResHeight;
            if ((requestedModes & ViewModeInterlace) != 0)
            {
                defaultHeight *= 2;
                maximumHeight *= 2;
            }

            _syntheticScreenHeight = Math.Clamp(
                requestedHeight == -1 || requestedHeight < 16
                    ? defaultHeight
                    : requestedHeight,
                16,
                maximumHeight);

            // HAM and Extra-Halfbrite both require all six OCS/ECS
            // bitplanes.  Keep the synthetic screen envelope large enough
            // for the graphics.library copper projection instead of
            // silently truncating a valid NewScreen request to five planes.
            var minimumDepth = 1;
            if ((requestedModes & (ViewModeHam | ViewModeExtraHalfBrite)) != 0)
            {
                minimumDepth = 6;
            }
            else if ((requestedModes & ViewModeDualPlayfield) != 0)
            {
                minimumDepth = 2;
            }
            var requestedOrDefaultDepth = requestedDepth != 0
                ? (int)requestedDepth
                : _syntheticScreenDepth;
            // ECS SuperHires is a native two-plane/four-colour display mode.
            // Keep an explicit SA_Depth from manufacturing an impossible
            // four- or six-plane SuperHires bitmap; the native OpenScreen
            // path rejects that envelope rather than publishing a display
            // list that Denise cannot fetch.  Other OCS/ECS modes retain the
            // six-plane maximum (with HAM/EHB already enforcing their
            // six-plane minimum above).
            var maximumDepth = (requestedModes & ViewModeSuperHires) != 0 &&
                _machine.Bus.Chipset.SupportsEcsDisplayRegisters
                ? 2
                : SupportsAgaPlanarDisplay()
                    ? 8
                    : 6;
            _syntheticScreenDepth = Math.Clamp(
                Math.Max(requestedOrDefaultDepth, minimumDepth),
                1,
                maximumDepth);

            // Preserve the OCS/ECS mode bits that the synthetic standard-
            // planar screen can represent.  Dual-playfield is backed by a
            // second RasInfo node pointing at the same bitmap; graphics
            // construction still sees the explicit two-node contract.
            _syntheticScreenViewModes = (ushort)(requestedModes &
                (ViewModeHires |
                 ViewModeInterlace |
                 ViewModeSuperHires |
                 ViewModeHam |
                 ViewModeExtraHalfBrite |
                 ViewModeDualPlayfield |
                 ViewModePlayfieldBitAssignment));
            if (_syntheticScreenWidth > AmigaConstants.PalLowResWidth ||
                (requestedModes & ViewModeSuperHires) != 0)
            {
                _syntheticScreenViewModes |= ViewModeHires;
            }

            if ((_syntheticScreenViewModes & ViewModeHires) != 0)
            {
                _syntheticScreenFlags |= SyntheticScreenHires;
            }
        }

        private static bool TryDecodeNativeScreenMode(uint displayId, out ushort viewModes)
        {
            viewModes = 0;
            if (displayId == CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsModeIds.Invalid)
                return false;

            // Native OCS/ECS ModeIDs are the monitor ID ORed with the mode
            // key.  The monitor IDs used by the portable database carry the
            // 0x1000 base bit; strip it before validating the public View.Modes
            // feature key, while leaving all actual display features intact.
            // A zero monitor part is the jumper-selected default monitor and
            // therefore resolves against the active PAL/NTSC machine profile.
            var monitor = displayId & 0xFFFF_1000u;
            if (monitor != CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsModeIds.DefaultMonitor &&
                monitor != CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsModeIds.PalMonitor &&
                monitor != CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsModeIds.NtscMonitor)
            {
                return false;
            }

            var modeKey = (ushort)(displayId & 0x0000_EFFFu);
            if (!CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsModeIds.IsNativeOcsEcsKey(modeKey))
            {
                return false;
            }

            viewModes = modeKey;
            return true;
        }

        private static bool? GetSyntheticMonitorNtscOverride(uint displayId)
        {
            var monitor = displayId & 0xFFFF_1000u;
            return monitor switch
            {
                CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsModeIds.NtscMonitor => true,
                CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsModeIds.PalMonitor => false,
                // The zero monitor part is the machine's jumper-selected
                // profile.  Returning null keeps the geometry helper's
                // active-profile fallback in force for both OpenScreen and
                // QueryOverscan.
                CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsModeIds.DefaultMonitor => null,
                _ => null
            };
        }

        private void CloseSyntheticScreen(M68kCpuState state)
        {
            var screen = state.A[0];
            if (screen == 0 || screen != _syntheticScreenAddress)
            {
                // The synthetic Intuition owner only claims the screen it
                // created.  A foreign or malformed pointer remains available
                // to native Intuition or another provider.
                state.D[0] = 0;
                return;
            }

            // Kickstart refuses to close a Screen while one of its Windows is
            // still open.  The synthetic session keeps a private Window-shaped
            // backing object for Screen.FirstWindow/RastPort presentation, so
            // only an explicit OpenWindow claim participates in this guard.
            if (_syntheticUiDisplay.WindowOpen)
            {
                state.D[0] = 0;
                return;
            }

            // Screen.FirstWindow is the public ownership chain, not merely a
            // presentation hint.  The synthetic bridge may expose its own
            // reset-scoped Window-shaped backing object there, but any other
            // non-NULL link belongs to a guest/native Window and must keep
            // CloseScreen from tearing down the Screen underneath it.
            if (!TryReadLongField(screen, ScreenFirstWindowOffset, out var firstWindow) ||
                (firstWindow != 0 && firstWindow != _syntheticWindowAddress))
            {
                state.D[0] = 0;
                return;
            }

            // The private backing may itself be the head of the public list.
            // A non-NULL Window.Next means another guest/native Window is
            // still attached and must keep the Screen alive.  Treat an
            // unreadable link as a malformed ownership chain and decline
            // rather than tearing down a display whose tail we cannot inspect.
            if (firstWindow == _syntheticWindowAddress &&
                (!TryReadLongField(firstWindow, WindowNextOffset, out var nextWindow) ||
                 nextWindow != 0))
            {
                state.D[0] = 0;
                return;
            }

            // The compatibility backing is only an owned Window for this
            // Screen while its WScreen link still points back to the same
            // guest Screen.  If a caller/provider has repointed that field,
            // preserve the display and leave the altered Window available to
            // its owner instead of tearing down the Screen underneath it.
            if (firstWindow == _syntheticWindowAddress &&
                (!TryReadLongField(firstWindow, WindowWScreenOffset, out var windowScreen) ||
                 windowScreen != screen))
            {
                state.D[0] = 0;
                return;
            }

            var viewPort = screen + ScreenViewPortOffset;
            if (!TryReadLongField(viewPort, ViewPortNextOffset, out var nextViewPort) ||
                nextViewPort != 0)
            {
                // ViewPort.Next is the public display-chain ownership link.
                // A provider/native tail must be detached before the
                // compatibility screen can clear its embedded ViewPort.
                state.D[0] = 0;
                return;
            }

            if (!TryReadLongField(
                    viewPort,
                    CopperStartGraphicsLayouts.ViewPortColorMap,
                    out var colorMap) ||
                colorMap != _syntheticColorMapAddress)
            {
                // ColorMap is the viewport's palette-ownership link.  A
                // provider may replace it while reusing the screen envelope,
                // but that replacement must be detached by its owner before
                // compatibility teardown can release the original map or
                // clear the viewport.  Treat both an unreadable link and a
                // foreign mapped map as an unclaimed CloseScreen request.
                state.D[0] = 0;
                return;
            }

            if (!TryReadLongField(viewPort, ViewPortRasInfoOffset, out var rasInfo) ||
                rasInfo != _syntheticRasInfoAddress)
            {
                // RasInfo is the public source-storage link for this
                // compatibility ViewPort.  A replaced or unreadable head
                // belongs to the provider that installed it; do not retire
                // the Screen while its display source is foreign.
                state.D[0] = 0;
                return;
            }

            if (!TryReadLongField(rasInfo, RasInfoNextOffset, out var nextRasInfo) ||
                nextRasInfo != _syntheticSecondRasInfoAddress)
            {
                // A second synthetic playfield is the only permitted tail of
                // the compatibility RasInfo chain.  Any other node belongs
                // to the owner that linked it and keeps CloseScreen pending.
                state.D[0] = 0;
                return;
            }

            if (!TryValidateSyntheticRasInfoTeardown(rasInfo) ||
                !TryReadLongField(
                    screen + ScreenRastPortOffset,
                    RastPortBitMapOffset,
                    out var screenRastPortBitMap) ||
                screenRastPortBitMap != GetSyntheticScreenBitMapAddress())
            {
                // The public RastPort/BitMap and RasInfo/BitMap links are
                // source-storage ownership records.  A provider-repointed
                // link must remain available to that owner instead of being
                // detached by synthetic screen teardown.  SA_BitMap is the
                // one standard-planar exception: the RastPort intentionally
                // points at the caller-owned header for the lifetime of the
                // screen.
                state.D[0] = 0;
                return;
            }

            if (!TryValidateSyntheticBitMapTeardown(
                    screen + ScreenBitMapOffset,
                    _syntheticBitMapAddress))
            {
                // The embedded Screen.BitMap is a second public view of the
                // same backing allocation.  Its header and plane links are
                // independently writable guest ownership records; do not
                // clear a provider-repointed plane or release the compatibility
                // backing store until those fields are restored.
                state.D[0] = 0;
                return;
            }

            if (!TryValidateSyntheticViewPortTeardown(viewPort))
            {
                // A mapped non-compatibility copper link belongs to a
                // provider/native display owner.  Refuse before LoadView(NULL)
                // or any guest-link clearing so the active stream remains
                // available for that owner to detach and retry.
                state.D[0] = 0;
                return;
            }

            var view = _syntheticViewAddress;
            if (!TryValidateSyntheticViewTeardown(view, viewPort))
            {
                // The synthetic View's LOF/SHF CPR links are public
                // ownership records too.  A mapped foreign link must be
                // detached by its provider before CloseScreen can clear the
                // compatibility View envelope; stale unmapped values remain
                // eligible for the historical cleanup path.
                state.D[0] = 0;
                return;
            }

            // A native/provider owner may publish GfxBase->ActiView directly
            // without passing through the host LoadView gateway.  Reconcile
            // that guest publication before deciding whether the compatibility
            // View is still ours to blank; otherwise a stale host cache could
            // clear a foreign active View.
            // Closing a compatibility Screen may blank the stream only after
            // reconciling the guest ActiView publication.  An odd discovered
            // native base cannot be probed as a 68000 LONG, so leave the
            // Screen and its active provider untouched for native teardown.
            if (!TryGetAlignedGraphicsLibraryBase(out var graphicsBase))
            {
                state.D[0] = 0;
                return;
            }

            var guestActiveView = _currentViewAddress;
            if (!TryGetGraphicsFieldAddress(
                    graphicsBase,
                    GfxBaseActiViewOffset,
                    sizeof(uint),
                    out var gfxBaseActiView))
            {
                state.D[0] = 0;
                return;
            }

            if (_machine.Bus.IsMappedMemoryRange(gfxBaseActiView, sizeof(uint)))
            {
                guestActiveView = _machine.Bus.ReadLong(gfxBaseActiView);
                _currentViewAddress = guestActiveView;
            }

            var activeSyntheticView = _syntheticViewAddress != 0 &&
                _currentViewAddress == _syntheticViewAddress &&
                guestActiveView == _syntheticViewAddress;
            // ViewPort identity is not View identity.  A native/provider View
            // may temporarily reference the synthetic screen's embedded
            // ViewPort while it owns the active-view publication.  Reusing
            // that viewport must not make CloseScreen blank the foreign View
            // or clear GfxBase->ActiView on its behalf; only the exact
            // compatibility View is ours to hand off through LoadView(NULL).

            if (activeSyntheticView)
            {
                // LoadView(NULL) is the classic display hand-off for closing
                // the last screen.  Use a private state so the CloseScreen
                // ABI remains a boolean result while the pending blanking
                // request still carries the caller's cycle boundary.
                var blankState = new M68kCpuState { Cycles = state.Cycles };
                blankState.A[1] = 0;
                // The hand-off is part of the ownership transaction.  If the
                // display boundary cannot publish the blanking list, keep
                // every guest link and the active-view pointer intact so the
                // native/provider owner can retry instead of tearing down an
                // otherwise still-visible screen.
                if (!TryLoadView(blankState))
                {
                    state.D[0] = 0;
                    return;
                }
            }

            FreeCompatibilityViewPortCopLists(viewPort);
            ClearCompatibilityViewPortCopLinks(viewPort, state.Cycles);
            if (view != 0 &&
                CanAddressField(view, ViewLofCprListOffset, sizeof(uint) * 2) &&
                _machine.Bus.IsMappedMemoryRange(view + (uint)ViewLofCprListOffset, sizeof(uint) * 2))
            {
                _machine.Bus.WriteLong(view + ViewLofCprListOffset, 0, state.Cycles);
                _machine.Bus.WriteLong(view + ViewShfCprListOffset, 0, state.Cycles);
            }

            // The backing allocations are owned by the reset-scoped
            // compatibility session.  Drop their handles so the next
            // OpenScreen call can publish a fresh Screen/View/ViewPort chain;
            // the guest allocator remains monotonic until reset, as it does
            // for the other synthetic UI objects.
            FreeSyntheticScreenColorMap();
            _syntheticScreenAddress = 0;
            _syntheticWindowAddress = 0;
            _syntheticUiDisplay.WindowOpen = false;
            _syntheticUiDisplay.WindowOpenCount = 0;
            _syntheticUserPortAddress = 0;
            _syntheticMessageAddress = 0;
            _syntheticViewAddress = 0;
            _syntheticRasInfoAddress = 0;
            _syntheticSecondRasInfoAddress = 0;
            _syntheticBitMapAddress = 0;
            _syntheticCustomBitMapAddress = 0;
            _syntheticRastPortAddress = 0;
            _syntheticPlaneAddress = 0;
            _syntheticGadgetListAddress = 0;
            _syntheticUserPortSignalMask = 0;
            _syntheticIdcmpFlags = 0;
            _syntheticScreenViewModes = 0;
            _syntheticPaletteLoaded = false;
            _syntheticPaletteRgb32Loaded = false;
            Array.Clear(_syntheticPalette);
            Array.Clear(_syntheticPaletteRgb32);

            var ntsc = _machine.Bus.Chipset.VideoStandard == VideoStandard.Ntsc;
            _syntheticScreenWidth = ntsc
                ? AmigaConstants.NtscLowResWidth
                : AmigaConstants.PalLowResWidth;
            _syntheticScreenHeight = ntsc
                ? AmigaConstants.NtscLowResStandardHeight
                : SyntheticScreenDefaultHeight;
            _syntheticScreenDepth = SyntheticScreenDefaultDepth;
            _syntheticScreenLeft = 0;
            _syntheticScreenTop = 0;
            _syntheticScreenDetailPen = 0;
            _syntheticScreenBlockPen = 1;
            _syntheticScreenFlags = (ushort)(CustomScreenType | SyntheticScreenShowTitle);
            _syntheticScreenInterleaved = false;
            _syntheticScreenDefaultTitleAddress = 0;
            ReleaseSyntheticScreenTextFont();
            _syntheticScreenFontAttrAddress = 0;
            _syntheticUiDisplay.WindowUsesDefaultFont = false;
            _syntheticWindowLeft = 0;
            _syntheticWindowTop = 0;
            _syntheticWindowWidth = _syntheticScreenWidth;
            _syntheticWindowHeight = _syntheticScreenHeight;
            // Closing a synthetic screen must not revoke a view published by
            // another owner.  The activeSyntheticView guard above is the same
            // ownership decision used for blanking; keep the foreign view and its
            // GfxBase->ActiView publication intact when it is currently active.
            if (activeSyntheticView)
            {
                _currentViewAddress = 0;
            }
            // The compatibility View envelope is retired even when a foreign
            // active View remains published.  Drop its topology fingerprint so a
            // later screen session cannot consult stale teardown metadata.
            InvalidatePublishedViewSignature();
            // Keep the close path and failed-open path on one reset boundary. The
            // shared default font survives, while no per-screen request state or
            // host-only sentinel remains available to a later OpenScreen call.
            ResetSyntheticScreenRequestState();
			state.D[0] = 1;
        }

    private void ConfigureSyntheticWindowFromNewWindow(uint newWindow)
    {
        _syntheticWindowConfigurationRejected = false;
        _syntheticWindowScreenTargetAddress = 0;
        var newWindowEnvelopeSize = NewWindowTypeOffset + sizeof(ushort);
        if (newWindow != 0 && (newWindow & 1u) != 0)
        {
                // NewWindow contains WORD/LONG fields.  A 68000 caller must
                // provide a word-aligned base; do not let byte-addressable
                // host memory turn an address-error request into a claimable
                // compatibility Window.
                _syntheticWindowConfigurationRejected = true;
                return;
            }

        if (newWindow != 0 &&
                (!CanAddressField(newWindow, 0, newWindowEnvelopeSize) ||
                 !_machine.Bus.IsMappedMemoryRange(newWindow, newWindowEnvelopeSize)))
        {
                // A present but unreadable NewWindow belongs to native
                // Intuition/provider ownership.  Do not manufacture a
                // default compatibility Window and consume a request whose
                // caller-owned envelope cannot be inspected.
                _syntheticWindowConfigurationRejected = true;
                return;
            }

        if (newWindow == 0 ||
                !CanAddressField(newWindow, 0, newWindowEnvelopeSize) ||
                !_machine.Bus.IsMappedMemoryRange(newWindow, newWindowEnvelopeSize))
        {
                return;
            }

            var requestedType = _machine.Bus.ReadWord(newWindow + NewWindowTypeOffset);
            if (requestedType != 0 && requestedType != CustomScreenType)
            {
                // The private bridge only owns CUSTOMSCREEN windows.  A
                // public/workbench window needs Intuition's screen lists and
                // visitor ownership, so leave those requests to native or a
                // provider rather than silently attaching them to the
                // synthetic display.
                _syntheticWindowConfigurationRejected = true;
                return;
            }

            var requestedScreen = _machine.Bus.ReadLong(newWindow + NewWindowScreenOffset);
            if (requestedScreen != 0 &&
                (_syntheticScreenAddress == 0 ||
                 requestedScreen != _syntheticScreenAddress))
            {
                // A non-null nw_Screen is an explicit custom-screen target.
                // Do not claim a foreign/unknown Screen, and do not create a
                // compatibility Screen merely because the request's pointer
                // is readable: its lifecycle belongs to another owner.
                _syntheticWindowConfigurationRejected = true;
                return;
            }

            _syntheticWindowScreenTargetAddress = requestedScreen;

            // The reset-scoped compatibility Window already carries its
            // published geometry.  The target/type admission above still
            // applies to every caller-owned NewWindow, but do not overwrite
            // that geometry on a repeated OpenWindow request.
            if (_syntheticWindowAddress != 0)
                return;

            _syntheticWindowLeft = ReadSignedWordOrDefault(newWindow + NewWindowLeftEdgeOffset, 0);
            _syntheticWindowTop = ReadSignedWordOrDefault(newWindow + NewWindowTopEdgeOffset, 0);
            var width = ReadPositiveWordOrDefault(newWindow + NewWindowWidthOffset, _syntheticScreenWidth);
            var height = ReadPositiveWordOrDefault(newWindow + NewWindowHeightOffset, _syntheticScreenHeight);
            _syntheticWindowWidth = Math.Clamp(width, 16, Math.Max(16, _syntheticScreenWidth));
            _syntheticWindowHeight = Math.Clamp(height, 16, Math.Max(16, _syntheticScreenHeight));
            _syntheticIdcmpFlags = _machine.Bus.ReadLong(newWindow + NewWindowIdcmpFlagsOffset);

            if (TryReadLong(newWindow + NewWindowFirstGadgetOffset, out var gadget) &&
                gadget != 0 &&
                _machine.Bus.IsMappedMemoryRange(gadget, GadgetHeightOffset + 2))
            {
                _syntheticGadgetListAddress = gadget;
            }
        }

        private uint OpenSyntheticWindow()
        {
            if (_syntheticWindowConfigurationRejected)
            {
                _syntheticWindowConfigurationRejected = false;
                return 0;
            }

            if (_syntheticWindowScreenTargetAddress != 0 &&
                _syntheticWindowScreenTargetAddress != _syntheticScreenAddress)
            {
                // Keep the target check at the final claim boundary as well:
                // a provider may have replaced the active Screen between
                // NewWindow decoding and OpenWindow publication.
                _syntheticWindowScreenTargetAddress = 0;
                return 0;
            }

            var window = EnsureSyntheticWindow();
            if (window == 0 || window != _syntheticWindowAddress)
            {
                // EnsureSyntheticWindow may return the generic host-object
                // sentinel when the guest Window allocation is unavailable.
                // That object is not a valid Intuition Window and must not
                // create an uncloseable Screen ownership claim.
                return 0;
            }

            if (window != 0 && window == _syntheticWindowAddress)
            {
                var currentHead = 0u;
                var insertingIntoScreenChain = false;
                if (_syntheticScreenAddress != 0)
                {
                    if (!CanAddressField(
                            _syntheticScreenAddress,
                            ScreenFirstWindowOffset,
                            sizeof(uint)) ||
                        !_machine.Bus.IsMappedMemoryRange(
                            _syntheticScreenAddress + (uint)ScreenFirstWindowOffset,
                            sizeof(uint)) ||
                        !TryReadLongField(
                            _syntheticScreenAddress,
                            ScreenFirstWindowOffset,
                            out currentHead))
                    {
                        return 0;
                    }

                    insertingIntoScreenChain = currentHead != window;
                    if (insertingIntoScreenChain &&
                        (!_machine.Bus.IsWritableMemoryRange(
                             _syntheticScreenAddress + (uint)ScreenFirstWindowOffset,
                             sizeof(uint)) ||
                         !TryReadLongField(window, WindowNextOffset, out _) ||
                         !_machine.Bus.IsWritableMemoryRange(
                             window + (uint)WindowNextOffset,
                             sizeof(uint))))
                    {
                        // Do not publish a host Window claim until both
                        // public list links can be committed.
                        return 0;
                    }
                }

                _syntheticUiDisplay.WindowOpenCount = checked(_syntheticUiDisplay.WindowOpenCount + 1);
                _syntheticUiDisplay.WindowOpen = true;
                if (_syntheticScreenAddress != 0)
                {
                    if (insertingIntoScreenChain)
                    {
                        // Keep any provider/native Window already published
                        // on the Screen as the public tail.  A compatibility
                        // OpenWindow is a list insertion, not permission to
                        // discard another owner's head; when the chain is
                        // empty, clear a stale tail left by an earlier
                        // synthetic close before reusing the backing.
                        _machine.Bus.WriteLong(
                            window + WindowNextOffset,
                            currentHead);
                    }
                    _machine.Bus.WriteLong(
                        _syntheticScreenAddress + (uint)ScreenFirstWindowOffset,
                        window);
                }
            }

            return window;
        }

        private void CloseSyntheticWindow(uint window)
        {
            if (window == 0 || window != _syntheticWindowAddress)
            {
                return;
            }

            if (_syntheticScreenAddress != 0 &&
                (!TryReadLongField(window, WindowWScreenOffset, out var windowScreen) ||
                 windowScreen != _syntheticScreenAddress))
            {
                // Do not retire a claim or rewrite Screen.FirstWindow when
                // the guest has repointed the Window away from the synthetic
                // Screen.  The altered envelope remains available to its
                // native/provider owner for a later retry.
                return;
            }

            var screenFirstWindow = 0u;
            var canDetachScreenWindow = false;
            if (_syntheticScreenAddress != 0)
            {
                if (!CanAddressField(
                        _syntheticScreenAddress,
                        ScreenFirstWindowOffset,
                        sizeof(uint)) ||
                    !_machine.Bus.IsMappedMemoryRange(
                        _syntheticScreenAddress + (uint)ScreenFirstWindowOffset,
                        sizeof(uint)) ||
                    !TryReadLongField(
                        _syntheticScreenAddress,
                        ScreenFirstWindowOffset,
                        out screenFirstWindow))
                {
                    // CloseWindow must not retire host ownership when the
                    // public chain cannot be inspected for a safe detach.
                    return;
                }

                canDetachScreenWindow = screenFirstWindow == window;
                if (canDetachScreenWindow &&
                    !_machine.Bus.IsWritableMemoryRange(
                        _syntheticScreenAddress + (uint)ScreenFirstWindowOffset,
                        sizeof(uint)))
                {
                    // A readable ROM/image overlay masks the guest link. Do
                    // not decrement the host claim while Screen.FirstWindow
                    // would still point at the closed Window.
                    return;
                }
            }

            // Keep the reset-scoped object available to the host presentation
            // path, but detach it from the public Screen.FirstWindow chain and
            // retire only the guest OpenWindow ownership claim.
            if (_syntheticUiDisplay.WindowOpenCount != 0)
            {
                _syntheticUiDisplay.WindowOpenCount--;
            }

            _syntheticUiDisplay.WindowOpen = _syntheticUiDisplay.WindowOpenCount != 0;
            if (_syntheticUiDisplay.WindowOpen)
            {
                return;
            }

            if (_syntheticScreenAddress != 0 && canDetachScreenWindow)
            {
                var nextWindow = 0u;
                _ = TryReadLongField(window, WindowNextOffset, out nextWindow);
                _machine.Bus.WriteLong(
                    _syntheticScreenAddress + (uint)ScreenFirstWindowOffset,
                    nextWindow);
            }
        }

        private void SetSyntheticWindowTitles(M68kCpuState state)
        {
            // SetWindowTitles is keyed by the caller's Window pointer.  Do
            // not let a foreign window mutate the private synthetic Screen
            // or allocate a compatibility session on its behalf.  A null
            // window remains accepted for the existing screen-title-only
            // compatibility path used by the host shim, but only after a
            // synthetic screen has already been opened.
            if ((state.A[0] != 0 &&
                 (_syntheticWindowAddress == 0 ||
                  state.A[0] != _syntheticWindowAddress)) ||
                (state.A[0] == 0 && _syntheticScreenAddress == 0))
            {
                return;
            }

            if (!TryReadOptionalIntuitionTitle(state.A[1], 80, out var windowTitle) ||
                !TryReadOptionalIntuitionTitle(state.A[2], 80, out var screenTitle))
            {
                // A title is a byte-addressed C string, but the compatibility
                // bridge must still refuse a string whose bounded scan would
                // wrap into low memory.  Leave the current title and backing
                // store untouched so the native/provider owner can retry it.
                return;
            }
            var title = !string.IsNullOrWhiteSpace(windowTitle)
                ? windowTitle
                : screenTitle;

            _ = EnsureSyntheticScreen();
            if (state.A[2] != NoTitleChange &&
                (_syntheticScreenAddress == 0 ||
                 !CanAddressField(_syntheticScreenAddress, ScreenTitleOffset, sizeof(uint)) ||
                 !_machine.Bus.IsMappedMemoryRange(
                     _syntheticScreenAddress + (uint)ScreenTitleOffset,
                     sizeof(uint)) ||
                 !_machine.Bus.IsWritableMemoryRange(
                     _syntheticScreenAddress + (uint)ScreenTitleOffset,
                     sizeof(uint))))
            {
                // The public Screen.Title LONG is part of the SetWindowTitles
                // publication. A readable ROM/image overlay must not let the
                // host repaint a title that the guest cannot commit.
                return;
            }

            LogIntuitionTitle(windowTitle, screenTitle);

            if (_syntheticScreenAddress != 0 && state.A[2] != NoTitleChange &&
                _machine.Bus.IsMappedMemoryRange(_syntheticScreenAddress + ScreenTitleOffset, 4))
            {
                _machine.Bus.WriteLong(
                    _syntheticScreenAddress + ScreenTitleOffset,
                    state.A[2],
                    state.Cycles);
            }
            if (string.IsNullOrWhiteSpace(title))
            {
                // A NULL/empty title explicitly blanks the current title bar
                // (NoTitleChange is handled separately above).  Keep the
                // redraw scoped to the bar so application pixels below it
                // remain guest-owned.
                if (state.A[2] != NoTitleChange &&
                    EnsureSyntheticScreenBitmap() &&
                    CanRenderSyntheticScreenTitle())
                {
                    FillSyntheticRect(0, 0, _syntheticScreenWidth, SyntheticScreenTitleHeight, 0);
                    _ = HostRethinkDisplay(state.Cycles);
                    state.Cycles = Math.Max(
                        state.Cycles + 1,
                        _machine.Bus.GetNextFrameStartCycle(Math.Max(0, state.Cycles)));
                }

                return;
            }

            if (!EnsureSyntheticScreenBitmap() ||
                !CanRenderSyntheticScreenTitle())
            {
                return;
            }

            RenderSyntheticScreenTitle(title);
            _ = HostRethinkDisplay(state.Cycles);
            state.Cycles = Math.Max(
                state.Cycles + 1,
                _machine.Bus.GetNextFrameStartCycle(Math.Max(0, state.Cycles)));
        }

        private void ShowSyntheticScreenTitle(M68kCpuState state)
        {
            var screen = state.A[0];
            if (screen == 0 || screen != _syntheticScreenAddress ||
                !CanAddressField(screen, ScreenFlagsOffset, 2) ||
                !_machine.Bus.IsMappedMemoryRange(screen + ScreenFlagsOffset, 2) ||
                !_machine.Bus.IsWritableMemoryRange(screen + ScreenFlagsOffset, 2) ||
                !CanAddressField(screen, ScreenTitleOffset, sizeof(uint)) ||
                !_machine.Bus.IsMappedMemoryRange(screen + ScreenTitleOffset, sizeof(uint)))
            {
                // Foreign or malformed screens remain available to native
                // Intuition or another provider; do not fabricate a claim.
                return;
            }

            var titlePointer = _machine.Bus.ReadLong(screen + ScreenTitleOffset);
            if (titlePointer != 0 &&
                titlePointer != NoTitleChange &&
                !TryValidateSyntheticTitle(titlePointer))
            {
                // SHOWTITLE changes the public flag and may repaint the title
                // bar. Validate the nested C-string first so an unterminated
                // high pointer cannot wrap into low memory after a partial
                // lifecycle publication.
                return;
            }

            var showTitle = state.D[0] != 0;
            SetSyntheticScreenFlag(SyntheticScreenShowTitle, showTitle);
            _machine.Bus.WriteWord(screen + ScreenFlagsOffset, _syntheticScreenFlags, state.Cycles);
            if (!EnsureSyntheticScreenBitmap())
            {
                state.D[0] = 0;
                return;
            }

            if (CanRenderSyntheticScreenTitle())
            {
                var title = ReadOptionalIntuitionTitle(
                    titlePointer,
                    80);
                if (!string.IsNullOrWhiteSpace(title))
                    RenderSyntheticScreenTitle(title);
            }
            state.D[0] = 0;
        }

        private void GetSyntheticScreenData(M68kCpuState state)
        {
            var buffer = state.A[0];
            var size = (ushort)state.D[0];
            var type = (ushort)state.D[1];
            var requestedScreen = state.A[1];

            // GetScreenData copies a caller-selected prefix of Screen.  Keep
            // the public structure boundary explicit so a future native ROM
            // can use the same layout, and never read or write past the
            // guest object owned by this compatibility session.
            if (buffer == 0 || size == 0 || size > ScreenStructSize)
            {
                state.D[0] = 0;
                return;
            }

            uint source;
            switch (type)
            {
                case CustomScreenType:
                    if (requestedScreen == 0 || requestedScreen != _syntheticScreenAddress)
                    {
                        // Foreign custom screens belong to native Intuition or
                        // another provider; do not claim their call.
                        return;
                    }

                    source = requestedScreen;
                    break;
                case WorkbenchScreenType:
                case PublicScreenType:
                    source = EnsureSyntheticScreen();
                    break;
                default:
                    state.D[0] = 0;
                    return;
            }

            if (source == 0 ||
                !CanAddressField(source, 0, size) ||
                !CanAddressField(buffer, 0, size) ||
                !_machine.Bus.IsMappedMemoryRange(source, size) ||
                !_machine.Bus.IsMappedMemoryRange(buffer, size) ||
                !_machine.Bus.IsWritableMemoryRange(buffer, size))
            {
                // A readable destination is not enough for GetScreenData:
                // report a decline before copying or publishing success when
                // a provider/image overlay masks the caller's buffer.
                state.D[0] = 0;
                return;
            }

            var snapshot = new byte[size];
            for (var index = 0; index < size; index++)
                snapshot[index] = _machine.Bus.ReadByte(source + (uint)index);

            // The standard-screen forms return a Screen-shaped compatibility
            // copy.  Keep the owned Screen object custom, but publish the
            // requested standard type in the copy's low SCREENTYPE bits so
            // callers see the same contract as a native Intuition screen.
            if (type != CustomScreenType &&
                size >= ScreenFlagsOffset + 2)
            {
                var flags = (ushort)((snapshot[ScreenFlagsOffset] << 8) |
                                     snapshot[ScreenFlagsOffset + 1]);
                flags = (ushort)((flags & ~0x000F) | type);
                snapshot[ScreenFlagsOffset] = (byte)(flags >> 8);
                snapshot[ScreenFlagsOffset + 1] = (byte)flags;
            }

            for (var index = 0; index < size; index++)
                _machine.Bus.WriteByte(buffer + (uint)index, snapshot[index], state.Cycles);

            state.D[0] = 1;
        }

        private void QuerySyntheticOverscan(M68kCpuState state)
        {
            var displayId = state.A[0];
            var rectangle = state.A[1];
            var overscan = (ushort)state.D[0];
            if (rectangle == 0 ||
                (rectangle & 1u) != 0 ||
                !CanAddressField(rectangle, 0, 8) ||
                !TryDecodeNativeScreenMode(displayId, out var viewModes) ||
                ((viewModes & ViewModeSuperHires) != 0 &&
                 !_machine.Bus.Chipset.SupportsEcsDisplayRegisters) ||
                !_machine.Bus.IsMappedMemoryRange(rectangle, 8) ||
                !_machine.Bus.IsWritableMemoryRange(rectangle, 8))
            {
                // Non-native/RTG display IDs remain available to the separate
                // provider boundary rather than receiving synthetic geometry.
                // A readable Rectangle is not enough for QueryOverscan:
                // geometry publication must not overwrite a provider/image
                // overlay that masks the caller's output span.
                return;
            }

            var ntscOverride = GetSyntheticMonitorNtscOverride(displayId);
            if (!TryGetSyntheticOverscanGeometry(
                    viewModes,
                    overscan,
                    out var left,
                    out var top,
                    out var width,
                    out var height,
                    ntscOverride) ||
                left < short.MinValue || left > short.MaxValue ||
                top < short.MinValue || top > short.MaxValue ||
                width < 1 || width - 1 > short.MaxValue ||
                height < 1 || height - 1 > short.MaxValue)
            {
                state.D[0] = 0;
                return;
            }

            _machine.Bus.WriteWord(rectangle, unchecked((ushort)(short)left), state.Cycles);
            _machine.Bus.WriteWord(rectangle + 2, unchecked((ushort)(short)top), state.Cycles);
            _machine.Bus.WriteWord(rectangle + 4, unchecked((ushort)(short)(left + width - 1)), state.Cycles);
            _machine.Bus.WriteWord(rectangle + 6, unchecked((ushort)(short)(top + height - 1)), state.Cycles);
            state.D[0] = 1;
        }

        private void LogIntuitionTitle(string windowTitle, string screenTitle)
        {
            if (_intuitionTitleDiagnosticCount >= 4)
            {
                return;
            }

            _diagnostics.Add(new AmigaBootDiagnostic(
                "AMIGA_BOOT_INTUITION_TITLE",
                $"SetWindowTitles window='{windowTitle}' screen='{screenTitle}'."));
            _intuitionTitleDiagnosticCount++;
        }

        private string ReadOptionalIntuitionTitle(uint address, int maxLength)
        {
            return TryReadOptionalIntuitionTitle(address, maxLength, out var title)
                ? title
                : string.Empty;
        }

        private bool TryReadOptionalIntuitionTitle(uint address, int maxLength, out string title)
        {
            title = string.Empty;
            if (address == 0 || address == NoTitleChange)
            {
                return true;
            }

            if (maxLength < 0 || !CanAddressField(address, 0, sizeof(byte)))
            {
                return false;
            }

            var chars = new char[maxLength];
            for (var index = 0; index < chars.Length; index++)
            {
                if (!CanAddressField(address, index, sizeof(byte)))
                {
                    return false;
                }

                var current = address + (uint)index;
                if (!_machine.Bus.IsMappedMemoryRange(current, sizeof(byte)))
                {
                    return false;
                }

                var value = _machine.Bus.ReadByte(current);
                if (value == 0)
                {
                    title = new string(chars, 0, index);
                    return true;
                }

                chars[index] = (char)value;
            }

            // Preserve the existing bounded-reader behavior: a string that
            // fills the requested title width is valid even without a NUL.
            title = new string(chars);
            return true;
        }

        private void LogUiCall(string libraryName, int displacement)
        {
            if (_uiDiagnosticCount >= 128)
            {
                return;
            }

            _diagnostics.Add(new AmigaBootDiagnostic("AMIGA_BOOT_UI_CALL", $"{libraryName} LVO {displacement}."));
            _uiDiagnosticCount++;
        }

        private void LogExecCall(int displacement)
        {
            if (_execDiagnosticCount >= 128)
            {
                return;
            }

            _diagnostics.Add(new AmigaBootDiagnostic("AMIGA_BOOT_EXEC_CALL", $"exec.library LVO {displacement}."));
            _execDiagnosticCount++;
        }

        private void LogIconCall(int displacement)
        {
            if (_iconDiagnosticCount >= 16)
            {
                return;
            }

            _diagnostics.Add(new AmigaBootDiagnostic("AMIGA_BOOT_ICON_CALL", $"icon.library LVO {displacement}."));
            _iconDiagnosticCount++;
        }

        private void LoadView(M68kCpuState state)
            => _ = TryLoadView(state);

        private bool TryLoadView(M68kCpuState state)
        {
            var result = false;
            _graphicsServices.WithMonitorStateLock(() =>
            {
                result = TryLoadViewCore(state);
                return result;
            });
            return result;
        }

        private bool TryLoadViewCore(M68kCpuState state)
        {
            // ActiView is the public ownership handoff for every LoadView
            // path, including a blank request and an assembling View.  A
            // native/provider image may leave the LONG readable while
            // masking it with a read-only overlay; decline before queuing a
            // copper change or updating the host-side active-view cache so
            // that owner can retry through its own boundary.
            var viewAddress = state.A[1];
            // A NULL request while CyberGraphX owns the selected RTG scanout
            // must remain visible to that provider.  The graphics gateway
            // deliberately forwards this boundary after observing the active
            // RTG View; do not let the compatibility callback blank the
            // provider's front viewport before its own LoadView path runs.
            if (viewAddress == 0 && IsSelectedRtgViewActive())
                return false;

            if (!CanPublishActiveView())
                return false;

            if (viewAddress == 0)
            {
                // A repeated NULL handoff is already satisfied once the
                // compatibility owner has blanked the stream and the guest
                // GfxBase sidecar still agrees.  Keep this no-op symmetric
                // with the portable LoadView idempotence rule, but do not
                // swallow a provider/native takeover that replaced the
                // guest ActiView pointer behind the host cache.
                // A discovered native graphics.library base is a 68000
                // word-addressed structure.  If a malformed/provider-owned
                // image is byte-readable at an odd base, do not interpret its
                // ActiView LONG through the host bridge or suppress a blank
                // handoff that the native owner still needs to see.
                var guestActiViewIsBlank = false;
                if (TryGetAlignedGraphicsLibraryBase(out var graphicsBase) &&
                    TryGetGraphicsFieldAddress(
                        graphicsBase,
                        GfxBaseActiViewOffset,
                        sizeof(uint),
                        out var gfxBaseActiView))
                {
                    if (_machine.Bus.IsMappedMemoryRange(gfxBaseActiView, sizeof(uint)))
                    {
                        guestActiViewIsBlank = _machine.Bus.ReadLong(gfxBaseActiView) == 0;
                    }
                    else
                    {
                        // The compact compatibility image may legitimately
                        // stop before the public GfxBase tail.  Its host
                        // cache is authoritative for that image, so retain
                        // the historical idempotent NULL handoff there.  An
                        // explicitly discovered native base is different:
                        // an unreadable ActiView field is an unknown
                        // provider/native state, not proof that the display
                        // is blank. Keep LoadView(NULL) visible so the
                        // native owner can handle the request.
                        guestActiViewIsBlank = _nativeGraphicsLibraryBase == 0;
                    }
                }
                if (TryGetPublishedViewSignature(0, out var blankSignature) &&
                    guestActiViewIsBlank &&
                    IsPublishedViewSignatureCurrent(0, blankSignature))
                {
                    // Keep the optional native ViewExtra tail blank even
                    // when the copper stream is already idempotently blank.
                    // This sidecar-only seam does not enqueue a second host
                    // display request.
                    _ = _graphicsServices.TryClearNativeViewSidecars();
                    PublishActiveView(0, state.Cycles);
                    _cyberGraphics?.SelectFrontViewPort(0);
                    return true;
                }

                // A null View blanks the display at the same frame boundary
                // as a normal LoadView request.  This must replace any older
                // pending list, including a list that has not reached the
                // custom chips yet.
                QueueCopperListLoad(0, state.Cycles);
                _currentViewAddress = 0;
                _ = _graphicsServices.TryClearNativeViewSidecars();
                PublishActiveView(0, state.Cycles);
                RememberPublishedViewSignature(0);
                _cyberGraphics?.SelectFrontViewPort(0);
                return true;
            }

            if (!TryReadLongField(viewAddress, ViewViewPortOffset, out var viewPort) ||
                !TryReadLongField(viewAddress, ViewLofCprListOffset, out var lofCprList) ||
                !TryReadLongField(viewAddress, ViewShfCprListOffset, out var shfCprList))
            {
                return false;
            }

            // LoadView is idempotent for an unchanged active View.  Keep the
            // guard after the public envelope preflight so malformed pointers
            // remain available to native/provider ownership, and repair only
            // the guest active-view sidecar on the no-op path.  The signature
            // deliberately covers the linked display topology rather than
            // the generated copper bytes; changing a ViewPort, RasInfo, or
            // plane link therefore still requests a fresh handoff.
            if (TryGetPublishedViewSignature(viewAddress, out var viewSignature) &&
                IsPublishedViewSignatureCurrent(viewAddress, viewSignature))
            {
                // ViewExtra monitor/top-line edits do not change the copper
                // topology fingerprint. Repair the optional native tail on
                // this idempotent host path without re-queueing the stream.
                _ = _graphicsServices.TryRefreshNativeViewSidecars(viewAddress);
                PublishActiveView(viewAddress, state.Cycles);
                PublishRtgFrontViewPort(viewPort);
                return true;
            }

            // A view with no prepared copper chain is legal while a display is
            // being assembled.  Keep the guest active-view publication, but
            // do not disturb the currently loaded hardware list.
            if (viewPort == 0 && lofCprList == 0 && shfCprList == 0)
            {
                // The View is publishable while its display chain is still
                // being assembled, but any older queued copper request now
                // belongs to the superseded View.  Retire only that pending
                // request: keep the currently loaded custom-chip stream
                // untouched until the provider supplies a complete chain.
                CancelPendingCopperList();
                _currentViewAddress = viewAddress;
                _ = _graphicsServices.TryRefreshNativeViewSidecars(viewAddress);
                PublishActiveView(viewAddress, state.Cycles);
                RememberPublishedViewSignature(viewAddress);
                // An assembling View has no public provider bitmap to own the
                // front selection.  Clear a stale CyberGraphX registration now
                // rather than leaving the provider marked active until a later
                // copper rebuild observes the incomplete chain.
                PublishRtgFrontViewPort(viewPort);
                return true;
            }

            // RTG/CyberGraphX owns its scanout publication.  Selecting the
            // provider front viewport is the complete host-side LoadView
            // operation for that path; no compatibility copper list is built.
            if (viewPort != 0 &&
                IsProviderViewPortForPublicBitmap(viewPort) &&
                _cyberGraphics?.RtgDevice?.FrontViewPort == viewPort)
            {
                _currentViewAddress = viewAddress;
                PublishActiveView(viewAddress, state.Cycles);
                RememberPublishedViewSignature(viewAddress);
                _cyberGraphics.SelectFrontViewPort(viewPort);
                return true;
            }

            // Only commit the guest active-view pointer and host current-view
            // field after the non-empty copper source has been resolved and
            // queued.  A failed projection therefore leaves the previously
            // active display stream and view publication intact.
            if (!TryPublishCopperListFromView(viewAddress, state.Cycles))
                return false;

            _currentViewAddress = viewAddress;
            _ = _graphicsServices.TryRefreshNativeViewSidecars(viewAddress);
            PublishActiveView(viewAddress, state.Cycles);
            RememberPublishedViewSignature(viewAddress);
            PublishRtgFrontViewPort(viewPort);
            return true;
        }

        private bool IsPublishedViewSignatureCurrent(uint view, ulong signature)
        {
            return _hasPublishedViewSignature &&
                _publishedViewAddress == view &&
                _currentViewAddress == view &&
                _publishedViewSignature == signature;
        }

        /// <summary>
        /// Determines whether the selected CyberGraphX front viewport is
        /// still the public bitmap attached to the currently active guest
        /// View.  A provider registration may outlive a screen/view reuse of
        /// the same embedded ViewPort, so the registration alone is not an
        /// ownership proof.  Readable public RasInfo/BitMap links are the
        /// discriminator; a sparse provider-owned envelope remains
        /// fail-closed for the provider.
        /// </summary>
        private bool IsSelectedRtgViewActive()
        {
            if (_cyberGraphics?.RtgScanoutSelected != true)
                return false;

            if (_currentViewAddress == 0)
                return true;

            if (!TryReadLongField(
                    _currentViewAddress,
                    ViewViewPortOffset,
                    out var viewPort))
            {
                return true;
            }

            var frontViewPort = _cyberGraphics.RtgDevice?.FrontViewPort ?? 0;
            if (frontViewPort == 0 || viewPort != frontViewPort)
                return false;

            return IsProviderViewPortForPublicBitmap(frontViewPort);
        }

        /// <summary>
        /// Classifies a ViewPort against its current public RasInfo bitmap.
        /// The CyberGraphX registration is only a candidate owner; a
        /// standard-planar bitmap written into the shared ViewPort revokes
        /// that candidate and lets graphics.library rebuild copper.
        /// </summary>
        private bool IsProviderViewPortForPublicBitmap(uint viewPort)
        {
            if (viewPort == 0 || _cyberGraphics?.IsRtgViewPort(viewPort) != true)
                return false;

            if (!TryReadLongField(
                    viewPort,
                    ViewPortRasInfoOffset,
                    out var rasInfo) ||
                rasInfo == 0)
            {
                return true;
            }

            if (!TryReadLongField(
                    rasInfo,
                    RasInfoBitMapOffset,
                    out var bitMap))
            {
                return true;
            }

            return _cyberGraphics.IsRtgBitMap(bitMap);
        }

        /// <summary>
        /// Publishes the provider front selection only when the loaded
        /// ViewPort still carries a provider-owned public bitmap.  Clearing
        /// a stale registration is part of the standard-planar LoadView
        /// handoff and does not touch any CyberGraphX surface storage.
        /// </summary>
        private void PublishRtgFrontViewPort(uint viewPort)
        {
            if (_cyberGraphics is null)
                return;

            _cyberGraphics.SelectFrontViewPort(
                IsProviderViewPortForPublicBitmap(viewPort) ? viewPort : 0);
        }

        private void PublishRtgFrontForCurrentView()
        {
            PublishRtgFrontViewPort(
                _currentViewAddress != 0 &&
                TryReadLongField(_currentViewAddress, ViewViewPortOffset, out var viewPort)
                    ? viewPort
                    : 0);
        }

        private void RememberPublishedViewSignature(uint view)
        {
            if (TryGetPublishedViewSignature(view, out var signature))
            {
                _publishedViewAddress = view;
                _publishedViewSignature = signature;
                _hasPublishedViewSignature = true;
                return;
            }

            // Do not retain a stale fingerprint when the guest envelope is
            // sparse.  The next LoadView must resolve and publish normally.
            _publishedViewAddress = 0;
            _publishedViewSignature = 0;
            _hasPublishedViewSignature = false;
        }

        private bool TryGetPublishedViewSignature(uint view, out ulong signature)
        {
            const ulong offsetBasis = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            signature = offsetBasis;
            if (view == 0)
                return true;

            var current = view;
            var visited = new HashSet<uint>();
            for (var node = 0; node < 64 && current != 0; node++)
            {
                if (!visited.Add(current))
                    return false;

                if (node == 0)
                {
                    if (!TryHashPublishedViewRange(
                            current,
                            CopperStartGraphicsLayouts.ViewSize,
                            prime,
                            ref signature))
                    {
                        return false;
                    }

                    if (!TryReadLongField(current, ViewViewPortOffset, out current))
                        return false;
                }
                else
                {
                    if (!TryReadWordField(current, ViewPortModesOffset, out var modes) ||
                        !TryReadLongField(current, ViewPortNextOffset, out var next))
                    {
                        return false;
                    }

                    if ((modes & CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsModeIds.ViewPortHidden) != 0)
                    {
                        // Resident display traversal consumes only Modes/Next
                        // for hidden nodes. Keep the publication fingerprint
                        // on that same ownership boundary so malformed or
                        // unmapped hidden RasInfo/BitMap payloads cannot
                        // reject an otherwise valid LoadView.
                        if (!CanAddressField(current, ViewPortModesOffset, sizeof(ushort)) ||
                            !CanAddressField(current, ViewPortNextOffset, sizeof(uint)) ||
                            !TryHashPublishedViewRange(
                                current + (uint)ViewPortModesOffset,
                                sizeof(ushort),
                                prime,
                                ref signature) ||
                            !TryHashPublishedViewRange(
                                current + (uint)ViewPortNextOffset,
                                sizeof(uint),
                                prime,
                                ref signature))
                        {
                            return false;
                        }

                        current = next;
                        continue;
                    }

                    if (!TryHashPublishedViewRange(
                            current,
                            CopperStartGraphicsLayouts.ViewPortSize,
                            prime,
                            ref signature) ||
                        !TryReadLongField(current, ViewPortRasInfoOffset, out var rasInfo) ||
                        !TryHashPublishedRasInfoChain(rasInfo, prime, ref signature))
                    {
                        return false;
                    }

                    current = next;
                }
            }

            return current == 0;
        }

        private bool TryHashPublishedRasInfoChain(
            uint rasInfo,
            ulong prime,
            ref ulong signature)
        {
            var visited = new HashSet<uint>();
            for (var node = 0; node < 64 && rasInfo != 0; node++)
            {
                if (!visited.Add(rasInfo) ||
                    !TryHashPublishedViewRange(
                        rasInfo,
                        CopperStartGraphicsLayouts.RasInfoSize,
                        prime,
                        ref signature) ||
                    !TryReadLongField(rasInfo, RasInfoNextOffset, out var next) ||
                    !TryReadLongField(rasInfo, RasInfoBitMapOffset, out var bitMap) ||
                    !TryHashPublishedBitMap(bitMap, prime, ref signature))
                {
                    return false;
                }

                rasInfo = next;
            }

            return rasInfo == 0;
        }

        private bool TryHashPublishedBitMap(
            uint bitMap,
            ulong prime,
            ref ulong signature)
        {
            if (bitMap == 0)
                return true;

            // The header is part of the topology signature even when the
            // caller has not supplied a usable depth yet.  That lets the
            // regular LoadView resolver preserve its historical acceptance
            // boundary while still invalidating a later header edit.
            if (!TryHashPublishedViewRange(bitMap, 6, prime, ref signature) ||
                !TryReadByteField(bitMap, BitMapDepthOffset, out var depth))
            {
                return false;
            }

            if (depth == 0 || depth > 8)
                return true;

            for (var plane = 0; plane < depth; plane++)
            {
                var planeOffset = BitMapPlanesOffset + plane * sizeof(uint);
                if (!TryHashPublishedViewRange(
                        bitMap + (uint)planeOffset,
                        sizeof(uint),
                        prime,
                        ref signature))
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryHashPublishedViewRange(
            uint address,
            int byteCount,
            ulong prime,
            ref ulong signature)
        {
            if (address == 0 || byteCount <= 0 ||
                !CanAddressField(address, 0, byteCount) ||
                !_machine.Bus.IsMappedMemoryRange(address, byteCount))
            {
                return false;
            }

            for (var offset = 0; offset < byteCount; offset++)
            {
                signature ^= _machine.Bus.ReadByte(address + (uint)offset);
                signature *= prime;
            }

            return true;
        }

        /// <summary>
        /// Implements the Kickstart 3.1 CalcIVG arithmetic for a compatibility
        /// viewport.  The native routine counts only the display copper
        /// instructions, charges four colour clocks per instruction, divides
        /// by the monitor's colour clocks per line, rounds up, keeps the
        /// legacy two-line OCS/ECS floor, and applies lace/scan-double
        /// scaling.  UCopIns remains outside this count just as it is in the
        /// ROM implementation; a viewport not built by this compatibility
        /// owner returns the documented zero failure result.
        /// </summary>
        private ushort CalcIvg(uint view, uint viewPort)
        {
            if (view == 0 || viewPort == 0 ||
                !_compatibilityCprListByViewPort.TryGetValue(viewPort, out var cprList) ||
                !_compatibilityCopperByCprList.TryGetValue(cprList, out var rawCopperList) ||
                !_compatibilityDisplayInstructionCountByCopper.TryGetValue(
                    rawCopperList,
                    out var displayInstructionCount) ||
                !TryReadLongField(viewPort, ViewPortDspInsOffset, out var dspIns) ||
                dspIns != rawCopperList)
            {
                return 0;
            }

            var modeId = GetViewPortModeId(viewPort);
            if (modeId == CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsModeIds.Invalid ||
                !CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsDisplayDatabase
                    .TryGetCalcIvgProfile(
                        modeId,
                        _machine.Bus.Chipset.VideoStandard == VideoStandard.Ntsc,
                        _machine.Bus.Chipset.SupportsEcsDisplayRegisters,
                        out var totalColorClocks,
                        out var isLaced,
                        out var isScanDoubled))
            {
                return 0;
            }

            if (displayInstructionCount < 0)
                return 0;

            // The source routine divides instructionCount * 2 copper cycles
            // by (TotalColorClocks / 2).  Keep the equivalent four-clock
            // form in a wide accumulator so malformed guest metadata cannot
            // wrap before the documented UWORD result is produced.
            var requiredColorClocks = (ulong)(uint)displayInstructionCount * 4UL;
            var lines = (requiredColorClocks + totalColorClocks - 1UL) /
                        totalColorClocks;

            if (!_machine.Bus.Chipset.HasAllCapabilities(AmigaChipCapabilities.AgaAliceRegisters))
                lines = Math.Max(2UL, lines);

            if (isLaced)
                lines *= 2UL;
            else if (isScanDoubled)
                lines = (lines + 1UL) / 2UL;

            return lines <= ushort.MaxValue ? (ushort)lines : (ushort)0;
        }

        private void PublishActiveView(uint viewAddress, long cycle)
        {
            if (!CanPublishActiveView() ||
                !TryGetAlignedGraphicsLibraryBase(out var graphicsBase))
            {
                return;
            }

            if (TryGetGraphicsFieldAddress(
                    graphicsBase,
                    GfxBaseActiViewOffset,
                    sizeof(uint),
                    out var gfxBaseActiView) &&
                _machine.Bus.IsMappedMemoryRange(gfxBaseActiView, sizeof(uint)) &&
                _machine.Bus.IsWritableMemoryRange(gfxBaseActiView, sizeof(uint)))
                _machine.Bus.WriteLong(gfxBaseActiView, viewAddress, cycle);
        }

        private bool CanPublishActiveView()
        {
            if (!TryGetAlignedGraphicsLibraryBase(out var graphicsBase))
            {
                // An odd discovered image is not a 68000 LONG-addressable
                // GfxBase.  Preserve the existing native/provider boundary:
                // do not interpret it, but also do not block the normal
                // compatibility display handoff.
                return true;
            }

            if (!TryGetGraphicsFieldAddress(
                    graphicsBase,
                    GfxBaseActiViewOffset,
                    sizeof(uint),
                    out var gfxBaseActiView))
            {
                // A wrapped/high native base cannot be probed safely.  Leave
                // that malformed field visible to the native/provider owner
                // while keeping the host's historical handoff behavior.
                return true;
            }

            if (!_machine.Bus.IsMappedMemoryRange(gfxBaseActiView, sizeof(uint)))
                return true;

            return _machine.Bus.IsWritableMemoryRange(gfxBaseActiView, sizeof(uint));
        }

        private uint SetChipRev(uint requestedBits)
        {
            var chipset = _machine.Bus.Chipset;
            var availableBits = 0u;
            if (chipset.SupportsEcsDmaRegisters)
                availableBits |= CopperStartGraphicsChipRevision.HrAgnus;
            if (chipset.SupportsEcsDisplayRegisters)
                availableBits |= CopperStartGraphicsChipRevision.HrDenise;
            if (chipset.HasAllCapabilities(AmigaChipCapabilities.AgaAliceRegisters))
                availableBits |= CopperStartGraphicsChipRevision.AaAlice;
            if (chipset.HasAllCapabilities(AmigaChipCapabilities.AgaLisaRegisters))
                availableBits |= CopperStartGraphicsChipRevision.AaLisa;

            _graphicsChipRevBits0 |= CopperStartGraphicsChipRevision.RequestedBits(requestedBits, availableBits);
            if (!TryGetAlignedGraphicsLibraryBase(out var graphicsBase))
                return _graphicsChipRevBits0;

            if (TryGetGraphicsFieldAddress(
                    graphicsBase,
                    GfxBaseChipRevBits0Offset,
                    sizeof(byte),
                    out var chipRevAddress) &&
                _machine.Bus.IsMappedMemoryRange(chipRevAddress, 1) &&
                _machine.Bus.IsWritableMemoryRange(chipRevAddress, 1))
            {
                _machine.Bus.WriteByte(
                    chipRevAddress,
                    unchecked((byte)_graphicsChipRevBits0),
                    _machine.Cpu.State.Cycles);
            }

            return _graphicsChipRevBits0;
        }

        private void LoadRgb4(M68kCpuState state)
        {
            var viewPort = state.A[0];
            var colors = state.A[1];
            var count = Math.Clamp(
                unchecked((int)state.D[0]),
                0,
                256);
            if (IsSyntheticViewPort(viewPort))
                count = Math.Min(count, GetSyntheticPaletteCapacity());

            if (colors == 0 ||
                count <= 0 ||
                !_machine.Bus.IsMappedMemoryRange(colors, count * 2))
            {
                return;
            }

            if (IsSyntheticViewPort(viewPort))
            {
                for (var index = 0; index < count; index++)
                {
                    var color = (ushort)(_machine.Bus.ReadWord(colors + (uint)(index * 2)) & 0x0FFF);
                    _syntheticPalette[index] = color;
                    _syntheticPaletteRgb32[index] = ExpandSyntheticRgb4(color);
                }

                _syntheticPaletteLoaded = true;
                _syntheticPaletteRgb32Loaded = true;
                _ = HostRethinkDisplay(state.Cycles);
                return;
            }

            if (!IsSelectedRtgViewActive() && IsActiveViewPort(viewPort))
                _ = HostRethinkDisplay(state.Cycles);
        }

        private void SetRgb4(M68kCpuState state)
        {
            var viewPort = state.A[0];
            var index = state.D[0];
            if (IsSyntheticViewPort(viewPort) && index >= (uint)GetSyntheticPaletteCapacity())
            {
                return;
            }

            var red = (ushort)(state.D[1] & 0x0F);
            var green = (ushort)(state.D[2] & 0x0F);
            var blue = (ushort)(state.D[3] & 0x0F);
            if (IsSyntheticViewPort(viewPort))
            {
                var color = (ushort)((red << 8) | (green << 4) | blue);
                _syntheticPalette[index] = color;
                _syntheticPaletteRgb32[index] = ExpandSyntheticRgb4(color);
                _syntheticPaletteLoaded = true;
                _syntheticPaletteRgb32Loaded = true;
                _ = HostRethinkDisplay(state.Cycles);
                return;
            }

            if (!IsSelectedRtgViewActive() && IsActiveViewPort(viewPort))
                _ = HostRethinkDisplay(state.Cycles);
        }

        private void SetRgb32(M68kCpuState state)
        {
            var viewPort = state.A[0];
            var index = state.D[0];
            if (IsSyntheticViewPort(viewPort) && index >= (uint)GetSyntheticPaletteCapacity())
                return;

            var red = (byte)(state.D[1] & 0xFF);
            var green = (byte)(state.D[2] & 0xFF);
            var blue = (byte)(state.D[3] & 0xFF);
            if (IsSyntheticViewPort(viewPort))
            {
                _syntheticPaletteRgb32[index] =
                    0xFF00_0000u |
                    ((uint)red << 16) |
                    ((uint)green << 8) |
                    blue;
                _syntheticPalette[index] = (ushort)(
                    ((red >> 4) << 8) |
                    ((green >> 4) << 4) |
                    (blue >> 4));
                _syntheticPaletteRgb32Loaded = true;
                _syntheticPaletteLoaded = true;
                _ = HostRethinkDisplay(state.Cycles);
                return;
            }

            // The custom-chip palette owner is deliberately outside this
            // graphics boundary. Until it accepts an AGA RGB8 projection,
            // preserve the existing redraw seam for ordinary planar views.
            if (!IsSelectedRtgViewActive() && IsActiveViewPort(viewPort))
                _ = HostRethinkDisplay(state.Cycles);
        }

        private static uint ExpandSyntheticRgb4(ushort color)
            => 0xFF00_0000u |
               ((uint)(((color >> 8) & 0x0F) * 17) << 16) |
               ((uint)(((color >> 4) & 0x0F) * 17) << 8) |
               (uint)((color & 0x0F) * 17);

        private bool IsActiveViewPort(uint viewPort)
        {
            if (viewPort == 0 ||
                _currentViewAddress == 0 ||
                !CanAddressField(_currentViewAddress, ViewViewPortOffset, sizeof(uint)))
            {
                return false;
            }

            if (!TryReadLongField(_currentViewAddress, ViewViewPortOffset, out var current))
                return false;

            var seen = new HashSet<uint>();
            for (var count = 0; current != 0 && count < MaxUserCopperLists; count++)
            {
                if (current == viewPort)
                    return true;
                if (!seen.Add(current) ||
                    !CanAddressField(current, ViewPortNextOffset, sizeof(uint)))
                {
                    return false;
                }

                if (!TryReadLongField(current, ViewPortNextOffset, out current))
                    return false;
            }

            return false;
        }

        private void DrawRastPort(M68kCpuState state)
        {
            var rastPort = state.A[1];
            if (!TryGetRastPortBitMap(rastPort, out _) ||
                !_machine.Bus.IsWritableMemoryRange(
                    rastPort + (uint)RastPortCurrentXOffset,
                    sizeof(ushort)) ||
                !_machine.Bus.IsWritableMemoryRange(
                    rastPort + (uint)RastPortCurrentYOffset,
                    sizeof(ushort)))
            {
                // The portable core rolls back a failed cursor publication;
                // keep the host fallback from drawing pixels when a readable
                // provider/image overlay masks either public cursor WORD.
                return;
            }

            var x0 = ReadSignedWordOrDefault(rastPort + RastPortCurrentXOffset, 0);
            var y0 = ReadSignedWordOrDefault(rastPort + RastPortCurrentYOffset, 0);
            var x1 = Long(state.D[0]);
            var y1 = Long(state.D[1]);
            DrawRastPortLine(rastPort, x0, y0, x1, y1, ReadRastPortFgPen(rastPort));
            _machine.Bus.WriteWord(rastPort + RastPortCurrentXOffset, unchecked((ushort)x1));
            _machine.Bus.WriteWord(rastPort + RastPortCurrentYOffset, unchecked((ushort)y1));
        }

        private void DrawRastPortText(M68kCpuState state)
        {
            var rastPort = state.A[1];
            if (!TryGetRastPortBitMap(rastPort, out _) ||
                !_machine.Bus.IsWritableMemoryRange(
                    rastPort + (uint)RastPortCurrentXOffset,
                    sizeof(ushort)))
            {
                // Text has already-rendered pixels in the fallback path only
                // if its final cursor can be committed.  A read-only cursor
                // overlay therefore keeps the operation unclaimed so the
                // native/provider owner can retry atomically.
                return;
            }

            var textAddress = state.A[0];
            var length = (int)Math.Min(state.D[0], 512u);
            if (length <= 0 || textAddress == 0 || !_machine.Bus.IsMappedMemoryRange(textAddress, length))
            {
                return;
            }

            var x = ReadSignedWordOrDefault(rastPort + RastPortCurrentXOffset, 0);
            var baseline = ReadSignedWordOrDefault(rastPort + RastPortCurrentYOffset, 0);
            var y = baseline - Math.Max(0, ReadPositiveWordOrDefault(rastPort + RastPortTextBaselineOffset, 7));
            var foreground = ReadRastPortFgPen(rastPort);
            var background = ReadRastPortBgPen(rastPort);
            var drawMode = _machine.Bus.ReadByte(rastPort + RastPortDrawModeOffset);
            for (var index = 0; index < length; index++)
            {
                var character = (char)_machine.Bus.ReadByte(textAddress + (uint)index);
                DrawRastPortGlyph(rastPort, character, x + (index * 8), y, foreground, background, drawMode);
            }

            _machine.Bus.WriteWord(
                rastPort + RastPortCurrentXOffset,
                unchecked((ushort)(short)(x + length * 8)));
        }

        private void SetRastPort(M68kCpuState state)
        {
            if (TryGetRastPortExtent(state.A[1], out var width, out var height))
            {
                FillRastPortRect(state.A[1], 0, 0, width - 1, height - 1, (int)(state.D[0] & 0xFF));
            }
        }

        private void FillRastPort(M68kCpuState state)
        {
            if (TryGetRastPortBitMap(state.A[1], out _))
            {
                FillRastPortRect(
                    state.A[1],
                    Long(state.D[0]),
                    Long(state.D[1]),
                    Long(state.D[2]),
                    Long(state.D[3]),
                    ReadRastPortFgPen(state.A[1]));
            }
        }

        private uint MakeViewPort(M68kCpuState state)
        {
            var view = state.A[0];
            var viewPort = state.A[1];
            if (view == 0 || viewPort == 0)
            {
                return unchecked((uint)CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsViewportStatuses.NoViewPortExtra);
            }

            return TryBuildViewPortCopperList(view, viewPort, out _)
                ? unchecked((uint)CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsViewportStatuses.Ok)
                : unchecked((uint)CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsViewportStatuses.NoDisplay);
        }

        private uint MergeCopperLists(M68kCpuState state)
        {
            var view = state.A[1];
            if (view == 0)
            {
                return unchecked((uint)CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsCopperOperations.MergeNoOp);
            }

            if (!TryReadLongField(view, ViewViewPortOffset, out var viewPort) ||
                viewPort == 0)
            {
                return unchecked((uint)CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsCopperOperations.MergeNoOp);
            }

            if (!TryRebuildViewPortChain(view, out var hasVisibleViewPort))
            {
                return unchecked((uint)CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsCopperOperations.MergeNoMemory);
            }

            if (!hasVisibleViewPort)
            {
                return unchecked((uint)CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsCopperOperations.MergeNoOp);
            }

            // The native MrgCop contract refreshes an already-active View
            // immediately.  Rebuilding the guest CPR/DspIns links is only
            // preparation; queue the newly resolved stream at the caller's
            // cycle so the active custom-chip presentation cannot continue
            // using the retired copper list.  Inactive Views remain
            // preparation-only and are published later by LoadView.
            if (_currentViewAddress == view &&
                !TryPublishCopperListFromView(view, state.Cycles))
            {
                // The guest chain has been rebuilt, but the active hardware
                // handoff could not resolve a usable CPR/raw stream.  Do not
                // report MCOP_OK while the custom-chip publication remains
                // on the previous stream; the caller must retain ownership
                // of this failed active-view merge for a safe retry.
                return unchecked((uint)CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsCopperOperations.MergeNoMemory);
            }

            // An active MrgCop rebuild is a public graphics.library handoff,
            // just like LoadView/RethinkDisplay.  Reclassify the selected
            // front after the copper stream is published so a stale provider
            // registration cannot outlive a standard-planar ViewPort chain.
            // PublishRtgFrontViewPort keeps genuine provider-owned bitmaps
            // selected; it only clears ownership when the guest chain proves
            // that this ViewPort is planar.
            if (_currentViewAddress == view)
                PublishRtgFrontViewPort(viewPort);

            return unchecked((uint)CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsCopperOperations.MergeOk);
        }

        private bool TryRebuildViewPortChain(uint view)
            => TryRebuildViewPortChain(view, out _);

        private bool TryRebuildViewPortChain(uint view, out bool hasVisibleViewPort)
        {
            hasVisibleViewPort = false;
            if (!TryReadLongField(view, ViewViewPortOffset, out var viewPort) ||
                viewPort == 0)
            {
                return false;
            }

            var firstViewPort = viewPort;

            // MrgCop and display rethink both consume the complete ViewPort
            // chain.  Preflight the bounded guest chain before allocating any
            // new lists so a malformed/cyclic link cannot make the host walk
            // unbounded or publish a partial tail.
            var viewPorts = new List<uint>();
            var visibleViewPorts = new List<uint>();
            var visited = new HashSet<uint>();
            for (var index = 0; index < 64 && viewPort != 0; index++)
            {
                if ((viewPort & 1u) != 0 ||
                    !visited.Add(viewPort) ||
                    !TryReadLongField(viewPort, ViewPortNextOffset, out var next))
                {
                    return false;
                }

                viewPorts.Add(viewPort);
                if (!TryReadWordField(viewPort, ViewPortModesOffset, out var viewModes))
                    return false;

                // Resident MrgCop only consumes the public Modes/Next links
                // for a hidden node and skips its RasInfo, ViewPortExtra, and
                // display-driver state entirely.  Keep malformed hidden
                // display payloads available to native/provider ownership
                // instead of rejecting an otherwise valid MCOP_NOP chain.
                if ((viewModes & CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsModeIds.ViewPortHidden) != 0)
                {
                    viewPort = next;
                    continue;
                }

                if (!_machine.Bus.IsMappedMemoryRange(
                        viewPort,
                        CopperStartGraphicsLayouts.ViewPortSize))
                    return false;

                // ExtendedModes is the public handoff for monitor/provider
                // state that the OCS/ECS compatibility builder cannot
                // project.  The graphics.library LoadView/MakeVPort paths
                // already leave nonzero values unclaimed; synthetic
                // RethinkDisplay must apply the same boundary before it
                // allocates or publishes replacement copper lists.
                if (!TryReadByteField(
                        viewPort,
                        CopperStartGraphicsLayouts.ViewPortExtendedModes,
                        out var extendedModes) ||
                    extendedModes != 0)
                {
                    return false;
                }

                // RethinkDisplay consumes the same compatibility RasInfo
                // source as MrgCop/LoadView. Reject a readable third
                // playfield before allocating replacement CPR/raw lists; a
                // null head remains legal while the screen is assembling.
                if (!TryValidateRethinkRasInfoCardinality(viewPort))
                    return false;

                visibleViewPorts.Add(viewPort);
                viewPort = next;
            }

            if (viewPort != 0)
            {
                // A non-terminating chain is treated like any other
                // malformed guest display description.  The MrgCop caller
                // receives a bounded failure status, while leaving the
                // existing view lists intact keeps the native/provider
                // boundary available for the caller.
                return false;
            }

            hasVisibleViewPort = visibleViewPorts.Count != 0;
            if (!hasVisibleViewPort)
                return true;

            // A hidden chain head may be temporarily replaced with the first
            // visible ViewPort below, and every successful merge republishes
            // the View's LOF/SHF CPR links.  Admit the complete public View
            // publication envelope before allocating or exposing any
            // replacement copper list; a readable provider/image overlay
            // must leave the current display chain available to its owner.
            if (!CanAddressField(view, ViewViewPortOffset, sizeof(uint)) ||
                !CanAddressField(view, ViewLofCprListOffset, sizeof(uint)) ||
                !CanAddressField(view, ViewShfCprListOffset, sizeof(uint)) ||
                !_machine.Bus.IsMappedMemoryRange(
                    view + (uint)ViewViewPortOffset,
                    sizeof(uint)) ||
                !_machine.Bus.IsMappedMemoryRange(
                    view + (uint)ViewLofCprListOffset,
                    sizeof(uint)) ||
                !_machine.Bus.IsMappedMemoryRange(
                    view + (uint)ViewShfCprListOffset,
                    sizeof(uint)) ||
                !_machine.Bus.IsWritableMemoryRange(
                    view + (uint)ViewViewPortOffset,
                    sizeof(uint)) ||
                !_machine.Bus.IsWritableMemoryRange(
                    view + (uint)ViewLofCprListOffset,
                    sizeof(uint)) ||
                !_machine.Bus.IsWritableMemoryRange(
                    view + (uint)ViewShfCprListOffset,
                    sizeof(uint)))
            {
                return false;
            }

            if (!TryReadLongField(view, ViewLofCprListOffset, out var originalLofCprList) ||
                !TryReadLongField(view, ViewShfCprListOffset, out var originalShfCprList))
            {
                return false;
            }

            var originalDspIns = new Dictionary<uint, uint>();
            var originalCprLists = new Dictionary<uint, uint>();
            foreach (var linkedViewPort in visibleViewPorts)
            {
                if (!TryReadLongField(linkedViewPort, ViewPortDspInsOffset, out var dspIns))
                {
                    return false;
                }

                originalDspIns[linkedViewPort] = dspIns;
                if (_compatibilityCprListByViewPort.TryGetValue(linkedViewPort, out var cprList))
                    originalCprLists[linkedViewPort] = cprList;
            }

            var rebuiltCprLists = new List<uint>();
            uint firstCprList = 0;
            // TryBuildViewPortCopperList preserves an existing View head when
            // rebuilding a non-head viewport.  If the chain head itself is
            // hidden, temporarily expose the first visible node so that its
            // freshly built CPR becomes the publication source; the original
            // guest chain head is restored after the rebuild (or by rollback).
            if (visibleViewPorts[0] != firstViewPort)
                _machine.Bus.WriteLong(view + ViewViewPortOffset, visibleViewPorts[0]);

            foreach (var linkedViewPort in visibleViewPorts)
            {
                if (!TryBuildViewPortCopperList(view, linkedViewPort, out var rebuiltCprList, retirePrevious: false))
                {
                    RollbackViewPortChainRebuild(
                        view,
                        firstViewPort,
                        viewPorts,
                        originalLofCprList,
                        originalShfCprList,
                        originalDspIns,
                        originalCprLists,
                        rebuiltCprLists);
                    return false;
                }

                rebuiltCprLists.Add(rebuiltCprList);

                if (firstCprList == 0)
                {
                    // TryBuildViewPortCopperList publishes a CPR wrapper in
                    // the View. Preserve the first node as the active list
                    // while still preparing DspIns for every linked node.
                    _ = TryReadLongField(view, ViewLofCprListOffset, out firstCprList);
                }
            }

            if (firstCprList == 0)
            {
                RollbackViewPortChainRebuild(
                    view,
                    firstViewPort,
                    viewPorts,
                    originalLofCprList,
                    originalShfCprList,
                    originalDspIns,
                    originalCprLists,
                    rebuiltCprLists);
                return false;
            }

            // The per-viewport builders own their individual DspIns streams,
            // while the View's first CPR is the hardware entry point.  Compose
            // the linked streams into that first raw list before replacing the
            // previous publication.  The compatibility buffer is deliberately
            // bounded; an oversized merge reports MCOP_NO_MEM and the full
            // transaction below restores every rebuilt wrapper.
            if (!TryMergeCompatibilityCopperLists(rebuiltCprLists))
            {
                RollbackViewPortChainRebuild(
                    view,
                    firstViewPort,
                    viewPorts,
                    originalLofCprList,
                    originalShfCprList,
                    originalDspIns,
                    originalCprLists,
                    rebuiltCprLists);
                return false;
            }

            foreach (var previousCprList in originalCprLists.Values.Distinct())
                FreeCompatibilityCprList(previousCprList);

            // The per-viewport builder writes the ViewPort link while it
            // publishes each individual DspIns wrapper. MrgCop and display
            // rethink must retain the chain head in View.ViewPort.
            _machine.Bus.WriteLong(view + ViewViewPortOffset, firstViewPort);
            _machine.Bus.WriteLong(view + ViewLofCprListOffset, firstCprList);
            _machine.Bus.WriteLong(view + ViewShfCprListOffset, firstCprList);
            return true;
        }

        private bool TryValidateRethinkRasInfoCardinality(uint viewPort)
        {
            if (!CanAddressField(viewPort, ViewPortRasInfoOffset, sizeof(uint)) ||
                !TryReadLongField(viewPort, ViewPortRasInfoOffset, out var rasInfo))
            {
                return false;
            }

            return rasInfo == 0 ||
                CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsRasterOperations
                    .ValidateViewPortForMake(_portableGraphicsMemory, viewPort);
        }

        private void RollbackViewPortChainRebuild(
            uint view,
            uint firstViewPort,
            IReadOnlyList<uint> viewPorts,
            uint originalLofCprList,
            uint originalShfCprList,
            IReadOnlyDictionary<uint, uint> originalDspIns,
            IReadOnlyDictionary<uint, uint> originalCprLists,
            IReadOnlyList<uint> rebuiltCprLists)
        {
            foreach (var rebuiltCprList in rebuiltCprLists.Distinct())
                FreeCompatibilityCprList(rebuiltCprList);

            foreach (var linkedViewPort in viewPorts)
            {
                if (originalCprLists.TryGetValue(linkedViewPort, out var originalCprList))
                    _compatibilityCprListByViewPort[linkedViewPort] = originalCprList;
                else
                    _compatibilityCprListByViewPort.Remove(linkedViewPort);

                if (originalDspIns.TryGetValue(linkedViewPort, out var originalDsp))
                    _machine.Bus.WriteLong(linkedViewPort + ViewPortDspInsOffset, originalDsp);
            }

            _machine.Bus.WriteLong(view + ViewViewPortOffset, firstViewPort);
            _machine.Bus.WriteLong(view + ViewLofCprListOffset, originalLofCprList);
            _machine.Bus.WriteLong(view + ViewShfCprListOffset, originalShfCprList);
        }

        private bool TryMergeCompatibilityCopperLists(IReadOnlyList<uint> cprLists)
        {
            if (cprLists.Count <= 1)
                return true;

            var segments = new List<(uint Address, int ByteLength, ushort DiwStart)>(cprLists.Count);
            var mergedByteLength = 0;
            foreach (var cprList in cprLists)
            {
                if (!_compatibilityCopperByCprList.TryGetValue(cprList, out var rawCopperList) ||
                    !TryGetCompatibilityCopperSpan(rawCopperList, out var byteLength, out var diwStart))
                {
                    return false;
                }

                var separatorBytes = segments.Count == 0 ? 0 : sizeof(uint);
                if (byteLength < 4 ||
                    mergedByteLength > GetCompatibilityCopperListSize() - 4 - separatorBytes - byteLength)
                {
                    return false;
                }

                mergedByteLength += separatorBytes + byteLength;
                segments.Add((rawCopperList, byteLength, diwStart));
            }

            var firstRawCopperList = segments[0].Address;
            if (!CanAddressField(firstRawCopperList, mergedByteLength + sizeof(uint), sizeof(byte)) ||
                !_machine.Bus.IsMappedMemoryRange(firstRawCopperList, mergedByteLength + sizeof(uint)) ||
                !_machine.Bus.IsWritableMemoryRange(firstRawCopperList, mergedByteLength + sizeof(uint)))
            {
                // The first raw list is the in-place merge destination.  A
                // readable ROM/image overlay may still expose the existing
                // stream for inspection, but it cannot accept the inserted
                // viewport waits, copied words, or terminator.  Decline
                // before taking the source snapshot so MrgCop's caller can
                // retain the prior CPR publication and let the provider or
                // native owner handle the mapped stream.
                return false;
            }

            // Snapshot every source word before the first write.  This keeps
            // the merge itself fail-closed if a host mapping is revoked or a
            // malformed CPR registry entry aliases the destination stream.
            var sourceWords = new List<ushort>(mergedByteLength / sizeof(ushort));
            foreach (var segment in segments)
            {
                for (var offset = 0; offset < segment.ByteLength; offset += sizeof(ushort))
                {
                    if (!TryReadWordField(segment.Address, offset, out var word))
                        return false;

                    sourceWords.Add(word);
                }
            }

            var destination = firstRawCopperList + (uint)segments[0].ByteLength;
            var sourceIndex = segments[0].ByteLength / sizeof(ushort);
            for (var segmentIndex = 1; segmentIndex < segments.Count; segmentIndex++)
            {
                var vertical = (ushort)((segments[segmentIndex].DiwStart >> 8) & 0x00FF);
                WriteCopperWait(ref destination, vertical, 0);
                for (var offset = 0;
                     offset < segments[segmentIndex].ByteLength;
                     offset += sizeof(ushort))
                {
                    _machine.Bus.WriteWord(destination, sourceWords[sourceIndex++]);
                    destination += sizeof(ushort);
                }
            }

            // The first segment already exists in the destination.  Skip its
            // snapshot words while preserving the source index for each later
            // segment copied above.
            _machine.Bus.WriteWord(destination, 0xFFFF);
            _machine.Bus.WriteWord(destination + 2, 0xFFFE);

            // The first CPR wrapper is the active View entry after the merge.
            // Its original per-viewport capacity (64 entries) is not enough
            // to describe a longer composed stream, even though the widened
            // chip span can hold it. Publish the merged capacity only after
            // the final terminator is present; the source CPR wrappers remain
            // private per-viewport records until the transaction commits.
            var mergedMaxCount = (uint)(mergedByteLength / sizeof(ushort) + 2);
            _machine.Bus.WriteWord(
                cprLists[0] + (uint)CopperStartGraphicsLayouts.CprListMaxCount,
                (ushort)Math.Min(ushort.MaxValue, mergedMaxCount));
            return true;
        }

        private bool TryGetCompatibilityCopperSpan(
            uint copperList,
            out int byteLength,
            out ushort diwStart)
        {
            byteLength = 0;
            diwStart = 0;
            if (copperList == 0 ||
                !CanAddressField(copperList, 0, GetCompatibilityCopperListSize()) ||
                !_machine.Bus.IsMappedMemoryRange(copperList, GetCompatibilityCopperListSize()) ||
                !TryReadWordField(copperList, 2, out diwStart))
            {
                return false;
            }

            for (var offset = 0; offset <= GetCompatibilityCopperListSize() - sizeof(uint); offset += sizeof(uint))
            {
                if (!TryReadWordField(copperList, offset, out var first) ||
                    !TryReadWordField(copperList, offset + sizeof(ushort), out var second))
                {
                    return false;
                }

                if (first == 0xFFFF && second == 0xFFFE)
                {
                    byteLength = offset;
                    return byteLength >= sizeof(uint);
                }
            }

            return false;
        }

        private uint HostRethinkDisplay(long cycle)
        {
            var result = 1u;
            _graphicsServices.WithMonitorStateLock(() =>
            {
                result = HostRethinkDisplayCore(cycle);
                return result == 0;
            });
            return result;
        }

        private uint HostRethinkDisplayCore(long cycle)
        {
            // CyberGraphX owns the selected RTG scanout and its ViewPort
            // publication.  A direct Intuition RethinkDisplay gateway must
            // not reinterpret that provider View as planar copper, mutate its
            // DspIns/CPR links, or replace the active provider frame while a
            // native/provider owner is still in control.
            if (IsSelectedRtgViewActive())
                return 0;

            if (_currentViewAddress == 0)
            {
                return 0;
            }

            // RethinkDisplay republishes the same public ActiView handoff as
            // LoadView after rebuilding the active chain.  Admit that LONG
            // before allocating/replacing CPR streams so a readable
            // native/provider overlay cannot leave a partially committed
            // display rethink behind.
            if (!CanPublishActiveView())
                return 1;

            var view = _currentViewAddress;
            // A View without a ViewPort can still carry an explicitly
            // prepared cprlist (the classic LoadView path).  Once a chain is
            // present, however, a failed rebuild must not replace the
            // currently published hardware list with a stale or partial
            // description.
            if (TryReadLongField(view, ViewViewPortOffset, out var viewPort) &&
                viewPort != 0)
            {
                var rebuilt = TryRebuildViewPortChain(view, out var hasVisibleViewPort);
                if (!rebuilt)
                {
                    // RethinkDisplay is a V39+ Intuition status vector. A
                    // malformed viewport chain is a reconstruction failure.
                    return 1;
                }

                if (!hasVisibleViewPort)
                {
                    // An all-hidden chain is the valid no-visible-node
                    // no-op handled by TryRebuildViewPortChain.
                    return 0;
                }
            }

            // Intuition's RethinkDisplay rebuilds the active View and then
            // performs the same display hand-off as graphics.library's
            // LoadView.  Keep the guest ActiView publication transactional:
            // a failed CPR resolution must leave both the previous hardware
            // stream and the previous active-view pointer untouched.
            if (!TryPublishCopperListFromView(view, cycle))
                return 1;

            // Intuition's rethink path is the same logical View activation as
            // graphics.library LoadView, but it reaches the host copper
            // projector directly. Refresh only the optional native ViewExtra
            // sidecars here; ActiView is published by the host path below and
            // the copper backend remains the owner of display timing.
            _ = _graphicsServices.TryRefreshNativeViewSidecars(view);
            PublishActiveView(view, cycle);
            RememberPublishedViewSignature(view);
            PublishRtgFrontViewPort(
                TryReadLongField(view, ViewViewPortOffset, out var publishedViewPort)
                    ? publishedViewPort
                    : 0);
            return 0;
        }

        /// <summary>
        /// Host-shim adapter for the argument-less Intuition RethinkDisplay
        /// vector.  A staged compatibility caller may carry its View in A1
        /// before the first publication; recover it only after proving that a
        /// readable display copper source exists.  Native/provider owners and
        /// malformed frames retain the ordinary no-op/failure boundary.
        /// </summary>
        private uint HostRethinkDisplayWithState(M68kCpuState state)
        {
            var result = 1u;
            _graphicsServices.WithMonitorStateLock(() =>
            {
                var previousView = _currentViewAddress;
                var adoptedView = false;
                if (previousView == 0 &&
                    _nativeGraphicsLibraryBase == 0 &&
                    state.A[1] != 0 &&
                    IsRethinkViewCandidate(state.A[1]))
                {
                    _currentViewAddress = state.A[1];
                    adoptedView = true;
                }

                result = HostRethinkDisplayCore(state.Cycles);
                if (adoptedView && result != 0)
                    _currentViewAddress = previousView;

                return result == 0;
            });
            return result;
        }

        private bool IsRethinkViewCandidate(uint view)
        {
            if (view == 0 ||
                (view & 1u) != 0 ||
                !TryReadLongField(view, ViewViewPortOffset, out _))
            {
                return false;
            }

            return TryResolveCopperListStartsFromView(
                view,
                out var longFrameCopperList,
                out var shortFrameCopperList) &&
                (longFrameCopperList != 0 || shortFrameCopperList != 0);
        }

        private bool TryActivateSyntheticScreenToFront(uint screen, long cycle)
        {
            // The compatibility Intuition bridge owns one standard-planar
            // Screen session.  When another owner has blanked or temporarily
            // published a different View, ScreenToFront must hand that exact
            // Screen back to the same scheduler-aware LoadView boundary that
            // OpenScreen/RethinkDisplay use; merely selecting a provider
            // viewport cannot restore the native copper stream or ActiView.
            if (screen == 0 || screen != _syntheticScreenAddress ||
                _syntheticViewAddress == 0)
            {
                return false;
            }

            // ScreenToFront is idempotent for the already-front synthetic
            // screen.  Do not enqueue a duplicate copper publication (which
            // could move an otherwise stable frame boundary) when both the
            // host view cache and the guest GfxBase sidecar already identify
            // this screen's View as active.
            if (IsSyntheticScreenAlreadyActive())
            {
                PublishRtgFrontForCurrentView();
                return true;
            }

            var state = new M68kCpuState { Cycles = cycle };
            state.A[1] = _syntheticViewAddress;
            InvalidatePublishedViewSignature();
            return TryLoadView(state);
        }

        private bool TryActivateSyntheticScreenToFrontState(
            uint screen,
            M68kCpuState callerState)
        {
            // Preserve the caller's register frame.  The graphics-side
            // LoadView boundary only needs A1 and the current cycle, while a
            // scheduler-aware host may advance the cycle field during the
            // publication.  Copy that field back only after acceptance so a
            // declined provider handoff remains transparent.
            if (callerState is null ||
                screen == 0 ||
                screen != _syntheticScreenAddress ||
                _syntheticViewAddress == 0)
            {
                return false;
            }

            // Keep the state-aware gateway just as idempotent as the legacy
            // cycle-only seam.  An already-front ScreenToFront must preserve
            // the caller's frame rather than scheduling an unnecessary
            // copper handoff through LoadView.
            if (IsSyntheticScreenAlreadyActive())
            {
                PublishRtgFrontForCurrentView();
                return true;
            }

            var activationState = new M68kCpuState
            {
                Cycles = callerState.Cycles
            };
            activationState.A[1] = _syntheticViewAddress;
            InvalidatePublishedViewSignature();
            if (!TryLoadView(activationState))
                return false;

            callerState.Cycles = activationState.Cycles;
            return true;
        }

        private bool IsSyntheticScreenAlreadyActive()
        {
            if (_syntheticViewAddress == 0 ||
                _currentViewAddress != _syntheticViewAddress)
            {
                return false;
            }

            // The host cache is authoritative when the optional native
            // GfxBase sidecar is not mapped.  If it is mapped, require the
            // guest publication to agree before claiming the no-op; a native
            // caller may have replaced ActiView behind the host bridge.
            if (!TryGetAlignedGraphicsLibraryBase(out var graphicsBase))
                return false;

            if (!TryGetGraphicsFieldAddress(
                    graphicsBase,
                    GfxBaseActiViewOffset,
                    sizeof(uint),
                    out var actiView))
            {
                return false;
            }

            return !_machine.Bus.IsMappedMemoryRange(actiView, sizeof(uint)) ||
                _machine.Bus.ReadLong(actiView) == _syntheticViewAddress;
        }

        private void InvalidatePublishedViewSignature()
        {
            _publishedViewAddress = 0;
            _publishedViewSignature = 0;
            _hasPublishedViewSignature = false;
        }

        private bool TryBuildViewPortCopperList(
            uint view,
            uint viewPort,
            out uint cprWrapper,
            bool retirePrevious = true)
        {
            cprWrapper = 0;
            // The builder publishes three View longwords and one ViewPort
            // DspIns longword only after the complete compatibility list has
            // been prepared.  Preflight those exact fields here so a host
            // entry point cannot let a high guest pointer wrap a publication
            // write into low memory after a successful projection.
            if (!CanAddressField(view, ViewViewPortOffset, sizeof(uint)) ||
                !CanAddressField(view, ViewLofCprListOffset, sizeof(uint)) ||
                !CanAddressField(view, ViewShfCprListOffset, sizeof(uint)) ||
                !CanAddressField(viewPort, ViewPortDspInsOffset, sizeof(uint)) ||
                !_machine.Bus.IsWritableMemoryRange(
                    view + (uint)ViewViewPortOffset,
                    sizeof(uint)) ||
                !_machine.Bus.IsWritableMemoryRange(
                    view + (uint)ViewLofCprListOffset,
                    sizeof(uint)) ||
                !_machine.Bus.IsWritableMemoryRange(
                    view + (uint)ViewShfCprListOffset,
                    sizeof(uint)) ||
                !_machine.Bus.IsWritableMemoryRange(
                    viewPort + (uint)ViewPortDspInsOffset,
                    sizeof(uint)))
            {
                // The builder retires an existing CPR wrapper only after
                // this admission. A readable ROM/image overlay over any
                // public publication LONG therefore declines before
                // allocation, retirement, or partial guest mutation.
                return false;
            }

            _compatibilityCprListByViewPort.TryGetValue(viewPort, out var previousCprList);

            // MakeVPort is also used to rebuild one viewport in an already
            // assembled multi-viewport View.  The per-viewport builder needs
            // a CPR wrapper for the new DspIns, but it must not temporarily
            // retarget View.ViewPort or the active LOF/SHF CPR links to the
            // viewport being rebuilt.  MrgCop owns publication of the chain
            // head; preserving an existing head here keeps a second
            // MakeVPort from disconnecting the first viewport from LoadView.
            var existingLofCprList = 0u;
            var existingShfCprList = 0u;
            var preserveViewPublication =
                TryReadLongField(view, ViewViewPortOffset, out var existingViewPort) &&
                existingViewPort != 0 &&
                existingViewPort != viewPort &&
                TryReadLongField(view, ViewLofCprListOffset, out existingLofCprList) &&
                TryReadLongField(view, ViewShfCprListOffset, out existingShfCprList);

            if (!TryCreateCopperListFromViewPort(view, viewPort, out var rawCopperList))
            {
                return false;
            }

            // Allocate the replacement wrapper before retiring the previous
            // one.  Program memory can be exhausted independently of chip
            // memory used by the raw copper stream; in that case MakeVPort
            // must return MVP_NO_DISPLAY without invalidating the prior
            // View/CPR publication.  The raw list is the only new allocation
            // at this point, so release it on the bounded allocation failure.
            uint cprList;
            try
            {
                cprList = AllocateProgramMemory(0x10);
            }
            catch (AmigaEmulationException)
            {
                FreeMemoryToMemList(rawCopperList, GetCompatibilityCopperListSize());
                return false;
            }

            if (retirePrevious && previousCprList != 0)
                FreeCompatibilityCprList(previousCprList);

            _machine.Bus.ClearMemory(cprList, 0x10);
            _machine.Bus.WriteLong(cprList + CprListStartOffset, rawCopperList);
            _machine.Bus.WriteWord(cprList + 0x08, 64);
            _machine.Bus.WriteLong(view + ViewViewPortOffset, viewPort);
            _machine.Bus.WriteLong(view + ViewLofCprListOffset, cprList);
            _machine.Bus.WriteLong(view + ViewShfCprListOffset, cprList);
            _machine.Bus.WriteLong(viewPort + ViewPortDspInsOffset, rawCopperList);
            _compatibilityCopperByCprList[cprList] = rawCopperList;
            _compatibilityCprListByViewPort[viewPort] = cprList;

            if (preserveViewPublication)
            {
                _machine.Bus.WriteLong(view + ViewViewPortOffset, existingViewPort);
                _machine.Bus.WriteLong(view + ViewLofCprListOffset, existingLofCprList);
                _machine.Bus.WriteLong(view + ViewShfCprListOffset, existingShfCprList);
            }

            // The caller owns the compatibility CPR wrapper for teardown and
            // rollback.  Its Start field remains the raw chip copper stream;
            // return the wrapper address so a chain rebuild can release the
            // complete wrapper/raw pair atomically.
            cprWrapper = cprList;
            return true;
        }

        private bool TryFreeCompatibilityViewPortCopLists(uint viewPort)
        {
            if (_compatibilityCprListByViewPort.TryGetValue(viewPort, out var cprList))
                return TryFreeCompatibilityCprList(cprList);

            return false;
        }

        private void FreeCompatibilityViewPortCopLists(uint viewPort)
            => _ = TryFreeCompatibilityViewPortCopLists(viewPort);

        private void ClearCompatibilityViewPortCopLinks(uint viewPort, long cycle)
        {
            var offsets = new[]
            {
                ViewPortDspInsOffset,
                ViewPortSprInsOffset,
                ViewPortClrInsOffset,
                ViewPortUCopInsOffset
            };
            for (var index = 0; index < offsets.Length; index++)
            {
                var offset = offsets[index];
                if (!CanAddressField(viewPort, offset, sizeof(uint)) ||
                    !_machine.Bus.IsMappedMemoryRange(viewPort + (uint)offset, sizeof(uint)) ||
                    !TryReadLongField(viewPort, offset, out _))
                {
                    return;
                }
            }

            for (var index = 0; index < offsets.Length; index++)
                _machine.Bus.WriteLong(viewPort + (uint)offsets[index], 0, cycle);
        }

        private bool TryValidateSyntheticViewPortTeardown(uint viewPort)
        {
            var offsets = new[]
            {
                ViewPortDspInsOffset,
                ViewPortSprInsOffset,
                ViewPortClrInsOffset,
                ViewPortUCopInsOffset
            };

            for (var index = 0; index < offsets.Length; index++)
            {
                var offset = offsets[index];
                if (!CanAddressField(viewPort, offset, sizeof(uint)) ||
                    !_machine.Bus.IsMappedMemoryRange(viewPort + (uint)offset, sizeof(uint)) ||
                    !_machine.Bus.IsWritableMemoryRange(viewPort + (uint)offset, sizeof(uint)) ||
                    !TryReadLongField(viewPort, offset, out var link))
                {
                    // CloseScreen clears every public copper-link slot.  A
                    // readable ROM/image overlay is not a writable guest
                    // ownership record, so decline before blanking the View
                    // or releasing the compatibility CPR wrapper.
                    return false;
                }

                if (link == 0)
                    continue;

                if (offset == ViewPortDspInsOffset &&
                    _compatibilityCprListByViewPort.TryGetValue(viewPort, out var cprList) &&
                    _compatibilityCopperByCprList.TryGetValue(cprList, out var compatibilityDspIns) &&
                    link == compatibilityDspIns)
                {
                    continue;
                }

                // Keep the historical cleanup of stale/unmapped links (the
                // synthetic close tests use that state), but never erase a
                // mapped link that another display/provider owner can inspect.
                if (_machine.Bus.IsMappedMemoryRange(link, sizeof(uint)))
                    return false;
            }

            return true;
        }

        private bool TryValidateSyntheticRasInfoTeardown(uint rasInfo)
        {
            if (rasInfo == 0 ||
                !TryReadLongField(rasInfo, RasInfoBitMapOffset, out var bitMap) ||
                bitMap != _syntheticBitMapAddress)
            {
                return false;
            }

            if (_syntheticSecondRasInfoAddress == 0)
                return true;

            return TryReadLongField(
                       _syntheticSecondRasInfoAddress,
                       RasInfoBitMapOffset,
                       out var secondBitMap) &&
                secondBitMap == _syntheticBitMapAddress &&
                TryReadLongField(
                    _syntheticSecondRasInfoAddress,
                    RasInfoNextOffset,
                    out var secondNext) &&
                secondNext == 0;
        }

        private bool TryValidateSyntheticBitMapTeardown(uint screenBitMap, uint backingBitMap)
        {
            if (screenBitMap == 0 ||
                backingBitMap == 0 ||
                !_machine.Bus.IsMappedMemoryRange(
                    screenBitMap,
                    CopperStartGraphicsLayouts.BitMapSize))
            {
                return false;
            }

            if (!TryReadWordField(screenBitMap, BitMapBytesPerRowOffset, out var screenBytesPerRow) ||
                !TryReadWordField(backingBitMap, BitMapBytesPerRowOffset, out var backingBytesPerRow) ||
                screenBytesPerRow != backingBytesPerRow ||
                !TryReadWordField(screenBitMap, BitMapRowsOffset, out var screenRows) ||
                !TryReadWordField(backingBitMap, BitMapRowsOffset, out var backingRows) ||
                screenRows != backingRows ||
                !TryReadByteField(screenBitMap, BitMapFlagsOffset, out var screenFlags) ||
                !TryReadByteField(backingBitMap, BitMapFlagsOffset, out var backingFlags) ||
                screenFlags != backingFlags ||
                !TryReadByteField(screenBitMap, BitMapDepthOffset, out var screenDepth) ||
                !TryReadByteField(backingBitMap, BitMapDepthOffset, out var backingDepth) ||
                screenDepth != backingDepth ||
                screenDepth == 0 ||
                screenDepth > 8)
            {
                return false;
            }

            if (!TryGetSyntheticBitMapHeaderSpan(
                    backingBitMap,
                    backingDepth,
                    backingFlags,
                    out _))
            {
                return false;
            }

            for (var plane = 0; plane < screenDepth; plane++)
            {
                var offset = BitMapPlanesOffset + (plane * sizeof(uint));
                if (!TryReadLongField(screenBitMap, offset, out var screenPlane) ||
                    !TryReadLongField(backingBitMap, offset, out var backingPlane) ||
                    screenPlane != backingPlane)
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryValidateSyntheticViewTeardown(uint view, uint expectedViewPort)
        {
            if (view == 0)
                return true;

            if (!TryReadLongField(view, ViewViewPortOffset, out var linkedViewPort) ||
                linkedViewPort != expectedViewPort)
            {
                // The compatibility View owns the embedded Screen ViewPort
                // link.  A repointed or unreadable link belongs to whichever
                // provider took over that View envelope.
                return false;
            }

            var offsets = new[] { ViewLofCprListOffset, ViewShfCprListOffset };
            for (var index = 0; index < offsets.Length; index++)
            {
                var offset = offsets[index];
                if (!CanAddressField(view, offset, sizeof(uint)) ||
                    !_machine.Bus.IsMappedMemoryRange(view + (uint)offset, sizeof(uint)) ||
                    !_machine.Bus.IsWritableMemoryRange(view + (uint)offset, sizeof(uint)) ||
                    !TryReadLongField(view, offset, out var link))
                {
                    // The View's CPR links are cleared after the hand-off.
                    // Keep the screen live when a provider exposes a
                    // readable but non-writable overlay over either slot.
                    return false;
                }

                if (link == 0 || _compatibilityCopperByCprList.ContainsKey(link))
                    continue;

                // Preserve the existing allowance for stale/unmapped links,
                // but never clear a mapped CPR envelope not owned by this
                // compatibility session.
                if (_machine.Bus.IsMappedMemoryRange(link, sizeof(uint)))
                    return false;
            }

            return true;
        }

        private bool TryFreeCompatibilityCprList(uint cprList)
        {
            if (!_compatibilityCopperByCprList.Remove(cprList, out var copperList))
                return false;

            _compatibilityDisplayInstructionCountByCopper.Remove(copperList);

            foreach (var pair in _compatibilityCprListByViewPort
                         .Where(pair => pair.Value == cprList)
                         .ToArray())
            {
                _compatibilityCprListByViewPort.Remove(pair.Key);
            }

            FreeMemoryToMemList(copperList, GetCompatibilityCopperListSize());
            FreeMemoryToMemList(cprList, 0x10);
            return true;
        }

        private void FreeCompatibilityCprList(uint cprList)
            => _ = TryFreeCompatibilityCprList(cprList);

        private bool TryCreateCopperListFromViewPort(uint view, uint viewPort, out uint copperList)
        {
            copperList = 0;
            if (!_machine.Bus.IsMappedMemoryRange(
                    viewPort,
                    CopperStartGraphicsLayouts.ViewPortSize))
            {
                return false;
            }

            var width = TryReadWordField(viewPort, ViewPortDWidthOffset, out var widthWord) &&
                widthWord != 0
                ? widthWord
                : 320;
            var height = TryReadWordField(viewPort, ViewPortDHeightOffset, out var heightWord) &&
                heightWord != 0
                ? heightWord
                : 256;
            var viewDx = TryReadWordField(view, ViewDxOffsetOffset, out var viewDxWord)
                ? unchecked((short)viewDxWord)
                : 0;
            var viewDy = TryReadWordField(view, ViewDyOffsetOffset, out var viewDyWord)
                ? unchecked((short)viewDyWord)
                : 0;
            var viewPortDx = TryReadWordField(viewPort, ViewPortDxOffsetOffset, out var dxWord)
                ? unchecked((short)dxWord)
                : 0;
            var viewPortDy = TryReadWordField(viewPort, ViewPortDyOffsetOffset, out var dyWord)
                ? unchecked((short)dyWord)
                : 0;
            var dx = viewDx + viewPortDx;
            var dy = viewDy + viewPortDy;
            var modes = TryReadWordField(viewPort, ViewPortModesOffset, out var modesWord)
                ? modesWord
                : (ushort)0;
            var highResolution = (modes & ViewModeHires) != 0;
            var superHires = (modes & ViewModeSuperHires) != 0;
            var maxDisplayWidth = superHires
                ? MaxCopperSuperHiresWidth
                : MaxCopperDisplayWidth;
            if (width > maxDisplayWidth || height > MaxCopperDisplayHeight)
            {
                // DDFSTOP cannot represent a fetch wider than 64 words, and
                // the OCS/ECS interlaced display envelope is at most 512
                // lines. Decline instead of clamping either dimension and
                // publishing a copper list that no longer matches the guest
                // ViewPort geometry.
                return false;
            }

            var requiredBytesPerRow = ((width + 15) / 16) * 2;
            var depth = 1;
            var bytesPerRow = Math.Max(2, requiredBytesPerRow);
            var secondBytesPerRow = bytesPerRow;
            var supportsAgaPlanar = SupportsAgaPlanarDisplay();
            var maximumPlanarDepth = supportsAgaPlanar ? 8 : 6;
            var planes = new uint[maximumPlanarDepth];
            var firstPlayfieldPlanes = new uint[maximumPlanarDepth];
            var firstPlayfieldDepth = 0;
            var secondPlayfieldDepth = 0;
            var firstPlayfieldFineScroll = 0;
            var secondPlayfieldFineScroll = 0;
            var distinctDualPlayfield = false;
            var bitMap = 0u;
            var sourceX = 0;
            var sourceY = 0;
            var rasInfo = 0u;
            var hasBitmap = TryReadLongField(viewPort, ViewPortRasInfoOffset, out rasInfo) &&
                rasInfo != 0 &&
                CanAddressField(rasInfo, RasInfoRyOffsetOffset, sizeof(ushort)) &&
                TryReadLongField(rasInfo, RasInfoBitMapOffset, out bitMap) &&
                bitMap != 0 &&
                CanAddressField(bitMap, BitMapPlanesOffset, planes.Length * sizeof(uint)) &&
                _machine.Bus.IsMappedMemoryRange(bitMap, BitMapPlanesOffset + (planes.Length * 4));
            if (hasBitmap)
            {
                ushort bitmapBytesPerRow = 0;
                ushort bitmapRowsWord = 0;
                byte bitmapDepth = 0;
                ushort rxOffsetWord = 0;
                ushort ryOffsetWord = 0;
                if (!TryReadWordField(bitMap, BitMapBytesPerRowOffset, out bitmapBytesPerRow) ||
                    !TryReadWordField(bitMap, BitMapRowsOffset, out bitmapRowsWord) ||
                    !TryReadByteField(bitMap, BitMapDepthOffset, out bitmapDepth) ||
                    !TryReadWordField(rasInfo, RasInfoRxOffsetOffset, out rxOffsetWord) ||
                    !TryReadWordField(rasInfo, RasInfoRyOffsetOffset, out ryOffsetWord))
                {
                    hasBitmap = false;
                }

                if (!hasBitmap)
                {
                    return false;
                }

                if (bitmapDepth == 0 ||
                    bitmapDepth > planes.Length ||
                    bitmapBytesPerRow < requiredBytesPerRow)
                {
                    // A standard planar row must contain every word that
                    // the display DMA fetches.  Older compatibility code
                    // clamped zero depth and short rows, which made the
                    // generated modulo describe memory outside the bitmap.
                    return false;
                }

                bytesPerRow = bitmapBytesPerRow;
                secondBytesPerRow = bytesPerRow;
                var bitmapRows = bitmapRowsWord != 0 ? bitmapRowsWord : height;
                sourceX = unchecked((short)rxOffsetWord);
                sourceY = unchecked((short)ryOffsetWord);
                if (sourceY >= 0)
                {
                    height = Math.Max(1, Math.Min(height, Math.Max(1, bitmapRows - sourceY)));
                }

                height = Math.Max(1, Math.Min(height, bitmapRows));
                depth = bitmapDepth;
                firstPlayfieldDepth = depth;
                // RxOffset is split between a coarse word pointer adjustment
                // and the four-bit PF1 fine-scroll delay in BPLCON1.  Use a
                // floor division for negative guest offsets so the fine part
                // remains in the hardware's 0..15 range.
                var sourceWordOffset = sourceX >= 0
                    ? sourceX / 16
                    : -((-sourceX + 15) / 16);
                firstPlayfieldFineScroll = sourceX - (sourceWordOffset * 16);
                var sourceByteOffset = ((long)sourceY * bytesPerRow) +
                    ((long)sourceWordOffset * 2);
                if (sourceByteOffset < int.MinValue || sourceByteOffset > int.MaxValue)
                {
                    hasBitmap = false;
                }
                else
                {
                    for (var plane = 0; plane < depth; plane++)
                    {
                        if (!TryReadLongField(
                                bitMap,
                                BitMapPlanesOffset + (plane * sizeof(uint)),
                                out var planeAddress) ||
                            !TryGetReadableCopperPlane(
                                planeAddress,
                                sourceByteOffset,
                                bytesPerRow,
                                height,
                                out var dmaPlane))
                        {
                            hasBitmap = false;
                            break;
                        }

                        firstPlayfieldPlanes[plane] = dmaPlane;
                        planes[plane] = dmaPlane;
                    }
                }

                // A dual-playfield viewport has two RasInfo nodes.  The
                // ordinary planar path uses the first node as playfield A;
                // when the second node points at a distinct bitmap, append
                // its planes as playfield B.  A shared bitmap is a common
                // compatibility construction (and is also used by the host
                // screen shim), so retain the original total-depth layout
                // instead of duplicating the same planes.
                var secondRasInfo = 0u;
                if ((modes & ViewModeDualPlayfield) != 0 &&
                    (!TryReadLongField(rasInfo, RasInfoNextOffset, out secondRasInfo) ||
                     secondRasInfo == 0))
                {
                    hasBitmap = false;
                }
                else if ((modes & ViewModeDualPlayfield) != 0)
                {
                    if (!CanAddressField(secondRasInfo, RasInfoRyOffsetOffset, sizeof(ushort)) ||
                        !TryReadLongField(secondRasInfo, RasInfoBitMapOffset, out var secondBitMap) ||
                        secondBitMap == 0 ||
                        !CanAddressField(
                            secondBitMap,
                            BitMapPlanesOffset,
                            planes.Length * sizeof(uint)))
                    {
                        hasBitmap = false;
                    }
                    else if (secondBitMap == bitMap)
                    {
                        // Shared backing is already represented by the
                        // first bitmap's complete planar depth. DPF assigns
                        // its alternating BPL slots to the two playfields,
                        // so derive their individual depths for palette and
                        // BPLCON3 PF2-offset selection without rearranging
                        // the bitmap's pointer order.
                        firstPlayfieldDepth = (depth + 1) / 2;
                        secondPlayfieldDepth = depth / 2;
                        distinctDualPlayfield = secondPlayfieldDepth != 0;
                        secondPlayfieldFineScroll = firstPlayfieldFineScroll;
                    }
                    else if (!_machine.Bus.IsMappedMemoryRange(
                                 secondBitMap,
                                 BitMapPlanesOffset + (planes.Length * 4)))
                    {
                        hasBitmap = false;
                    }
                    else
                    {
                        if (!TryReadWordField(
                                secondBitMap,
                                BitMapBytesPerRowOffset,
                                out var secondBitmapBytesPerRow) ||
                            !TryReadWordField(
                                secondBitMap,
                                BitMapRowsOffset,
                                out var secondBitmapRowsWord) ||
                            !TryReadByteField(
                                secondBitMap,
                                BitMapDepthOffset,
                                out var secondBitmapDepth) ||
                            !TryReadWordField(
                                secondRasInfo,
                                RasInfoRxOffsetOffset,
                                out var secondRxOffsetWord) ||
                            !TryReadWordField(
                                secondRasInfo,
                                RasInfoRyOffsetOffset,
                                out var secondRyOffsetWord))
                        {
                            hasBitmap = false;
                            return false;
                        }

                        if (secondBitmapDepth == 0 ||
                            secondBitmapDepth > planes.Length ||
                            secondBitmapBytesPerRow < requiredBytesPerRow)
                        {
                            return false;
                        }

                        var secondDepth = (int)secondBitmapDepth;
                        var maximumDualPlayfieldDepth = supportsAgaPlanar ? 4 : 3;
                        if (depth > maximumDualPlayfieldDepth ||
                            secondDepth > maximumDualPlayfieldDepth ||
                            depth + secondDepth > planes.Length)
                        {
                            hasBitmap = false;
                        }
                        else
                        {
                            var secondPlayfieldPlanes = new uint[maximumPlanarDepth];
                            secondBytesPerRow = secondBitmapBytesPerRow;
                            var secondBitmapRows = secondBitmapRowsWord != 0
                                ? secondBitmapRowsWord
                                : height;
                            var secondSourceX = unchecked((short)secondRxOffsetWord);
                            var secondSourceY = unchecked((short)secondRyOffsetWord);
                            if (secondSourceY >= 0)
                            {
                                height = Math.Max(
                                    1,
                                    Math.Min(
                                        height,
                                        Math.Max(1, secondBitmapRows - secondSourceY)));
                            }

                            height = Math.Max(1, Math.Min(height, secondBitmapRows));
                            var secondSourceWordOffset = secondSourceX >= 0
                                ? secondSourceX / 16
                                : -((-secondSourceX + 15) / 16);
                            secondPlayfieldFineScroll = secondSourceX - (secondSourceWordOffset * 16);
                            var secondSourceByteOffset =
                                ((long)secondSourceY * secondBytesPerRow) +
                                ((long)secondSourceWordOffset * 2);
                            if (secondSourceByteOffset < int.MinValue ||
                                secondSourceByteOffset > int.MaxValue)
                            {
                                hasBitmap = false;
                            }
                            else
                            {
                                for (var plane = 0; plane < secondDepth; plane++)
                                {
                                    if (!TryReadLongField(
                                            secondBitMap,
                                            BitMapPlanesOffset + (plane * sizeof(uint)),
                                            out var planeAddress) ||
                                        !TryGetReadableCopperPlane(
                                            planeAddress,
                                            secondSourceByteOffset,
                                            secondBytesPerRow,
                                            height,
                                            out var dmaPlane))
                                    {
                                        hasBitmap = false;
                                        break;
                                    }

                                    secondPlayfieldPlanes[plane] = dmaPlane;
                                }
                            }

                            // DPF consumes alternating BPL pointers while
                            // both playfields still have planes: BPL1/BPL3/
                            // BPL5 belong to playfield A and BPL2/BPL4/BPL6
                            // belong to playfield B.  The legal OCS/ECS
                            // distributions are asymmetric as well as
                            // symmetric (1+1, 2+1, 2+2, 3+2, 3+3), so append
                            // the remaining planes from whichever playfield
                            // is deeper after each alternating pair.
                            if (depth + secondDepth > planes.Length)
                            {
                                hasBitmap = false;
                            }
                            else
                            {
                                distinctDualPlayfield = true;
                                secondPlayfieldDepth = secondDepth;
                                Array.Clear(planes);
                                var firstPlane = 0;
                                var secondPlane = 0;
                                var outputPlane = 0;
                                while (firstPlane < depth || secondPlane < secondDepth)
                                {
                                    if (firstPlane < depth)
                                        planes[outputPlane++] = firstPlayfieldPlanes[firstPlane++];
                                    if (secondPlane < secondDepth)
                                        planes[outputPlane++] = secondPlayfieldPlanes[secondPlane++];
                                }

                                depth = outputPlane;
                            }
                        }
                    }
                }
            }

            var hasPlane = true;
            for (var plane = 0; plane < depth; plane++)
            {
                // The display copper stream must be able to fetch every
                // declared plane.  A partial plane array is not a usable
                // standard-planar viewport, even if another plane happens
                // to be mapped.
                hasPlane &= planes[plane] != 0;
            }

            if (!hasBitmap || !hasPlane)
            {
                return false;
            }

            copperList = AllocateChipProgramMemory(GetCompatibilityCopperListSize());
            var offset = copperList;
            WriteCopperMove(ref offset, 0x08E, EncodeDiwStart(dx, dy));
            WriteCopperMove(
                ref offset,
                0x090,
                EncodeDiwStop(dx, dy, highResolution ? Math.Max(16, width / 2) : width, height));
            var fetchWords = Math.Min((width + 15) / 16, MaxCopperFetchWords);
            var ddfStart = highResolution ? (ushort)0x003C : (ushort)0x0038;
            var ddfStop = highResolution
                ? (ushort)0x00D0
                : (ushort)(0x0038 + ((fetchWords - 1) * 8));
            WriteCopperMove(ref offset, 0x092, ddfStart);
            WriteCopperMove(ref offset, 0x094, ddfStop);
            var firstModulo = (short)(bytesPerRow - (fetchWords * 2));
            var secondModulo = (short)(secondBytesPerRow - (fetchWords * 2));
            WriteCopperMove(ref offset, 0x108, unchecked((ushort)firstModulo));
            WriteCopperMove(ref offset, 0x10A, unchecked((ushort)secondModulo));
            // ViewPort.Modes uses mode-ID feature bits, while BPLCON0 uses
            // the chipset control bits.  Keep the translation explicit so a
            // HAM/dual-playfield/superhires screen does not silently become
            // an ordinary lores playfield when its copper list is rebuilt.
            var bplcon0Modes = modes & (ViewModeHires | ViewModeInterlace);
            if ((modes & ViewModeSuperHires) != 0)
                bplcon0Modes |= Bplcon0SuperHires;
            if ((modes & ViewModeDualPlayfield) != 0)
                bplcon0Modes |= Bplcon0DualPlayfield;
            if ((modes & ViewModeHam) != 0)
                bplcon0Modes |= Bplcon0Ham;
            var bplcon2Modes = (modes & (ViewModeDualPlayfield | ViewModePlayfieldBitAssignment)) ==
                (ViewModeDualPlayfield | ViewModePlayfieldBitAssignment)
                ? Bplcon2Playfield2Priority
                : (ushort)0;
            var bplcon1Modes = (ushort)(firstPlayfieldFineScroll & 0x000F);
            if ((modes & ViewModeDualPlayfield) != 0)
            {
                bplcon1Modes |= (ushort)((secondPlayfieldFineScroll & 0x000F) << 4);
            }
            var encodedBitplaneDepth = (ushort)((depth & 0x7) << 12);
            if (supportsAgaPlanar && (depth & 0x8) != 0)
                encodedBitplaneDepth |= Bplcon0AgaBitplane8;
            WriteCopperMove(ref offset, 0x100, (ushort)(encodedBitplaneDepth | bplcon0Modes));
            // BPLCON1 carries the per-playfield fine-scroll delays left over
            // after the RasInfo word-address adjustment above.
            WriteCopperMove(ref offset, 0x102, bplcon1Modes);
            // PF2-first is a BPLCON2 priority selection, not a BPLCON0
            // mode feature.  Always emit the value so a subsequent ordinary
            // viewport cannot inherit a prior dual-playfield priority bit.
            WriteCopperMove(ref offset, 0x104, bplcon2Modes);
            for (var plane = 0; plane < depth; plane++)
            {
                var register = (ushort)(0x0E0 + (plane * 4));
                WriteCopperMove(ref offset, register, (ushort)(planes[plane] >> 16));
                WriteCopperMove(ref offset, (ushort)(register + 2), (ushort)planes[plane]);
            }

            if (distinctDualPlayfield)
            {
                // DPF assigns COLOR0..COLOR7 to playfield A and
                // COLOR8..COLOR15 to playfield B.  Emit only the entries
                // represented by each playfield's declared depth; the
                // transparent index remains the first color in each bank.
                var maximumDualPlayfieldColors = supportsAgaPlanar ? 16 : 8;
                var firstColorCount = Math.Clamp(1 << firstPlayfieldDepth, 2, maximumDualPlayfieldColors);
                var secondColorCount = Math.Clamp(1 << secondPlayfieldDepth, 2, maximumDualPlayfieldColors);
                if (_machine.Bus.Chipset.HasAllCapabilities(
                        AmigaChipCapabilities.AgaAliceRegisters |
                        AmigaChipCapabilities.AgaLisaRegisters))
                {
                    var agaDualPlayfieldBase = firstPlayfieldDepth == 4
                        ? AgaBplcon3DualPlayfield16ColorOffset
                        : AgaBplcon3Default;
                    var secondPlayfieldColorBase = firstPlayfieldDepth == 4 ? 16 : 8;
                    WriteCopperMove(ref offset, 0x106, agaDualPlayfieldBase);
                    WriteAgaDualPlayfieldPalettePass(
                        ref offset,
                        viewPort,
                        firstColorCount,
                        secondColorCount,
                        secondPlayfieldColorBase,
                        lowNibbles: false);
                    WriteCopperMove(ref offset, 0x106, (ushort)(agaDualPlayfieldBase | 0x0200));
                    WriteAgaDualPlayfieldPalettePass(
                        ref offset,
                        viewPort,
                        firstColorCount,
                        secondColorCount,
                        secondPlayfieldColorBase,
                        lowNibbles: true);
                    WriteCopperMove(ref offset, 0x106, agaDualPlayfieldBase);
                }
                else
                {
                    for (var color = 0; color < firstColorCount; color++)
                    {
                        WriteCopperMove(
                            ref offset,
                            (ushort)(0x180 + (color * 2)),
                            GetViewPortColor(viewPort, color));
                    }

                    for (var color = 0; color < secondColorCount; color++)
                    {
                        var colorIndex = 8 + color;
                        WriteCopperMove(
                            ref offset,
                            (ushort)(0x180 + (colorIndex * 2)),
                            GetViewPortColor(viewPort, colorIndex));
                    }
                }
            }
            else
            {
                var colorCount = Math.Clamp(1 << depth, 2, supportsAgaPlanar ? 256 : 32);
                WriteCompatibilityPalette(ref offset, viewPort, colorCount);
            }

            var displayInstructionBytes = (ulong)offset - copperList;
            if (offset < copperList ||
                displayInstructionBytes > int.MaxValue ||
                (displayInstructionBytes & 3UL) != 0)
            {
                FreeMemoryToMemList(copperList, GetCompatibilityCopperListSize());
                copperList = 0;
                return false;
            }

            var displayInstructionCount = (int)(displayInstructionBytes / 4UL);
            if (!TryAppendUserCopperInstructions(viewPort, ref offset, copperList))
            {
                FreeMemoryToMemList(copperList, GetCompatibilityCopperListSize());
                copperList = 0;
                return false;
            }

            _machine.Bus.WriteWord(offset, 0xFFFF);
            _machine.Bus.WriteWord(offset + 2, 0xFFFE);
            _compatibilityDisplayInstructionCountByCopper[copperList] = displayInstructionCount;
            return true;
        }

        private bool IsReadableCopperPlane(uint planeAddress, int bytesPerRow, int rows)
        {
            if (planeAddress == 0 || bytesPerRow <= 0 || rows <= 0)
            {
                return false;
            }

            var byteCount = (ulong)(uint)bytesPerRow * (uint)rows;
            return byteCount <= int.MaxValue &&
                _machine.Bus.IsMappedMemoryRange(planeAddress, (int)byteCount);
        }

        private bool TryGetReadableCopperPlane(
            uint planeAddress,
            long byteOffset,
            int bytesPerRow,
            int rows,
            out uint dmaPlane)
        {
            dmaPlane = 0;
            if (planeAddress == 0 ||
                (planeAddress & 1u) != 0 ||
                byteOffset < int.MinValue ||
                byteOffset > int.MaxValue ||
                bytesPerRow <= 0 ||
                rows <= 0)
            {
                return false;
            }

            var firstByte = (long)planeAddress + byteOffset;
            var byteCount = (ulong)(uint)bytesPerRow * (uint)rows;
            if (firstByte < 0 ||
                (ulong)firstByte > uint.MaxValue ||
                byteCount == 0 ||
                byteCount > int.MaxValue ||
                (ulong)firstByte + byteCount > (ulong)uint.MaxValue + 1UL)
            {
                return false;
            }

            dmaPlane = _machine.Bus.AddChipDmaPointerOffset(
                planeAddress,
                (int)byteOffset);
            return IsReadableCopperPlane(dmaPlane, bytesPerRow, rows);
        }

        /// <summary>
        /// Projects the bounded guest UCopList/CopList representation into the
        /// compatibility copper stream.  The host list is intentionally
        /// fail-closed: malformed links, unsupported pseudo-operations and
        /// capacity overflow reject the new list before any user instruction
        /// is emitted.
        /// </summary>
        private bool TryAppendUserCopperInstructions(
            uint viewPort,
            ref uint offset,
            uint copperList)
        {
            if (!TryReadLongField(
                    viewPort,
                    CopperStartGraphicsLayouts.ViewPortUCopIns,
                    out var userList) ||
                userList == 0)
            {
                return userList == 0;
            }

            var decoded = new List<(ushort Opcode, ushort Arg0, ushort Arg1)>();
            var userLists = new HashSet<uint>();
            var copLists = new HashSet<uint>();
            var currentUserList = userList;
            while (currentUserList != 0)
            {
                if (userLists.Count >= MaxUserCopperLists ||
                    !userLists.Add(currentUserList) ||
                    !TryReadLongField(
                        currentUserList,
                        CopperStartGraphicsLayouts.UCopListNext,
                        out var nextUserList) ||
                    !TryReadLongField(
                        currentUserList,
                        CopperStartGraphicsLayouts.UCopListFirstCopList,
                        out var firstCopList) ||
                    !TryReadLongField(
                        currentUserList,
                        CopperStartGraphicsLayouts.UCopListCopList,
                        out var currentCopList))
                {
                    return false;
                }

                // UCopperListInit publishes both fields.  Accept a hand-built
                // list with only CopList populated, but never silently skip a
                // non-empty list.
                var copList = firstCopList != 0 ? firstCopList : currentCopList;
                while (copList != 0)
                {
                    if (copLists.Count >= MaxUserCopperLists ||
                        !copLists.Add(copList) ||
                        !TryReadLongField(
                            copList,
                            CopperStartGraphicsLayouts.CopListNext,
                            out var nextCopList) ||
                        !TryReadLongField(
                            copList,
                            CopperStartGraphicsLayouts.CopListCopIns,
                            out var instructions) ||
                        !TryReadWordField(
                            copList,
                            CopperStartGraphicsLayouts.CopListCount,
                            out var instructionCount) ||
                        !TryReadWordField(
                            copList,
                            CopperStartGraphicsLayouts.CopListMaxCount,
                            out var maxInstructionCount) ||
                        instructionCount > maxInstructionCount ||
                        instructionCount > MaxUserCopperInstructions - decoded.Count)
                    {
                        return false;
                    }

                    if (instructionCount != 0)
                    {
                        var byteCount = (uint)instructionCount * (uint)CopperStartGraphicsLayouts.CopInsSize;
                        if (instructions == 0 ||
                            byteCount > int.MaxValue ||
                            !CanAddressField(instructions, 0, (int)byteCount) ||
                            !_machine.Bus.IsMappedMemoryRange(instructions, (int)byteCount))
                        {
                            return false;
                        }

                        for (var index = 0; index < instructionCount; index++)
                        {
                            var instruction = instructions + ((uint)index * (uint)CopperStartGraphicsLayouts.CopInsSize);
                            if (!TryReadWordField(
                                    instruction,
                                    CopperStartGraphicsLayouts.CopInsOpCode,
                                    out var opcode) ||
                                !TryReadWordField(
                                    instruction,
                                    CopperStartGraphicsLayouts.CopInsArg0,
                                    out var arg0) ||
                                !TryReadWordField(
                                    instruction,
                                    CopperStartGraphicsLayouts.CopInsArg1,
                                    out var arg1) ||
                                (opcode != 0 && opcode != 1) ||
                                (opcode == 0 && ((arg0 & 1) != 0 || arg0 > 0x01FE)))
                            {
                                return false;
                            }

                            decoded.Add((opcode, arg0, arg1));
                        }
                    }

                    copList = nextCopList;
                }

                currentUserList = nextUserList;
            }

            foreach (var instruction in decoded)
            {
                // Leave room for the terminating $FFFF/$FFFE pair.  The
                // generated compatibility list is fixed-size, so this check
                // is the final guard against guest list overrun.
                var listEnd = copperList + (uint)GetCompatibilityCopperListSize();
                if (offset < copperList || listEnd < copperList || offset > listEnd - 8)
                {
                    return false;
                }

                if (instruction.Opcode == 0)
                {
                    WriteCopperMove(ref offset, instruction.Arg0, instruction.Arg1);
                }
                else
                {
                    WriteCopperWait(ref offset, instruction.Arg0, instruction.Arg1);
                }
            }

            return true;
        }

        private ushort GetViewPortColor(uint viewPort, int index)
        {
            if (IsSyntheticViewPort(viewPort))
            {
                if (_syntheticPaletteLoaded && (uint)index < _syntheticPalette.Length)
                {
                    return _syntheticPalette[index];
                }

                return index switch
                {
                    0 => 0x000,
                    1 => 0x238,
                    _ => 0xFFF
                };
            }

            // A valid guest ColorMap is the canonical OCS/ECS source for the
            // compatibility COLOR moves.  LowColorBits preserve the extra
            // V39/RGB32 precision for portable callers, but the hardware
            // copper register consumes the high four bits of each component.
            if (TryReadLongField(
                    viewPort,
                    (int)ViewPortColorMapOffset,
                    out var colorMap) &&
                colorMap != 0 &&
                TryReadWordField(
                    colorMap,
                    CopperStartGraphicsLayouts.ColorMapCount,
                    out var colorCount) &&
                index >= 0 &&
                index < colorCount &&
                TryReadLongField(
                    colorMap,
                    CopperStartGraphicsLayouts.ColorMapColorTable,
                    out var colorTable) &&
                colorTable != 0 &&
                CanAddressField(colorTable, index * sizeof(ushort), sizeof(ushort)))
            {
                if (TryReadWordField(colorTable, index * sizeof(ushort), out var color))
                    return (ushort)(color & 0x0FFF);
            }

            return index == 0 ? (ushort)0x000 : (ushort)0xFFF;
        }

        /// <summary>
        /// Emits the standard 12-bit COLOR-register palette, or each AGA
        /// 32-colour bank as matching high/low-nibble COLOR writes.
        /// </summary>
        private void WriteCompatibilityPalette(ref uint offset, uint viewPort, int colorCount)
        {
            if (!_machine.Bus.Chipset.HasAllCapabilities(
                    AmigaChipCapabilities.AgaAliceRegisters |
                    AmigaChipCapabilities.AgaLisaRegisters))
            {
                for (var color = 0; color < colorCount; color++)
                {
                    WriteCopperMove(
                        ref offset,
                        (ushort)(0x180 + (color * 2)),
                        GetViewPortColor(viewPort, color));
                }

                return;
            }

            // In AGA BPLCON3 bit 9 selects the low colour nibbles and bits
            // 15..13 select the 32-entry COLOR register bank. Publish each
            // bank's high and low nibbles together, then restore bank zero /
            // high-nibble selection before following user copper executes.
            for (var bank = 0; bank < (colorCount + 31) / 32; bank++)
            {
                var bankStart = bank * 32;
                var bankColorCount = Math.Min(32, colorCount - bankStart);
                var bankSelect = (ushort)(AgaBplcon3Default | (bank << 13));
                WriteCopperMove(ref offset, 0x106, bankSelect);
                for (var color = 0; color < bankColorCount; color++)
                {
                    WriteCopperMove(
                        ref offset,
                        (ushort)(0x180 + (color * 2)),
                        PackAgaPaletteHighNibbles(
                            GetViewPortColorRgb8(viewPort, bankStart + color)));
                }

                WriteCopperMove(ref offset, 0x106, (ushort)(bankSelect | 0x0200));
                for (var color = 0; color < bankColorCount; color++)
                {
                    WriteCopperMove(
                        ref offset,
                        (ushort)(0x180 + (color * 2)),
                        PackAgaPaletteLowNibbles(
                            GetViewPortColorRgb8(viewPort, bankStart + color)));
                }
            }

            WriteCopperMove(ref offset, 0x106, AgaBplcon3Default);
        }

        private static ushort PackAgaPaletteHighNibbles(uint color)
            => (ushort)((((color >> 16) & 0xF0) << 4) |
                        ((color >> 8) & 0xF0) |
                        ((color & 0xF0) >> 4));

        private static ushort PackAgaPaletteLowNibbles(uint color)
            => (ushort)((((color >> 16) & 0x0F) << 8) |
                        (((color >> 8) & 0x0F) << 4) |
                        (color & 0x0F));

        private void WriteAgaDualPlayfieldPalettePass(
            ref uint offset,
            uint viewPort,
            int firstColorCount,
            int secondColorCount,
            int secondPlayfieldColorBase,
            bool lowNibbles)
        {
            for (var color = 0; color < firstColorCount; color++)
            {
                var value = GetViewPortColorRgb8(viewPort, color);
                WriteCopperMove(
                    ref offset,
                    (ushort)(0x180 + (color * 2)),
                    lowNibbles
                        ? PackAgaPaletteLowNibbles(value)
                        : PackAgaPaletteHighNibbles(value));
            }

            for (var color = 0; color < secondColorCount; color++)
            {
                var colorIndex = secondPlayfieldColorBase + color;
                var value = GetViewPortColorRgb8(viewPort, colorIndex);
                WriteCopperMove(
                    ref offset,
                    (ushort)(0x180 + (colorIndex * 2)),
                    lowNibbles
                        ? PackAgaPaletteLowNibbles(value)
                        : PackAgaPaletteHighNibbles(value));
            }
        }

        private uint GetViewPortColorRgb8(uint viewPort, int index)
        {
            if (IsSyntheticViewPort(viewPort))
            {
                if (_syntheticPaletteRgb32Loaded &&
                    (uint)index < _syntheticPaletteRgb32.Length)
                {
                    return _syntheticPaletteRgb32[index] & 0x00FF_FFFFu;
                }

                return ExpandSyntheticRgb4(GetViewPortColor(viewPort, index)) & 0x00FF_FFFFu;
            }

            if (TryReadLongField(
                    viewPort,
                    (int)ViewPortColorMapOffset,
                    out var colorMap) &&
                colorMap != 0 &&
                TryReadWordField(
                    colorMap,
                    CopperStartGraphicsLayouts.ColorMapCount,
                    out var colorCount) &&
                index >= 0 &&
                index < colorCount &&
                TryReadLongField(
                    colorMap,
                    CopperStartGraphicsLayouts.ColorMapColorTable,
                    out var colorTable) &&
                colorTable != 0 &&
                CanAddressField(colorTable, index * sizeof(ushort), sizeof(ushort)) &&
                TryReadWordField(colorTable, index * sizeof(ushort), out var high))
            {
                ushort low = 0;
                if (TryReadLongField(
                        colorMap,
                        CopperStartGraphicsLayouts.ColorMapLowColorBits,
                        out var lowColors) &&
                    lowColors != 0 &&
                    CanAddressField(lowColors, index * sizeof(ushort), sizeof(ushort)))
                {
                    _ = TryReadWordField(lowColors, index * sizeof(ushort), out low);
                }

                return ((uint)(((high >> 8) & 0x0F) << 4 | ((low >> 8) & 0x0F)) << 16) |
                       ((uint)(((high >> 4) & 0x0F) << 4 | ((low >> 4) & 0x0F)) << 8) |
                       (uint)(((high & 0x0F) << 4) | (low & 0x0F));
            }

            return index == 0 ? 0u : 0x00FF_FFFFu;
        }

        private bool IsSyntheticViewPort(uint viewPort)
        {
            return _syntheticScreenAddress != 0 &&
                viewPort == _syntheticScreenAddress + ScreenViewPortOffset;
        }

        private bool TryPublishCopperListFromView(uint view, long cycle)
        {
            if (view == 0)
            {
                return false;
            }

            if (TryResolveCopperListStartsFromView(
                    view,
                    out var longFrameCopperList,
                    out var shortFrameCopperList))
            {
                QueueCopperListLoad(
                    longFrameCopperList,
                    shortFrameCopperList,
                    cycle);
                return true;
            }

            return false;
        }

        private bool TryResolveCopperListStartsFromView(
            uint view,
            out uint longFrameCopperList,
            out uint shortFrameCopperList)
        {
            longFrameCopperList = 0;
            shortFrameCopperList = 0;
            if (!CanAddressField(view, ViewShfCprListOffset, sizeof(uint)))
            {
                return false;
            }

            if (!TryReadLongField(view, ViewLofCprListOffset, out var lofCprList) ||
                !TryReadLongField(view, ViewShfCprListOffset, out var shfCprList))
            {
                return false;
            }

            _ = TryResolveCopperListStart(lofCprList, out longFrameCopperList);
            _ = TryResolveCopperListStart(shfCprList, out shortFrameCopperList);
            if (longFrameCopperList != 0 || shortFrameCopperList != 0)
            {
                return true;
            }

            if (!TryReadLongField(view, ViewViewPortOffset, out var viewPort))
                return false;

            if (viewPort != 0 &&
                CanAddressField(viewPort, ViewPortDspInsOffset, sizeof(uint)) &&
                TryReadLongField(viewPort, ViewPortDspInsOffset, out var dspIns) &&
                TryResolveCopperListStart(dspIns, out var displayCopperList))
            {
                longFrameCopperList = displayCopperList;
                shortFrameCopperList = displayCopperList;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Identifies a View whose visible chain is backed by this host's
        /// generated compatibility CPR/raw copper registry.  The raw DspIns
        /// shape is deliberately not admitted by the portable preallocated
        /// CopList validator; keeping the ownership proof on the registry
        /// prevents native/provider copper from crossing this fallback.
        /// </summary>
        private bool IsCompatibilityLoadView(uint view)
        {
            if (view == 0 ||
                (view & 1u) != 0 ||
                !TryReadLongField(view, ViewViewPortOffset, out var viewPort))
            {
                return false;
            }

            var visited = new HashSet<uint>();
            for (var index = 0; index < MaxUserCopperLists && viewPort != 0; index++)
            {
                var current = viewPort;
                if (!visited.Add(current) ||
                    (current & 1u) != 0 ||
                    !TryReadWordField(current, ViewPortModesOffset, out var modes) ||
                    !TryReadLongField(current, ViewPortNextOffset, out viewPort))
                {
                    return false;
                }

                if ((modes & CopperMod.Amiga.CopperStart.Graphics.Portable.GraphicsModeIds.ViewPortHidden) != 0)
                    continue;

                if (!_compatibilityCprListByViewPort.TryGetValue(current, out var cprList) ||
                    !_compatibilityCopperByCprList.TryGetValue(cprList, out var rawCopperList) ||
                    !LooksLikeCopperList(rawCopperList) ||
                    !IsCopperListDisplayDmaSource(rawCopperList))
                {
                    return false;
                }

                return true;
            }

            return false;
        }

        private bool TryResolveCopperListStart(uint candidate, out uint copperList)
        {
            copperList = 0;
            if (candidate == 0)
            {
                return false;
            }

            if (TryReadLongField(candidate, CprListStartOffset, out var wrappedStart))
            {
                if (LooksLikeCopperList(wrappedStart) &&
                    IsCopperListDisplayDmaSource(wrappedStart))
                {
                    copperList = wrappedStart;
                    return true;
                }
            }

            if (LooksLikeCopperList(candidate) &&
                IsCopperListDisplayDmaSource(candidate))
            {
                copperList = candidate;
                return true;
            }

            return false;
        }

        private bool IsCopperListDisplayDmaSource(uint address)
        {
            // The first fetch is enough to classify the source before a
            // View reaches the scheduler.  The bounded recognizer has
            // already proved that every inspected word is mapped; this
            // profile hook rejects a CPU-readable fast-RAM stream while
            // keeping the host/native publication seam independent of the
            // actual copper instruction count.
            return address != 0 &&
                _guestMemory.IsDisplayDmaRange(address, sizeof(uint));
        }

        private bool LooksLikeCopperList(uint address)
        {
            if (address == 0 || !CanAddressField(address, 0, sizeof(uint)))
            {
                return false;
            }

            var sawInstruction = false;
            // Match the host-owned compatibility allocation span.  A merged
            // linked View may place its final terminator beyond the legacy
            // singleton 0x100-byte probe window, but it remains bounded by
            // the same chip-program allocation used by MrgCop.
            for (var offset = 0u; offset < GetCompatibilityCopperListSize(); offset += 4)
            {
                if (!CanAddressField(address, checked((int)offset), sizeof(uint)) ||
                    !TryReadWordField(address, checked((int)offset), out var first) ||
                    !TryReadWordField(address, checked((int)offset + 2), out var second))
                {
                    return false;
                }

                if (first == 0xFFFF && second == 0xFFFE)
                {
                    // A caller-owned preallocated graphics.library copper
                    // resource may intentionally contain only the canonical
                    // end marker.  That is a valid blank stream: LoadView
                    // still has to publish its COP1 pointer and display-DMA
                    // handoff without requiring a synthesized instruction.
                    return true;
                }

                if (first == 0 && second == 0)
                {
                    return false;
                }

                // WAIT instructions carry the mask in the second word; the
                // vertical beam value in the first word is not a MOVE
                // register and may legitimately exceed $01FE.
                if (second == 0xFFFE)
                {
                    sawInstruction = true;
                    continue;
                }

                sawInstruction = true;
                if ((first & 1) == 0 && first > 0x01FE)
                {
                    return false;
                }
            }

            return sawInstruction;
        }

        private void LoadCopperList(uint copperList, long cycle)
        {
            cycle = Math.Max(cycle, _machine.Bus.CausalBusExecutor.ExecutedThroughCycle + 1);
            // A NULL LoadView is a display hand-off, not a request to turn
            // every display DMA channel back on.  The native routine leaves
            // the existing DMA enables alone; in particular sprite DMA must
            // continue after LoadView(NULL), while a provider/native owner
            // may have intentionally left bitplane DMA disabled.  A concrete
            // copper stream still needs the normal master/copper/bitplane
            // enables used by the synthetic planar presentation.
            if (copperList != 0)
                _machine.Bus.WriteWord(0x00DFF096, 0x8380, cycle);
            _machine.Bus.WriteWord(0x00DFF080, (ushort)(copperList >> 16), cycle);
            _machine.Bus.WriteWord(0x00DFF082, (ushort)copperList, cycle);
            _machine.Bus.WriteWord(0x00DFF088, 0, cycle);
        }

        private void QueueCopperListLoad(uint copperList, long cycle)
            => QueueCopperListLoad(copperList, copperList, cycle);

        private void QueueCopperListLoad(
            uint longFrameCopperList,
            uint shortFrameCopperList,
            long cycle)
        {
            var normalizedCycle = Math.Max(0, cycle);

            // Presentation frames may be finalized ahead of the CPU state
            // supplied by a host trap (for example, a later LoadView(NULL)
            // after a frame was rendered).  Never schedule a custom-register
            // write behind that immutable display horizon; advance the
            // request to the first cycle after it and let the normal frame
            // boundary rule decide whether it can publish immediately.
            var finalizedPresentationThrough = _machine.Bus.Display.LiveFinalizedPresentationThroughCycle;
            if (normalizedCycle <= finalizedPresentationThrough)
                normalizedCycle = finalizedPresentationThrough + 1;

            if (IsFrameBoundary(normalizedCycle))
            {
                CancelPendingCopperList();
                LoadCopperList(
                    SelectCopperListForFrame(
                        longFrameCopperList,
                        shortFrameCopperList,
                        normalizedCycle),
                    normalizedCycle);
                return;
            }

            _pendingCopperList = longFrameCopperList;
            _pendingLongFrameCopperList = longFrameCopperList;
            _pendingShortFrameCopperList = shortFrameCopperList;
            _pendingCopperListValid = true;
            _pendingCopperListHasFieldVariants =
                longFrameCopperList != shortFrameCopperList;
            _pendingCopperListCycle = _machine.Bus.GetNextFrameStartCycle(normalizedCycle);
        }

        private void CancelPendingCopperList()
        {
            _pendingCopperList = 0;
            _pendingLongFrameCopperList = 0;
            _pendingShortFrameCopperList = 0;
            _pendingCopperListValid = false;
            _pendingCopperListHasFieldVariants = false;
            _pendingCopperListCycle = -1;
        }

        private bool IsFrameBoundary(long cycle)
        {
            cycle = Math.Max(0, cycle);
            return _machine.Bus.GetBeamPosition(cycle).FrameStartCycle == cycle;
        }

        private void ApplyPendingCopperListAtFrameBoundary(long currentCycle)
        {
            if (!_pendingCopperListValid || _pendingCopperListCycle < 0 || currentCycle < _pendingCopperListCycle)
            {
                return;
            }

            var copperList = _pendingCopperList;
            if (_pendingCopperListHasFieldVariants)
            {
                copperList = SelectCopperListForFrame(
                    _pendingLongFrameCopperList,
                    _pendingShortFrameCopperList,
                    _pendingCopperListCycle);
            }
            var publishCycle = _pendingCopperListCycle;
            _pendingCopperList = 0;
            _pendingLongFrameCopperList = 0;
            _pendingShortFrameCopperList = 0;
            _pendingCopperListValid = false;
            _pendingCopperListHasFieldVariants = false;
            _pendingCopperListCycle = -1;
            LoadCopperList(copperList, publishCycle);
        }

        private uint SelectCopperListForFrame(
            uint longFrameCopperList,
            uint shortFrameCopperList,
            long cycle)
        {
            if (longFrameCopperList == 0)
                return shortFrameCopperList;

            if (shortFrameCopperList == 0 ||
                shortFrameCopperList == longFrameCopperList)
            {
                return longFrameCopperList;
            }

            return _machine.Bus.GetBeamPosition(Math.Max(0, cycle)).IsLongFrame
                ? longFrameCopperList
                : shortFrameCopperList;
        }

        private void WriteCopperMove(ref uint offset, ushort register, ushort value)
        {
            _machine.Bus.WriteWord(offset, (ushort)(register & 0x01FE));
            _machine.Bus.WriteWord(offset + 2, value);
            offset += 4;
        }

        private void WriteCopperWait(ref uint offset, ushort vertical, ushort horizontal)
        {
            _machine.Bus.WriteWord(
                offset,
                (ushort)(((vertical & 0x00FF) << 8) | (horizontal & 0x00FE)));
            _machine.Bus.WriteWord(offset + 2, 0xFFFE);
            offset += 4;
        }

        private int ReadPositiveWordOrDefault(uint address, int defaultValue)
        {
            return TryReadWord(address, out var value) && value != 0 ? value : defaultValue;
        }

        private byte ReadSyntheticPenByte(uint address, byte defaultValue)
        {
            if (!_machine.Bus.IsMappedMemoryRange(address, 1))
                return defaultValue;

            var value = _machine.Bus.ReadByte(address);
            return value == 0xFF ? defaultValue : value;
        }

        private bool TryReadLong(uint address, out uint value)
        {
            // All LONG reads at this host/guest boundary model a native
            // 68000 structure or vector field.  A byte-addressable odd
            // mapping is not a legal way to satisfy that access: preserving
            // the alignment fault/ownership boundary lets a native or
            // provider implementation handle the request instead of
            // interpreting a shifted longword through the host bridge.
            if ((address & 1u) != 0 ||
                !_machine.Bus.IsMappedMemoryRange(address, 4))
            {
                value = 0;
                return false;
            }

            value = _machine.Bus.ReadLong(address);
            return true;
        }

        private bool TryReadLongField(uint baseAddress, int offset, out uint value)
        {
            value = 0;
            if ((baseAddress & 1u) != 0 ||
                !CanAddressField(baseAddress, offset, sizeof(uint)))
            {
                return false;
            }

            return TryReadLong(baseAddress + (uint)offset, out value);
        }

        private bool TryReadWordField(uint baseAddress, int offset, out ushort value)
        {
            value = 0;
            if ((baseAddress & 1u) != 0 ||
                !CanAddressField(baseAddress, offset, sizeof(ushort)))
                return false;

            return TryReadWord(baseAddress + (uint)offset, out value);
        }

        private bool TryReadByteField(uint baseAddress, int offset, out byte value)
        {
            value = 0;
            // Although the selected field is byte-sized, its containing
            // public structure is still a native 68000 envelope.  Refuse an
            // odd base before a byte field can make a shifted structure look
            // valid to the host lifecycle bridge.
            if ((baseAddress & 1u) != 0 ||
                !CanAddressField(baseAddress, offset, sizeof(byte)))
                return false;

            var address = baseAddress + (uint)offset;
            if (!_machine.Bus.IsMappedMemoryRange(address, sizeof(byte)))
                return false;

            value = _machine.Bus.ReadByte(address);
            return true;
        }

        private static bool CanAddressField(uint baseAddress, int offset, int byteCount)
        {
            if (offset < 0 || byteCount <= 0)
                return false;

            var end = (ulong)(uint)offset + (uint)byteCount;
            return end <= (ulong)uint.MaxValue + 1UL &&
                (ulong)baseAddress + end <= (ulong)uint.MaxValue + 1UL;
        }

        private bool TryReadWord(uint address, out ushort value)
        {
            // WORD fields use the same 68000 alignment boundary as LONG
            // fields.  Keep byte reads available for genuinely byte-packed
            // fields, but never claim a shifted guest word for the host
            // lifecycle bridge.
            if ((address & 1u) != 0 ||
                !_machine.Bus.IsMappedMemoryRange(address, 2))
            {
                value = 0;
                return false;
            }

            value = _machine.Bus.ReadWord(address);
            return true;
        }

        private static ushort EncodeDiwStart(int dx, int dy)
        {
            var hStart = Math.Clamp(0x81 + dx, 0, 0xFF);
            var vStart = Math.Clamp(0x2C + dy, 0, 0xFF);
            return (ushort)((vStart << 8) | hStart);
        }

        private static ushort EncodeDiwStop(int dx, int dy, int width, int height)
        {
            var hStart = Math.Clamp(0x81 + dx, 0, 0xFF);
            var vStart = Math.Clamp(0x2C + dy, 0, 0xFF);
            var hStop = Math.Clamp(hStart + Math.Max(16, width), 0x100, 0x1FF);
            var vStop = vStart + Math.Max(1, height);
            return (ushort)(((vStop & 0xFF) << 8) | (hStop & 0xFF));
        }

        private uint EnsureWorkbenchDiskObject()
        {
            if (_workbenchDiskObjectAddress != 0)
            {
                return _workbenchDiskObjectAddress;
            }

            var defaultToolAddress = WriteProgramString(_workbenchDefaultToolPath);
            var toolTypeArrayAddress = AllocateProgramMemory((_workbenchToolTypes.Count + 1) * 4);
            for (var i = 0; i < _workbenchToolTypes.Count; i++)
            {
                var toolTypeAddress = WriteProgramString(_workbenchToolTypes[i]);
                _machine.Bus.WriteLong(toolTypeArrayAddress + (uint)(i * 4), toolTypeAddress);
            }

            _machine.Bus.WriteLong(toolTypeArrayAddress + (uint)(_workbenchToolTypes.Count * 4), 0);

            _workbenchDiskObjectAddress = AllocateProgramMemory(0x50);
            _machine.Bus.WriteWord(_workbenchDiskObjectAddress, 0xE310);
            _machine.Bus.WriteWord(_workbenchDiskObjectAddress + 2, 1);
            _machine.Bus.WriteLong(_workbenchDiskObjectAddress + 0x34, defaultToolAddress);
            _machine.Bus.WriteLong(_workbenchDiskObjectAddress + 0x38, toolTypeArrayAddress);
            _machine.Bus.WriteLong(_workbenchDiskObjectAddress + 0x4C, (uint)Math.Max(1, _workbenchStackSize));
            return _workbenchDiskObjectAddress;
        }

        private uint EnsureSyntheticScreen()
        {
            if (_syntheticScreenConfigurationRejected)
            {
                return 0;
            }

            if (_syntheticScreenAddress != 0)
            {
                return _syntheticScreenAddress;
            }

            // Keep the complete public Screen prefix through DetailPen and
            // BlockPen available to native-style callers.  The embedded
            // ViewPort/RastPort/BitMap remain at their classic offsets.
            try
            {
                _syntheticScreenAddress = AllocateProgramMemory(0x160);
            }
            catch (AmigaEmulationException)
            {
                // OpenScreen is a public allocation boundary.  The boot
                // program allocator throws for its own mandatory startup
                // objects, but a compatibility Screen must report the
                // classic NULL result when its guest envelope cannot be
                // staged; do not leak that host exception through Intuition.
                _syntheticScreenAddress = 0;
                _syntheticScreenOpenErrorCode = OpenScreenErrorNoMemory;
                ResetSyntheticScreenRequestState();
                return 0;
            }
            if (_syntheticScreenAddress == 0)
            {
                return EnsureSyntheticHostObject();
            }

            // The synthetic screen owns the complete public Screen prefix and
            // its embedded ViewPort/RastPort/BitMap envelopes.  AllocMem does
            // not imply MEMF_CLEAR, so initialize every byte before publishing
            // the selected fields below; otherwise a reused guest region can
            // retain stale copper links, window pointers, or bitmap flags.
            _machine.Bus.ClearMemory(_syntheticScreenAddress, 0x160);
            _machine.Bus.WriteWord(
                _syntheticScreenAddress + ScreenLeftEdgeOffset,
                unchecked((ushort)(short)_syntheticScreenLeft));
            _machine.Bus.WriteWord(
                _syntheticScreenAddress + ScreenTopEdgeOffset,
                unchecked((ushort)(short)_syntheticScreenTop));
            _machine.Bus.WriteWord(_syntheticScreenAddress + ScreenWidthOffset, (ushort)_syntheticScreenWidth);
            _machine.Bus.WriteWord(_syntheticScreenAddress + ScreenHeightOffset, (ushort)_syntheticScreenHeight);
            _machine.Bus.WriteWord(_syntheticScreenAddress + ScreenFlagsOffset, _syntheticScreenFlags);
            _machine.Bus.WriteLong(_syntheticScreenAddress + ScreenTitleOffset, 0);
            _machine.Bus.WriteLong(
                _syntheticScreenAddress + ScreenDefaultTitleOffset,
                _syntheticScreenDefaultTitleAddress);
            _machine.Bus.WriteByte(_syntheticScreenAddress + ScreenDetailPenOffset, _syntheticScreenDetailPen, 0);
            _machine.Bus.WriteByte(_syntheticScreenAddress + ScreenBlockPenOffset, _syntheticScreenBlockPen, 0);
            InitializeSyntheticViewPort(GetSyntheticScreenViewPortAddress());
            try
            {
                // Screen.Font is the caller-visible TextAttr* from the
                // NewScreen/SA_Font request. When omitted, publish a small
                // reset-scoped guest TextAttr that mirrors the synthetic
                // opened TextFont, keeping native callers on the canonical
                // 68k Screen layout without taking ownership of a supplied
                // pointer.
                var screenFont = _syntheticScreenFontAttrAddress != 0
                    ? _syntheticScreenFontAttrAddress
                    : EnsureSyntheticScreenFontAttr();
                if (screenFont == 0 ||
                    !_machine.Bus.IsMappedMemoryRange(screenFont, CopperStartGraphicsLayouts.TextAttrSize))
                {
                    _syntheticScreenOpenErrorCode = OpenScreenErrorNoMemory;
                    RollbackSyntheticScreenConstruction();
                    return 0;
                }

                _machine.Bus.WriteLong(
                    _syntheticScreenAddress + ScreenFontOffset,
                    screenFont);
                if (EnsureSyntheticScreenTextFont() == 0)
                {
                    _syntheticScreenOpenErrorCode = OpenScreenErrorNoMemory;
                    RollbackSyntheticScreenConstruction();
                    return 0;
                }
                if (!EnsureSyntheticScreenBitmap())
                {
                    // A Screen is not publishable until its backing bitmap
                    // and viewport RasInfo link are complete.  The bitmap
                    // builder already releases provider-owned storage and
                    // clears its guest links; retire this staged Screen as
                    // one atomic lifecycle unit so OpenScreen returns the
                    // classic NULL failure instead of exposing a half-built
                    // display chain.
                    RollbackSyntheticScreenConstruction();
                    if (_syntheticScreenOpenErrorCode == 0)
                        _syntheticScreenOpenErrorCode = OpenScreenErrorNoMemory;
                    return 0;
                }
                // OpenScreen initializes every standard-planar viewport with
                // a ColorMap, even when the caller did not provide
                // SA_Colors, SA_Colors32, SA_ColorMapEntries, or
                // SA_VideoControl.  Keep the allocation in the same
                // transaction as the bitmap and RasInfo publication so a
                // default screen exposes the classic ColorMap link and a
                // late allocation failure still rolls back the whole open.
                if (!EnsureSyntheticScreenColorMap())
                {
                    RollbackSyntheticScreenConstruction();
                    if (_syntheticScreenOpenErrorCode == 0)
                        _syntheticScreenOpenErrorCode = OpenScreenErrorNoMemory;
                    return 0;
                }
                var syntheticWindow = EnsureSyntheticWindow();
                // EnsureSyntheticWindow may return a host-only sentinel when
                // its guest envelope cannot be allocated.  Never publish that
                // sentinel as a guest Screen.FirstWindow link: it is not a
                // valid Window and would make the public ownership chain
                // impossible to close or traverse from 68k code.
                _machine.Bus.WriteLong(
                    _syntheticScreenAddress + ScreenFirstWindowOffset,
                    syntheticWindow != 0 && syntheticWindow == _syntheticWindowAddress
                        ? syntheticWindow
                        : 0);
                EnsureSyntheticView();
            }
            catch (AmigaEmulationException)
            {
                // Later guest envelopes (RasInfo, BitMap, RastPort, Window,
                // or View) are also recoverable OpenScreen allocations. The
                // startup allocator's exception is translated here, after
                // clearing every staged compatibility handle, so a retry
                // cannot observe a half-built screen session.
                RollbackSyntheticScreenConstruction();
                _syntheticScreenOpenErrorCode = OpenScreenErrorNoMemory;
                return 0;
            }
            return _syntheticScreenAddress;
        }

        private void RollbackSyntheticScreenConstruction()
        {
            FreeSyntheticScreenColorMap();
            if (_cyberGraphics != null &&
                _syntheticScreenAddress != 0 &&
                CyberGraphics.TryGetBitMapSurface(
                    _syntheticScreenAddress + ScreenBitMapOffset,
                    out var surface))
            {
                RollbackSyntheticScreenBitmap(surface);
            }
            else
            {
                RollbackSyntheticScreenBitmap();
            }

            _syntheticScreenAddress = 0;
            _syntheticScreenConfigurationRejected = false;
            _syntheticWindowConfigurationRejected = false;
            _syntheticWindowAddress = 0;
            _syntheticUiDisplay.WindowOpen = false;
            _syntheticUiDisplay.WindowOpenCount = 0;
            _syntheticViewAddress = 0;
            _syntheticRastPortAddress = 0;
            _syntheticBitMapAddress = 0;
            _syntheticRasInfoAddress = 0;
            _syntheticSecondRasInfoAddress = 0;
            _syntheticPlaneAddress = 0;
            _syntheticUiDisplay.WindowUsesDefaultFont = false;
            _syntheticScreenVideoControlTags = 0;
            _syntheticScreenColorMapEntries = 0;
            // Configuration is staged before the guest Screen envelope is
            // allocated.  A failed public OpenScreen request must not leave
            // its width/mode/palette/font selection in the reset-scoped
            // compatibility state for a later, unrelated retry.
            ResetSyntheticScreenRequestState();
        }

        private bool EnsureSyntheticScreenColorMap()
        {
            if (_syntheticScreenAddress == 0)
            {
                return true;
            }

            // A CyberGraphX-backed synthetic bitmap owns its palette through
            // the RTG provider.  Keep this standard-planar ColorMap unit
            // transparent for that surface; the provider's own screen patch
            // remains responsible for any chunky/RTG palette object.
            if (_syntheticBitMapAddress != 0 &&
                _cyberGraphics?.TryGetBitMapSurface(_syntheticBitMapAddress, out _) == true)
            {
                return true;
            }

            if (_syntheticColorMapAddress != 0)
            {
                return true;
            }

            var entries = _syntheticScreenColorMapEntries;
            if (entries == 0)
            {
                var depth = Math.Clamp(_syntheticScreenDepth, 1, 8);
                // OpenScreen's default is 1<<depth, but never less than the
                // classic 32-entry preference palette.  SA_ColorMapEntries
                // remains the explicit escape hatch for a caller that needs
                // a different map size.
                entries = Math.Max(32u, 1u << depth);
            }

            var mapState = new M68kCpuState { D = { [0] = entries } };
            _graphicsServices.GetColorMap(mapState);
            var colorMap = mapState.D[0];
            if (colorMap == 0)
            {
                _syntheticScreenOpenErrorCode = OpenScreenErrorNoMemory;
                return false;
            }

            var viewPort = _syntheticScreenAddress + ScreenViewPortOffset;
            if (!_machine.Bus.IsMappedMemoryRange(
                    viewPort + (uint)CopperStartGraphicsLayouts.ViewPortColorMap,
                    sizeof(uint)) ||
                !_machine.Bus.IsMappedMemoryRange(
                    colorMap + (uint)CopperStartGraphicsLayouts.ColorMapViewPort,
                    sizeof(uint)))
            {
                var freeState = new M68kCpuState { A = { [0] = colorMap } };
                _graphicsServices.FreeColorMap(freeState);
                _syntheticScreenOpenErrorCode = OpenScreenErrorNoMemory;
                return false;
            }

            _machine.Bus.WriteLong(
                viewPort + (uint)CopperStartGraphicsLayouts.ViewPortColorMap,
                colorMap);
            _machine.Bus.WriteLong(
                colorMap + (uint)CopperStartGraphicsLayouts.ColorMapViewPort,
                viewPort);
            _syntheticColorMapAddress = colorMap;
            return true;
        }

        private void FreeSyntheticScreenColorMap()
        {
            var colorMap = _syntheticColorMapAddress;
            if (colorMap == 0)
                return;

            // The synthetic map is a public Exec allocation.  Clear its
            // header before returning it so a later un-cleared AllocMem block
            // cannot inherit stale ColorMap fields (for example as a
            // NewScreen prefix) from this compatibility session.  The map is
            // already proven to be ours here, so this does not cross a
            // provider-owned guest boundary.
            if (_machine.Bus.IsMappedMemoryRange(
                    colorMap,
                    CopperStartGraphicsLayouts.ColorMapSize))
            {
                _machine.Bus.ClearMemory(
                    colorMap,
                    CopperStartGraphicsLayouts.ColorMapSize);
            }

            var freeState = new M68kCpuState { A = { [0] = colorMap } };
            _graphicsServices.FreeColorMap(freeState);
            _syntheticColorMapAddress = 0;
        }

        private void ResetSyntheticScreenRequestState()
        {
            RollbackSyntheticScreenPaletteTransaction();
            _syntheticWindowScreenTargetAddress = 0;

            // The default compatibility TextFont is shared by future screen
            // requests, so preserve that one persistent handle while
            // resetting every per-screen/public request field.
            var defaultFont = _syntheticFontAddress;
            ReleaseSyntheticScreenTextFont();
            _syntheticUiDisplay.Reset();
            _syntheticFontAddress = defaultFont;
        }

        private uint GetSyntheticScreenViewPortAddress()
        {
            // EnsureSyntheticScreen may return the host-only compatibility
            // sentinel when the guest Screen envelope cannot be allocated.
            // That sentinel is deliberately not a public Screen address; do
            // not add the embedded ViewPort offset to it and accidentally
            // expose a fabricated/odd ViewPort to graphics.library callers.
            var screen = EnsureSyntheticScreen();
            if (_syntheticScreenAddress == 0 || screen != _syntheticScreenAddress ||
                !CanAddressField(screen, ScreenViewPortOffset, CopperStartGraphicsLayouts.ViewPortSize))
            {
                return 0;
            }

            return screen + (uint)ScreenViewPortOffset;
        }

        private uint GetSyntheticWindowViewPortAddress(uint window)
        {
            // ViewPortAddress is a Window*-based query.  Keep the synthetic
            // host claim narrow: only the Window-shaped object created by the
            // compatibility Intuition owner may expose its Screen viewport;
            // foreign or malformed windows remain available to native or
            // provider ownership.
            if (window == 0 || window != _syntheticWindowAddress ||
                _syntheticScreenAddress == 0 ||
                !TryReadLongField(window, WindowWScreenOffset, out var screen) ||
                screen != _syntheticScreenAddress ||
                screen > uint.MaxValue - (uint)ScreenViewPortOffset)
            {
                return 0;
            }

            return screen + (uint)ScreenViewPortOffset;
        }

        private uint EnsureSyntheticView()
        {
            if (_syntheticViewAddress != 0)
            {
                return _syntheticViewAddress;
            }

            _syntheticViewAddress = AllocateProgramMemory(0x20);
            if (_syntheticViewAddress == 0)
            {
                return EnsureSyntheticHostObject();
            }

            _machine.Bus.ClearMemory(_syntheticViewAddress, CopperStartGraphicsLayouts.ViewSize);
            _machine.Bus.WriteLong(_syntheticViewAddress + ViewViewPortOffset, GetSyntheticScreenViewPortAddress());
            _machine.Bus.WriteWord(
                _syntheticViewAddress + ViewDyOffsetOffset,
                unchecked((ushort)ViewDefaultDyOffset));
            _machine.Bus.WriteWord(
                _syntheticViewAddress + ViewDxOffsetOffset,
                unchecked((ushort)ViewDefaultDxOffset));
            _currentViewAddress = _syntheticViewAddress;
            return _syntheticViewAddress;
        }

        private void InitializeSyntheticViewPort(uint viewPort)
        {
            _machine.Bus.ClearMemory(viewPort, CopperStartGraphicsLayouts.ViewPortSize);
            _machine.Bus.WriteWord(viewPort + ViewPortDWidthOffset, (ushort)_syntheticScreenWidth);
            _machine.Bus.WriteWord(viewPort + ViewPortDHeightOffset, (ushort)_syntheticScreenHeight);
            _machine.Bus.WriteWord(
                viewPort + ViewPortDxOffsetOffset,
                unchecked((ushort)(short)_syntheticScreenLeft));
            _machine.Bus.WriteWord(
                viewPort + ViewPortDyOffsetOffset,
                unchecked((ushort)(short)_syntheticScreenTop));
            _machine.Bus.WriteWord(viewPort + ViewPortModesOffset, _syntheticScreenViewModes);
            _machine.Bus.WriteByte(
                viewPort + CopperStartGraphicsLayouts.ViewPortSpritePriorities,
                CopperStartGraphicsLayouts.ViewPortDefaultSpritePriorities,
                0);
        }

        private bool EnsureSyntheticScreenBitmap()
        {
            if (_syntheticScreenAddress == 0)
            {
                _ = EnsureSyntheticScreen();
                return _syntheticPlaneAddress != 0;
            }

            if (_syntheticPlaneAddress != 0)
            {
                return true;
            }

            if (_syntheticCustomBitMapAddress != 0)
            {
                // SA_BitMap is borrowed storage.  Validate it again at the
                // allocation boundary because the caller may have edited the
                // header between tag parsing and OpenScreen finalization.
                if (!TryValidateSyntheticCustomBitMap(_syntheticCustomBitMapAddress) ||
                    !TryReadWordField(
                        _syntheticCustomBitMapAddress,
                        BitMapBytesPerRowOffset,
                        out var customBytesPerRow) ||
                    !TryReadWordField(
                        _syntheticCustomBitMapAddress,
                        BitMapRowsOffset,
                        out var customRows) ||
                    !TryReadByteField(
                        _syntheticCustomBitMapAddress,
                        BitMapDepthOffset,
                        out var customDepth) ||
                    (uint)customBytesPerRow * 8u < (uint)_syntheticScreenWidth ||
                    customRows < _syntheticScreenHeight ||
                    customDepth < _syntheticScreenDepth)
                {
                    _syntheticScreenOpenErrorCode = OpenScreenErrorNoMemory;
                    return false;
                }

                _syntheticRasInfoAddress = AllocateProgramMemory(0x10);
                if (_syntheticRasInfoAddress == 0 ||
                    !CopySyntheticBitMapHeader(
                        _syntheticCustomBitMapAddress,
                        _syntheticScreenAddress + ScreenBitMapOffset))
                {
                    _syntheticScreenOpenErrorCode = OpenScreenErrorNoMemory;
                    RollbackSyntheticScreenBitmap();
                    return false;
                }

                _syntheticBitMapAddress = _syntheticCustomBitMapAddress;
                _syntheticPlaneAddress = _machine.Bus.ReadLong(
                    _syntheticCustomBitMapAddress + BitMapPlanesOffset);
                if (!InitializeSyntheticRasInfoChain(_syntheticBitMapAddress))
                {
                    _syntheticScreenOpenErrorCode = OpenScreenErrorNoMemory;
                    RollbackSyntheticScreenBitmap();
                    return false;
                }

                _machine.Bus.WriteLong(
                    GetSyntheticScreenViewPortAddress() + ViewPortRasInfoOffset,
                    _syntheticRasInfoAddress);
                InitializeSyntheticRastPort(
                    _syntheticScreenAddress + ScreenRastPortOffset,
                    GetSyntheticScreenBitMapAddress(),
                    EnsureSyntheticScreenTextFont());
                _ = EnsureSyntheticRastPort();
                if (CanRenderSyntheticScreenTitle())
                    RenderSyntheticScreenTitle("Loading");
                return true;
            }

            if (_cyberGraphics?.RtgDevice?.IsAvailable == true)
            {
                var surface = CyberGraphics.AllocateRtgSurface(
                    _syntheticScreenWidth,
                    _syntheticScreenHeight,
                    CyberGraphicsPixelFormat.Lut8);
                if (surface == null)
                {
                    _syntheticScreenOpenErrorCode = OpenScreenErrorNoMemory;
                    return false;
                }

                _syntheticPlaneAddress = surface.GuestBaseAddress;
                _syntheticRasInfoAddress = AllocateProgramMemory(0x10);
                _syntheticBitMapAddress = AllocateProgramMemory(BitMapPlanesOffset + 8 * 4);
                if (_syntheticRasInfoAddress == 0 || _syntheticBitMapAddress == 0)
                {
                    _syntheticScreenOpenErrorCode = OpenScreenErrorNoMemory;
                    RollbackSyntheticScreenBitmap(surface);
                    return false;
                }

                WriteRtgBitMap(_syntheticBitMapAddress, surface);
                WriteRtgBitMap(_syntheticScreenAddress + ScreenBitMapOffset, surface);
                if (!InitializeSyntheticRasInfoChain(_syntheticBitMapAddress))
                {
                    _syntheticScreenOpenErrorCode = OpenScreenErrorNoMemory;
                    RollbackSyntheticScreenBitmap(surface);
                    return false;
                }
                CyberGraphics.RegisterBitMap(_syntheticBitMapAddress, surface);
                CyberGraphics.RegisterBitMap(_syntheticScreenAddress + ScreenBitMapOffset, surface);
                var viewPort = GetSyntheticScreenViewPortAddress();
                _machine.Bus.WriteLong(viewPort + ViewPortRasInfoOffset, _syntheticRasInfoAddress);
                InitializeSyntheticRastPort(
                    _syntheticScreenAddress + ScreenRastPortOffset,
                    GetSyntheticScreenBitMapAddress(),
                    EnsureSyntheticScreenTextFont());
                var rastPort = EnsureSyntheticRastPort();
                CyberGraphics.RegisterRastPort(_syntheticScreenAddress + ScreenRastPortOffset, surface);
                CyberGraphics.RegisterRastPort(rastPort, surface);
                CyberGraphics.RegisterViewPort(viewPort, surface);
                CyberGraphics.SelectFrontViewPort(viewPort);
                if (CanRenderSyntheticScreenTitle())
                    RenderSyntheticScreenTitle("Loading");
                return true;
            }

            var planeBytes = GetSyntheticScreenPlaneSize() * _syntheticScreenDepth;
            _syntheticPlaneAddress = AllocateMemoryFromMemList(
                planeBytes,
                MemfPublic | MemfChip | MemfClear);
            if (_syntheticPlaneAddress == 0)
            {
                _syntheticScreenOpenErrorCode = OpenScreenErrorNoChipMemory;
                return false;
            }

            _syntheticRasInfoAddress = AllocateProgramMemory(0x10);
            _syntheticBitMapAddress = AllocateProgramMemory(BitMapPlanesOffset + 8 * 4);
            if (_syntheticRasInfoAddress == 0 || _syntheticBitMapAddress == 0)
            {
                _syntheticScreenOpenErrorCode = OpenScreenErrorNoMemory;
                RollbackSyntheticScreenBitmap();
                return false;
            }

            WriteSyntheticBitMap(_syntheticBitMapAddress);
            WriteSyntheticBitMap(_syntheticScreenAddress + ScreenBitMapOffset);
            if (!InitializeSyntheticRasInfoChain(_syntheticBitMapAddress))
            {
                _syntheticScreenOpenErrorCode = OpenScreenErrorNoMemory;
                RollbackSyntheticScreenBitmap();
                return false;
            }

            _machine.Bus.WriteLong(GetSyntheticScreenViewPortAddress() + ViewPortRasInfoOffset, _syntheticRasInfoAddress);
            InitializeSyntheticRastPort(
                _syntheticScreenAddress + ScreenRastPortOffset,
                GetSyntheticScreenBitMapAddress(),
                EnsureSyntheticScreenTextFont());
            _ = EnsureSyntheticRastPort();
            if (CanRenderSyntheticScreenTitle())
                RenderSyntheticScreenTitle("Loading");
            return true;
        }

        private void RollbackSyntheticScreenBitmap(CyberGraphicsSurface? surface = null)
        {
            if (surface != null)
            {
                CyberGraphics.UnregisterSurface(surface);
                CyberGraphics.FreeRtgSurface(surface);
            }

            // A failed setup must not leave the embedded Screen bitmap or its
            // ViewPort RasInfo link pointing at a half-built allocation. The
            // backing guest allocations remain monotonic/reset-scoped, but
            // clearing these links makes a retry start from an unambiguous
            // lifecycle state instead of treating stale handles as valid.
            if (_syntheticScreenAddress != 0 &&
                _machine.Bus.IsMappedMemoryRange(
                    _syntheticScreenAddress + ScreenBitMapOffset,
                    BitMapPlanesOffset + 8 * 4))
            {
                _machine.Bus.ClearMemory(
                    _syntheticScreenAddress + ScreenBitMapOffset,
                    BitMapPlanesOffset + 8 * 4);
            }

            if (_syntheticScreenAddress != 0)
            {
                var viewPort = _syntheticScreenAddress + ScreenViewPortOffset;
                if (_machine.Bus.IsMappedMemoryRange(viewPort + ViewPortRasInfoOffset, 4))
                    _machine.Bus.WriteLong(viewPort + ViewPortRasInfoOffset, 0);
            }

            _syntheticPlaneAddress = 0;
            _syntheticRasInfoAddress = 0;
            _syntheticSecondRasInfoAddress = 0;
            _syntheticBitMapAddress = 0;
            _syntheticCustomBitMapAddress = 0;
        }

        private bool InitializeSyntheticRasInfoChain(uint bitMap)
        {
            if (_syntheticRasInfoAddress == 0 || bitMap == 0)
            {
                return false;
            }

            _machine.Bus.ClearMemory(_syntheticRasInfoAddress, 0x10);
            _machine.Bus.WriteLong(_syntheticRasInfoAddress + RasInfoBitMapOffset, bitMap);
            _syntheticSecondRasInfoAddress = 0;

            if ((_syntheticScreenViewModes & ViewModeDualPlayfield) == 0)
            {
                return true;
            }

            _syntheticSecondRasInfoAddress = AllocateProgramMemory(0x10);
            if (_syntheticSecondRasInfoAddress == 0)
            {
                return false;
            }

            _machine.Bus.ClearMemory(_syntheticSecondRasInfoAddress, 0x10);
            _machine.Bus.WriteLong(
                _syntheticSecondRasInfoAddress + RasInfoBitMapOffset,
                bitMap);
            _machine.Bus.WriteLong(
                _syntheticRasInfoAddress + RasInfoNextOffset,
                _syntheticSecondRasInfoAddress);
            return true;
        }

        private void WriteRtgBitMap(uint bitMap, CyberGraphicsSurface surface)
        {
            _machine.Bus.ClearMemory(bitMap, BitMapPlanesOffset + 8 * 4);
            _machine.Bus.WriteWord(bitMap + BitMapBytesPerRowOffset, checked((ushort)surface.BytesPerRow));
            _machine.Bus.WriteWord(bitMap + BitMapRowsOffset, checked((ushort)surface.Height));
            _machine.Bus.WriteByte(bitMap + BitMapDepthOffset, checked((byte)surface.Depth), 0);
            _machine.Bus.WriteLong(bitMap + BitMapPlanesOffset, surface.GuestBaseAddress);
        }

        private uint GetSyntheticScreenBitMapAddress()
            => _syntheticCustomBitMapAddress != 0
                ? _syntheticCustomBitMapAddress
                : _syntheticScreenAddress != 0
                ? _syntheticScreenAddress + ScreenBitMapOffset
                : _syntheticBitMapAddress;

        private int GetSyntheticScreenBytesPerRow()
            => _syntheticDisplayServices.BytesPerRow;

        private int GetSyntheticScreenPlaneSize()
            => _syntheticDisplayServices.PlaneSize;

        private uint EnsureSyntheticRastPort()
        {
            if (_syntheticRastPortAddress != 0)
            {
                var bitMap = GetSyntheticScreenBitMapAddress();
                if (bitMap != 0)
                {
                    _machine.Bus.WriteLong(_syntheticRastPortAddress + RastPortBitMapOffset, bitMap);
                }

                ApplySyntheticRastPortFont(
                    _syntheticRastPortAddress,
                    GetSyntheticWindowTextFont());

                return _syntheticRastPortAddress;
            }

            _syntheticRastPortAddress = AllocateProgramMemory(0x80);
            if (_syntheticRastPortAddress == 0)
            {
                return EnsureSyntheticHostObject();
            }

            InitializeSyntheticRastPort(
                _syntheticRastPortAddress,
                GetSyntheticScreenBitMapAddress(),
                GetSyntheticWindowTextFont());
            return _syntheticRastPortAddress;
        }

        private uint GetSyntheticWindowTextFont()
            => _syntheticUiDisplay.WindowUsesDefaultFont
                ? EnsureSyntheticFont()
                : (_syntheticScreenTextFontAddress != 0
                    ? _syntheticScreenTextFontAddress
                    : EnsureSyntheticFont());

        private void InitializeSyntheticRastPort(uint rastPort, uint bitMap, uint textFont = 0)
        {
            if (rastPort == 0 ||
                !_machine.Bus.IsMappedMemoryRange(rastPort, RastPortTextSpacingOffset + 2))
            {
                return;
            }

            _machine.Bus.ClearMemory(rastPort, RastPortTextSpacingOffset + 2);
            _machine.Bus.WriteLong(rastPort + RastPortBitMapOffset, bitMap);
            _machine.Bus.WriteByte(rastPort + RastPortMaskOffset, 0xFF, 0);
            _machine.Bus.WriteByte(rastPort + RastPortFgPenOffset, 1, 0);
            _machine.Bus.WriteByte(rastPort + RastPortBgPenOffset, 0, 0);
            _machine.Bus.WriteByte(rastPort + RastPortDrawModeOffset, 1, 0);
            _machine.Bus.WriteWord(rastPort + RastPortLinePatternOffset, 0xFFFF);
            _machine.Bus.WriteWord(rastPort + RastPortPenWidthOffset, 1);
            _machine.Bus.WriteWord(rastPort + RastPortPenHeightOffset, 1);
            var selectedFont = textFont != 0 ? textFont : EnsureSyntheticFont();
            ApplySyntheticRastPortFont(rastPort, selectedFont);
        }

        private void ApplySyntheticRastPortFont(uint rastPort, uint textFont)
        {
            if (rastPort == 0 ||
                !_machine.Bus.IsMappedMemoryRange(rastPort, RastPortTextSpacingOffset + 2))
            {
                return;
            }

            var selectedFont = textFont != 0 ? textFont : EnsureSyntheticFont();
            var textHeight = (ushort)8;
            var textWidth = (ushort)8;
            var textBaseline = (ushort)7;
            var textSpacing = (ushort)0;
            if (selectedFont != 0 &&
                _syntheticFontBackend.TryGetMetrics(selectedFont, out var metrics))
            {
                textHeight = metrics.Height;
                textWidth = metrics.Width;
                textBaseline = metrics.Baseline;
                textSpacing = metrics.Spacing;
            }

            _machine.Bus.WriteLong(rastPort + RastPortFontOffset, selectedFont);
            _machine.Bus.WriteWord(rastPort + RastPortTextHeightOffset, textHeight);
            _machine.Bus.WriteWord(rastPort + RastPortTextWidthOffset, textWidth);
            _machine.Bus.WriteWord(rastPort + RastPortTextBaselineOffset, textBaseline);
            _machine.Bus.WriteWord(rastPort + RastPortTextSpacingOffset, textSpacing);
        }

        private uint EnsureSyntheticFont()
        {
            if (_syntheticFontAddress != 0)
            {
                return _syntheticFontAddress;
            }

            const int syntheticFontStrikeOffset = 0x40;
            const int syntheticFontGlyphCount = 96;
            const int syntheticFontModulo = syntheticFontGlyphCount + 1;
            const int syntheticFontStrikeBytes = syntheticFontModulo * 8;
            const int syntheticFontBytes = syntheticFontStrikeOffset + syntheticFontStrikeBytes;

            _syntheticFontAddress = AllocateProgramMemory(syntheticFontBytes);
            if (_syntheticFontAddress != 0)
            {
                _machine.Bus.ClearMemory(_syntheticFontAddress, syntheticFontBytes);
                // The host-only envelope reserves 0x40 bytes, while the
                // public TextFont prefix ends at 0x34. Keep the synthetic
                // name in that already-owned tail instead of allocating a
                // second guest block and perturbing the Exec memory lists.
                var fontNameAddress = _syntheticFontAddress + 0x34;
                _machine.Bus.WriteLong(_syntheticFontAddress + 0x0A, fontNameAddress);
                var fontName = "topaz.font";
                for (var index = 0; index < fontName.Length; index++)
                    _machine.Bus.WriteByte(fontNameAddress + (uint)index, (byte)fontName[index], 0);
                _machine.Bus.WriteByte(fontNameAddress + (uint)fontName.Length, 0, 0);

                _machine.Bus.WriteWord(_syntheticFontAddress + 0x14, 8);
                _machine.Bus.WriteByte(_syntheticFontAddress + 0x16, 7, 0);
                _machine.Bus.WriteByte(_syntheticFontAddress + 0x17, 8, 0);
                _machine.Bus.WriteWord(_syntheticFontAddress + 0x18, 8);
                _machine.Bus.WriteWord(_syntheticFontAddress + 0x1A, 7);
                _machine.Bus.WriteWord(_syntheticFontAddress + 0x1C, 1);
                _machine.Bus.WriteByte(_syntheticFontAddress + 0x20, 32, 0);
                _machine.Bus.WriteByte(_syntheticFontAddress + 0x21, 127, 0);
                var strikeAddress = _syntheticFontAddress + (uint)syntheticFontStrikeOffset;
                _machine.Bus.WriteLong(_syntheticFontAddress + 0x22, strikeAddress);
                _machine.Bus.WriteWord(_syntheticFontAddress + 0x26, (ushort)syntheticFontModulo);
                for (var row = 0; row < 8; row++)
                {
                    for (var index = 0; index <= syntheticFontGlyphCount; index++)
                    {
                        var character = index < syntheticFontGlyphCount
                            ? (char)(32 + index)
                            : '?';
                        var glyph = SyntheticGlyph(character);
                        var rowBits = row < 7
                            ? (byte)((glyph >> ((6 - row) * 5)) & 0x1F)
                            : (byte)0;
                        _machine.Bus.WriteByte(
                            strikeAddress + (uint)(row * syntheticFontModulo + index),
                            (byte)(rowBits << 3),
                            0);
                    }
                }
                _ = _graphicsFontList.TryAdd(_syntheticFontAddress);
                _ = _graphicsFontList.SetDefaultFont(_syntheticFontAddress);
            }

            return _syntheticFontAddress != 0 ? _syntheticFontAddress : EnsureSyntheticHostObject();
        }

        private uint EnsureSyntheticScreenFontAttr()
        {
            if (_syntheticScreenFontAttrAddress != 0)
            {
                return _syntheticScreenFontAttrAddress;
            }

            var font = EnsureSyntheticFont();
            if (font == 0 ||
                !_machine.Bus.IsMappedMemoryRange(font + 0x0A, 0x18))
            {
                return 0;
            }

            var textAttr = AllocateProgramMemory(CopperStartGraphicsLayouts.TextAttrSize);
            _machine.Bus.ClearMemory(textAttr, CopperStartGraphicsLayouts.TextAttrSize);
            _machine.Bus.WriteLong(textAttr, _machine.Bus.ReadLong(font + 0x0A));
            _machine.Bus.WriteWord(textAttr + 0x04, _machine.Bus.ReadWord(font + 0x14));
            _machine.Bus.WriteByte(textAttr + 0x06, _machine.Bus.ReadByte(font + 0x16), 0);
            _machine.Bus.WriteByte(textAttr + 0x07, _machine.Bus.ReadByte(font + 0x17), 0);
            _syntheticScreenFontAttrAddress = textAttr;
            return textAttr;
        }

        private uint EnsureSyntheticScreenTextFont()
        {
            if (_syntheticScreenTextFontAddress != 0 &&
                _machine.Bus.IsMappedMemoryRange(
                    _syntheticScreenTextFontAddress,
                    CopperStartGraphicsLayouts.TextFontMinimumSize))
            {
                return _syntheticScreenTextFontAddress;
            }

            ReleaseSyntheticScreenTextFont();

            if (_syntheticScreenFontAttrAddress != 0 &&
                _machine.Bus.IsMappedMemoryRange(
                    _syntheticScreenFontAttrAddress,
                    CopperStartGraphicsLayouts.TextAttrSize) &&
                _syntheticFontBackend.TryOpen(
                    _syntheticScreenFontAttrAddress,
                    out var selectedFont) &&
                selectedFont != 0 &&
                _machine.Bus.IsMappedMemoryRange(
                    selectedFont,
                    CopperStartGraphicsLayouts.TextFontMinimumSize))
            {
                _syntheticScreenTextFontAddress = selectedFont;
                _syntheticScreenTextFontOpened = true;
                return selectedFont;
            }

            var defaultFont = EnsureSyntheticFont();
            if (defaultFont == 0 ||
                !_machine.Bus.IsMappedMemoryRange(
                    defaultFont,
                    CopperStartGraphicsLayouts.TextFontMinimumSize))
            {
                return 0;
            }

            _syntheticScreenTextFontAddress = defaultFont;
            _syntheticScreenTextFontOpened = false;
            return defaultFont;
        }

        private void ReleaseSyntheticScreenTextFont()
        {
            if (_syntheticScreenTextFontOpened &&
                _syntheticScreenTextFontAddress != 0)
            {
                _ = _syntheticFontBackend.TryClose(_syntheticScreenTextFontAddress);
            }

            _syntheticScreenTextFontAddress = 0;
            _syntheticScreenTextFontOpened = false;
        }

        private void WriteSyntheticBitMap(uint bitMapAddress)
            => _syntheticDisplayServices.WriteBitMap(bitMapAddress, BitMapBytesPerRowOffset, BitMapRowsOffset, BitMapDepthOffset, BitMapPlanesOffset);

        private void RenderSyntheticScreenTitle(string title)
            => _syntheticDisplayServices.RenderTitle(title, SyntheticScreenTitleHeight);

        private bool CanRenderSyntheticScreenTitle()
            // SHOWTITLE controls title-bar ordering relative to backdrop
            // windows.  The synthetic host has one backing surface and no
            // backdrop stack, so only SCREENQUIET suppresses raster output.
            => (_syntheticScreenFlags & SyntheticScreenQuiet) == 0;

        private void ClearSyntheticScreenBitmap()
            => _syntheticDisplayServices.ClearBackingStore();

        private void FillSyntheticRect(int x, int y, int width, int height, int color)
            => _syntheticDisplayServices.FillRect(x, y, width, height, color);

        private void DrawSyntheticText(string text, int x, int y, int color)
            => _syntheticDisplayServices.DrawText(text, x, y, color);

        private void WriteSyntheticPixel(int x, int y, int color)
            => _syntheticDisplayServices.WritePixel(x, y, color);

        private bool IsMappedRastPort(uint rastPort)
            => rastPort != 0 &&
                _machine.Bus.IsMappedMemoryRange(rastPort, RastPortTextSpacingOffset + 2);

        private bool TryGetRastPortBitMap(uint rastPort, out uint bitMap)
        {
            bitMap = 0;
            if (!IsMappedRastPort(rastPort))
            {
                return false;
            }

            bitMap = _machine.Bus.ReadLong(rastPort + RastPortBitMapOffset);
            return bitMap != 0 &&
                _machine.Bus.IsMappedMemoryRange(bitMap, BitMapPlanesOffset + 4);
        }

        private int ReadRastPortFgPen(uint rastPort)
            => IsMappedRastPort(rastPort) ? _machine.Bus.ReadByte(rastPort + RastPortFgPenOffset) : 1;

        private int ReadRastPortBgPen(uint rastPort)
            => IsMappedRastPort(rastPort) ? _machine.Bus.ReadByte(rastPort + RastPortBgPenOffset) : 0;

        private int ReadSignedWordOrDefault(uint address, int defaultValue)
            => TryReadWord(address, out var value) ? unchecked((short)value) : defaultValue;

        private void FillBitMapRect(
            uint bitMap,
            int xMin,
            int yMin,
            int xMax,
            int yMax,
            int color,
            byte writeMask = 0xFF)
        {
            if (!TryReadBitMapInfo(bitMap, out var info))
            {
                return;
            }

            var left = Math.Clamp(Math.Min(xMin, xMax), 0, info.Width);
            var top = Math.Clamp(Math.Min(yMin, yMax), 0, info.Height);
            var right = Math.Clamp(Math.Max(xMin, xMax), -1, info.Width - 1);
            var bottom = Math.Clamp(Math.Max(yMin, yMax), -1, info.Height - 1);
            for (var y = top; y <= bottom; y++)
            {
                for (var x = left; x <= right; x++)
                {
                    WriteBitMapPixel(info, x, y, color, writeMask);
                }
            }
        }

        private void DrawBitMapLine(uint bitMap, int x0, int y0, int x1, int y1, int color)
        {
            if (!TryReadBitMapInfo(bitMap, out var info))
            {
                return;
            }

            var dx = Math.Abs(x1 - x0);
            var sx = x0 < x1 ? 1 : -1;
            var dy = -Math.Abs(y1 - y0);
            var sy = y0 < y1 ? 1 : -1;
            var error = dx + dy;
            while (true)
            {
                WriteBitMapPixel(info, x0, y0, color);
                if (x0 == x1 && y0 == y1)
                {
                    return;
                }

                var doubleError = error * 2;
                if (doubleError >= dy)
                {
                    error += dy;
                    x0 += sx;
                }

                if (doubleError <= dx)
                {
                    error += dx;
                    y0 += sy;
                }
            }
        }

        private void DrawBitMapGlyph(uint bitMap, char character, int x, int y, int foreground, int background, int drawMode)
        {
            if (!TryReadBitMapInfo(bitMap, out var info))
            {
                return;
            }

            var glyph = SyntheticGlyph(character);
            for (var row = 0; row < 8; row++)
            {
                for (var column = 0; column < 8; column++)
                {
                    var set = row < 7 &&
                        column < 5 &&
                        (((glyph >> ((6 - row) * 5)) & (ulong)(0x10 >> column)) != 0);
                    if (set)
                    {
                        WriteBitMapPixel(info, x + column, y + row, foreground);
                    }
                    else if ((drawMode & 1) != 0)
                    {
                        WriteBitMapPixel(info, x + column, y + row, background);
                    }
                }
            }
        }

        private bool TryReadBitMapInfo(uint bitMap, out HostBitMapInfo info)
        {
            info = default;
            if (_cyberGraphics is { } cyberGraphics &&
                cyberGraphics.TryGetBitMapSurface(bitMap, out var rtgSurface))
            {
                info = new HostBitMapInfo(rtgSurface);
                return true;
            }

            if (bitMap == 0 || !_machine.Bus.IsMappedMemoryRange(bitMap, BitMapPlanesOffset + 4))
            {
                return false;
            }

            var publishedBytesPerRow = ReadPositiveWordOrDefault(bitMap + BitMapBytesPerRowOffset, GetSyntheticScreenBytesPerRow());
            var rows = ReadPositiveWordOrDefault(bitMap + BitMapRowsOffset, _syntheticScreenHeight);
            var flags = _machine.Bus.ReadByte(bitMap + BitMapFlagsOffset);
            var depth = Math.Clamp((int)_machine.Bus.ReadByte(bitMap + BitMapDepthOffset), 1, 8);
            var interleaved = (flags & 0x04) != 0;
            if (interleaved)
            {
                if (publishedBytesPerRow == 0 || publishedBytesPerRow % depth != 0)
                    return false;

                publishedBytesPerRow = (ushort)(publishedBytesPerRow / depth);
            }

            var rowStride = interleaved
                ? checked(publishedBytesPerRow * depth)
                : publishedBytesPerRow;
            Span<uint> planes = stackalloc uint[8];
            var hasPlane = false;
            for (var plane = 0; plane < depth; plane++)
            {
                var planeAddressOffset = bitMap + BitMapPlanesOffset + (uint)(plane * 4);
                if (!_machine.Bus.IsMappedMemoryRange(planeAddressOffset, 4))
                {
                    return false;
                }

                planes[plane] = _machine.Bus.ReadLong(planeAddressOffset);
                hasPlane |= planes[plane] != 0;
            }

            if (!hasPlane)
            {
                return false;
            }

            info = new HostBitMapInfo(
                publishedBytesPerRow,
                rowStride,
                rows,
                depth,
                interleaved,
                planes);
            return true;
        }

        private void WriteBitMapPixel(
            HostBitMapInfo info,
            int x,
            int y,
            int color,
            byte writeMask = 0xFF)
        {
            if (x < 0 || y < 0 || x >= info.Width || y >= info.Height)
            {
                return;
            }

            if (info.RtgSurface != null)
            {
                var surface = info.RtgSurface;
                _cyberGraphics!.WriteSurfacePen(
                    surface,
                    x,
                    y,
                    (byte)color,
                    writeMask);
                return;
            }

            var byteOffset = checked((y * info.RowStride) + (x >> 3));
            var mask = (byte)(0x80 >> (x & 7));
            for (var plane = 0; plane < info.Depth; plane++)
            {
                if ((writeMask & (1 << plane)) == 0)
                {
                    continue;
                }

                var planeAddress = info.GetPlane(plane);
                if (planeAddress == 0 ||
                    !_machine.Bus.IsMappedMemoryRange(planeAddress + (uint)byteOffset, 1))
                {
                    continue;
                }

                var address = planeAddress + (uint)byteOffset;
                var value = _machine.Bus.ReadByte(address);
                value = ((color >> plane) & 1) != 0
                    ? (byte)(value | mask)
                    : (byte)(value & (byte)~mask);
                _machine.Bus.WriteByte(address, value, 0);
            }
        }

        private static ulong SyntheticGlyph(char character)
        {
            return char.ToUpperInvariant(character) switch
            {
                'A' => PackSyntheticGlyph(0x0E, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11),
                'B' => PackSyntheticGlyph(0x1E, 0x11, 0x11, 0x1E, 0x11, 0x11, 0x1E),
                'C' => PackSyntheticGlyph(0x0F, 0x10, 0x10, 0x10, 0x10, 0x10, 0x0F),
                'D' => PackSyntheticGlyph(0x1E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x1E),
                'E' => PackSyntheticGlyph(0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x1F),
                'F' => PackSyntheticGlyph(0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x10),
                'G' => PackSyntheticGlyph(0x0F, 0x10, 0x10, 0x13, 0x11, 0x11, 0x0F),
                'H' => PackSyntheticGlyph(0x11, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11),
                'I' => PackSyntheticGlyph(0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x1F),
                'J' => PackSyntheticGlyph(0x01, 0x01, 0x01, 0x01, 0x11, 0x11, 0x0E),
                'K' => PackSyntheticGlyph(0x11, 0x12, 0x14, 0x18, 0x14, 0x12, 0x11),
                'L' => PackSyntheticGlyph(0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x1F),
                'M' => PackSyntheticGlyph(0x11, 0x1B, 0x15, 0x15, 0x11, 0x11, 0x11),
                'N' => PackSyntheticGlyph(0x11, 0x19, 0x15, 0x13, 0x11, 0x11, 0x11),
                'O' => PackSyntheticGlyph(0x0E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E),
                'P' => PackSyntheticGlyph(0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10, 0x10),
                'Q' => PackSyntheticGlyph(0x0E, 0x11, 0x11, 0x11, 0x15, 0x12, 0x0D),
                'R' => PackSyntheticGlyph(0x1E, 0x11, 0x11, 0x1E, 0x14, 0x12, 0x11),
                'S' => PackSyntheticGlyph(0x0F, 0x10, 0x10, 0x0E, 0x01, 0x01, 0x1E),
                'T' => PackSyntheticGlyph(0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04),
                'U' => PackSyntheticGlyph(0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E),
                'V' => PackSyntheticGlyph(0x11, 0x11, 0x11, 0x11, 0x0A, 0x0A, 0x04),
                'W' => PackSyntheticGlyph(0x11, 0x11, 0x11, 0x15, 0x15, 0x15, 0x0A),
                'X' => PackSyntheticGlyph(0x11, 0x11, 0x0A, 0x04, 0x0A, 0x11, 0x11),
                'Y' => PackSyntheticGlyph(0x11, 0x11, 0x0A, 0x04, 0x04, 0x04, 0x04),
                'Z' => PackSyntheticGlyph(0x1F, 0x01, 0x02, 0x04, 0x08, 0x10, 0x1F),
                '0' => PackSyntheticGlyph(0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E),
                '1' => PackSyntheticGlyph(0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E),
                '2' => PackSyntheticGlyph(0x0E, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1F),
                '3' => PackSyntheticGlyph(0x1E, 0x01, 0x01, 0x0E, 0x01, 0x01, 0x1E),
                '4' => PackSyntheticGlyph(0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02),
                '5' => PackSyntheticGlyph(0x1F, 0x10, 0x10, 0x1E, 0x01, 0x01, 0x1E),
                '6' => PackSyntheticGlyph(0x0E, 0x10, 0x10, 0x1E, 0x11, 0x11, 0x0E),
                '7' => PackSyntheticGlyph(0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08),
                '8' => PackSyntheticGlyph(0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E),
                '9' => PackSyntheticGlyph(0x0E, 0x11, 0x11, 0x0F, 0x01, 0x01, 0x0E),
                ':' => PackSyntheticGlyph(0x00, 0x04, 0x04, 0x00, 0x04, 0x04, 0x00),
                '.' => PackSyntheticGlyph(0x00, 0x00, 0x00, 0x00, 0x00, 0x0C, 0x0C),
                '-' => PackSyntheticGlyph(0x00, 0x00, 0x00, 0x1F, 0x00, 0x00, 0x00),
                '/' => PackSyntheticGlyph(0x01, 0x01, 0x02, 0x04, 0x08, 0x10, 0x10),
                '\\' => PackSyntheticGlyph(0x10, 0x10, 0x08, 0x04, 0x02, 0x01, 0x01),
                '(' => PackSyntheticGlyph(0x02, 0x04, 0x08, 0x08, 0x08, 0x04, 0x02),
                ')' => PackSyntheticGlyph(0x08, 0x04, 0x02, 0x02, 0x02, 0x04, 0x08),
                '\'' => PackSyntheticGlyph(0x04, 0x04, 0x08, 0x00, 0x00, 0x00, 0x00),
                ' ' => 0,
                _ => PackSyntheticGlyph(0x1F, 0x11, 0x02, 0x04, 0x04, 0x00, 0x04),
            };
        }

        private static ulong PackSyntheticGlyph(uint row0, uint row1, uint row2, uint row3, uint row4, uint row5, uint row6)
        {
            return ((ulong)(row0 & 0x1Fu) << 30) |
                ((ulong)(row1 & 0x1Fu) << 25) |
                ((ulong)(row2 & 0x1Fu) << 20) |
                ((ulong)(row3 & 0x1Fu) << 15) |
                ((ulong)(row4 & 0x1Fu) << 10) |
                ((ulong)(row5 & 0x1Fu) << 5) |
                (ulong)(row6 & 0x1Fu);
        }

        private uint EnsureSyntheticWindow()
        {
            if (_syntheticWindowAddress != 0)
            {
                _machine.Bus.WriteLong(_syntheticWindowAddress + WindowUserPortOffset, EnsureSyntheticUserPort());
                _machine.Bus.WriteLong(_syntheticWindowAddress + WindowIdcmpFlagsOffset, _syntheticIdcmpFlags);
                if (_syntheticScreenAddress != 0)
                {
                    _machine.Bus.WriteLong(_syntheticWindowAddress + WindowWScreenOffset, _syntheticScreenAddress);
                }
                return _syntheticWindowAddress;
            }

            _syntheticWindowAddress = AllocateProgramMemory(0x100);
            if (_syntheticWindowAddress != 0)
            {
                var syntheticPort = EnsureSyntheticUserPort();
                _machine.Bus.WriteLong(_syntheticWindowAddress + WindowNextOffset, 0);
                _machine.Bus.WriteWord(_syntheticWindowAddress + 0x04, unchecked((ushort)(short)_syntheticWindowLeft));
                _machine.Bus.WriteWord(_syntheticWindowAddress + 0x06, unchecked((ushort)(short)_syntheticWindowTop));
                _machine.Bus.WriteWord(_syntheticWindowAddress + 0x08, (ushort)_syntheticWindowWidth);
                _machine.Bus.WriteWord(_syntheticWindowAddress + 0x0A, (ushort)_syntheticWindowHeight);
                if (_syntheticScreenAddress != 0)
                {
                    _machine.Bus.WriteLong(_syntheticWindowAddress + WindowWScreenOffset, _syntheticScreenAddress);
                }

                _machine.Bus.WriteLong(_syntheticWindowAddress + WindowRPortOffset, EnsureSyntheticRastPort());
                if (_syntheticGadgetListAddress != 0)
                {
                    _machine.Bus.WriteLong(_syntheticWindowAddress + WindowFirstGadgetOffset, _syntheticGadgetListAddress);
                }

                _machine.Bus.WriteLong(_syntheticWindowAddress + WindowIdcmpFlagsOffset, _syntheticIdcmpFlags);
                _machine.Bus.WriteLong(_syntheticWindowAddress + WindowUserPortOffset, syntheticPort);
            }

            return _syntheticWindowAddress != 0 ? _syntheticWindowAddress : EnsureSyntheticHostObject();
        }

        private uint EnsureSyntheticUserPort()
        {
            if (_syntheticUserPortAddress == 0)
            {
                _syntheticUserPortAddress = AllocateProgramMemory(0x30);
            }

            if (_syntheticUserPortAddress == 0)
            {
                return EnsureSyntheticHostObject();
            }

            var signalBit = EnsureSyntheticUserPortSignalBit();
            _machine.Bus.ClearMemory(_syntheticUserPortAddress, 0x30);
            _machine.Bus.WriteByte(_syntheticUserPortAddress + MsgPortTypeOffset, 4, 0);
            _machine.Bus.WriteByte(_syntheticUserPortAddress + MsgPortFlagsOffset, 0, 0);
            _machine.Bus.WriteByte(_syntheticUserPortAddress + MsgPortSigBitOffset, (byte)signalBit, 0);
            _machine.Bus.WriteLong(_syntheticUserPortAddress + MsgPortSigTaskOffset, GetCurrentTaskAddress());
            InitializeSyntheticList(_syntheticUserPortAddress + MsgPortMsgListOffset);
            return _syntheticUserPortAddress;
        }

        private int EnsureSyntheticUserPortSignalBit()
        {
            if (_syntheticUserPortSignalMask != 0)
            {
                for (var bit = 0; bit < 32; bit++)
                {
                    if ((_syntheticUserPortSignalMask & (1u << bit)) != 0)
                    {
                        return bit;
                    }
                }
            }

            var allocatedBit = _execSignalServices.EnsureCompatibilitySignalBit();
            if (allocatedBit is >= 0 and < 32)
            {
                _syntheticUserPortSignalMask = 1u << allocatedBit;
                return allocatedBit;
            }
            _syntheticUserPortSignalMask = 1;
            return 0;
        }

        private void InitializeSyntheticList(uint list)
        {
            if (!_machine.Bus.IsMappedMemoryRange(list, 12))
            {
                return;
            }

            _machine.Bus.WriteLong(list, list + 4);
            _machine.Bus.WriteLong(list + 4, 0);
            _machine.Bus.WriteLong(list + 8, list);
        }

        private void QueueSyntheticGadgetMessageAtMouse(uint messageClass)
        {
            if (!ShouldQueueSyntheticIdcmp(messageClass, requireExplicitFlag: false))
            {
                return;
            }

            if (TryFindSyntheticGadgetAt(_syntheticUiInput.MouseX, _syntheticUiInput.MouseY, out var gadget))
            {
                var gadgetCode = _machine.Bus.IsMappedMemoryRange(gadget, GadgetIdOffset + 2)
                    ? _machine.Bus.ReadWord(gadget + GadgetIdOffset)
                    : (ushort)0;
                _syntheticUiInput.Enqueue(new CopperStartSyntheticIntuiMessage(
                    messageClass,
                    code: gadgetCode,
                    qualifier: 0,
                    iAddress: gadget,
                    mouseX: _syntheticUiInput.MouseX,
                    mouseY: _syntheticUiInput.MouseY,
                    cycles: _machine.Cpu.State.Cycles));
                SignalSyntheticUserPort();
            }
        }

        private bool ShouldQueueSyntheticIdcmp(uint messageClass, bool requireExplicitFlag)
            => (_syntheticIdcmpFlags & messageClass) != 0 ||
                (!requireExplicitFlag && _syntheticIdcmpFlags == 0 && messageClass == IdcmpGadgetUp);

        private void SignalSyntheticUserPort()
        {
            if (_syntheticUserPortSignalMask == 0)
            {
                _ = EnsureSyntheticUserPort();
            }

            _execSignalServices.SignalCompatibility(_syntheticUserPortSignalMask);
        }

        private bool TryFindSyntheticGadgetAt(int x, int y, out uint gadget)
        {
            gadget = 0;
            var current = _syntheticGadgetListAddress;
            for (var scanned = 0; scanned < 128 && current != 0; scanned++)
            {
                if (!_machine.Bus.IsMappedMemoryRange(current, GadgetHeightOffset + 2))
                {
                    return false;
                }

                var left = ReadSignedWordOrDefault(current + GadgetLeftEdgeOffset, 0);
                var top = ReadSignedWordOrDefault(current + GadgetTopEdgeOffset, 0);
                var width = ReadPositiveWordOrDefault(current + GadgetWidthOffset, 0);
                var height = ReadPositiveWordOrDefault(current + GadgetHeightOffset, 0);
                if (width > 0 &&
                    height > 0 &&
                    x >= left &&
                    y >= top &&
                    x < left + width &&
                    y < top + height)
                {
                    gadget = current;
                    return true;
                }

                current = _machine.Bus.ReadLong(current + GadgetNextOffset);
            }

            return false;
        }

        private void MoveTaskToList(uint task, uint list, M68kCpuState state)
        {
            if (IsValidExecNode(task) && _machine.Bus.ReadLong(task + NodePredecessorOffset) != 0) RemoveExecNode(task);
            EnsureExecList(list);
            if (list == GetActiveExecBase() + ExecTaskReadyOffset)
            {
                GetExecListServices().Enqueue(list, task);
            }
            else
            {
                AddTailExecList(list, task);
            }
            _machine.Bus.WriteByte(task + TaskStateOffset, list == GetActiveExecBase() + ExecTaskWaitOffset ? (byte)4 : (byte)3, state.Cycles);
        }

        private uint GetMessage(M68kCpuState state)
        {
            if (UsesGuestExecTasks)
            {
                return _execPortServices.GetMsg(state);
            }

            if (!_syntheticUiInput.TryDequeue(out var message))
            {
                return 0;
            }

            if (_syntheticUiInput.MessageCount == 0)
            {
                _execSignalServices.ClearCompatibility(_syntheticUserPortSignalMask);
            }

            return WriteSyntheticMessage(message);
        }

        private void InitializeExecList(uint list)
            => GetExecListServices().Initialize(list);

        private void EnsureExecList(uint list)
            => GetExecListServices().Ensure(list);

        private bool ContainsExecNode(uint list, uint node)
            => GetExecListServices().Contains(list, node);

        private bool IsValidExecList(uint list)
            => GetExecListServices().IsValidList(list);

        private bool IsValidExecNode(uint node)
            => GetExecListServices().IsValidNode(node);

        private uint RemoveExecNode(uint node)
            => GetExecListServices().Remove(node);

        private uint RemoveExecListEnd(uint list, bool head)
            => GetExecListServices().RemoveEnd(list, head);

        private void AddTailExecList(uint list, uint node)
            => GetExecListServices().AddTail(list, node);

        private void AddExecNodeAtomically(uint list, uint node, bool priorityOrdered, M68kCpuState state)
        {
			EnsureExecList(list);
            if (!IsValidExecList(list) || !IsValidExecNode(node) || ContainsExecNode(list, node)) return;
            _execTaskServices.Forbid(state);
            try
            {
                if (priorityOrdered)
                {
                    GetExecListServices().Enqueue(list, node);
                }
                else
                {
                    AddTailExecList(list, node);
                }
            }
            finally
            {
                _execTaskServices.Permit(state);
            }
        }

        private void RemoveExecNodeAtomically(uint node, M68kCpuState state)
        {
            _execTaskServices.Forbid(state);
            try { RemoveExecNode(node); }
            finally { _execTaskServices.Permit(state); }
        }

        private void StartGuestExecSubroutine(M68kCpuState state, uint entry, uint continuation)
        {
            if (entry == 0 || !_machine.Bus.IsCpuPhysicalAddressMapped(entry, 2, AmigaBusAccessKind.CpuInstructionFetch) || state.A[7] < 4)
            {
                state.D[0] = 0;
                return;
            }

            state.A[7] -= 4;
            _machine.Bus.WriteLong(state.A[7], continuation, state.Cycles);
            state.ProgramCounter = entry + 6;
            if (_machine.Bus.TryInvokeHostGatewayAt(entry, state))
            {
                if (state.ProgramCounter == entry + 6)
                {
                    state.ProgramCounter = _machine.Bus.ReadLong(state.A[7]);
                    state.A[7] += 4;
                }
                return;
            }

            state.ProgramCounter = entry;
        }

        private M68kHostGatewayResult ContinueHostWait(M68kCpuState state)
            => _execSemaphoreServices.TryContinueWait(state, out var result)
                ? result
                : _execSignalServices.ContinueWait(state);

        private M68kHostGatewayResult WaitPort(M68kCpuState state)
        {
            if (UsesGuestExecTasks)
            {
                return _execPortServices.WaitPort(state);
            }

            if (_syntheticUiInput.TryPeek(out var message))
            {
                state.D[0] = WriteSyntheticMessage(message);
                return M68kHostGatewayResult.Completed;
            }

            if (state.LastInstructionProgramCounter != 0)
            {
                state.ProgramCounter = state.LastInstructionProgramCounter;
            }

            var nextFrameCycle = _machine.Bus.GetNextFrameStartCycle(Math.Max(0, state.Cycles));
            state.Cycles = Math.Max(state.Cycles + 1, nextFrameCycle);
            return 0;
        }

        private void AddExecLikeDiagnostic(string code, string message)
        {
            if (_execDiagnosticCount >= 128)
            {
                return;
            }

            _diagnostics.Add(new AmigaBootDiagnostic(code, message));
            _execDiagnosticCount++;
        }

        private bool CanAddExecLikeDiagnostic => _execDiagnosticCount < 128;

        private uint GetCurrentTaskAddress()
        {
            var task = _machine.Bus.ReadLong(GetActiveExecBase() + ExecThisTaskOffset);
            if (task != 0)
            {
                return task;
            }

            return _currentTaskAddress != 0 ? _currentTaskAddress : AmigaKickstartHost.ExecStructAddress;
        }

        private uint GetActiveExecBase()
            => _activeExecBase != 0 ? _activeExecBase : AmigaKickstartHost.ExecLibraryBase;

        private uint EnsureSyntheticMessage()
        {
            if (_syntheticMessageAddress != 0)
            {
                return _syntheticMessageAddress;
            }

            _syntheticMessageAddress = AllocateProgramMemory(0x60);
            if (_syntheticMessageAddress == 0)
            {
                return EnsureSyntheticHostObject();
            }

            return _syntheticMessageAddress;
        }

        private uint WriteSyntheticMessage(CopperStartSyntheticIntuiMessage message)
        {
            var address = EnsureSyntheticMessage();
            if (address == 0 || !_machine.Bus.IsMappedMemoryRange(address, 0x34))
            {
                return address;
            }

            _machine.Bus.ClearMemory(address, 0x34);
            _machine.Bus.WriteWord(address + 0x12, 0x0034);
            _machine.Bus.WriteLong(address + 0x14, message.Class);
            _machine.Bus.WriteWord(address + 0x18, message.Code);
            _machine.Bus.WriteWord(address + 0x1A, message.Qualifier);
            _machine.Bus.WriteLong(address + 0x1C, message.IAddress);
            _machine.Bus.WriteWord(address + 0x20, unchecked((ushort)(short)message.MouseX));
            _machine.Bus.WriteWord(address + 0x22, unchecked((ushort)(short)message.MouseY));
            _machine.Bus.WriteLong(address + 0x24, (uint)(Math.Max(0, message.Cycles) / AmigaConstants.A500PalCpuCyclesPerSecond));
            _machine.Bus.WriteLong(address + 0x28, 0);
            _machine.Bus.WriteLong(address + 0x2C, _syntheticWindowAddress);
            return address;
        }

        private uint EnsureSyntheticHostObject()
        {
            if (_syntheticHostObjectAddress != 0)
            {
                return _syntheticHostObjectAddress;
            }

            _syntheticHostObjectAddress = AllocateProgramMemory(0x40);
            return _syntheticHostObjectAddress != 0 ? _syntheticHostObjectAddress : 1u;
        }

        private uint FindToolTypeValue(uint toolTypesAddress, string key)
        {
            if (toolTypesAddress == 0 || string.IsNullOrWhiteSpace(key))
            {
                return 0;
            }

            for (var index = 0; index < 128; index++)
            {
                var pointer = _machine.Bus.ReadLong(toolTypesAddress + (uint)(index * 4));
                if (pointer == 0)
                {
                    return 0;
                }

                var value = ReadNullTerminatedString(pointer, 256);
                var separator = value.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                if (value.Substring(0, separator).Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    return pointer + (uint)separator + 1;
                }
            }

            return 0;
        }

        private static bool AsciiEqualsIgnoreCase(byte left, char right)
        {
            var leftChar = (char)left;
            if (leftChar is >= 'A' and <= 'Z')
            {
                leftChar = (char)(leftChar + ('a' - 'A'));
            }

            if (right is >= 'A' and <= 'Z')
            {
                right = (char)(right + ('a' - 'A'));
            }

            return leftChar == right;
        }

        private uint FindNamedGuestEntry(string target, uint first, Func<uint, bool> isEntry, Func<uint, uint> next, Func<uint, uint> namePointer, bool indirect = false)
        {
            for (var entry = first; entry != 0 && isEntry(entry); entry = next(entry))
            {
                var candidate = indirect ? _machine.Bus.ReadLong(entry) : entry;
                if (candidate != 0 && string.Equals(target, ReadNullTerminatedString(namePointer(candidate), 96), StringComparison.OrdinalIgnoreCase)) return candidate;
            }
            return 0;
        }

        private void ContinueExecLibraryCall(M68kCpuState state)
        {
            GetRomExecLibraryServices().CompleteLibraryCallback(state);
            if (_nativeGraphicsOpenPending)
            {
                _nativeGraphicsOpenPending = false;
                if (state.D[0] != 0)
                {
                    _nativeGraphicsLibraryBase = state.D[0];
                    _ = SetChipRev(CopperStartGraphicsChipRevision.SetBest);
                    _ = _graphicsServices.InstallKickstartRomOverlay(state.D[0]);
                }
            }
        }

        private void TryInstallNativeGraphicsOverlayFromLibraryList(uint execBase)
        {
            var listAddress = execBase + (uint)ExecLibListOffset;
            if (!_machine.Bus.IsMappedMemoryRange(listAddress, 8))
                return;

            var node = _machine.Bus.ReadLong(listAddress);
            for (var guard = 0; node != 0 && node != listAddress + 4 && guard < 128; guard++)
            {
                if (!_machine.Bus.IsMappedMemoryRange(node + (uint)NodeNameOffset, 8))
                    return;

                var nameAddress = _machine.Bus.ReadLong(node + (uint)NodeNameOffset);
                if (nameAddress != 0 &&
                    _machine.Bus.IsMappedMemoryRange(nameAddress, "graphics.library".Length + 1) &&
                    MatchesNullTerminatedString(null, nameAddress, 96, "graphics.library"))
                {
                    _nativeGraphicsLibraryBase = node;
                    _ = SetChipRev(CopperStartGraphicsChipRevision.SetBest);
                    _ = _graphicsServices.InstallKickstartRomOverlay(node);
                    return;
                }

                node = _machine.Bus.ReadLong(node);
            }
        }

        private uint GetGraphicsLibraryBase()
            => _nativeGraphicsLibraryBase != 0
                ? _nativeGraphicsLibraryBase
                : AmigaKickstartHost.GraphicsLibraryBase;

        private bool TryGetAlignedGraphicsLibraryBase(out uint graphicsBase)
        {
            graphicsBase = GetGraphicsLibraryBase();
            return (graphicsBase & 1u) == 0;
        }

        private static bool TryGetGraphicsFieldAddress(
            uint graphicsBase,
            int offset,
            int byteCount,
            out uint address)
        {
            if (!CanAddressField(graphicsBase, offset, byteCount))
            {
                address = 0;
                return false;
            }

            address = graphicsBase + (uint)offset;
            return true;
        }


        private void EnsureDosResident()
        {
            var resident = new byte[0x60];
            BigEndian.WriteUInt16(resident, 0x00, 0x4AFC);
            BigEndian.WriteUInt32(resident, 0x02, DosResidentAddress);
            BigEndian.WriteUInt32(resident, 0x06, DosResidentAddress + (uint)resident.Length);
            resident[0x0A] = 0x01;
            resident[0x0B] = 34;
            resident[0x0C] = 9;
            resident[0x0D] = 0;
            BigEndian.WriteUInt32(resident, 0x0E, DosResidentNameAddress);
            BigEndian.WriteUInt32(resident, 0x12, DosResidentIdAddress);
            BigEndian.WriteUInt32(resident, 0x16, DosResidentInitAddress);
            WriteAscii(resident.AsSpan((int)(DosResidentNameAddress - DosResidentAddress)), "dos.library");
            WriteAscii(resident.AsSpan((int)(DosResidentIdAddress - DosResidentAddress)), "dos.library 34.20");
            _machine.Bus.CopyToChipRam(DosResidentAddress, resident);
        }

        private static void WriteAscii(Span<byte> destination, string value)
        {
            var count = Math.Min(destination.Length - 1, value.Length);
            for (var i = 0; i < count; i++)
            {
                destination[i] = (byte)value[i];
            }

            destination[count] = 0;
        }

        private void HostAbleIcr(M68kCpuState state)
        {
            state.D[0] = 0;
        }

        private void HostSetIcr(M68kCpuState state)
        {
            state.D[0] = 0;
        }

        private void HostNullCallback(M68kCpuState state)
        {
            var returnAddress = _machine.Bus.IsMappedMemoryRange(state.A[7], 4)
                ? _machine.Bus.ReadLong(state.A[7])
                : 0u;
            var nullPc = state.ProgramCounter == 0 && returnAddress == 0;
            _diagnostics.Add(new AmigaBootDiagnostic(
                nullPc ? "AMIGA_BOOT_NULL_PC" : "AMIGA_BOOT_NULL_HOST_CALLBACK",
                (nullPc
                    ? "Boot program returned or jumped to address zero."
                    : "Boot program called a null host callback; treating it as a no-op.") + " " +
                $"PC=0x{state.ProgramCounter:X8}, lastPC=0x{state.LastInstructionProgramCounter:X8}, " +
                $"lastOpcode=0x{state.LastOpcode:X4}, SP=0x{state.A[7]:X8}, return=0x{returnAddress:X8}, " +
                $"D0=0x{state.D[0]:X8}, A0=0x{state.A[0]:X8}, A1=0x{state.A[1]:X8}, A6=0x{state.A[6]:X8}."));
            if (nullPc)
            {
                state.Halted = true;
            }
        }

        private static void HostOk(M68kCpuState state)
        {
            state.D[0] = 0;
        }

        private bool TryStartDosBootContinuation()
        {
            if (_dosBootContinuationStarted || _machine.Cpu.State.D[0] != 0 || Drive0.Disk == null)
            {
                return false;
            }

            _dosBootContinuationStarted = true;
            AmigaDosFileSystem fileSystem;
            try
            {
                fileSystem = EnsureDosFileSystem();
            }
            catch (Exception ex) when (ex is AmigaEmulationException or OverflowException or ArgumentOutOfRangeException)
            {
                _diagnostics.Add(new AmigaBootDiagnostic(
                    "AMIGA_BOOT_DOS_FILESYSTEM_UNSUPPORTED",
                    $"Boot block returned, but the disk is not a supported slim AmigaDOS filesystem: {ex.Message}"));
                return false;
            }

            AmigaProgramLaunchRequest request;
            string autostartDescription;
            if (AutoRunStartupSequence &&
                TryReadStartupSequence(fileSystem, out var startupSequence))
            {
                if (TryStartStartupSequence(fileSystem, startupSequence, out autostartDescription))
                {
                    _dosBootBlockHeaderProbeEnabled = true;
                    _diagnostics.Add(new AmigaBootDiagnostic(
                        "AMIGA_BOOT_DOS_AUTOSTART",
                        $"Started {autostartDescription}."));
                    return true;
                }

                return false;
            }

            if (fileSystem.TryResolveWorkbenchDefaultTool(out var projectPath, out var toolPath, out var toolTypes) &&
                fileSystem.TryReadFile(toolPath, out _))
            {
                request = new AmigaProgramLaunchRequest(
                    toolPath,
                    projectPath,
                    AmigaDosFileSystem.GetDirectoryName(projectPath),
                    toolTypes,
                    4096,
                    cliArguments: null);
                autostartDescription = $"Workbench default tool {toolPath}";
            }
            else if (TryCreateStartupSequenceLaunchRequest(fileSystem, out request, out autostartDescription))
            {
            }
            else
            {
                return false;
            }

            PendingWorkbenchLaunchRequest = request;
            if (!AutoStartWorkbenchDefaultTool)
            {
                _diagnostics.Add(new AmigaBootDiagnostic(
                    "AMIGA_BOOT_DOS_WORKBENCH_HANDOFF",
                    $"{autostartDescription} is ready to launch."));
                return false;
            }

            if (!TryLaunchProgram(request, out _, out _))
            {
                return false;
            }

            _dosBootBlockHeaderProbeEnabled = true;
            _diagnostics.Add(new AmigaBootDiagnostic(
                "AMIGA_BOOT_DOS_AUTOSTART",
                $"Started {autostartDescription}."));
            return true;
        }

        private bool TryStartStartupSequence(
            AmigaDosFileSystem fileSystem,
            string startupSequence,
            out string description)
        {
            description = string.Empty;
            _startupSequenceCommands.Clear();
            _startupSequenceCommandIndex = 0;
            foreach (var rawLine in startupSequence.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = NormalizeStartupSequenceLine(rawLine);
                if (line.Length == 0 || line[0] == ';')
                {
                    continue;
                }

                var executablePath = ExtractStartupCommandPath(line);
                if (executablePath.Length == 0)
                {
                    continue;
                }

                _startupSequenceCommands.Add(new StartupSequenceCommand(
                    executablePath,
                    ExtractStartupCommandArguments(line),
                    rawLine.Trim()));
            }

            if (_startupSequenceCommands.Count == 0)
            {
                return false;
            }

            _startupSequenceActive = true;
            return TryLaunchNextStartupSequenceCommand(fileSystem, out description);
        }

        private bool TryContinueStartupSequence()
        {
            if (!_startupSequenceActive)
            {
                return false;
            }

            var fileSystem = EnsureDosFileSystem();
            if (TryLaunchNextStartupSequenceCommand(fileSystem, out var description))
            {
                _diagnostics.Add(new AmigaBootDiagnostic(
                    "AMIGA_BOOT_DOS_STARTUP_CONTINUE",
                    $"Started {description}."));
                return true;
            }

            _diagnostics.Add(new AmigaBootDiagnostic(
                "AMIGA_BOOT_DOS_STARTUP_COMPLETE",
                "Startup-Sequence reached the end of the host bridge runner."));
            return false;
        }

        private bool TryLaunchNextStartupSequenceCommand(AmigaDosFileSystem fileSystem, out string description)
        {
            description = string.Empty;
            while (_startupSequenceCommandIndex < _startupSequenceCommands.Count)
            {
                var command = _startupSequenceCommands[_startupSequenceCommandIndex++];
                if (IsStartupSequenceTerminator(command.ExecutablePath))
                {
                    _startupSequenceActive = false;
                    description = $"startup-sequence command {command.ExecutablePath}";
                    return false;
                }

                if (TryHandleHostBridgeSetupCommand(fileSystem, command))
                {
                    continue;
                }

                var launchPath = command.ExecutablePath;
                if (!fileSystem.TryCreateLaunchRequest(launchPath, out var request, out var message))
                {
                    _diagnostics.Add(new AmigaBootDiagnostic(
                        "AMIGA_BOOT_DOS_STARTUP_SKIP",
                        $"Skipped startup-sequence command '{command.RawLine}': {message}"));
                    continue;
                }

                EnsureWorkbenchHostShimInstalled();
                if (command.Arguments.Length != 0)
                {
                    request = new AmigaProgramLaunchRequest(
                        request.ExecutablePath,
                        request.ProjectPath,
                        request.CurrentDirectory,
                        request.ToolTypes,
                        request.StackSize,
                        command.Arguments);
                }

                PendingWorkbenchLaunchRequest = request;
                if (!TryLaunchProgram(
                    request,
                    out _,
                    out message,
                    enableProgramInterrupts: true))
                {
                    _diagnostics.Add(new AmigaBootDiagnostic(
                        "AMIGA_BOOT_DOS_STARTUP_SKIP",
                        $"Could not launch startup-sequence command '{command.RawLine}': {message}"));
                    continue;
                }

                description = $"startup-sequence command {command.ExecutablePath}";
                return true;
            }

            _startupSequenceActive = false;
            return false;
        }

        private void EnsureWorkbenchHostShimInstalled()
        {
            if (_memoryListInstalled)
            {
                return;
            }

            InstallBootHostTraps();
        }

        private bool TryCreateStartupSequenceLaunchRequest(
            AmigaDosFileSystem fileSystem,
            out AmigaProgramLaunchRequest request,
            out string description)
        {
            request = default;
            description = string.Empty;
            if (!TryReadStartupSequence(fileSystem, out var startupSequence))
            {
                return false;
            }

            foreach (var rawLine in startupSequence.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line[0] == ';')
                {
                    continue;
                }

                var executablePath = ExtractStartupCommandPath(line);
                if (executablePath.Length == 0 ||
                    !fileSystem.TryCreateLaunchRequest(executablePath, out request, out _))
                {
                    continue;
                }

                description = $"startup-sequence command {executablePath}";
                return true;
            }

            return false;
        }

        private static bool TryReadStartupSequence(AmigaDosFileSystem fileSystem, out string startupSequence)
        {
            if (fileSystem.TryReadFile("s/startup-sequence", out var data) ||
                fileSystem.TryReadFile("startup-sequence", out data))
            {
                startupSequence = Encoding.ASCII.GetString(data);
                return true;
            }

            startupSequence = string.Empty;
            return false;
        }

        private static string ExtractStartupCommandPath(string line)
        {
            var space = line.IndexOf(' ');
            var tab = line.IndexOf('\t');
            var end = space < 0
                ? tab
                : tab < 0
                    ? space
                    : Math.Min(space, tab);
            return end < 0 ? line : line[..end];
        }

        private static string ExtractStartupCommandArguments(string line)
        {
            var space = line.IndexOf(' ');
            var tab = line.IndexOf('\t');
            var start = space < 0
                ? tab
                : tab < 0
                    ? space
                    : Math.Min(space, tab);
            return start < 0 ? string.Empty : line[start..].Trim();
        }

        private static string NormalizeStartupSequenceLine(string line)
        {
            line = RemoveStartupRedirections(line.Trim());
            var comment = line.IndexOf(';');
            if (comment >= 0)
            {
                line = line[..comment].TrimEnd();
            }

            return line;
        }

        private static string RemoveStartupRedirections(string line)
        {
            if (line.IndexOf('>') < 0 && line.IndexOf('<') < 0)
            {
                return line;
            }

            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var kept = new List<string>(parts.Length);
            var skipNext = false;
            foreach (var part in parts)
            {
                if (skipNext)
                {
                    skipNext = false;
                    continue;
                }

                if (part is ">" or "<")
                {
                    skipNext = true;
                    continue;
                }

                if (part.StartsWith(">", StringComparison.Ordinal) ||
                    part.StartsWith("<", StringComparison.Ordinal))
                {
                    continue;
                }

                kept.Add(part);
            }

            return string.Join(" ", kept);
        }

        private static bool IsStartupSequenceTerminator(string executablePath)
        {
            var normalized = AmigaDosFileSystem.GetFileName(executablePath);
            return normalized.Equals("EndCLI", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("EndShell", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryHandleHostBridgeSetupCommand(AmigaDosFileSystem fileSystem, StartupSequenceCommand command)
        {
            var normalized = AmigaDosFileSystem.GetFileName(command.ExecutablePath);
            var arguments = SplitStartupArguments(command.Arguments);
            if (IsSetPatchCommand(normalized))
            {
                var hasM68040Library = TryFindDosEntry("Libs/68040.library", out var library) && library.IsFile;
                _taskTrapRuntime.Install();
                AddStartupHostDiagnostic(
                    command,
                    hasM68040Library
                        ? "Modeled SetPatch and detected Libs/68040.library."
                        : "Modeled SetPatch without a disk 68040.library.");
                return true;
            }

            if (normalized.Equals("Version", StringComparison.OrdinalIgnoreCase))
            {
                AddStartupHostDiagnostic(command, "Modeled Version query.");
                return true;
            }

            if (normalized.Equals("AddBuffers", StringComparison.OrdinalIgnoreCase))
            {
                AddStartupHostDiagnostic(command, "Modeled disk buffer allocation.");
                return true;
            }

            if (normalized.Equals("FailAt", StringComparison.OrdinalIgnoreCase))
            {
                if (arguments.Length > 0 && int.TryParse(arguments[0], out var failAt))
                {
                    _startupSequenceFailAt = failAt;
                }

                AddStartupHostDiagnostic(command, $"Set host startup FailAt threshold to {_startupSequenceFailAt}.");
                return true;
            }

            if (normalized.Equals("MakeDir", StringComparison.OrdinalIgnoreCase))
            {
                if (arguments.Length > 0)
                {
                    var directory = NormalizeHostDosPath(arguments[0]);
                    if (directory.StartsWith("RAM/", StringComparison.OrdinalIgnoreCase))
                    {
                        _ramDirectorySources[directory] = string.Empty;
                    }
                }

                AddStartupHostDiagnostic(command, "Modeled RAM: directory creation.");
                return true;
            }

            if (normalized.Equals("Copy", StringComparison.OrdinalIgnoreCase))
            {
                if (arguments.Length >= 2)
                {
                    var source = ResolveAssignedDosPath(arguments[0]);
                    var target = NormalizeHostDosPath(arguments[1]);
                    if (target.StartsWith("RAM/", StringComparison.OrdinalIgnoreCase))
                    {
                        _ramDirectorySources[target] = source;
                    }
                }

                AddStartupHostDiagnostic(command, "Modeled startup copy into RAM:.");
                return true;
            }

            if (normalized.Equals("Assign", StringComparison.OrdinalIgnoreCase))
            {
                if (arguments.Length >= 2)
                {
                    var assignName = NormalizeAssignName(arguments[0]);
                    if (assignName.Length != 0)
                    {
                        _dosAssigns[assignName] = NormalizeHostDosAssignTarget(arguments[1]);
                    }
                }

                AddStartupHostDiagnostic(command, "Modeled DOS assign.");
                return true;
            }

            if (normalized.Equals("BindDrivers", StringComparison.OrdinalIgnoreCase))
            {
                AddStartupHostDiagnostic(command, "Modeled BindDrivers expansion scan.");
                return true;
            }

            return false;
        }

        private static bool IsSetPatchCommand(string fileName)
            => fileName.Equals("SetPatch", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith("SetPatch_", StringComparison.OrdinalIgnoreCase);

        private void AddStartupHostDiagnostic(StartupSequenceCommand command, string message)
        {
            _diagnostics.Add(new AmigaBootDiagnostic(
                "AMIGA_BOOT_DOS_STARTUP_HOST",
                $"{message} Command '{command.RawLine}'."));
        }

        private static string[] SplitStartupArguments(string arguments)
        {
            return (arguments ?? string.Empty)
                .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public bool TryLaunchProgram(
            AmigaProgramLaunchRequest request,
            out AmigaProgramLaunchResult result,
            out string message,
            bool enableProgramInterrupts = true)
        {
            result = default;
            message = string.Empty;
            if (Drive0.Disk == null)
            {
                message = "No disk is inserted in DF0:.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(request.ExecutablePath))
            {
                message = "No executable path was provided.";
                return false;
            }

            if (!EnsureDosFileSystem().TryReadFile(request.ExecutablePath, out var executable))
            {
                message = $"'{request.ExecutablePath}' could not be read from DF0:.";
                return false;
            }

            return TryLaunchProgram(
                executable,
                request,
                out result,
                out message,
                enableProgramInterrupts);
        }

        public bool TryLaunchProgram(
            ReadOnlySpan<byte> executable,
            AmigaProgramLaunchRequest request,
            out AmigaProgramLaunchResult result,
            out string message,
            bool enableProgramInterrupts = true)
        {
            result = default;
            message = string.Empty;

            if (!AmigaHunkProgramLoader.HasHunkHeader(executable))
            {
                message = $"'{request.ExecutablePath}' is not a HUNK executable.";
                return false;
            }

            _workbenchToolTypes = NormalizeToolTypes(request.ToolTypes);
            _workbenchDefaultToolPath = request.ExecutablePath;
            _workbenchCurrentDirectory = request.CurrentDirectory;
            _workbenchStackSize = Math.Max(1, request.StackSize);
            _workbenchLanguageSelectionIndex = FindWorkbenchLanguageSelectionIndex(_workbenchToolTypes);
            _workbenchLanguageSelectionApplied = false;
            _workbenchDiskObjectAddress = 0;

            var loader = new AmigaHunkProgramLoader(_machine.Bus, AllocateProgramMemory);
            var program = loader.Load(executable);
            var startupArguments = request.CliArguments ?? BuildCliArguments(_workbenchToolTypes);
            var startupAddress = WriteProgramString(startupArguments);
            InitializeProgramRegisterFrame();
            _machine.Cpu.BeginSubroutine(program.EntryAddress, GetProgramStackTopAddress(), DosProgramReturnAddress);
            _machine.Cpu.State.D[0] = (uint)startupArguments.Length;
            _machine.Cpu.State.A[0] = startupAddress;
            _machine.Cpu.State.A[6] = AmigaKickstartHost.ExecLibraryBase;
            if (enableProgramInterrupts)
            {
                EnableWorkbenchProgramInterrupts();
            }

            result = new AmigaProgramLaunchResult(
                program.EntryAddress,
                request.ExecutablePath,
                startupArguments,
                _workbenchStackSize);
            _diagnostics.Add(new AmigaBootDiagnostic(
                "AMIGA_BOOT_COPPERBENCH_LAUNCH",
                $"Started {request.ExecutablePath}."));
            return true;
        }

        private void InitializeProgramRegisterFrame()
        {
            Array.Clear(_machine.Cpu.State.D);
            Array.Clear(_machine.Cpu.State.A);
        }

        private void EnableWorkbenchProgramInterrupts()
        {
            var cycle = _machine.Cpu.State.Cycles;
            _machine.Bus.WriteWord(0x00DFF09A, (ushort)(0x8000 | 0x4000 | AmigaConstants.IntreqVerticalBlank), cycle);
            _machine.Bus.SynchronizePaulaThrough(cycle);
        }

        private void ApplyWorkbenchLanguageSelectionIfNeeded()
        {
            if (_workbenchLanguageSelectionApplied ||
                !_workbenchLanguageSelectionIndex.HasValue)
            {
                return;
            }

            if (_machine.Bus.ExpansionRam.Length == 0 ||
                _machine.Cpu.State.ProgramCounter != _machine.Bus.ExpansionRamBase)
            {
                return;
            }

            var pc = _machine.Cpu.State.ProgramCounter;
            var d0 = _machine.Cpu.State.D[0];
            if ((d0 & 0xFF) == 0xFF)
            {
                _machine.Cpu.State.D[0] = (d0 & 0xFFFF_FF00) | (uint)_workbenchLanguageSelectionIndex.Value;
                _diagnostics.Add(new AmigaBootDiagnostic(
                    "AMIGA_BOOT_LANGUAGE_SELECTION",
                    $"Applied Workbench language selection {_workbenchLanguageSelectionIndex.Value} at PC=0x{pc:X6}."));
            }

            _workbenchLanguageSelectionApplied = true;
        }

        private static IReadOnlyList<string> NormalizeToolTypes(IEnumerable<string> toolTypes)
        {
            var normalized = new List<string>();
            foreach (var toolType in toolTypes)
            {
                var separator = toolType.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var key = NormalizeWorkbenchToolTypeKey(toolType.Substring(0, separator));

                if (key.Length == 0)
                {
                    continue;
                }

                normalized.Add(key + "=" + toolType.Substring(separator + 1));
            }

            return normalized;
        }

        private static int? FindWorkbenchLanguageSelectionIndex(IEnumerable<string> toolTypes)
        {
            foreach (var toolType in toolTypes)
            {
                var separator = toolType.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var key = NormalizeWorkbenchToolTypeKey(toolType.Substring(0, separator));
                if (TryGetLanguageSelectionIndex(key, out var selection))
                {
                    return selection;
                }
            }

            return null;
        }

        internal static string BuildCliArguments(IEnumerable<string> toolTypes)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var toolType in toolTypes)
            {
                var separator = toolType.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var key = NormalizeWorkbenchToolTypeKey(toolType.Substring(0, separator));
                var value = toolType.Substring(separator + 1);

                if (key.Length != 0)
                {
                    if (TryNormalizeLanguageSelection(key, value, out var selectedLanguage))
                    {
                        values["LANGUAGES"] = selectedLanguage;
                    }
                    else
                    {
                        values[key] = value;
                    }
                }
            }

            var builder = new StringBuilder();
            foreach (var key in new[]
            {
                "CODE",
                "DATA",
                "CHIP",
                "EXCHIP",
                "ANY",
                "EXANY",
                "TEMP",
                "RAMDISK",
                "LANGUAGES",
                "PARAM1",
                "PARAM2",
                "PARAM3",
                "PARAM4",
                "PARAM5",
                "CIAA_TIMERA",
                "CIAA_TIMERB",
                "CIAB_TIMERA",
                "CIAB_TIMERB",
                "INT_PORTS",
                "INT_VBLANK",
                "INT_EXTER",
                "INT_COPPER",
                "INT_BLITTER",
                "CACR_INST",
                "CACR_IBE",
                "CACR_DATA",
                "CACR_DBE",
                "CACR_COPYBACK"
            })
            {
                if (!values.TryGetValue(key, out var value))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(key);
                builder.Append(' ');
                builder.Append(value);
            }

            foreach (var key in new[]
            {
                "RELOCATE",
                "UNPACK",
                "KILLSYS",
                "SERIAL",
                "PARALLEL",
                "AUDIO",
                "FLOPPY",
                "POTGO",
                "CLOSEWB",
                "RETAPPWIN",
                "INFO"
            })
            {
                if (!values.TryGetValue(key, out var value) || !IsTruthyToolTypeValue(value))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(key);
            }

            builder.Append('\n');
            return builder.ToString();
        }

        private static string NormalizeWorkbenchToolTypeKey(string key)
        {
            key = key.Trim();
            while (key.Length > 0 && (key[0] == '$' || key[0] == '.'))
            {
                key = key.Substring(1).TrimStart();
            }

            return key;
        }

        private static bool TryNormalizeLanguageSelection(string key, string value, out string selectedLanguage)
        {
            selectedLanguage = string.Empty;
            if (!TryGetLanguageSelectionIndex(key, out var selection))
            {
                return false;
            }

            var rawLanguages = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var languages = new List<string>();
            foreach (var rawLanguage in rawLanguages)
            {
                var language = rawLanguage.Trim();
                if (language.Length != 0)
                {
                    languages.Add(language);
                }
            }
            if (selection >= languages.Count)
            {
                return false;
            }

            selectedLanguage = languages[selection];
            return true;
        }

        private static bool TryGetLanguageSelectionIndex(string key, out int selection)
        {
            selection = -1;
            var languageSuffix = "LANGUAGES";
            if (key.Length <= languageSuffix.Length ||
                !key.EndsWith(languageSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var selectionText = key.Substring(0, key.Length - languageSuffix.Length);
            return int.TryParse(selectionText, out selection) && selection >= 0;
        }

        private static bool IsTruthyToolTypeValue(string value)
        {
            return value.Trim().Equals("YES", StringComparison.OrdinalIgnoreCase) ||
                value.Trim().Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
                value.Trim() == "1";
        }

        private bool TryReadDosFile(string path, out byte[] data)
        {
            path = ResolveAssignedDosPath(path);
            if (TryReadDosFileFromResolvedPath(path, out data))
            {
                return true;
            }

            if (_workbenchCurrentDirectory.Length != 0 &&
                path.IndexOf(':') < 0 &&
                path.IndexOf('/') < 0 &&
                path.IndexOf('\\') < 0)
            {
                return TryReadDosFileFromResolvedPath(
                    AmigaDosFileSystem.CombinePath(_workbenchCurrentDirectory, path),
                    out data);
            }

            data = Array.Empty<byte>();
            return false;
        }

        private bool TryFindDosEntry(string path, out AmigaDosDirectoryEntry entry)
        {
            path = ResolveAssignedDosPath(path);
            if (TryFindDosEntryFromResolvedPath(path, out entry))
            {
                return true;
            }

            if (_workbenchCurrentDirectory.Length != 0 &&
                path.IndexOf(':') < 0 &&
                path.IndexOf('/') < 0 &&
                path.IndexOf('\\') < 0)
            {
                return TryFindDosEntryFromResolvedPath(
                    AmigaDosFileSystem.CombinePath(_workbenchCurrentDirectory, path),
                    out entry);
            }

            entry = default;
            return false;
        }

        private bool TryReadDosFileFromResolvedPath(string path, out byte[] data)
        {
            if (TryParseDrivePath(path, out var driveIndex, out var drivePath))
            {
                return TryReadDosFileFromDrive(driveIndex, drivePath, out data);
            }

            if (TryReadDosFileFromDrive(0, path, out data))
            {
                return true;
            }

            for (var index = 1; index < _machine.Bus.Disk.ConnectedDriveCount; index++)
            {
                if (TryReadDosFileFromDrive(index, path, out data))
                {
                    return true;
                }
            }

            data = Array.Empty<byte>();
            return false;
        }

        private bool TryFindDosEntryFromResolvedPath(string path, out AmigaDosDirectoryEntry entry)
        {
            if (TryParseDrivePath(path, out var driveIndex, out var drivePath))
            {
                return TryFindDosEntryFromDrive(driveIndex, drivePath, out entry);
            }

            if (TryFindDosEntryFromDrive(0, path, out entry))
            {
                return true;
            }

            for (var index = 1; index < _machine.Bus.Disk.ConnectedDriveCount; index++)
            {
                if (TryFindDosEntryFromDrive(index, path, out entry))
                {
                    return true;
                }
            }

            entry = default;
            return false;
        }

        private bool TryReadDosFileFromDrive(int driveIndex, string path, out byte[] data)
        {
            if (TryGetDosFileSystem(driveIndex, out var fileSystem) &&
                fileSystem.TryReadFile(path, out data))
            {
                return true;
            }

            data = Array.Empty<byte>();
            return false;
        }

        private bool TryFindDosEntryFromDrive(int driveIndex, string path, out AmigaDosDirectoryEntry entry)
        {
            if (TryGetDosFileSystem(driveIndex, out var fileSystem) &&
                fileSystem.TryFindEntry(path, out entry))
            {
                return true;
            }

            entry = default;
            return false;
        }

        private string ResolveAssignedDosPath(string path)
        {
            path = (path ?? string.Empty).Trim().Trim('"').Replace('\\', '/');
            var colon = path.IndexOf(':');
            if (colon >= 0)
            {
                var assignName = NormalizeAssignName(path.Substring(0, colon));
                var suffix = path.Substring(colon + 1).TrimStart('/');
                if (TryParseDrivePrefix(assignName, out var driveIndex))
                {
                    return $"DF{driveIndex}:{NormalizeHostDosPath(suffix)}";
                }

                if (assignName.Length != 0 &&
                    _dosAssigns.TryGetValue(assignName, out var target))
                {
                    return CombineHostDosPath(ResolveRamDirectorySource(target), suffix);
                }

                for (var mountedDrive = 0; mountedDrive < _machine.Bus.Disk.ConnectedDriveCount; mountedDrive++)
                {
                    if (TryGetDosFileSystem(mountedDrive, out var fileSystem) &&
                        assignName.Equals(fileSystem.VolumeName, StringComparison.OrdinalIgnoreCase))
                    {
                        return $"DF{mountedDrive}:{NormalizeHostDosPath(suffix)}";
                    }
                }
            }

            return ResolveRamDirectorySource(path);
        }

        private string ResolveRamDirectorySource(string path)
        {
            path = NormalizeHostDosAssignTarget(path);
            if (TryParseDrivePath(path, out _, out _))
            {
                return path;
            }

            foreach (var pair in _ramDirectorySources)
            {
                if (path.Equals(pair.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return string.IsNullOrWhiteSpace(pair.Value) ? path : pair.Value;
                }

                if (path.StartsWith(pair.Key + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return string.IsNullOrWhiteSpace(pair.Value)
                        ? path
                        : CombineHostDosPath(pair.Value, path.Substring(pair.Key.Length + 1));
                }
            }

            return path;
        }

        private static string CombineHostDosPath(string parentPath, string suffix)
        {
            if (TryParseDrivePath(parentPath, out var driveIndex, out var drivePath))
            {
                var combinedDrivePath = CombineHostDosPath(drivePath, suffix);
                return $"DF{driveIndex}:{combinedDrivePath}";
            }

            parentPath = NormalizeHostDosPath(parentPath);
            suffix = NormalizeHostDosPath(suffix);
            if (parentPath.Length == 0)
            {
                return suffix;
            }

            return suffix.Length == 0 ? parentPath : parentPath + "/" + suffix;
        }

        private static string NormalizeHostDosPath(string path)
        {
            return AmigaDosFileSystem.NormalizeDisplayPath(path ?? string.Empty);
        }

        private static string NormalizeHostDosAssignTarget(string path)
        {
            path = (path ?? string.Empty).Trim().Trim('"').Replace('\\', '/');
            return TryParseDrivePath(path, out var driveIndex, out var drivePath)
                ? $"DF{driveIndex}:{NormalizeHostDosPath(drivePath)}"
                : NormalizeHostDosPath(path);
        }

        private static string NormalizeAssignName(string assignName)
        {
            return (assignName ?? string.Empty).Trim().TrimEnd(':');
        }

        private static bool TryParseDrivePath(string path, out int driveIndex, out string drivePath)
        {
            driveIndex = -1;
            drivePath = string.Empty;
            path = (path ?? string.Empty).Trim().Trim('"').Replace('\\', '/');
            var colon = path.IndexOf(':');
            if (colon < 0 || !TryParseDrivePrefix(path.Substring(0, colon), out driveIndex))
            {
                return false;
            }

            drivePath = NormalizeHostDosPath(path.Substring(colon + 1));
            return true;
        }

        private static bool TryParseDrivePrefix(string prefix, out int driveIndex)
        {
            driveIndex = -1;
            prefix = NormalizeAssignName(prefix);
            if (prefix.Length != 3 ||
                (prefix[0] != 'D' && prefix[0] != 'd') ||
                (prefix[1] != 'F' && prefix[1] != 'f') ||
                prefix[2] is < '0' or > '3')
            {
                return false;
            }

            driveIndex = prefix[2] - '0';
            return true;
        }

        private AmigaDosFileSystem EnsureDosFileSystem()
            => EnsureDosFileSystem(0);

        private AmigaDosFileSystem EnsureDosFileSystem(int driveIndex)
        {
            if ((uint)driveIndex >= (uint)_dosFileSystems.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(driveIndex));
            }

            if (_dosFileSystems[driveIndex] != null)
            {
                return _dosFileSystems[driveIndex]!;
            }

            var drive = GetDrive(driveIndex);
            if (drive.Disk == null)
            {
                throw new AmigaEmulationException($"No disk is inserted in DF{driveIndex}:.");
            }

            _dosFileSystems[driveIndex] = new AmigaDosFileSystem(drive.Disk);
            return _dosFileSystems[driveIndex]!;
        }

        private bool TryGetDosFileSystem(int driveIndex, out AmigaDosFileSystem fileSystem)
        {
            fileSystem = null!;
            if ((uint)driveIndex >= (uint)_dosFileSystems.Length ||
                driveIndex >= _machine.Bus.Disk.ConnectedDriveCount)
            {
                return false;
            }

            try
            {
                fileSystem = EnsureDosFileSystem(driveIndex);
                return true;
            }
            catch (Exception ex) when (ex is AmigaEmulationException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                return false;
            }
        }

        private AmigaFloppyDrive GetDrive(int driveIndex)
        {
            return driveIndex switch
            {
                0 => Drive0,
                1 => Drive1,
                2 => Drive2,
                3 => Drive3,
                _ => throw new ArgumentOutOfRangeException(nameof(driveIndex))
            };
        }

        private void InstallKickstartMemoryList()
        {
            var hasPseudoFast = _machine.Bus.ExpansionRam.Length != 0;
            var hasRealFast = _machine.Bus.RealFastRam.Length != 0;
            var listAddress = GetActiveExecBase() + ExecMemListOffset;
            if (hasRealFast)
            {
                var realMetadataBase = _machine.Bus.RealFastRamBase;
                _fastMemHeaderAddress = realMetadataBase;
                _fastMemNameAddress = realMetadataBase + 0x40;
                _fastMemLower = realMetadataBase + BootRealFastMetadataSize;
                _fastMemUpper = realMetadataBase + (uint)_machine.Bus.RealFastRam.Length;
            }
            else
            {
                _fastMemHeaderAddress = 0;
                _fastMemNameAddress = 0;
                _fastMemLower = 0;
                _fastMemUpper = 0;
            }

            if (hasPseudoFast)
            {
                var pseudoBase = _machine.Bus.ExpansionRamBase;
                if (hasRealFast)
                {
                    var metadataBase = _machine.Bus.RealFastRamBase;
                    _pseudoFastMemHeaderAddress = metadataBase + 0x80;
                    _pseudoFastMemNameAddress = metadataBase + 0xC0;
                    _chipMemHeaderAddress = metadataBase + 0x100;
                    _chipMemNameAddress = metadataBase + 0x140;
                    _currentTaskAddress = metadataBase + 0x180;
                    _pseudoFastMemLower = pseudoBase + (_kickstartRomBootActive ? BootKickstartRomPseudoFastReserve : 0);
                }
                else
                {
                    var metadataBase = pseudoBase;
                    _pseudoFastMemHeaderAddress = metadataBase;
                    _chipMemHeaderAddress = metadataBase + 0x40;
                    _pseudoFastMemNameAddress = metadataBase + 0x80;
                    _chipMemNameAddress = metadataBase + 0x90;
                    _currentTaskAddress = metadataBase + BootPseudoFastCurrentTaskOffset;
                    _pseudoFastMemLower = metadataBase + (_kickstartRomBootActive ? BootKickstartRomPseudoFastReserve : BootPseudoFastMetadataSize);
                }

                _pseudoFastMemUpper = pseudoBase + (uint)_machine.Bus.ExpansionRam.Length - BootPseudoFastStackReserve;
                _chipMemLower = BootChipPublicLowerAddress;
                _chipMemUpper = (uint)_machine.Bus.ChipRam.Length;
            }
            else if (hasRealFast)
            {
                var metadataBase = _machine.Bus.RealFastRamBase;
                _chipMemHeaderAddress = metadataBase + 0x80;
                _chipMemNameAddress = metadataBase + 0xC0;
                _currentTaskAddress = metadataBase + 0x100;
                _pseudoFastMemHeaderAddress = 0;
                _pseudoFastMemNameAddress = 0;
                _pseudoFastMemLower = 0;
                _pseudoFastMemUpper = 0;
                _chipMemLower = BootChipPublicLowerAddress;
                _chipMemUpper = (uint)_machine.Bus.ChipRam.Length;
            }
            else
            {
                var privateBase = GetChipOnlyPrivateMetadataBase();
                _currentTaskAddress = privateBase;
                _chipMemHeaderAddress = privateBase + BootChipOnlyMemHeaderOffset;
                _chipMemNameAddress = privateBase + BootChipOnlyMemNameOffset;
                _pseudoFastMemHeaderAddress = 0;
                _pseudoFastMemNameAddress = 0;
                _pseudoFastMemLower = 0;
                _pseudoFastMemUpper = 0;
                _chipMemLower = BootChipPublicLowerAddress;
                _chipMemUpper = privateBase;
            }

            PlanTlsfControlStorage(hasRealFast, hasPseudoFast);

            var execImage = new byte[ExecBaseImageSize];
            var firstHeader = hasRealFast
                ? _fastMemHeaderAddress
                : hasPseudoFast
                    ? _pseudoFastMemHeaderAddress
                    : _chipMemHeaderAddress;
            var lastHeader = _chipMemHeaderAddress;
            WriteExecBaseStaticFields(execImage);
            BigEndian.WriteUInt32(execImage, ExecThisTaskOffset, _currentTaskAddress);
            BigEndian.WriteUInt32(execImage, ExecTaskTrapCodeOffset, DefaultTaskTrapCodeAddress);
            BigEndian.WriteUInt16(execImage, ExecTaskTrapAllocOffset, 0);
            BigEndian.WriteUInt32(execImage, ExecMemListOffset, firstHeader);
            BigEndian.WriteUInt32(execImage, ExecMemListOffset + 4, 0);
            BigEndian.WriteUInt32(execImage, ExecMemListOffset + 8, lastHeader);
            execImage[ExecMemListOffset + 12] = 0;
            execImage[ExecMemListOffset + 13] = 0;
			var memoryHandlers = global::Amiga.ExecLayout.ExecBase.ExMemHandlers;
			BigEndian.WriteUInt32(execImage, memoryHandlers, AmigaKickstartHost.ExecLibraryBase + (uint)memoryHandlers + 4);
			BigEndian.WriteUInt32(execImage, memoryHandlers + 4, 0);
			BigEndian.WriteUInt32(execImage, memoryHandlers + 8, AmigaKickstartHost.ExecLibraryBase + (uint)memoryHandlers);
            _machine.Bus.MapWritableMemory(AmigaKickstartHost.ExecLibraryBase, execImage);
            // InstallSyntheticExec registers vectors before the public memory
            // image is mapped. Mapping that image replaces the installer's
            // provisional list bytes, so publish the SDK-defined scheduler
            // lists again before AddTask or a Layers wait can enqueue a task.
            InitializeExecList(
                AmigaKickstartHost.ExecLibraryBase +
                    (uint)global::Amiga.ExecLayout.ExecBase.TaskReady);
            InitializeExecList(
                AmigaKickstartHost.ExecLibraryBase +
                    (uint)global::Amiga.ExecLayout.ExecBase.TaskWait);
            WriteInitialTask();

            if (hasRealFast)
            {
                WriteInitialMemoryHeader(
                    _fastMemHeaderAddress,
                    hasPseudoFast ? _pseudoFastMemHeaderAddress : _chipMemHeaderAddress,
                    listAddress,
                    MemfPublic | MemfFast,
                    _fastMemLower,
                    _fastMemUpper,
                    _fastMemNameAddress,
                    "real-fast",
                    _tlsfFastControlAddress);
            }

            if (hasPseudoFast)
            {
                WriteInitialMemoryHeader(
                    _pseudoFastMemHeaderAddress,
                    _chipMemHeaderAddress,
                    hasRealFast ? _fastMemHeaderAddress : listAddress,
                    MemfPublic | MemfFast,
                    _pseudoFastMemLower,
                    _pseudoFastMemUpper,
                    _pseudoFastMemNameAddress,
                    "pseudo-fast",
                    _tlsfPseudoFastControlAddress);
            }

            if (hasRealFast || hasPseudoFast)
            {
                var predecessor = hasPseudoFast
                    ? _pseudoFastMemHeaderAddress
                    : _fastMemHeaderAddress;
                WriteInitialMemoryHeader(
                    _chipMemHeaderAddress,
                    listAddress + 4,
                    predecessor,
                    MemfPublic | MemfChip,
                    _chipMemLower,
                    _chipMemUpper,
                    _chipMemNameAddress,
                    "chip",
                    _tlsfChipControlAddress);
            }
            else
            {
                WriteInitialMemoryHeader(
                    _chipMemHeaderAddress,
                    listAddress + 4,
                    listAddress,
                    MemfPublic | MemfChip,
                    _chipMemLower,
                    _chipMemUpper,
                    _chipMemNameAddress,
                    "chip",
                    _tlsfChipControlAddress);
            }

            _memoryListInstalled = true;
            EnsureAbsExecBasePointer();
            if (_kickstartRomBootActive)
            {
                _machine.Bus.StrictCpuPhysicalDataMapping = true;
            }
        }

        private void PlanTlsfControlStorage(bool hasRealFast, bool hasPseudoFast)
        {
            _tlsfChipControlAddress = 0;
            _tlsfFastControlAddress = 0;
            _tlsfPseudoFastControlAddress = 0;
            if (_memoryAllocator == PortableExecMemory.ExecMemoryAllocatorKind.Classic ||
                (!hasRealFast && !hasPseudoFast))
            {
                return;
            }

            // Keep the TLSF tables in the first usable bytes of a fast arena.
            // The arena's public lower bound is advanced after reservation, so
            // no ordinary allocation can overlap the metadata.
            var storageUpper = hasRealFast ? _fastMemUpper : _pseudoFastMemUpper;
            var cursor = Align(hasRealFast ? _fastMemLower : _pseudoFastMemLower, 16);
            var reservedAny = false;

            if (hasRealFast && TryReserveTlsfControl(
                    _fastMemHeaderAddress, _fastMemLower, _fastMemUpper,
                    storageUpper, ref cursor, out var fastControl))
            {
                _tlsfFastControlAddress = fastControl;
                reservedAny = true;
            }

            if (hasPseudoFast && TryReserveTlsfControl(
                    _pseudoFastMemHeaderAddress, _pseudoFastMemLower, _pseudoFastMemUpper,
                    storageUpper, ref cursor, out var pseudoFastControl))
            {
                _tlsfPseudoFastControlAddress = pseudoFastControl;
                reservedAny = true;
            }

            if ((hasRealFast || hasPseudoFast) && TryReserveTlsfControl(
                    _chipMemHeaderAddress, _chipMemLower, _chipMemUpper,
                    storageUpper, ref cursor, out var chipControl))
            {
                _tlsfChipControlAddress = chipControl;
                reservedAny = true;
            }

            if (!reservedAny)
            {
                return;
            }

            if (hasRealFast)
            {
                _fastMemLower = Math.Max(_fastMemLower, cursor);
            }
            else
            {
                _pseudoFastMemLower = Math.Max(_pseudoFastMemLower, cursor);
            }
        }

        private static bool TryReserveTlsfControl(
            uint headerAddress,
            uint lower,
            uint upper,
            uint storageUpper,
            ref uint cursor,
            out uint controlAddress)
        {
            controlAddress = 0;
            if (!PortableExecMemory.TlsfPolicy.IsConversionEligible(
                    global::Amiga.APTR.FromPointer(headerAddress),
                    global::Amiga.APTR.FromPointer(lower),
                    global::Amiga.APTR.FromPointer(upper),
                    global::Amiga.APTR.FromPointer(cursor)))
            {
                return false;
            }

            var controlBytes = (uint)PortableExecMemory.TlsfPolicy.ControlBytes;
            if (cursor > storageUpper || controlBytes > storageUpper - cursor)
            {
                return false;
            }

            controlAddress = cursor;
            cursor = Align(cursor + controlBytes, 16);
            return true;
        }

        private void EnsureAbsExecBasePointer()
        {
            _machine.Bus.WriteLong(AbsExecBaseAddress, AmigaKickstartHost.ExecLibraryBase);
        }

        private void EnsureHostLowMemoryPointersCurrent()
        {
            if (!_memoryListInstalled ||
                _machine.Bus.ReadLong(AbsExecBaseAddress) == AmigaKickstartHost.ExecLibraryBase)
            {
                return;
            }

            EnsureAbsExecBasePointer();
        }

        private void WriteExecBaseStaticFields(Span<byte> execImage)
        {
            var maxLocalMemory = AlignDown((uint)_machine.Bus.ChipRam.Length, 4);
            var maxExtendedMemory = 0u;
            if (_machine.Bus.ExpansionRam.Length != 0)
            {
                maxExtendedMemory = Math.Max(
                    maxExtendedMemory,
                    AlignDown(_machine.Bus.ExpansionRamBase + (uint)_machine.Bus.ExpansionRam.Length, 4));
            }

            if (_machine.Bus.RealFastRam.Length != 0)
            {
                maxExtendedMemory = Math.Max(
                    maxExtendedMemory,
                    AlignDown(_machine.Bus.RealFastRamBase + (uint)_machine.Bus.RealFastRam.Length, 4));
            }

            BigEndian.WriteUInt32(execImage, 0x00, AmigaKickstartHost.ExecLibraryBase);
            BigEndian.WriteUInt16(execImage, ExecSoftVerOffset, Kickstart13SoftVer);
            BigEndian.WriteUInt16(execImage, ExecLowMemChkSumOffset, CalculateLowMemoryVectorChecksum());
            BigEndian.WriteUInt32(execImage, ExecChkBaseOffset, ~AmigaKickstartHost.ExecLibraryBase);
            BigEndian.WriteUInt32(execImage, ExecSysStkUpperOffset, BootSupervisorStackTopAddress);
            BigEndian.WriteUInt32(execImage, ExecSysStkLowerOffset, 0);
            BigEndian.WriteUInt32(execImage, ExecMaxLocMemOffset, maxLocalMemory);
            BigEndian.WriteUInt32(execImage, ExecMaxExtMemOffset, maxExtendedMemory);
            // The synthetic scheduler gates dispatch on the public ExecBase
            // nesting counters.  This image is recreated from zeroed storage,
            // so publish their enabled (-1) state through the SDK layout rather
            // than relying on a host-local offset or an earlier bootstrap image.
            execImage[global::Amiga.ExecLayout.ExecBase.IDNestCount] =
                ExecNestingEnabled;
            execImage[global::Amiga.ExecLayout.ExecBase.TaskDisableNestCount] =
                ExecNestingEnabled;
            BigEndian.WriteUInt16(execImage, ExecChkSumOffset, CalculateExecBaseStaticChecksum(execImage));
        }

        private void WriteInitialTask()
        {
            var taskAddress = _currentTaskAddress != 0 ? _currentTaskAddress : AmigaKickstartHost.ExecStructAddress;
            var taskNameAddress = taskAddress + 0x70;
            var stackUpper = AlignDown((uint)Math.Max(0, _machine.Bus.ChipRam.Length - BootPseudoFastStackReserve), 4);
            var stackPointer = stackUpper >= 4 ? stackUpper - 4 : 0;
            _machine.Bus.ClearMemory(taskAddress, 0x80);
            _machine.Bus.WriteByte(taskAddress + TaskNodeTypeOffset, 1, 0);
            _machine.Bus.WriteLong(taskAddress + TaskNodeNameOffset, taskNameAddress);
            _machine.Bus.WriteWord(taskAddress + TaskTrapAllocOffset, 0);
            _machine.Bus.WriteWord(taskAddress + TaskTrapAbleOffset, 0);
            _machine.Bus.WriteLong(taskAddress + TaskTrapCodeOffset, DefaultTaskTrapCodeAddress);
            _machine.Bus.WriteByte(
                taskAddress + (uint)global::Amiga.ExecLayout.Task.State,
                (byte)global::Amiga.TaskState.Running,
                0);
            _machine.Bus.WriteLong(taskAddress + TaskStackPointerOffset, stackPointer);
            _machine.Bus.WriteLong(taskAddress + TaskStackLowerOffset, BootChipPublicLowerAddress);
            _machine.Bus.WriteLong(taskAddress + TaskStackUpperOffset, stackUpper);
            _machine.Bus.CopyToMemory(taskNameAddress, Encoding.ASCII.GetBytes("CopperStart\0"));
        }

        private void WriteInitialMemoryHeader(
            uint headerAddress,
            uint successor,
            uint predecessor,
            uint attributes,
            uint lower,
            uint upper,
            uint nameAddress,
            string name,
            uint tlsfControlAddress = 0)
        {
            _machine.Bus.ClearMemory(headerAddress, 0x40);
            _machine.Bus.WriteLong(headerAddress, successor);
            _machine.Bus.WriteLong(headerAddress + 4, predecessor);
            _machine.Bus.WriteByte(headerAddress + 8, 10, 0);
            _machine.Bus.WriteByte(headerAddress + 9, 0, 0);
            _machine.Bus.WriteLong(headerAddress + MemNodeNameOffset, nameAddress);
            _machine.Bus.WriteWord(headerAddress + MemHeaderAttributesOffset, (ushort)attributes);
            WriteFixedAscii(nameAddress, name, 16);
            var platform = CreatePortableMemoryPlatform();
            if (_memoryAllocator == PortableExecMemory.ExecMemoryAllocatorKind.Classic)
            {
                _machine.Bus.WriteLong(headerAddress + MemHeaderLowerOffset, lower);
                _machine.Bus.WriteLong(headerAddress + MemHeaderUpperOffset, upper);
                var freeBytes = upper > lower ? AlignDown(upper - lower, 8) : 0;
                _machine.Bus.WriteLong(headerAddress + MemHeaderFirstChunkOffset, freeBytes == 0 ? 0 : lower);
                _machine.Bus.WriteLong(headerAddress + MemHeaderFreeOffset, freeBytes);
                if (freeBytes != 0)
                {
                    _machine.Bus.WriteLong(lower + MemChunkNextOffset, 0);
                    _machine.Bus.WriteLong(lower + MemChunkBytesOffset, freeBytes);
                }
            }
            else
            {
                var policy = new PortableExecMemory.TlsfPolicy();
                policy.Initialize(
                    ref platform,
                    headerAddress,
                    lower,
                    upper,
                    tlsfControlAddress == 0
                        ? global::Amiga.APTR.Null
                        : global::Amiga.APTR.FromPointer(tlsfControlAddress));
            }
        }

        private AmigaBusExecMemoryPlatform CreatePortableMemoryPlatform()
            => new(_machine.Bus, _portableCurrentTask, _portableMemoryAlert,
				_activeExecMemoryGatewayState, _portableMemoryHandler,
				_portableExpungeLibraries, bootstrapCpuProfile: GetBootstrapCpuProfile());

		private PortableExecMemory.BootstrapCpuProfile GetBootstrapCpuProfile()
		{
			var backend = _machine.Options.CpuBackend;
			if (backend is M68kBackendKind.AccurateM68040 or
				M68kBackendKind.JitM68040)
				return new PortableExecMemory.BootstrapCpuProfile(
					PortableExecMemory.BootstrapCpuFamily.M68040,
					PortableExecMemory.BootstrapCpuCapabilities.Baseline68000 |
					PortableExecMemory.BootstrapCpuCapabilities.Vbr |
					PortableExecMemory.BootstrapCpuCapabilities.Cacr |
					PortableExecMemory.BootstrapCpuCapabilities.Mmu |
					PortableExecMemory.BootstrapCpuCapabilities.Caches |
					PortableExecMemory.BootstrapCpuCapabilities.LongAddress);
			if (backend == M68kBackendKind.AccurateM68030)
				return new PortableExecMemory.BootstrapCpuProfile(
					PortableExecMemory.BootstrapCpuFamily.M68030,
					PortableExecMemory.BootstrapCpuCapabilities.Baseline68000 |
					PortableExecMemory.BootstrapCpuCapabilities.Vbr |
					PortableExecMemory.BootstrapCpuCapabilities.Cacr |
					PortableExecMemory.BootstrapCpuCapabilities.Mmu |
					PortableExecMemory.BootstrapCpuCapabilities.LongAddress);
			if (backend == M68kBackendKind.AccurateM68EC020)
				return new PortableExecMemory.BootstrapCpuProfile(
					PortableExecMemory.BootstrapCpuFamily.M68EC020,
					PortableExecMemory.BootstrapCpuCapabilities.Baseline68000 |
					PortableExecMemory.BootstrapCpuCapabilities.Vbr |
					PortableExecMemory.BootstrapCpuCapabilities.Cacr);
			if (backend == M68kBackendKind.AccurateM68020)
				return new PortableExecMemory.BootstrapCpuProfile(
					PortableExecMemory.BootstrapCpuFamily.M68020,
					PortableExecMemory.BootstrapCpuCapabilities.Baseline68000 |
					PortableExecMemory.BootstrapCpuCapabilities.Vbr |
					PortableExecMemory.BootstrapCpuCapabilities.Cacr |
					PortableExecMemory.BootstrapCpuCapabilities.LongAddress);
			return new PortableExecMemory.BootstrapCpuProfile(
				PortableExecMemory.BootstrapCpuFamily.M68000,
				PortableExecMemory.BootstrapCpuCapabilities.Baseline68000);
		}

		private void ExpungeDelayedLibraries(global::Amiga.APTR execBase)
		{
			if (execBase.IsNull)
				return;
			var list = execBase.Raw + (uint)global::Amiga.ExecLayout.ExecBase.LibraryList;
			var tail = list + (uint)global::Amiga.ExecLayout.List.Tail;
			if (!_machine.Bus.IsMappedMemoryRange(list, (int)global::Amiga.List.Size))
				return;
			var library = _machine.Bus.ReadLong(list + (uint)global::Amiga.ExecLayout.List.Head);
			for (var visited = 0; library != 0 && library != tail && visited < 4096; visited++)
			{
				if (!_machine.Bus.IsMappedMemoryRange(library, (int)global::Amiga.Library.Size))
					return;
				var next = _machine.Bus.ReadLong(library + (uint)global::Amiga.ExecLayout.Node.Successor);
				var flags = (global::Amiga.LibraryFlags)_machine.Bus.ReadByte(
					library + (uint)global::Amiga.ExecLayout.Library.Flags);
				var openCount = _machine.Bus.ReadWord(library + (uint)global::Amiga.ExecLayout.Library.OpenCount);
				if (openCount == 0 && (flags & global::Amiga.LibraryFlags.DelayedExpunge) != 0)
					_ = TryInvokeLibraryExpunge(library);
				if (next == library)
					return;
				library = next;
			}
		}

		private bool TryInvokeLibraryExpunge(uint library)
		{
			const int expungeLvo = -18;
			var code = unchecked(library + (uint)expungeLvo);
			var state = _activeExecMemoryGatewayState ?? _machine.Cpu.State;
			if (state.A[7] < 4 ||
				!_machine.Bus.IsCpuPhysicalAddressMapped(code, 2, AmigaBusAccessKind.CpuInstructionFetch) ||
				!_machine.Bus.IsMappedMemoryRange(state.A[7] - 4, 4))
				return false;

			var originalProgramCounter = state.ProgramCounter;
			var originalStackPointer = state.A[7];
			var originalA6 = state.A[6];
			try
			{
				state.A[6] = library;
				state.A[7] = originalStackPointer - 4;
				_machine.Bus.WriteLong(state.A[7], MemoryHandlerReturnSentinel, state.Cycles);
				state.ProgramCounter = code + 6;
				if (_machine.Bus.TryInvokeHostGatewayAt(code, state) && state.ProgramCounter == code + 6)
				{
					state.ProgramCounter = _machine.Bus.ReadLong(state.A[7]);
					state.A[7] += 4;
				}
				else if (state.ProgramCounter == code + 6)
				{
					state.ProgramCounter = code;
				}

				for (var instruction = 0;
					state.ProgramCounter != MemoryHandlerReturnSentinel &&
					!state.Halted && instruction < MaximumMemoryHandlerInstructions;
					instruction++)
				{
					_machine.Cpu.ExecuteInstruction();
				}
				var completed = state.ProgramCounter == MemoryHandlerReturnSentinel &&
					state.A[7] == originalStackPointer;
				if (!completed)
					_diagnostics.Add(new AmigaBootDiagnostic(
						"AMIGA_EXEC_LIBRARY_EXPUNGE",
						$"Delayed expunge vector at ${code:X8} did not return normally."));
				return completed;
			}
			finally
			{
				state.ProgramCounter = originalProgramCounter;
				state.A[7] = originalStackPointer;
				state.A[6] = originalA6;
			}
		}

		private global::Amiga.MemoryHandlerResult InvokeGuestMemoryHandler(
			M68kCpuState? callbackState,
			global::Amiga.APTR code,
			global::Amiga.APTR interruptData,
			global::Amiga.APTR handlerData,
			global::Amiga.APTR execBase)
		{
			var state = callbackState ?? _machine.Cpu.State;
			if (code.IsNull || state.A[7] < 4 ||
				!_machine.Bus.IsCpuPhysicalAddressMapped(code.Raw, 2, AmigaBusAccessKind.CpuInstructionFetch) ||
				!_machine.Bus.IsMappedMemoryRange(state.A[7] - 4, 4))
				return global::Amiga.MemoryHandlerResult.DidNothing;

			var originalProgramCounter = state.ProgramCounter;
			var originalStackPointer = state.A[7];
			var originalA6 = state.A[6];
			var completed = false;
			try
			{
				state.A[0] = handlerData.Raw;
				state.A[1] = interruptData.Raw;
				state.A[6] = execBase.Raw;
				state.A[7] = originalStackPointer - 4;
				_machine.Bus.WriteLong(state.A[7], MemoryHandlerReturnSentinel, state.Cycles);

				state.ProgramCounter = code.Raw + 6;
				if (_machine.Bus.TryInvokeHostGatewayAt(code.Raw, state) &&
					state.ProgramCounter == code.Raw + 6)
				{
					state.ProgramCounter = _machine.Bus.ReadLong(state.A[7]);
					state.A[7] += 4;
				}
				else if (state.ProgramCounter == code.Raw + 6)
				{
					state.ProgramCounter = code.Raw;
				}

				for (var instruction = 0;
					state.ProgramCounter != MemoryHandlerReturnSentinel &&
					!state.Halted && instruction < MaximumMemoryHandlerInstructions;
					instruction++)
				{
					_machine.Cpu.ExecuteInstruction();
				}
				completed = state.ProgramCounter == MemoryHandlerReturnSentinel &&
					state.A[7] == originalStackPointer;
				if (!completed)
				{
					_diagnostics.Add(new AmigaBootDiagnostic(
						"AMIGA_EXEC_MEMORY_HANDLER",
						$"Memory handler at ${code.Raw:X8} did not return normally."));
					return global::Amiga.MemoryHandlerResult.DidNothing;
				}

				return unchecked((int)state.D[0]) switch
				{
					(int)global::Amiga.MemoryHandlerResult.TryAgain => global::Amiga.MemoryHandlerResult.TryAgain,
					(int)global::Amiga.MemoryHandlerResult.AllDone => global::Amiga.MemoryHandlerResult.AllDone,
					_ => global::Amiga.MemoryHandlerResult.DidNothing
				};
			}
			finally
			{
				state.ProgramCounter = originalProgramCounter;
				state.A[7] = originalStackPointer;
				state.A[6] = originalA6;
				if (!completed)
					state.D[0] = 0;
			}
		}

        private uint AllocatePortableMemory(int byteCount, uint flags)
        {
            if (!_memoryListInstalled || byteCount <= 0) return 0;
            var platform = CreatePortableMemoryPlatform();
            var execBase = GetActiveExecBase();
			return _memoryAllocator == PortableExecMemory.ExecMemoryAllocatorKind.Classic
				? PortableExecMemory.ExecMemoryCore.AllocMem<AmigaBusExecMemoryPlatform, PortableExecMemory.ClassicPolicy>(
					ref platform, execBase, (uint)byteCount, (global::Amiga.Exec.MemoryFlags)flags).Raw
				: PortableExecMemory.ExecMemoryCore.AllocMem<AmigaBusExecMemoryPlatform, PortableExecMemory.TlsfPolicy>(
					ref platform, execBase, (uint)byteCount, (global::Amiga.Exec.MemoryFlags)flags).Raw;
        }

        private void FreePortableMemory(uint address, int byteCount)
        {
            if (!_memoryListInstalled || address == 0 || byteCount <= 0) return;
            var platform = CreatePortableMemoryPlatform();
            var execBase = GetActiveExecBase();
			if (_memoryAllocator == PortableExecMemory.ExecMemoryAllocatorKind.Classic)
				PortableExecMemory.ExecMemoryCore.FreeMem<AmigaBusExecMemoryPlatform, PortableExecMemory.ClassicPolicy>(
					ref platform, execBase, address, (uint)byteCount);
			else
				PortableExecMemory.ExecMemoryCore.FreeMem<AmigaBusExecMemoryPlatform, PortableExecMemory.TlsfPolicy>(
					ref platform, execBase, address, (uint)byteCount);
        }

        private uint AllocatePortableAbsoluteMemory(int byteCount, uint location)
        {
            if (!_memoryListInstalled || byteCount <= 0 || location == 0) return 0;
            var platform = CreatePortableMemoryPlatform();
			return _memoryAllocator == PortableExecMemory.ExecMemoryAllocatorKind.Classic
				? PortableExecMemory.ExecMemoryCore.AllocAbs<AmigaBusExecMemoryPlatform, PortableExecMemory.ClassicPolicy>(
					ref platform, GetActiveExecBase(), (uint)byteCount, location).Raw
				: PortableExecMemory.ExecMemoryCore.AllocAbs<AmigaBusExecMemoryPlatform, PortableExecMemory.TlsfPolicy>(
					ref platform, GetActiveExecBase(), (uint)byteCount, location).Raw;
        }

        private uint AllocatePortableFromHeader(uint headerAddress, int byteCount, uint flags)
        {
            if (byteCount <= 0) return 0;
            var platform = CreatePortableMemoryPlatform();
			return _memoryAllocator == PortableExecMemory.ExecMemoryAllocatorKind.Classic
				? PortableExecMemory.ExecMemoryCore.Allocate<AmigaBusExecMemoryPlatform, PortableExecMemory.ClassicPolicy>(
					ref platform, headerAddress, (uint)byteCount, (global::Amiga.Exec.MemoryFlags)flags).Raw
				: PortableExecMemory.ExecMemoryCore.Allocate<AmigaBusExecMemoryPlatform, PortableExecMemory.TlsfPolicy>(
					ref platform, headerAddress, (uint)byteCount, (global::Amiga.Exec.MemoryFlags)flags).Raw;
        }

        private void DeallocatePortableToHeader(uint headerAddress, uint address, int byteCount)
        {
            if (byteCount <= 0) return;
            var platform = CreatePortableMemoryPlatform();
			if (_memoryAllocator == PortableExecMemory.ExecMemoryAllocatorKind.Classic)
				PortableExecMemory.ExecMemoryCore.Deallocate<AmigaBusExecMemoryPlatform, PortableExecMemory.ClassicPolicy>(
					ref platform, headerAddress, address, (uint)byteCount);
			else
				PortableExecMemory.ExecMemoryCore.Deallocate<AmigaBusExecMemoryPlatform, PortableExecMemory.TlsfPolicy>(
					ref platform, headerAddress, address, (uint)byteCount);
        }

        private uint QueryPortableAvailableMemory(uint flags)
        {
            if (!_memoryListInstalled) return 0;
            var platform = CreatePortableMemoryPlatform();
			return _memoryAllocator == PortableExecMemory.ExecMemoryAllocatorKind.Classic
				? PortableExecMemory.ExecMemoryCore.AvailMem<AmigaBusExecMemoryPlatform, PortableExecMemory.ClassicPolicy>(
					ref platform, GetActiveExecBase(), (global::Amiga.Exec.MemoryFlags)flags)
				: PortableExecMemory.ExecMemoryCore.AvailMem<AmigaBusExecMemoryPlatform, PortableExecMemory.TlsfPolicy>(
					ref platform, GetActiveExecBase(), (global::Amiga.Exec.MemoryFlags)flags);
        }

        private uint TypeOfPortableMemory(uint address)
        {
            if (!_memoryListInstalled || address == 0) return 0;
            var platform = CreatePortableMemoryPlatform();
			return (uint)PortableExecMemory.ExecMemoryCore.TypeOfMem(
				ref platform, GetActiveExecBase(), (global::Amiga.APTR)address);
        }

        private uint AllocateFromMemoryHeader(uint headerAddress, int byteCount, uint flags)
        {
            if (byteCount <= 0 || !_machine.Bus.IsMappedMemoryRange(headerAddress, MemHeaderFreeOffset + 4)) return 0;
            var size = Align((uint)byteCount, 8);
            var previousLinkAddress = headerAddress + MemHeaderFirstChunkOffset;
            var chunkAddress = _machine.Bus.ReadLong(previousLinkAddress);
            while (chunkAddress != 0)
            {
                var next = _machine.Bus.ReadLong(chunkAddress + MemChunkNextOffset);
                var bytes = _machine.Bus.ReadLong(chunkAddress + MemChunkBytesOffset);
                if (bytes >= size)
                {
                    uint address; uint allocated;
                    if ((flags & MemfReverse) != 0 && bytes - size >= 8)
                    {
                        address = chunkAddress + bytes - size; allocated = size;
                        _machine.Bus.WriteLong(chunkAddress + MemChunkBytesOffset, bytes - size);
                    }
                    else if (bytes - size < 8)
                    {
                        address = chunkAddress; allocated = bytes;
                        _machine.Bus.WriteLong(previousLinkAddress, next);
                    }
                    else
                    {
                        address = chunkAddress; allocated = size;
                        var remaining = chunkAddress + size;
                        _machine.Bus.WriteLong(previousLinkAddress, remaining);
                        _machine.Bus.WriteLong(remaining + MemChunkNextOffset, next);
                        _machine.Bus.WriteLong(remaining + MemChunkBytesOffset, bytes - size);
                    }
                    var free = _machine.Bus.ReadLong(headerAddress + MemHeaderFreeOffset);
                    _machine.Bus.WriteLong(headerAddress + MemHeaderFreeOffset, free >= allocated ? free - allocated : 0);
                    if ((flags & MemfClear) != 0) _machine.Bus.ClearMemory(address, checked((int)allocated));
                    return address;
                }
                previousLinkAddress = chunkAddress + MemChunkNextOffset;
                chunkAddress = next;
            }
            return 0;
        }

        private uint AllocateMemoryFromMemList(int byteCount, uint flags)
        {
            if (!_memoryListInstalled || byteCount <= 0)
            {
                return 0;
            }

            var size = Align((uint)byteCount, 8);
            foreach (var headerAddress in EnumerateCompatibleMemoryHeaders(flags))
            {
                var allocatedFromHeader = AllocateFromMemoryHeader(headerAddress, byteCount, flags);
                if (allocatedFromHeader != 0) return allocatedFromHeader;
                var previousLinkAddress = headerAddress + MemHeaderFirstChunkOffset;
                var chunkAddress = _machine.Bus.ReadLong(previousLinkAddress);
                while (chunkAddress != 0)
                {
                    var nextChunkAddress = _machine.Bus.ReadLong(chunkAddress + MemChunkNextOffset);
                    var chunkBytes = _machine.Bus.ReadLong(chunkAddress + MemChunkBytesOffset);
                    if (chunkBytes >= size)
                    {
                        uint allocatedAddress;
                        uint allocatedBytes;
                        if ((flags & MemfReverse) != 0 && chunkBytes - size >= 8)
                        {
                            allocatedAddress = chunkAddress + chunkBytes - size;
                            allocatedBytes = size;
                            _machine.Bus.WriteLong(chunkAddress + MemChunkBytesOffset, chunkBytes - size);
                        }
                        else if (chunkBytes - size < 8)
                        {
                            allocatedAddress = chunkAddress;
                            allocatedBytes = chunkBytes;
                            _machine.Bus.WriteLong(previousLinkAddress, nextChunkAddress);
                        }
                        else
                        {
                            allocatedAddress = chunkAddress;
                            allocatedBytes = size;
                            var remainingChunkAddress = chunkAddress + size;
                            _machine.Bus.WriteLong(previousLinkAddress, remainingChunkAddress);
                            _machine.Bus.WriteLong(remainingChunkAddress + MemChunkNextOffset, nextChunkAddress);
                            _machine.Bus.WriteLong(remainingChunkAddress + MemChunkBytesOffset, chunkBytes - size);
                        }

                        var freeBytes = _machine.Bus.ReadLong(headerAddress + MemHeaderFreeOffset);
                        _machine.Bus.WriteLong(headerAddress + MemHeaderFreeOffset, freeBytes >= allocatedBytes ? freeBytes - allocatedBytes : 0);
                        if ((flags & MemfClear) != 0)
                        {
                            _machine.Bus.ClearMemory(allocatedAddress, checked((int)allocatedBytes));
                        }

                        return allocatedAddress;
                    }

                    previousLinkAddress = chunkAddress + MemChunkNextOffset;
                    chunkAddress = nextChunkAddress;
                }
            }

            return 0;
        }

        private uint AllocateAbsoluteMemoryFromMemList(int byteCount, uint location)
        {
            if (!_memoryListInstalled || byteCount <= 0 || location == 0)
            {
                return 0;
            }

            var size = Align((uint)byteCount, 8);
            var end = location + size;
            if (end <= location || !_machine.Bus.IsMappedMemoryRange(location, checked((int)size)))
            {
                return 0;
            }

            var headerAddress = FindOwningMemoryHeader(location, size);
            if (headerAddress == 0)
            {
                return 0;
            }

            var previousLinkAddress = headerAddress + MemHeaderFirstChunkOffset;
            var chunkAddress = _machine.Bus.ReadLong(previousLinkAddress);
            while (chunkAddress != 0)
            {
                var nextChunkAddress = _machine.Bus.ReadLong(chunkAddress + MemChunkNextOffset);
                var chunkBytes = _machine.Bus.ReadLong(chunkAddress + MemChunkBytesOffset);
                var chunkEnd = chunkAddress + chunkBytes;
                if (location >= chunkAddress && end <= chunkEnd)
                {
                    var beforeBytes = location - chunkAddress;
                    var afterBytes = chunkEnd - end;
                    if (beforeBytes >= 8)
                    {
                        _machine.Bus.WriteLong(previousLinkAddress, chunkAddress);
                        _machine.Bus.WriteLong(chunkAddress + MemChunkNextOffset, afterBytes >= 8 ? end : nextChunkAddress);
                        _machine.Bus.WriteLong(chunkAddress + MemChunkBytesOffset, beforeBytes);
                    }
                    else if (afterBytes >= 8)
                    {
                        _machine.Bus.WriteLong(previousLinkAddress, end);
                    }
                    else
                    {
                        _machine.Bus.WriteLong(previousLinkAddress, nextChunkAddress);
                    }

                    if (afterBytes >= 8)
                    {
                        _machine.Bus.WriteLong(end + MemChunkNextOffset, nextChunkAddress);
                        _machine.Bus.WriteLong(end + MemChunkBytesOffset, afterBytes);
                    }

                    var allocatedBytes = chunkBytes - (beforeBytes >= 8 ? beforeBytes : 0) - (afterBytes >= 8 ? afterBytes : 0);
                    var freeBytes = _machine.Bus.ReadLong(headerAddress + MemHeaderFreeOffset);
                    _machine.Bus.WriteLong(headerAddress + MemHeaderFreeOffset, freeBytes >= allocatedBytes ? freeBytes - allocatedBytes : 0);
                    return location;
                }

                previousLinkAddress = chunkAddress + MemChunkNextOffset;
                chunkAddress = nextChunkAddress;
            }

            return 0;
        }

        private void FreeMemoryToMemList(uint address, int byteCount)
        {
            if (!_memoryListInstalled || address == 0 || byteCount <= 0)
            {
                return;
            }

            var size = Align((uint)byteCount, 8);
            var headerAddress = FindOwningMemoryHeader(address, size);
            if (headerAddress == 0)
            {
                return;
            }

            var previousLinkAddress = headerAddress + MemHeaderFirstChunkOffset;
            var previousChunkAddress = 0u;
            var currentChunkAddress = _machine.Bus.ReadLong(previousLinkAddress);
            while (currentChunkAddress != 0 && currentChunkAddress < address)
            {
                previousChunkAddress = currentChunkAddress;
                previousLinkAddress = currentChunkAddress + MemChunkNextOffset;
                currentChunkAddress = _machine.Bus.ReadLong(currentChunkAddress + MemChunkNextOffset);
            }

            _machine.Bus.WriteLong(address + MemChunkNextOffset, currentChunkAddress);
            _machine.Bus.WriteLong(address + MemChunkBytesOffset, size);
            _machine.Bus.WriteLong(previousLinkAddress, address);

            var mergedAddress = address;
            var mergedSize = size;
            if (currentChunkAddress != 0 && address + size == currentChunkAddress)
            {
                mergedSize += _machine.Bus.ReadLong(currentChunkAddress + MemChunkBytesOffset);
                _machine.Bus.WriteLong(address + MemChunkNextOffset, _machine.Bus.ReadLong(currentChunkAddress + MemChunkNextOffset));
                _machine.Bus.WriteLong(address + MemChunkBytesOffset, mergedSize);
            }

            if (previousChunkAddress != 0)
            {
                var previousSize = _machine.Bus.ReadLong(previousChunkAddress + MemChunkBytesOffset);
                if (previousChunkAddress + previousSize == mergedAddress)
                {
                    mergedSize += previousSize;
                    _machine.Bus.WriteLong(previousChunkAddress + MemChunkNextOffset, _machine.Bus.ReadLong(mergedAddress + MemChunkNextOffset));
                    _machine.Bus.WriteLong(previousChunkAddress + MemChunkBytesOffset, mergedSize);
                    mergedAddress = previousChunkAddress;
                }
            }

            _ = mergedAddress;
            var freeBytes = _machine.Bus.ReadLong(headerAddress + MemHeaderFreeOffset);
            _machine.Bus.WriteLong(headerAddress + MemHeaderFreeOffset, freeBytes + size);
        }

        private uint QueryAvailableMemory(uint flags)
        {
            if (!_memoryListInstalled)
            {
                return 0;
            }

            var total = 0u;
            var largest = 0u;
            foreach (var headerAddress in EnumerateCompatibleMemoryHeaders(flags))
            {
                if ((flags & MemfTotal) != 0)
                {
                    var lower = _machine.Bus.ReadLong(headerAddress + MemHeaderLowerOffset);
                    var upper = _machine.Bus.ReadLong(headerAddress + MemHeaderUpperOffset);
                    if (upper > lower)
                    {
                        total += upper - lower;
                    }

                    continue;
                }

                var chunkAddress = _machine.Bus.ReadLong(headerAddress + MemHeaderFirstChunkOffset);
                while (chunkAddress != 0)
                {
                    var bytes = _machine.Bus.ReadLong(chunkAddress + MemChunkBytesOffset);
                    total += bytes;
                    largest = Math.Max(largest, bytes);
                    chunkAddress = _machine.Bus.ReadLong(chunkAddress + MemChunkNextOffset);
                }
            }

            return (flags & MemfLargest) != 0 ? largest : total;
        }

        private IEnumerable<uint> EnumerateCompatibleMemoryHeaders(uint flags)
        {
            var listAddress = GetActiveExecBase() + ExecMemListOffset;
            var headerAddress = _machine.Bus.ReadLong(listAddress);
            for (var guard = 0; headerAddress != 0 && guard < 8; guard++)
            {
                if (IsMemoryHeaderCompatible(headerAddress, flags))
                {
                    yield return headerAddress;
                }

                var next = _machine.Bus.ReadLong(headerAddress);
                headerAddress = next == listAddress + 4 ? 0 : next;
            }
        }

        private uint FindOwningMemoryHeader(uint address, uint byteCount)
        {
            var listAddress = GetActiveExecBase() + ExecMemListOffset;
            var headerAddress = _machine.Bus.ReadLong(listAddress);
            for (var guard = 0; headerAddress != 0 && guard < 8; guard++)
            {
                var lower = _machine.Bus.ReadLong(headerAddress + MemHeaderLowerOffset);
                var upper = _machine.Bus.ReadLong(headerAddress + MemHeaderUpperOffset);
                if (address >= lower && address + byteCount <= upper)
                {
                    return headerAddress;
                }

                var next = _machine.Bus.ReadLong(headerAddress);
                headerAddress = next == listAddress + 4 ? 0 : next;
            }

            return 0;
        }

        private bool IsMemoryHeaderCompatible(uint headerAddress, uint flags)
        {
            var attributes = _machine.Bus.ReadWord(headerAddress + MemHeaderAttributesOffset);
            var lower = _machine.Bus.ReadLong(headerAddress + MemHeaderLowerOffset);
            var upper = _machine.Bus.ReadLong(headerAddress + MemHeaderUpperOffset);
            if ((flags & Memf24BitDma) != 0 && (lower >= 0x0100_0000 || upper > 0x0100_0000)) return false;
            if ((flags & MemfKick) != 0 && (attributes & MemfKick) == 0) return false;
            if ((flags & MemfChip) != 0)
            {
                return (attributes & MemfChip) != 0;
            }

            if ((flags & MemfFast) != 0)
            {
                return (attributes & MemfFast) != 0;
            }

            return (attributes & MemfPublic) != 0;
        }

        private uint TypeOfGuestMemory(uint address)
        {
            var listAddress = GetActiveExecBase() + ExecMemListOffset;
            for (var header = _machine.Bus.ReadLong(listAddress); header != 0 && header != listAddress + 4; header = _machine.Bus.ReadLong(header))
            {
                if (!_machine.Bus.IsMappedMemoryRange(header, MemHeaderUpperOffset + 4)) return 0;
                var lower = _machine.Bus.ReadLong(header + MemHeaderLowerOffset);
                var upper = _machine.Bus.ReadLong(header + MemHeaderUpperOffset);
                if (address >= lower && address < upper) return _machine.Bus.ReadWord(header + MemHeaderAttributesOffset);
            }
            return 0;
        }

        private uint AllocateProgramMemory(int byteCount)
        {
			var address = TryAllocateProgramMemory(byteCount);
            if (address == 0)
            {
                throw new AmigaEmulationException("The boot program does not fit in the available emulated memory.");
            }

            return address;
        }

		private uint TryAllocateProgramMemory(int byteCount)
		{
			var flags = _machine.Bus.RealFastRam.Length != 0 || _machine.Bus.ExpansionRam.Length != 0
				? MemfPublic | MemfFast
				: MemfPublic;
			return AllocateMemoryFromMemList(Math.Max(4, byteCount), flags);
		}

        private uint AllocateChipProgramMemory(int byteCount)
        {
            var address = AllocateMemoryFromMemList(Math.Max(4, byteCount), MemfPublic | MemfChip);
            if (address == 0)
            {
                throw new AmigaEmulationException("The boot program does not fit in the available emulated chip memory.");
            }

            return address;
        }

        private uint WriteProgramString(string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            var address = AllocateProgramMemory(bytes.Length + 1);
            _machine.Bus.CopyToMemory(address, bytes);
            _machine.Bus.WriteByte(address + (uint)bytes.Length, 0, 0);
            return address;
        }

        private void WriteFileInfoBlock(uint address, AmigaDosDirectoryEntry entry)
        {
            if (address == 0 || !_machine.Bus.IsMappedMemoryRange(address, 260))
            {
                return;
            }

            _machine.Bus.ClearMemory(address, 260);
            var type = entry.IsFile ? -3 : entry.IsDirectory ? 2 : entry.SecondaryType;
            _machine.Bus.WriteLong(address + 0x04, unchecked((uint)type));
            WriteFixedAscii(address + 0x08, entry.Name, 108);
            _machine.Bus.WriteLong(address + 0x74, 0);
            _machine.Bus.WriteLong(address + 0x78, unchecked((uint)type));
            _machine.Bus.WriteLong(address + 0x7C, entry.IsFile ? (uint)Math.Max(0, entry.Size) : 0);
            _machine.Bus.WriteLong(address + 0x80, entry.IsFile ? (uint)Math.Max(1, (entry.Size + 511) / 512) : 0);
        }

        private void WriteFixedAscii(uint address, string value, int maxLength)
        {
            var count = Math.Min(Math.Max(0, maxLength - 1), value.Length);
            for (var i = 0; i < count; i++)
            {
                _machine.Bus.WriteByte(address + (uint)i, (byte)value[i], 0);
            }

            _machine.Bus.WriteByte(address + (uint)count, 0, 0);
        }

        private string ReadDosPath(uint value)
        {
            if (TryReadDosPathCandidate(value, out var path))
            {
                return path;
            }

            if (TryReadDosPathCandidate(value << 2, out path))
            {
                return path;
            }

            return string.Empty;
        }

        private bool TryReadDosPathCandidate(uint candidate, out string path)
        {
            path = ReadBstr(candidate, 255);
            if (!string.IsNullOrWhiteSpace(path))
            {
                return true;
            }

            path = ReadNullTerminatedString(candidate, 255);
            return !string.IsNullOrWhiteSpace(path);
        }

        private string ReadBstr(uint address, int maxLength)
        {
            if (!_machine.Bus.IsMappedMemoryRange(address, 1))
            {
                return string.Empty;
            }

            var length = Math.Min(_machine.Bus.ReadByte(address), maxLength);
            if (length <= 0 || !_machine.Bus.IsMappedMemoryRange(address + 1, length))
            {
                return string.Empty;
            }

            var chars = new char[length];
            for (var i = 0; i < length; i++)
            {
                var value = _machine.Bus.ReadByte(address + 1 + (uint)i);
                if (value < 32 || value >= 127)
                {
                    return string.Empty;
                }

                chars[i] = (char)value;
            }

            return new string(chars);
        }

        private string ReadNullTerminatedString(uint address, int maxLength)
        {
            var chars = new char[Math.Max(0, maxLength)];
            var count = 0;
            while (count < chars.Length)
            {
                var value = _machine.Bus.ReadByte(address + (uint)count);
                if (value == 0)
                {
                    break;
                }

                chars[count++] = (char)value;
            }

            return new string(chars, 0, count);
        }

        private string ReadMemoryText(uint address, int length)
        {
            var chars = new char[Math.Max(0, length)];
            var count = 0;
            for (var i = 0; i < chars.Length; i++)
            {
                var value = _machine.Bus.ReadByte(address + (uint)i);
                chars[count++] = value is >= 32 and < 127 ? (char)value : value == 10 ? '\n' : '.';
            }

            return new string(chars, 0, count);
        }

        private static uint Lvo(uint baseAddress, int displacement)
        {
            return unchecked((uint)((int)baseAddress + displacement));
        }

        private uint GetBootStackTopAddress()
        {
            var reservedTop = Math.Max(0, _machine.Bus.ChipRam.Length - BootPseudoFastStackReserve);
            return AlignDown((uint)reservedTop, 4) - 4;
        }

        private uint GetProgramStackTopAddress()
        {
            if (_machine.Bus.RealFastRam.Length != 0)
            {
                return AlignDown(_machine.Bus.RealFastRamBase + (uint)_machine.Bus.RealFastRam.Length, 4) - 4;
            }

            if (_machine.Bus.ExpansionRam.Length != 0)
            {
                return AlignDown(_machine.Bus.ExpansionRamBase + (uint)_machine.Bus.ExpansionRam.Length, 4) - 4;
            }

            return GetBootStackTopAddress();
        }

        private uint GetChipOnlyPrivateMetadataBase()
        {
            var chipLength = (uint)_machine.Bus.ChipRam.Length;
            if (chipLength <= BootChipPublicLowerAddress)
            {
                return AlignDown(chipLength, 4);
            }

            var privateBase = chipLength > BootChipOnlyPrivateMetadataSize
                ? chipLength - BootChipOnlyPrivateMetadataSize
                : BootChipPublicLowerAddress;
            return AlignDown(privateBase, 4);
        }

        private static uint Align(uint value, uint alignment)
        {
            return (value + alignment - 1) & ~(alignment - 1);
        }

        private static uint AlignDown(uint value, uint alignment)
        {
            return value & ~(alignment - 1);
        }

        private ushort CalculateLowMemoryVectorChecksum()
        {
            var sum = 0;
            for (var address = 0u; address < BootSupervisorStackTopAddress; address += 2)
            {
                sum = (sum + _machine.Bus.ReadWord(address)) & 0xFFFF;
            }

            return unchecked((ushort)-sum);
        }

        private static ushort CalculateExecBaseStaticChecksum(ReadOnlySpan<byte> execImage)
        {
            var sum = 0;
            for (var offset = ExecSoftVerOffset; offset < ExecChkSumOffset; offset += 2)
            {
                sum = (sum + BigEndian.ReadUInt16(execImage, offset, "exec static checksum word")) & 0xFFFF;
            }

            return unchecked((ushort)-sum);
        }

        private readonly struct StartupSequenceCommand
        {
            public StartupSequenceCommand(string executablePath, string arguments, string rawLine)
            {
                ExecutablePath = executablePath ?? string.Empty;
                Arguments = arguments ?? string.Empty;
                RawLine = rawLine ?? string.Empty;
            }

            public string ExecutablePath { get; }

            public string Arguments { get; }

            public string RawLine { get; }
        }

        private readonly struct HostBitMapInfo
        {
            private readonly uint _plane0;
            private readonly uint _plane1;
            private readonly uint _plane2;
            private readonly uint _plane3;
            private readonly uint _plane4;
            private readonly uint _plane5;
            private readonly uint _plane6;
            private readonly uint _plane7;

            public HostBitMapInfo(
                int bytesPerRow,
                int rowStride,
                int height,
                int depth,
                bool interleaved,
                ReadOnlySpan<uint> planes)
            {
                BytesPerRow = bytesPerRow;
                RowStride = rowStride;
                Height = height;
                Depth = depth;
                Interleaved = interleaved;
                _plane0 = planes[0];
                _plane1 = planes[1];
                _plane2 = planes[2];
                _plane3 = planes[3];
                _plane4 = planes[4];
                _plane5 = planes[5];
                _plane6 = planes[6];
                _plane7 = planes[7];
                RtgSurface = null;
                Width = bytesPerRow * 8;
            }

            public HostBitMapInfo(CyberGraphicsSurface surface)
            {
                RtgSurface = surface;
                BytesPerRow = surface.BytesPerRow;
                RowStride = surface.BytesPerRow;
                Height = surface.Height;
                Depth = surface.Depth;
                Interleaved = false;
                _plane0 = 0;
                _plane1 = 0;
                _plane2 = 0;
                _plane3 = 0;
                _plane4 = 0;
                _plane5 = 0;
                _plane6 = 0;
                _plane7 = 0;
                Width = surface.Width;
            }

            public int BytesPerRow { get; }

            public int RowStride { get; }

            public int Height { get; }

            public int Depth { get; }

            public bool Interleaved { get; }

            public CyberGraphicsSurface? RtgSurface { get; }

            public int Width { get; }

            public uint GetPlane(int index) => index switch
            {
                0 => _plane0,
                1 => _plane1,
                2 => _plane2,
                3 => _plane3,
                4 => _plane4,
                5 => _plane5,
                6 => _plane6,
                7 => _plane7,
                _ => 0
            };
        }

        private readonly struct SyntheticInterruptServer
        {
            public SyntheticInterruptServer(uint interruptAddress, uint dataAddress)
				: this(interruptAddress, dataAddress, 0)
			{
			}

			public SyntheticInterruptServer(
				uint interruptAddress,
				uint dataAddress,
				uint codeAddress)
            {
                InterruptAddress = interruptAddress;
                DataAddress = dataAddress;
				CodeAddress = codeAddress;
            }

            public uint InterruptAddress { get; }

            public uint DataAddress { get; }

			public uint CodeAddress { get; }
        }

        private readonly struct PendingGraphicsDoubleBufferMessage
        {
            public PendingGraphicsDoubleBufferMessage(uint messageAddress, long dueCycle)
            {
                MessageAddress = messageAddress;
                DueCycle = dueCycle;
            }

            public uint MessageAddress { get; }

            public long DueCycle { get; }
        }

        private void DeallocateToMemoryHeader(uint headerAddress, uint address, int byteCount)
        {
            if (headerAddress == 0 || byteCount <= 0 || FindOwningMemoryHeader(address, Align((uint)byteCount, 8)) != headerAddress) return;
            FreeMemoryToMemList(address, byteCount);
        }

        private bool MatchesNullTerminatedString(string? cached, uint address, int maxLength, string value)
        {
            if (cached != null)
            {
                return string.Equals(cached, value, StringComparison.OrdinalIgnoreCase);
            }

            if (address == 0 || maxLength <= value.Length)
            {
                return false;
            }

            for (var offset = 0; offset < value.Length; offset++)
            {
                if (!AsciiEqualsIgnoreCase(_machine.Bus.ReadByte(address + (uint)offset), value[offset]))
                {
                    return false;
                }
            }

            return _machine.Bus.ReadByte(address + (uint)value.Length) == 0;
        }

        uint ICyberGraphicsGuestServices.Allocate(int byteCount)
            => AllocateMemoryFromMemList(Math.Max(4, byteCount), MemfPublic | MemfClear);

        void ICyberGraphicsGuestServices.Free(uint address, int byteCount)
            => FreeMemoryToMemList(address, byteCount);

        bool ICyberGraphicsGuestServices.InvokeHook(uint entryAddress, uint objectAddress, uint messageAddress)
        {
            _ = entryAddress;
            _ = objectAddress;
            _ = messageAddress;
            return false;
        }

		private IReadOnlyList<AmigaDosDirectoryEntry> ListDosDirectory(string path)
		{
			path = ResolveAssignedDosPath(path);
			if (TryParseDrivePath(path, out var driveIndex, out var drivePath))
			{
				return TryGetDosFileSystem(driveIndex, out var selected)
					? selected.ListDirectory(drivePath)
					: Array.Empty<AmigaDosDirectoryEntry>();
			}
			for (var index = 0; index < _machine.Bus.Disk.ConnectedDriveCount;
				index++)
			{
				if (TryGetDosFileSystem(index, out var fileSystem) &&
					fileSystem.TryFindEntry(path, out var entry) && entry.IsDirectory)
					return fileSystem.ListDirectory(path);
			}
			return Array.Empty<AmigaDosDirectoryEntry>();
		}

    }

    internal readonly struct AmigaBootResult
    {
        public AmigaBootResult(
            uint loadedAddress,
            uint entryAddress,
            uint finalProgramCounter,
            int instructionsExecuted,
            bool completedBootBlock,
            IReadOnlyList<AmigaBootDiagnostic> diagnostics)
        {
            LoadedAddress = loadedAddress;
            EntryAddress = entryAddress;
            FinalProgramCounter = finalProgramCounter;
            InstructionsExecuted = instructionsExecuted;
            CompletedBootBlock = completedBootBlock;
            Diagnostics = diagnostics;
        }

        public uint LoadedAddress { get; }

        public uint EntryAddress { get; }

        public uint FinalProgramCounter { get; }

        public int InstructionsExecuted { get; }

        public bool CompletedBootBlock { get; }

        public IReadOnlyList<AmigaBootDiagnostic> Diagnostics { get; }
    }

    internal readonly struct AmigaBootDiagnostic
    {
        public AmigaBootDiagnostic(string code, string message)
        {
            Code = code;
            Message = message;
        }

        public string Code { get; }

        public string Message { get; }
    }
}
