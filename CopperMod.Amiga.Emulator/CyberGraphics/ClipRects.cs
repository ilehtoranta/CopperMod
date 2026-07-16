/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;

namespace CopperMod.Amiga
{
    internal sealed partial class AmigaBootController
    {
        private const int RastPortLayerOffset = 0x00;
        private const int LayerClipRectOffset = 0x08;
        private const int LayerBoundsOffset = 0x10;
        private const int ClipRectNextOffset = 0x00;
        private const int ClipRectBitMapOffset = 0x0C;
        private const int ClipRectBoundsOffset = 0x10;
        private const int RectangleSize = 0x08;
        private const int MaximumClipRects = 4096;

        private readonly record struct RasterClipFragment(
            uint BitMap,
            int RequestLeft,
            int RequestTop,
            int RequestRight,
            int RequestBottom,
            int BitMapLeft,
            int BitMapTop)
        {
            public int Width => RequestRight - RequestLeft + 1;
            public int Height => RequestBottom - RequestTop + 1;
        }

        private readonly record struct RasterBlitOperation(
            uint SourceBitMap,
            int SourceX,
            int SourceY,
            uint DestinationBitMap,
            int DestinationX,
            int DestinationY,
            int Width,
            int Height,
            bool SourceIsRtg,
            bool DestinationIsRtg);

        private bool TryGetRastPortExtent(uint rastPort, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (!TryGetRastPortBitMap(rastPort, out var bitMap))
            {
                return false;
            }

            var layer = _machine.Bus.ReadLong(rastPort + RastPortLayerOffset);
            if (layer != 0 && TryReadRectangle(layer + LayerBoundsOffset, out var bounds))
            {
                width = bounds.Right - bounds.Left + 1;
                height = bounds.Bottom - bounds.Top + 1;
                return width > 0 && height > 0;
            }

            if (!TryReadBitMapInfo(bitMap, out var info))
            {
                return false;
            }

            width = info.Width;
            height = info.Height;
            return true;
        }

        private List<RasterClipFragment> GetRastPortClipFragments(
            uint rastPort,
            int left,
            int top,
            int right,
            int bottom)
        {
            var fragments = new List<RasterClipFragment>();
            if (!TryGetRastPortBitMap(rastPort, out var rastPortBitMap))
            {
                return fragments;
            }

            NormalizeRectangle(ref left, ref top, ref right, ref bottom);
            var layer = _machine.Bus.ReadLong(rastPort + RastPortLayerOffset);
            if (layer == 0)
            {
                if (TryReadBitMapInfo(rastPortBitMap, out var info) &&
                    TryIntersect(left, top, right, bottom, 0, 0, info.Width - 1, info.Height - 1,
                        out var intersection))
                {
                    fragments.Add(new RasterClipFragment(
                        rastPortBitMap,
                        intersection.Left,
                        intersection.Top,
                        intersection.Right,
                        intersection.Bottom,
                        intersection.Left,
                        intersection.Top));
                }

                return fragments;
            }

            if (!_machine.Bus.IsMappedMemoryRange(layer, LayerBoundsOffset + RectangleSize) ||
                !TryReadRectangle(layer + LayerBoundsOffset, out var layerBounds))
            {
                return fragments;
            }

            var globalLeft = (long)left + layerBounds.Left;
            var globalTop = (long)top + layerBounds.Top;
            var globalRight = (long)right + layerBounds.Left;
            var globalBottom = (long)bottom + layerBounds.Top;
            if (globalLeft < int.MinValue || globalTop < int.MinValue ||
                globalRight > int.MaxValue || globalBottom > int.MaxValue)
            {
                return fragments;
            }

            var clipRect = _machine.Bus.ReadLong(layer + LayerClipRectOffset);
            var visited = new HashSet<uint>();
            for (var count = 0;
                clipRect != 0 && count < MaximumClipRects && visited.Add(clipRect);
                count++)
            {
                if ((clipRect & 1) != 0 ||
                    !_machine.Bus.IsMappedMemoryRange(clipRect, ClipRectBoundsOffset + RectangleSize))
                {
                    break;
                }

                var next = _machine.Bus.ReadLong(clipRect + ClipRectNextOffset);
                if (TryReadRectangle(clipRect + ClipRectBoundsOffset, out var clipBounds) &&
                    TryIntersect(
                        (int)globalLeft, (int)globalTop, (int)globalRight, (int)globalBottom,
                        clipBounds.Left, clipBounds.Top, clipBounds.Right, clipBounds.Bottom,
                        out var intersection))
                {
                    var clipBitMap = _machine.Bus.ReadLong(clipRect + ClipRectBitMapOffset);
                    var targetBitMap = clipBitMap != 0 ? clipBitMap : rastPortBitMap;
                    var bitMapLeft = clipBitMap != 0
                        ? intersection.Left - clipBounds.Left
                        : intersection.Left;
                    var bitMapTop = clipBitMap != 0
                        ? intersection.Top - clipBounds.Top
                        : intersection.Top;
                    fragments.Add(new RasterClipFragment(
                        targetBitMap,
                        intersection.Left - layerBounds.Left,
                        intersection.Top - layerBounds.Top,
                        intersection.Right - layerBounds.Left,
                        intersection.Bottom - layerBounds.Top,
                        bitMapLeft,
                        bitMapTop));
                }

                clipRect = next;
            }

            return fragments;
        }

