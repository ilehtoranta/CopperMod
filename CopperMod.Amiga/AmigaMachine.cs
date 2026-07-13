/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;
using System.Linq;

namespace CopperMod.Amiga
{
    internal readonly record struct AmigaInterruptDispatchTrace(
        int Level,
        ushort ActiveInterruptBits,
        long CpuVisibleCycle,
        long AcceptanceCycle,
        long EntryCompletedCycle,
        uint InterruptedProgramCounter,
        uint HandlerProgramCounter,
        ushort SavedStatusRegister);

    internal enum AmigaMachineProfile
    {
        A500PalCustPlayback,
        A500PalFullEmulationSkeleton,
        A500Pal512KChipOnlyBoot,
        A500Pal512KBoot
    }

    internal sealed class AmigaMachineOptions
    {
        private AmigaMachineOptions(AmigaMachineProfile profile)
        {
            Profile = profile;
        }

        public AmigaMachineProfile Profile { get; }

        public int ChipRamSize { get; private set; } = AmigaConstants.DefaultChipRamSize;

        public int ExpansionRamSize { get; private set; }

        public uint ExpansionRamBase { get; private set; } = AmigaConstants.A500BootPseudoFastRamBase;

        public int RealFastRamSize { get; private set; }

        public uint RealFastRamBase { get; private set; } = AmigaConstants.A500RealFastRamBase;

        public int FloppyDriveCount { get; private set; } = 1;

        public bool RealTimeClockEnabled { get; private set; }

        public IAmigaBusArbiter BusArbiter { get; private set; } = new ZeroWaitBusArbiter();

        public bool CaptureBusAccesses { get; private set; } = true;

        public bool LiveAgnusDma { get; private set; } = true;

        public bool LiveDisplayDma { get; private set; } = true;

        public bool HardwareSpecializationEnabled { get; private set; }

        public bool CopperQuiescentFastPathEnabled { get; private set; }

        public bool CopperQuiescentFastPathVerifyEnabled { get; private set; }

        public bool CopperQuiescentDiagnosticsEnabled { get; private set; }

        public bool DeferredCpuBusBatchEnabled { get; private set; }

        public bool DeferredCpuBusBatchVerifyEnabled { get; private set; }

        public bool CpuWaitSlotReferencePathEnabled { get; private set; }

        public int AudioDmaMinimumPeriod { get; private set; } = AmigaConstants.A500PalMinimumAudioDmaPeriod;

        public IM68kBackendCoreFactory CpuFactory { get; private set; } = AmigaM68kCoreFactory.Default;

        public M68kBackendKind CpuBackend { get; private set; } = M68kBackendKind.AccurateM68000;

        public AmigaKickstartConfiguration KickstartConfiguration { get; private set; } = AmigaKickstartConfiguration.HostShim13;

        public IReadOnlyList<AmigaHardfileConfiguration> Hardfiles { get; private set; } = Array.Empty<AmigaHardfileConfiguration>();

        public static AmigaMachineOptions ForProfile(AmigaMachineProfile profile)
        {
            var options = new AmigaMachineOptions(profile);
            if (profile == AmigaMachineProfile.A500PalCustPlayback)
            {
                options.CaptureBusAccesses = false;
                options.LiveDisplayDma = false;
                options.ExpansionRamSize = 0x0001_0000;
                options.AudioDmaMinimumPeriod = AmigaConstants.A500PalMinimumAudioDmaPeriod;
            }
            else if (profile == AmigaMachineProfile.A500Pal512KChipOnlyBoot)
            {
                options.ChipRamSize = AmigaConstants.A500BootChipRamSize;
            }
            else if (profile == AmigaMachineProfile.A500Pal512KBoot)
            {
                options.ChipRamSize = AmigaConstants.A500BootChipRamSize;
                options.ExpansionRamSize = AmigaConstants.A500BootPseudoFastRamSize;
                options.FloppyDriveCount = 2;
                options.RealTimeClockEnabled = true;
            }

            return options;
        }

        public AmigaMachineOptions WithCpu(IM68kBackendCoreFactory factory, M68kBackendKind backend)
        {
            CpuFactory = factory ?? throw new ArgumentNullException(nameof(factory));
            CpuBackend = backend;
            return this;
        }

        public AmigaMachineOptions WithKickstart(AmigaKickstartConfiguration configuration)
        {
            KickstartConfiguration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            return this;
        }

