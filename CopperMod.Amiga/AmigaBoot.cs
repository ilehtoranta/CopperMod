using System;
using System.Collections.Generic;
using System.Text;

namespace CopperMod.Amiga
{
    internal enum AmigaBootRunMode
    {
        StopAfterBootDiskRead,
        ContinueAfterBootDiskRead
    }

    internal sealed class AmigaBootController
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
        private const int ExecBaseImageSize = 0x180;
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
        private const int ExecMemListOffset = 0x142;
        private const int TaskNodeTypeOffset = 0x08;
        private const int TaskNodeNameOffset = 0x0A;
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
        private const uint MemfClear = 0x0001_0000;
        private const uint MemfLargest = 0x0002_0000;
        private const uint MemfTotal = 0x0008_0000;
        private const uint BootChipPublicLowerAddress = 0x0000_0400;
        private const uint BootSupervisorStackTopAddress = 0x0000_0400;
        private const uint DosProgramReturnAddress = 0x00FF_FFFC;
        private const uint SafeInterruptReturnAddress = 0x00F0_7F00;
        private const uint TaskTrapDispatcherBaseAddress = 0x00F0_8000;
        private const uint DefaultTaskTrapCodeAddress = 0x00F0_8100;
        private const uint BootPseudoFastMetadataSize = 0x0000_0200;
        private const uint BootPseudoFastStackReserve = 0x0000_1000;
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
        private const int ScreenViewPortOffset = 0x2C;
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
        private const ushort ViewModeInterlace = 0x0004;

        private readonly AmigaMachine _machine;
        private readonly IAmigaDiskDmaEngine _diskDma;
        private readonly BootInstructionBoundary _instructionBoundary;
        private readonly List<AmigaBootDiagnostic> _diagnostics = new List<AmigaBootDiagnostic>();
        private readonly Dictionary<uint, BootDosHandle> _dosHandles = new Dictionary<uint, BootDosHandle>();
        private readonly Dictionary<uint, AmigaDosDirectoryEntry> _dosLocks = new Dictionary<uint, AmigaDosDirectoryEntry>();
        private bool _bootDiskReadCompleted;
        private bool _dosBootContinuationStarted;
        private bool _dosBootBlockHeaderProbeEnabled;
        private int _hostAllocationDiagnosticCount;
        private int _dosOpenDiagnosticCount;
        private int _dosReadDiagnosticCount;
        private int _openLibraryDiagnosticCount;
        private int _iconDiagnosticCount;
        private int _uiDiagnosticCount;
        private int _execDiagnosticCount;
        private int _hostFreeDiagnosticCount;
        private int _dosWriteDiagnosticCount;
        private int _nextAllocatedSignalBit;
        private uint _allocatedSignalMask;
        private uint _syntheticSignalMask;
        private IReadOnlyList<string> _workbenchToolTypes = Array.Empty<string>();
        private string _workbenchDefaultToolPath = "C/SystemTakeover";
        private string _workbenchCurrentDirectory = string.Empty;
        private int _workbenchStackSize = 4096;
        private int? _workbenchLanguageSelectionIndex;
        private bool _workbenchLanguageSelectionApplied;
        private uint _workbenchDiskObjectAddress;
        private uint _syntheticScreenAddress;
        private uint _syntheticWindowAddress;
        private uint _syntheticMessageAddress;
        private uint _syntheticHostObjectAddress;
        private uint _syntheticViewAddress;
        private uint _currentViewAddress;
        private int _pendingSyntheticMessages;
        private uint _nextDosHandle;
        private uint _nextDosLock;
        private uint _lastDosError;
        private uint _chipMemHeaderAddress;
        private uint _fastMemHeaderAddress;
        private uint _chipMemNameAddress;
        private uint _fastMemNameAddress;
        private uint _currentTaskAddress;
        private uint _chipMemLower;
        private uint _chipMemUpper;
        private uint _fastMemLower;
        private uint _fastMemUpper;
        private bool _memoryListInstalled;
        private AmigaDosFileSystem? _dosFileSystem;

        public AmigaBootController(AmigaMachine machine, IAmigaDiskDmaEngine? diskDma = null)
        {
            _machine = machine ?? throw new ArgumentNullException(nameof(machine));
            _diskDma = diskDma ?? new ImmediateDiskDmaEngine();
            _instructionBoundary = new BootInstructionBoundary(this);
        }

        public AmigaFloppyDrive Drive0 => _machine.Bus.Disk.Drive0;

        public AmigaFloppyDrive Drive1 => _machine.Bus.Disk.Drive1;

        public AmigaFloppyDrive Drive2 => _machine.Bus.Disk.Drive2;

        public AmigaFloppyDrive Drive3 => _machine.Bus.Disk.Drive3;

        public IReadOnlyList<AmigaBootDiagnostic> Diagnostics => _diagnostics;

        public bool AutoStartWorkbenchDefaultTool { get; set; } = true;

        public AmigaProgramLaunchRequest? PendingWorkbenchLaunchRequest { get; private set; }

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
            if (_machine.Kickstart.Configuration.Backend != AmigaKickstartBackendKind.RomImage)
            {
                throw new InvalidOperationException("Kickstart ROM boot requires a ROM-backed Kickstart configuration.");
            }

            ResetBootState(disk, installHostShim: false);
            _machine.Kickstart.Install(_machine.Bus, CreateHostTrapTable());
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

