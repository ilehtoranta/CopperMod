/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace CopperMod.Amiga.Video.Rtg.CyberGraphics
{
    internal static class CyberGraphicsPlanarBlitter
    {
        private const int BitMapBytesPerRowOffset = 0;
        private const int BitMapRowsOffset = 2;
        private const int BitMapDepthOffset = 5;
        private const int BitMapPlanesOffset = 8;
        private const int MaxPlanes = 8;

        public static int Blit(
            AmigaBus bus,
            uint sourceBitMap,
            int sourceX,
            int sourceY,
            CyberGraphicsSurface destination,
            int destinationX,
            int destinationY,
            int width,
            int height,
            byte minterm,
            byte planeMask,
            uint maskPlane = 0)
        {
            ArgumentNullException.ThrowIfNull(bus);
            ArgumentNullException.ThrowIfNull(destination);
            if (sourceBitMap == 0 || width <= 0 || height <= 0 ||
                !bus.IsMappedMemoryRange(sourceBitMap, BitMapPlanesOffset + MaxPlanes * 4))
            {
                return 0;
            }

            var bytesPerRow = bus.ReadWord(sourceBitMap + BitMapBytesPerRowOffset);
            var rows = bus.ReadWord(sourceBitMap + BitMapRowsOffset);
            var depth = Math.Min(MaxPlanes, (int)bus.ReadByte(sourceBitMap + BitMapDepthOffset));
            if (bytesPerRow == 0 || rows == 0 || depth == 0)
            {
                return 0;
            }

            var maskByteCount = (long)bytesPerRow * rows;
            if (maskPlane != 0 &&
                (maskByteCount > int.MaxValue || !bus.IsMappedMemoryRange(maskPlane, (int)maskByteCount)))
            {
                return 0;
            }

            var operation = (byte)((minterm >> 4) & 0x0F);
            var written = 0;
            for (var y = 0; y < height; y++)
            {
                var sy = sourceY + y;
                var dy = destinationY + y;
                if ((uint)sy >= rows || (uint)dy >= destination.Height)
                {
                    continue;
                }

                for (var x = 0; x < width; x++)
                {
                    var sx = sourceX + x;
                    var dx = destinationX + x;
                    if (sx < 0 || sx >= bytesPerRow * 8 || (uint)dx >= destination.Width)
                    {
                        continue;
                    }

                    if (maskPlane != 0)
                    {
                        var maskOffset = checked((uint)(sy * bytesPerRow + (sx >> 3)));
                        var maskBit = 7 - (sx & 7);
                        if (((bus.ReadByte(maskPlane + maskOffset) >> maskBit) & 1) == 0)
                        {
                            continue;
                        }
                    }

                    var sourcePen = ReadPlanarPen(bus, sourceBitMap, bytesPerRow, depth, sx, sy, planeMask);
                    ApplyPixel(bus, destination, dx, dy, sourcePen, operation, planeMask);
                    written++;
                }
            }

            return written;
        }

        private static byte ReadPlanarPen(
            AmigaBus bus,
            uint bitMap,
            int bytesPerRow,
            int depth,
            int x,
            int y,
            byte planeMask)
        {
            var byteOffset = checked((uint)(y * bytesPerRow + (x >> 3)));
            var bit = 7 - (x & 7);
            var pen = 0;
            for (var plane = 0; plane < depth; plane++)
            {
                if ((planeMask & (1 << plane)) == 0)
                {
                    continue;
                }

                var pointer = bus.ReadLong(bitMap + BitMapPlanesOffset + (uint)(plane * 4));
                var set = pointer == uint.MaxValue ||
                    (pointer != 0 && ((bus.ReadByte(pointer + byteOffset) >> bit) & 1) != 0);
                if (set)
                {
                    pen |= 1 << plane;
                }
            }

            return (byte)pen;
        }

        private static void ApplyPixel(
            AmigaBus bus,
            CyberGraphicsSurface destination,
            int x,
            int y,
            byte sourcePen,
            byte operation,
            byte planeMask)
        {
            var offset = checked(y * destination.BytesPerRow + x * destination.BytesPerPixel);
            switch (destination.PixelFormat)
            {
                case CyberGraphicsPixelFormat.Lut8:
                {
                    var current = destination.ReadByte(bus, offset);
                    var result = (byte)ApplyMinterm(sourcePen, current, operation, 0xFF);
                    destination.WriteByte(bus, offset, (byte)((result & planeMask) | (current & ~planeMask)));
                    return;
                }
                case CyberGraphicsPixelFormat.Rgb15:
                case CyberGraphicsPixelFormat.Rgb15X:
                case CyberGraphicsPixelFormat.Rgb15Pc:
                case CyberGraphicsPixelFormat.Bgr15Pc:
                {
                    var littleEndian = destination.PixelFormat is
                        CyberGraphicsPixelFormat.Rgb15Pc or CyberGraphicsPixelFormat.Bgr15Pc;
                    var shifted = destination.PixelFormat == CyberGraphicsPixelFormat.Rgb15X;
                    var bgr = destination.PixelFormat == CyberGraphicsPixelFormat.Bgr15Pc;
                    var current = littleEndian
                        ? (ushort)(destination.ReadByte(bus, offset) |
                            (destination.ReadByte(bus, offset + 1) << 8))
                        : (ushort)((destination.ReadByte(bus, offset) << 8) |
                            destination.ReadByte(bus, offset + 1));
                    var mapped = EncodeRgb15(destination.Palette[sourcePen], bgr, shifted);
                    var mask = shifted ? 0xFFFEu : 0x7FFFu;
                    uint EncodePen(byte pen) => EncodeRgb15(destination.Palette[pen], bgr, shifted);
                    var result = (ushort)(operation switch
                    {
                        0 => EncodePen(0),
                        3 => EncodePen((byte)~sourcePen),
                        5 => ~current & mask,
                        10 => current & mask,
                        12 => mapped,
                        15 => EncodePen(255),
                        _ => ApplyMinterm(mapped, current, operation, mask)
                    });
                    destination.WriteByte(bus, offset, littleEndian ? (byte)result : (byte)(result >> 8));
                    destination.WriteByte(bus, offset + 1, littleEndian ? (byte)(result >> 8) : (byte)result);
                    return;
                }
                case CyberGraphicsPixelFormat.Rgb16:
                {
                    var current = (ushort)((destination.ReadByte(bus, offset) << 8) |
                        destination.ReadByte(bus, offset + 1));
                    var mapped = EncodeRgb16(destination.Palette[sourcePen]);
                    var result = (ushort)ApplyDirectMinterm(destination, sourcePen, mapped, current, operation, 0xFFFF);
                    destination.WriteByte(bus, offset, (byte)(result >> 8));
                    destination.WriteByte(bus, offset + 1, (byte)result);
                    return;
                }
                case CyberGraphicsPixelFormat.Argb32:
                {
                    var current = ((uint)destination.ReadByte(bus, offset) << 24) |
                        ((uint)destination.ReadByte(bus, offset + 1) << 16) |
                        ((uint)destination.ReadByte(bus, offset + 2) << 8) |
                        destination.ReadByte(bus, offset + 3);
                    var mapped = destination.Palette[sourcePen];
                    var rgb = ApplyDirectMinterm(destination, sourcePen, mapped, current, operation, 0x00FF_FFFF);
                    var result = (current & 0xFF00_0000u) | (rgb & 0x00FF_FFFFu);
                    destination.WriteByte(bus, offset, (byte)(result >> 24));
                    destination.WriteByte(bus, offset + 1, (byte)(result >> 16));
                    destination.WriteByte(bus, offset + 2, (byte)(result >> 8));
                    destination.WriteByte(bus, offset + 3, (byte)result);
                    return;
                }
                default:
                    return;
            }
        }

        private static uint ApplyDirectMinterm(
            CyberGraphicsSurface destination,
            byte sourcePen,
            uint mapped,
            uint current,
            byte operation,
            uint mask)
        {
            return operation switch
            {
                0 => destination.Palette[0] & mask,
                3 => destination.Palette[(byte)~sourcePen] & mask,
                5 => ~current & mask,
                10 => current & mask,
                12 => mapped & mask,
                15 => destination.Palette[255] & mask,
                _ => ApplyMinterm(mapped, current, operation, mask)
            };
        }

        private static uint ApplyMinterm(uint source, uint destination, byte operation, uint mask)
        {
            var result = 0u;
            if ((operation & 0x8) != 0) result |= source & destination;
            if ((operation & 0x4) != 0) result |= source & ~destination;
            if ((operation & 0x2) != 0) result |= ~source & destination;
            if ((operation & 0x1) != 0) result |= ~source & ~destination;
            return result & mask;
        }

        private static ushort EncodeRgb16(uint argb)
        {
            var red = (argb >> 16) & 0xFF;
            var green = (argb >> 8) & 0xFF;
            var blue = argb & 0xFF;
            return (ushort)(((red >> 3) << 11) | ((green >> 2) << 5) | (blue >> 3));
        }

        private static ushort EncodeRgb15(uint argb, bool bgr, bool shifted)
        {
            var red = (argb >> 16) & 0xFF;
            var green = (argb >> 8) & 0xFF;
            var blue = argb & 0xFF;
            var first = bgr ? blue : red;
            var last = bgr ? red : blue;
            var value = (ushort)(((first >> 3) << 10) | ((green >> 3) << 5) | (last >> 3));
            return shifted ? (ushort)(value << 1) : value;
        }
    }
}
