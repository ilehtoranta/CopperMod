/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

namespace CopperMod.Amiga
{
    internal sealed partial class AmigaBootController
    {
        bool ICyberGraphicsGuestServices.TryInvokeLayersLibraryPatch(
            int vectorOffset,
            M68kCpuState state)
        {
            _ = vectorOffset;
            _ = state;

            // Layer topology, damage regions and ClipRect ownership remain with
            // Kickstart. The vectors are intercepted so individual operations
            // can be promoted once a trace proves that they touch Planes[]
            // directly; today every one performs an exact saved-vector chain.
            return false;
        }
    }
}
