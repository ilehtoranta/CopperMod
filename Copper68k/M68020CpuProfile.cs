using System;
using System.Collections.Generic;

namespace Copper68k
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
        private const uint DefaultRealFastRamBase = 0x0020_0000;
        private const uint DefaultExpansionRamBase = 0x00C0_0000;

        private M68020CpuProfile(
            string name,
            M68kAcceleratorModel model,
            int nativeCyclesPerMachineCycle,
            M68020BusTimingRule[] busTiming,
            int? fixedInstructionNativeCycles = null,
            bool fastInstructionFetch = false,
            bool fastNonChipMemoryAccess = false,
            bool fastCiaAPortAAccess = false)
        {
            if (nativeCyclesPerMachineCycle <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(nativeCyclesPerMachineCycle),
                    nativeCyclesPerMachineCycle,
                    "Native cycles per machine cycle must be positive.");
            }

            if (fixedInstructionNativeCycles is < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fixedInstructionNativeCycles),
                    fixedInstructionNativeCycles,
                    "Fixed instruction cycles must be non-negative when provided.");
            }

            Name = name ?? throw new ArgumentNullException(nameof(name));
            Model = model;
            NativeCyclesPerMachineCycle = nativeCyclesPerMachineCycle;
            BusTiming = busTiming ?? throw new ArgumentNullException(nameof(busTiming));
            FixedInstructionNativeCycles = fixedInstructionNativeCycles;
            FastInstructionFetch = fastInstructionFetch;
            FastNonChipMemoryAccess = fastNonChipMemoryAccess;
            FastCiaAPortAAccess = fastCiaAPortAAccess;
        }

        internal static M68020CpuProfile OcsAccelerator14Mhz { get; } = new(
            "Ocs68020_14MHz",
            M68kAcceleratorModel.M68020,
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

        internal static M68020CpuProfile Ocs68030Accelerator14Mhz { get; } = new(
            "Ocs68030_14MHz",
            M68kAcceleratorModel.M68030,
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

        internal static M68020CpuProfile Ocs68040Accelerator25Mhz { get; } = new(
            "Ocs68040_25MHz",
            M68kAcceleratorModel.M68040,
            nativeCyclesPerMachineCycle: 4,
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
            },
            fixedInstructionNativeCycles: 1);

        internal static M68020CpuProfile Ocs68040JitMaxSpeed { get; } = new(
            "Ocs68040_JitMaxSpeed",
            M68kAcceleratorModel.M68040,
            nativeCyclesPerMachineCycle: 1,
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
            },
            fixedInstructionNativeCycles: 1,
            fastInstructionFetch: true,
            fastNonChipMemoryAccess: true,
            fastCiaAPortAAccess: true);

        internal string Name { get; }

        internal M68kAcceleratorModel Model { get; }

        internal string ModelName => Model switch
        {
            M68kAcceleratorModel.M68030 => "MC68030",
            M68kAcceleratorModel.M68040 => "MC68040",
            _ => "MC68020"
        };

        internal int NativeCyclesPerMachineCycle { get; }

        internal IReadOnlyList<M68020BusTimingRule> BusTiming { get; }

        internal int? FixedInstructionNativeCycles { get; }

        internal bool FastInstructionFetch { get; }

        internal bool FastNonChipMemoryAccess { get; }

        internal bool FastCiaAPortAAccess { get; }

        internal static M68020CpuProfile CreateForTesting(
            string name,
            M68kAcceleratorModel model,
            int nativeCyclesPerMachineCycle,
            params M68020BusTimingRule[] busTiming)
            => new(name, model, nativeCyclesPerMachineCycle, busTiming);

        internal long NativeToMachineCycles(long nativeCycles)
            => (nativeCycles + NativeCyclesPerMachineCycle - 1) / NativeCyclesPerMachineCycle;

        internal long MachineToNativeCycles(long machineCycles)
            => machineCycles * NativeCyclesPerMachineCycle;

        internal M68020BusTimingRule GetBusTimingRule(uint address)
        {
            var target = ClassifyTarget(address);
            for (var i = 0; i < BusTiming.Count; i++)
            {
                if (BusTiming[i].Target == target)
                {
                    return BusTiming[i];
                }
            }

            return new M68020BusTimingRule(target, M68020BusWidth.Word, 0);
        }

        internal static M68020MemoryTarget ClassifyTarget(uint address)
        {
            address &= 0x00FF_FFFF;
            if (address < 0x0020_0000)
            {
                return M68020MemoryTarget.ChipRam;
            }

            if (address >= DefaultRealFastRamBase && address < 0x00A0_0000)
            {
                return M68020MemoryTarget.RealFastRam;
            }

            if (address >= 0x00BF_E000 && address < 0x00C0_0000)
            {
                return M68020MemoryTarget.Cia;
            }

            if (address >= DefaultExpansionRamBase && address < 0x00D0_0000)
            {
                return M68020MemoryTarget.ExpansionRam;
            }

            if (address >= 0x00DFF000 && address < 0x00DFF200)
            {
                return M68020MemoryTarget.CustomRegisters;
            }

            if (address >= 0x00F0_0000)
            {
                return M68020MemoryTarget.Rom;
            }

            return M68020MemoryTarget.Unmapped;
        }
    }
}
