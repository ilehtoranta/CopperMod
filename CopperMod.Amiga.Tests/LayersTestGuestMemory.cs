using Amiga;

namespace CopperMod.Amiga.Tests;

/// <summary>
/// SDK-codec adapter for semantic Layers fixtures. Deliberate byte-layout
/// oracle tests remain separately classified.
/// </summary>
internal readonly struct LayersTestGuestMemory : global::CopperStart.Layers.ILayersMemoryPlatform
{
    private readonly AmigaBus _bus;

    internal LayersTestGuestMemory(AmigaBus bus)
        => _bus = bus;

    public byte ReadUInt8(APTR address, int offset = 0)
        => _bus.ReadByte(Add(address, offset));

    public ushort ReadUInt16(APTR address, int offset = 0)
        => _bus.ReadWord(Add(address, offset));

    public uint ReadUInt32(APTR address, int offset = 0)
        => _bus.ReadLong(Add(address, offset));

    public void WriteUInt8(APTR address, int offset, byte value)
        => _bus.WriteByte(Add(address, offset), value, 0);

    public void WriteUInt16(APTR address, int offset, ushort value)
        => _bus.WriteWord(Add(address, offset), value, 0);

    public void WriteUInt32(APTR address, int offset, uint value)
        => _bus.WriteLong(Add(address, offset), value);

    public void Clear(APTR address, uint byteCount)
    {
        for (var offset = 0u; offset < byteCount; offset++)
            _bus.WriteByte(address.Raw + offset, 0, 0);
    }

    public void Copy(APTR source, APTR destination, uint byteCount)
    {
        if (destination.Raw > source.Raw &&
            (ulong)destination.Raw < (ulong)source.Raw + byteCount)
        {
            for (var offset = byteCount; offset != 0; offset--)
            {
                var index = offset - 1;
                _bus.WriteByte(
                    destination.Raw + index,
                    _bus.ReadByte(source.Raw + index),
                    0);
            }
            return;
        }

        for (var offset = 0u; offset < byteCount; offset++)
        {
            _bus.WriteByte(
                destination.Raw + offset,
                _bus.ReadByte(source.Raw + offset),
                0);
        }
    }

    public bool IsMapped(APTR address, uint byteSize)
        => byteSize <= int.MaxValue &&
            address.Raw <= uint.MaxValue - byteSize &&
            _bus.IsMappedMemoryRange(address.Raw, checked((int)byteSize));

    private static uint Add(APTR address, int offset)
        => unchecked(address.Raw + (uint)offset);
}

