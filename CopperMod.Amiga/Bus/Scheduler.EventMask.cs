/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace CopperMod.Amiga.Bus
{
    [Flags]
    internal enum AmigaHardwareEventMask
    {
        None = 0,
        Raster = 1 << 0,
        CiaTimers = 1 << 1,
        PaulaRegister = 1 << 2,
        DiskEvents = 1 << 3,
        DiskPassiveInput = 1 << 4,
        Agnus = 1 << 5,
        Blitter = 1 << 6,
        DiskCiaEvents = 1 << 7,
        ForceCatchUp = 1 << 8,
        CpuBoundary = 1 << 11,
        PaulaDma = 1 << 13,
        All = Raster |
            CiaTimers |
            PaulaRegister |
            DiskEvents |
            Agnus |
            Blitter |
            DiskCiaEvents
    }
}
