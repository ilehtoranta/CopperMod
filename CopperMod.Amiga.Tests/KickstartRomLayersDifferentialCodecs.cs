using Amiga;

namespace CopperMod.Amiga.Tests;

/// <summary>
/// SDK-codec boundary for the licensed V40.63 differential fixture. The
/// fixture compares semantic values; public byte offsets remain owned by the
/// SDK codecs and their dedicated layout tests.
/// </summary>
public sealed partial class KickstartRomLayersDifferentialTests
{
    private static LayerFlags ReadOracleLayerFlags(AmigaBus bus, uint layer)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersLayerCodec.ReadFlags(ref memory, APTR.FromPointer(layer));
    }

    private static ushort ReadOracleLibraryVersion(AmigaBus bus, uint library)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersLibraryCodec.ReadVersion(
            ref memory, APTR.FromPointer(library));
    }

    private static ushort ReadOracleLibraryNegativeSize(AmigaBus bus, uint library)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersLibraryCodec.ReadNegativeSize(
            ref memory, APTR.FromPointer(library));
    }

    private static uint ReadOracleExecNodeNext(AmigaBus bus, uint node)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersExecNodeCodec.ReadNext(
            ref memory, APTR.FromPointer(node)).Raw;
    }

    private static uint ReadOracleExecNodeName(AmigaBus bus, uint node)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersExecNodeCodec.ReadName(
            ref memory, APTR.FromPointer(node)).Raw;
    }

    private static uint ReadOracleLibraryListHead(AmigaBus bus, uint execBase)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersExecListCodec.ReadHead(
            ref memory,
            LayersExecBaseCodec.LibraryListAddress(APTR.FromPointer(execBase))).Raw;
    }

    private static uint OracleLibraryListTailAddress(uint execBase) =>
        LayersExecListCodec.TailAddress(
            LayersExecBaseCodec.LibraryListAddress(APTR.FromPointer(execBase))).Raw;

    private static void WriteOracleHookEntry(
        AmigaBus bus, uint hook, uint entry)
    {
        var memory = new LayersTestGuestMemory(bus);
        LayersHookCodec.WriteEntry(
            ref memory, APTR.FromPointer(hook), APTR.FromPointer(entry));
    }

    private static void WriteOracleLayerFlags(
        AmigaBus bus, uint layer, LayerFlags flags)
    {
        var memory = new LayersTestGuestMemory(bus);
        LayersLayerCodec.WriteFlags(
            ref memory, APTR.FromPointer(layer), flags);
    }

    private static uint ReadOracleTopLayer(AmigaBus bus, uint layerInfo)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersLayerInfoCodec.ReadTopLayer(
            ref memory, APTR.FromPointer(layerInfo)).Raw;
    }

    private static uint ReadOracleLayerPointer(
        AmigaBus bus, uint layer, OracleLayerPointer field)
    {
        var memory = new LayersTestGuestMemory(bus);
        var address = APTR.FromPointer(layer);
        return field switch
        {
            OracleLayerPointer.Back => LayersLayerCodec.ReadBack(ref memory, address).Raw,
            OracleLayerPointer.ClipRect => LayersLayerCodec.ReadClipRect(ref memory, address).Raw,
            OracleLayerPointer.RastPort => LayersLayerCodec.ReadRastPort(ref memory, address).Raw,
            OracleLayerPointer.SuperBitMap => LayersLayerCodec.ReadSuperBitMap(ref memory, address).Raw,
            OracleLayerPointer.SuperClipRect => LayersLayerCodec.ReadSuperClipRect(ref memory, address).Raw,
            OracleLayerPointer.ClipRegion => LayersLayerCodec.ReadClipRegion(ref memory, address).Raw,
            OracleLayerPointer.DamageList => LayersLayerCodec.ReadDamageList(ref memory, address).Raw,
            OracleLayerPointer.LayerInfo => LayersLayerCodec.ReadLayerInfo(ref memory, address).Raw,
            _ => 0
        };
    }

    private static Rectangle ReadOracleLayerBounds(AmigaBus bus, uint layer)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersLayerCodec.ReadBounds(ref memory, APTR.FromPointer(layer));
    }

    private static short ReadOracleLayerScrollX(AmigaBus bus, uint layer)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersLayerCodec.ReadScrollX(ref memory, APTR.FromPointer(layer));
    }

    private static short ReadOracleLayerScrollY(AmigaBus bus, uint layer)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersLayerCodec.ReadScrollY(ref memory, APTR.FromPointer(layer));
    }

    private static uint ReadOracleClipRectPointer(
        AmigaBus bus, uint clipRect, OracleClipRectPointer field)
    {
        var memory = new LayersTestGuestMemory(bus);
        var address = APTR.FromPointer(clipRect);
        return field switch
        {
            OracleClipRectPointer.Next => LayersClipRectCodec.ReadNext(ref memory, address).Raw,
            OracleClipRectPointer.ReservedLink => LayersClipRectCodec.ReadReservedLink(ref memory, address).Raw,
            OracleClipRectPointer.ObscuringLayer => LayersClipRectCodec.ReadObscuringLayer(ref memory, address).Raw,
            OracleClipRectPointer.BitMap => LayersClipRectCodec.ReadBitMap(ref memory, address).Raw,
            _ => 0
        };
    }

    private static Rectangle ReadOracleClipRectBounds(AmigaBus bus, uint clipRect)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersClipRectCodec.ReadBounds(
            ref memory, APTR.FromPointer(clipRect));
    }

    private static bool IsOracleClipRectMapped(AmigaBus bus, uint clipRect)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersClipRectCodec.IsMapped(
            ref memory, APTR.FromPointer(clipRect));
    }

    private static uint ReadOracleRastPortPointer(
        AmigaBus bus, uint rastPort, bool bitMap)
    {
        var memory = new LayersTestGuestMemory(bus);
        var address = APTR.FromPointer(rastPort);
        return (bitMap
            ? LayersRastPortCodec.ReadBitMap(ref memory, address)
            : LayersRastPortCodec.ReadLayer(ref memory, address)).Raw;
    }

    private static ushort ReadOracleBitMapBytesPerRow(AmigaBus bus, uint bitMap)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersBitMapCodec.ReadBytesPerRow(
            ref memory, APTR.FromPointer(bitMap));
    }

    private static ushort ReadOracleBitMapRows(AmigaBus bus, uint bitMap)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersBitMapCodec.ReadRows(ref memory, APTR.FromPointer(bitMap));
    }

    private static byte ReadOracleBitMapDepth(AmigaBus bus, uint bitMap)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersBitMapCodec.ReadDepth(ref memory, APTR.FromPointer(bitMap));
    }

    private static uint ReadOracleBitMapPlane(AmigaBus bus, uint bitMap, int plane)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersBitMapCodec.ReadPlane(
            ref memory, APTR.FromPointer(bitMap), plane).Raw;
    }

    private static void WriteOracleBitMap(
        AmigaBus bus, uint bitMap, ushort bytesPerRow, ushort rows, byte depth)
    {
        var memory = new LayersTestGuestMemory(bus);
        var address = APTR.FromPointer(bitMap);
        LayersBitMapCodec.WriteBytesPerRow(ref memory, address, bytesPerRow);
        LayersBitMapCodec.WriteRows(ref memory, address, rows);
        LayersBitMapCodec.WriteDepth(ref memory, address, depth);
    }

    private static void WriteOracleBitMapPlane(
        AmigaBus bus, uint bitMap, int plane, uint value)
    {
        var memory = new LayersTestGuestMemory(bus);
        LayersBitMapCodec.WritePlane(
            ref memory,
            APTR.FromPointer(bitMap),
            plane,
            APTR.FromPointer(value));
    }

    private static Rectangle ReadOracleRectangle(AmigaBus bus, uint address)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersRectangleCodec.Read(ref memory, APTR.FromPointer(address));
    }

    private static string FormatOracleRectangle(Rectangle value) =>
        $"{value.MinX},{value.MinY},{value.MaxX},{value.MaxY}";

    private static uint ReadOracleRegionFirst(AmigaBus bus, uint region)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersRegionCodec.ReadFirst(
            ref memory, APTR.FromPointer(region)).Raw;
    }

    private static Rectangle ReadOracleRegionBounds(AmigaBus bus, uint region)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersRegionCodec.ReadBounds(
            ref memory, APTR.FromPointer(region));
    }

    private static uint ReadOracleRegionRectanglePointer(
        AmigaBus bus, uint node, bool previous)
    {
        var memory = new LayersTestGuestMemory(bus);
        var address = APTR.FromPointer(node);
        return (previous
            ? LayersRegionRectangleCodec.ReadPrevious(ref memory, address)
            : LayersRegionRectangleCodec.ReadNext(ref memory, address)).Raw;
    }

    private static Rectangle ReadOracleRegionRectangleBounds(
        AmigaBus bus, uint node)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersRegionRectangleCodec.ReadBounds(
            ref memory, APTR.FromPointer(node));
    }

    private static void WriteOracleRectangle(
        AmigaBus bus, uint address, short minX, short minY, short maxX, short maxY)
    {
        var memory = new LayersTestGuestMemory(bus);
        LayersRectangleCodec.Write(
            ref memory,
            APTR.FromPointer(address),
            LayersRectangleCodec.Create(minX, minY, maxX, maxY));
    }

    private enum OracleLayerPointer
    {
        Back,
        ClipRect,
        RastPort,
        SuperBitMap,
        SuperClipRect,
        ClipRegion,
        DamageList,
        LayerInfo
    }

    private enum OracleClipRectPointer
    {
        Next,
        ReservedLink,
        ObscuringLayer,
        BitMap
    }
}
