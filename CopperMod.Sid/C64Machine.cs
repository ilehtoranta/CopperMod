using System;
using System.Collections.Generic;

namespace CopperMod.Sid
{
    internal sealed class C64Machine : ICpuBus
    {
        private readonly byte[] _ram = new byte[65536];
        private readonly byte[] _kernalRom = new byte[0x2000];
        private readonly byte[] _basicRom = new byte[0x2000];
        private readonly byte[] _characterRom = new byte[0x1000];
        private readonly SidModule _module;
        private readonly C64ClockProfile _clock;
        private readonly Cia6526 _cia1 = new Cia6526();
        private readonly Cia6526 _cia2 = new Cia6526();
        private readonly VicII _vic;
        private const ushort KernalRomStart = 0xE000;
        private const ushort BasicRomStart = 0xA000;
        private const ushort RamIrqVector = 0x0314;
        private const ushort RamNmiVector = 0x0318;
        private const ushort KernalIrqEntryAddress = 0xFF80;
        private const ushort KernalNmiEntryAddress = 0xFF88;
        private const ushort RtiStubAddress = 0xFF92;
        private const ushort IdleLoopAddress = 0xFF94;
        private const ushort KernalIrqHandlerAddress = 0xEA31;
        private const ushort KernalIrqExitAddress = 0xEA81;
        private const ushort KernalNmiHandlerAddress = 0xFE47;
        private const ushort KernalIrqVectorAddress = 0xFF48;
        private const ushort KernalIrqUserVectorJumpAddress = 0xFF58;
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

