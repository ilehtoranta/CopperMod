/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;
using System.Linq;

namespace CopperMod.Amiga.Runtime
{
    internal enum AgnusModel
    {
        Ocs,
        Ecs
    }

    internal enum DeniseModel
    {
        Ocs,
        Ecs
    }

    internal enum VideoStandard
    {
        Pal,
        Ntsc
    }

    internal readonly record struct AmigaChipset(
        AgnusModel Agnus,
        DeniseModel Denise,
        VideoStandard VideoStandard)
    {
        public static AmigaChipset OcsPal { get; } = new(
            AgnusModel.Ocs,
            DeniseModel.Ocs,
            VideoStandard.Pal);

        public static AmigaChipset OcsNtsc { get; } = new(
            AgnusModel.Ocs,
            DeniseModel.Ocs,
            VideoStandard.Ntsc);

        public static AmigaChipset EcsPal { get; } = new(
            AgnusModel.Ecs,
            DeniseModel.Ecs,
            VideoStandard.Pal);

        public static AmigaChipset EcsNtsc { get; } = new(
            AgnusModel.Ecs,
            DeniseModel.Ecs,
            VideoStandard.Ntsc);
    }

    internal readonly record struct InterruptDispatchTrace(
        int Level,
        ushort ActiveInterruptBits,
        long CpuVisibleCycle,
		long CpuSampleCycle,
        long AcceptanceCycle,
		long EntryCompletedCycle,
		uint InterruptedProgramCounter,
		uint HandlerProgramCounter,
		ushort SavedStatusRegister,
		uint DataRegister3,
		M68000PrefetchDiagnosticState? PrefetchBefore,
		M68000PrefetchDiagnosticState? PrefetchAfter);

    internal sealed record InterruptBusPhaseTrace(
        int Level,
        long CpuVisibleCycle,
        long AcceptanceCycle,
        List<AmigaCpuBusPhaseTrace> Phases);

    internal enum MachineProfile
    {
        A500PalCustPlayback,
        A500PalFullEmulationSkeleton,
        A500Pal512KChipOnlyBoot,
        A500Pal512KBoot
    }

    internal sealed class MachineOptions
    {
        private MachineOptions(MachineProfile profile)
        {
            Profile = profile;
        }

        public MachineProfile Profile { get; }

        public AmigaChipset Chipset { get; private set; } = AmigaChipset.OcsPal;

        public int ChipRamSize { get; private set; } = AmigaConstants.DefaultChipRamSize;

        public int ExpansionRamSize { get; private set; }

        public uint ExpansionRamBase { get; private set; } = AmigaConstants.A500BootPseudoFastRamBase;

        public int RealFastRamSize { get; private set; }

        public uint RealFastRamBase { get; private set; } = AmigaConstants.A500RealFastRamBase;

        public long RtgVramSize { get; private set; }

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

        public KickstartConfiguration KickstartConfiguration { get; private set; } = KickstartConfiguration.HostShim13;

        public IReadOnlyList<AmigaHardfileConfiguration> Hardfiles { get; private set; } = Array.Empty<AmigaHardfileConfiguration>();

        public static MachineOptions ForProfile(MachineProfile profile)
        {
            var options = new MachineOptions(profile);
            if (profile == MachineProfile.A500PalCustPlayback)
            {
                options.CaptureBusAccesses = false;
                options.LiveDisplayDma = false;
                options.ExpansionRamSize = 0x0001_0000;
                options.AudioDmaMinimumPeriod = AmigaConstants.A500PalMinimumAudioDmaPeriod;
            }
            else if (profile == MachineProfile.A500Pal512KChipOnlyBoot)
            {
                options.ChipRamSize = AmigaConstants.A500BootChipRamSize;
            }
            else if (profile == MachineProfile.A500Pal512KBoot)
            {
                options.ChipRamSize = AmigaConstants.A500BootChipRamSize;
                options.ExpansionRamSize = AmigaConstants.A500BootPseudoFastRamSize;
                options.FloppyDriveCount = 2;
                options.RealTimeClockEnabled = true;
            }

            return options;
        }

        public MachineOptions WithCpu(IM68kBackendCoreFactory factory, M68kBackendKind backend)
        {
            CpuFactory = factory ?? throw new ArgumentNullException(nameof(factory));
            CpuBackend = backend;
            return this;
        }

        public MachineOptions WithChipset(AmigaChipset chipset)
        {
            Chipset = chipset;
            return this;
        }