        public AmigaMachineOptions WithBusArbiter(IAmigaBusArbiter arbiter)
        {
            BusArbiter = arbiter ?? throw new ArgumentNullException(nameof(arbiter));
            return this;
        }

        public AmigaMachineOptions WithBusAccessLogging(bool enabled)
        {
            CaptureBusAccesses = enabled;
            return this;
        }

        public AmigaMachineOptions WithLiveAgnusDma(bool enabled)
        {
            LiveAgnusDma = enabled;
            return this;
        }

        public AmigaMachineOptions WithLiveDisplayDma(bool enabled)
        {
            LiveDisplayDma = enabled;
            return this;
        }

        public AmigaMachineOptions WithHardwareSpecialization(bool enabled)
        {
            HardwareSpecializationEnabled = enabled;
            return this;
        }

        public AmigaMachineOptions WithCopperQuiescentFastPath(bool enabled, bool verify)
        {
            CopperQuiescentFastPathEnabled = enabled;
            CopperQuiescentFastPathVerifyEnabled = verify;
            return this;
        }

        public AmigaMachineOptions WithCopperQuiescentDiagnostics(bool enabled)
        {
            CopperQuiescentDiagnosticsEnabled = enabled;
            return this;
        }

        public AmigaMachineOptions WithDeferredCpuBusBatch(bool enabled, bool verify)
        {
            DeferredCpuBusBatchEnabled = enabled;
            DeferredCpuBusBatchVerifyEnabled = verify;
            return this;
        }

        public AmigaMachineOptions WithCpuWaitSlotReferencePath(bool enabled)
        {
            CpuWaitSlotReferencePathEnabled = enabled;
            return this;
        }

        public AmigaMachineOptions WithAudioDmaMinimumPeriod(int period)
        {
            if (period <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(period), period, "Audio DMA minimum period must be positive.");
            }

            AudioDmaMinimumPeriod = period;
            return this;
        }

