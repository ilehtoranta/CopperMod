using System;
using System.Collections.Generic;

namespace CopperMod.Sid
{
    internal sealed class C64Machine : ICpuBus
    {
        private readonly byte[] _ram = new byte[65536];
        private readonly byte[] _colorRam = new byte[0x400];
        private readonly byte[] _kernalRom = new byte[0x2000];
        private readonly byte[] _basicRom = new byte[0x2000];
        private readonly byte[] _characterRom = new byte[0x1000];
        private readonly SidModule _module;
        private readonly C64ClockProfile _clock;
        private readonly Cia6526 _cia1 = new Cia6526();
        private readonly Cia6526 _cia2 = new Cia6526();
        private readonly VicII _vic;
        private readonly VicMemoryReader _readVicMemory;
        private readonly EasyFlashCartridge? _easyFlash;
        private readonly DigimaxAudio _digimax = new DigimaxAudio();
        private const ushort KernalRomStart = 0xE000;
        private const ushort BasicRomStart = 0xA000;
        private const ushort RamIrqVector = 0x0314;
        private const ushort RamNmiVector = 0x0318;
        private const ushort KernalIrqEntryAddress = 0xFF48;
        private const ushort KernalNmiEntryAddress = 0xFE47;
        private const ushort RtiStubAddress = 0xFF92;
        private const ushort IdleLoopAddress = 0xFF94;
        private const ushort KernalIrqHandlerAddress = 0xEA31;
        private const ushort KernalIrqExitAddress = 0xEA81;
        private const ushort KernalNmiHandlerAddress = 0xFE50;
        private const ushort KernalUpdateClockAddress = 0xFFEA;
        private const ushort KernalIrqUserVectorJumpAddress = 0xFF58;
        private const ushort KernalCintAddress = 0xFF81;
        private const ushort KernalIoInitAddress = 0xFF84;
        private const ushort KernalRamTasAddress = 0xFF87;
        private const ushort KernalRestorAddress = 0xFF8A;
        private const ushort KernalChrOutAddress = 0xFFD2;
        private const ushort TextScreenStart = 0x0400;
        private const int TextScreenColumns = 40;
        private const int TextScreenRows = 25;
        private const byte DefaultTextColor = 0x0E;
        private byte _processorPortDirection;
        private byte _processorPortValue;
        private long _hardwareCycle;
        private bool _irqLine;
        private bool _cia2NmiLine;
        private bool _nmiPending;
        private C64InterruptSource _lastInterruptSource;
        private BasicRsidRunner? _basicRunner;
        private long _psidCiaTimerAIntervalCycles;
        private bool _psidCiaTimerATouched;
        private long _pendingCpuStallCycles;
        private bool _cpuInstructionActive;
        private int _vicBankBase;
        private readonly List<ScheduledAutostartKey> _autostartKeys = new List<ScheduledAutostartKey>();
        private readonly HashSet<C64Key> _pressedKeys = new HashSet<C64Key>();
        private readonly byte[] _videoVicRegisters = new byte[0x40];
        private readonly List<C64SpriteRegisterSnapshot> _spriteRegisterSnapshots = new List<C64SpriteRegisterSnapshot>(32);
        private long _videoFrameNumber;
        private int _kernalCursorColumn;
        private int _kernalCursorRow;
        private byte _kernalTextColor = DefaultTextColor;

