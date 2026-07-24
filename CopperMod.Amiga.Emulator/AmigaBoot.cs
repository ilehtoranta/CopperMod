/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
using CopperStartExecMemoryServices = CopperMod.Amiga.CopperStart.Exec.ExecMemoryServices;
using CopperStartExecMemoryContext = CopperMod.Amiga.CopperStart.Exec.ExecMemoryContext;
using CopperStartExecGatewayServices = CopperMod.Amiga.CopperStart.Exec.ExecGatewayServices;
using CopperStartExecLibraryGatewayServices = CopperMod.Amiga.CopperStart.Exec.ExecLibraryGatewayServices;
using CopperStartExecFormatServices = CopperMod.Amiga.CopperStart.Exec.ExecFormatServices;
using CopperStartExecInitStructServices = CopperMod.Amiga.CopperStart.Exec.ExecInitStructServices;
using CopperStartExecMakeLibraryServices = CopperMod.Amiga.CopperStart.Exec.ExecMakeLibraryServices;
using CopperStartExecIoServices = CopperMod.Amiga.CopperStart.Exec.ExecIoServices;
using CopperStartExecLvos = CopperMod.Amiga.CopperStart.Exec.ExecLvos;
using CopperStartTrackdiskDeviceServices = CopperMod.Amiga.CopperStart.Devices.Trackdisk.TrackdiskDeviceServices;
using CopperStartTrackdiskRawTrack = CopperMod.Amiga.CopperStart.Devices.Trackdisk.TrackdiskRawTrack;
using CopperStartTimerDeviceServices = CopperMod.Amiga.CopperStart.Devices.Timer.TimerDeviceServices;
using CopperStartEncodedTrack = CopperMod.Amiga.Storage.Floppy.AmigaEncodedTrack;
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
using CopperStartSyntheticDisplayServices = CopperMod.Amiga.CopperStart.Graphics.SyntheticDisplayServices;
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

    internal sealed partial class AmigaBootController : ICyberGraphicsGuestServices
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
        private const int ExecBaseImageSize = 0x188;
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
        private const int ExecPortListOffset = 0x188;
        private const int ExecIntrListOffset = 0x16C;
        private const int ExecMemListOffset = 0x142;
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
        private const uint ExecMakeLibraryContinuationAddress = 0x00F0_8400;
        private const uint RawDoFmtContinuationAddress = 0x00F0_8400;
        private const uint ExecWaitResumeGatewayAddress = 0x00F0_8500;
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
        private const int ViewStructSize = 0x12;
        private const int CprListStartOffset = 0x04;
        private const int ScreenFirstWindowOffset = 0x04;
        private const int ScreenWidthOffset = 0x0C;
        private const int ScreenHeightOffset = 0x0E;
        private const int ScreenViewPortOffset = 0x2C;
        private const int ScreenRastPortOffset = 0x54;
        private const int ScreenBitMapOffset = 0xB8;
        private const int NewWindowLeftEdgeOffset = 0x00;
        private const int NewWindowTopEdgeOffset = 0x02;
        private const int NewWindowWidthOffset = 0x04;
        private const int NewWindowHeightOffset = 0x06;
        private const int NewWindowIdcmpFlagsOffset = 0x0A;
        private const int NewWindowFirstGadgetOffset = 0x12;
        private const int WindowWScreenOffset = 0x2E;
        private const int WindowRPortOffset = 0x32;
        private const int WindowFirstGadgetOffset = 0x3E;
        private const int WindowIdcmpFlagsOffset = 0x52;
        private const int WindowUserPortOffset = 0x56;
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
        private const int ViewPortDWidthOffset = 0x18;
        private const int ViewPortDHeightOffset = 0x1A;
        private const int ViewPortDxOffsetOffset = 0x1C;
        private const int ViewPortDyOffsetOffset = 0x1E;
        private const int ViewPortModesOffset = 0x20;
        private const int ViewPortRasInfoOffset = 0x24;
        private const int RasInfoBitMapOffset = 0x04;
        private const int RasInfoRxOffsetOffset = 0x08;
        private const int RasInfoRyOffsetOffset = 0x0A;
        private const int BitMapBytesPerRowOffset = 0x00;
        private const int BitMapRowsOffset = 0x02;
        private const int BitMapDepthOffset = 0x05;
        private const int BitMapPlanesOffset = 0x08;
        private const uint BitMapAttributeHeight = 0;
        private const uint BitMapAttributeDepth = 4;
        private const uint BitMapAttributeWidth = 8;
        private const uint BitMapAttributeFlags = 12;
        private const uint BitMapFlagClear = 1;
        private const int NewScreenWidthOffset = 0x04;
        private const int NewScreenHeightOffset = 0x06;
        private const int NewScreenDepthOffset = 0x08;
        private const int NewScreenViewModesOffset = 0x0C;
        private const int NewScreenStructMinimumSize = 0x10;
        private const ushort ViewModeHires = 0x8000;
        private const ushort ViewModeInterlace = 0x0004;
        private const int SyntheticScreenDefaultDepth = 2;
        private const int SyntheticScreenDefaultHeight = 256;
        private const int SyntheticScreenTitleHeight = 24;
        private const int SyntheticPresentationLeft = AmigaConstants.PalLowResOverscanBorderX * 2;
        private const int SyntheticPresentationTop = AmigaConstants.PalLowResOverscanBorderY * 2;
        private const uint NoTitleChange = 0xFFFF_FFFF;
        private const uint IdcmpGadgetDown = 0x0000_0020;
        private const uint IdcmpGadgetUp = 0x0000_0040;
        private const int VBlankInterruptNumber = 5;
        private const int InterruptDataOffset = 0x0E;
        private const int InterruptCodeOffset = 0x12;

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
        private readonly Dictionary<int, List<SyntheticInterruptServer>> _execInterruptServers = new Dictionary<int, List<SyntheticInterruptServer>>();
        private readonly Queue<int> _pendingExecInterruptSources = new Queue<int>();
        private ushort _observedExecInterruptBits;
        private int _activeExecInterruptSource = -1;
        private int _activeExecInterruptServerIndex;
        private uint _execInterruptReturnProgramCounter;
        private readonly Dictionary<uint, int> _allocatedRtgBitMaps = new Dictionary<uint, int>();
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
        private uint _syntheticWindowAddress { get => _syntheticUiDisplay.WindowAddress; set => _syntheticUiDisplay.WindowAddress = value; }
        private uint _syntheticUserPortAddress { get => _syntheticUiDisplay.UserPortAddress; set => _syntheticUiDisplay.UserPortAddress = value; }
        private uint _syntheticMessageAddress { get => _syntheticUiDisplay.MessageAddress; set => _syntheticUiDisplay.MessageAddress = value; }
        private uint _syntheticHostObjectAddress { get => _syntheticUiDisplay.HostObjectAddress; set => _syntheticUiDisplay.HostObjectAddress = value; }
        private uint _syntheticViewAddress { get => _syntheticUiDisplay.ViewAddress; set => _syntheticUiDisplay.ViewAddress = value; }
        private uint _syntheticRasInfoAddress { get => _syntheticUiDisplay.RasInfoAddress; set => _syntheticUiDisplay.RasInfoAddress = value; }
        private uint _syntheticBitMapAddress { get => _syntheticUiDisplay.BitMapAddress; set => _syntheticUiDisplay.BitMapAddress = value; }
        private uint _syntheticRastPortAddress { get => _syntheticUiDisplay.RastPortAddress; set => _syntheticUiDisplay.RastPortAddress = value; }
        private uint _syntheticFontAddress { get => _syntheticUiDisplay.FontAddress; set => _syntheticUiDisplay.FontAddress = value; }
        private uint _syntheticPlaneAddress { get => _syntheticUiDisplay.PlaneAddress; set => _syntheticUiDisplay.PlaneAddress = value; }
        private uint _syntheticGadgetListAddress { get => _syntheticUiDisplay.GadgetListAddress; set => _syntheticUiDisplay.GadgetListAddress = value; }
        private uint _syntheticUserPortSignalMask { get => _syntheticUiDisplay.UserPortSignalMask; set => _syntheticUiDisplay.UserPortSignalMask = value; }
        private uint _syntheticIdcmpFlags { get => _syntheticUiDisplay.IdcmpFlags; set => _syntheticUiDisplay.IdcmpFlags = value; }
        private int _syntheticScreenWidth { get => _syntheticUiDisplay.ScreenWidth; set => _syntheticUiDisplay.ScreenWidth = value; }
        private int _syntheticScreenHeight { get => _syntheticUiDisplay.ScreenHeight; set => _syntheticUiDisplay.ScreenHeight = value; }
        private int _syntheticScreenDepth { get => _syntheticUiDisplay.ScreenDepth; set => _syntheticUiDisplay.ScreenDepth = value; }
        private int _syntheticWindowLeft { get => _syntheticUiDisplay.WindowLeft; set => _syntheticUiDisplay.WindowLeft = value; }
        private int _syntheticWindowTop { get => _syntheticUiDisplay.WindowTop; set => _syntheticUiDisplay.WindowTop = value; }
        private int _syntheticWindowWidth { get => _syntheticUiDisplay.WindowWidth; set => _syntheticUiDisplay.WindowWidth = value; }
        private int _syntheticWindowHeight { get => _syntheticUiDisplay.WindowHeight; set => _syntheticUiDisplay.WindowHeight = value; }
        private ushort _syntheticScreenViewModes { get => _syntheticUiDisplay.ScreenViewModes; set => _syntheticUiDisplay.ScreenViewModes = value; }
        private uint _currentViewAddress;
        // LoadView is a display ownership hand-off, not an ordinary register
        // poke.  Keep the most recent request until the next complete frame so
        // an HLE caller cannot splice two Copper lists into one presentation.
        private uint _pendingCopperList;
        private long _pendingCopperListCycle = -1;
        private ushort[] _syntheticPalette => _syntheticUiDisplay.Palette;
        private bool _syntheticPaletteLoaded { get => _syntheticUiDisplay.PaletteLoaded; set => _syntheticUiDisplay.PaletteLoaded = value; }
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
        private bool _memoryListInstalled;
        private readonly AmigaDosFileSystem?[] _dosFileSystems = new AmigaDosFileSystem?[4];
        private readonly List<StartupSequenceCommand> _startupSequenceCommands = new List<StartupSequenceCommand>();
        private int _startupSequenceCommandIndex;
        private bool _startupSequenceActive;
        private int _startupSequenceFailAt = 10;
        private bool _kickstartRomBootActive;
        private CopperStartExecLibraryServices? _romExecLibraryServices;
        private CopperStartExecListServices? _execListServices;
        private readonly CopperStartTaskScheduler _taskScheduler = new CopperStartTaskScheduler();
        private readonly CopperStartExecTaskServices _execTaskServices;
        private readonly CopperStartExecPortServices _execPortServices;
        private readonly CopperStartExecMemoryServices _execMemoryServices;
        private readonly CopperStartExecPoolServices _execPoolServices;
        private readonly CopperStartGuestMemory _guestMemory;
        private readonly CopperStartExecContext _execContext;
        private readonly CopperStartGraphicsContext _graphicsContext;
        private readonly CopperStartGraphicsServices _graphicsServices;
        private readonly CopperStartSyntheticDisplayServices _syntheticDisplayServices;
        private readonly CopperStartDosServices _dosServices;
        private readonly CopperStartExecSignalServices _execSignalServices;
        private readonly CopperStartExecTrapServices _execTrapServices;
        private readonly CopperStartExecNameServices _execNameServices;
        private readonly CopperStartExecResidentServices _execResidentServices;
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
        private uint _activeExecBase;
        private KickstartRomExecTakeoverState _kickstartRomExecTakeoverState;
        private readonly CopperStartRuntimeInstructionBoundary _runtimeInstructionBoundary;

        public AmigaBootController(Machine machine, IAmigaDiskDmaEngine? diskDma = null)
        {
            _machine = machine ?? throw new ArgumentNullException(nameof(machine));
            _guestMemory = new CopperStartGuestMemory(_machine.Bus);
            _syntheticDisplayServices = new CopperStartSyntheticDisplayServices(_guestMemory, _syntheticUiDisplay, SyntheticGlyph);
            _execContext = new CopperStartExecContext(
                _guestMemory, GetActiveExecBase, GetCurrentTaskAddress, ReadNullTerminatedString,
                MoveTaskToList, _taskScheduler.RequestDispatch, SuspendCurrentTaskThroughNativeExecScheduler, ExecWaitResumeGatewayAddress,
                address => _machine.Bus.IsCpuPhysicalAddressMapped(address, 2, AmigaBusAccessKind.CpuInstructionFetch),
                _taskScheduler.Register, _taskScheduler.Remove, StartGuestExecSubroutine,
                () => _kickstartRomExecTakeoverState == KickstartRomExecTakeoverState.Active,
                () => _syntheticScreenAddress != 0,
                (name, nameAddress) => TryGetHostLibraryBase(name, nameAddress, out var libraryBase) ? libraryBase : 0,
                new CopperStartExecMemoryOperations(
                    AllocateMemoryFromMemList,
                    FreeMemoryToMemList,
                    (address, byteCount) => _machine.Bus.ClearMemory(address, byteCount)),
                () => { EnsureDosResident(); return DosResidentAddress; });
            _graphicsContext = new CopperStartGraphicsContext(
                _guestMemory,
                state => _ = WaitForNextFrame(state),
                (address, value, cycles) => _machine.Bus.WriteWord(address, value, cycles),
                viewPort => _cyberGraphics?.SelectFrontViewPort(viewPort),
                () => { },
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
                (viewPort, bitMap) => CyberGraphics.ChangeViewPortBitMap(viewPort, bitMap) ? 0u : 1u,
                MergeCopperLists,
                MakeViewPort,
                LoadView,
                LoadRgb4,
                SetRgb4,
                EnsureSyntheticHostObject,
                DrawRastPort,
                DrawRastPortText,
                SetRastPort,
                FillRastPort);
            _graphicsServices = new CopperStartGraphicsServices(_graphicsContext);
            _dosServices = new CopperStartDosServices(new CopperStartDosContext(
                _guestMemory,
                WorkbenchRootLock,
                ReadDosPath,
                path => TryReadDosFile(path, out var data) ? data : null,
                path => TryFindDosEntry(path, out var entry) ? entry : null,
                WriteFileInfoBlock,
                AllocateMemoryFromMemList,
                ReadMemoryText,
                (code, message) => _diagnostics.Add(new AmigaBootDiagnostic(code, message))));
            _execTaskServices = new CopperStartExecTaskServices(_execContext, GetExecListServices());
            _execSignalServices = new CopperStartExecSignalServices(_execContext, () => _kickstartRomExecTakeoverState == KickstartRomExecTakeoverState.Active);
            _execSemaphoreServices = new CopperStartExecSemaphoreServices(_execContext, _execSignalServices);
            _execPortServices = new CopperStartExecPortServices(_execContext, GetExecListServices(), _execSignalServices);
            _execMemoryServices = new CopperStartExecMemoryServices(new CopperStartExecMemoryContext(
                _machine.Bus, AllocateMemoryFromMemList, AllocateAbsoluteMemoryFromMemList, FreeMemoryToMemList,
                QueryAvailableMemory, AllocateFromMemoryHeader, DeallocateToMemoryHeader, TypeOfGuestMemory,
                RecordAllocDiagnostic, RecordAllocAbsDiagnostic, RecordFreeDiagnostic));
            _execPoolServices = new CopperStartExecPoolServices(_execContext);
            _execTrapServices = new CopperStartExecTrapServices(_execContext);
            _execNameServices = new CopperStartExecNameServices(_execContext, GetExecListServices());
            _execInitStructServices = new CopperStartExecInitStructServices(_execContext);
            _execMakeLibraryServices = new CopperStartExecMakeLibraryServices(_execContext, _execInitStructServices, ExecMakeLibraryContinuationAddress);
            _execResidentServices = new CopperStartExecResidentServices(
                _execContext, GetExecListServices(), _execMakeLibraryServices, ExecMakeLibraryContinuationAddress);
            _execIoServices = new CopperStartExecIoServices(_execContext, _execSignalServices, HostDoIo);
            _execSignalServices.SetIoActiveProbe(_execIoServices.IsActive);
            _trackdiskDeviceServices = new CopperStartTrackdiskDeviceServices(
                _machine.Bus,
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
                ReplyTrackdiskMessage,
                message => _diagnostics.Add(new AmigaBootDiagnostic("AMIGA_TIMER", message)));
            _execFormatServices = new CopperStartExecFormatServices(_machine.Bus, RawDoFmtContinuationAddress, ReadNullTerminatedString);
            _execGatewayServices = new CopperStartExecGatewayServices(
                LogExecCall, _execMemoryServices, _execTaskServices, GetExecListServices(), _execSignalServices,
                _execSemaphoreServices, _execTrapServices, _execPortServices, _execPoolServices, _execInitStructServices, AddInterruptServer, RemoveInterruptServer,
                GetMessage, WaitPort, _execFormatServices.RawDoFmt);
            _execLibraryGatewayServices = new CopperStartExecLibraryGatewayServices(
                () => _kickstartRomExecTakeoverState == KickstartRomExecTakeoverState.Active,
                OpenRomLibrary, CloseRomLibrary, AddRomLibrary, RemoveRomLibrary, AddRomDevice,
                RemoveRomDevice, OpenRomDevice, CloseRomDevice,
                AddRomResource, RemoveRomResource, OpenRomResource, OpenCompatibilityLibrary,
                _execMemoryServices.AllocMemAndStore, AmigaKickstartHost.DosLibraryBase);
            _copperStartRuntime = new CopperStartRuntime(_machine.Bus, CreateExecServices);
            _workbenchServices = new CopperStartWorkbenchServices(new CopperStartWorkbenchContext(
                lvo => LogUiCall("workbench.library", lvo), EnsureSyntheticScreen, EnsureSyntheticHostObject));
            _intuitionServices = new CopperStartIntuitionServices(new CopperStartIntuitionContext(
                lvo => LogUiCall("intuition.library", lvo), ConfigureSyntheticScreenFromNewScreen, ConfigureSyntheticWindowFromNewWindow,
                EnsureSyntheticScreen, EnsureSyntheticWindow, EnsureSyntheticView, EnsureSyntheticHostObject,
                () => _currentViewAddress, GetSyntheticScreenViewPortAddress, viewPort => CyberGraphics.SelectFrontViewPort(viewPort),
                AddSyntheticGadgetList, ModifySyntheticIdcmp, SetSyntheticWindowTitles,
                HostRethinkDisplay, _execMemoryServices.AllocMemAndStore));
            _iconServices = new CopperStartIconServices(new CopperStartIconContext(
                LogIconCall, EnsureWorkbenchDiskObject, FindToolTypeValue, ReadNullTerminatedString));
            _expansionServices = new CopperStartExpansionServices(new CopperStartExpansionContext(
                lvo => LogUiCall("expansion.library", lvo), EnsureSyntheticHostObject));
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
                    TryDispatchCopperStartTaskScheduler, TryDispatchPendingExecInterruptServer,
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
                    ProcessHostDevices, GetNextHostDeviceBoundary));
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

        public IReadOnlyList<AmigaBootDiagnostic> Diagnostics => _diagnostics;

        internal CyberGraphicsLibrary CyberGraphics
            => _cyberGraphics ?? throw new InvalidOperationException("CyberGraphX is not enabled for this machine.");

        internal bool HasCyberGraphics => _cyberGraphics != null;

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

        private void ResetBootState(AmigaDiskImage disk)
        {
            ResetBootState(disk, installHostShim: true);
        }

        private void ResetBootState(AmigaDiskImage? disk, bool installHostShim)
        {
            _trackdiskDeviceServices.Reset();
            _timerDeviceServices.Reset();
            _copperStartRuntime.Reset();
            _romExecLibraryServices = null;
            _execListServices = null;
            _execPoolServices.Reset();
            _execMakeLibraryServices.Reset();
            _execIoServices.Reset();
            _dosServices.Reset();
            _activeExecBase = 0;
            _kickstartRomExecTakeoverState = KickstartRomExecTakeoverState.Disabled;
            _diagnostics.Clear();
            _dosAssigns.Clear();
            _ramDirectorySources.Clear();
            _taskPendingSignals.Clear();
            _taskAllocatedSignals.Clear();
            _dosAssigns["ENVARC"] = "Prefs/Env-Archive";
            _dosAssigns["SYS"] = string.Empty;
            _syntheticVBlankInterruptServers.Clear();
            _execInterruptServers.Clear();
            _pendingExecInterruptSources.Clear();
            _observedExecInterruptBits = 0;
            _activeExecInterruptSource = -1;
            _taskScheduler.Reset();
            _activeExecInterruptServerIndex = 0;
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
            _syntheticUiInput.Reset(AmigaConstants.PalLowResStandardWidth / 2, SyntheticScreenDefaultHeight / 2);
            _currentViewAddress = 0;
            _pendingCopperList = 0;
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
            _machine.Kickstart.InstallHostShim(bus, CreateHostTrapTable());
            InstallSafeAutovectors(bus);
            InstallCopperStartExecGateways();
            bus.RegisterHostGateway(RawDoFmtContinuationAddress, _execFormatServices.Continue);
            bus.RegisterHostGateway(ExecWaitResumeGatewayAddress, ContinueHostWait);
            _taskTrapRuntime.Install();
            for (var displacement = -6; displacement >= -1200; displacement -= 6)
            {
                var captured = displacement;
                bus.RegisterHostGateway(Lvo(AmigaKickstartHost.DosLibraryBase, captured), state => _dosServices.InvokeGeneric(state, captured));
            }

            bus.RegisterHostGateway(Lvo(AmigaKickstartHost.DosLibraryBase, -48), _dosServices.Write);
            bus.RegisterHostGateway(Lvo(AmigaKickstartHost.DosLibraryBase, -54), _dosServices.Input);
            bus.RegisterHostGateway(Lvo(AmigaKickstartHost.DosLibraryBase, -60), _dosServices.Output);
            bus.RegisterHostGateway(Lvo(AmigaKickstartHost.DosLibraryBase, -84), _dosServices.Lock);
            bus.RegisterHostGateway(Lvo(AmigaKickstartHost.DosLibraryBase, -90), _dosServices.UnLock);
            bus.RegisterHostGateway(Lvo(AmigaKickstartHost.DosLibraryBase, -102), _dosServices.Examine);
            bus.RegisterHostGateway(Lvo(AmigaKickstartHost.DosLibraryBase, -126), _dosServices.CurrentDir);
            bus.RegisterHostGateway(Lvo(AmigaKickstartHost.DosLibraryBase, -132), _dosServices.IoErr);
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
                bus.RegisterHostGateway(Lvo(AmigaKickstartHost.GraphicsLibraryBase, captured), state => _graphicsServices.Invoke(state, captured));
                bus.RegisterHostGateway(Lvo(AmigaKickstartHost.IntuitionLibraryBase, captured), state => _intuitionServices.Invoke(state, captured));
                bus.RegisterHostGateway(Lvo(AmigaKickstartHost.ExpansionLibraryBase, captured), state => _expansionServices.Invoke(state, captured));
            }

            bus.RegisterHostGateway(Lvo(AmigaKickstartHost.IconLibraryBase, -78), _iconServices.GetDiskObject);
            bus.RegisterHostGateway(Lvo(AmigaKickstartHost.IconLibraryBase, -90), CopperStartIconServices.FreeDiskObject);
            bus.RegisterHostGateway(Lvo(AmigaKickstartHost.IconLibraryBase, -96), _iconServices.FindToolType);
            bus.RegisterHostGateway(Lvo(AmigaKickstartHost.IconLibraryBase, -102), _iconServices.MatchToolValue);
            bus.RegisterHostGateway(DosResidentInitAddress, _execLibraryGatewayServices.InitResident);
            bus.ConfigureAutoconfigFastRamForHost();
            bus.ConfigureAutoconfigRtgForHost();
            InstallKickstartMemoryList();
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

        private void TryActivateKickstartRomExecServices()
        {
            if (_kickstartRomExecTakeoverState != KickstartRomExecTakeoverState.Pending)
            {
                if (_kickstartRomExecTakeoverState == KickstartRomExecTakeoverState.Active)
                {
                    _trackdiskDeviceServices.TryInstall(_activeExecBase);
                    _timerDeviceServices.TryInstall(_activeExecBase);
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
                _kickstartRomExecTakeoverState = KickstartRomExecTakeoverState.Unavailable;
                return;
            }

            _copperStartRuntime.ActivateRomExec(execBase);
            _machine.Bus.RegisterHostGateway(ExecInterruptContinuationAddress, ContinueExecInterruptServer);
            _machine.Bus.RegisterHostGateway(ExecLibraryCallContinuationAddress, ContinueExecLibraryCall);
            _machine.Bus.RegisterHostGateway(ExecMakeLibraryContinuationAddress, state =>
            {
                _execMakeLibraryServices.Continue(state);
                _execResidentServices.Continue(state);
            });
            _machine.Bus.RegisterHostGateway(RawDoFmtContinuationAddress, _execFormatServices.Continue);
            _machine.Bus.RegisterHostGateway(ExecWaitResumeGatewayAddress, ContinueHostWait);
            _activeExecBase = execBase;
            _ = GetRomExecLibraryServices();
            _memoryListInstalled = true;
            _kickstartRomExecTakeoverState = KickstartRomExecTakeoverState.Active;
            _trackdiskDeviceServices.TryInstall(execBase);
            _timerDeviceServices.TryInstall(execBase);
        }

        private void ProcessHostDevices()
        {
            _trackdiskDeviceServices.ProcessPending(_machine.Cpu.State);
            _timerDeviceServices.ProcessPending(_machine.Cpu.State);
        }

        private long GetNextHostDeviceBoundary(long currentCycle, long targetCycle)
            => _timerDeviceServices.GetNextDeadline(currentCycle, targetCycle);

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

            return _machine.Bus.IsCpuPhysicalAddressMapped(
                unchecked(execBase - 6u), 6, AmigaBusAccessKind.CpuInstructionFetch);
        }

        private CopperStartExecServices CreateExecServices(uint execBase)
            => new CopperStartExecServices(
                _machine.Bus, execBase, _execGatewayServices.Invoke,
                _execIoServices.DoIo, _execIoServices.SendIo,
                _execIoServices.CheckIo, _execIoServices.WaitIo, _execIoServices.AbortIo,
                _execResidentServices.FindResident, HostOk,
                _execNameServices.FindName, _execMemoryServices.AllocMem, _execMemoryServices.AllocMemAndStore, _execMemoryServices.AllocAbs, _execMemoryServices.FreeMem,
                _execMemoryServices.AvailMem, _execLibraryGatewayServices.OpenLibrary, _execLibraryGatewayServices.CloseLibrary, _execLibraryGatewayServices.AddLibrary, _execLibraryGatewayServices.RemLibrary,
                _execLibraryGatewayServices.AddDevice, _execLibraryGatewayServices.RemDevice, _execLibraryGatewayServices.OpenDevice, _execLibraryGatewayServices.CloseDevice,
                _execLibraryGatewayServices.AddResource, _execLibraryGatewayServices.RemResource, _execLibraryGatewayServices.OpenResource,
                state => state.D[0] = _execMakeLibraryServices.MakeFunctions(state), state => _execMakeLibraryServices.MakeLibrary(state), _execResidentServices.InitResident,
                _execTaskServices.Reschedule);

        private CopperStartExecLibraryServices GetRomExecLibraryServices()
            => _romExecLibraryServices ??= new CopperStartExecLibraryServices(
                _machine.Bus,
                GetActiveExecBase,
                FindNameInExecList,
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
            var state = new M68kCpuState();
            _execPortServices.ReplyMessage(request, state);
        }

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
            => GetRomExecLibraryServices().OpenLibrary(state, ExecLibraryCallContinuationAddress);

        private void OpenCompatibilityLibrary(M68kCpuState state)
        {
            var shouldRecordDiagnostic = _openLibraryDiagnosticCount < 24;
            var name = shouldRecordDiagnostic ? ReadNullTerminatedString(state.A[1], 96) : null;
            if (_openLibraryDiagnosticCount < 24)
            {
                _diagnostics.Add(new AmigaBootDiagnostic("AMIGA_BOOT_OPEN_LIBRARY", $"OpenLibrary requested '{name}'."));
                _openLibraryDiagnosticCount++;
            }

            if (TryGetHostLibraryBase(name, state.A[1], out var libraryBase))
            {
                state.D[0] = libraryBase;
            }
            else
            {
                state.D[0] = AmigaKickstartHost.DummyLibraryBase;
            }
        }

        private bool TryGetHostLibraryBase(string? cachedName, uint nameAddress, out uint libraryBase)
        {
            if (MatchesNullTerminatedString(cachedName, nameAddress, 96, "cybergraphics.library"))
            {
                libraryBase = _cyberGraphicsFirmware?.LibraryBase ?? 0;
                return libraryBase != 0 && _machine.Bus.RtgVram.Active;
            }

            if (MatchesNullTerminatedString(cachedName, nameAddress, 96, "graphics.library"))
            {
                libraryBase = AmigaKickstartHost.GraphicsLibraryBase;
                return true;
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

            if (_kickstartRomExecTakeoverState == KickstartRomExecTakeoverState.Active)
            {
                var list = GetActiveExecBase() + ExecIntrListOffset + (uint)(interruptNumber * 14);
                EnsureExecList(list);
                if (!ContainsExecNode(list, interrupt)) AddTailExecList(list, interrupt);
                if (!_execInterruptServers.TryGetValue(interruptNumber, out var servers))
                {
                    servers = new List<SyntheticInterruptServer>();
                    _execInterruptServers.Add(interruptNumber, servers);
                }

                for (var i = 0; i < servers.Count; i++)
                {
                    if (servers[i].InterruptAddress == interrupt)
                    {
                        servers[i] = new SyntheticInterruptServer(interrupt, data);
                        return 0;
                    }
                }

                servers.Add(new SyntheticInterruptServer(interrupt, data));
                return 0;
            }

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

            if (_kickstartRomExecTakeoverState == KickstartRomExecTakeoverState.Active)
            {
                var list = GetActiveExecBase() + ExecIntrListOffset + (uint)(interruptNumber * 14);
                if (ContainsExecNode(list, interrupt)) RemoveExecNode(interrupt);
                if (_execInterruptServers.TryGetValue(interruptNumber, out var servers))
                {
                    servers.RemoveAll(server => server.InterruptAddress == interrupt);
                    if (servers.Count == 0)
                    {
                        _execInterruptServers.Remove(interruptNumber);
                    }
                }

                return 0;
            }

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

            if (_syntheticVBlankInterruptServers.Count == 0)
            {
                return;
            }

            var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
            var previousFrame = Math.Max(0, previousCycle) / frameCycles;
            var currentFrame = Math.Max(0, currentCycle) / frameCycles;
            var ticks = currentFrame - previousFrame;
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

        private bool TryDispatchCopperStartTaskScheduler()
        {
            if (_kickstartRomExecTakeoverState != KickstartRomExecTakeoverState.Active)
            {
                return false;
            }

            // A current RemTask has already entered KS Switch. Reap its
            // tc_MemEntry allocations only at this later outer boundary, when
            // the old task's stack is no longer executing.
            _execTaskServices.ReapDeferredTasks();

            if (!_taskScheduler.DispatchPending ||
                _machine.Bus.ReadByte(GetActiveExecBase() + 0x126) != 0 ||
                _machine.Bus.ReadByte(GetActiveExecBase() + 0x127) != 0)
            {
                return false;
            }

            // A host gateway only latches dispatch.  Enter the original
            // Schedule vector at this outer CPU boundary; it owns tc_SPReg,
            // ready/wait lists and tasks that were created before takeover.
            if (!EnterNativeExecScheduler(_machine.Cpu.State, _machine.Cpu.State.ProgramCounter))
            {
                return false;
            }

            _taskScheduler.AcknowledgeDispatch();
            return true;
        }

        private bool SuspendCurrentTaskThroughNativeExecScheduler(M68kCpuState state)
        {
            // The blocked gateway has not performed its implicit RTS yet.  A
            // return through this small gateway rechecks Wait/WaitPort/WaitIO
            // before returning to the original 68k caller.  Switch is the
            // native path for a task that has already committed itself to the
            // wait list; Schedule is the later priority/quantum decision path.
            return EnterNativeExecVector(state, ExecWaitResumeGatewayAddress, CopperStartExecLvos.Switch);
        }

        private bool EnterNativeExecScheduler(M68kCpuState state, uint returnAddress)
            => EnterNativeExecVector(state, returnAddress, CopperStartExecLvos.Schedule);

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
            if (_kickstartRomExecTakeoverState != KickstartRomExecTakeoverState.Active)
            {
                return false;
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
            _execInterruptReturnProgramCounter = _machine.Cpu.State.ProgramCounter;
            return StartNextExecInterruptServer(_machine.Cpu.State);
        }

        private void ContinueExecInterruptServer(M68kCpuState state)
        {
            _activeExecInterruptServerIndex++;
            _ = StartNextExecInterruptServer(state);
        }

        private bool StartNextExecInterruptServer(M68kCpuState state)
        {
            var servers = GetGuestInterruptServers(_activeExecInterruptSource);
            if (servers.Count == 0)
            {
                FinishExecInterruptDispatch(state);
                return false;
            }

            while (_activeExecInterruptServerIndex < servers.Count)
            {
                var server = servers[_activeExecInterruptServerIndex];
                var code = _machine.Bus.ReadLong(server.InterruptAddress + InterruptCodeOffset);
                if (code == 0 || !_machine.Bus.IsCpuPhysicalAddressMapped(code, 2, AmigaBusAccessKind.CpuInstructionFetch))
                {
                    _activeExecInterruptServerIndex++;
                    continue;
                }

                state.A[1] = server.DataAddress;
                state.A[7] -= 4;
                _machine.Bus.WriteLong(state.A[7], ExecInterruptContinuationAddress, state.Cycles);
                // Match the core's post-fetch PC before directly invoking a host
                // gateway. If it leaves PC unchanged, perform the normal gateway RTS.
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

            FinishExecInterruptDispatch(state);
            return false;
        }

        private bool HasGuestInterruptServers(int interruptNumber)
            => GetGuestInterruptServers(interruptNumber).Count != 0;

        private List<SyntheticInterruptServer> GetGuestInterruptServers(int interruptNumber)
        {
            var result = new List<SyntheticInterruptServer>();
            var list = GetActiveExecBase() + ExecIntrListOffset + (uint)(interruptNumber * 14);
            if (!IsValidExecList(list)) return result;
            var tail = list + 4;
            for (var server = _machine.Bus.ReadLong(list); server != tail && IsValidExecNode(server); server = _machine.Bus.ReadLong(server))
            {
                result.Add(new SyntheticInterruptServer(server, _machine.Bus.ReadLong(server + InterruptDataOffset)));
            }
            return result;
        }

        private void FinishExecInterruptDispatch(M68kCpuState state)
        {
            state.ProgramCounter = _execInterruptReturnProgramCounter;
            _activeExecInterruptSource = -1;
            _activeExecInterruptServerIndex = 0;
            _execInterruptReturnProgramCounter = 0;
        }

        private long GetNextSyntheticVBlankBoundaryCycle(long currentCycle, long targetCycle)
        {
            if (targetCycle <= currentCycle)
            {
                return targetCycle;
            }

            var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
            var nextFrameCycle = ((Math.Max(0, currentCycle) / frameCycles) + 1) * frameCycles;
            var nextBoundary = targetCycle;
            if (_pendingCopperListCycle > currentCycle)
            {
                nextBoundary = Math.Min(nextBoundary, _pendingCopperListCycle);
            }

            if (_syntheticVBlankInterruptServers.Count != 0)
            {
                nextBoundary = Math.Min(nextBoundary, nextFrameCycle);
            }

            return nextBoundary;
        }

        private void HandleDefaultTaskTrap(M68kCpuState state)
            => _taskTrapRecovery.HandleDefault(state);

        private bool TryRecoverHostTaskTrapFromZeroVector()
            => _taskTrapRecovery.TryRecoverFromZeroPc(_machine.Cpu.State, _memoryListInstalled);

        private static uint WaitForNextFrame(M68kCpuState state)
        {
            var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
            var nextFrameCycle = ((Math.Max(0, state.Cycles) / frameCycles) + 1) * frameCycles;
            state.Cycles = Math.Max(state.Cycles + 1, nextFrameCycle);
            return 0;
        }

        private uint BltBitMap(M68kCpuState state)
        {
            var pixels = CyberGraphics.BlitPlanarToRtg(
                state.A[0],
                Long(state.D[0]),
                Long(state.D[1]),
                state.A[1],
                Long(state.D[2]),
                Long(state.D[3]),
                Long(state.D[4]),
                Long(state.D[5]),
                (byte)state.D[6],
                (byte)state.D[7]);
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
            if (_kickstartRomExecTakeoverState == KickstartRomExecTakeoverState.Active)
            {
                GetRomExecLibraryServices().CloseLibrary(state, ExecLibraryCallContinuationAddress);
            }
            else
            {
                state.D[0] = 0;
            }
        }

        private void AddRomLibrary(M68kCpuState state)
        {
            if (_kickstartRomExecTakeoverState != KickstartRomExecTakeoverState.Active)
            {
                _execMemoryServices.AllocMemAndStore(state);
                return;
            }

            GetRomExecLibraryServices().AddLibrary(state);
            state.D[0] = 0;
        }

        private void RemoveRomLibrary(M68kCpuState state)
        {
            if (_kickstartRomExecTakeoverState == KickstartRomExecTakeoverState.Active)
                GetRomExecLibraryServices().RemLibrary(state);
            state.D[0] = 0;
        }

        private void AddRomDevice(M68kCpuState state)
        {
            if (_kickstartRomExecTakeoverState == KickstartRomExecTakeoverState.Active)
                GetRomExecLibraryServices().AddDevice(state);
            state.D[0] = 0;
        }

        private void RemoveRomDevice(M68kCpuState state)
        {
            if (_kickstartRomExecTakeoverState == KickstartRomExecTakeoverState.Active)
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
            if (_kickstartRomExecTakeoverState == KickstartRomExecTakeoverState.Active)
                GetRomExecLibraryServices().AddResource(state);
            state.D[0] = 0;
        }

        private void RemoveRomResource(M68kCpuState state)
        {
            if (_kickstartRomExecTakeoverState == KickstartRomExecTakeoverState.Active)
                GetRomExecLibraryServices().RemResource(state);
            state.D[0] = 0;
        }

        private uint OpenRomResource(M68kCpuState state)
            => GetRomExecLibraryServices().OpenResource(state);

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
            if (state.A[1] != 0 &&
                _machine.Bus.IsMappedMemoryRange(state.A[1], GadgetHeightOffset + 2))
            {
                _syntheticGadgetListAddress = state.A[1];
                if (state.A[0] != 0 &&
                    _machine.Bus.IsMappedMemoryRange(state.A[0], WindowFirstGadgetOffset + 4))
                {
                    _machine.Bus.WriteLong(state.A[0] + WindowFirstGadgetOffset, state.A[1]);
                }
            }

            state.D[0] = 0;
        }

        private void ModifySyntheticIdcmp(M68kCpuState state)
        {
            _syntheticIdcmpFlags = state.D[0];
            if (state.A[0] != 0 &&
                _machine.Bus.IsMappedMemoryRange(state.A[0], WindowIdcmpFlagsOffset + 4))
            {
                _machine.Bus.WriteLong(state.A[0] + WindowIdcmpFlagsOffset, _syntheticIdcmpFlags);
                if (_machine.Bus.ReadLong(state.A[0] + WindowUserPortOffset) == 0)
                {
                    _machine.Bus.WriteLong(state.A[0] + WindowUserPortOffset, EnsureSyntheticUserPort());
                }
            }
        }

        private void ConfigureSyntheticScreenFromNewScreen(uint newScreen)
        {
            if (newScreen == 0 ||
                _syntheticScreenAddress != 0 ||
                _syntheticPlaneAddress != 0 ||
                !_machine.Bus.IsMappedMemoryRange(newScreen, NewScreenStructMinimumSize))
            {
                return;
            }

            var requestedWidth = _machine.Bus.ReadWord(newScreen + NewScreenWidthOffset);
            var requestedHeight = _machine.Bus.ReadWord(newScreen + NewScreenHeightOffset);
            var requestedDepth = _machine.Bus.ReadByte(newScreen + NewScreenDepthOffset);
            var requestedModes = _machine.Bus.ReadWord(newScreen + NewScreenViewModesOffset);

            if (requestedWidth >= 64)
            {
                _syntheticScreenWidth = Math.Clamp(
                    (int)requestedWidth,
                    64,
                    AmigaConstants.PalHighResWidth);
            }

            if (requestedHeight >= 16)
            {
                _syntheticScreenHeight = Math.Clamp(
                    (int)requestedHeight,
                    16,
                    AmigaConstants.PalLowResHeight);
            }

            if (requestedDepth != 0)
            {
                _syntheticScreenDepth = Math.Clamp((int)requestedDepth, 1, 5);
            }

            _syntheticScreenViewModes = (ushort)(requestedModes & (ViewModeHires | ViewModeInterlace));
            if (_syntheticScreenWidth > AmigaConstants.PalLowResWidth)
            {
                _syntheticScreenViewModes |= ViewModeHires;
            }
        }

        private void ConfigureSyntheticWindowFromNewWindow(uint newWindow)
        {
            if (newWindow == 0 ||
                _syntheticWindowAddress != 0 ||
                !_machine.Bus.IsMappedMemoryRange(newWindow, NewWindowFirstGadgetOffset + 4))
            {
                return;
            }

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

        private void SetSyntheticWindowTitles(M68kCpuState state)
        {
            var windowTitle = ReadOptionalIntuitionTitle(state.A[1], 80);
            var screenTitle = ReadOptionalIntuitionTitle(state.A[2], 80);
            var title = !string.IsNullOrWhiteSpace(windowTitle)
                ? windowTitle
                : screenTitle;

            LogIntuitionTitle(windowTitle, screenTitle);

            _ = EnsureSyntheticScreen();
            if (string.IsNullOrWhiteSpace(title) || !EnsureSyntheticScreenBitmap())
            {
                return;
            }

            RenderSyntheticScreenTitle(title);
            _ = HostRethinkDisplay(state.Cycles);
            state.Cycles += AmigaConstants.A500PalCpuCyclesPerFrame;
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
            if (address == 0 || address == NoTitleChange || !_machine.Bus.IsMappedMemoryRange(address, 1))
            {
                return string.Empty;
            }

            return ReadNullTerminatedString(address, maxLength);
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
        {
            _currentViewAddress = state.A[1];
            if (_currentViewAddress == 0)
            {
                _cyberGraphics?.SelectFrontViewPort(0);
                return;
            }

            if (TryReadLong(_currentViewAddress + ViewViewPortOffset, out var viewPort))
            {
                _cyberGraphics?.SelectFrontViewPort(viewPort);
                if (_cyberGraphics?.RtgDevice?.FrontViewPort == viewPort)
                {
                    return;
                }
            }

            _ = TryPublishCopperListFromView(_currentViewAddress, state.Cycles);
        }

        private void LoadRgb4(M68kCpuState state)
        {
            var viewPort = state.A[0];
            var colors = state.A[1];
            var count = Math.Clamp(
                unchecked((int)state.D[0]),
                0,
                _syntheticPalette.Length);
            if (!IsSyntheticViewPort(viewPort) ||
                colors == 0 ||
                count <= 0 ||
                !_machine.Bus.IsMappedMemoryRange(colors, count * 2))
            {
                return;
            }

            for (var index = 0; index < count; index++)
            {
                _syntheticPalette[index] = (ushort)(_machine.Bus.ReadWord(colors + (uint)(index * 2)) & 0x0FFF);
            }

            _syntheticPaletteLoaded = true;
            _ = HostRethinkDisplay(state.Cycles);
        }

        private void SetRgb4(M68kCpuState state)
        {
            var viewPort = state.A[0];
            var index = state.D[0];
            if (!IsSyntheticViewPort(viewPort) || index >= (uint)_syntheticPalette.Length)
            {
                return;
            }

            var red = (ushort)(state.D[1] & 0x0F);
            var green = (ushort)(state.D[2] & 0x0F);
            var blue = (ushort)(state.D[3] & 0x0F);
            _syntheticPalette[index] = (ushort)((red << 8) | (green << 4) | blue);
            _syntheticPaletteLoaded = true;
            _ = HostRethinkDisplay(state.Cycles);
        }

        private void DrawRastPort(M68kCpuState state)
        {
            var rastPort = state.A[1];
            if (!TryGetRastPortBitMap(rastPort, out _))
            {
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
            if (!TryGetRastPortBitMap(rastPort, out _))
            {
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
            if (view == 0 || viewPort == 0 || !TryBuildViewPortCopperList(view, viewPort, out _))
            {
                return 0;
            }

            return 0;
        }

        private uint MergeCopperLists(M68kCpuState state)
        {
            var view = state.A[1];
            if (view == 0)
            {
                return 0;
            }

            var viewPort = TryReadLong(view + ViewViewPortOffset, out var pointer)
                ? pointer
                : 0;
            if (viewPort != 0)
            {
                _ = TryBuildViewPortCopperList(view, viewPort, out _);
            }

            return 0;
        }

        private uint HostRethinkDisplay(long cycle)
        {
            if (_currentViewAddress == 0)
            {
                return 0;
            }

            var view = _currentViewAddress;
            if (TryReadLong(view + ViewViewPortOffset, out var viewPort) && viewPort != 0)
            {
                _ = TryBuildViewPortCopperList(view, viewPort, out _);
            }

            _ = TryPublishCopperListFromView(view, cycle);
            return 0;
        }

        private bool TryBuildViewPortCopperList(uint view, uint viewPort, out uint copperList)
        {
            copperList = 0;
            if (!TryCreateCopperListFromViewPort(viewPort, out var rawCopperList))
            {
                return false;
            }

            var cprList = AllocateProgramMemory(0x10);
            _machine.Bus.ClearMemory(cprList, 0x10);
            _machine.Bus.WriteLong(cprList + CprListStartOffset, rawCopperList);
            _machine.Bus.WriteWord(cprList + 0x08, 64);
            _machine.Bus.WriteLong(view + ViewViewPortOffset, viewPort);
            _machine.Bus.WriteLong(view + ViewLofCprListOffset, cprList);
            _machine.Bus.WriteLong(view + ViewShfCprListOffset, cprList);
            _machine.Bus.WriteLong(viewPort + ViewPortDspInsOffset, rawCopperList);
            copperList = rawCopperList;
            return true;
        }

        private bool TryCreateCopperListFromViewPort(uint viewPort, out uint copperList)
        {
            copperList = 0;
            if (!_machine.Bus.IsMappedMemoryRange(viewPort, 0x28))
            {
                return false;
            }

            var width = ReadPositiveWordOrDefault(viewPort + ViewPortDWidthOffset, 320);
            var height = ReadPositiveWordOrDefault(viewPort + ViewPortDHeightOffset, 256);
            var dx = TryReadWord(viewPort + ViewPortDxOffsetOffset, out var dxWord) ? unchecked((short)dxWord) : 0;
            var dy = TryReadWord(viewPort + ViewPortDyOffsetOffset, out var dyWord) ? unchecked((short)dyWord) : 0;
            var modes = TryReadWord(viewPort + ViewPortModesOffset, out var modesWord) ? modesWord : (ushort)0;
            var highResolution = (modes & ViewModeHires) != 0;
            var depth = 1;
            var bytesPerRow = Math.Max(2, ((width + 15) / 16) * 2);
            var planes = new uint[6];
            var bitMap = 0u;
            var sourceX = 0;
            var sourceY = 0;
            var hasBitmap = TryReadLong(viewPort + ViewPortRasInfoOffset, out var rasInfo) &&
                rasInfo != 0 &&
                TryReadLong(rasInfo + RasInfoBitMapOffset, out bitMap) &&
                bitMap != 0 &&
                _machine.Bus.IsMappedMemoryRange(bitMap, BitMapPlanesOffset + (planes.Length * 4));
            if (hasBitmap)
            {
                bytesPerRow = Math.Max(2, ReadPositiveWordOrDefault(bitMap + BitMapBytesPerRowOffset, bytesPerRow));
                var bitmapRows = ReadPositiveWordOrDefault(bitMap + BitMapRowsOffset, height);
                sourceX = TryReadWord(rasInfo + RasInfoRxOffsetOffset, out var rxOffsetWord)
                    ? unchecked((short)rxOffsetWord)
                    : 0;
                sourceY = TryReadWord(rasInfo + RasInfoRyOffsetOffset, out var ryOffsetWord)
                    ? unchecked((short)ryOffsetWord)
                    : 0;
                if (sourceY >= 0)
                {
                    height = Math.Max(1, Math.Min(height, Math.Max(1, bitmapRows - sourceY)));
                }

                height = Math.Max(1, Math.Min(height, bitmapRows));
                depth = Math.Clamp((int)_machine.Bus.ReadByte(bitMap + BitMapDepthOffset), 1, planes.Length);
                var sourceByteOffset = (sourceY * bytesPerRow) + ((sourceX / 16) * 2);
                for (var plane = 0; plane < depth; plane++)
                {
                    planes[plane] = _machine.Bus.AddChipDmaPointerOffset(
                        _machine.Bus.ReadLong(bitMap + BitMapPlanesOffset + (uint)(plane * 4)),
                        sourceByteOffset);
                }
            }

            var hasPlane = false;
            for (var plane = 0; plane < depth; plane++)
            {
                hasPlane |= planes[plane] != 0;
            }

            if (!hasBitmap || !hasPlane)
            {
                return false;
            }

            copperList = AllocateChipProgramMemory(0x100);
            var offset = copperList;
            WriteCopperMove(ref offset, 0x08E, EncodeDiwStart(dx, dy));
            WriteCopperMove(
                ref offset,
                0x090,
                EncodeDiwStop(dx, dy, highResolution ? Math.Max(16, width / 2) : width, height));
            var fetchWords = Math.Clamp((width + 15) / 16, 1, 64);
            var ddfStart = highResolution ? (ushort)0x003C : (ushort)0x0038;
            var ddfStop = highResolution
                ? (ushort)0x00D0
                : (ushort)(0x0038 + ((fetchWords - 1) * 8));
            WriteCopperMove(ref offset, 0x092, ddfStart);
            WriteCopperMove(ref offset, 0x094, ddfStop);
            var modulo = (short)(bytesPerRow - (fetchWords * 2));
            WriteCopperMove(ref offset, 0x108, unchecked((ushort)modulo));
            WriteCopperMove(ref offset, 0x10A, unchecked((ushort)modulo));
            var bplcon0Modes = modes & (ViewModeHires | ViewModeInterlace);
            WriteCopperMove(ref offset, 0x100, (ushort)((depth << 12) | bplcon0Modes));
            for (var plane = 0; plane < depth; plane++)
            {
                var register = (ushort)(0x0E0 + (plane * 4));
                WriteCopperMove(ref offset, register, (ushort)(planes[plane] >> 16));
                WriteCopperMove(ref offset, (ushort)(register + 2), (ushort)planes[plane]);
            }

            var colorCount = Math.Clamp(1 << depth, 2, 32);
            for (var color = 0; color < colorCount; color++)
            {
                WriteCopperMove(
                    ref offset,
                    (ushort)(0x180 + (color * 2)),
                    GetViewPortColor(viewPort, color));
            }

            _machine.Bus.WriteWord(offset, 0xFFFF);
            _machine.Bus.WriteWord(offset + 2, 0xFFFE);
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

            return index == 0 ? (ushort)0x000 : (ushort)0xFFF;
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

            if (TryResolveCopperListStartFromView(view, out var copperList))
            {
                QueueCopperListLoad(copperList, cycle);
                return true;
            }

            return false;
        }

        private bool TryResolveCopperListStartFromView(uint view, out uint copperList)
        {
            copperList = 0;
            if (!_machine.Bus.IsMappedMemoryRange(view, ViewShfCprListOffset + 4))
            {
                return false;
            }

            if (TryResolveCopperListStart(_machine.Bus.ReadLong(view + ViewLofCprListOffset), out copperList) ||
                TryResolveCopperListStart(_machine.Bus.ReadLong(view + ViewShfCprListOffset), out copperList))
            {
                return true;
            }

            var viewPort = _machine.Bus.ReadLong(view + ViewViewPortOffset);
            if (viewPort != 0 &&
                _machine.Bus.IsMappedMemoryRange(viewPort, ViewPortDspInsOffset + 4) &&
                TryResolveCopperListStart(_machine.Bus.ReadLong(viewPort + ViewPortDspInsOffset), out copperList))
            {
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

            if (_machine.Bus.IsMappedMemoryRange(candidate + CprListStartOffset, 4))
            {
                var wrappedStart = _machine.Bus.ReadLong(candidate + CprListStartOffset);
                if (LooksLikeCopperList(wrappedStart))
                {
                    copperList = wrappedStart;
                    return true;
                }
            }

            if (LooksLikeCopperList(candidate))
            {
                copperList = candidate;
                return true;
            }

            return false;
        }

        private bool LooksLikeCopperList(uint address)
        {
            if (address == 0 || !_machine.Bus.IsMappedMemoryRange(address, 4))
            {
                return false;
            }

            var sawInstruction = false;
            for (var offset = 0u; offset < 0x100; offset += 4)
            {
                if (!_machine.Bus.IsMappedMemoryRange(address + offset, 4))
                {
                    return false;
                }

                var first = _machine.Bus.ReadWord(address + offset);
                var second = _machine.Bus.ReadWord(address + offset + 2);
                if (first == 0xFFFF && second == 0xFFFE)
                {
                    return sawInstruction;
                }

                if (first == 0 && second == 0)
                {
                    return false;
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
            _machine.Bus.WriteWord(0x00DFF096, 0x8380, cycle);
            _machine.Bus.WriteWord(0x00DFF080, (ushort)(copperList >> 16), cycle);
            _machine.Bus.WriteWord(0x00DFF082, (ushort)copperList, cycle);
            _machine.Bus.WriteWord(0x00DFF088, 0, cycle);
        }

        private void QueueCopperListLoad(uint copperList, long cycle)
        {
            var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
            var normalizedCycle = Math.Max(0, cycle);
            if (normalizedCycle % frameCycles == 0)
            {
                LoadCopperList(copperList, cycle);
                return;
            }

            _pendingCopperList = copperList;
            _pendingCopperListCycle = ((normalizedCycle / frameCycles) + 1) * frameCycles;
        }

        private void ApplyPendingCopperListAtFrameBoundary(long currentCycle)
        {
            if (_pendingCopperList == 0 || _pendingCopperListCycle < 0 || currentCycle < _pendingCopperListCycle)
            {
                return;
            }

            var copperList = _pendingCopperList;
            var publishCycle = _pendingCopperListCycle;
            _pendingCopperList = 0;
            _pendingCopperListCycle = -1;
            LoadCopperList(copperList, publishCycle);
        }

        private void WriteCopperMove(ref uint offset, ushort register, ushort value)
        {
            _machine.Bus.WriteWord(offset, (ushort)(register & 0x01FE));
            _machine.Bus.WriteWord(offset + 2, value);
            offset += 4;
        }

        private int ReadPositiveWordOrDefault(uint address, int defaultValue)
        {
            return TryReadWord(address, out var value) && value != 0 ? value : defaultValue;
        }

        private bool TryReadLong(uint address, out uint value)
        {
            if (!_machine.Bus.IsMappedMemoryRange(address, 4))
            {
                value = 0;
                return false;
            }

            value = _machine.Bus.ReadLong(address);
            return true;
        }

        private bool TryReadWord(uint address, out ushort value)
        {
            if (!_machine.Bus.IsMappedMemoryRange(address, 2))
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
            if (_syntheticScreenAddress != 0)
            {
                return _syntheticScreenAddress;
            }

            _syntheticScreenAddress = AllocateProgramMemory(0x100);
            if (_syntheticScreenAddress == 0)
            {
                return EnsureSyntheticHostObject();
            }

            _machine.Bus.WriteWord(_syntheticScreenAddress + ScreenWidthOffset, (ushort)_syntheticScreenWidth);
            _machine.Bus.WriteWord(_syntheticScreenAddress + ScreenHeightOffset, (ushort)_syntheticScreenHeight);
            InitializeSyntheticViewPort(GetSyntheticScreenViewPortAddress());
            _ = EnsureSyntheticScreenBitmap();
            _machine.Bus.WriteLong(_syntheticScreenAddress + ScreenFirstWindowOffset, EnsureSyntheticWindow());
            EnsureSyntheticView();
            return _syntheticScreenAddress;
        }

        private uint GetSyntheticScreenViewPortAddress()
        {
            return EnsureSyntheticScreen() + ScreenViewPortOffset;
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

            _machine.Bus.WriteLong(_syntheticViewAddress + ViewViewPortOffset, GetSyntheticScreenViewPortAddress());
            _currentViewAddress = _syntheticViewAddress;
            return _syntheticViewAddress;
        }

        private void InitializeSyntheticViewPort(uint viewPort)
        {
            _machine.Bus.WriteWord(viewPort + ViewPortDWidthOffset, (ushort)_syntheticScreenWidth);
            _machine.Bus.WriteWord(viewPort + ViewPortDHeightOffset, (ushort)_syntheticScreenHeight);
            _machine.Bus.WriteWord(viewPort + ViewPortDxOffsetOffset, 0);
            _machine.Bus.WriteWord(viewPort + ViewPortDyOffsetOffset, 0);
            _machine.Bus.WriteWord(viewPort + ViewPortModesOffset, _syntheticScreenViewModes);
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

            if (_cyberGraphics?.RtgDevice?.IsAvailable == true)
            {
                var surface = CyberGraphics.AllocateRtgSurface(
                    _syntheticScreenWidth,
                    _syntheticScreenHeight,
                    CyberGraphicsPixelFormat.Lut8);
                if (surface == null)
                {
                    return false;
                }

                _syntheticPlaneAddress = surface.GuestBaseAddress;
                _syntheticRasInfoAddress = AllocateProgramMemory(0x10);
                _syntheticBitMapAddress = AllocateProgramMemory(BitMapPlanesOffset + 8 * 4);
                _machine.Bus.ClearMemory(_syntheticRasInfoAddress, 0x10);
                _machine.Bus.WriteLong(_syntheticRasInfoAddress + RasInfoBitMapOffset, _syntheticBitMapAddress);
                WriteRtgBitMap(_syntheticBitMapAddress, surface);
                WriteRtgBitMap(_syntheticScreenAddress + ScreenBitMapOffset, surface);
                CyberGraphics.RegisterBitMap(_syntheticBitMapAddress, surface);
                CyberGraphics.RegisterBitMap(_syntheticScreenAddress + ScreenBitMapOffset, surface);
                var viewPort = GetSyntheticScreenViewPortAddress();
                _machine.Bus.WriteLong(viewPort + ViewPortRasInfoOffset, _syntheticRasInfoAddress);
                InitializeSyntheticRastPort(_syntheticScreenAddress + ScreenRastPortOffset, GetSyntheticScreenBitMapAddress());
                var rastPort = EnsureSyntheticRastPort();
                CyberGraphics.RegisterRastPort(_syntheticScreenAddress + ScreenRastPortOffset, surface);
                CyberGraphics.RegisterRastPort(rastPort, surface);
                CyberGraphics.RegisterViewPort(viewPort, surface);
                CyberGraphics.SelectFrontViewPort(viewPort);
                RenderSyntheticScreenTitle("Loading");
                return true;
            }

            var planeBytes = GetSyntheticScreenPlaneSize() * _syntheticScreenDepth;
            _syntheticPlaneAddress = AllocateMemoryFromMemList(
                planeBytes,
                MemfPublic | MemfChip | MemfClear);
            if (_syntheticPlaneAddress == 0)
            {
                return false;
            }

            _syntheticRasInfoAddress = AllocateProgramMemory(0x10);
            _syntheticBitMapAddress = AllocateProgramMemory(BitMapPlanesOffset + 6 * 4);
            _machine.Bus.ClearMemory(_syntheticRasInfoAddress, 0x10);
            _machine.Bus.WriteLong(_syntheticRasInfoAddress + RasInfoBitMapOffset, _syntheticBitMapAddress);
            WriteSyntheticBitMap(_syntheticBitMapAddress);
            WriteSyntheticBitMap(_syntheticScreenAddress + ScreenBitMapOffset);

            _machine.Bus.WriteLong(GetSyntheticScreenViewPortAddress() + ViewPortRasInfoOffset, _syntheticRasInfoAddress);
            InitializeSyntheticRastPort(
                _syntheticScreenAddress + ScreenRastPortOffset,
                GetSyntheticScreenBitMapAddress());
            _ = EnsureSyntheticRastPort();
            RenderSyntheticScreenTitle("Loading");
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
            => _syntheticScreenAddress != 0
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

                return _syntheticRastPortAddress;
            }

            _syntheticRastPortAddress = AllocateProgramMemory(0x80);
            if (_syntheticRastPortAddress == 0)
            {
                return EnsureSyntheticHostObject();
            }

            InitializeSyntheticRastPort(
                _syntheticRastPortAddress,
                GetSyntheticScreenBitMapAddress());
            return _syntheticRastPortAddress;
        }

        private void InitializeSyntheticRastPort(uint rastPort, uint bitMap)
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
            _machine.Bus.WriteLong(rastPort + RastPortFontOffset, EnsureSyntheticFont());
            _machine.Bus.WriteWord(rastPort + RastPortTextHeightOffset, 8);
            _machine.Bus.WriteWord(rastPort + RastPortTextWidthOffset, 8);
            _machine.Bus.WriteWord(rastPort + RastPortTextBaselineOffset, 7);
            _machine.Bus.WriteWord(rastPort + RastPortTextSpacingOffset, 0);
        }

        private uint EnsureSyntheticFont()
        {
            if (_syntheticFontAddress != 0)
            {
                return _syntheticFontAddress;
            }

            _syntheticFontAddress = AllocateProgramMemory(0x40);
            if (_syntheticFontAddress != 0)
            {
                _machine.Bus.ClearMemory(_syntheticFontAddress, 0x40);
                _machine.Bus.WriteWord(_syntheticFontAddress + 0x14, 8);
                _machine.Bus.WriteByte(_syntheticFontAddress + 0x16, 7, 0);
                _machine.Bus.WriteByte(_syntheticFontAddress + 0x17, 8, 0);
            }

            return _syntheticFontAddress != 0 ? _syntheticFontAddress : EnsureSyntheticHostObject();
        }

        private void WriteSyntheticBitMap(uint bitMapAddress)
            => _syntheticDisplayServices.WriteBitMap(bitMapAddress, BitMapBytesPerRowOffset, BitMapRowsOffset, BitMapDepthOffset, BitMapPlanesOffset);

        private void RenderSyntheticScreenTitle(string title)
            => _syntheticDisplayServices.RenderTitle(title, SyntheticScreenTitleHeight);

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
            if (CyberGraphics.TryGetBitMapSurface(bitMap, out var rtgSurface))
            {
                info = new HostBitMapInfo(rtgSurface);
                return true;
            }

            if (bitMap == 0 || !_machine.Bus.IsMappedMemoryRange(bitMap, BitMapPlanesOffset + 4))
            {
                return false;
            }

            var bytesPerRow = ReadPositiveWordOrDefault(bitMap + BitMapBytesPerRowOffset, GetSyntheticScreenBytesPerRow());
            var rows = ReadPositiveWordOrDefault(bitMap + BitMapRowsOffset, _syntheticScreenHeight);
            var depth = Math.Clamp((int)_machine.Bus.ReadByte(bitMap + BitMapDepthOffset), 1, 8);
            var planes = new uint[depth];
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

            info = new HostBitMapInfo(bytesPerRow, rows, depth, planes);
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
                CyberGraphics.WriteSurfacePen(surface, x, y, (byte)color, writeMask);
                return;
            }

            var byteOffset = (y * info.BytesPerRow) + (x >> 3);
            var mask = (byte)(0x80 >> (x & 7));
            for (var plane = 0; plane < info.Depth; plane++)
            {
                if ((writeMask & (1 << plane)) == 0)
                {
                    continue;
                }

                var planeAddress = info.Planes[plane];
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
                return _syntheticWindowAddress;
            }

            _syntheticWindowAddress = AllocateProgramMemory(0x100);
            if (_syntheticWindowAddress != 0)
            {
                var syntheticPort = EnsureSyntheticUserPort();
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
            if (_kickstartRomExecTakeoverState == KickstartRomExecTakeoverState.Active)
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

        private void LinkExecNode(uint node, uint predecessor, uint successor)
            => GetExecListServices().Link(node, predecessor, successor);

        private uint RemoveExecNode(uint node)
            => GetExecListServices().Remove(node);

        private uint RemoveExecListEnd(uint list, bool head)
            => GetExecListServices().RemoveEnd(list, head);

        private void AddTailExecList(uint list, uint node)
            => GetExecListServices().AddTail(list, node);

        private void AddExecNodeAtomically(uint list, uint node, bool priorityOrdered, M68kCpuState state)
        {
            if (!IsValidExecList(list) || !IsValidExecNode(node) || ContainsExecNode(list, node)) return;
            _execTaskServices.Forbid(state);
            try
            {
                if (priorityOrdered)
                {
                    var priority = unchecked((sbyte)_machine.Bus.ReadByte(node + 9));
                    var current = _machine.Bus.ReadLong(list);
                    while (current != list + 4 && IsValidExecNode(current) &&
                        unchecked((sbyte)_machine.Bus.ReadByte(current + 9)) >= priority)
                    {
                        current = _machine.Bus.ReadLong(current + NodeSuccessorOffset);
                    }

                    var predecessor = _machine.Bus.ReadLong(current + NodePredecessorOffset);
                    if (IsValidExecNode(predecessor))
                    {
                        LinkExecNode(node, predecessor, current);
                        if (current == list + 4) _machine.Bus.WriteLong(list + 8, node);
                    }
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
            if (_kickstartRomExecTakeoverState == KickstartRomExecTakeoverState.Active)
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

            var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
            var nextFrameCycle = ((Math.Max(0, state.Cycles) / frameCycles) + 1) * frameCycles;
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

        private uint FindNameInExecList(uint list, uint nameAddress)
            => GetExecListServices().FindName(list, nameAddress);

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
            _machine.Bus.MapWritableMemory(AmigaKickstartHost.ExecLibraryBase, execImage);
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
                    "real-fast");
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
                    "pseudo-fast");
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
                    "chip");
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
                    "chip");
            }

            _memoryListInstalled = true;
            EnsureAbsExecBasePointer();
            if (_kickstartRomBootActive)
            {
                _machine.Bus.StrictCpuPhysicalDataMapping = true;
            }
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
            string name)
        {
            _machine.Bus.ClearMemory(headerAddress, 0x40);
            _machine.Bus.WriteLong(headerAddress, successor);
            _machine.Bus.WriteLong(headerAddress + 4, predecessor);
            _machine.Bus.WriteByte(headerAddress + 8, 10, 0);
            _machine.Bus.WriteByte(headerAddress + 9, 0, 0);
            _machine.Bus.WriteLong(headerAddress + MemNodeNameOffset, nameAddress);
            _machine.Bus.WriteWord(headerAddress + MemHeaderAttributesOffset, (ushort)attributes);
            _machine.Bus.WriteLong(headerAddress + MemHeaderLowerOffset, lower);
            _machine.Bus.WriteLong(headerAddress + MemHeaderUpperOffset, upper);
            WriteFixedAscii(nameAddress, name, 16);

            if (upper <= lower)
            {
                _machine.Bus.WriteLong(headerAddress + MemHeaderFirstChunkOffset, 0);
                _machine.Bus.WriteLong(headerAddress + MemHeaderFreeOffset, 0);
                return;
            }

            var freeBytes = AlignDown(upper - lower, 8);
            _machine.Bus.WriteLong(headerAddress + MemHeaderFirstChunkOffset, lower);
            _machine.Bus.WriteLong(headerAddress + MemHeaderFreeOffset, freeBytes);
            _machine.Bus.WriteLong(lower + MemChunkNextOffset, 0);
            _machine.Bus.WriteLong(lower + MemChunkBytesOffset, freeBytes);
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
            var flags = _machine.Bus.RealFastRam.Length != 0 || _machine.Bus.ExpansionRam.Length != 0
                ? MemfPublic | MemfFast
                : MemfPublic;
            var address = AllocateMemoryFromMemList(Math.Max(4, byteCount), flags);
            if (address == 0)
            {
                throw new AmigaEmulationException("The boot program does not fit in the available emulated memory.");
            }

            return address;
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
            public HostBitMapInfo(int bytesPerRow, int height, int depth, uint[] planes)
            {
                BytesPerRow = bytesPerRow;
                Height = height;
                Depth = depth;
                Planes = planes;
                RtgSurface = null;
                Width = bytesPerRow * 8;
            }

            public HostBitMapInfo(CyberGraphicsSurface surface)
            {
                RtgSurface = surface;
                BytesPerRow = surface.BytesPerRow;
                Height = surface.Height;
                Depth = surface.Depth;
                Planes = Array.Empty<uint>();
                Width = surface.Width;
            }

            public int BytesPerRow { get; }

            public int Height { get; }

            public int Depth { get; }

            public uint[] Planes { get; }

            public CyberGraphicsSurface? RtgSurface { get; }

            public int Width { get; }
        }

        private readonly struct SyntheticInterruptServer
        {
            public SyntheticInterruptServer(uint interruptAddress, uint dataAddress)
            {
                InterruptAddress = interruptAddress;
                DataAddress = dataAddress;
            }

            public uint InterruptAddress { get; }

            public uint DataAddress { get; }
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
