using System;
using System.Buffers.Binary;
using System.Text;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>Shared deterministic MonitorSpec prefix; no host or ROM pointers.</summary>
internal static class GraphicsMonitorSpecImage
{
    // These LONGs are relative to the monitor allocation in the native image.
    // The extended-node library backlink is separately fixed to GfxBase.
    internal static ReadOnlySpan<int> NativeSelfPointerOffsets =>
    [
        GraphicsLayouts.MonitorSpecNodeName,
        GraphicsLayouts.MonitorSpecDisplayInfoDataBaseHead,
        GraphicsLayouts.MonitorSpecDisplayInfoDataBaseTailPred,
        GraphicsLayouts.MonitorSpecDisplayInfoSemaphoreWaitQueue,
        GraphicsLayouts.MonitorSpecDisplayInfoSemaphoreWaitQueue + 8
    ];

    internal static byte[] CreateNativeDefaultImage(bool ntsc)
    {
        var name = Encoding.ASCII.GetBytes(ntsc ? "ntsc.monitor\0" : "pal.monitor\0");
        var image = new byte[(GraphicsLayouts.MonitorSpecSize + name.Length + 3) & ~3];
        Create(0, GraphicsLayouts.MonitorSpecSize, !ntsc, 0, 0).CopyTo(image, 0);
        name.CopyTo(image, GraphicsLayouts.MonitorSpecSize);
        return image;
    }

    // The guest-memory caller validates the full address envelope first. Zero
    // is intentional when emitting the self-relative native template above.
    internal static byte[] Create(uint address, uint name, bool pal, ushort openCount, uint graphicsBase)
    {
        var image = new byte[GraphicsLayouts.MonitorSpecSize];
        var timing = GraphicsNativeMonitorTiming.For(pal);
        image[GraphicsLayouts.MonitorSpecNodeType] = 18; // NT_GRAPHICS
        image[GraphicsLayouts.MonitorSpecNodeSubsystem] = 2; // SS_GRAPHICS
        image[GraphicsLayouts.MonitorSpecNodeSubtype] = 4; // MONITOR_SPEC_TYPE
        Long(GraphicsLayouts.ExtendedNodeLibrary, graphicsBase);
        Long(GraphicsLayouts.MonitorSpecNodeName, name);
        Word(GraphicsLayouts.MonitorSpecFlags, pal ? (ushort)2 : (ushort)1);
        Long(GraphicsLayouts.MonitorSpecRatioH, 16);
        Long(GraphicsLayouts.MonitorSpecRatioV, 16);
        Word(GraphicsLayouts.MonitorSpecTotalRows, timing.TotalRows);
        Word(GraphicsLayouts.MonitorSpecTotalColorClocks, timing.TotalColorClocks);
        Word(GraphicsLayouts.MonitorSpecDeniseMaxDisplayColumn, timing.DeniseMaxDisplayColumn);
        Word(GraphicsLayouts.MonitorSpecBeamCon0, timing.BeamCon0);
        Word(GraphicsLayouts.MonitorSpecMinRow, timing.MinRow);
        Word(GraphicsLayouts.MonitorSpecOpenCount, openCount);
        Word(GraphicsLayouts.MonitorSpecXOffset, 9);
        Word(GraphicsLayouts.MonitorSpecLegalView, timing.LegalViewLeft);
        Word(GraphicsLayouts.MonitorSpecLegalView + 2, timing.LegalViewTop);
        Word(GraphicsLayouts.MonitorSpecLegalView + 4, timing.LegalViewRight);
        Word(GraphicsLayouts.MonitorSpecLegalView + 6, timing.LegalViewBottom);
        Word(GraphicsLayouts.MonitorSpecDeniseMinDisplayColumn, timing.DeniseMinDisplayColumn);
        Long(GraphicsLayouts.MonitorSpecDisplayInfoDataBaseHead,
            address + (uint)GraphicsLayouts.MonitorSpecDisplayInfoDataBaseTail);
        Long(GraphicsLayouts.MonitorSpecDisplayInfoDataBaseTailPred,
            address + (uint)GraphicsLayouts.MonitorSpecDisplayInfoDataBase);
        image[GraphicsLayouts.MonitorSpecDisplayInfoSemaphoreNodeType] = 15; // NT_SIGNALSEM
        Long(GraphicsLayouts.MonitorSpecDisplayInfoSemaphoreWaitQueue,
            address + (uint)GraphicsLayouts.MonitorSpecDisplayInfoSemaphoreWaitQueue + 4);
        Long(GraphicsLayouts.MonitorSpecDisplayInfoSemaphoreWaitQueue + 8,
            address + (uint)GraphicsLayouts.MonitorSpecDisplayInfoSemaphoreWaitQueue);
        Word(GraphicsLayouts.MonitorSpecDisplayInfoSemaphoreQueueCount, ushort.MaxValue);
        // All other fields, including callback slots, list tails, owner, nest
        // count and waiter links, are deliberately zero in this boot prefix.
        return image;

        void Word(int offset, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(offset, 2), value);
        void Long(int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(offset, 4), value);
    }
}
