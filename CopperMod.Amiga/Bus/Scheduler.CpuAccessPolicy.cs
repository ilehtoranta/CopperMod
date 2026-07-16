/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

namespace CopperMod.Amiga
{
    internal sealed partial class AmigaHardwareScheduler
    {
        private AmigaHardwareEventMask GetCpuAccessMask(
            AmigaBusAccessTarget target,
            uint address,
            bool isWrite)
        {
            if (target == AmigaBusAccessTarget.ChipRam ||
                target == AmigaBusAccessTarget.ExpansionRam ||
                target == AmigaBusAccessTarget.RealTimeClock)
            {
                return SlotContendedMemoryAccessMask;
            }

            if (!isWrite)
            {
                return AmigaHardwareEventMask.None;
            }

            if (target == AmigaBusAccessTarget.CustomRegisters)
            {
                return AmigaHardwareEventMask.All;
            }

            return AmigaHardwareEventMask.None;
        }
    }
}
