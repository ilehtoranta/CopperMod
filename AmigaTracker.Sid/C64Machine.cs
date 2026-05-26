using System;
using System.Collections.Generic;

namespace AmigaTracker.Sid
{
    internal sealed class C64Machine : ICpuBus
    {
        private readonly byte[] _ram = new byte[65536];
        private readonly SidModule _module;
        private readonly C64ClockProfile _clock;
        private readonly Cia6526 _cia1 = new Cia6526();
        private readonly Cia6526 _cia2 = new Cia6526();
        private readonly VicII _vic;
        private const ushort RamIrqVector = 0x0314;
        private const ushort RamNmiVector = 0x0318;
        private const ushort IrqTrampolineAddress = 0xFF80;
        private const ushort NmiTrampolineAddress = 0xFF83;
        private const ushort RtiStubAddress = 0xFF86;
        private const ushort IdleLoopAddress = 0xFF90;
        private byte _processorPortDirection;
        private byte _processorPortValue;
        private long _irqHoldoffCycles;

        public C64Machine(SidModule module)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));
            _clock = C64ClockProfile.FromSidClock(module.Clock);
            _vic = new VicII(_clock);
            Sid = new SidSystem(module.Chips, module.EffectiveChipModel);
            Cpu = new Mos6510(this);
        }

        public Mos6510 Cpu { get; }

        public SidSystem Sid { get; }

        public C64ClockProfile Clock => _clock;

        public long Cycle => Cpu.Cycles;

        public byte[] Ram => _ram;

        public IReadOnlyList<SidRegisterWrite> SidWrites => Sid.Writes;

        public void Reset(int subSongIndex)
        {
            Array.Clear(_ram);
            _processorPortDirection = 0x2F;
            _processorPortValue = (byte)SidConstants.DefaultBankRegister;
            _irqHoldoffCycles = 0;
            _cia1.Reset(defaultTimerA60Hz: _module.IsRsid);
            _cia2.Reset(defaultTimerA60Hz: false);
            _vic.Reset();
            Sid.Reset();
            LoadPayload();
            InstallRsidEnvironment();
            Cpu.Reset(_module.InitAddress);
            Cpu.BeginSubroutine(_module.InitAddress, (byte)subSongIndex);
            RunUntilSubroutineReturn(250_000);
            Sid.AdvanceTo(Cpu.Cycles);
            if (_module.IsRsid && Cpu.ProgramCounter == 0xFFFF)
            {
                Cpu.ProgramCounter = IdleLoopAddress;
            }

            Cpu.ResetCycles();
            Sid.ResetClock();
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
            }
        }

        public void RunCycles(long cycleCount)
        {
            var target = Cpu.Cycles + cycleCount;
            while (Cpu.Cycles < target && !Cpu.Halted)
            {
                var before = Cpu.Cycles;
                var executed = Cpu.ExecuteInstruction();
                TickHardware(Math.Max(1, executed));
                if (Cpu.Cycles == before)
                {
                    Cpu.ResetCycles();
                    break;
                }
            }

            Sid.AdvanceTo(target);
        }

        public void RunUntilSubroutineReturn(long maxCycles)
        {
            var target = Cpu.Cycles + maxCycles;
            while (Cpu.Cycles < target && !Cpu.Halted && Cpu.ProgramCounter != 0xFFFF)
            {
                var executed = Cpu.ExecuteInstruction();
                TickHardware(Math.Max(1, executed));
            }
        }

        public void RenderFrame(Span<float> destination, AudioRenderOptionsAdapter options, long? cycleCount = null)
        {
            var frames = destination.Length / options.ChannelCount;
            var tickCycles = Math.Max(1, cycleCount ?? _clock.CyclesPerFrame);
            var frameStartCycle = Cpu.Cycles;
            var frameEndCycle = frameStartCycle + tickCycles;
            var cyclesPerOutputFrame = tickCycles / (double)frames;
            var psidPlayActive = BeginPsidFrame();
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
        }

        private bool BeginPsidFrame()
        {
            if (_module.IsRsid || _module.PlayAddress == 0)
            {
                return false;
            }

            Cpu.BeginSubroutine(_module.PlayAddress, 0);
            return true;
        }

        private void RunPsidPlayUntil(long targetCycle, ref bool active)
        {
            while (active && Cpu.Cycles < targetCycle && !Cpu.Halted && Cpu.ProgramCounter != 0xFFFF)
            {
                var before = Cpu.Cycles;
                var executed = Cpu.ExecuteInstruction();
                TickHardware(Math.Max(1, executed));
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
            if (address == 0x0000)
            {
                return _processorPortDirection;
            }

            if (address == 0x0001)
            {
                return _processorPortValue;
            }

            if (IsIoVisible() && address >= 0xD000 && address <= 0xDFFF)
            {
                return ReadIo(address);
            }

            return _ram[address];
        }

        public void Write(ushort address, byte value, int cycleOffset)
        {
            var writeCycle = Cpu.Cycles + Math.Max(0, cycleOffset);
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

            WriteWord(RamIrqVector, RtiStubAddress);
            WriteWord(RamNmiVector, RtiStubAddress);
            WriteWord(0xFFFE, IrqTrampolineAddress);
            WriteWord(0xFFFA, NmiTrampolineAddress);

            _ram[IrqTrampolineAddress + 0] = 0x6C; // JMP ($0314)
            _ram[IrqTrampolineAddress + 1] = (byte)(RamIrqVector & 0xFF);
            _ram[IrqTrampolineAddress + 2] = (byte)(RamIrqVector >> 8);
            _ram[NmiTrampolineAddress + 0] = 0x6C; // JMP ($0318)
            _ram[NmiTrampolineAddress + 1] = (byte)(RamNmiVector & 0xFF);
            _ram[NmiTrampolineAddress + 2] = (byte)(RamNmiVector >> 8);
            _ram[RtiStubAddress] = 0x40; // RTI

            _ram[IdleLoopAddress + 0] = 0xEA; // NOP
            _ram[IdleLoopAddress + 1] = 0xEA; // NOP
            _ram[IdleLoopAddress + 2] = 0x4C; // JMP $FF90
            _ram[IdleLoopAddress + 3] = (byte)(IdleLoopAddress & 0xFF);
            _ram[IdleLoopAddress + 4] = (byte)(IdleLoopAddress >> 8);
        }

        private void WriteWord(ushort address, ushort value)
        {
            _ram[address] = (byte)value;
            _ram[(ushort)(address + 1)] = (byte)(value >> 8);
        }

        private void TickHardware(int cycles)
        {
            for (var i = 0; i < cycles; i++)
            {
                var irq = _cia1.Tick() | _vic.Tick();
                _ = _cia2.Tick();
                if (_irqHoldoffCycles > 0)
                {
                    _irqHoldoffCycles--;
                    continue;
                }

                if (irq)
                {
                    Cpu.RequestIrq();
                    _irqHoldoffCycles = 16;
                }
            }
        }

        private bool IsIoVisible()
        {
            return (_processorPortValue & 0x04) != 0;
        }

        private byte ReadIo(ushort address)
        {
            if (address >= 0xD000 && address <= 0xD3FF)
            {
                return _vic.Read((byte)address);
            }

            foreach (var chip in Sid.Chips)
            {
                if (address >= chip.BaseAddress && address < chip.BaseAddress + 0x20)
                {
                    return chip.Registers[(address - chip.BaseAddress) & 0x1F];
                }
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
                _cia1.Write((byte)address, value);
                return;
            }

            if (address >= 0xDD00 && address <= 0xDDFF)
            {
                _cia2.Write((byte)address, value);
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
