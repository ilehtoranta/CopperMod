/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

namespace CopperMod.Amiga
{
    internal interface IAmigaExecutionBoundarySchedule
    {
        void BeginFrame();

        void BeginExecution(long startCycle, long endCycle);

        long GetNextBoundaryCycle(long currentCycle, long targetCycle);

        void AdvanceThrough(long previousCycle, long currentCycle);

        void CompleteExecution(long endCycle);

        void CompleteFrame();
    }
}
