using System;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>OpenMonitor ID syntax, independent of chipset execution capability.</summary>
internal static class GraphicsMonitorSelectors
{
    // Pinned against original Kickstart3.1 for both boot profiles, including
    // SuperHires selectors on OCS. A family match is NOT proof of residency.
    internal static ReadOnlySpan<ushort> ModeKeys =>
    [
        0, 4, 0x8000, 0x8004, 0x8020, 0x8024, 0x800, 0x804, 0x80, 0x84,
        0x400, 0x404, 0x8400, 0x8404, 0x8420, 0x8424, 0x440, 0x444,
        0x8440, 0x8444, 0x8460, 0x8464
    ];

    internal static bool TryGetFamily(uint displayId, out uint family)
    {
        family = displayId & 0xFFFF1000u;
        if ((displayId >> 16) == 0) family = 0;
        if (family != 0 && family != GraphicsModeIds.NtscMonitor && family != GraphicsModeIds.PalMonitor)
            return false;
        return ModeKeys.Contains((ushort)(displayId & 0xEFFF));
    }
}