        private void ResetBootState(AmigaDiskImage disk, bool installHostShim)
        {
            ArgumentNullException.ThrowIfNull(disk);
            _diagnostics.Clear();
            _dosHandles.Clear();
            _dosLocks.Clear();
            _bootDiskReadCompleted = false;
            _dosBootContinuationStarted = false;
            _dosBootBlockHeaderProbeEnabled = true;
            _hostAllocationDiagnosticCount = 0;
            _dosOpenDiagnosticCount = 0;
            _dosReadDiagnosticCount = 0;
            _openLibraryDiagnosticCount = 0;
            _iconDiagnosticCount = 0;
            _uiDiagnosticCount = 0;
            _execDiagnosticCount = 0;
            _hostFreeDiagnosticCount = 0;
            _dosWriteDiagnosticCount = 0;
            _nextAllocatedSignalBit = 0;
            _allocatedSignalMask = 0;
            _syntheticSignalMask = 0;
            _workbenchToolTypes = Array.Empty<string>();
            _workbenchDefaultToolPath = "C/SystemTakeover";
            _workbenchCurrentDirectory = string.Empty;
            _workbenchStackSize = 4096;
            _workbenchLanguageSelectionIndex = null;
            _workbenchLanguageSelectionApplied = false;
            _workbenchDiskObjectAddress = 0;
            _syntheticScreenAddress = 0;
            _syntheticWindowAddress = 0;
            _syntheticMessageAddress = 0;
            _syntheticHostObjectAddress = 0;
            _syntheticViewAddress = 0;
            _currentViewAddress = 0;
            _pendingSyntheticMessages = 0;
            _nextDosHandle = 0x0000_5000;
            _nextDosLock = 0x0000_7000;
            _lastDosError = 0;
            _chipMemHeaderAddress = 0;
            _fastMemHeaderAddress = 0;
            _chipMemNameAddress = 0;
            _fastMemNameAddress = 0;
            _currentTaskAddress = 0;
            _chipMemLower = 0;
            _chipMemUpper = 0;
            _fastMemLower = 0;
            _fastMemUpper = 0;
            _memoryListInstalled = false;
            _dosFileSystem = null;
            PendingWorkbenchLaunchRequest = null;
            Drive0.Insert(disk);
            Drive1.Eject();
            Drive2.Eject();
            Drive3.Eject();
            _machine.ResetHardware();
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
            _machine.Bus.Paula.AdvanceTo(0);
        }

        public AmigaBootResult ContinueExecution(int maxInstructions = 20_000)
        {
            return ExecuteBootBlock(maxInstructions, AmigaBootRunMode.ContinueAfterBootDiskRead);
        }

        public AmigaBootResult ContinueExecutionUntilCycle(long targetCycle, int maxInstructions = 100_000, Action<long, long>? beforeDeviceAdvance = null)
        {
            return ExecuteBootBlock(
                maxInstructions,
                AmigaBootRunMode.ContinueAfterBootDiskRead,
                targetCycle,
                reportOverrun: false,
                beforeDeviceAdvance);
        }

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
            _machine.Kickstart.Install(bus, CreateHostTrapTable());
            InstallSafeAutovectors(bus);
            for (var displacement = -6; displacement >= -600; displacement -= 6)
            {
                var captured = displacement;
                bus.RegisterHostCallback(Lvo(AmigaKickstartHost.ExecLibraryBase, captured), state => HostExecGeneric(state, captured));
            }

