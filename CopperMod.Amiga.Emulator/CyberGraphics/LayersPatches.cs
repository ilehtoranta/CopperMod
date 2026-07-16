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

            // Layer topology, damage regions, backing stores and ClipRect
            // ownership remain with Kickstart. RTG graphics gateways consume
            // those live lists after native layer operations update them, so
            // every topology vector continues through its exact saved target.
            return false;
        }
    }
}
