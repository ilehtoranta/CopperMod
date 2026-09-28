namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>A signed, guest-layout Point value for MonitorInfo.ViewPosition.</summary>
internal readonly record struct GraphicsMonitorViewPosition(short X, short Y)
{
    // Original3.1 PAL/NTSC cold-boot captures352. This is initialization data,
    // not a claim that preference-controlled display records are immutable.
    // Resident record mutation/publication remains a separate implementation
    // requirement; do not infer this field from ActiView or MonitorSpec offsets.
    internal static GraphicsMonitorViewPosition BootDefault => new(129, 44);

    internal uint WordPair => ((uint)unchecked((ushort)X) << 16) | unchecked((ushort)Y);
}
