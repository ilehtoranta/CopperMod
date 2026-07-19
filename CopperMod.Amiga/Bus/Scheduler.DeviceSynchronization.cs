/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System.Runtime.CompilerServices;

namespace CopperMod.Amiga.Bus
{
    internal sealed partial class Scheduler
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SynchronizeBlitterThrough(long targetCycle)
        {
            if (_bus.Blitter.HasAdvanceWorkThrough(targetCycle))
            {
                _bus.Blitter.ExecuteAdmittedWorkThrough(targetCycle);
            }
        }

        internal void SynchronizePaulaThrough(long targetCycle)
            => _bus.Paula.AdvanceTo(targetCycle);

        internal void SynchronizeDiskThrough(long targetCycle)
            => _bus.Disk.AdvanceTo(targetCycle);
    }
}
