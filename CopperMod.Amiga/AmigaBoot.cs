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
        private const uint DosResidentAddress = 0x0000_3400;
        private const uint DosResidentNameAddress = DosResidentAddress + 0x40;
        private const uint DosResidentIdAddress = DosResidentAddress + 0x50;
        private const uint DosResidentInitAddress = 0x00F2_0100;
        private const uint WorkbenchRootLock = 0x00F8_0000;
        private const uint ChipOnlyMemHeaderAddress = 0x0000_2400;
        private const uint ChipOnlyMemNameAddress = 0x0000_2480;
        private const int ExecBaseImageSize = 0x180;
        private const int ExecMemListOffset = 0x142;
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
        private const uint MemfLargest = 0x0002_0000;
        private const uint MemfTotal = 0x0008_0000;
        private const uint BootChipOnlyReservedLower = 0x0000_2800;
        private const uint BootPseudoFastMetadataSize = 0x0000_0100;
        private const uint BootPseudoFastStackReserve = 0x0000_1000;
        private const uint BootChipKillSysLower = 0x0000_0100;

        private readonly AmigaMachine _machine;
        private readonly IAmigaDiskDmaEngine _diskDma;
        private readonly List<AmigaBootDiagnostic> _diagnostics = new List<AmigaBootDiagnostic>();
        private readonly Dictionary<uint, BootDosHandle> _dosHandles = new Dictionary<uint, BootDosHandle>();
        private readonly Dictionary<uint, AmigaDosDirectoryEntry> _dosLocks = new Dictionary<uint, AmigaDosDirectoryEntry>();
        private bool _bootDiskReadCompleted;
        private bool _knownProtectionGateInstalled;
        private bool _dosBootContinuationStarted;
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
        private uint _workbenchDiskObjectAddress;
        private uint _syntheticScreenAddress;
        private uint _syntheticWindowAddress;
        private uint _syntheticMessageAddress;
        private uint _syntheticHostObjectAddress;
        private int _pendingSyntheticMessages;
        private uint _nextDosHandle;
        private uint _nextDosLock;
        private uint _lastDosError;
        private uint _chipMemHeaderAddress;
        private uint _fastMemHeaderAddress;
        private uint _chipMemNameAddress;
        private uint _fastMemNameAddress;
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
        }

        public AmigaFloppyDrive Drive0 => _machine.Bus.Disk.Drive0;

        public IReadOnlyList<AmigaBootDiagnostic> Diagnostics => _diagnostics;

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
            _diagnostics.Clear();
            _dosHandles.Clear();
            _dosLocks.Clear();
            _bootDiskReadCompleted = false;
            _knownProtectionGateInstalled = false;
            _dosBootContinuationStarted = false;
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
            _workbenchDiskObjectAddress = 0;
            _syntheticScreenAddress = 0;
            _syntheticWindowAddress = 0;
            _syntheticMessageAddress = 0;
            _syntheticHostObjectAddress = 0;
            _pendingSyntheticMessages = 0;
            _nextDosHandle = 0x0000_5000;
            _nextDosLock = 0x0000_7000;
            _lastDosError = 0;
            _chipMemHeaderAddress = 0;
            _fastMemHeaderAddress = 0;
            _chipMemNameAddress = 0;
            _fastMemNameAddress = 0;
            _chipMemLower = 0;
            _chipMemUpper = 0;
            _fastMemLower = 0;
            _fastMemUpper = 0;
            _memoryListInstalled = false;
            _dosFileSystem = null;
            Drive0.Insert(disk);
            _machine.ResetHardware();
            InstallBootHostTraps();
            ValidateBootBlock(disk.BootBlock);
            _machine.Bus.CopyToChipRam(BootBlockAddress, disk.BootBlock);
            _machine.Cpu.Reset(BootEntryAddress, GetBootStackTopAddress());
            _machine.Cpu.State.A[1] = BootIoRequestAddress;
            _machine.Cpu.State.A[6] = AmigaKickstartHost.ExecLibraryBase;
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
            _machine.Kickstart.Install(
                bus,
                new AmigaKickstartTrapTable(
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
                    HostDosSeek));
            for (var displacement = -6; displacement >= -600; displacement -= 6)
            {
                var captured = displacement;
                bus.RegisterHostCallback(Lvo(AmigaKickstartHost.ExecLibraryBase, captured), state => HostExecGeneric(state, captured));
            }

            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.ExecLibraryBase, -456), HostDoIo);
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.ExecLibraryBase, -96), HostFindResident);
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.ExecLibraryBase, -132), HostOk);
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.ExecLibraryBase, -138), HostOk);
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.ExecLibraryBase, -276), HostFindName);
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.ExecLibraryBase, -198), HostAllocMem);
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

        private AmigaBootResult ExecuteBootBlock(
            int maxInstructions,
            AmigaBootRunMode runMode,
            long? targetCycle = null,
            bool reportOverrun = true,
            Action<long, long>? beforeDeviceAdvance = null)
        {
            var instructions = 0;
            var completed = false;
            try
            {
                while (!_machine.Cpu.State.Halted &&
                    instructions < maxInstructions &&
                    (!targetCycle.HasValue || _machine.Cpu.State.Cycles < targetCycle.Value))
                {
                    if (_machine.Cpu.State.ProgramCounter == 0 && instructions > 0)
                    {
                        if (TryStartDosBootContinuation())
                        {
                            continue;
                        }

                        completed = true;
                        break;
                    }

                    if (_machine.Cpu.State.ProgramCounter == 0x0000_0400 && instructions > 0)
                    {
                        completed = true;
                        break;
                    }

                    var previousCycle = _machine.Cpu.State.Cycles;
                    _machine.Cpu.ExecuteInstruction();
                    beforeDeviceAdvance?.Invoke(previousCycle, _machine.Cpu.State.Cycles);
                    _machine.Bus.AdvanceRasterTo(_machine.Cpu.State.Cycles);
                    _machine.Bus.AdvanceCiasTo(_machine.Cpu.State.Cycles);
                    _machine.Bus.Paula.AdvanceTo(_machine.Cpu.State.Cycles);
                    _machine.DispatchPendingHardwareInterrupt();
                    instructions++;
                    if (_bootDiskReadCompleted && runMode == AmigaBootRunMode.StopAfterBootDiskRead)
                    {
                        completed = true;
                        break;
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
                completed,
                _diagnostics.ToArray());
        }

        private void HostDoIo(M68kCpuState state)
        {
            var io = state.A[1];
            var command = _machine.Bus.ReadWord(io + 0x1C);
            var length = _machine.Bus.ReadLong(io + 0x24);
            var destination = _machine.Bus.ReadLong(io + 0x28);
            var offset = _machine.Bus.ReadLong(io + 0x2C);
            if (command != CmdRead)
            {
                _diagnostics.Add(new AmigaBootDiagnostic("AMIGA_BOOT_UNSUPPORTED_IO", $"Unsupported boot IO command {command}."));
                state.D[0] = 1;
                return;
            }

            ReadBootDiskBytesToChipRam(checked((int)offset), checked((int)length), destination, state.Cycles);
            _bootDiskReadCompleted = true;
            TryInstallKnownBootLoaderTraps();
            state.D[0] = 0;
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

        private void TryInstallKnownBootLoaderTraps()
        {
            if (_knownProtectionGateInstalled || Drive0.Disk == null)
            {
                return;
            }

            if (!IsFullContactDiskOneBootBlock(Drive0.Disk.BootBlock))
            {
                return;
            }

            _machine.Bus.RegisterHostCallback(0x0007_B000, HostKnownProtectionGate);
            _knownProtectionGateInstalled = true;
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

            if (string.IsNullOrWhiteSpace(path) || !EnsureDosFileSystem().TryReadFile(path, out var data))
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
            if (string.IsNullOrWhiteSpace(path) || !EnsureDosFileSystem().TryFindEntry(path, out var entry))
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
                -342 => 0xFFFF_FFFF,
                -348 => 0,
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

        private void HostGraphicsGeneric(M68kCpuState state, int displacement)
        {
            LogUiCall("graphics.library", displacement);
            state.D[0] = EnsureSyntheticHostObject();
        }

        private void HostIntuitionGeneric(M68kCpuState state, int displacement)
        {
            LogUiCall("intuition.library", displacement);
            state.D[0] = displacement switch
            {
                -198 => EnsureSyntheticScreen(),
                -204 => EnsureSyntheticWindow(),
                _ => EnsureSyntheticHostObject()
            };
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

        private uint EnsureWorkbenchDiskObject()
        {
            if (_workbenchDiskObjectAddress != 0)
            {
                return _workbenchDiskObjectAddress;
            }

            var defaultToolAddress = WriteProgramString("C/SystemTakeover");
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
            _machine.Bus.WriteLong(_workbenchDiskObjectAddress + 0x4C, 4096);
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

            _machine.Bus.WriteLong(_syntheticScreenAddress + 0x2C, EnsureSyntheticWindow());
            return _syntheticScreenAddress;
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
            return EnsureSyntheticHostObject();
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
            _ = state;
            _diagnostics.Add(new AmigaBootDiagnostic("AMIGA_BOOT_NULL_HOST_CALLBACK", "Boot program called a null host callback; treating it as a no-op."));
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
            var fileSystem = EnsureDosFileSystem();
            if (!fileSystem.TryResolveWorkbenchDefaultTool(out var projectPath, out var toolPath, out var toolTypes) ||
                !fileSystem.TryReadFile(toolPath, out var executable))
            {
                return false;
            }

            _workbenchToolTypes = NormalizeToolTypes(toolTypes);
            var loader = new AmigaHunkProgramLoader(_machine.Bus, AllocateProgramMemory);
            var program = loader.Load(executable);
            var startupArguments = BuildCliArguments(_workbenchToolTypes);
            var startupAddress = WriteProgramString(startupArguments);
            _machine.Cpu.BeginSubroutine(program.EntryAddress, GetProgramStackTopAddress(), 0x0000_0400);
            _machine.Cpu.State.D[0] = (uint)startupArguments.Length;
            _machine.Cpu.State.A[0] = startupAddress;
            _machine.Cpu.State.A[6] = AmigaKickstartHost.ExecLibraryBase;
            _diagnostics.Add(new AmigaBootDiagnostic(
                "AMIGA_BOOT_DOS_AUTOSTART",
                $"Started Workbench default tool {toolPath}."));
            return true;
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

                var key = toolType.Substring(0, separator).Trim();
                while (key.Length > 0 && !char.IsLetter(key[0]))
                {
                    key = key.Substring(1);
                }

                if (key.Length == 0)
                {
                    continue;
                }

                normalized.Add(key + "=" + toolType.Substring(separator + 1));
            }

            return normalized;
        }

        private static string BuildCliArguments(IEnumerable<string> toolTypes)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var toolType in toolTypes)
            {
                var separator = toolType.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var key = toolType.Substring(0, separator).Trim();
                while (key.Length > 0 && !char.IsLetter(key[0]))
                {
                    key = key.Substring(1);
                }

                if (key.Length != 0)
                {
                    values[key] = toolType.Substring(separator + 1);
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

        private static bool IsTruthyToolTypeValue(string value)
        {
            return value.Trim().Equals("YES", StringComparison.OrdinalIgnoreCase) ||
                value.Trim().Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
                value.Trim() == "1";
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
                _fastMemLower = metadataBase + BootPseudoFastMetadataSize;
                _fastMemUpper = metadataBase + (uint)_machine.Bus.ExpansionRam.Length - BootPseudoFastStackReserve;
                _chipMemLower = BootChipKillSysLower;
                _chipMemUpper = (uint)_machine.Bus.ChipRam.Length;
            }
            else
            {
                _chipMemHeaderAddress = ChipOnlyMemHeaderAddress;
                _chipMemNameAddress = ChipOnlyMemNameAddress;
                _fastMemHeaderAddress = 0;
                _fastMemNameAddress = 0;
                _fastMemLower = 0;
                _fastMemUpper = 0;
                _chipMemLower = BootChipOnlyReservedLower;
                _chipMemUpper = (uint)Math.Max(0, _machine.Bus.ChipRam.Length - BootPseudoFastStackReserve);
            }

            var execImage = new byte[ExecBaseImageSize];
            var firstHeader = hasPseudoFast ? _fastMemHeaderAddress : _chipMemHeaderAddress;
            var lastHeader = _chipMemHeaderAddress;
            BigEndian.WriteUInt32(execImage, ExecMemListOffset, firstHeader);
            BigEndian.WriteUInt32(execImage, ExecMemListOffset + 4, 0);
            BigEndian.WriteUInt32(execImage, ExecMemListOffset + 8, lastHeader);
            execImage[ExecMemListOffset + 12] = 0;
            execImage[ExecMemListOffset + 13] = 0;
            _machine.Bus.MapReadOnlyMemory(AmigaKickstartHost.ExecLibraryBase, execImage);

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
                            allocatedAddress = chunkAddress + chunkBytes - size;
                            allocatedBytes = size;
                            _machine.Bus.WriteLong(chunkAddress + MemChunkBytesOffset, chunkBytes - size);
                        }

                        var freeBytes = _machine.Bus.ReadLong(headerAddress + MemHeaderFreeOffset);
                        _machine.Bus.WriteLong(headerAddress + MemHeaderFreeOffset, freeBytes >= allocatedBytes ? freeBytes - allocatedBytes : 0);
                        _machine.Bus.ClearMemory(allocatedAddress, checked((int)allocatedBytes));
                        return allocatedAddress;
                    }

                    previousLinkAddress = chunkAddress + MemChunkNextOffset;
                    chunkAddress = nextChunkAddress;
                }
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

        private void HostKnownProtectionGate(M68kCpuState state)
        {
            _diagnostics.Add(new AmigaBootDiagnostic(
                "AMIGA_BOOT_PROTECTED_DISK_UNSUPPORTED",
                "The boot program entered a known Copylock-style protected loader at 0x0007B000. Standard ADF images do not carry the raw protection data needed to decode this path."));
            state.Halted = true;
            state.D[0] = 1;
        }

        private static uint Lvo(uint baseAddress, int displacement)
        {
            return unchecked((uint)((int)baseAddress + displacement));
        }

        private uint GetBootStackTopAddress()
        {
            return AlignDown((uint)_machine.Bus.ChipRam.Length, 4) - 4;
        }

        private uint GetProgramStackTopAddress()
        {
            if (_machine.Bus.ExpansionRam.Length != 0)
            {
                return AlignDown(_machine.Bus.ExpansionRamBase + (uint)_machine.Bus.ExpansionRam.Length, 4) - 4;
            }

            return GetBootStackTopAddress();
        }

        private static uint Align(uint value, uint alignment)
        {
            return (value + alignment - 1) & ~(alignment - 1);
        }

        private static uint AlignDown(uint value, uint alignment)
        {
            return value & ~(alignment - 1);
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