public sealed partial class CopperStartLayersBootTests
{
    private static uint ReadCurrentTask(AmigaBus bus, uint execBase)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersExecBaseCodec.ReadCurrentTask(
            ref memory,
            APTR.FromPointer(execBase)).Raw;
    }

    private static uint ReadReadyTaskHead(AmigaBus bus, uint execBase)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersExecListCodec.ReadHead(
            ref memory,
            LayersExecBaseCodec.TaskReadyAddress(
                APTR.FromPointer(execBase))).Raw;
    }

    private static byte ReadTaskState(AmigaBus bus, uint task)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersExecTaskCodec.ReadState(
            ref memory,
            APTR.FromPointer(task));
    }

    private static sbyte ReadTaskPriority(AmigaBus bus, uint task)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersExecNodeCodec.ReadPriority(
            ref memory,
            APTR.FromPointer(task));
    }

    private static void InitializeTaskPublicFields(
        AmigaBus bus,
        uint task,
        uint stackPointer,
        sbyte priority)
    {
        var memory = new LayersTestGuestMemory(bus);
        var address = APTR.FromPointer(task);
        LayersExecTaskCodec.WriteStackPointer(
            ref memory,
            address,
            APTR.FromPointer(stackPointer));
        LayersExecNodeCodec.WritePriority(ref memory, address, priority);
    }

    private static uint ReadLayerLockOwner(AmigaBus bus, uint layer)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersSignalSemaphoreCodec.ReadOwner(
            ref memory,
            LayersLayerCodec.LockAddress(APTR.FromPointer(layer))).Raw;
    }

    private static uint ReadLayerClipRect(AmigaBus bus, uint layer)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersLayerCodec.ReadClipRect(
            ref memory,
            APTR.FromPointer(layer)).Raw;
    }

    private static void WriteLayerClipRect(
        AmigaBus bus,
        uint layer,
        uint clipRect)
    {
        var memory = new LayersTestGuestMemory(bus);
        LayersLayerCodec.WriteClipRect(
            ref memory,
            APTR.FromPointer(layer),
            APTR.FromPointer(clipRect));
    }

    private static uint ReadLayerClipRegion(AmigaBus bus, uint layer)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersLayerCodec.ReadClipRegion(
            ref memory,
            APTR.FromPointer(layer)).Raw;
    }

    private static uint ReadLayerSuperClipRect(AmigaBus bus, uint layer)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersLayerCodec.ReadSuperClipRect(
            ref memory,
            APTR.FromPointer(layer)).Raw;
    }

    private static uint ReadLayerRastPort(AmigaBus bus, uint layer)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersLayerCodec.ReadRastPort(
            ref memory,
            APTR.FromPointer(layer)).Raw;
    }

    private static short ReadLayerScrollX(AmigaBus bus, uint layer)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersLayerCodec.ReadScrollX(
            ref memory,
            APTR.FromPointer(layer));
    }

    private static short ReadLayerScrollY(AmigaBus bus, uint layer)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersLayerCodec.ReadScrollY(
            ref memory,
            APTR.FromPointer(layer));
    }

    private static void WriteLayerScroll(
        AmigaBus bus,
        uint layer,
        short scrollX,
        short scrollY)
    {
        var memory = new LayersTestGuestMemory(bus);
        LayersLayerCodec.WriteScroll(
            ref memory,
            APTR.FromPointer(layer),
            scrollX,
            scrollY);
    }

    private static ushort ReadLayerFlags(AmigaBus bus, uint layer)
    {
        var memory = new LayersTestGuestMemory(bus);
        return (ushort)LayersLayerCodec.ReadFlags(
            ref memory,
            APTR.FromPointer(layer));
    }

    private static uint ReadTopLayer(AmigaBus bus, uint layerInfo)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersLayerInfoCodec.ReadTopLayer(
            ref memory,
            APTR.FromPointer(layerInfo)).Raw;
    }

    private static uint ReadClipRectNext(AmigaBus bus, uint clipRect)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersClipRectCodec.ReadNext(
            ref memory,
            APTR.FromPointer(clipRect)).Raw;
    }

    private static uint ReadClipRectObscuringLayer(
        AmigaBus bus,
        uint clipRect)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersClipRectCodec.ReadObscuringLayer(
            ref memory,
            APTR.FromPointer(clipRect)).Raw;
    }

    private static uint ReadClipRectBitMap(AmigaBus bus, uint clipRect)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersClipRectCodec.ReadBitMap(
            ref memory,
            APTR.FromPointer(clipRect)).Raw;
    }

    private static void WriteRectangleBounds(
        AmigaBus bus,
        uint rectangle,
        short minX,
        short minY,
        short maxX,
        short maxY)
    {
        var memory = new LayersTestGuestMemory(bus);
        LayersRectangleCodec.Write(
            ref memory,
            APTR.FromPointer(rectangle),
            LayersRectangleCodec.Create(minX, minY, maxX, maxY));
    }

    private static uint ReadBitMapPlane(AmigaBus bus, uint bitMap, int plane)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersBitMapCodec.ReadPlane(
            ref memory,
            APTR.FromPointer(bitMap),
            plane).Raw;
    }

    private static ushort ReadBitMapBytesPerRow(AmigaBus bus, uint bitMap)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersBitMapCodec.ReadBytesPerRow(
            ref memory,
            APTR.FromPointer(bitMap));
    }

    private static ushort ReadBitMapRows(AmigaBus bus, uint bitMap)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersBitMapCodec.ReadRows(
            ref memory,
            APTR.FromPointer(bitMap));
    }

    private static byte ReadBitMapDepth(AmigaBus bus, uint bitMap)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersBitMapCodec.ReadDepth(
            ref memory,
            APTR.FromPointer(bitMap));
    }

    private static void WriteBitMapBytesPerRow(
        AmigaBus bus, uint bitMap, ushort bytesPerRow)
    {
        var memory = new LayersTestGuestMemory(bus);
        LayersBitMapCodec.WriteBytesPerRow(
            ref memory, APTR.FromPointer(bitMap), bytesPerRow);
    }

    private static void WriteBitMapRows(
        AmigaBus bus, uint bitMap, ushort rows)
    {
        var memory = new LayersTestGuestMemory(bus);
        LayersBitMapCodec.WriteRows(
            ref memory, APTR.FromPointer(bitMap), rows);
    }

    private static void WriteBitMapDepth(AmigaBus bus, uint bitMap, byte depth)
    {
        var memory = new LayersTestGuestMemory(bus);
        LayersBitMapCodec.WriteDepth(
            ref memory,
            APTR.FromPointer(bitMap),
            depth);
    }

    private static void WriteBitMapPlane(
        AmigaBus bus, uint bitMap, int plane, uint address)
    {
        var memory = new LayersTestGuestMemory(bus);
        LayersBitMapCodec.WritePlane(
            ref memory,
            APTR.FromPointer(bitMap),
            plane,
            APTR.FromPointer(address));
    }

    private static byte ReadRastPortForegroundPen(AmigaBus bus, uint rastPort)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersRastPortCodec.ReadForegroundPen(
            ref memory,
            APTR.FromPointer(rastPort));
    }

    private static void WriteRastPortForegroundPen(
        AmigaBus bus, uint rastPort, byte value)
    {
        var memory = new LayersTestGuestMemory(bus);
        LayersRastPortCodec.WriteForegroundPen(
            ref memory, APTR.FromPointer(rastPort), value);
    }

    private static byte ReadRastPortBackgroundPen(AmigaBus bus, uint rastPort)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersRastPortCodec.ReadBackgroundPen(
            ref memory,
            APTR.FromPointer(rastPort));
    }

    private static void WriteRastPortBackgroundPen(
        AmigaBus bus, uint rastPort, byte value)
    {
        var memory = new LayersTestGuestMemory(bus);
        LayersRastPortCodec.WriteBackgroundPen(
            ref memory, APTR.FromPointer(rastPort), value);
    }

    private static byte ReadRastPortDrawMode(AmigaBus bus, uint rastPort)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersRastPortCodec.ReadDrawMode(
            ref memory,
            APTR.FromPointer(rastPort));
    }

    private static void WriteRastPortDrawMode(
        AmigaBus bus, uint rastPort, byte value)
    {
        var memory = new LayersTestGuestMemory(bus);
        LayersRastPortCodec.WriteDrawMode(
            ref memory, APTR.FromPointer(rastPort), value);
    }

    private static byte ReadRastPortMask(AmigaBus bus, uint rastPort)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersRastPortCodec.ReadMask(
            ref memory,
            APTR.FromPointer(rastPort));
    }

    private static void WriteRastPortMask(
        AmigaBus bus, uint rastPort, byte value)
    {
        var memory = new LayersTestGuestMemory(bus);
        LayersRastPortCodec.WriteMask(
            ref memory, APTR.FromPointer(rastPort), value);
    }

    private static ushort ReadRastPortLinePattern(AmigaBus bus, uint rastPort)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersRastPortCodec.ReadLinePattern(
            ref memory,
            APTR.FromPointer(rastPort));
    }

    private static void WriteRastPortLinePattern(
        AmigaBus bus, uint rastPort, ushort value)
    {
        var memory = new LayersTestGuestMemory(bus);
        LayersRastPortCodec.WriteLinePattern(
            ref memory, APTR.FromPointer(rastPort), value);
    }

    private static byte ReadRastPortLinePatternCount(
        AmigaBus bus, uint rastPort)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersRastPortCodec.ReadLinePatternCount(
            ref memory,
            APTR.FromPointer(rastPort));
    }

    private static void WriteRastPortLinePatternCount(
        AmigaBus bus, uint rastPort, byte value)
    {
        var memory = new LayersTestGuestMemory(bus);
        LayersRastPortCodec.WriteLinePatternCount(
            ref memory, APTR.FromPointer(rastPort), value);
    }

    private static ushort ReadRastPortCurrentX(AmigaBus bus, uint rastPort)
    {
        var memory = new LayersTestGuestMemory(bus);
        return unchecked((ushort)LayersRastPortCodec.ReadCurrentX(
            ref memory,
            APTR.FromPointer(rastPort)));
    }

    private static void WriteRastPortCurrentX(
        AmigaBus bus, uint rastPort, short value)
    {
        var memory = new LayersTestGuestMemory(bus);
        LayersRastPortCodec.WriteCurrentX(
            ref memory, APTR.FromPointer(rastPort), value);
    }

    private static ushort ReadRastPortCurrentY(AmigaBus bus, uint rastPort)
    {
        var memory = new LayersTestGuestMemory(bus);
        return unchecked((ushort)LayersRastPortCodec.ReadCurrentY(
            ref memory,
            APTR.FromPointer(rastPort)));
    }

    private static void WriteRastPortCurrentY(
        AmigaBus bus, uint rastPort, short value)
    {
        var memory = new LayersTestGuestMemory(bus);
        LayersRastPortCodec.WriteCurrentY(
            ref memory, APTR.FromPointer(rastPort), value);
    }

    private static void WriteRastPortAreaInfo(
        AmigaBus bus, uint rastPort, uint areaInfo)
    {
        var memory = new LayersTestGuestMemory(bus);
        LayersRastPortCodec.WriteAreaInfo(
            ref memory,
            APTR.FromPointer(rastPort),
            APTR.FromPointer(areaInfo));
    }

    private static void WriteRastPortTextMetrics(
        AmigaBus bus, uint rastPort, short baseline, short height)
    {
        var memory = new LayersTestGuestMemory(bus);
        var address = APTR.FromPointer(rastPort);
        LayersRastPortCodec.WriteTextBaseline(ref memory, address, baseline);
        LayersRastPortCodec.WriteTextHeight(ref memory, address, height);
    }


    private static ushort ReadLayerLockQueueCount(AmigaBus bus, uint layer)
    {
        var memory = new LayersTestGuestMemory(bus);
        return unchecked((ushort)LayersSignalSemaphoreCodec.ReadQueueCount(
            ref memory,
            LayersLayerCodec.LockAddress(APTR.FromPointer(layer))));
    }

    private static uint ReadLayerInfoLockOwner(AmigaBus bus, uint layerInfo)
    {
        var memory = new LayersTestGuestMemory(bus);
        return LayersSignalSemaphoreCodec.ReadOwner(
            ref memory,
            LayersLayerInfoCodec.LockAddress(APTR.FromPointer(layerInfo))).Raw;
    }
}