        private void FillRastPortRect(
            uint rastPort,
            int left,
            int top,
            int right,
            int bottom,
            int color)
        {
            var writeMask = ReadRastPortMask(rastPort);
            foreach (var fragment in GetRastPortClipFragments(rastPort, left, top, right, bottom))
            {
                FillBitMapRect(
                    fragment.BitMap,
                    fragment.BitMapLeft,
                    fragment.BitMapTop,
                    fragment.BitMapLeft + fragment.Width - 1,
                    fragment.BitMapTop + fragment.Height - 1,
                    color,
                    writeMask);
            }
        }

        private void WriteClippedRastPortPixel(
            IReadOnlyList<RasterClipFragment> fragments,
            int x,
            int y,
            int color,
            byte writeMask)
        {
            foreach (var fragment in fragments)
            {
                if (x >= fragment.RequestLeft && x <= fragment.RequestRight &&
                    y >= fragment.RequestTop && y <= fragment.RequestBottom &&
                    TryReadBitMapInfo(fragment.BitMap, out var info))
                {
                    WriteBitMapPixel(
                        info,
                        fragment.BitMapLeft + x - fragment.RequestLeft,
                        fragment.BitMapTop + y - fragment.RequestTop,
                        color,
                        writeMask);
                }
            }
        }

        private void DrawRastPortLine(uint rastPort, int x0, int y0, int x1, int y1, int color)
        {
            var fragments = GetRastPortClipFragments(
                rastPort,
                Math.Min(x0, x1),
                Math.Min(y0, y1),
                Math.Max(x0, x1),
                Math.Max(y0, y1));
            var writeMask = ReadRastPortMask(rastPort);
            var dx = Math.Abs(x1 - x0);
            var sx = x0 < x1 ? 1 : -1;
            var dy = -Math.Abs(y1 - y0);
            var sy = y0 < y1 ? 1 : -1;
            var error = dx + dy;
            while (true)
            {
                WriteClippedRastPortPixel(fragments, x0, y0, color, writeMask);
                if (x0 == x1 && y0 == y1)
                {
                    return;
                }

                var doubleError = error * 2;
                if (doubleError >= dy)
                {
                    error += dy;
                    x0 += sx;
                }

                if (doubleError <= dx)
                {
                    error += dx;
                    y0 += sy;
                }
            }
        }

        private void DrawRastPortGlyph(
            uint rastPort,
            char character,
            int x,
            int y,
            int foreground,
            int background,
            int drawMode)
        {
            var fragments = GetRastPortClipFragments(rastPort, x, y, x + 7, y + 7);
            var writeMask = ReadRastPortMask(rastPort);
            var glyph = SyntheticGlyph(character);
            for (var row = 0; row < 8; row++)
            {
                for (var column = 0; column < 8; column++)
                {
                    var set = row < 7 && column < 5 &&
                        (((glyph >> ((6 - row) * 5)) & (ulong)(0x10 >> column)) != 0);
                    if (set)
                    {
                        WriteClippedRastPortPixel(
                            fragments, x + column, y + row, foreground, writeMask);
                    }
                    else if ((drawMode & 1) != 0)
                    {
                        WriteClippedRastPortPixel(
                            fragments, x + column, y + row, background, writeMask);
                    }
                }
            }
        }

