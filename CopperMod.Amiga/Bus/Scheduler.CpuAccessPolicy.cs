/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

namespace CopperMod.Amiga.Bus
{
    internal sealed partial class Scheduler
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

            if (target == AmigaBusAccessTarget.CustomRegisters)
            {
                return isWrite
                    ? AmigaHardwareEventMask.All
                    : CustomRegisterReadMask;
            }

            if (!isWrite)
            {
                return AmigaHardwareEventMask.None;
            }

            return AmigaHardwareEventMask.None;
        }
    }
}
