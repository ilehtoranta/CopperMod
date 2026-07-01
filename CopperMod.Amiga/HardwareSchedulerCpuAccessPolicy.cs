namespace CopperMod.Amiga
{
    internal sealed partial class AmigaHardwareScheduler
    {
        private static AmigaHardwareEventMask GetCpuAccessMask(
            AmigaBusAccessTarget target,
            uint address,
            bool isWrite)
        {
            if (target == AmigaBusAccessTarget.Cia)
            {
                return AmigaHardwareEventMask.Raster |
                    AmigaHardwareEventMask.CiaTimers |
                    AmigaHardwareEventMask.DiskCiaEvents |
                    AmigaHardwareEventMask.CiaRegisterSample;
            }

            if (target == AmigaBusAccessTarget.CustomRegisters && !isWrite)
            {
                return GetCustomRegisterReadMask(address);
            }

            if (target == AmigaBusAccessTarget.ChipRam ||
                target == AmigaBusAccessTarget.ExpansionRam ||
                target == AmigaBusAccessTarget.RealTimeClock)
            {
                return SlotContendedMemoryAccessMask;
            }

            if (target == AmigaBusAccessTarget.CustomRegisters)
            {
                return AmigaHardwareEventMask.All;
            }

            return AmigaHardwareEventMask.None;
        }

        private static AmigaHardwareEventMask GetCustomRegisterReadMask(uint address)
        {
            switch (AmigaBus.GetCustomRegisterReadAdvanceKindForScheduler(address))
            {
                case CustomRegisterReadAdvanceKind.BeamPosition:
                case CustomRegisterReadAdvanceKind.InputOnly:
                    return AmigaHardwareEventMask.None;

                case CustomRegisterReadAdvanceKind.BlitterStatus:
                    return AmigaHardwareEventMask.PaulaRegister |
                        AmigaHardwareEventMask.Blitter;

                case CustomRegisterReadAdvanceKind.InterruptSources:
                    return AmigaHardwareEventMask.Raster |
                        AmigaHardwareEventMask.CiaTimers |
                        AmigaHardwareEventMask.PaulaRegister |
                        AmigaHardwareEventMask.DiskEvents |
                        AmigaHardwareEventMask.Blitter;

                case CustomRegisterReadAdvanceKind.DiskEventOnly:
                    return AmigaHardwareEventMask.PaulaRegister |
                        AmigaHardwareEventMask.DiskEvents |
                        AmigaHardwareEventMask.DiskRegisterSample;

                case CustomRegisterReadAdvanceKind.DiskPassiveInput:
                    return AmigaHardwareEventMask.PaulaRegister |
                        AmigaHardwareEventMask.DiskEvents |
                        AmigaHardwareEventMask.DiskPassiveInput |
                        AmigaHardwareEventMask.DiskRegisterSample;

                default:
                    return AmigaHardwareEventMask.PaulaRegister;
            }
        }
    }
}
