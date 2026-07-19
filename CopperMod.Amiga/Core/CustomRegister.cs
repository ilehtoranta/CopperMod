/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

namespace CopperMod.Amiga.Core
{
    internal enum CustomRegister : ushort
    {
        Bltcon0 = 0x040,
        Bltcon1 = 0x042,
        Bltsize = 0x058,
        Bltcon0l = 0x05A,
        Bltsizv = 0x05C,
        Bltsizh = 0x05E,
        DeniseId = 0x07C,
        Copcon = 0x02E,
        Cop1lch = 0x080,
        Cop1lcl = 0x082,
        Cop2lch = 0x084,
        Cop2lcl = 0x086,
        Copjmp1 = 0x088,
        Copjmp2 = 0x08A,
        Diwstrt = 0x08E,
        Diwstop = 0x090,
        Ddfstrt = 0x092,
        Ddfstop = 0x094,
        Dmacon = 0x096,
        Bplcon0 = 0x100,
        Bplcon1 = 0x102,
        Bplcon2 = 0x104,
        Bplcon3 = 0x106,
        Bpl1mod = 0x108,
        Bpl2mod = 0x10A,
        Htotal = 0x1C0,
        Hsstop = 0x1C2,
        Hbstrt = 0x1C4,
        Hbstop = 0x1C6,
        Vtotal = 0x1C8,
        Vsstop = 0x1CA,
        Vbstrt = 0x1CC,
        Vbstop = 0x1CE,
        Sprhstrt = 0x1D0,
        Sprhstop = 0x1D2,
        Bplhstrt = 0x1D4,
        Bplhstop = 0x1D6,
        Hhposw = 0x1D8,
        Hhposr = 0x1DA,
        Beamcon0 = 0x1DC,
        Hsstrt = 0x1DE,
        Vsstrt = 0x1E0,
        Hcenter = 0x1E2,
        Diwhigh = 0x1E4
    }
}
