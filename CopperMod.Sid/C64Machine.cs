using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Copper6510;

namespace CopperMod.Sid
{
    internal sealed class C64Machine : IMos6510Bus
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
        private const ushort KernalCintAddress = 0xFF81;
        private const ushort KernalIoInitAddress = 0xFF84;
        private const ushort KernalRamTasAddress = 0xFF87;
        private const ushort KernalRestorAddress = 0xFF8A;
        private const ushort KernalChrOutAddress = 0xFFD2;
        private const ushort KernalChrInAddress = 0xFFCF;
        private const ushort KernalStopAddress = 0xFFE1;
        private const ushort KernalGetInAddress = 0xFFE4;
        private const ushort BasicExecuteProgramAddress = 0xA7AE;
        private const ushort PalNtscFlagAddress = 0x02A6;
        private const ushort TextScreenStart = 0x0400;
        private const int TextScreenColumns = 40;
        private const int TextScreenRows = 25;
        private const byte DefaultTextColor = 0x0E;
        private const long RomBasicBootCycles = 2_500_000;
        // SidPlayFP psiddrv reaches the first VBI play call 3236 instruction
        // cycles after init entry; CopperMod accounts through the completed
        // init RTS, so the equivalent post-init phase is seven cycles later.
        internal const int SidPlayFpPsidVbiFirstPlayCycleAfterInitEntry = 3_243;
        private byte _processorPortDirection;
        private byte _processorPortValue;
        private long _hardwareCycle;
        private bool _irqLine;
        private bool _cia2NmiLine;
        private bool _nmiPending;
        private C64InterruptSource _lastInterruptSource;
        private BasicRsidRunner? _basicRunner;
        private bool _psidPlayActive;
        private long _psidPlayOffsetCycles;
        private long _psidCiaTimerAIntervalCycles;
        private bool _psidCiaTimerATouched;
        private bool _cpuInstructionActive;
        private bool _serviceCpuInterrupts = true;
        private long? _busCycleOverride;
        private bool _cpuBusCallbackThisCycle;
        private C64InterruptSource _cpuCycleIrqSource;
        private C64InterruptSource _pendingIrqSource;
        private int _vicBankBase;
        private readonly List<ScheduledAutostartKey> _autostartKeys = new List<ScheduledAutostartKey>();
        private readonly HashSet<C64Key> _pressedKeys = new HashSet<C64Key>();
        private readonly Queue<byte> _hostInputQueue = new Queue<byte>();
        private int _basicInputScheduleIndex;
        private int _romBasicInputScheduleIndex;
        private bool _realRomInstalled;
        private bool _kernalInterruptServiceActive;
        private bool _clearKernalInterruptServiceAfterInstruction;
        private readonly byte[] _videoVicRegisters = new byte[0x40];
        private readonly List<C64SpriteRegisterSnapshot> _spriteRegisterSnapshots = new List<C64SpriteRegisterSnapshot>(32);
        private long _videoFrameNumber;
        private int _kernalCursorColumn;
        private int _kernalCursorRow;
        private byte _kernalTextColor = DefaultTextColor;