            InstallTaskTrapDispatchers();
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.ExecLibraryBase, -456), HostDoIo);
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.ExecLibraryBase, -96), HostFindResident);
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.ExecLibraryBase, -132), HostOk);
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.ExecLibraryBase, -138), HostOk);
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.ExecLibraryBase, -276), HostFindName);
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.ExecLibraryBase, -198), HostAllocMem);
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.ExecLibraryBase, -204), HostAllocAbs);
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.ExecLibraryBase, -210), HostFreeMem);
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.ExecLibraryBase, -216), HostAvailMem);
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.ExecLibraryBase, -408), HostOpenLibrary);
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.ExecLibraryBase, -414), HostOk);
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.ExecLibraryBase, -498), HostOpenLibrary);
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.ExecLibraryBase, -552), HostOpenLibrary);
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.DosLibraryBase, -48), HostDosWrite);
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.DosLibraryBase, -54), HostDosInput);
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.DosLibraryBase, -60), HostDosOutput);
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.DosLibraryBase, -84), HostDosLock);
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.DosLibraryBase, -90), HostDosUnLock);
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.DosLibraryBase, -102), HostDosExamine);
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.DosLibraryBase, -126), HostDosCurrentDir);
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.DosLibraryBase, -132), HostDosIoErr);
            for (var displacement = -6; displacement >= -180; displacement -= 6)
            {
                var captured = displacement;
                bus.RegisterHostCallback(Lvo(AmigaKickstartHost.IconLibraryBase, captured), state => HostIconGeneric(state, captured));
            }

            for (var displacement = -6; displacement >= -600; displacement -= 6)
            {
                var captured = displacement;
                bus.RegisterHostCallback(Lvo(AmigaKickstartHost.GraphicsLibraryBase, captured), state => HostGraphicsGeneric(state, captured));
                bus.RegisterHostCallback(Lvo(AmigaKickstartHost.IntuitionLibraryBase, captured), state => HostIntuitionGeneric(state, captured));
                bus.RegisterHostCallback(Lvo(AmigaKickstartHost.ExpansionLibraryBase, captured), state => HostExpansionGeneric(state, captured));
            }

            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.IconLibraryBase, -78), HostIconGetDiskObject);
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.IconLibraryBase, -90), HostIconFreeDiskObject);
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.IconLibraryBase, -96), HostIconFindToolType);
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.IconLibraryBase, -102), HostIconMatchToolValue);
            bus.RegisterHostCallback(DosResidentInitAddress, HostInitResident);
            InstallKickstartMemoryList();
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

        private AmigaKickstartTrapTable CreateHostTrapTable()
        {
            return new AmigaKickstartTrapTable(
                0,
                HostNullCallback,
                HostOk,
                HostOpenLibrary,
                HostAllocMem,
                HostAllocMemAndStore,
                HostFreeMem,
                HostOk,
                HostOk,
                HostOk,
                HostAbleIcr,
                HostSetIcr,
                HostDosOpen,
                HostDosClose,
                HostDosRead,
                HostDosSeek);
        }

        private void InstallTaskTrapDispatchers()
        {
            var bus = _machine.Bus;
            bus.RegisterHostCallback(DefaultTaskTrapCodeAddress, HostDefaultTaskTrapCode);
            for (var trap = 0; trap < 16; trap++)
            {
                var vector = 32 + trap;
                var dispatcherAddress = TaskTrapDispatcherBaseAddress + (uint)(trap * 4);
                bus.RegisterHostCallback(dispatcherAddress, state => HostTaskTrapDispatcher(state, vector));
                bus.WriteLong((uint)(vector * 4), dispatcherAddress);
            }
        }

        private AmigaBootResult ExecuteBootBlock(
            int maxInstructions,
            AmigaBootRunMode runMode,
            long? targetCycle = null,
            bool reportOverrun = true,
            Action<long, long>? beforeDeviceAdvance = null)
        {
            var instructions = 0;
            var boundary = _instructionBoundary;
            boundary.Reset(runMode, beforeDeviceAdvance);
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

        private sealed class BootInstructionBoundary :
            IM68kStoppedCpuFastForwardBoundary,
            IM68kPureCpuTraceBatchBoundary
        {
            private readonly AmigaBootController _owner;
            private AmigaBootRunMode _runMode;
            private Action<long, long>? _beforeDeviceAdvance;
            private int _instructions;

            public BootInstructionBoundary(AmigaBootController owner)
            {
                _owner = owner;
            }

            public void Reset(AmigaBootRunMode runMode, Action<long, long>? beforeDeviceAdvance)
            {
                _runMode = runMode;
                _beforeDeviceAdvance = beforeDeviceAdvance;
                _instructions = 0;
                Completed = false;
            }

            public bool Completed { get; private set; }

            public bool BeforeInstruction()
            {
                if (Completed)
                {
                    return false;
                }

                if (_owner._machine.Cpu.State.ProgramCounter == 0 && _instructions > 0)
                {
                    if (_owner.TryStartDosBootContinuation())
                    {
                        return true;
                    }

                    Completed = true;
                    return false;
                }

                if (_owner._machine.Cpu.State.ProgramCounter == DosProgramReturnAddress && _instructions > 0)
                {
                    Completed = true;
                    return false;
                }

                _owner.SkipDosBootBlockHeaderIfNeeded();
                _owner.ApplyWorkbenchLanguageSelectionIfNeeded();
                return true;
            }

            public void AfterInstruction(long previousCycle, long currentCycle)
                => AfterInstructionBatch(previousCycle, currentCycle, 1);

            public bool TryBeginPureCpuTraceBatch(
                M68kCpuState state,
                long targetCycle,
                out long batchTargetCycle)
            {
                batchTargetCycle = targetCycle;
                if (_runMode == AmigaBootRunMode.StopAfterBootDiskRead ||
                    targetCycle <= state.Cycles ||
                    !BeforeInstruction())
                {
                    return false;
                }

                batchTargetCycle = _owner._machine.Bus.GetNextCpuBatchWakeCandidateCycle(
                    state.Cycles,
                    targetCycle);
                batchTargetCycle = Math.Clamp(batchTargetCycle, state.Cycles + 1, targetCycle);
                return batchTargetCycle > state.Cycles;
            }

            public void AfterPureCpuTraceBatch(long previousCycle, long currentCycle, int instructionCount)
                => AfterInstructionBatch(previousCycle, currentCycle, instructionCount);

            private void AfterInstructionBatch(long previousCycle, long currentCycle, int instructionCount)
            {
                if (instructionCount <= 0)
                {
                    return;
                }

                _beforeDeviceAdvance?.Invoke(previousCycle, currentCycle);
                _owner._machine.Bus.AdvanceRasterTo(currentCycle);
                _owner._machine.Bus.AdvanceCiasTo(currentCycle);
                _owner._machine.Bus.AdvanceDmaTo(currentCycle, advanceLiveAgnus: false);
                _owner._machine.DispatchPendingHardwareInterrupt();
                _instructions += instructionCount;
                if (_owner._bootDiskReadCompleted && _runMode == AmigaBootRunMode.StopAfterBootDiskRead)
                {
                    Completed = true;
                }
            }

            public bool TryFastForwardStoppedInstruction(
                M68kCpuState state,
                long targetCycle,
                out long advancedCycles)
            {
                advancedCycles = 0;
                if (!BeforeInstruction())
                {
                    return false;
                }

                var previousCycle = state.Cycles;
                if (targetCycle <= previousCycle)
                {
                    return false;
                }

                var wakeCycle = _owner._machine.Bus.GetNextStoppedCpuWakeCandidateCycle(previousCycle, targetCycle);
                wakeCycle = Math.Clamp(wakeCycle, previousCycle + 1, targetCycle);
                advancedCycles = wakeCycle - previousCycle;
                state.Cycles = wakeCycle;
                AfterInstruction(previousCycle, wakeCycle);
                return true;
            }
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
            if (ShouldLeaveDf0SelectedAfterBootRead())
            {
                LeaveDf0SelectedAfterBootRead(state.Cycles);
            }

            _machine.Bus.WriteByte(io + IoErrorOffset, 0, state.Cycles);
            _machine.Bus.WriteLong(io + IoActualOffset, length, state.Cycles);
            _bootDiskReadCompleted = true;
            state.D[0] = 0;
        }

        private bool ShouldLeaveDf0SelectedAfterBootRead()
        {
            return Drive0.Disk != null &&
                (IsFullContactDiskOneBootBlock(Drive0.Disk.BootBlock) ||
                    Drive0.Disk.Name.IndexOf("Full Contact", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void LeaveDf0SelectedAfterBootRead(long cycle)
        {
            _machine.Bus.WriteByte(0x00BFD100, 0x77, cycle);
            _machine.Bus.WriteByte(0x00BFD300, 0xFF, cycle);
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

        private static bool IsFullContactDiskOneBootBlock(ReadOnlySpan<byte> bootBlock)
        {
            return bootBlock.Length >= 0xCE &&
                BigEndian.ReadUInt32(bootBlock, 0, "boot magic") == 0x444F_5300 &&
                BigEndian.ReadUInt32(bootBlock, 4, "boot checksum") == 0x730D_90D9 &&
                BigEndian.ReadUInt32(bootBlock, 0xC8, "known protection jump high") == 0x4EB9_0007 &&
                BigEndian.ReadUInt16(bootBlock, 0xCC, "known protection jump low") == 0xB000;
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

        private void HostAllocMem(M68kCpuState state)
        {
            var size = (int)Math.Min(state.D[0], int.MaxValue);
            var flags = state.D[1];
            state.D[0] = AllocateMemoryFromMemList(Math.Max(4, size), flags);
            if (_hostAllocationDiagnosticCount < 16)
            {
                _diagnostics.Add(new AmigaBootDiagnostic(
                    "AMIGA_BOOT_ALLOC_MEM",
                    $"AllocMem requested 0x{size:X} bytes with flags 0x{flags:X8} and returned 0x{state.D[0]:X8}."));
                _hostAllocationDiagnosticCount++;
            }
        }

        private void HostAllocMemAndStore(M68kCpuState state)
        {
            HostAllocMem(state);
            if (state.A[0] != 0)
            {
                _machine.Bus.WriteLong(state.A[0], state.D[0], state.Cycles);
            }
        }

        private void HostAvailMem(M68kCpuState state)
        {
            state.D[0] = QueryAvailableMemory(state.D[1]);
        }

        private void HostAllocAbs(M68kCpuState state)
        {
            var size = (int)Math.Min(state.D[0], int.MaxValue);
            var location = state.A[1];
            state.D[0] = AllocateAbsoluteMemoryFromMemList(Math.Max(4, size), location);
            if (_hostAllocationDiagnosticCount < 16)
            {
                _diagnostics.Add(new AmigaBootDiagnostic(
                    "AMIGA_BOOT_ALLOC_ABS",
                    $"AllocAbs requested 0x{size:X} bytes at 0x{location:X8} and returned 0x{state.D[0]:X8}."));
                _hostAllocationDiagnosticCount++;
            }
        }

        private void HostFreeMem(M68kCpuState state)
        {
            var address = state.A[1];
            var size = (int)Math.Min(state.D[0], int.MaxValue);
            FreeMemoryToMemList(address, size);
            if (_hostFreeDiagnosticCount < 16)
            {
                _diagnostics.Add(new AmigaBootDiagnostic(
                    "AMIGA_BOOT_FREE_MEM",
                    $"FreeMem released 0x{size:X} bytes at 0x{address:X8}."));
                _hostFreeDiagnosticCount++;
            }

            state.D[0] = 0;
        }

        private void HostOpenLibrary(M68kCpuState state)
        {
            var name = ReadNullTerminatedString(state.A[1], 96);
            if (_openLibraryDiagnosticCount < 24)
            {
                _diagnostics.Add(new AmigaBootDiagnostic("AMIGA_BOOT_OPEN_LIBRARY", $"OpenLibrary requested '{name}'."));
                _openLibraryDiagnosticCount++;
            }

            if (name.IndexOf("graphics", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                state.D[0] = AmigaKickstartHost.GraphicsLibraryBase;
            }
            else if (name.IndexOf("intuition", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                state.D[0] = AmigaKickstartHost.IntuitionLibraryBase;
            }
            else if (name.IndexOf("expansion", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                state.D[0] = AmigaKickstartHost.ExpansionLibraryBase;
            }
            else if (name.IndexOf("dos", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                state.D[0] = AmigaKickstartHost.DosLibraryBase;
            }
            else if (name.IndexOf("ciaa", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                state.D[0] = AmigaKickstartHost.CiaAResourceBase;
            }
            else if (name.IndexOf("ciab", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                state.D[0] = AmigaKickstartHost.CiaBResourceBase;
            }
            else if (name.IndexOf("cia", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                state.D[0] = AmigaKickstartHost.CiaBResourceBase;
            }
            else if (name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                state.D[0] = AmigaKickstartHost.IconLibraryBase;
            }
            else
            {
                state.D[0] = AmigaKickstartHost.DummyLibraryBase;
            }
        }

        private void HostDosOpen(M68kCpuState state)
        {
            var path = ReadDosPath(state.D[1]);
            if (path.StartsWith("con:", StringComparison.OrdinalIgnoreCase))
            {
                var consoleHandle = _nextDosHandle;
                _nextDosHandle += 4;
                _dosHandles[consoleHandle] = new BootDosHandle(path, Array.Empty<byte>(), isConsole: true);
                state.D[0] = consoleHandle;
                return;
            }

            if (string.IsNullOrWhiteSpace(path) || !TryReadDosFile(path, out var data))
            {
                if (_dosOpenDiagnosticCount < 16)
                {
                    _diagnostics.Add(new AmigaBootDiagnostic("AMIGA_BOOT_DOS_OPEN_MISSING", $"Open failed for '{path}'."));
                    _dosOpenDiagnosticCount++;
                }

                state.D[0] = 0;
                _lastDosError = 205;
                return;
            }

            var handle = _nextDosHandle;
            _nextDosHandle += 4;
            _dosHandles[handle] = new BootDosHandle(path, data);
            if (_dosOpenDiagnosticCount < 16)
            {
                _diagnostics.Add(new AmigaBootDiagnostic("AMIGA_BOOT_DOS_OPEN", $"Opened '{path}' as 0x{handle:X8}."));
                _dosOpenDiagnosticCount++;
            }

            state.D[0] = handle;
            _lastDosError = 0;
        }

        private void HostDosInput(M68kCpuState state)
        {
            HostDosOutput(state);
        }

        private void HostDosWrite(M68kCpuState state)
        {
            if (!_dosHandles.TryGetValue(state.D[1], out var handle) || !handle.IsConsole)
            {
                state.D[0] = 0xFFFF_FFFF;
                return;
            }

            if (_dosWriteDiagnosticCount < 16)
            {
                _diagnostics.Add(new AmigaBootDiagnostic(
                    "AMIGA_BOOT_DOS_WRITE",
                    ReadMemoryText(state.D[2], checked((int)Math.Min(state.D[3], 160)))));
                _dosWriteDiagnosticCount++;
            }

            state.D[0] = state.D[3];
        }

        private void HostDosOutput(M68kCpuState state)
        {
            foreach (var pair in _dosHandles)
            {
                if (pair.Value.IsConsole)
                {
                    state.D[0] = pair.Key;
                    return;
                }
            }

            var consoleHandle = _nextDosHandle;
            _nextDosHandle += 4;
            _dosHandles[consoleHandle] = new BootDosHandle("con:", Array.Empty<byte>(), isConsole: true);
            state.D[0] = consoleHandle;
        }

        private static void HostDosCurrentDir(M68kCpuState state)
        {
            state.D[0] = WorkbenchRootLock;
        }

        private void HostDosLock(M68kCpuState state)
        {
            var path = ReadDosPath(state.D[1]);
            if (string.IsNullOrWhiteSpace(path) || !TryFindDosEntry(path, out var entry))
            {
                state.D[0] = 0;
                _lastDosError = 205;
                return;
            }

            var lockHandle = _nextDosLock;
            _nextDosLock += 4;
            _dosLocks[lockHandle] = entry;
            state.D[0] = lockHandle;
            _lastDosError = 0;
        }

        private void HostDosUnLock(M68kCpuState state)
        {
            _dosLocks.Remove(state.D[1]);
            state.D[0] = 0;
            _lastDosError = 0;
        }

        private void HostDosExamine(M68kCpuState state)
        {
            if (!_dosLocks.TryGetValue(state.D[1], out var entry))
            {
                state.D[0] = 0;
                _lastDosError = 205;
                return;
            }

            WriteFileInfoBlock(state.D[2], entry);
            state.D[0] = 1;
            _lastDosError = 0;
        }

        private void HostDosIoErr(M68kCpuState state)
        {
            state.D[0] = _lastDosError;
        }

        private void HostIconGetDiskObject(M68kCpuState state)
        {
            _ = state;
            LogIconCall(-78);
            state.D[0] = EnsureWorkbenchDiskObject();
        }

        private static void HostIconFreeDiskObject(M68kCpuState state)
        {
            state.D[0] = 0;
        }

        private void HostIconFindToolType(M68kCpuState state)
        {
            LogIconCall(-96);
            var toolTypesAddress = state.A[0] != 0 ? state.A[0] : state.D[0];
            var key = ReadNullTerminatedString(state.A[1] != 0 ? state.A[1] : state.D[1], 64);
            state.D[0] = FindToolTypeValue(toolTypesAddress, key);
        }

        private void HostIconMatchToolValue(M68kCpuState state)
        {
            LogIconCall(-102);
            var value = ReadNullTerminatedString(state.A[0] != 0 ? state.A[0] : state.D[0], 64);
            var expected = ReadNullTerminatedString(state.A[1] != 0 ? state.A[1] : state.D[1], 64);
            state.D[0] = value.Equals(expected, StringComparison.OrdinalIgnoreCase) ? 1u : 0u;
        }

        private void HostIconGeneric(M68kCpuState state, int displacement)
        {
            LogIconCall(displacement);
            state.D[0] = EnsureWorkbenchDiskObject();
        }

        private void HostExecGeneric(M68kCpuState state, int displacement)
        {
            LogExecCall(displacement);
            state.D[0] = displacement switch
            {
                -294 => EnsureSyntheticTask(),
                -306 => HostExecSetSignal(state),
                -312 => 0,
                -318 => HostExecWait(state),
                -324 => HostExecSignal(state),
                -330 => HostExecAllocSignal(state),
                -336 => HostExecFreeSignal(state),
                -150 => HostExecSuperState(state),
                -156 => HostExecUserState(state),
                -342 => HostExecAllocTrap(state),
                -348 => HostExecFreeTrap(state),
                -354 => 0,
                -360 => 0,
                -366 => 0,
                -372 => HostExecGetMsg(),
                -378 => 0,
                -384 => EnsureSyntheticMessage(),
                -390 => 0,
                _ => 0
            };
        }

        private static uint HostExecSuperState(M68kCpuState state)
        {
            return state.EnterSupervisorModeWithUserStack();
        }

        private static uint HostExecUserState(M68kCpuState state)
        {
            state.ReturnToUserModeWithUserStack(state.D[0]);
            return 0;
        }

        private void HostTaskTrapDispatcher(M68kCpuState state, int vector)
        {
            var task = GetCurrentTaskAddress();
            var trapCode = task != 0 ? _machine.Bus.ReadLong(task + TaskTrapCodeOffset) : 0;
            if (trapCode == 0)
            {
                trapCode = _machine.Bus.ReadLong(AmigaKickstartHost.ExecLibraryBase + ExecTaskTrapCodeOffset);
            }

            if (trapCode == 0)
            {
                trapCode = DefaultTaskTrapCodeAddress;
            }

            state.SetActiveStackPointer(state.A[7] - 4);
            _machine.Bus.WriteLong(state.A[7], (uint)vector);
            state.ProgramCounter = trapCode;
        }

        private void HostDefaultTaskTrapCode(M68kCpuState state)
        {
            var frameAddress = state.A[7] + 4;
            var statusRegister = _machine.Bus.ReadWord(frameAddress);
            var programCounter = _machine.Bus.ReadLong(frameAddress + 2);
            state.SetActiveStackPointer(frameAddress + 6);
            state.StatusRegister = statusRegister;
            state.ProgramCounter = programCounter;
        }

        private void HostGraphicsGeneric(M68kCpuState state, int displacement)
        {
            LogUiCall("graphics.library", displacement);
            switch (displacement)
            {
                case -0xD2: // MrgCop
                    state.D[0] = HostGraphicsMrgCop(state);
                    return;
                case -0xD8: // MakeVPort
                    state.D[0] = HostGraphicsMakeVPort(state);
                    return;
                case -0xDE: // LoadView
                    HostGraphicsLoadView(state);
                    state.D[0] = 0;
                    return;
                case -0x168: // InitView
                    HostGraphicsInitView(state);
                    state.D[0] = 0;
                    return;
                case -0xCC: // InitVPort
                    HostGraphicsInitVPort(state);
                    state.D[0] = 0;
                    return;
                default:
                    state.D[0] = EnsureSyntheticHostObject();
                    return;
            }
        }

        private void HostIntuitionGeneric(M68kCpuState state, int displacement)
        {
            LogUiCall("intuition.library", displacement);
            switch (displacement)
            {
                case -198:
                    state.D[0] = EnsureSyntheticScreen();
                    return;
                case -204:
                    state.D[0] = EnsureSyntheticWindow();
                    return;
                case -378: // MakeScreen
                case -384: // RemakeDisplay
                case -390: // RethinkDisplay
                    state.D[0] = HostRethinkDisplay(state.Cycles);
                    return;
                case -294: // ViewAddress
                    state.D[0] = _currentViewAddress != 0 ? _currentViewAddress : EnsureSyntheticView();
                    return;
                case -300: // ViewPortAddress(Window)
                    state.D[0] = GetSyntheticScreenViewPortAddress();
                    return;
                default:
                    state.D[0] = EnsureSyntheticHostObject();
                    return;
            }
        }

        private void HostExpansionGeneric(M68kCpuState state, int displacement)
        {
            LogUiCall("expansion.library", displacement);
            state.D[0] = EnsureSyntheticHostObject();
        }

        private void LogUiCall(string libraryName, int displacement)
        {
            if (_uiDiagnosticCount >= 16)
            {
                return;
            }

            _diagnostics.Add(new AmigaBootDiagnostic("AMIGA_BOOT_UI_CALL", $"{libraryName} LVO {displacement}."));
            _uiDiagnosticCount++;
        }

        private void LogExecCall(int displacement)
        {
            if (_execDiagnosticCount >= 16)
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

        private void HostGraphicsLoadView(M68kCpuState state)
        {
            _currentViewAddress = state.A[1];
            if (_currentViewAddress == 0)
            {
                return;
            }

            _ = TryPublishCopperListFromView(_currentViewAddress, state.Cycles);
        }

        private uint HostGraphicsMakeVPort(M68kCpuState state)
        {
            var view = state.A[0];
            var viewPort = state.A[1];
            if (view == 0 || viewPort == 0 || !TryBuildViewPortCopperList(view, viewPort, out _))
            {
                return 0;
            }

            return 0;
        }

        private uint HostGraphicsMrgCop(M68kCpuState state)
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

        private void HostGraphicsInitView(M68kCpuState state)
        {
            var view = state.A[1];
            if (view == 0 || !_machine.Bus.IsMappedMemoryRange(view, ViewStructSize))
            {
                return;
            }

            _machine.Bus.ClearMemory(view, ViewStructSize);
        }

        private void HostGraphicsInitVPort(M68kCpuState state)
        {
            var viewPort = state.A[0];
            if (viewPort == 0 || !_machine.Bus.IsMappedMemoryRange(viewPort, 0x28))
            {
                return;
            }

            _machine.Bus.ClearMemory(viewPort, 0x28);
            InitializeSyntheticViewPort(viewPort);
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
                depth = Math.Clamp(_machine.Bus.ReadByte(bitMap + BitMapDepthOffset), 1, planes.Length);
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

            copperList = AllocateProgramMemory(0x100);
            var offset = copperList;
            WriteCopperMove(ref offset, 0x08E, EncodeDiwStart(dx, dy));
            WriteCopperMove(ref offset, 0x090, EncodeDiwStop(dx, dy, width, height));
            var fetchWords = Math.Clamp((width + 15) / 16, 1, 64);
            WriteCopperMove(ref offset, 0x092, 0x0038);
            WriteCopperMove(ref offset, 0x094, (ushort)(0x0038 + ((fetchWords - 1) * 8)));
            var modulo = (short)(bytesPerRow - (fetchWords * 2));
            WriteCopperMove(ref offset, 0x108, unchecked((ushort)modulo));
            WriteCopperMove(ref offset, 0x10A, unchecked((ushort)modulo));
            WriteCopperMove(ref offset, 0x100, (ushort)((depth << 12) | (modes & ViewModeInterlace)));
            for (var plane = 0; plane < depth; plane++)
            {
                var register = (ushort)(0x0E0 + (plane * 4));
                WriteCopperMove(ref offset, register, (ushort)(planes[plane] >> 16));
                WriteCopperMove(ref offset, (ushort)(register + 2), (ushort)planes[plane]);
            }

            WriteCopperMove(ref offset, 0x180, 0x0000);
            WriteCopperMove(ref offset, 0x182, 0x0FFF);
            _machine.Bus.WriteWord(offset, 0xFFFF);
            _machine.Bus.WriteWord(offset + 2, 0xFFFE);
            return true;
        }

        private bool TryPublishCopperListFromView(uint view, long cycle)
        {
            if (view == 0)
            {
                return false;
            }

            if (TryResolveCopperListStartFromView(view, out var copperList))
            {
                LoadCopperList(copperList, cycle);
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
            _machine.Bus.WriteWord(0x00DFF080, (ushort)(copperList >> 16), cycle);
            _machine.Bus.WriteWord(0x00DFF082, (ushort)copperList, cycle);
            _machine.Bus.WriteWord(0x00DFF088, 0, cycle);
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

            _machine.Bus.WriteLong(_syntheticScreenAddress + ScreenFirstWindowOffset, EnsureSyntheticWindow());
            InitializeSyntheticViewPort(GetSyntheticScreenViewPortAddress());
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
            _machine.Bus.WriteWord(viewPort + ViewPortDWidthOffset, (ushort)AmigaConstants.PalLowResWidth);
            _machine.Bus.WriteWord(viewPort + ViewPortDHeightOffset, 256);
            _machine.Bus.WriteWord(viewPort + ViewPortDxOffsetOffset, 0);
            _machine.Bus.WriteWord(viewPort + ViewPortDyOffsetOffset, 0);
            _machine.Bus.WriteWord(viewPort + ViewPortModesOffset, 0);
        }

        private uint EnsureSyntheticWindow()
        {
            if (_syntheticWindowAddress != 0)
            {
                return _syntheticWindowAddress;
            }

            _syntheticWindowAddress = AllocateProgramMemory(0x100);
            if (_syntheticWindowAddress != 0)
            {
                var syntheticPort = EnsureSyntheticHostObject();
                _machine.Bus.WriteLong(_syntheticWindowAddress + 0x56, syntheticPort);
            }

            return _syntheticWindowAddress != 0 ? _syntheticWindowAddress : EnsureSyntheticHostObject();
        }

        private uint HostExecWait(M68kCpuState state)
        {
            _pendingSyntheticMessages = Math.Max(_pendingSyntheticMessages, 1);
            var requested = state.D[0] != 0 ? state.D[0] : 1u;
            var delivered = _syntheticSignalMask & requested;
            if (delivered == 0)
            {
                delivered = requested;
            }

            _syntheticSignalMask &= ~delivered;
            return delivered;
        }

        private uint HostExecSetSignal(M68kCpuState state)
        {
            var oldSignals = _syntheticSignalMask;
            _syntheticSignalMask = (_syntheticSignalMask & ~state.D[1]) | (state.D[0] & state.D[1]);
            return oldSignals;
        }

        private uint HostExecSignal(M68kCpuState state)
        {
            _syntheticSignalMask |= state.D[0];
            return 0;
        }

        private uint HostExecAllocSignal(M68kCpuState state)
        {
            var requested = unchecked((int)state.D[0]);
            if (requested >= 0 && requested < 32)
            {
                var requestedMask = 1u << requested;
                if ((_allocatedSignalMask & requestedMask) == 0)
                {
                    _allocatedSignalMask |= requestedMask;
                    _nextAllocatedSignalBit = Math.Max(_nextAllocatedSignalBit, requested + 1);
                    return (uint)requested;
                }

                return 0xFFFF_FFFF;
            }

            for (var offset = 0; offset < 32; offset++)
            {
                var bit = (_nextAllocatedSignalBit + offset) & 31;
                var mask = 1u << bit;
                if ((_allocatedSignalMask & mask) != 0)
                {
                    continue;
                }

                _allocatedSignalMask |= mask;
                _nextAllocatedSignalBit = (bit + 1) & 31;
                return (uint)bit;
            }

            return 0xFFFF_FFFF;
        }

        private uint HostExecFreeSignal(M68kCpuState state)
        {
            var bit = unchecked((int)state.D[0]);
            if (bit is >= 0 and < 32)
            {
                var mask = 1u << bit;
                _allocatedSignalMask &= ~mask;
                _syntheticSignalMask &= ~mask;
                _nextAllocatedSignalBit = Math.Min(_nextAllocatedSignalBit, bit);
            }

            return 0;
        }

        private uint HostExecAllocTrap(M68kCpuState state)
        {
            var task = GetCurrentTaskAddress();
            var requested = unchecked((int)state.D[0]);
            var allocated = _machine.Bus.ReadWord(task + TaskTrapAllocOffset);
            if (requested is >= 0 and < 16)
            {
                return TryAllocateTrap(task, allocated, requested);
            }

            if (requested != -1)
            {
                return 0xFFFF_FFFF;
            }

            for (var trap = 0; trap < 16; trap++)
            {
                if ((allocated & (1 << trap)) == 0)
                {
                    return TryAllocateTrap(task, allocated, trap);
                }
            }

            return 0xFFFF_FFFF;
        }

        private uint TryAllocateTrap(uint task, ushort allocated, int trap)
        {
            var mask = 1 << trap;
            if ((allocated & mask) != 0)
            {
                return 0xFFFF_FFFF;
            }

            _machine.Bus.WriteWord(task + TaskTrapAllocOffset, (ushort)(allocated | mask));
            _machine.Bus.WriteWord(task + TaskTrapAbleOffset, (ushort)(_machine.Bus.ReadWord(task + TaskTrapAbleOffset) | mask));
            return (uint)trap;
        }

        private uint HostExecFreeTrap(M68kCpuState state)
        {
            var trap = unchecked((int)state.D[0]);
            if (trap is < 0 or >= 16)
            {
                return 0;
            }

            var task = GetCurrentTaskAddress();
            var mask = (ushort)~(1 << trap);
            _machine.Bus.WriteWord(task + TaskTrapAllocOffset, (ushort)(_machine.Bus.ReadWord(task + TaskTrapAllocOffset) & mask));
            _machine.Bus.WriteWord(task + TaskTrapAbleOffset, (ushort)(_machine.Bus.ReadWord(task + TaskTrapAbleOffset) & mask));
            return 0;
        }

        private uint HostExecGetMsg()
        {
            if (_pendingSyntheticMessages <= 0)
            {
                return 0;
            }

            _pendingSyntheticMessages--;
            return EnsureSyntheticMessage();
        }

        private uint EnsureSyntheticTask()
        {
            return GetCurrentTaskAddress();
        }

        private uint GetCurrentTaskAddress()
        {
            var task = _machine.Bus.ReadLong(AmigaKickstartHost.ExecLibraryBase + ExecThisTaskOffset);
            if (task != 0)
            {
                return task;
            }

            return _currentTaskAddress != 0 ? _currentTaskAddress : AmigaKickstartHost.ExecStructAddress;
        }

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

            _machine.Bus.WriteLong(_syntheticMessageAddress + 0x14, 0x0000_0060);
            _machine.Bus.WriteWord(_syntheticMessageAddress + 0x18, 0x000D);
            _machine.Bus.WriteLong(_syntheticMessageAddress + 0x1C, _syntheticMessageAddress);
            _machine.Bus.WriteWord(_syntheticMessageAddress + 0x26, 0x0001);
            return _syntheticMessageAddress;
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

        private void HostDosClose(M68kCpuState state)
        {
            _dosHandles.Remove(state.D[1]);
            state.D[0] = 0;
        }

        private void HostDosRead(M68kCpuState state)
        {
            if (!_dosHandles.TryGetValue(state.D[1], out var handle))
            {
                state.D[0] = 0xFFFF_FFFF;
                return;
            }

            var requested = (int)Math.Min(state.D[3], int.MaxValue);
            var available = Math.Max(0, handle.Data.Length - handle.Position);
            var count = Math.Min(requested, available);
            if (count > 0)
            {
                _machine.Bus.CopyToMemory(state.D[2], handle.Data.AsSpan(handle.Position, count));
                handle.Position += count;
            }

            if (_dosReadDiagnosticCount < 24)
            {
                _diagnostics.Add(new AmigaBootDiagnostic(
                    "AMIGA_BOOT_DOS_READ",
                    $"Read {count}/0x{requested:X} bytes from '{handle.Path}' into 0x{state.D[2]:X8}."));
                _dosReadDiagnosticCount++;
            }

            state.D[0] = (uint)count;
        }

        private void HostDosSeek(M68kCpuState state)
        {
            if (!_dosHandles.TryGetValue(state.D[1], out var handle))
            {
                state.D[0] = 0xFFFF_FFFF;
                return;
            }

            var oldPosition = handle.Position;
            var offset = unchecked((int)state.D[2]);
            var mode = unchecked((int)state.D[3]);
            var origin = mode switch
            {
                1 => handle.Data.Length,
                -1 => 0,
                _ => handle.Position
            };
            handle.Position = Math.Clamp(origin + offset, 0, handle.Data.Length);
            state.D[0] = (uint)oldPosition;
        }

        private void HostFindResident(M68kCpuState state)
        {
            var name = ReadNullTerminatedString(state.A[1], 96);
            if (_execDiagnosticCount < 16)
            {
                _diagnostics.Add(new AmigaBootDiagnostic("AMIGA_BOOT_FIND_RESIDENT", $"FindResident requested '{name}'."));
                _execDiagnosticCount++;
            }

            if (name.IndexOf("dos", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                EnsureDosResident();
                state.D[0] = DosResidentAddress;
                return;
            }

            state.D[0] = 0;
        }

        private static void HostFindName(M68kCpuState state)
        {
            state.D[0] = 0;
        }

        private static void HostInitResident(M68kCpuState state)
        {
            state.D[0] = AmigaKickstartHost.DosLibraryBase;
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
            var end = line.IndexOfAny(new[] { ' ', '\t' });
            return end < 0 ? line : line[..end];
        }

        public bool TryLaunchProgram(
            AmigaProgramLaunchRequest request,
            out AmigaProgramLaunchResult result,
            out string message)
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
            _machine.Cpu.BeginSubroutine(program.EntryAddress, GetProgramStackTopAddress(), DosProgramReturnAddress);
            _machine.Cpu.State.D[0] = (uint)startupArguments.Length;
            _machine.Cpu.State.A[0] = startupAddress;
            _machine.Cpu.State.A[6] = AmigaKickstartHost.ExecLibraryBase;
            EnableWorkbenchProgramInterrupts();
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

        private void EnableWorkbenchProgramInterrupts()
        {
            var cycle = _machine.Cpu.State.Cycles;
            _machine.Bus.WriteWord(0x00DFF09A, (ushort)(0x8000 | 0x4000 | AmigaConstants.IntreqVerticalBlank), cycle);
            _machine.Bus.Paula.AdvanceTo(cycle);
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
            var fileSystem = EnsureDosFileSystem();
            if (fileSystem.TryReadFile(path, out data))
            {
                return true;
            }

            if (_workbenchCurrentDirectory.Length != 0 &&
                path.IndexOf(':') < 0 &&
                path.IndexOf('/') < 0 &&
                path.IndexOf('\\') < 0)
            {
                return fileSystem.TryReadFile(
                    AmigaDosFileSystem.CombinePath(_workbenchCurrentDirectory, path),
                    out data);
            }

            data = Array.Empty<byte>();
            return false;
        }

        private bool TryFindDosEntry(string path, out AmigaDosDirectoryEntry entry)
        {
            var fileSystem = EnsureDosFileSystem();
            if (fileSystem.TryFindEntry(path, out entry))
            {
                return true;
            }

            if (_workbenchCurrentDirectory.Length != 0 &&
                path.IndexOf(':') < 0 &&
                path.IndexOf('/') < 0 &&
                path.IndexOf('\\') < 0)
            {
                return fileSystem.TryFindEntry(
                    AmigaDosFileSystem.CombinePath(_workbenchCurrentDirectory, path),
                    out entry);
            }

            entry = default;
            return false;
        }

        private AmigaDosFileSystem EnsureDosFileSystem()
        {
            if (_dosFileSystem != null)
            {
                return _dosFileSystem;
            }

            if (Drive0.Disk == null)
            {
                throw new AmigaEmulationException("No disk is inserted in DF0:.");
            }

            _dosFileSystem = new AmigaDosFileSystem(Drive0.Disk);
            return _dosFileSystem;
        }

        private void InstallKickstartMemoryList()
        {
            var hasPseudoFast = _machine.Bus.ExpansionRam.Length != 0;
            var listAddress = AmigaKickstartHost.ExecLibraryBase + ExecMemListOffset;
            if (hasPseudoFast)
            {
                var metadataBase = _machine.Bus.ExpansionRamBase;
                _fastMemHeaderAddress = metadataBase;
                _chipMemHeaderAddress = metadataBase + 0x40;
                _fastMemNameAddress = metadataBase + 0x80;
                _chipMemNameAddress = metadataBase + 0x90;
                _currentTaskAddress = metadataBase + BootPseudoFastCurrentTaskOffset;
                _fastMemLower = metadataBase + BootPseudoFastMetadataSize;
                _fastMemUpper = metadataBase + (uint)_machine.Bus.ExpansionRam.Length - BootPseudoFastStackReserve;
                _chipMemLower = BootChipPublicLowerAddress;
                _chipMemUpper = (uint)_machine.Bus.ChipRam.Length;
            }
            else
            {
                var privateBase = GetChipOnlyPrivateMetadataBase();
                _currentTaskAddress = privateBase;
                _chipMemHeaderAddress = privateBase + BootChipOnlyMemHeaderOffset;
                _chipMemNameAddress = privateBase + BootChipOnlyMemNameOffset;
                _fastMemHeaderAddress = 0;
                _fastMemNameAddress = 0;
                _fastMemLower = 0;
                _fastMemUpper = 0;
                _chipMemLower = BootChipPublicLowerAddress;
                _chipMemUpper = privateBase;
            }

            var execImage = new byte[ExecBaseImageSize];
            var firstHeader = hasPseudoFast ? _fastMemHeaderAddress : _chipMemHeaderAddress;
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

            if (hasPseudoFast)
            {
                WriteInitialMemoryHeader(
                    _fastMemHeaderAddress,
                    _chipMemHeaderAddress,
                    listAddress,
                    MemfPublic | MemfFast,
                    _fastMemLower,
                    _fastMemUpper,
                    _fastMemNameAddress,
                    "pseudo-fast");

                WriteInitialMemoryHeader(
                    _chipMemHeaderAddress,
                    listAddress + 4,
                    _fastMemHeaderAddress,
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
        }

        private void WriteExecBaseStaticFields(Span<byte> execImage)
        {
            var maxLocalMemory = AlignDown((uint)_machine.Bus.ChipRam.Length, 4);
            var maxExtendedMemory = _machine.Bus.ExpansionRam.Length != 0
                ? AlignDown(_machine.Bus.ExpansionRamBase + (uint)_machine.Bus.ExpansionRam.Length, 4)
                : 0;

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

        private uint AllocateMemoryFromMemList(int byteCount, uint flags)
        {
            if (!_memoryListInstalled || byteCount <= 0)
            {
                return 0;
            }

            var size = Align((uint)byteCount, 8);
            foreach (var headerAddress in EnumerateCompatibleMemoryHeaders(flags))
            {
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
                        if (chunkBytes - size < 8)
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
            var listAddress = AmigaKickstartHost.ExecLibraryBase + ExecMemListOffset;
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
            var listAddress = AmigaKickstartHost.ExecLibraryBase + ExecMemListOffset;
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

        private uint AllocateProgramMemory(int byteCount)
        {
            var flags = _machine.Bus.ExpansionRam.Length != 0
                ? MemfPublic | MemfFast
                : MemfPublic;
            var address = AllocateMemoryFromMemList(Math.Max(4, byteCount), flags);
            if (address == 0)
            {
                throw new AmigaEmulationException("The boot program does not fit in the available emulated memory.");
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
            foreach (var candidate in new[] { value, value << 2 })
            {
                var bstr = ReadBstr(candidate, 255);
                if (!string.IsNullOrWhiteSpace(bstr))
                {
                    return bstr;
                }

                var cstr = ReadNullTerminatedString(candidate, 255);
                if (!string.IsNullOrWhiteSpace(cstr))
                {
                    return cstr;
                }
            }

            return string.Empty;
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

        private sealed class BootDosHandle
        {
            public BootDosHandle(string path, byte[] data, bool isConsole = false)
            {
                Path = path;
                Data = data;
                IsConsole = isConsole;
            }

            public string Path { get; }

            public byte[] Data { get; }

            public bool IsConsole { get; }

            public int Position { get; set; }
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
