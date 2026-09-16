using System.Runtime.CompilerServices;

namespace CopperMod.Amiga.Lightweight;

/// <summary>
/// Direct OCS chip-slot ownership used by CPU transfers.
/// </summary>
/// <remarks>
/// This is deliberately a calculation over the canonical clock, not a slot
/// schedule. Later DMA devices extend the owner decision with compact state.
/// </remarks>
internal sealed class LightweightBusArbiter
{
    internal const int SlotCycles = LightweightClock.CpuCyclesPerColorClock;
    internal const int FirstRefreshHorizontal = 0x00;
    internal const int LastRefreshHorizontal = 0x06;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal long GrantCpuWord(long requestedCycle)
    {
        var candidate = AlignToSlot(requestedCycle);
        while (IsMandatoryRefreshSlot(candidate))
        {
            candidate += SlotCycles;
        }

        return candidate;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static long AlignToSlot(long cycle)
    {
        cycle = Math.Max(0, cycle);
        return (cycle + (SlotCycles - 1)) & -SlotCycles;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsMandatoryRefreshSlot(long slotCycle)
    {
        var horizontal = GetHorizontal(slotCycle);
        return horizontal <= LastRefreshHorizontal &&
            ((horizontal - FirstRefreshHorizontal) & 1) == 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int GetHorizontal(long slotCycle)
        => (int)((AlignToSlot(slotCycle) % LightweightClock.CpuCyclesPerLine) /
            SlotCycles);
}