        public C64Machine(
            SidModule module,
            SidFilterProfileId filterProfile = SidFilterProfileId.Auto,
            SidEmulationProfile sidEmulationProfile = SidEmulationProfile.Balanced,
            string? c64RomPath = null)
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
            InstallRoms(c64RomPath);
        }

        public Mos6510 Cpu { get; }

        public SidSystem Sid { get; }

        public SidEmulationProfile SidEmulationProfile { get; }

        public C64ClockProfile Clock => _clock;

        public long Cycle => _hardwareCycle;

        public byte[] Ram => _ram;

        public IReadOnlyList<SidRegisterWrite> SidWrites => Sid.Writes;

        public IReadOnlyList<DigimaxWrite> DigimaxWrites => _digimax.Writes;

        internal CpuBusTrace? CpuBusTrace { get; set; }

        public long PsidCiaTimerAIntervalCycles => _psidCiaTimerAIntervalCycles;

        public long PsidPlayOffsetCycles => _psidPlayOffsetCycles;

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
            _psidPlayActive = false;
            _psidPlayOffsetCycles = 0;
            _psidCiaTimerAIntervalCycles = GetDefaultPsidCiaTimerAIntervalCycles();
            _psidCiaTimerATouched = false;
            _cpuInstructionActive = false;
            _serviceCpuInterrupts = true;
            _busCycleOverride = null;
            _cpuBusCallbackThisCycle = false;
            _cpuCycleIrqSource = C64InterruptSource.None;
            _pendingIrqSource = C64InterruptSource.None;
            var psidUsesCiaTiming = UsesPsidCiaTiming(subSongIndex);
            var psidOrRsid = _module.Kind == SidFileKind.Psid || _module.IsRsid;
            _cia1.Reset(
                defaultTimerA60Hz: psidOrRsid,
                cpuCyclesPerSecond: _clock.CpuCyclesPerSecond,
                defaultTimerAIrq: _module.IsRsid || psidUsesCiaTiming);
            _cia2.Reset(defaultTimerA60Hz: false, _clock.CpuCyclesPerSecond);
            UpdateVicBankBase();
            _vic.Reset();
            Sid.Reset();
            InstallPsidEnvironment(psidUsesCiaTiming);
            _digimax.Reset();
            _easyFlash?.Reset();
            _pressedKeys.Clear();
            _hostInputQueue.Clear();
            _basicInputScheduleIndex = 0;
            _romBasicInputScheduleIndex = 0;
            _spriteRegisterSnapshots.Clear();
            _videoFrameNumber = 0;
            _kernalCursorColumn = 0;
            _kernalCursorRow = 0;
            _kernalTextColor = DefaultTextColor;
            _kernalInterruptServiceActive = false;
            _clearKernalInterruptServiceAfterInstruction = false;
            if (_module.IsCartridge)
            {
                InstallKernalRamVectors();
                ExecuteHardwareReset();
                RefreshInterruptLines();
                return;
            }

            if (_module.IsBasicRsid && _module.PreferRomBasic && _realRomInstalled)
            {
                StartRomBasicProgram();
                return;
            }

            LoadPayload();
            InstallRsidEnvironment();
            if (_module.IsBasicRsid)
            {
                Cpu.InitializeState(IdleLoopAddress);
                _basicRunner = new BasicRsidRunner(this, _module.EffectiveLoadAddress);
                Cpu.ResetCycles();
                _hardwareCycle = 0;
                Sid.ResetClock();
                RefreshInterruptLines();
                return;
            }

            Cpu.InitializeState(_module.InitAddress);
            PreparePsidRoutineCall(_module.InitAddress);
            Cpu.BeginSubroutine(_module.InitAddress, (byte)subSongIndex);
            RunUntilSubroutineReturn(250_000, serviceInterrupts: _module.Kind != SidFileKind.Psid);
            var initRoutineCycles = Cpu.Cycles;
            Sid.AdvanceTo(Cpu.Cycles);
            CapturePsidInitCiaTimerInterval(subSongIndex);
            _psidPlayOffsetCycles = ComputePsidPlayOffsetCycles(psidUsesCiaTiming, initRoutineCycles);
            if (_module.RunsContinuously && Cpu.ProgramCounter == 0xFFFF)
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
            if (_module.RunsContinuously ||
                _module.PlayAddress == 0 ||
                _psidPlayActive ||
                _psidPlayOffsetCycles <= 0)
            {
                BeginFrame();
                RunCycles(_clock.CyclesPerFrame);
                return;
            }

            var frameStartCycle = Cpu.Cycles;
            var frameEndCycle = frameStartCycle + _clock.CyclesPerFrame;
            var offset = Math.Min(_psidPlayOffsetCycles, _clock.CyclesPerFrame);
            AdvanceSidOnly(offset);
            BeginFrame();
            if (Cpu.Cycles < frameEndCycle)
            {
                RunCycles(frameEndCycle - Cpu.Cycles);
            }
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
                if (!_psidPlayActive)
                {
                    PreparePsidRoutineCall(_module.PlayAddress);
                    Cpu.BeginSubroutine(_module.PlayAddress, 0);
                    _psidPlayActive = true;
                }

                RunUntilSubroutineReturn(250_000, serviceInterrupts: false);
                if (Cpu.ProgramCounter == 0xFFFF || Cpu.Halted)
                {
                    _psidPlayActive = false;
                    FinishPsidPlayRoutine();
                }
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
                ParkReturnedContinuousRoutine();
                var before = Cpu.Cycles;
                var executed = ExecuteCpuInstruction(serviceInterrupts: true);
                ParkReturnedContinuousRoutine();
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
        public void RunUntilSubroutineReturn(long maxCycles, bool serviceInterrupts = true)
        {
            var target = Cpu.Cycles + maxCycles;
            while (Cpu.Cycles < target && !Cpu.Halted && Cpu.ProgramCounter != 0xFFFF)
            {
                var executed = ExecuteCpuInstruction(serviceInterrupts);
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

            var frameStartCycle = Cpu.Cycles;
            var frameEndCycle = frameStartCycle + tickCycles;
            var psidPlayCycle = frameStartCycle + GetPsidPlayOffsetForTick(tickCycles);
            var psidPlayPending = !_psidPlayActive && !_module.RunsContinuously && _module.PlayAddress != 0;
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
                else
                {
                    AdvancePsidFramePlaybackTo(
                        targetCycle,
                        psidPlayCycle,
                        ref psidPlayPending);
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
                else
                {
                    AdvancePsidFramePlaybackTo(
                        frameEndCycle,
                        psidPlayCycle,
                        ref psidPlayPending);
                }
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
            PreparePsidRoutineCall(_module.PlayAddress);
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

        private void CapturePsidInitCiaTimerInterval(int subSongIndex)
        {
            if (_module.Kind == SidFileKind.Psid &&
                UsesPsidCiaTiming(subSongIndex) &&
                _psidCiaTimerATouched)
            {
                _psidCiaTimerAIntervalCycles = Math.Max(1L, _cia1.TimerALatch);
            }

            _psidCiaTimerATouched = false;
        }

        private long ComputePsidPlayOffsetCycles(bool usesCiaTiming, long initRoutineCycles)
        {
            if (_module.Kind != SidFileKind.Psid || _module.PlayAddress == 0 || usesCiaTiming)
            {
                return 0;
            }

            var playCycle = (long)SidPlayFpPsidVbiFirstPlayCycleAfterInitEntry;
            while (playCycle < initRoutineCycles)
            {
                playCycle += _clock.CyclesPerFrame;
            }

            return Math.Max(0, playCycle - initRoutineCycles);
        }

        private long GetPsidPlayOffsetForTick(long tickCycles)
        {
            if (_module.RunsContinuously || _module.PlayAddress == 0)
            {
                return 0;
            }

            return Math.Clamp(_psidPlayOffsetCycles, 0, Math.Max(0, tickCycles));
        }

        [HotPath]
        private void RunPsidPlayUntil(long targetCycle, ref bool active)
        {
            // Direct PSID play calls are driven by CopperMod's scheduler; servicing
            // emulated IRQs inside the routine can vector PlaySID-era tunes into
            // uninitialized RAM after they execute CLI during setup.
            while (active && Cpu.Cycles < targetCycle && !Cpu.Halted && Cpu.ProgramCounter != 0xFFFF)
            {
                var before = Cpu.Cycles;
                var executed = ExecuteCpuInstruction(serviceInterrupts: false);
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
        private void AdvancePsidFramePlaybackTo(
            long targetCycle,
            long playCycle,
            ref bool playPending)
        {
            if (!_psidPlayActive && playPending && targetCycle >= playCycle)
            {
                if (Cpu.Cycles < playCycle)
                {
                    AdvanceSidOnly(playCycle - Cpu.Cycles);
                }

                _psidPlayActive = BeginPsidFrame();
                playPending = false;
            }

            if (_psidPlayActive)
            {
                var active = true;
                RunPsidPlayUntil(targetCycle, ref active);
                if (!active)
                {
                    _psidPlayActive = false;
                    FinishPsidPlayRoutine();
                }

                if (Cpu.Cycles < targetCycle)
                {
                    AdvanceSidOnly(targetCycle - Cpu.Cycles);
                }

                return;
            }

            AdvanceSidOnly(Math.Max(0, targetCycle - Cpu.Cycles));
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
            _hardwareCycle = target;
        }

        [HotPath]
        public byte Read(ushort address)
        {
            return Read(address, Mos6510BusAccessKind.Read);
        }

        [HotPath]
        public byte Read(ushort address, Mos6510BusAccessKind kind = Mos6510BusAccessKind.Read)
        {
            _cpuBusCallbackThisCycle = true;
            var busCycle = CurrentCpuBusCycle();
            var readCycle = busCycle;
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

            if (TryReadResidentKernalInterruptStub(address, kind, out value))
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
        public byte Read(ushort address, int cycleOffset, Mos6510BusAccessKind kind = Mos6510BusAccessKind.Read)
        {
            var targetCycle = Cpu.Cycles + Math.Max(0, cycleOffset);
            AdvanceHardwareTo(targetCycle);
            _busCycleOverride = targetCycle;
            try
            {
                return Read(address, kind);
            }
            finally
            {
                _busCycleOverride = null;
            }
        }

        [HotPath]
        public void Write(ushort address, byte value, Mos6510BusAccessKind kind = Mos6510BusAccessKind.Write)
        {
            _cpuBusCallbackThisCycle = true;
            var busCycle = CurrentCpuBusCycle();
            var writeCycle = busCycle;
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
        public void Write(ushort address, byte value, int cycleOffset)
        {
            _ = cycleOffset;
            Write(address, value, Mos6510BusAccessKind.Write);
        }

        [HotPath]
        internal byte NativeRead(ushort address)
        {
            return Read(address);
        }

        [HotPath]
        internal void NativeWrite(ushort address, byte value)
        {
            Write(address, value);
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
                case KernalChrInAddress:
                case KernalGetInAddress:
                    HostKernalInput(address == KernalChrInAddress);
                    AdvanceNativeCycles(64);
                    return true;
                case KernalStopAddress:
                    Cpu.SetAccumulatorAndFlags(0xFF);
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

        private void InstallPsidEnvironment(bool usesCiaTiming)
        {
            if (_module.Kind != SidFileKind.Psid)
            {
                return;
            }

            _ram[PalNtscFlagAddress] = GetVideoStandardFlag();
            ApplyPsidCompatibilityRegisterDefaults(usesCiaTiming);
            if (_module.PlayAddress == 0)
            {
                InstallKernalInterruptStubs();
            }
        }

        [HotPath]
        private void ParkReturnedContinuousRoutine()
        {
            if (_module.RunsContinuously && Cpu.ProgramCounter == 0xFFFF)
            {
                Cpu.ProgramCounter = IdleLoopAddress;
            }
        }

        private void ApplyPsidCompatibilityRegisterDefaults(bool usesCiaTiming)
        {
            _vic.Write(0x1A, 0x00);
            _vic.Write(0x19, 0x71);
            _cia1.Write(0x0D, 0x7F);
            _cia2.Write(0x0D, 0x7F);

            Sid.TryWrite((ushort)(SidConstants.DefaultSidBaseAddress + 0x18), 0x0F, 0);
            Sid.ClearCapturedWrites();

            var (timerLow, timerHigh) = GetDefaultPsidCiaTimerALatchBytes();
            _cia1.Write(0x04, timerLow);
            _cia1.Write(0x05, timerHigh);

            _vic.Write(0x11, 0x1B);
            _vic.Write(0x12, 0x00);

            if (usesCiaTiming)
            {
                _cia1.Write(0x0D, 0x81);
                _cia1.Write(0x0E, 0x01);
            }
            else
            {
                _vic.Write(0x1A, 0x81);
            }

            RefreshInterruptLines();
        }

        private (byte Low, byte High) GetDefaultPsidCiaTimerALatchBytes()
        {
            return _clock.RasterLines == 263
                ? ((byte)0x95, (byte)0x42)
                : ((byte)0x25, (byte)0x40);
        }

        private byte GetVideoStandardFlag()
        {
            return _clock.RasterLines == 263 ? (byte)0x00 : (byte)0x01;
        }

        private void PreparePsidRoutineCall(ushort address)
        {
            if (_module.Kind != SidFileKind.Psid)
            {
                return;
            }

            var bankRegister = GetPsidRoutineBankRegister(address);
            _processorPortValue = bankRegister;
            _ram[0x0001] = bankRegister;
        }

        private static byte GetPsidRoutineBankRegister(ushort address)
        {
            if (address < 0xA000)
            {
                return 0x37;
            }

            if (address < 0xD000)
            {
                return 0x36;
            }

            return address >= 0xE000 ? (byte)0x35 : (byte)0x34;
        }

        private void StartRomBasicProgram()
        {
            ExecuteHardwareReset();
            RefreshInterruptLines();

            RunRomBasicBootUntilInput(RomBasicBootCycles);

            LoadPayload();
            InitializeBasicProgramPointers();

            Cpu.InitializeState(BasicExecuteProgramAddress);
            _hardwareCycle = 0;
            Sid.Reset();
            Sid.ResetClock();
            RefreshInterruptLines();
        }

        private void RunRomBasicBootUntilInput(long maxCycles)
        {
            var target = Cpu.Cycles + maxCycles;
            while (Cpu.Cycles < target &&
                   !Cpu.Halted &&
                   Cpu.ProgramCounter != KernalChrInAddress &&
                   Cpu.ProgramCounter != KernalGetInAddress)
            {
                var before = Cpu.Cycles;
                _ = ExecuteCpuInstruction(serviceInterrupts: true);
                if (Cpu.Cycles == before)
                {
                    break;
                }
            }
        }

        private void InitializeBasicProgramPointers()
        {
            var start = _module.EffectiveLoadAddress;
            var end = (ushort)(start + _module.Payload.Length);
            WriteWord(0x002B, start); // TXTTAB
            WriteWord(0x002D, end);   // VARTAB
            WriteWord(0x002F, end);   // ARYTAB
            WriteWord(0x0031, end);   // STREND
            WriteWord(0x007A, (ushort)(start - 1)); // TXTPTR, CHRGET pre-increments.
            WriteWord(0x0039, 0x0000); // CURLIN: program mode before the first line header is read.
            WriteWord(0x003B, 0xFFFF); // OLDLIN
            WriteWord(0x003D, 0x0000); // OLDTXT
        }

        private void InstallRsidEnvironment()
        {
            if (!_module.IsRsid)
            {
                return;
            }

            InstallKernalInterruptStubs();
        }

        private void InstallKernalInterruptStubs()
        {
            InstallKernalRamVectors();
            WriteWord(0xFFFE, KernalIrqEntryAddress);
            WriteWord(0xFFFA, KernalNmiEntryAddress);

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
            WriteKernalStub(
                KernalIrqEntryAddress,
                0x48,                         // PHA
                0x8A,                         // TXA
                0x48,                         // PHA
                0x98,                         // TYA
                0x48,                         // PHA
                0xBA,                         // TSX
                0xBD, 0x04, 0x01,             // LDA $0104,X; stacked status after A/X/Y saves
                0x29, 0x10,                   // AND #$10; BRK flag
                0xD0, 0x03,                   // BNE break cleanup
                0x6C, 0x14, 0x03,             // JMP ($0314)
                0x68,                         // PLA
                0xA8,                         // TAY
                0x68,                         // PLA
                0xAA,                         // TAX
                0x68,                         // PLA
                0x68,                         // PLA; discard status
                0x68,                         // PLA; discard return low
                0x68,                         // PLA; discard return high
                0x4C,
                (byte)(IdleLoopAddress & 0xFF),
                (byte)(IdleLoopAddress >> 8));
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

        private void InstallRoms(string? c64RomPath)
        {
            _realRomInstalled = false;
            if (string.IsNullOrWhiteSpace(c64RomPath))
            {
                InstallMinimalRoms();
                return;
            }

            var rom = File.ReadAllBytes(c64RomPath);
            switch (rom.Length)
            {
                case 0x4000:
                    Array.Copy(rom, 0, _basicRom, 0, _basicRom.Length);
                    Array.Copy(rom, _basicRom.Length, _kernalRom, 0, _kernalRom.Length);
                    C64CharacterRom.Install(_characterRom);
                    _realRomInstalled = true;
                    break;
                case 0x5000:
                    Array.Copy(rom, 0, _basicRom, 0, _basicRom.Length);
                    Array.Copy(rom, _basicRom.Length, _kernalRom, 0, _kernalRom.Length);
                    Array.Copy(rom, _basicRom.Length + _kernalRom.Length, _characterRom, 0, _characterRom.Length);
                    _realRomInstalled = true;
                    break;
                default:
                    throw new InvalidDataException(
                        "C64 ROM image must be 16 KiB BASIC+KERNAL or 20 KiB BASIC+KERNAL+CHAR.");
            }
        }

        [HotPath]
        private bool TryReadResidentKernalInterruptStub(ushort address, Mos6510BusAccessKind kind, out byte value)
        {
            value = 0;
            if (address < KernalRomStart)
            {
                return false;
            }

            if (!_kernalInterruptServiceActive && kind == Mos6510BusAccessKind.OpcodeFetch)
            {
                if (address == KernalIrqEntryAddress && IsInstalledRamVector(0xFFFE, KernalIrqEntryAddress))
                {
                    _kernalInterruptServiceActive = true;
                }
                else if (address == KernalNmiEntryAddress && IsInstalledRamVector(0xFFFA, KernalNmiEntryAddress))
                {
                    _kernalInterruptServiceActive = true;
                }
            }

            if (!_kernalInterruptServiceActive || !IsResidentKernalInterruptStubAddress(address))
            {
                return false;
            }

            value = _kernalRom[address - KernalRomStart];
            if (kind == Mos6510BusAccessKind.OpcodeFetch && IsResidentKernalInterruptReturnOpcode(address))
            {
                _clearKernalInterruptServiceAfterInstruction = true;
            }

            return true;
        }

        [HotPath]
        private bool IsInstalledRamVector(ushort vectorAddress, ushort target)
        {
            return _ram[vectorAddress] == (byte)(target & 0xFF) &&
                   _ram[(ushort)(vectorAddress + 1)] == (byte)(target >> 8);
        }

        [HotPath]
        private static bool IsResidentKernalInterruptStubAddress(ushort address)
        {
            return IsInRange(address, KernalIrqHandlerAddress, 15) ||
                   IsInRange(address, 0xEA7E, 9) ||
                   IsInRange(address, KernalNmiEntryAddress, 3) ||
                   IsInRange(address, KernalNmiHandlerAddress, 4) ||
                   IsInRange(address, KernalUpdateClockAddress, 15) ||
                   IsInRange(address, KernalIrqEntryAddress, 30) ||
                   address == RtiStubAddress;
        }

        [HotPath]
        private static bool IsResidentKernalInterruptReturnOpcode(ushort address)
        {
            return address == KernalIrqExitAddress + 5 ||
                   address == KernalNmiHandlerAddress + 3 ||
                   address == RtiStubAddress;
        }

        [HotPath]
        private static bool IsInRange(ushort address, ushort start, int length)
        {
            return address >= start && address < start + length;
        }

        private bool TryHandleKernalOpcodeFetch(ushort address, Mos6510BusAccessKind kind, out byte value)
        {
            value = 0;
            if (kind != Mos6510BusAccessKind.OpcodeFetch)
            {
                return false;
            }

            if (_module.PreferRomBasic && _realRomInstalled)
            {
                switch (address)
                {
                    case KernalChrInAddress:
                    case KernalGetInAddress:
                        HostKernalInput(address == KernalChrInAddress);
                        value = 0x60;
                        return true;
                    case KernalStopAddress:
                        Cpu.SetAccumulatorAndFlags(0xFF);
                        value = 0x60;
                        return true;
                    default:
                        return false;
                }
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
                case KernalChrInAddress:
                case KernalGetInAddress:
                    HostKernalInput(address == KernalChrInAddress);
                    value = 0x60;
                    return true;
                case KernalStopAddress:
                    Cpu.SetAccumulatorAndFlags(0xFF);
                    value = 0x60;
                    return true;
                default:
                    return false;
            }
        }

        private void HostKernalInput(bool blocking)
        {
            if (!TryDequeueRomBasicInput(out var value) && blocking)
            {
                if (_romBasicInputScheduleIndex < _autostartKeys.Count)
                {
                    var nextCycle = _autostartKeys[_romBasicInputScheduleIndex].StartCycle;
                    if (nextCycle > Cpu.Cycles)
                    {
                        AdvanceNativeCycles(nextCycle - Cpu.Cycles);
                    }

                    _ = TryDequeueRomBasicInput(out value);
                }
                else
                {
                    AdvanceNativeCycles(512);
                }
            }

            Cpu.SetAccumulatorAndFlags(value);
        }

        private bool TryDequeueRomBasicInput(out byte value)
        {
            if (_hostInputQueue.Count > 0)
            {
                value = _hostInputQueue.Dequeue();
                return true;
            }

            while (_romBasicInputScheduleIndex < _autostartKeys.Count)
            {
                var key = _autostartKeys[_romBasicInputScheduleIndex];
                if (Cpu.Cycles < key.StartCycle)
                {
                    break;
                }

                _romBasicInputScheduleIndex++;
                if (key.Key == C64Key.Return)
                {
                    _hostInputQueue.Enqueue(0x0D);
                }
                else if (key.BasicInputText != null)
                {
                    foreach (var ch in key.BasicInputText)
                    {
                        _hostInputQueue.Enqueue((byte)ch);
                    }
                }

                if (_hostInputQueue.Count > 0)
                {
                    value = _hostInputQueue.Dequeue();
                    return true;
                }
            }

            value = 0;
            return false;
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
        private int ExecuteCpuInstruction(bool serviceInterrupts)
        {
            var start = Cpu.Cycles;
            _cpuInstructionActive = true;
            _serviceCpuInterrupts = serviceInterrupts;
            while (true)
            {
                PrepareCpuCycle();
                var result = Cpu.StepCycle();
                HandleCpuCycleResult(result);
                if (result.CompletedOperation == Mos6510OperationKind.Instruction ||
                    result.CompletedOperation == Mos6510OperationKind.Halted)
                {
                    break;
                }
            }

            AdvanceHardwareTo(Cpu.Cycles);
            _cpuInstructionActive = false;
            _serviceCpuInterrupts = true;
            CompleteKernalInterruptServiceInstruction();
            return checked((int)(Cpu.Cycles - start));
        }

        private void ExecuteHardwareReset()
        {
            Cpu.InitializeState(0);
            Cpu.StackPointer = 0;
            _hardwareCycle = 0;
            _serviceCpuInterrupts = false;
            Cpu.SetResetLine(true);
            for (var cycle = 0; cycle < 2; cycle++)
            {
                PrepareCpuCycle();
                _ = Cpu.StepCycle();
            }

            Cpu.SetResetLine(false);
            while (true)
            {
                PrepareCpuCycle();
                var result = Cpu.StepCycle();
                if (result.CompletedOperation == Mos6510OperationKind.Reset)
                {
                    break;
                }
            }

            AdvanceHardwareTo(Cpu.Cycles);
            _serviceCpuInterrupts = true;
            Sid.AdvanceTo(Cpu.Cycles);
        }

        [HotPath]
        private void PrepareCpuCycle()
        {
            AdvanceHardwareTo(Cpu.Cycles);
            RefreshInterruptLines();
            _cpuBusCallbackThisCycle = false;
            _cpuCycleIrqSource = _vic.IrqLine
                ? C64InterruptSource.Vic
                : _cia1.InterruptLine
                    ? C64InterruptSource.Cia1
                    : C64InterruptSource.None;
            if (_cpuCycleIrqSource != C64InterruptSource.None)
            {
                _pendingIrqSource = _cpuCycleIrqSource;
            }
            Cpu.SetReadyLine(!_module.IsRsid || !_vic.IsCpuReadBlocked());
            Cpu.SetBusAvailable(!_module.IsRsid || !_vic.IsCpuWriteBlocked());
        }

        [HotPath]
        private void HandleCpuCycleResult(Mos6510CycleResult result)
        {
            if (!_cpuBusCallbackThisCycle)
            {
                var cycle = Cpu.Cycles - 1;
                var vicOwned = _module.IsRsid && _vic.IsCpuWriteBlocked();
                CpuBusTrace?.Add(new CpuBusTraceFrame(
                    cycle,
                    Cpu.LastOpcode,
                    address: null,
                    null,
                    kind: null,
                    cpuAdvanced: result.CpuAdvanced,
                    ready: !_module.IsRsid || !_vic.IsCpuReadBlocked(),
                    busAvailable: !vicOwned,
                    vicOwned: vicOwned));
            }

            if (result.StartedOperation == Mos6510OperationKind.Nmi)
            {
                _nmiPending = false;
                _lastInterruptSource = C64InterruptSource.Cia2;
            }
            else if (result.StartedOperation == Mos6510OperationKind.Irq)
            {
                _lastInterruptSource = _cpuCycleIrqSource != C64InterruptSource.None
                    ? _cpuCycleIrqSource
                    : _pendingIrqSource;
                _pendingIrqSource = C64InterruptSource.None;
            }

            if ((result.CompletedOperation == Mos6510OperationKind.Irq ||
                 result.CompletedOperation == Mos6510OperationKind.Nmi) &&
                (Cpu.ProgramCounter == KernalIrqEntryAddress || Cpu.ProgramCounter == KernalNmiEntryAddress))
            {
                _kernalInterruptServiceActive = true;
            }
        }

        [HotPath]
        private void CompleteKernalInterruptServiceInstruction()
        {
            if (!_clearKernalInterruptServiceAfterInstruction)
            {
                return;
            }

            _kernalInterruptServiceActive = false;
            _clearKernalInterruptServiceAfterInstruction = false;
        }

        [HotPath]
        private long CurrentCpuBusCycle()
        {
            return _busCycleOverride ?? _hardwareCycle;
        }

        [HotPath]
        private void CaptureCpuBusRead(long busCycle, ushort address, byte value, Mos6510BusAccessKind kind)
        {
            var blocked = _cpuInstructionActive && _module.IsRsid && _vic.IsCpuReadBlocked();
            CpuBusTrace?.Add(new CpuBusTraceFrame(
                busCycle,
                kind == Mos6510BusAccessKind.OpcodeFetch ? value : Cpu.LastOpcode,
                address,
                value,
                kind,
                cpuAdvanced: !blocked,
                ready: !blocked,
                busAvailable: true,
                vicOwned: false));
        }

        [HotPath]
        private void CaptureCpuBusWrite(long busCycle, ushort address, byte value, Mos6510BusAccessKind kind)
        {
            CpuBusTrace?.Add(new CpuBusTraceFrame(
                busCycle,
                Cpu.LastOpcode,
                address,
                value,
                kind,
                cpuAdvanced: true,
                ready: !_vic.IsCpuReadBlocked(),
                busAvailable: true,
                vicOwned: false));
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
            Cpu.SetIrqLine(_serviceCpuInterrupts && _irqLine);
            if (_serviceCpuInterrupts)
            {
                Cpu.SetNmiLine(_cia2NmiLine);
            }
        }

        [HotPath]
        private bool IsIoVisible()
        {
            if (_module.Kind == SidFileKind.Psid)
            {
                return true;
            }

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

        private bool UsesPsidCiaTiming(int subSongIndex)
        {
            if (_module.Kind != SidFileKind.Psid)
            {
                return false;
            }

            var bitIndex = Math.Min(Math.Max(subSongIndex, 0), 31);
            return ((_module.Speed >> bitIndex) & 1U) != 0;
        }

        internal void ScheduleAutostartKey(string key, TimeSpan delay, TimeSpan hold)
        {
            var clampedDelay = delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
            var clampedHold = hold <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : hold;
            var start = SidIntegerMath.TimeSpanToCycles(clampedDelay, _clock.CpuCyclesPerSecond);
            var length = Math.Max(1, SidIntegerMath.TimeSpanToCycles(clampedHold, _clock.CpuCyclesPerSecond));
            var c64Key = ParseAutostartKey(key);
            var keyPosition = C64KeyboardMatrix.GetPosition(c64Key);
            _autostartKeys.Add(new ScheduledAutostartKey(
                start,
                start + length,
                keyPosition.ColumnMask,
                keyPosition.RowMask,
                c64Key,
                GetBasicInputText(c64Key)));
        }

        internal bool TryReadScheduledBasicInputLine(out string line)
        {
            line = string.Empty;
            if (_basicInputScheduleIndex >= _autostartKeys.Count)
            {
                return false;
            }

            var builder = new StringBuilder();
            var waitUntilCycle = Cpu.Cycles;
            for (var i = _basicInputScheduleIndex; i < _autostartKeys.Count; i++)
            {
                var key = _autostartKeys[i];
                waitUntilCycle = Math.Max(waitUntilCycle, key.EndCycle);
                if (key.Key == C64Key.Return)
                {
                    _basicInputScheduleIndex = i + 1;
                    if (waitUntilCycle > Cpu.Cycles)
                    {
                        AdvanceNativeCycles(waitUntilCycle - Cpu.Cycles);
                    }

                    line = builder.ToString();
                    return true;
                }

                if (key.BasicInputText != null)
                {
                    builder.Append(key.BasicInputText);
                }
            }

            return false;
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
            key = key.Trim();
            if (key.Length == 1 && TryParseCharacterKey(key[0], out var characterKey))
            {
                return characterKey;
            }

            return key.ToLowerInvariant() switch
            {
                "return" or "enter" => C64Key.Return,
                "space" => C64Key.Space,
                "f1" => C64Key.F1,
                "f3" => C64Key.F3,
                "f5" => C64Key.F5,
                "f7" => C64Key.F7,
                "runstop" or "run-stop" or "stop" => C64Key.RunStop,
                "delete" or "del" => C64Key.Delete,
                "home" => C64Key.Home,
                "cursorright" or "right" => C64Key.CursorRight,
                "cursordown" or "down" => C64Key.CursorDown,
                _ => throw new ArgumentException("Unsupported C64 autostart key: " + key, nameof(key))
            };
        }

        private static bool TryParseCharacterKey(char value, out C64Key key)
        {
            key = value switch
            {
                '0' => C64Key.Zero,
                '1' => C64Key.One,
                '2' => C64Key.Two,
                '3' => C64Key.Three,
                '4' => C64Key.Four,
                '5' => C64Key.Five,
                '6' => C64Key.Six,
                '7' => C64Key.Seven,
                '8' => C64Key.Eight,
                '9' => C64Key.Nine,
                ' ' => C64Key.Space,
                '+' => C64Key.Plus,
                '-' => C64Key.Minus,
                '.' => C64Key.Period,
                ':' => C64Key.Colon,
                '@' => C64Key.At,
                ',' => C64Key.Comma,
                '*' => C64Key.Asterisk,
                ';' => C64Key.Semicolon,
                '=' => C64Key.Equals,
                '/' => C64Key.Slash,
                _ => char.ToUpperInvariant(value) switch
                {
                    'A' => C64Key.A,
                    'B' => C64Key.B,
                    'C' => C64Key.C,
                    'D' => C64Key.D,
                    'E' => C64Key.E,
                    'F' => C64Key.F,
                    'G' => C64Key.G,
                    'H' => C64Key.H,
                    'I' => C64Key.I,
                    'J' => C64Key.J,
                    'K' => C64Key.K,
                    'L' => C64Key.L,
                    'M' => C64Key.M,
                    'N' => C64Key.N,
                    'O' => C64Key.O,
                    'P' => C64Key.P,
                    'Q' => C64Key.Q,
                    'R' => C64Key.R,
                    'S' => C64Key.S,
                    'T' => C64Key.T,
                    'U' => C64Key.U,
                    'V' => C64Key.V,
                    'W' => C64Key.W,
                    'X' => C64Key.X,
                    'Y' => C64Key.Y,
                    'Z' => C64Key.Z,
                    _ => default
                }
            };
            return key != default || value == '0';
        }

        private static string? GetBasicInputText(C64Key key)
        {
            return key switch
            {
                C64Key.A => "A",
                C64Key.B => "B",
                C64Key.C => "C",
                C64Key.D => "D",
                C64Key.E => "E",
                C64Key.F => "F",
                C64Key.G => "G",
                C64Key.H => "H",
                C64Key.I => "I",
                C64Key.J => "J",
                C64Key.K => "K",
                C64Key.L => "L",
                C64Key.M => "M",
                C64Key.N => "N",
                C64Key.O => "O",
                C64Key.P => "P",
                C64Key.Q => "Q",
                C64Key.R => "R",
                C64Key.S => "S",
                C64Key.T => "T",
                C64Key.U => "U",
                C64Key.V => "V",
                C64Key.W => "W",
                C64Key.X => "X",
                C64Key.Y => "Y",
                C64Key.Z => "Z",
                C64Key.Zero => "0",
                C64Key.One => "1",
                C64Key.Two => "2",
                C64Key.Three => "3",
                C64Key.Four => "4",
                C64Key.Five => "5",
                C64Key.Six => "6",
                C64Key.Seven => "7",
                C64Key.Eight => "8",
                C64Key.Nine => "9",
                C64Key.Space => " ",
                C64Key.Plus => "+",
                C64Key.Minus => "-",
                C64Key.Period => ".",
                C64Key.Colon => ":",
                C64Key.At => "@",
                C64Key.Comma => ",",
                C64Key.Asterisk => "*",
                C64Key.Semicolon => ";",
                C64Key.Equals => "=",
                C64Key.Slash => "/",
                _ => null
            };
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
            public ScheduledAutostartKey(
                long startCycle,
                long endCycle,
                byte portAColumnMask,
                byte portBRowMask,
                C64Key key,
                string? basicInputText)
            {
                StartCycle = startCycle;
                EndCycle = endCycle;
                PortAColumnMask = portAColumnMask;
                PortBRowMask = portBRowMask;
                Key = key;
                BasicInputText = basicInputText;
            }

            public long StartCycle { get; }

            public long EndCycle { get; }

            public byte PortAColumnMask { get; }

            public byte PortBRowMask { get; }

            public C64Key Key { get; }

            public string? BasicInputText { get; }
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
