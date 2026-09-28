using System.Collections.Generic;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>Per-library monitor positions, independent of the active View.</summary>
internal sealed class GraphicsMonitorPositionState
{
    private readonly Dictionary<uint, GraphicsMonitorViewPosition> _positions = new();

    // This boundary accepts an explicit canonical monitor, not a default
    // alias. Intuition/driver policy must select the owner before updating it.
    // Values are absolute signed guest coordinates, not cumulative deltas.
    internal bool TrySet(uint monitorId, short? x, short? y)
    {
        if (monitorId != 0x00011000u && monitorId != 0x00021000u)
            return false;
        var previous = Get(monitorId, false);
        _positions[monitorId] = new(x ?? previous.X, y ?? previous.Y);
        return true;
    }

    internal GraphicsMonitorViewPosition Get(uint modeId, bool defaultMonitorNtsc)
    {
        var monitorId = modeId & 0xFFFF1000u;
        if (monitorId == 0)
            monitorId = defaultMonitorNtsc ? 0x00011000u : 0x00021000u;
        return _positions.TryGetValue(monitorId, out var position)
            ? position : GraphicsMonitorViewPosition.BootDefault;
    }
}