        public MachineOptions WithKickstart(KickstartConfiguration configuration)
        {
            KickstartConfiguration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            return this;
        }

        public MachineOptions WithBusArbiter(IAmigaBusArbiter arbiter)
        {
            BusArbiter = arbiter ?? throw new ArgumentNullException(nameof(arbiter));
            return this;
        }

        public MachineOptions WithBusAccessLogging(bool enabled)
        {
            CaptureBusAccesses = enabled;
            return this;
        }

        public MachineOptions WithLiveAgnusDma(bool enabled)
        {
            LiveAgnusDma = enabled;
            return this;
        }

        public MachineOptions WithLiveDisplayDma(bool enabled)
        {
            LiveDisplayDma = enabled;
            return this;
        }

        public MachineOptions WithHardwareSpecialization(bool enabled)
        {
            HardwareSpecializationEnabled = enabled;
            return this;
        }

        public MachineOptions WithCopperQuiescentFastPath(bool enabled, bool verify)
        {
            CopperQuiescentFastPathEnabled = enabled;
            CopperQuiescentFastPathVerifyEnabled = verify;
            return this;
        }

        public MachineOptions WithCopperQuiescentDiagnostics(bool enabled)
        {
            CopperQuiescentDiagnosticsEnabled = enabled;
            return this;
        }

        public MachineOptions WithDeferredCpuBusBatch(bool enabled, bool verify)
        {
            DeferredCpuBusBatchEnabled = enabled;
            DeferredCpuBusBatchVerifyEnabled = verify;
            return this;
        }

        public MachineOptions WithCpuWaitSlotReferencePath(bool enabled)
        {
            CpuWaitSlotReferencePathEnabled = enabled;
            return this;
        }

        public MachineOptions WithAudioDmaMinimumPeriod(int period)
        {
            if (period <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(period), period, "Audio DMA minimum period must be positive.");
            }

            AudioDmaMinimumPeriod = period;
            return this;
        }

        public MachineOptions WithChipRam(int size)
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

