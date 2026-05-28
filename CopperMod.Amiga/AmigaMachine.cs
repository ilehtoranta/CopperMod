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

        public IAmigaBusArbiter BusArbiter { get; private set; } = new ZeroWaitBusArbiter();

        public IM68kCoreFactory CpuFactory { get; private set; } = M68kCoreFactory.Default;

        public M68kBackendKind CpuBackend { get; private set; } = M68kBackendKind.AccurateM68000;

        public AmigaKickstartConfiguration KickstartConfiguration { get; private set; } = AmigaKickstartConfiguration.HostShim13;

        public static AmigaMachineOptions ForProfile(AmigaMachineProfile profile)
        {
            var options = new AmigaMachineOptions(profile);
            if (profile == AmigaMachineProfile.A500Pal512KChipOnlyBoot)
            {
                options.ChipRamSize = AmigaConstants.A500BootChipRamSize;
            }
            else if (profile == AmigaMachineProfile.A500Pal512KBoot)
            {
                options.ChipRamSize = AmigaConstants.A500BootChipRamSize;
                options.ExpansionRamSize = AmigaConstants.A500BootPseudoFastRamSize;
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
    }

    internal sealed class AmigaMachine
    {
        public AmigaMachine(AmigaMachineOptions options)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            Bus = new AmigaBus(options.ChipRamSize, options.BusArbiter, options.ExpansionRamSize, options.ExpansionRamBase);
            Cpu = options.CpuFactory.Create(options.CpuBackend, Bus);
            Kickstart = new AmigaKickstartHost(options.KickstartConfiguration);
        }

        public AmigaMachineOptions Options { get; }

        public AmigaMachineProfile Profile => Options.Profile;

        public AmigaBus Bus { get; }

        public IM68kCore Cpu { get; }

        public AmigaKickstartHost Kickstart { get; }

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
