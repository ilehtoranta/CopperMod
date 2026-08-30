/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using Amiga;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga
{
    internal sealed partial class AmigaBootController
    {
        /// <summary>
        /// Allocation-free logical bitmap projected through the exact
        /// LayersRasterCore endpoint. Portable graphics primitives see one
        /// non-layered bitmap in layer-local coordinates; its plane bytes are
        /// synthesized from, and written back to, the validated public
        /// ClipRect fragments. This keeps cursor/text/area state single-shot
        /// while mapping visible display and obscured backing sinks correctly.
        /// </summary>
        private sealed class ValidatedLayersRasterMemory : IGraphicsMemory
        {
            private const uint VirtualBitMapAddress = 0x7FFF_FF00;
            private const uint VirtualPlaneBase = 0x8000_0000;
            private readonly AmigaBootController _owner;
            private uint _rastPort;
            private uint _displayBitMap;
            private int _logicalWidth;
            private int _logicalOriginX;
            private int _logicalOriginY;
            private int _screenOffsetX;
            private int _screenOffsetY;
            private uint _polyPointArray;
            private uint _polyPointCount;
            private uint _areaVectorTable;
            private uint _areaFlagTable;
            private uint _areaVectorCount;
            private ushort _bytesPerRow;
            private ushort _rows;
            private byte _depth;
            private uint _planeBytes;
            private uint _planeStride;
            private bool _active;

            internal ValidatedLayersRasterMemory(AmigaBootController owner)
                => _owner = owner;

            internal int LogicalOriginX => _logicalOriginX;
            internal int LogicalOriginY => _logicalOriginY;

            internal bool Configure(uint rastPort, int width, int height, int depth)
            {
                Reset();
                if (rastPort == 0 || width <= 0 || width > ushort.MaxValue ||
                    height <= 0 || height > ushort.MaxValue ||
                    depth <= 0 || depth > 8)
                {
                    return false;
                }

                var memory = new LayersGuestMemory(_owner);
                var layer = APTR.FromPointer(
                    _owner._validatedPrimaryEndpoint.Layer);
                if (!_owner.TryGetRastPortBitMap(rastPort, out var displayBitMap) ||
                    !LayersLayerCodec.TryReadBounds(
                        ref memory,
                        layer,
                        out var layerBounds))
                {
                    return false;
                }
                var scrollX = LayersLayerCodec.ReadScrollX(ref memory, layer);
                var scrollY = LayersLayerCodec.ReadScrollY(ref memory, layer);

                var bytesPerRow = checked(((width + 15) / 16) * 2);
                var planeBytes = (ulong)(uint)bytesPerRow * (uint)height;
                var planeStride = (planeBytes + 15u) & ~15u;
                var allPlanes = planeStride * (uint)depth;
                if (bytesPerRow > ushort.MaxValue || planeBytes == 0 ||
                    planeBytes > uint.MaxValue || planeStride > uint.MaxValue ||
                    allPlanes > uint.MaxValue - VirtualPlaneBase)
                {
                    return false;
                }

                _rastPort = rastPort;
                _displayBitMap = displayBitMap;
                _logicalWidth = width;
                var superDomain =
                    _owner._validatedPrimaryEndpoint.FirstSuperClipRect != 0;
                _logicalOriginX = superDomain ? 0 : scrollX;
                _logicalOriginY = superDomain ? 0 : scrollY;
                // screen = logical + Bounds.Min - Scroll. Virtual coordinates
                // are logical minus the selected domain origin (Scroll for a
                // viewport, zero for the whole caller SuperBitMap).
                _screenOffsetX = checked(
                    layerBounds.MinX - scrollX + _logicalOriginX);
                _screenOffsetY = checked(
                    layerBounds.MinY - scrollY + _logicalOriginY);
                if (_owner._portableGraphicsMemory.TryReadLong(
                        rastPort + (uint)GraphicsLayouts.RastPortAreaInfo,
                        out var areaInfo) &&
                    areaInfo != 0)
                {
                    _ = _owner._portableGraphicsMemory.TryReadLong(
                        areaInfo + (uint)GraphicsLayouts.AreaInfoVectorTable,
                        out _areaVectorTable);
                    _ = _owner._portableGraphicsMemory.TryReadLong(
                        areaInfo + (uint)GraphicsLayouts.AreaInfoFlagTable,
                        out _areaFlagTable);
                    if (_owner._portableGraphicsMemory.TryReadWord(
                            areaInfo + (uint)GraphicsLayouts.AreaInfoCount,
                            out var areaCount))
                    {
                        _areaVectorCount = areaCount;
                    }
                }
                _bytesPerRow = checked((ushort)bytesPerRow);
                _rows = checked((ushort)height);
                _depth = checked((byte)depth);
                _planeBytes = checked((uint)planeBytes);
                _planeStride = checked((uint)planeStride);
                _active = true;
                return true;
            }

            internal void Reset()
            {
                _rastPort = 0;
                _displayBitMap = 0;
                _logicalWidth = 0;
                _logicalOriginX = 0;
                _logicalOriginY = 0;
                _screenOffsetX = 0;
                _screenOffsetY = 0;
                _polyPointArray = 0;
                _polyPointCount = 0;
                _areaVectorTable = 0;
                _areaFlagTable = 0;
                _areaVectorCount = 0;
                _bytesPerRow = 0;
                _rows = 0;
                _depth = 0;
                _planeBytes = 0;
                _planeStride = 0;
                _active = false;
            }

            public bool TryReadByte(uint address, out byte value)
            {
                value = 0;
                if (!_active)
                    return _owner._portableGraphicsMemory.TryReadByte(address, out value);
                if (TryReadRastPortOverlayByte(address, out value))
                    return true;
                if (TryReadVirtualBitMapByte(address, out value))
                    return true;
                if (TryDecodeVirtualPlaneByte(address, out var plane, out var byteOffset))
                    return TryReadVirtualPlaneByte(plane, byteOffset, out value);
                return _owner._portableGraphicsMemory.TryReadByte(address, out value);
            }

            public bool TryReadWord(uint address, out ushort value)
            {
                value = 0;
                var projectedCoordinate = false;
                if (_active && TryReadProjectedCoordinate(
                        address,
                        out value,
                        out projectedCoordinate))
                    return true;
                if (projectedCoordinate)
                    return false;
                if (!TouchesVirtualAddress(address, 2))
                    return _owner._portableGraphicsMemory.TryReadWord(address, out value);
                value = 0;
                if (!TryReadByte(address, out var high) ||
                    !TryReadByte(address + 1, out var low))
                {
                    return false;
                }
                value = (ushort)((high << 8) | low);
                return true;
            }

            internal void ConfigurePolyPoints(uint address, uint count)
            {
                _polyPointArray = address;
                _polyPointCount = count;
            }

            internal bool TryProjectX(short logical, out short projected)
                => TryProject(logical, _logicalOriginX, out projected);

            internal bool TryProjectY(short logical, out short projected)
                => TryProject(logical, _logicalOriginY, out projected);

            private static bool TryProject(short logical, int origin, out short projected)
            {
                var value = (int)logical - origin;
                if (value < short.MinValue || value > short.MaxValue)
                {
                    projected = 0;
                    return false;
                }
                projected = (short)value;
                return true;
            }

            private bool TryReadProjectedCoordinate(
                uint address,
                out ushort value,
                out bool handled)
            {
                value = 0;
                handled = TryGetProjectedCoordinateOrigin(address, out var origin);
                if (!handled)
                    return false;
                if (!_owner._portableGraphicsMemory.TryReadWord(address, out var raw))
                    return false;
                var projected = (int)unchecked((short)raw) - origin;
                if (projected < short.MinValue || projected > short.MaxValue)
                    return false;
                value = unchecked((ushort)(short)projected);
                return true;
            }

            private bool TryWriteProjectedCoordinate(
                uint address,
                ushort value,
                out bool handled)
            {
                handled = false;
                var currentX = _rastPort +
                    (uint)GraphicsLayouts.RastPortCurrentX;
                var currentY = _rastPort +
                    (uint)GraphicsLayouts.RastPortCurrentY;
                var origin = 0;
                if (address == currentX)
                {
                    handled = true;
                    origin = _logicalOriginX;
                }
                else if (address == currentY)
                {
                    handled = true;
                    origin = _logicalOriginY;
                }
                if (!handled)
                    return false;
                var logical = (int)unchecked((short)value) + origin;
                return logical >= short.MinValue && logical <= short.MaxValue &&
                    _owner._portableGraphicsMemory.TryWriteWord(
                        address,
                        unchecked((ushort)(short)logical));
            }

            private bool TryGetProjectedCoordinateOrigin(
                uint address,
                out int origin)
            {
                origin = 0;
                var currentX = _rastPort +
                    (uint)GraphicsLayouts.RastPortCurrentX;
                var currentY = _rastPort +
                    (uint)GraphicsLayouts.RastPortCurrentY;
                if (address == currentX)
                {
                    origin = _logicalOriginX;
                    return true;
                }
                if (address == currentY)
                {
                    origin = _logicalOriginY;
                    return true;
                }
                if (TryGetVectorCoordinate(
                        _polyPointArray,
                        _polyPointCount,
                        address,
                        out var polyIsY,
                        out _))
                {
                    origin = polyIsY ? _logicalOriginY : _logicalOriginX;
                    return true;
                }
                if (!TryGetVectorCoordinate(
                        _areaVectorTable,
                        _areaVectorCount,
                        address,
                        out var areaIsY,
                        out var areaIndex))
                {
                    return false;
                }

                // An ellipse consumes two consecutive flag-3 vectors: the
                // first is its logical centre, the second is an unshifted
                // radius pair. Walk the bounded grammar to distinguish them.
                var cursor = 0u;
                while (cursor < _areaVectorCount)
                {
                    if (!_owner._portableGraphicsMemory.TryReadByte(
                            _areaFlagTable + cursor,
                            out var flag))
                    {
                        return false;
                    }
                    if (flag == 3)
                    {
                        if (areaIndex == cursor + 1)
                        {
                            origin = 0;
                            return true;
                        }
                        if (areaIndex == cursor)
                        {
                            origin = areaIsY
                                ? _logicalOriginY
                                : _logicalOriginX;
                            return true;
                        }
                        cursor += 2;
                        continue;
                    }
                    if (areaIndex == cursor)
                    {
                        origin = areaIsY ? _logicalOriginY : _logicalOriginX;
                        return true;
                    }
                    cursor++;
                }
                return false;
            }

            private static bool TryGetVectorCoordinate(
                uint vectorTable,
                uint count,
                uint address,
                out bool isY,
                out uint index)
            {
                isY = false;
                index = 0;
                if (vectorTable == 0 || count == 0 || address < vectorTable)
                    return false;
                var relative = address - vectorTable;
                var byteCount = (ulong)count * 4u;
                if (relative >= byteCount || (relative & 1u) != 0 ||
                    (relative & 3u) is not (0u or 2u))
                {
                    return false;
                }
                index = relative / 4u;
                isY = (relative & 3u) == 2u;
                return true;
            }

            public bool TryReadLong(uint address, out uint value)
            {
                if (!TouchesVirtualAddress(address, 4))
                    return _owner._portableGraphicsMemory.TryReadLong(address, out value);
                value = 0;
                for (var offset = 0u; offset < 4; offset++)
                {
                    if (!TryReadByte(address + offset, out var item))
                        return false;
                    value = (value << 8) | item;
                }
                return true;
            }

            public bool TryWriteByte(uint address, byte value)
            {
                if (!_active)
                    return _owner._portableGraphicsMemory.TryWriteByte(address, value);
                if (IsRastPortOverlayByte(address) || IsVirtualBitMapByte(address))
                    return false;
                if (TryDecodeVirtualPlaneByte(address, out var plane, out var byteOffset))
                    return TryWriteVirtualPlaneByte(plane, byteOffset, value);
                return _owner._portableGraphicsMemory.TryWriteByte(address, value);
            }

            public bool TryWriteWord(uint address, ushort value)
            {
                var handled = false;
                if (_active && TryWriteProjectedCoordinate(address, value, out handled))
                    return true;
                if (handled)
                    return false;
                if (!TouchesVirtualAddress(address, 2))
                    return _owner._portableGraphicsMemory.TryWriteWord(address, value);
                return TryWriteByte(address, (byte)(value >> 8)) &&
                    TryWriteByte(address + 1, (byte)value);
            }

            public bool TryWriteLong(uint address, uint value)
            {
                if (!TouchesVirtualAddress(address, 4))
                    return _owner._portableGraphicsMemory.TryWriteLong(address, value);
                return TryWriteByte(address, (byte)(value >> 24)) &&
                    TryWriteByte(address + 1, (byte)(value >> 16)) &&
                    TryWriteByte(address + 2, (byte)(value >> 8)) &&
                    TryWriteByte(address + 3, (byte)value);
            }

            private bool TryReadRastPortOverlayByte(uint address, out byte value)
            {
                value = 0;
                var rastPort = APTR.FromPointer(_rastPort);
                var bitMapField = LayersRastPortCodec.BitMapAddress(rastPort).Raw;
                if (address >= bitMapField && address < bitMapField + 4)
                {
                    value = BigEndianByte(VirtualBitMapAddress, address - bitMapField);
                    return true;
                }
                var layerField = LayersRastPortCodec.LayerAddress(rastPort).Raw;
                return address >= layerField && address < layerField + 4;
            }

            private bool IsRastPortOverlayByte(uint address)
            {
                var rastPort = APTR.FromPointer(_rastPort);
                var bitMapField = LayersRastPortCodec.BitMapAddress(rastPort).Raw;
                var layerField = LayersRastPortCodec.LayerAddress(rastPort).Raw;
                return (address >= bitMapField && address < bitMapField + 4) ||
                    (address >= layerField && address < layerField + 4);
            }

            private bool TryReadVirtualBitMapByte(uint address, out byte value)
            {
                value = 0;
                if (!IsVirtualBitMapByte(address))
                    return false;
                var offset = checked((int)(address - VirtualBitMapAddress));
                value = offset switch
                {
                    GraphicsLayouts.BitMapBytesPerRow => (byte)(_bytesPerRow >> 8),
                    GraphicsLayouts.BitMapBytesPerRow + 1 => (byte)_bytesPerRow,
                    GraphicsLayouts.BitMapRows => (byte)(_rows >> 8),
                    GraphicsLayouts.BitMapRows + 1 => (byte)_rows,
                    GraphicsLayouts.BitMapDepth => _depth,
                    _ => TryReadVirtualPlanePointerByte(offset, out var pointerByte)
                        ? pointerByte
                        : (byte)0
                };
                return true;
            }

            private bool TryReadVirtualPlanePointerByte(int offset, out byte value)
            {
                value = 0;
                var relative = offset - GraphicsLayouts.BitMapPlanes;
                if (relative < 0 || relative >= 8 * sizeof(uint))
                    return false;
                var plane = relative / sizeof(uint);
                if (plane >= _depth)
                    return true;
                var pointer = checked(VirtualPlaneBase + ((uint)plane * _planeStride));
                value = BigEndianByte(pointer, checked((uint)(relative & 3)));
                return true;
            }

            private static byte BigEndianByte(uint value, uint offset)
                => (byte)(value >> checked((int)((3 - offset) * 8)));

            private static bool IsVirtualBitMapByte(uint address)
                => address >= VirtualBitMapAddress &&
                    address < VirtualBitMapAddress + GraphicsLayouts.BitMapSize;

            private bool TryDecodeVirtualPlaneByte(
                uint address,
                out int plane,
                out uint byteOffset)
            {
                plane = 0;
                byteOffset = 0;
                if (!_active || address < VirtualPlaneBase || _planeStride == 0)
                    return false;
                var relative = address - VirtualPlaneBase;
                var candidatePlane = relative / _planeStride;
                var candidateOffset = relative % _planeStride;
                if (candidatePlane >= _depth || candidateOffset >= _planeBytes)
                    return false;
                plane = checked((int)candidatePlane);
                byteOffset = candidateOffset;
                return true;
            }

            private bool TryReadVirtualPlaneByte(
                int plane,
                uint byteOffset,
                out byte value)
            {
                value = 0;
                var y = checked((int)(byteOffset / _bytesPerRow));
                var firstX = checked((int)(byteOffset % _bytesPerRow) * 8);
                for (var bit = 0; bit < 8; bit++)
                {
                    var x = firstX + bit;
                    if (x >= _bytesPerRow * 8 || y >= _rows ||
                        !TryReadProjectedPlaneBit(x, y, plane, out var set))
                    {
                        return false;
                    }
                    if (set)
                        value |= (byte)(0x80 >> bit);
                }
                return true;
            }

            private bool TryWriteVirtualPlaneByte(
                int plane,
                uint byteOffset,
                byte value)
            {
                var y = checked((int)(byteOffset / _bytesPerRow));
                var firstX = checked((int)(byteOffset % _bytesPerRow) * 8);
                // Preflight all mapped targets before the first write so a
                // malformed late fragment cannot expose a partial byte.
                for (var bit = 0; bit < 8; bit++)
                {
                    var x = firstX + bit;
                    if (!TryReadProjectedPlaneBit(x, y, plane, out _))
                        return false;
                }
                for (var bit = 0; bit < 8; bit++)
                {
                    var x = firstX + bit;
                    if (!TryWriteProjectedPlaneBit(
                            x,
                            y,
                            plane,
                            (value & (0x80 >> bit)) != 0))
                    {
                        return false;
                    }
                }
                return true;
            }

            private bool TryReadProjectedPlaneBit(
                int logicalX,
                int logicalY,
                int plane,
                out bool set)
            {
                set = false;
                if (!TryFindProjectedPixel(
                        logicalX + _logicalOriginX,
                        logicalY + _logicalOriginY,
                        out var bitMap,
                        out var bitMapX,
                        out var bitMapY))
                {
                    // Destination clipping does not remove ScrollRaster's
                    // source samples. For a logical pixel outside the
                    // published sink fragments, read the layer display
                    // bitmap at the native logical-to-screen transform while
                    // keeping writes suppressed. Padding remains zero.
                    if (logicalX < 0 || logicalX >= _logicalWidth ||
                        logicalY < 0 || logicalY >= _rows)
                    {
                        return true;
                    }
                    return TryReadTargetPlaneBit(
                        _displayBitMap,
                        logicalX + _screenOffsetX,
                        logicalY + _screenOffsetY,
                        plane,
                        out set);
                }
                return TryReadTargetPlaneBit(
                    bitMap,
                    bitMapX,
                    bitMapY,
                    plane,
                    out set);
            }

            private bool TryWriteProjectedPlaneBit(
                int logicalX,
                int logicalY,
                int plane,
                bool set)
            {
                if (!TryFindProjectedPixel(
                        logicalX + _logicalOriginX,
                        logicalY + _logicalOriginY,
                        out var bitMap,
                        out var bitMapX,
                        out var bitMapY))
                {
                    return true;
                }
                return TryWriteTargetPlaneBit(
                    bitMap,
                    bitMapX,
                    bitMapY,
                    plane,
                    set);
            }

            private bool TryFindProjectedPixel(
                int logicalX,
                int logicalY,
                out uint bitMap,
                out int bitMapX,
                out int bitMapY)
            {
                foreach (var fragment in _owner._validatedVisibilityFragments)
                {
                    if (logicalX < fragment.RequestLeft ||
                        logicalX > fragment.RequestRight ||
                        logicalY < fragment.RequestTop ||
                        logicalY > fragment.RequestBottom)
                    {
                        continue;
                    }
                    bitMap = fragment.BitMap;
                    bitMapX = fragment.BitMapLeft +
                        logicalX - fragment.RequestLeft;
                    bitMapY = fragment.BitMapTop +
                        logicalY - fragment.RequestTop;
                    return true;
                }
                bitMap = 0;
                bitMapX = 0;
                bitMapY = 0;
                return false;
            }

            private bool TryReadTargetPlaneBit(
                uint bitMap,
                int x,
                int y,
                int plane,
                out bool set)
            {
                set = false;
                if (_owner._cyberGraphics is { } cyberGraphics &&
                    cyberGraphics.TryGetBitMapSurface(bitMap, out var surface))
                {
                    if (x < 0 || y < 0 || x >= surface.Width ||
                        y >= surface.Height || plane < 0 || plane >= 8)
                    {
                        return false;
                    }
                    var pen = cyberGraphics.ReadSurfacePen(surface, x, y);
                    set = (pen & (1 << plane)) != 0;
                    return true;
                }

                return TryGetPlanarPlaneByte(
                        bitMap,
                        x,
                        y,
                        plane,
                        out var address,
                        out var mask) &&
                    TryReadPlanarBit(address, mask, out set);
            }

            private bool TryWriteTargetPlaneBit(
                uint bitMap,
                int x,
                int y,
                int plane,
                bool set)
            {
                if (_owner._cyberGraphics is { } cyberGraphics &&
                    cyberGraphics.TryGetBitMapSurface(bitMap, out var surface))
                {
                    if (x < 0 || y < 0 || x >= surface.Width ||
                        y >= surface.Height || plane < 0 || plane >= 8)
                    {
                        return false;
                    }
                    var pen = cyberGraphics.ReadSurfacePen(surface, x, y);
                    pen = (byte)(set
                        ? pen | (1 << plane)
                        : pen & ~(1 << plane));
                    cyberGraphics.WriteSurfacePen(
                        surface,
                        x,
                        y,
                        (byte)pen,
                        (byte)(1 << plane));
                    return true;
                }

                if (!TryGetPlanarPlaneByte(
                        bitMap,
                        x,
                        y,
                        plane,
                        out var address,
                        out var mask) ||
                    !_owner._machine.Bus.IsMappedMemoryRange(address, 1))
                {
                    return false;
                }
                var value = _owner._machine.Bus.ReadByte(address);
                value = set ? (byte)(value | mask) : (byte)(value & ~mask);
                _owner._machine.Bus.WriteByte(address, value, 0);
                return true;
            }

            private bool TryGetPlanarPlaneByte(
                uint bitMap,
                int x,
                int y,
                int plane,
                out uint address,
                out byte mask)
            {
                address = 0;
                mask = 0;
                var memory = new LayersGuestMemory(_owner);
                var bitMapAddress = APTR.FromPointer(bitMap);
                if (!LayersBitMapCodec.IsMapped(ref memory, bitMapAddress))
                {
                    return false;
                }
                var bytesPerRow = LayersBitMapCodec.ReadBytesPerRow(
                    ref memory,
                    bitMapAddress);
                var rows = LayersBitMapCodec.ReadRows(ref memory, bitMapAddress);
                var depth = LayersBitMapCodec.ReadDepth(ref memory, bitMapAddress);
                if (bytesPerRow == 0 || (bytesPerRow & 1) != 0 || rows == 0 ||
                    depth == 0 || depth > 8 || plane < 0 || plane >= depth ||
                    x < 0 || y < 0 || x >= bytesPerRow * 8 || y >= rows)
                {
                    return false;
                }
                var planeAddress = LayersBitMapCodec.ReadPlane(
                    ref memory,
                    bitMapAddress,
                    plane).Raw;
                var byteOffset = checked((ulong)(uint)y * bytesPerRow +
                    (uint)(x >> 3));
                if (planeAddress == 0 || byteOffset > uint.MaxValue - planeAddress)
                    return false;
                address = planeAddress + checked((uint)byteOffset);
                mask = (byte)(0x80 >> (x & 7));
                return _owner._machine.Bus.IsMappedMemoryRange(address, 1);
            }

            private bool TryReadPlanarBit(uint address, byte mask, out bool set)
            {
                set = false;
                if (!_owner._machine.Bus.IsMappedMemoryRange(address, 1))
                    return false;
                set = (_owner._machine.Bus.ReadByte(address) & mask) != 0;
                return true;
            }

            private bool TouchesVirtualAddress(uint address, uint byteCount)
            {
                for (var offset = 0u; offset < byteCount; offset++)
                {
                    var current = address + offset;
                    if (IsRastPortOverlayByte(current) ||
                        IsVirtualBitMapByte(current) ||
                        TryDecodeVirtualPlaneByte(current, out _, out _))
                    {
                        return true;
                    }
                }
                return false;
            }
        }
    }
}