        public C64Machine(SidModule module, SidFilterProfileId filterProfile = SidFilterProfileId.Auto)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));
            _clock = C64ClockProfile.FromSidClock(module.Clock);
            _vic = new VicII(_clock);
            Sid = new SidSystem(module.Chips, module.EffectiveChipModel, _clock.CpuClockHz, filterProfile);
            Cpu = new Mos6510(this);
            InstallMinimalRoms();
        }

        public Mos6510 Cpu { get; }

        public SidSystem Sid { get; }

        public C64ClockProfile Clock => _clock;

        public long Cycle => Cpu.Cycles;

        public byte[] Ram => _ram;

        public IReadOnlyList<SidRegisterWrite> SidWrites => Sid.Writes;

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

        public void Reset(int subSongIndex)
        {
            Array.Clear(_ram);
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
            _cia1.Reset(defaultTimerA60Hz: _module.IsRsid, _clock.CpuClockHz);
            _cia2.Reset(defaultTimerA60Hz: false, _clock.CpuClockHz);
            _vic.Reset();
            Sid.Reset();
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

        public void RunFrame()
        {
            BeginFrame();
            RunCycles(_clock.CyclesPerFrame);
        }

        public void BeginFrame()
        {
            if (_module.IsRsid)
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

        public void RunCycles(long cycleCount)
        {
            var target = Cpu.Cycles + cycleCount;
            if (_basicRunner != null)
            {
                _basicRunner.RunUntil(target);
                AdvanceHardwareTo(target);
                Sid.AdvanceTo(target);
                return;
            }

            while (Cpu.Cycles < target && !Cpu.Halted)
            {
                var before = Cpu.Cycles;
                var executed = Cpu.ExecuteInstruction();
                AdvanceHardwareTo(Cpu.Cycles);
                ServicePendingInterrupts();
                if (Cpu.Cycles == before)
                {
                    Cpu.ResetCycles();
                    _hardwareCycle = 0;
                    break;
                }
            }

            AdvanceHardwareTo(target);
            Sid.AdvanceTo(target);
        }

        public void RunUntilSubroutineReturn(long maxCycles)
        {
            var target = Cpu.Cycles + maxCycles;
            while (Cpu.Cycles < target && !Cpu.Halted && Cpu.ProgramCounter != 0xFFFF)
            {
                var executed = Cpu.ExecuteInstruction();
                AdvanceHardwareTo(Cpu.Cycles);
                ServicePendingInterrupts();
            }
        }

        public void RenderFrame(Span<float> destination, AudioRenderOptionsAdapter options, long? cycleCount = null)
        {
            var frames = destination.Length / options.ChannelCount;
            var tickCycles = Math.Max(1, cycleCount ?? _clock.CyclesPerFrame);
            if (_basicRunner != null)
            {
                RenderBasicFrame(destination, options, frames, tickCycles);
                return;
            }

            var frameStartCycle = Cpu.Cycles;
            var frameEndCycle = frameStartCycle + tickCycles;
            var cyclesPerOutputFrame = tickCycles / (double)frames;
            var psidPlayActive = BeginPsidFrame();
            var psidPlayStarted = psidPlayActive;
            var outputFrame = 0;
            while (outputFrame < frames)
            {
                var targetCycle = frameStartCycle + (long)Math.Round((outputFrame + 1) * cyclesPerOutputFrame);
                if (_module.IsRsid)
                {
                    RunCycles(Math.Max(0, targetCycle - Cpu.Cycles));
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

                var sample = Sid.RenderSample(targetCycle);
                var offset = outputFrame * options.ChannelCount;
                if (options.ChannelCount == 1)
                {
                    destination[offset] = sample;
                }
                else
                {
                    destination[offset] = sample;
                    destination[offset + 1] = sample;
                    for (var channel = 2; channel < options.ChannelCount; channel++)
                    {
                        destination[offset + channel] = sample;
                    }
                }

                outputFrame++;
            }

            if (Cpu.Cycles < frameEndCycle)
            {
                if (_module.IsRsid)
                {
                    RunCycles(frameEndCycle - Cpu.Cycles);
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
        }

        private void RenderBasicFrame(Span<float> destination, AudioRenderOptionsAdapter options, int frames, long tickCycles)
        {
            var frameStartCycle = Cpu.Cycles;
            var frameEndCycle = frameStartCycle + tickCycles;
            var cyclesPerOutputFrame = tickCycles / (double)frames;
            _basicRunner!.RunUntil(frameEndCycle);
            AdvanceHardwareTo(frameEndCycle);

            for (var outputFrame = 0; outputFrame < frames; outputFrame++)
            {
                var targetCycle = frameStartCycle + (long)Math.Round((outputFrame + 1) * cyclesPerOutputFrame);
                var sample = Sid.RenderSample(targetCycle);
                var offset = outputFrame * options.ChannelCount;
                if (options.ChannelCount == 1)
                {
                    destination[offset] = sample;
                }
                else
                {
                    destination[offset] = sample;
                    destination[offset + 1] = sample;
                    for (var channel = 2; channel < options.ChannelCount; channel++)
                    {
                        destination[offset + channel] = sample;
                    }
                }
            }
        }

        private bool BeginPsidFrame()
        {
            if (_module.IsRsid || _module.PlayAddress == 0)
            {
                return false;
            }

            _psidCiaTimerATouched = false;
            Cpu.BeginSubroutine(_module.PlayAddress, 0);
            return true;
        }

        private void FinishPsidPlayRoutine()
        {
            if (_module.IsRsid || !_psidCiaTimerATouched)
            {
                return;
            }

            _psidCiaTimerAIntervalCycles = Math.Max(1L, _cia1.DebugState.TimerALatch);
            _psidCiaTimerATouched = false;
        }

        private void RunPsidPlayUntil(long targetCycle, ref bool active)
        {
            while (active && Cpu.Cycles < targetCycle && !Cpu.Halted && Cpu.ProgramCounter != 0xFFFF)
            {
                var before = Cpu.Cycles;
                var executed = Cpu.ExecuteInstruction();
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

        public byte Read(ushort address)
        {
            return Read(address, cycleOffset: 0);
        }

        public byte Read(ushort address, int cycleOffset = 0)
        {
            AdvanceHardwareTo(Cpu.Cycles + Math.Max(0, cycleOffset));
            if (address == 0x0000)
            {
                return _processorPortDirection;
            }

            if (address == 0x0001)
            {
                return ProcessorPortEffectiveValue;
            }

            if (IsIoVisible() && address >= 0xD000 && address <= 0xDFFF)
            {
                return ReadIo(address);
            }

            if (IsCharacterRomVisible() && address >= 0xD000 && address <= 0xDFFF)
            {
                return _characterRom[address - 0xD000];
            }

            if (_module.IsRsid && IsBasicVisible() && address >= 0xA000 && address <= 0xBFFF)
            {
                return _basicRom[address - BasicRomStart];
            }

            if (_module.IsRsid && IsKernalVisible() && address >= 0xE000)
            {
                return _kernalRom[address - KernalRomStart];
            }

            return _ram[address];
        }

        public void Write(ushort address, byte value, int cycleOffset)
        {
            var writeCycle = Cpu.Cycles + Math.Max(0, cycleOffset);
            AdvanceHardwareTo(writeCycle);
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

        internal byte NativeRead(ushort address)
        {
            return Read(address, cycleOffset: 0);
        }

        internal void NativeWrite(ushort address, byte value)
        {
            Write(address, value, cycleOffset: 0);
        }

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
                case 0xFFD2: // CHROUT
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

            WriteWord(RamIrqVector, KernalIrqHandlerAddress);
            WriteWord(RamNmiVector, KernalNmiHandlerAddress);
            WriteWord(0xFFFE, KernalIrqEntryAddress);
            WriteWord(0xFFFA, KernalNmiEntryAddress);

            WriteRamStub(KernalIrqEntryAddress, 0x48, 0x8A, 0x48, 0x98, 0x48, 0x6C, 0x14, 0x03);
            WriteRamStub(KernalNmiEntryAddress, 0x6C, 0x18, 0x03);
            WriteRamStub(RtiStubAddress, 0x40); // RTI

            _ram[IdleLoopAddress + 0] = 0xEA; // NOP
            _ram[IdleLoopAddress + 1] = 0xEA; // NOP
            _ram[IdleLoopAddress + 2] = 0x4C; // JMP idle loop
            _ram[IdleLoopAddress + 3] = (byte)(IdleLoopAddress & 0xFF);
            _ram[IdleLoopAddress + 4] = (byte)(IdleLoopAddress >> 8);
        }

        private void InstallMinimalRoms()
        {
            Array.Fill(_basicRom, (byte)0x60); // RTS for unimplemented BASIC calls.
            Array.Fill(_kernalRom, (byte)0x60); // RTS for unimplemented KERNAL calls.
            for (var i = 0; i < _characterRom.Length; i++)
            {
                _characterRom[i] = (byte)(0x80 | (i & 0x7F));
            }

            WriteKernalStub(KernalIrqHandlerAddress, 0xAD, 0x0D, 0xDC, 0xAD, 0x19, 0xD0, 0x8D, 0x19, 0xD0, 0x4C, 0x81, 0xEA);
            WriteKernalStub(0xEA7E, 0xAD, 0x0D, 0xDC, 0x4C, 0x81, 0xEA); // LDA $DC0D; JMP IRQ exit
            WriteKernalStub(KernalIrqExitAddress, 0x68, 0xA8, 0x68, 0xAA, 0x68, 0x40); // PLA/TAY, PLA/TAX, PLA, RTI
            WriteKernalStub(KernalNmiHandlerAddress, 0xAD, 0x0D, 0xDD, 0x40);
            WriteKernalStub(KernalIrqVectorAddress, 0x4C, 0x80, 0xFF); // JMP $FF80
            WriteKernalStub(KernalIrqUserVectorJumpAddress, 0x6C, 0x14, 0x03); // JMP ($0314)
            WriteKernalStub(KernalIrqEntryAddress, 0x48, 0x8A, 0x48, 0x98, 0x48, 0x6C, 0x14, 0x03);
            WriteKernalStub(KernalNmiEntryAddress, 0x6C, 0x18, 0x03);
            WriteKernalStub(RtiStubAddress, 0x40);
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
                var vicIrq = _vic.Tick();
                var cia2NmiLine = _cia2.Tick();
                _irqLine = cia1Irq || vicIrq;
                if (cia2NmiLine && !_cia2NmiLine)
                {
                    _nmiPending = true;
                }

                _cia2NmiLine = cia2NmiLine;
            }
        }

        private void RefreshInterruptLines()
        {
            _irqLine = _cia1.DebugState.InterruptLine || _vic.DebugState.IrqLine;
            var cia2Line = _cia2.DebugState.InterruptLine;
            if (cia2Line && !_cia2NmiLine)
            {
                _nmiPending = true;
            }

            _cia2NmiLine = cia2Line;
        }

        private void ServicePendingInterrupts()
        {
            RefreshInterruptLines();
            if (_nmiPending)
            {
                _nmiPending = false;
                _lastInterruptSource = C64InterruptSource.Cia2;
                Cpu.TryRequestNmi();
                AdvanceHardwareTo(Cpu.Cycles);
                RefreshInterruptLines();
                return;
            }

            if (_irqLine && Cpu.TryRequestIrq())
            {
                _lastInterruptSource = _vic.DebugState.IrqLine
                    ? C64InterruptSource.Vic
                    : C64InterruptSource.Cia1;
                AdvanceHardwareTo(Cpu.Cycles);
                RefreshInterruptLines();
            }
        }

        private bool IsIoVisible()
        {
            var value = ProcessorPortEffectiveValue;
            return (value & 0x04) != 0 && (value & 0x03) != 0;
        }

        private bool IsKernalVisible()
        {
            return _module.IsRsid && (ProcessorPortEffectiveValue & 0x02) != 0;
        }

        private bool IsBasicVisible()
        {
            return _module.IsRsid && (ProcessorPortEffectiveValue & 0x03) == 0x03;
        }

        private bool IsCharacterRomVisible()
        {
            var value = ProcessorPortEffectiveValue;
            return _module.IsRsid && (value & 0x04) == 0 && (value & 0x03) != 0;
        }

        private byte ProcessorPortEffectiveValue => (byte)((_processorPortValue & _processorPortDirection) | (~_processorPortDirection & 0xFF));

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

        private byte ReadIo(ushort address)
        {
            if (address >= 0xD000 && address <= 0xD3FF)
            {
                return _vic.Read((byte)address);
            }

            if (Sid.TryRead(address, out var sidValue))
            {
                return sidValue;
            }

            if (address >= 0xDC00 && address <= 0xDCFF)
            {
                return _cia1.Read((byte)address);
            }

            if (address >= 0xDD00 && address <= 0xDDFF)
            {
                return _cia2.Read((byte)address);
            }

            return 0;
        }

        private void WriteIo(ushort address, byte value, long writeCycle)
        {
            if (address >= 0xD000 && address <= 0xD3FF)
            {
                _vic.Write((byte)address, value);
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
                return;
            }

            if (address >= 0xDD00 && address <= 0xDDFF)
            {
                _cia2.Write((byte)address, value);
            }
        }

        private long GetDefaultPsidCiaTimerAIntervalCycles()
        {
            return Math.Max(1, (long)Math.Round(_clock.CpuClockHz / SidConstants.CiaTimerRefreshHz));
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