        public AmigaMachineOptions WithChipRam(int size)
        {
            if (size <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size), size, "Chip RAM size must be positive.");
            }

            if (size > AmigaConstants.MaxChipRamSize)
            {
                throw new ArgumentOutOfRangeException(nameof(size), size, "Chip RAM size cannot exceed the custom-chip DMA address space.");
            }

            if (!IsPowerOfTwo(size))
            {
                throw new ArgumentOutOfRangeException(nameof(size), size, "Chip RAM size must be a power of two so custom-chip DMA address masking is well-defined.");
            }

            ChipRamSize = size;
            return this;
        }

        public AmigaMachineOptions WithExpansionRam(int size, uint baseAddress = AmigaConstants.A500BootPseudoFastRamBase)
        {
            if (size < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size), size, "Expansion RAM size cannot be negative.");
            }

            ExpansionRamSize = size;
            ExpansionRamBase = baseAddress;
            return this;
        }

        public AmigaMachineOptions WithRealFastRam(int size, uint baseAddress = AmigaConstants.A500RealFastRamBase)
        {
            if (size < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size), size, "Real fast RAM size cannot be negative.");
            }

            RealFastRamSize = size;
            RealFastRamBase = baseAddress;
            return this;
        }

        public AmigaMachineOptions WithFloppyDriveCount(int count)
        {
            if (count is < 1 or > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "The connected floppy drive count must be between 1 and 4.");
            }

            FloppyDriveCount = count;
            return this;
        }

        public AmigaMachineOptions WithRealTimeClock(bool enabled)
        {
            RealTimeClockEnabled = enabled;
            return this;
        }

        public AmigaMachineOptions WithHardfiles(IEnumerable<AmigaHardfileConfiguration> hardfiles)
        {
            Hardfiles = hardfiles?.ToArray() ?? Array.Empty<AmigaHardfileConfiguration>();
            return this;
        }

        private static bool IsPowerOfTwo(int value)
        {
            return value > 0 && (value & (value - 1)) == 0;
        }
    }

    internal sealed class AmigaMachine : IDisposable
    {
        private List<AmigaInterruptDispatchTrace>? _interruptDispatchTrace;
        private int _interruptDispatchTraceCapacity;

        public AmigaMachine(AmigaMachineOptions options)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            Bus = new AmigaBus(
                options.ChipRamSize,
                options.BusArbiter,
                options.ExpansionRamSize,
                options.ExpansionRamBase,
                options.FloppyDriveCount,
                options.CaptureBusAccesses,
                options.LiveAgnusDma,
                options.LiveDisplayDma,
                options.AudioDmaMinimumPeriod,
                options.RealFastRamSize,
                options.RealFastRamBase,
                options.HardwareSpecializationEnabled,
                options.RealTimeClockEnabled,
                null,
                options.Hardfiles,
                options.CopperQuiescentFastPathEnabled,
                options.CopperQuiescentFastPathVerifyEnabled,
                options.DeferredCpuBusBatchEnabled,
                options.DeferredCpuBusBatchVerifyEnabled,
                options.CopperQuiescentDiagnosticsEnabled,
                options.CpuWaitSlotReferencePathEnabled);
            Cpu = options.CpuFactory.Create(options.CpuBackend, Bus);
            if (Bus.DiskDivergenceTraceEnabled)
            {
                Bus.ConfigureDiskDivergenceTrace(
                    options.CpuBackend.ToString(),
                    () => new AmigaDiskTraceCpuContext(
                        Cpu.State.ProgramCounter,
                        Cpu.State.LastInstructionProgramCounter,
                        Cpu.State.LastOpcode,
                        Cpu.State.Cycles));
            }

            Kickstart = new AmigaKickstartHost(options.KickstartConfiguration);
        }

        public AmigaMachineOptions Options { get; }

        public AmigaMachineProfile Profile => Options.Profile;

        public AmigaBus Bus { get; }

        public IM68kCore Cpu { get; }

        public AmigaKickstartHost Kickstart { get; }

        internal IReadOnlyList<AmigaInterruptDispatchTrace> InterruptDispatchTrace
            => _interruptDispatchTrace ?? (IReadOnlyList<AmigaInterruptDispatchTrace>)Array.Empty<AmigaInterruptDispatchTrace>();

        internal void CaptureInterruptDispatchTrace(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _interruptDispatchTrace = new List<AmigaInterruptDispatchTrace>(capacity);
            _interruptDispatchTraceCapacity = capacity;
        }

        public void Dispose()
        {
            Cpu.Dispose();
            Bus.Dispose();
        }

        public void ResetHardware()
        {
            Bus.Reset();
            Cpu.Reset(0, 0);
        }

        public bool DispatchPendingHardwareInterrupt()
        {
            var ciaEvents = Bus.DrainCiaInterrupts();
            for (var i = 0; i < ciaEvents.Count; i++)
            {
                var ciaEvent = ciaEvents[i];
                var cia = Bus.GetCia(ciaEvent.Cia);
                if ((cia.PendingInterrupts & cia.InterruptMask) == 0)
                {
                    continue;
                }

                var intreqBit = ciaEvent.Cia == AmigaCiaId.A
                    ? AmigaConstants.IntreqPorts
                    : AmigaConstants.IntreqExternal;
                Bus.RequestHardwareInterrupt(intreqBit, ciaEvent.Cycle);
            }

            var level = Bus.Paula.GetHighestCpuVisibleInterruptLevel(Cpu.State.Cycles);
            if (level <= 0)
            {
                return false;
            }

            var interruptMask = (Cpu.State.StatusRegister >> 8) & 0x07;
            if (level <= interruptMask)
            {
                return false;
            }

            var acceptanceCycle = Cpu.State.Cycles;
            var interruptedProgramCounter = Cpu.State.ProgramCounter;
            var savedStatusRegister = Cpu.State.StatusRegister;
            var activeInterruptBits = Bus.Paula.ActiveInterruptBits;
            var cpuVisibleCycle = Bus.Paula.GetCpuInterruptReleaseCycleForLevel(level, acceptanceCycle) ?? acceptanceCycle;
            Cpu.RequestInterrupt(level, GetAutovectorAddress(level));
            var trace = _interruptDispatchTrace;
            if (trace != null && trace.Count < _interruptDispatchTraceCapacity)
            {
                trace.Add(new AmigaInterruptDispatchTrace(
                    level,
                    activeInterruptBits,
                    cpuVisibleCycle,
                    acceptanceCycle,
                    Cpu.State.Cycles,
                    interruptedProgramCounter,
                    Cpu.State.ProgramCounter,
                    savedStatusRegister));
            }

            return true;
        }

        private static uint GetAutovectorAddress(int level)
        {
            return (uint)((24 + level) * 4);
        }
    }
}