        public MachineOptions WithExpansionRam(int size, uint baseAddress = AmigaConstants.A500BootPseudoFastRamBase)
        {
            if (size < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size), size, "Expansion RAM size cannot be negative.");
            }

            ExpansionRamSize = size;
            ExpansionRamBase = baseAddress;
            return this;
        }

        public MachineOptions WithRealFastRam(int size, uint? baseAddress = null)
        {
            if (size < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size), size, "Autoconfig fast RAM size cannot be negative.");
            }

            if (size == 0)
            {
                RealFastRamSize = 0;
                RealFastRamBase = baseAddress ?? AmigaConstants.A500RealFastRamBase;
                return this;
            }

            var selectedBase = baseAddress ?? AutoconfigFastRamBoard.GetDefaultBase(size);
            AutoconfigFastRamBoard.ValidateBase(size, selectedBase);
            RealFastRamSize = size;
            RealFastRamBase = selectedBase;
            return this;
        }

        public MachineOptions WithRtgVram(long size)
        {
            if (size < 0 || size > 2L * 1024 * 1024 * 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(size), size, "RTG VRAM size must be between 0 and 2 GiB.");
            }

            RtgVramSize = size;
            return this;
        }

        public MachineOptions WithFloppyDriveCount(int count)
        {
            if (count is < 1 or > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "The connected floppy drive count must be between 1 and 4.");
            }

            FloppyDriveCount = count;
            return this;
        }

        public MachineOptions WithRealTimeClock(bool enabled)
        {
            RealTimeClockEnabled = enabled;
            return this;
        }

        public MachineOptions WithHardfiles(IEnumerable<AmigaHardfileConfiguration> hardfiles)
        {
            Hardfiles = hardfiles?.ToArray() ?? Array.Empty<AmigaHardfileConfiguration>();
            return this;
        }

        private static bool IsPowerOfTwo(int value)
        {
            return value > 0 && (value & (value - 1)) == 0;
        }
    }

    internal sealed class Machine : IDisposable
    {
        private List<InterruptDispatchTrace>? _interruptDispatchTrace;
        private int _interruptDispatchTraceCapacity;
        private List<InterruptBusPhaseTrace>? _interruptBusPhaseTrace;
        private InterruptBusPhaseTrace? _activeInterruptBusPhaseTrace;
        private int _interruptBusPhaseWindowCycles;

        public Machine(MachineOptions options)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            if (options.RealFastRamSize > 8 * 1024 * 1024 &&
                options.CpuBackend is not (
                    M68kBackendKind.AccurateM68020 or
                    M68kBackendKind.AccurateM68030 or
                    M68kBackendKind.AccurateM68040 or
                    M68kBackendKind.JitM68040))
            {
                throw new InvalidOperationException(
                    $"{options.RealFastRamSize / (1024 * 1024)} MiB of Zorro III fast RAM requires a full 32-bit MC68020, MC68030, or MC68040 backend.");
            }

            if (options.RtgVramSize != 0 &&
                options.CpuBackend is not (
                    M68kBackendKind.AccurateM68020 or
                    M68kBackendKind.AccurateM68030 or
                    M68kBackendKind.AccurateM68040 or
                    M68kBackendKind.JitM68040))
            {
                throw new InvalidOperationException(
                    "Linear RTG VRAM requires a full 32-bit MC68020, MC68030, or MC68040 backend.");
            }

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
                options.CpuWaitSlotReferencePathEnabled,
                options.RtgVramSize);
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

        public MachineOptions Options { get; }

        public MachineProfile Profile => Options.Profile;

        public AmigaBus Bus { get; }

        public IM68kCore Cpu { get; }

        public AmigaKickstartHost Kickstart { get; }

        internal IReadOnlyList<InterruptDispatchTrace> InterruptDispatchTrace
            => _interruptDispatchTrace ?? (IReadOnlyList<InterruptDispatchTrace>)Array.Empty<InterruptDispatchTrace>();

        internal IReadOnlyList<InterruptBusPhaseTrace> InterruptBusPhaseTrace
            => _interruptBusPhaseTrace ?? (IReadOnlyList<InterruptBusPhaseTrace>)Array.Empty<InterruptBusPhaseTrace>();

        internal void CaptureInterruptDispatchTrace(int capacity, int busPhaseWindowCycles = 0)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _interruptDispatchTrace = new List<InterruptDispatchTrace>(capacity);
            _interruptDispatchTraceCapacity = capacity;
            _interruptBusPhaseWindowCycles = busPhaseWindowCycles;
            _interruptBusPhaseTrace = busPhaseWindowCycles > 0
                ? new List<InterruptBusPhaseTrace>(capacity)
                : null;
            Bus.SetCpuBusPhaseObserver(busPhaseWindowCycles > 0 ? RecordInterruptBusPhase : null);
        }

        private void RecordInterruptBusPhase(AmigaCpuBusPhaseTrace phase)
        {
            var active = _activeInterruptBusPhaseTrace;
            if (active == null)
            {
                return;
            }

            if (phase.CpuPhase.RequestedCycle > active.AcceptanceCycle + _interruptBusPhaseWindowCycles)
            {
                _activeInterruptBusPhaseTrace = null;
                return;
            }

            active.Phases.Add(phase);
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
			var interruptRecognition = Cpu as IM68000InterruptRecognition;
			if (interruptRecognition != null &&
				!interruptRecognition.HasRecognizedInterrupt(cpuVisibleCycle))
			{
				return false;
			}
			var prefetchDiagnostics = Cpu as IM68000PrefetchDiagnostics;
			var prefetchBefore = prefetchDiagnostics?.CapturePrefetchDiagnosticState();
			if (_interruptBusPhaseTrace != null &&
				_interruptBusPhaseTrace.Count < _interruptDispatchTraceCapacity)
			{
				var window = new InterruptBusPhaseTrace(
					level,
					cpuVisibleCycle,
					acceptanceCycle,
					new List<AmigaCpuBusPhaseTrace>(32));
				_interruptBusPhaseTrace.Add(window);
				_activeInterruptBusPhaseTrace = window;
			}
			Cpu.RequestInterrupt(level, GetAutovectorAddress(level));
			var prefetchAfter = prefetchDiagnostics?.CapturePrefetchDiagnosticState();
            var trace = _interruptDispatchTrace;
            if (trace != null && trace.Count < _interruptDispatchTraceCapacity)
            {
                trace.Add(new InterruptDispatchTrace(
                    level,
                    activeInterruptBits,
                    cpuVisibleCycle,
					interruptRecognition?.LastInterruptSampleCycle ?? acceptanceCycle,
                    acceptanceCycle,
                    Cpu.State.Cycles,
					interruptedProgramCounter,
					Cpu.State.ProgramCounter,
					savedStatusRegister,
					Cpu.State.D[3],
					prefetchBefore,
					prefetchAfter));
            }

            return true;
        }

        private static uint GetAutovectorAddress(int level)
        {
            return (uint)((24 + level) * 4);
        }
    }
}
