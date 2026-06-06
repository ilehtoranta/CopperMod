using System;

namespace CopperMod.Amiga
{
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

        public IAmigaBusArbiter BusArbiter { get; private set; } = new ZeroWaitBusArbiter();

        public bool CaptureBusAccesses { get; private set; } = true;

        public bool LiveAgnusDma { get; private set; } = true;

        public bool LiveDisplayDma { get; private set; } = true;

        public bool HardwareSpecializationEnabled { get; private set; }

        public int AudioDmaMinimumPeriod { get; private set; } = AmigaConstants.A500PalMinimumAudioDmaPeriod;

        public IM68kCoreFactory CpuFactory { get; private set; } = M68kCoreFactory.Default;

        public M68kBackendKind CpuBackend { get; private set; } = M68kBackendKind.AccurateM68000;

        public AmigaKickstartConfiguration KickstartConfiguration { get; private set; } = AmigaKickstartConfiguration.HostShim13;

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
            }

            return options;
        }

        public AmigaMachineOptions WithCpu(IM68kCoreFactory factory, M68kBackendKind backend)
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

        private static bool IsPowerOfTwo(int value)
        {
            return value > 0 && (value & (value - 1)) == 0;
        }
    }

    internal sealed class AmigaMachine : IDisposable
    {
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
                options.HardwareSpecializationEnabled);
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

        public void Dispose()
            => Cpu.Dispose();

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
                Bus.WriteDeviceWord(
                    AmigaBusRequester.Cia,
                    AmigaBusAccessKind.CustomRegister,
                    0x00DFF09C,
                    (ushort)(0x8000 | intreqBit),
                    ciaEvent.Cycle);
            }

            var level = Bus.Paula.GetHighestPendingInterruptLevel();
            if (level <= 0)
            {
                return false;
            }

            Cpu.RequestInterrupt(level, GetAutovectorAddress(level));
            return true;
        }

        private static uint GetAutovectorAddress(int level)
        {
            return (uint)((24 + level) * 4);
        }
    }
}