        private int BlitBitMapToRastPortClipped(
            M68kCpuState state,
            uint sourceBitMap,
            uint destinationRastPort,
            uint maskPlane = 0)
        {
            var sourceX = unchecked((short)(ushort)state.D[0]);
            var sourceY = unchecked((short)(ushort)state.D[1]);
            var destinationX = unchecked((short)(ushort)state.D[2]);
            var destinationY = unchecked((short)(ushort)state.D[3]);
            var width = unchecked((short)(ushort)state.D[4]);
            var height = unchecked((short)(ushort)state.D[5]);
            if (width <= 0 || height <= 0)
            {
                return 0;
            }

            var sourceIsRtg = CyberGraphics.IsRtgBitMap(sourceBitMap);
            var operations = new List<RasterBlitOperation>();
            foreach (var fragment in GetRastPortClipFragments(
                destinationRastPort,
                destinationX,
                destinationY,
                destinationX + width - 1,
                destinationY + height - 1))
            {
                var destinationIsRtg = CyberGraphics.IsRtgBitMap(fragment.BitMap);
                if (!sourceIsRtg && !destinationIsRtg)
                {
                    continue;
                }

                var deltaX = fragment.RequestLeft - destinationX;
                var deltaY = fragment.RequestTop - destinationY;
                operations.Add(new RasterBlitOperation(
                    sourceBitMap,
                    sourceX + deltaX,
                    sourceY + deltaY,
                    fragment.BitMap,
                    fragment.BitMapLeft,
                    fragment.BitMapTop,
                    fragment.Width,
                    fragment.Height,
                    sourceIsRtg,
                    destinationIsRtg));
            }

            return ExecuteRasterBlits(
                operations,
                (byte)state.D[6],
                ReadRastPortMask(destinationRastPort),
                maskPlane,
                destinationX - sourceX,
                destinationY - sourceY);
        }

        private int BlitRastPortToRastPortClipped(
            M68kCpuState state,
            uint sourceRastPort,
            uint destinationRastPort)
        {
            var sourceX = unchecked((short)(ushort)state.D[0]);
            var sourceY = unchecked((short)(ushort)state.D[1]);
            var destinationX = unchecked((short)(ushort)state.D[2]);
            var destinationY = unchecked((short)(ushort)state.D[3]);
            var width = unchecked((short)(ushort)state.D[4]);
            var height = unchecked((short)(ushort)state.D[5]);
            if (width <= 0 || height <= 0)
            {
                return 0;
            }

            var sourceFragments = GetRastPortClipFragments(
                sourceRastPort, sourceX, sourceY, sourceX + width - 1, sourceY + height - 1);
            var destinationFragments = GetRastPortClipFragments(
                destinationRastPort,
                destinationX,
                destinationY,
                destinationX + width - 1,
                destinationY + height - 1);
            var operations = new List<RasterBlitOperation>();
            foreach (var source in sourceFragments)
            {
                var sourceOffsetLeft = source.RequestLeft - sourceX;
                var sourceOffsetTop = source.RequestTop - sourceY;
                var sourceOffsetRight = source.RequestRight - sourceX;
                var sourceOffsetBottom = source.RequestBottom - sourceY;
                foreach (var destination in destinationFragments)
                {
                    var sourceIsRtg = CyberGraphics.IsRtgBitMap(source.BitMap);
                    var destinationIsRtg = CyberGraphics.IsRtgBitMap(destination.BitMap);
                    if (!sourceIsRtg && !destinationIsRtg)
                    {
                        continue;
                    }

                    var destinationOffsetLeft = destination.RequestLeft - destinationX;
                    var destinationOffsetTop = destination.RequestTop - destinationY;
                    var destinationOffsetRight = destination.RequestRight - destinationX;
                    var destinationOffsetBottom = destination.RequestBottom - destinationY;
                    if (!TryIntersect(
                        sourceOffsetLeft, sourceOffsetTop, sourceOffsetRight, sourceOffsetBottom,
                        destinationOffsetLeft, destinationOffsetTop,
                        destinationOffsetRight, destinationOffsetBottom,
                        out var offsets))
                    {
                        continue;
                    }

                    var clippedSourceX = source.BitMapLeft + (sourceX + offsets.Left - source.RequestLeft);
                    var clippedSourceY = source.BitMapTop + (sourceY + offsets.Top - source.RequestTop);
                    var clippedDestinationX = destination.BitMapLeft +
                        (destinationX + offsets.Left - destination.RequestLeft);
                    var clippedDestinationY = destination.BitMapTop +
                        (destinationY + offsets.Top - destination.RequestTop);
                    var clippedWidth = offsets.Right - offsets.Left + 1;
                    var clippedHeight = offsets.Bottom - offsets.Top + 1;
                    operations.Add(new RasterBlitOperation(
                        source.BitMap,
                        clippedSourceX,
                        clippedSourceY,
                        destination.BitMap,
                        clippedDestinationX,
                        clippedDestinationY,
                        clippedWidth,
                        clippedHeight,
                        sourceIsRtg,
                        destinationIsRtg));
                }
            }

            return ExecuteRasterBlits(
                operations,
                (byte)state.D[6],
                ReadRastPortMask(destinationRastPort),
                0,
                destinationX - sourceX,
                destinationY - sourceY);
        }