        public C64Machine(
            SidModule module,
            SidFilterProfileId filterProfile = SidFilterProfileId.Auto,
            SidEmulationProfile sidEmulationProfile = SidEmulationProfile.Balanced)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));
            _clock = C64ClockProfile.FromSidClock(module.Clock);
            _vic = new VicII(_clock);
            _readVicMemory = ReadVicMemory;
            _easyFlash = module.Cartridge?.Type == C64CartridgeType.EasyFlash
                ? new EasyFlashCartridge(module.Cartridge)
                : null;
            SidEmulationProfile = sidEmulationProfile;
            Sid = new SidSystem(module.Chips, module.EffectiveChipModel, _clock.CpuCyclesPerSecond, filterProfile, sidEmulationProfile);
            Cpu = new Mos6510(this);
            UpdateVicBankBase();
            InstallMinimalRoms();
        }

        public Mos6510 Cpu { get; }

        public SidSystem Sid { get; }

        public SidEmulationProfile SidEmulationProfile { get; }

        public C64ClockProfile Clock => _clock;

        public long Cycle => Cpu.Cycles;

        public byte[] Ram => _ram;

        public IReadOnlyList<SidRegisterWrite> SidWrites => Sid.Writes;

        public IReadOnlyList<DigimaxWrite> DigimaxWrites => _digimax.Writes;

        internal CpuBusTrace? CpuBusTrace { get; set; }

        public long PsidCiaTimerAIntervalCycles => _psidCiaTimerAIntervalCycles;

        public C64MachineDebugState DebugState => new C64MachineDebugState(
            _hardwareCycle,
            _cia1.DebugState,
            _cia2.DebugState,
            _vic.DebugState,
            CreateMemoryBankState(),
            _basicRunner?.DebugState ?? BasicRsidDebugState.Disabled,
            _irqLine,
            _nmiPending,
            _cia2NmiLine,
            _lastInterruptSource);

        public C64VideoFrame RenderVideoFrame()
        {
            _vic.CopyRegisters(_videoVicRegisters);
            var frame = C64DiagnosticVideoRenderer.Render(
                _ram,
                _colorRam,
                _videoVicRegisters,
                ReadVicMemory,
                _vicBankBase,
                _spriteRegisterSnapshots,
                _videoFrameNumber++,
                SidIntegerMath.CyclesToTimeSpan(Cpu.Cycles, _clock.CpuCyclesPerSecond));
            return frame;
        }

        public void Reset(int subSongIndex)
        {
            Array.Clear(_ram);
            Array.Clear(_colorRam);
            _processorPortDirection = 0x2F;
            _processorPortValue = (byte)SidConstants.DefaultBankRegister;
            _hardwareCycle = 0;
            _irqLine = false;
            _cia2NmiLine = false;
            _nmiPending = false;
            _lastInterruptSource = C64InterruptSource.None;
            _basicRunner = null;
            _psidCiaTimerAIntervalCycles = GetDefaultPsidCiaTimerAIntervalCycles();
            _psidCiaTimerATouched = false;
            _pendingCpuStallCycles = 0;
            _cpuInstructionActive = false;
            _cia1.Reset(defaultTimerA60Hz: _module.IsRsid, _clock.CpuCyclesPerSecond);
            _cia2.Reset(defaultTimerA60Hz: false, _clock.CpuCyclesPerSecond);
            UpdateVicBankBase();
            _vic.Reset();
            Sid.Reset();
            _digimax.Reset();
            _easyFlash?.Reset();
            _pressedKeys.Clear();
            _spriteRegisterSnapshots.Clear();
            _videoFrameNumber = 0;
            _kernalCursorColumn = 0;
            _kernalCursorRow = 0;
            _kernalTextColor = DefaultTextColor;
            if (_module.IsCartridge)
            {
                InstallKernalRamVectors();
                Cpu.Reset(ReadResetVector());
                Cpu.ResetCycles();
                _hardwareCycle = 0;
                Sid.ResetClock();
                RefreshInterruptLines();
                return;
            }

            LoadPayload();
            InstallRsidEnvironment();
            if (_module.IsBasicRsid)
            {
                Cpu.Reset(IdleLoopAddress);
                _basicRunner = new BasicRsidRunner(this, _module.EffectiveLoadAddress);
                Cpu.ResetCycles();
                _hardwareCycle = 0;
                Sid.ResetClock();
                RefreshInterruptLines();
                return;
            }

            Cpu.Reset(_module.InitAddress);
            Cpu.BeginSubroutine(_module.InitAddress, (byte)subSongIndex);
            RunUntilSubroutineReturn(250_000);
            Sid.AdvanceTo(Cpu.Cycles);
            if (_module.IsRsid && Cpu.ProgramCounter == 0xFFFF)
            {
                Cpu.ProgramCounter = IdleLoopAddress;
            }

            Cpu.ResetCycles();
            _hardwareCycle = 0;
            Sid.ResetClock();
            RefreshInterruptLines();
        }

        [HotPath]
        public void RunFrame()
        {
            BeginFrame();
            RunCycles(_clock.CyclesPerFrame);
        }

        [HotPath]
        public void BeginFrame()
        {
            if (_module.RunsContinuously)
            {
                return;
            }

            if (_module.PlayAddress != 0)
            {
                Cpu.BeginSubroutine(_module.PlayAddress, 0);
                RunUntilSubroutineReturn(250_000);
                FinishPsidPlayRoutine();
            }
        }

        [HotPath]
        public void RunCycles(long cycleCount)
        {
            RunCycles(cycleCount, advanceSidToFinalCycle: true);
        }

        [HotPath]
        private void RunCycles(long cycleCount, bool advanceSidToFinalCycle)
        {
            var target = Cpu.Cycles + cycleCount;
            if (_basicRunner != null)
            {
                _basicRunner.RunUntil(target);
                AdvanceHardwareTo(target);
                if (advanceSidToFinalCycle)
                {
                    Sid.AdvanceTo(target);
                }

                return;
            }

            while (Cpu.Cycles < target && !Cpu.Halted)
            {
                var before = Cpu.Cycles;
                BeginCpuInstruction();
                var executed = Cpu.ExecuteInstruction();
                CompleteCpuInstruction();
                AdvanceHardwareTo(Cpu.Cycles);
                ServicePendingInterrupts();
                if (Cpu.Cycles == before)
                {
                    Cpu.ResetCycles();
                    _hardwareCycle = 0;
                    break;
                }
            }

            var finalCycle = Math.Max(target, Cpu.Cycles);
            AdvanceHardwareTo(finalCycle);
            if (advanceSidToFinalCycle)
            {
                Sid.AdvanceTo(finalCycle);
            }
        }

        [HotPath]
        public void RunUntilSubroutineReturn(long maxCycles)
        {
            var target = Cpu.Cycles + maxCycles;
            while (Cpu.Cycles < target && !Cpu.Halted && Cpu.ProgramCounter != 0xFFFF)
            {
                BeginCpuInstruction();
                var executed = Cpu.ExecuteInstruction();
                CompleteCpuInstruction();
                AdvanceHardwareTo(Cpu.Cycles);
                ServicePendingInterrupts();
            }
        }

        [HotPath]
        public void RenderFrame(
            Span<float> destination,
            AudioRenderOptionsAdapter options,
            ReadOnlySpan<long> sampleTargetCycles,
            long cycleCount)
        {
            var frames = destination.Length / options.ChannelCount;
            if (frames != sampleTargetCycles.Length)
            {
                throw new ArgumentException("Destination frame count must match the sample target cycle count.", nameof(destination));
            }

            var tickCycles = Math.Max(1, cycleCount);
            if (_basicRunner != null)
            {
                RenderBasicFrame(destination, options, sampleTargetCycles, tickCycles);
                return;
            }

            var frameEndCycle = Cpu.Cycles + tickCycles;
            var psidPlayActive = BeginPsidFrame();
            var psidPlayStarted = psidPlayActive;
            for (var outputFrame = 0; outputFrame < sampleTargetCycles.Length; outputFrame++)
            {
                var targetCycle = sampleTargetCycles[outputFrame];
                if (targetCycle > frameEndCycle)
                {
                    throw new ArgumentOutOfRangeException(nameof(sampleTargetCycles), targetCycle, "Sample target cycle cannot be after the rendered cycle range.");
                }

                if (_module.RunsContinuously)
                {
                    RunCycles(Math.Max(0, targetCycle - Cpu.Cycles), advanceSidToFinalCycle: false);
                }
                else if (psidPlayActive)
                {
                    RunPsidPlayUntil(targetCycle, ref psidPlayActive);
                    if (Cpu.Cycles < targetCycle)
                    {
                        AdvanceSidOnly(targetCycle - Cpu.Cycles);
                    }
                }
                else
                {
                    AdvanceSidOnly(Math.Max(0, targetCycle - Cpu.Cycles));
                }

                var sample = MixDigitalOutputs(Sid.RenderSample(targetCycle));
                WriteOutputFrame(destination, options.ChannelCount, outputFrame, sample);
            }

            if (Cpu.Cycles < frameEndCycle)
            {
                if (_module.RunsContinuously)
                {
                    RunCycles(frameEndCycle - Cpu.Cycles, advanceSidToFinalCycle: false);
                }
                else if (psidPlayActive)
                {
                    RunPsidPlayUntil(frameEndCycle, ref psidPlayActive);
                    if (Cpu.Cycles < frameEndCycle)
                    {
                        AdvanceSidOnly(frameEndCycle - Cpu.Cycles);
                    }
                }
                else
                {
                    AdvanceSidOnly(frameEndCycle - Cpu.Cycles);
                }
            }

            if (psidPlayStarted && !psidPlayActive)
            {
                FinishPsidPlayRoutine();
            }

            Sid.AdvanceTo(frameEndCycle);
        }

        [HotPath]
        private void RenderBasicFrame(
            Span<float> destination,
            AudioRenderOptionsAdapter options,
            ReadOnlySpan<long> sampleTargetCycles,
            long tickCycles)
        {
            var frameStartCycle = Cpu.Cycles;
            var frameEndCycle = frameStartCycle + tickCycles;
            _basicRunner!.RunUntil(frameEndCycle);
            AdvanceHardwareTo(frameEndCycle);

            for (var outputFrame = 0; outputFrame < sampleTargetCycles.Length; outputFrame++)
            {
                var targetCycle = sampleTargetCycles[outputFrame];
                if (targetCycle > frameEndCycle)
                {
                    throw new ArgumentOutOfRangeException(nameof(sampleTargetCycles), targetCycle, "Sample target cycle cannot be after the rendered cycle range.");
                }

                var sample = MixDigitalOutputs(Sid.RenderSample(targetCycle));
                WriteOutputFrame(destination, options.ChannelCount, outputFrame, sample);
            }
        }

        [HotPath]
        private bool BeginPsidFrame()
        {
            if (_module.RunsContinuously || _module.PlayAddress == 0)
            {
                return false;
            }

            _psidCiaTimerATouched = false;
            Cpu.BeginSubroutine(_module.PlayAddress, 0);
            return true;
        }

        [HotPath]
        private void FinishPsidPlayRoutine()
        {
            if (_module.RunsContinuously || !_psidCiaTimerATouched)
            {
                return;
            }

            _psidCiaTimerAIntervalCycles = Math.Max(1L, _cia1.TimerALatch);
            _psidCiaTimerATouched = false;
        }

        [HotPath]
        private void RunPsidPlayUntil(long targetCycle, ref bool active)
        {
            while (active && Cpu.Cycles < targetCycle && !Cpu.Halted && Cpu.ProgramCounter != 0xFFFF)
            {
                var before = Cpu.Cycles;
                BeginCpuInstruction();
                var executed = Cpu.ExecuteInstruction();
                CompleteCpuInstruction();
                AdvanceHardwareTo(Cpu.Cycles);
                ServicePendingInterrupts();
                if (Cpu.Cycles == before)
                {
                    active = false;
                    break;
                }
            }

            if (Cpu.ProgramCounter == 0xFFFF || Cpu.Halted)
            {
                active = false;
            }
        }

        [HotPath]
        private void AdvanceSidOnly(long cycleCount)
        {
            if (cycleCount <= 0)
            {
                return;
            }

            var target = Cpu.Cycles + cycleCount;
            Sid.AdvanceTo(target);
            Cpu.AdvanceCycles(cycleCount);
        }

        [HotPath]
        public byte Read(ushort address)
        {
            return Read(address, cycleOffset: 0, CpuBusAccessKind.Read);
        }

        [HotPath]
        public byte Read(ushort address, int cycleOffset = 0, CpuBusAccessKind kind = CpuBusAccessKind.Read)
        {
            var busCycle = ResolveCpuBusCycle(cycleOffset, kind);
            var readCycle = busCycle.Cycle;
            AdvanceHardwareTo(readCycle);
            byte value;
            if (address == 0x0000)
            {
                value = _processorPortDirection;
                CaptureCpuBusRead(busCycle, address, value, kind);
                return value;
            }

            if (address == 0x0001)
            {
                value = ProcessorPortEffectiveValue;
                CaptureCpuBusRead(busCycle, address, value, kind);
                return value;
            }

            if (IsIoVisible() && address >= 0xD000 && address <= 0xDFFF)
            {
                value = ReadIo(address, readCycle);
                CaptureCpuBusRead(busCycle, address, value, kind);
                return value;
            }

            if (IsCharacterRomVisible() && address >= 0xD000 && address <= 0xDFFF)
            {
                value = _characterRom[address - 0xD000];
                CaptureCpuBusRead(busCycle, address, value, kind);
                return value;
            }

            var kernalVisible = IsKernalVisible();
            if (_easyFlash != null && _easyFlash.TryRead(address, kernalVisible, out value))
            {
                CaptureCpuBusRead(busCycle, address, value, kind);
                return value;
            }

            if (kernalVisible && address >= KernalRomStart && TryHandleKernalOpcodeFetch(address, kind, out value))
            {
                CaptureCpuBusRead(busCycle, address, value, kind);
                return value;
            }

            if (IsBasicVisible() && address >= 0xA000 && address <= 0xBFFF)
            {
                value = _basicRom[address - BasicRomStart];
                CaptureCpuBusRead(busCycle, address, value, kind);
                return value;
            }

            if (kernalVisible && address >= 0xE000)
            {
                value = _kernalRom[address - KernalRomStart];
                CaptureCpuBusRead(busCycle, address, value, kind);
                return value;
            }

            value = _ram[address];
            CaptureCpuBusRead(busCycle, address, value, kind);
            return value;
        }

        [HotPath]
        public void Write(ushort address, byte value, int cycleOffset, CpuBusAccessKind kind = CpuBusAccessKind.Write)
        {
            var busCycle = ResolveCpuBusCycle(cycleOffset, kind);
            var writeCycle = busCycle.Cycle;
            AdvanceHardwareTo(writeCycle);
            CaptureCpuBusWrite(busCycle, address, value, kind);
            if (address == 0x0000)
            {
                _processorPortDirection = value;
                _ram[address] = value;
                return;
            }

            if (address == 0x0001)
            {
                _processorPortValue = value;
                _ram[address] = value;
                return;
            }

            if (IsIoVisible() && address >= 0xD000 && address <= 0xDFFF)
            {
                WriteIo(address, value, writeCycle);
            }

            _ram[address] = value;
        }

        [HotPath]
        public void Idle(ushort address, int cycleOffset, CpuBusAccessKind kind = CpuBusAccessKind.Idle)
        {
            var busCycle = ResolveCpuBusCycle(cycleOffset, kind);
            AdvanceHardwareTo(busCycle.Cycle);
            CaptureCpuBusIdle(busCycle, address, kind);
        }

        [HotPath]
        internal byte NativeRead(ushort address)
        {
            return Read(address, cycleOffset: 0);
        }

        [HotPath]
        internal void NativeWrite(ushort address, byte value)
        {
            Write(address, value, cycleOffset: 0);
        }

        [HotPath]
        internal void AdvanceNativeCycles(long cycles)
        {
            if (cycles <= 0)
            {
                return;
            }

            var target = Cpu.Cycles + cycles;
            AdvanceHardwareTo(target);
            Cpu.AdvanceCycles(cycles);
        }

        internal bool RunNativeSubroutine(ushort address, long maxCycles)
        {
            if ((address >= BasicRomStart && address <= 0xBFFF) || address >= KernalRomStart)
            {
                return RunNativeRomTrap(address);
            }

            Cpu.BeginSubroutine(address, 0);
            RunUntilSubroutineReturn(maxCycles);
            if (Cpu.ProgramCounter == 0xFFFF)
            {
                Cpu.ProgramCounter = IdleLoopAddress;
                return true;
            }

            return !Cpu.Halted;
        }

        private bool RunNativeRomTrap(ushort address)
        {
            switch (address)
            {
                case KernalCintAddress:
                    HostKernalCint();
                    AdvanceNativeCycles(256);
                    return true;
                case KernalIoInitAddress:
                    HostKernalIoInit();
                    AdvanceNativeCycles(128);
                    return true;
                case KernalRestorAddress:
                    InstallKernalRamVectors();
                    AdvanceNativeCycles(64);
                    return true;
                case KernalChrOutAddress:
                    HostKernalChrOut(Cpu.A);
                    AdvanceNativeCycles(128);
                    return true;
                case 0xFFCF: // CHRIN
                case 0xFFE1: // STOP
                case 0xFFE4: // GETIN
                    Cpu.A = 0;
                    AdvanceNativeCycles(64);
                    return true;
                default:
                    return false;
            }
        }

        private void LoadPayload()
        {
            if (_module.IsCartridge)
            {
                return;
            }

            var address = _module.EffectiveLoadAddress;
            for (var i = 0; i < _module.Payload.Length; i++)
            {
                _ram[address + i] = _module.Payload[i];
            }
        }

        private void InstallRsidEnvironment()
        {
            if (!_module.IsRsid)
            {
                return;
            }

            InstallKernalRamVectors();
            WriteWord(0xFFFE, KernalIrqEntryAddress);
            WriteWord(0xFFFA, KernalNmiEntryAddress);

            WriteRamStub(KernalIrqEntryAddress, 0x48, 0x8A, 0x48, 0x98, 0x48, 0x6C, 0x14, 0x03);
            WriteRamStub(KernalNmiEntryAddress, 0x6C, 0x18, 0x03);
            WriteRamStub(KernalNmiHandlerAddress, 0xAD, 0x0D, 0xDD, 0x40);
            WriteRamStub(RtiStubAddress, 0x40); // RTI

            _ram[IdleLoopAddress + 0] = 0xEA; // NOP
            _ram[IdleLoopAddress + 1] = 0xEA; // NOP
            _ram[IdleLoopAddress + 2] = 0x4C; // JMP idle loop
            _ram[IdleLoopAddress + 3] = (byte)(IdleLoopAddress & 0xFF);
            _ram[IdleLoopAddress + 4] = (byte)(IdleLoopAddress >> 8);
        }

        private void InstallKernalRamVectors()
        {
            WriteWord(RamIrqVector, KernalIrqHandlerAddress);
            WriteWord(RamNmiVector, KernalNmiHandlerAddress);
        }

        private void InstallMinimalRoms()
        {
            Array.Fill(_basicRom, (byte)0x60); // RTS for unimplemented BASIC calls.
            Array.Fill(_kernalRom, (byte)0x60); // RTS for unimplemented KERNAL calls.
            C64CharacterRom.Install(_characterRom);

            WriteKernalStub(KernalIrqHandlerAddress, 0x20, 0xEA, 0xFF, 0xAD, 0x0D, 0xDC, 0xAD, 0x19, 0xD0, 0x8D, 0x19, 0xD0, 0x4C, 0x81, 0xEA);
            WriteKernalStub(0xEA7E, 0xAD, 0x0D, 0xDC, 0x4C, 0x81, 0xEA); // LDA $DC0D; JMP IRQ exit
            WriteKernalStub(KernalIrqExitAddress, 0x68, 0xA8, 0x68, 0xAA, 0x68, 0x40); // PLA/TAY, PLA/TAX, PLA, RTI
            WriteKernalStub(KernalNmiEntryAddress, 0x6C, 0x18, 0x03);
            WriteKernalStub(KernalNmiHandlerAddress, 0xAD, 0x0D, 0xDD, 0x40);
            WriteKernalStub(KernalIrqUserVectorJumpAddress, 0x6C, 0x14, 0x03); // JMP ($0314)
            WriteKernalStub(KernalIrqEntryAddress, 0x48, 0x8A, 0x48, 0x98, 0x48, 0x6C, 0x14, 0x03);
            WriteKernalStub(KernalCintAddress, 0x60);
            WriteKernalStub(KernalIoInitAddress, 0x60);
            WriteKernalStub(KernalRamTasAddress, 0x60);
            WriteKernalStub(KernalRestorAddress, 0x60);
            WriteKernalStub(RtiStubAddress, 0x40);
            WriteKernalStub(KernalUpdateClockAddress, 0xEE, 0xA2, 0x00, 0xD0, 0x08, 0xEE, 0xA1, 0x00, 0xD0, 0x03, 0xEE, 0xA0, 0x00, 0x60);
            WriteKernalStub(
                IdleLoopAddress,
                0xEA,
                0xEA,
                0x4C,
                (byte)(IdleLoopAddress & 0xFF),
                (byte)(IdleLoopAddress >> 8));
            WriteKernalStub(
                0xFFFA,
                (byte)(KernalNmiEntryAddress & 0xFF),
                (byte)(KernalNmiEntryAddress >> 8),
                (byte)(IdleLoopAddress & 0xFF),
                (byte)(IdleLoopAddress >> 8),
                (byte)(KernalIrqEntryAddress & 0xFF),
                (byte)(KernalIrqEntryAddress >> 8));
        }

        private bool TryHandleKernalOpcodeFetch(ushort address, CpuBusAccessKind kind, out byte value)
        {
            value = 0;
            if (kind != CpuBusAccessKind.OpcodeFetch)
            {
                return false;
            }

            switch (address)
            {
                case KernalCintAddress:
                    HostKernalCint();
                    value = 0x60;
                    return true;
                case KernalIoInitAddress:
                    HostKernalIoInit();
                    value = 0x60;
                    return true;
                case KernalRestorAddress:
                    InstallKernalRamVectors();
                    value = 0x60;
                    return true;
                case KernalChrOutAddress:
                    HostKernalChrOut(Cpu.A);
                    value = 0x60;
                    return true;
                case 0xFFCF: // CHRIN
                case 0xFFE1: // STOP
                case 0xFFE4: // GETIN
                    Cpu.A = 0;
                    value = 0x60;
                    return true;
                default:
                    return false;
            }
        }

        private void HostKernalIoInit()
        {
            _cia1.Write(0x0D, 0x7F);
            _cia2.Write(0x0D, 0x7F);
            _cia1.Write(0x0E, 0x00);
            _cia1.Write(0x0F, 0x00);
            _cia2.Write(0x02, 0x3F);
            _cia2.Write(0x0E, 0x00);
            _cia2.Write(0x0F, 0x00);
            UpdateVicBankBase();
            RefreshInterruptLines();
        }

        private void HostKernalCint()
        {
            _vic.Write(0x11, 0x1B);
            _vic.Write(0x16, 0x08);
            _vic.Write(0x18, 0x14);
            _vic.Write(0x20, 0x0E);
            _vic.Write(0x21, 0x06);
            ClearKernalTextScreen();
        }

        private void ClearKernalTextScreen()
        {
            Array.Fill(_colorRam, DefaultTextColor);
            for (var i = 0; i < TextScreenColumns * TextScreenRows; i++)
            {
                _ram[TextScreenStart + i] = 0x20;
            }

            _kernalCursorColumn = 0;
            _kernalCursorRow = 0;
            _kernalTextColor = DefaultTextColor;
        }

        private void HostKernalChrOut(byte petscii)
        {
            switch (petscii)
            {
                case 0x0D:
                    NewLine();
                    return;
                case 0x13:
                    _kernalCursorColumn = 0;
                    _kernalCursorRow = 0;
                    return;
                case 0x93:
                    ClearKernalTextScreen();
                    return;
            }

            if (petscii < 0x20)
            {
                return;
            }

            var cell = (_kernalCursorRow * TextScreenColumns) + _kernalCursorColumn;
            _ram[TextScreenStart + cell] = PetsciiToScreenCode(petscii);
            _colorRam[cell] = _kernalTextColor;
            _kernalCursorColumn++;
            if (_kernalCursorColumn >= TextScreenColumns)
            {
                NewLine();
            }
        }

        private void NewLine()
        {
            _kernalCursorColumn = 0;
            _kernalCursorRow++;
            if (_kernalCursorRow < TextScreenRows)
            {
                return;
            }

            ScrollTextScreen();
            _kernalCursorRow = TextScreenRows - 1;
        }

        private void ScrollTextScreen()
        {
            const int screenLength = TextScreenColumns * TextScreenRows;
            Array.Copy(_ram, TextScreenStart + TextScreenColumns, _ram, TextScreenStart, screenLength - TextScreenColumns);
            for (var i = screenLength - TextScreenColumns; i < screenLength; i++)
            {
                _ram[TextScreenStart + i] = 0x20;
            }

            Array.Copy(_colorRam, TextScreenColumns, _colorRam, 0, screenLength - TextScreenColumns);
            for (var i = screenLength - TextScreenColumns; i < screenLength; i++)
            {
                _colorRam[i] = _kernalTextColor;
            }
        }

        private static byte PetsciiToScreenCode(byte value)
        {
            if (value >= (byte)'A' && value <= (byte)'Z')
            {
                return (byte)(value - (byte)'A' + 1);
            }

            if (value >= 0xC1 && value <= 0xDA)
            {
                return (byte)(value - 0xC1 + 1);
            }

            if (value >= 0x20 && value <= 0x3F)
            {
                return value;
            }

            if (value >= 0x60 && value <= 0x7F)
            {
                return (byte)(value - 0x40);
            }

            return 0x20;
        }

        private void WriteRamStub(ushort address, params byte[] bytes)
        {
            for (var i = 0; i < bytes.Length; i++)
            {
                _ram[address + i] = bytes[i];
            }
        }

        private void WriteKernalStub(ushort address, params byte[] bytes)
        {
            var offset = address - KernalRomStart;
            for (var i = 0; i < bytes.Length; i++)
            {
                _kernalRom[offset + i] = bytes[i];
            }
        }

        private void WriteWord(ushort address, ushort value)
        {
            _ram[address] = (byte)value;
            _ram[(ushort)(address + 1)] = (byte)(value >> 8);
        }

        [HotPath]
        private void AdvanceHardwareTo(long targetCycle)
        {
            if (targetCycle <= _hardwareCycle)
            {
                return;
            }

            while (_hardwareCycle < targetCycle)
            {
                _hardwareCycle++;
                var cia1Irq = _cia1.Tick();
                var vicIrq = _vic.Tick(_readVicMemory, _vicBankBase);
                var cia2NmiLine = _cia2.Tick();
                _irqLine = cia1Irq || vicIrq;
                if (cia2NmiLine && !_cia2NmiLine)
                {
                    _nmiPending = true;
                }

                _cia2NmiLine = cia2NmiLine;
            }
        }

        [HotPath]
        private void BeginCpuInstruction()
        {
            _pendingCpuStallCycles = 0;
            _cpuInstructionActive = true;
        }

        [HotPath]
        private void CompleteCpuInstruction()
        {
            if (_pendingCpuStallCycles <= 0)
            {
                _cpuInstructionActive = false;
                return;
            }

            Cpu.AdvanceCycles(_pendingCpuStallCycles);
            _pendingCpuStallCycles = 0;
            _cpuInstructionActive = false;
        }

        [HotPath]
        private CpuBusCycleResolution ResolveCpuBusCycle(int cycleOffset, CpuBusAccessKind kind)
        {
            var offset = Math.Max(0, cycleOffset);
            var requestedCycle = Cpu.Cycles + offset + _pendingCpuStallCycles;
            if (!_cpuInstructionActive || !_module.IsRsid)
            {
                return new CpuBusCycleResolution(requestedCycle, requestedCycle, offset);
            }

            var actualCycle = requestedCycle;
            while (true)
            {
                AdvanceHardwareTo(actualCycle);
                var blocked = IsCpuBusWrite(kind)
                    ? _vic.IsCpuWriteBlocked()
                    : _vic.IsCpuReadBlocked();
                if (!blocked)
                {
                    break;
                }

                actualCycle++;
            }

            _pendingCpuStallCycles += actualCycle - requestedCycle;
            return new CpuBusCycleResolution(requestedCycle, actualCycle, offset);
        }

        [HotPath]
        private void CaptureCpuBusRead(CpuBusCycleResolution busCycle, ushort address, byte value, CpuBusAccessKind kind)
        {
            CpuBusTrace?.Add(new CpuBusTraceFrame(
                busCycle.RequestedCycle,
                busCycle.Cycle,
                busCycle.CycleOffset,
                kind == CpuBusAccessKind.OpcodeFetch ? value : Cpu.LastOpcode,
                address,
                value,
                kind,
                busCycle.DelayedByVic));
        }

        [HotPath]
        private void CaptureCpuBusWrite(CpuBusCycleResolution busCycle, ushort address, byte value, CpuBusAccessKind kind)
        {
            CpuBusTrace?.Add(new CpuBusTraceFrame(
                busCycle.RequestedCycle,
                busCycle.Cycle,
                busCycle.CycleOffset,
                Cpu.LastOpcode,
                address,
                value,
                kind,
                busCycle.DelayedByVic));
        }

        [HotPath]
        private void CaptureCpuBusIdle(CpuBusCycleResolution busCycle, ushort address, CpuBusAccessKind kind)
        {
            CpuBusTrace?.Add(new CpuBusTraceFrame(
                busCycle.RequestedCycle,
                busCycle.Cycle,
                busCycle.CycleOffset,
                Cpu.LastOpcode,
                address,
                null,
                kind,
                busCycle.DelayedByVic));
        }

        [HotPath]
        private static bool IsCpuBusWrite(CpuBusAccessKind kind)
        {
            return kind == CpuBusAccessKind.Write ||
                kind == CpuBusAccessKind.DummyWrite ||
                kind == CpuBusAccessKind.StackWrite;
        }

        private readonly struct CpuBusCycleResolution
        {
            public CpuBusCycleResolution(long requestedCycle, long cycle, int cycleOffset)
            {
                RequestedCycle = requestedCycle;
                Cycle = cycle;
                CycleOffset = cycleOffset;
            }

            public long RequestedCycle { get; }

            public long Cycle { get; }

            public int CycleOffset { get; }

            public bool DelayedByVic => Cycle != RequestedCycle;
        }

        [HotPath]
        private void RefreshInterruptLines()
        {
            _irqLine = _cia1.InterruptLine || _vic.IrqLine;
            var cia2Line = _cia2.InterruptLine;
            if (cia2Line && !_cia2NmiLine)
            {
                _nmiPending = true;
            }

            _cia2NmiLine = cia2Line;
        }

        [HotPath]
        private void ServicePendingInterrupts()
        {
            RefreshInterruptLines();
            if (_nmiPending)
            {
                _nmiPending = false;
                _lastInterruptSource = C64InterruptSource.Cia2;
                BeginCpuInstruction();
                Cpu.TryRequestNmi();
                CompleteCpuInstruction();
                AdvanceHardwareTo(Cpu.Cycles);
                RefreshInterruptLines();
                return;
            }

            if (_irqLine)
            {
                BeginCpuInstruction();
                var serviced = Cpu.TryRequestIrq();
                CompleteCpuInstruction();
                if (!serviced)
                {
                    return;
                }

                _lastInterruptSource = _vic.IrqLine
                    ? C64InterruptSource.Vic
                    : C64InterruptSource.Cia1;
                AdvanceHardwareTo(Cpu.Cycles);
                RefreshInterruptLines();
            }
        }

        [HotPath]
        private bool IsIoVisible()
        {
            var value = ProcessorPortEffectiveValue;
            return (value & 0x04) != 0 && (value & 0x03) != 0;
        }

        [HotPath]
        private bool IsKernalVisible()
        {
            return (_module.IsRsid || _module.IsCartridge) && (ProcessorPortEffectiveValue & 0x02) != 0;
        }

        [HotPath]
        private bool IsBasicVisible()
        {
            return (_module.IsRsid || _module.IsCartridge) && (ProcessorPortEffectiveValue & 0x03) == 0x03;
        }

        [HotPath]
        private bool IsCharacterRomVisible()
        {
            var value = ProcessorPortEffectiveValue;
            return (_module.IsRsid || _module.IsCartridge) && (value & 0x04) == 0 && (value & 0x03) != 0;
        }

        private byte ProcessorPortEffectiveValue => (byte)((_processorPortValue & _processorPortDirection) | (~_processorPortDirection & 0xFF));

        [HotPath]
        private void UpdateVicBankBase()
        {
            _vicBankBase = ((~_cia2.EffectivePortA) & 0x03) * 0x4000;
        }

        [HotPath]
        private byte ReadVicMemory(ushort address)
        {
            if ((address >= 0x1000 && address <= 0x1FFF) ||
                (address >= 0x9000 && address <= 0x9FFF))
            {
                return _characterRom[address & 0x0FFF];
            }

            return _ram[address];
        }

        private C64MemoryBankState CreateMemoryBankState()
        {
            return new C64MemoryBankState(
                _processorPortDirection,
                _processorPortValue,
                ProcessorPortEffectiveValue,
                IsBasicVisible(),
                IsKernalVisible(),
                IsIoVisible(),
                IsCharacterRomVisible());
        }

        [HotPath]
        private byte ReadIo(ushort address, long readCycle)
        {
            if (address >= 0xD000 && address <= 0xD3FF)
            {
                var value = _vic.Read((byte)address);
                RefreshInterruptLines();
                return value;
            }

            if (Sid.TryRead(address, readCycle, out var sidValue))
            {
                return sidValue;
            }

            if (address >= 0xDC00 && address <= 0xDCFF)
            {
                var value = _cia1.Read(
                    (byte)address,
                    portAInputMask: GetKeyboardPortAInput(readCycle),
                    portBInputMask: GetKeyboardPortBInput(readCycle));
                RefreshInterruptLines();
                return value;
            }

            if (address >= 0xDD00 && address <= 0xDDFF)
            {
                var value = _cia2.Read((byte)address);
                RefreshInterruptLines();
                return value;
            }

            if (address >= 0xD800 && address <= 0xDBFF)
            {
                return (byte)(0xF0 | _colorRam[address & 0x03FF]);
            }

            return 0;
        }

        [HotPath]
        private void WriteIo(ushort address, byte value, long writeCycle)
        {
            if (address >= 0xDE00 && address <= 0xDEFF && _easyFlash?.TryWriteIo1(address, value) == true)
            {
                return;
            }

            if (_digimax.TryWrite(address, value, writeCycle))
            {
                return;
            }

            if (address >= 0xD000 && address <= 0xD3FF)
            {
                var register = (byte)address;
                _vic.Write(register, value);
                CaptureSpriteRegisterSnapshot(register);
                RefreshInterruptLines();
                return;
            }

            if (Sid.TryWrite(address, value, writeCycle))
            {
                return;
            }

            if (address >= 0xDC00 && address <= 0xDCFF)
            {
                var register = address & 0x0F;
                if (!_module.IsRsid && (register == 0x04 || register == 0x05))
                {
                    _psidCiaTimerATouched = true;
                }

                _cia1.Write((byte)address, value);
                RefreshInterruptLines();
                return;
            }

            if (address >= 0xDD00 && address <= 0xDDFF)
            {
                var register = address & 0x0F;
                _cia2.Write((byte)address, value);
                if (register == 0x00 || register == 0x02)
                {
                    UpdateVicBankBase();
                }

                RefreshInterruptLines();
                return;
            }

            if (address >= 0xD800 && address <= 0xDBFF)
            {
                _colorRam[address & 0x03FF] = (byte)(value & 0x0F);
            }
        }

        [HotPath]
        private long GetDefaultPsidCiaTimerAIntervalCycles()
        {
            return Math.Max(1, SidIntegerMath.DivRoundNearest(_clock.CpuCyclesPerSecond, SidConstants.CiaTimerRefreshHz));
        }

        internal void ScheduleAutostartKey(string key, TimeSpan delay, TimeSpan hold)
        {
            var clampedDelay = delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
            var clampedHold = hold <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : hold;
            var start = SidIntegerMath.TimeSpanToCycles(clampedDelay, _clock.CpuCyclesPerSecond);
            var length = Math.Max(1, SidIntegerMath.TimeSpanToCycles(clampedHold, _clock.CpuCyclesPerSecond));
            var keyPosition = C64KeyboardMatrix.GetPosition(ParseAutostartKey(key));
            _autostartKeys.Add(new ScheduledAutostartKey(start, start + length, keyPosition.ColumnMask, keyPosition.RowMask));
        }

        internal void SetKeyPressed(C64Key key, bool pressed)
        {
            if (pressed)
            {
                _pressedKeys.Add(key);
            }
            else
            {
                _pressedKeys.Remove(key);
            }
        }

        internal void ReleaseAllKeys()
        {
            _pressedKeys.Clear();
        }

        private ushort ReadResetVector()
        {
            var low = Read(0xFFFC, cycleOffset: 0, CpuBusAccessKind.Read);
            var high = Read(0xFFFD, cycleOffset: 0, CpuBusAccessKind.Read);
            return (ushort)(low | (high << 8));
        }

        [HotPath]
        private float MixDigitalOutputs(float sidSample)
        {
            return Math.Clamp(sidSample + _digimax.RenderSample(), -0.999f, 0.999f);
        }

        [HotPath]
        private byte GetKeyboardPortAInput(long cycle)
        {
            var mask = (byte)0xFF;
            var selectedRows = unchecked((byte)~_cia1.EffectivePortB);
            foreach (var key in _autostartKeys)
            {
                if (cycle >= key.StartCycle &&
                    cycle < key.EndCycle &&
                    (selectedRows & key.PortBRowMask) != 0)
                {
                    mask = unchecked((byte)(mask & ~key.PortAColumnMask));
                }
            }

            foreach (var key in _pressedKeys)
            {
                var position = C64KeyboardMatrix.GetPosition(key);
                if ((selectedRows & position.RowMask) != 0)
                {
                    mask = unchecked((byte)(mask & ~position.ColumnMask));
                }
            }

            return mask;
        }

        [HotPath]
        private byte GetKeyboardPortBInput(long cycle)
        {
            var mask = (byte)0xFF;
            var selectedColumns = unchecked((byte)~_cia1.EffectivePortA);
            foreach (var key in _autostartKeys)
            {
                if (cycle >= key.StartCycle &&
                    cycle < key.EndCycle &&
                    (selectedColumns & key.PortAColumnMask) != 0)
                {
                    mask = unchecked((byte)(mask & ~key.PortBRowMask));
                }
            }

            foreach (var key in _pressedKeys)
            {
                var position = C64KeyboardMatrix.GetPosition(key);
                if ((selectedColumns & position.ColumnMask) != 0)
                {
                    mask = unchecked((byte)(mask & ~position.RowMask));
                }
            }

            return mask;
        }

        private static C64Key ParseAutostartKey(string key)
        {
            if (string.Equals(key, "f3", StringComparison.OrdinalIgnoreCase))
            {
                return C64Key.F3;
            }

            if (string.Equals(key, "space", StringComparison.OrdinalIgnoreCase))
            {
                return C64Key.Space;
            }

            throw new ArgumentException("Unsupported C64 autostart key: " + key, nameof(key));
        }

        private void CaptureSpriteRegisterSnapshot(byte register)
        {
            register = (byte)(register & 0x3F);
            if (register != 0x0F && register != 0x15 && register != 0x10 && register != 0x17 && register != 0x1C && register != 0x1D)
            {
                return;
            }

            _vic.CopyRegisters(_videoVicRegisters);
            if (_videoVicRegisters[0x15] == 0)
            {
                return;
            }

            foreach (var snapshot in _spriteRegisterSnapshots)
            {
                if (SpriteRegistersMatch(snapshot, _videoVicRegisters))
                {
                    return;
                }
            }

            var registers = new byte[0x40];
            _videoVicRegisters.CopyTo(registers, 0);
            var pointers = ReadSpritePointers(registers, _vicBankBase);
            _spriteRegisterSnapshots.Add(new C64SpriteRegisterSnapshot(registers, pointers, _vicBankBase));
            if (_spriteRegisterSnapshots.Count > 32)
            {
                _spriteRegisterSnapshots.RemoveAt(0);
            }
        }

        private byte[] ReadSpritePointers(byte[] registers, int vicBankBase)
        {
            var screenBase = vicBankBase + ((registers[0x18] & 0xF0) << 6);
            var pointers = new byte[8];
            for (var sprite = 0; sprite < pointers.Length; sprite++)
            {
                pointers[sprite] = ReadVicMemory((ushort)((screenBase + 0x03F8 + sprite) & 0xFFFF));
            }

            return pointers;
        }

        private static bool SpriteRegistersMatch(C64SpriteRegisterSnapshot left, byte[] right)
        {
            for (var i = 0; i <= 0x1D; i++)
            {
                if (left.Registers[i] != right[i])
                {
                    return false;
                }
            }

            for (var i = 0x25; i <= 0x2E; i++)
            {
                if (left.Registers[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }

        private readonly struct ScheduledAutostartKey
        {
            public ScheduledAutostartKey(long startCycle, long endCycle, byte portAColumnMask, byte portBRowMask)
            {
                StartCycle = startCycle;
                EndCycle = endCycle;
                PortAColumnMask = portAColumnMask;
                PortBRowMask = portBRowMask;
            }

            public long StartCycle { get; }

            public long EndCycle { get; }

            public byte PortAColumnMask { get; }

            public byte PortBRowMask { get; }
        }

        [HotPath]
        private static void WriteOutputFrame(Span<float> destination, int channelCount, int outputFrame, float sample)
        {
            var offset = outputFrame * channelCount;
            if (channelCount == 1)
            {
                destination[offset] = sample;
                return;
            }

            destination[offset] = sample;
            destination[offset + 1] = sample;
            for (var channel = 2; channel < channelCount; channel++)
            {
                destination[offset + channel] = sample;
            }
        }
    }

    internal readonly struct AudioRenderOptionsAdapter
    {
        public AudioRenderOptionsAdapter(int sampleRate, int channelCount)
        {
            SampleRate = sampleRate;
            ChannelCount = channelCount;
        }

        public int SampleRate { get; }

        public int ChannelCount { get; }
    }
}
