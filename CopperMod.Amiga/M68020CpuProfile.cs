using System;
using System.Collections.Generic;

namespace CopperMod.Amiga
{
    internal enum M68020BusWidth
    {
        Byte = 1,
        Word = 2,
        Long = 4
    }

    internal enum M68020MemoryTarget
    {
        ChipRam,
        ExpansionRam,
        RealFastRam,
        CustomRegisters,
        Cia,
        Rom,
        HostTrap,
        Unmapped
    }

    internal readonly record struct M68020BusTimingRule(
        M68020MemoryTarget Target,
        M68020BusWidth Width,
        int WaitStates);

    internal sealed class M68020CpuProfile
    {
        private M68020CpuProfile(
            string name,
            int nativeCyclesPerMachineCycle,
            M68020BusTimingRule[] busTiming)
        {
            if (nativeCyclesPerMachineCycle <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(nativeCyclesPerMachineCycle),
                    nativeCyclesPerMachineCycle,
                    "Native cycles per machine cycle must be positive.");
            }

            Name = name ?? throw new ArgumentNullException(nameof(name));
            NativeCyclesPerMachineCycle = nativeCyclesPerMachineCycle;
            BusTiming = busTiming ?? throw new ArgumentNullException(nameof(busTiming));
        }

        public static M68020CpuProfile OcsAccelerator14Mhz { get; } = new(
            "Ocs68020_14MHz",
            nativeCyclesPerMachineCycle: 2,
            new[]
            {
                new M68020BusTimingRule(M68020MemoryTarget.ChipRam, M68020BusWidth.Word, 0),
                new M68020BusTimingRule(M68020MemoryTarget.ExpansionRam, M68020BusWidth.Word, 0),
                new M68020BusTimingRule(M68020MemoryTarget.RealFastRam, M68020BusWidth.Long, 0),
                new M68020BusTimingRule(M68020MemoryTarget.CustomRegisters, M68020BusWidth.Word, 0),
                new M68020BusTimingRule(M68020MemoryTarget.Cia, M68020BusWidth.Byte, 0),
                new M68020BusTimingRule(M68020MemoryTarget.Rom, M68020BusWidth.Word, 0),
                new M68020BusTimingRule(M68020MemoryTarget.HostTrap, M68020BusWidth.Word, 0),
                new M68020BusTimingRule(M68020MemoryTarget.Unmapped, M68020BusWidth.Word, 0)
            });

        public string Name { get; }

        public int NativeCyclesPerMachineCycle { get; }

        public IReadOnlyList<M68020BusTimingRule> BusTiming { get; }

        public long NativeToMachineCycles(long nativeCycles)
            => (nativeCycles + NativeCyclesPerMachineCycle - 1) / NativeCyclesPerMachineCycle;

        public long MachineToNativeCycles(long machineCycles)
            => machineCycles * NativeCyclesPerMachineCycle;
    }
}