        private int ExecuteRasterBlits(
            List<RasterBlitOperation> operations,
            byte minterm,
            byte writeMask,
            uint maskPlane,
            int moveX,
            int moveY)
        {
            operations.Sort((left, right) =>
            {
                var comparison = moveY switch
                {
                    > 0 => right.SourceY.CompareTo(left.SourceY),
                    < 0 => left.SourceY.CompareTo(right.SourceY),
                    _ => 0
                };
                if (comparison != 0)
                {
                    return comparison;
                }

                return moveX > 0
                    ? right.SourceX.CompareTo(left.SourceX)
                    : left.SourceX.CompareTo(right.SourceX);
            });

            var written = 0;
            foreach (var operation in operations)
            {
                written += (operation.SourceIsRtg, operation.DestinationIsRtg) switch
                {
                    (true, true) => CyberGraphics.BlitRtgToRtg(
                        operation.SourceBitMap,
                        operation.SourceX,
                        operation.SourceY,
                        operation.DestinationBitMap,
                        operation.DestinationX,
                        operation.DestinationY,
                        operation.Width,
                        operation.Height,
                        minterm,
                        writeMask,
                        maskPlane),
                    (true, false) => CyberGraphics.BlitRtgToPlanar(
                        operation.SourceBitMap,
                        operation.SourceX,
                        operation.SourceY,
                        operation.DestinationBitMap,
                        operation.DestinationX,
                        operation.DestinationY,
                        operation.Width,
                        operation.Height,
                        minterm,
                        writeMask,
                        maskPlane),
                    (false, true) => CyberGraphics.BlitPlanarToRtg(
                        operation.SourceBitMap,
                        operation.SourceX,
                        operation.SourceY,
                        operation.DestinationBitMap,
                        operation.DestinationX,
                        operation.DestinationY,
                        operation.Width,
                        operation.Height,
                        minterm,
                        writeMask,
                        maskPlane),
                    _ => 0
                };
            }

            return written;
        }

        private static void NormalizeRectangle(ref int left, ref int top, ref int right, ref int bottom)
        {
            if (left > right)
            {
                (left, right) = (right, left);
            }

            if (top > bottom)
            {
                (top, bottom) = (bottom, top);
            }
        }

        private bool TryReadRectangle(uint address, out (int Left, int Top, int Right, int Bottom) rectangle)
        {
            rectangle = default;
            if (!_machine.Bus.IsMappedMemoryRange(address, RectangleSize))
            {
                return false;
            }

            rectangle = (
                unchecked((short)_machine.Bus.ReadWord(address)),
                unchecked((short)_machine.Bus.ReadWord(address + 2)),
                unchecked((short)_machine.Bus.ReadWord(address + 4)),
                unchecked((short)_machine.Bus.ReadWord(address + 6)));
            return rectangle.Right >= rectangle.Left && rectangle.Bottom >= rectangle.Top;
        }

        private static bool TryIntersect(
            int leftA, int topA, int rightA, int bottomA,
            int leftB, int topB, int rightB, int bottomB,
            out (int Left, int Top, int Right, int Bottom) intersection)
        {
            intersection = (
                Math.Max(leftA, leftB),
                Math.Max(topA, topB),
                Math.Min(rightA, rightB),
                Math.Min(bottomA, bottomB));
            return intersection.Right >= intersection.Left && intersection.Bottom >= intersection.Top;
        }
    }
}
