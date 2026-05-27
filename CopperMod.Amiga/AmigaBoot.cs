using System;
using System.Collections.Generic;

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
        public const uint StackTopAddress = 0x0007_FFFC;
        public const int CmdRead = 2;

        private readonly AmigaMachine _machine;
        private readonly IAmigaDiskDmaEngine _diskDma;
        private readonly List<AmigaBootDiagnostic> _diagnostics = new List<AmigaBootDiagnostic>();
        private bool _bootDiskReadCompleted;
        private bool _knownProtectionGateInstalled;

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
            ArgumentNullException.ThrowIfNull(disk);
            _diagnostics.Clear();
            _bootDiskReadCompleted = false;
            _knownProtectionGateInstalled = false;
            Drive0.Insert(disk);
            _machine.ResetHardware();
            InstallBootHostTraps();
            ValidateBootBlock(disk.BootBlock);
            _machine.Bus.CopyToChipRam(BootBlockAddress, disk.BootBlock);
            _machine.Cpu.Reset(BootEntryAddress, StackTopAddress);
            _machine.Cpu.State.A[1] = BootIoRequestAddress;
            _machine.Cpu.State.A[6] = AmigaKickstartHost.ExecLibraryBase;
            var result = ExecuteBootBlock(maxInstructions, runMode);
            return result;
        }

        public AmigaBootResult ContinueExecution(int maxInstructions = 20_000)
        {
            return ExecuteBootBlock(maxInstructions, AmigaBootRunMode.ContinueAfterBootDiskRead);
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
                    HostOk,
                    HostOk,
                    HostOk,
                    HostOk));
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.ExecLibraryBase, -456), HostDoIo);
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.ExecLibraryBase, -198), HostAllocMem);
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.ExecLibraryBase, -210), HostOk);
            bus.RegisterHostCallback(Lvo(AmigaKickstartHost.ExecLibraryBase, -414), HostOk);
        }

        private AmigaBootResult ExecuteBootBlock(int maxInstructions, AmigaBootRunMode runMode)
        {
            var instructions = 0;
            var completed = false;
            try
            {
                while (!_machine.Cpu.State.Halted && instructions < maxInstructions)
                {
                    if (_machine.Cpu.State.ProgramCounter == 0x0000_0400 && instructions > 0)
                    {
                        completed = true;
                        break;
                    }

                    _machine.Cpu.ExecuteInstruction();
                    _machine.Bus.AdvanceRasterTo(_machine.Cpu.State.Cycles);
                    _machine.Bus.Paula.AdvanceTo(_machine.Cpu.State.Cycles);
                    instructions++;
                    if (_bootDiskReadCompleted && runMode == AmigaBootRunMode.StopAfterBootDiskRead)
                    {
                        completed = true;
                        break;
                    }
                }

                if (instructions >= maxInstructions)
                {
                    _diagnostics.Add(new AmigaBootDiagnostic("AMIGA_BOOT_OVERRUN", "Boot block execution exceeded the instruction budget."));
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
            state.D[0] = 0x0004_0000;
        }

        private void HostAllocMemAndStore(M68kCpuState state)
        {
            HostAllocMem(state);
            if (state.A[0] != 0 && state.A[0] + 4 <= _machine.Bus.ChipRam.Length)
            {
                _machine.Bus.WriteLong(state.A[0], state.D[0], state.Cycles);
            }
        }

        private static void HostFreeMem(M68kCpuState state)
        {
            state.D[0] = 0;
        }

        private void HostOpenLibrary(M68kCpuState state)
        {
            var name = ReadNullTerminatedString(state.A[1], 96);
            if (name.IndexOf("dos", StringComparison.OrdinalIgnoreCase) >= 0)
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
            else
            {
                state.D[0] = AmigaKickstartHost.DummyLibraryBase;
            }
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
