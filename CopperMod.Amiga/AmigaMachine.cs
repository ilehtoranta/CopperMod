using System;

namespace CopperMod.Amiga
{
    internal enum AmigaMachineProfile
    {
        A500PalCustPlayback,
        A500PalFullEmulationSkeleton,
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

        public IAmigaBusArbiter BusArbiter { get; private set; } = new ZeroWaitBusArbiter();

        public IM68kCoreFactory CpuFactory { get; private set; } = M68kCoreFactory.Default;

        public M68kBackendKind CpuBackend { get; private set; } = M68kBackendKind.AccurateM68000;

        public AmigaKickstartConfiguration KickstartConfiguration { get; private set; } = AmigaKickstartConfiguration.HostShim13;

        public static AmigaMachineOptions ForProfile(AmigaMachineProfile profile)
        {
            var options = new AmigaMachineOptions(profile);
            if (profile == AmigaMachineProfile.A500Pal512KBoot)
            {
                options.ChipRamSize = AmigaConstants.A500BootChipRamSize;
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
    }

    internal sealed class AmigaMachine
    {
        public AmigaMachine(AmigaMachineOptions options)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            Bus = new AmigaBus(options.ChipRamSize, options.BusArbiter);
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
