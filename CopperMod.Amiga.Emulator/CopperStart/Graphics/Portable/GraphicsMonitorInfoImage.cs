namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>Guest-layout boot MonitorInfo, shared by all modes of a monitor family.</summary>
internal static class GraphicsMonitorInfoImage
{
    internal const int Size = 96;
    internal const int TransferSize = 88;

    // Original Kickstart 3.1 PAL/NTSC captures 456/457. These are family-record
    // defaults, not MonitorSpec legal geometry or display-mode pixel ticks.
    // Keep the SDK envelope, including its untransferred reserved terminator.
    internal static byte[] Create(
        bool pal,
        uint monitorSpec,
        GraphicsMonitorViewPosition currentPosition,
        GraphicsMonitorViewPosition originalPosition)
    {
        var data = new byte[Size];
        var family = pal ? GraphicsModeIds.PalMonitor : GraphicsModeIds.NtscMonitor;
        Long(data, 0x00, 0x80002000);
        Long(data, 0x04, family);
        Long(data, 0x08, 3);
        Long(data, 0x0C, 9);
        Long(data, 0x10, monitorSpec); // Opaque database value; never dereference here.
        Long(data, 0x14, currentPosition.WordPair);
        Long(data, 0x18, pal ? 0x002C002Cu : 0x002C0034u);
        Long(data, 0x1C, pal ? 0x005D001Du : 0x005D0015u);
        Long(data, 0x20, pal ? 0x00880039u : 0x0088003Fu);
        Long(data, 0x24, pal ? 0x013800E2u : 0x010600E2u);
        Long(data, 0x28, pal ? 0x001D0000u : 0x00150000u); // MinRow, MCOMPAT_MIXED

        // The SDK calls 0x2C..0x4B pad[32], but the ROM returns these nonzero
        // bytes. Preserve the observed payload without assigning it semantics.
        Long(data, 0x34, 0x000036FF);
        Long(data, 0x38, pal ? 0x00002BFFu : 0x0000289Fu);
        Long(data, 0x44, 0x000036FF);
        Long(data, 0x48, pal ? 0x00002BFFu : 0x0000289Fu);
        Long(data, 0x4C, pal ? 0x00160016u : 0x0016001Au);
        Long(data, 0x50, originalPosition.WordPair);
        Long(data, 0x54, family | 0x8000); // Family's preferred hires mode.
        return data;
    }

    private static void Long(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)(value >> 16);
        data[offset + 2] = (byte)(value >> 8);
        data[offset + 3] = (byte)value;
    }
}
