/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.Video.Rtg.CyberGraphics
{
    internal sealed partial class CyberGraphicsLibrary
    {
        private const uint ProcessFadeFullScale = 0x8523_1020;
        private const uint ProcessFadeOffset = 0x8523_1021;
        private const uint ProcessGradientType = 0x8523_1022;
        private const uint ProcessGradientColor1 = 0x8523_1023;
        private const uint ProcessGradientColor2 = 0x8523_1024;
        private const uint ProcessRgbMask = 0x8523_1025;
        private const uint ProcessGradientSymmetricCenter = 0x8523_1026;

        private readonly record struct ProcessPixelArrayOptions(
            uint Operation,
            uint Value,
            uint FadeFullScale,
            uint FadeOffset,
            uint GradientType,
            uint GradientColor1,
            uint GradientColor2,
            uint GradientFullScale,
            uint GradientOffset,
            uint RgbMask,
            bool GradientSymmetricCenter);

        private uint ReadRgbPixel(uint rastPort, int x, int y)
        {
            if (!TryGetRastPortSurface(rastPort, out var surface) || !surface.Contains(x, y))
            {
                return InvalidDisplayId;
            }

            var color = ReadSurfaceArgb(surface, x, y);
            return surface.Depth == 32 ? color : color & 0x00FF_FFFFu;
        }

        private uint WriteRgbPixel(uint rastPort, int x, int y, uint color)
        {
            if (!TryGetRastPortSurface(rastPort, out var surface) || !surface.Contains(x, y))
            {
                return InvalidDisplayId;
            }

            WriteSurfaceArgb(surface, x, y, color);
            return 0;
        }

        private uint ReadPixelArray(M68kCpuState state)
        {
            if (!TryGetRastPortSurface(state.A[1], out var surface))
            {
                return 0;
            }

            var destination = state.A[0];
            var destinationX = UWord(state.D[0]);
            var destinationY = UWord(state.D[1]);
            var destinationStride = UWord(state.D[2]);
            var sourceX = UWord(state.D[3]);
            var sourceY = UWord(state.D[4]);
            var width = UWord(state.D[5]);
            var height = UWord(state.D[6]);
            var format = (CyberGraphicsRectangleFormat)(byte)state.D[7];
            var bytesPerPixel = GetRectangleBytesPerPixel(format, surface);
            if (bytesPerPixel == 0 || destinationStride < destinationX * bytesPerPixel)
            {
                return 0;
            }

            var copyWidth = Math.Min(width, Math.Max(0, surface.Width - sourceX));
            var copyHeight = Math.Min(height, Math.Max(0, surface.Height - sourceY));
            uint count = 0;
            for (var y = 0; y < copyHeight; y++)
            {
                for (var x = 0; x < copyWidth; x++)
                {
                    var sx = sourceX + x;
                    var sy = sourceY + y;
                    if (!surface.Contains(sx, sy))
                    {
                        continue;
                    }

                    var offset = ((long)(destinationY + y) * destinationStride) + ((destinationX + x) * bytesPerPixel);
                    if (!TryAdd(destination, offset, bytesPerPixel, out var pixelAddress))
                    {
                        continue;
                    }

                    WriteRectanglePixel(pixelAddress, format, surface, ReadSurfaceArgb(surface, sx, sy));
                    count++;
                }
            }

            return count;
        }

        private uint WritePixelArray(M68kCpuState state, bool alpha)
        {
            if (!TryGetRastPortSurface(state.A[1], out var surface))
            {
                return 0;
            }

            var source = state.A[0];
            var sourceX = UWord(state.D[0]);
            var sourceY = UWord(state.D[1]);
            var sourceStride = UWord(state.D[2]);
            var destinationX = UWord(state.D[3]);
            var destinationY = UWord(state.D[4]);
            var width = UWord(state.D[5]);
            var height = UWord(state.D[6]);
            var format = alpha
                ? CyberGraphicsRectangleFormat.Argb
                : (CyberGraphicsRectangleFormat)(byte)state.D[7];
            var globalAlpha = alpha ? state.D[7] : uint.MaxValue;
            var bytesPerPixel = GetRectangleBytesPerPixel(format, surface);
            if (bytesPerPixel == 0 || sourceStride < sourceX * bytesPerPixel)
            {
                return 0;
            }

            var copyWidth = Math.Min(width, Math.Max(0, surface.Width - destinationX));
            var copyHeight = Math.Min(height, Math.Max(0, surface.Height - destinationY));
            uint count = 0;
            for (var y = 0; y < copyHeight; y++)
            {
                for (var x = 0; x < copyWidth; x++)
                {
                    var dx = destinationX + x;
                    var dy = destinationY + y;
                    if (!surface.Contains(dx, dy))
                    {
                        continue;
                    }

                    var offset = ((long)(sourceY + y) * sourceStride) + ((sourceX + x) * bytesPerPixel);
                    if (!TryAdd(source, offset, bytesPerPixel, out var pixelAddress))
                    {
                        continue;
                    }

                    var color = ReadRectanglePixel(pixelAddress, format, surface);
                    if (alpha)
                    {
                        color = Blend(ReadSurfaceArgb(surface, dx, dy), color, globalAlpha, useSourceAlpha: true, 0);
                    }

                    WriteSurfaceArgb(surface, dx, dy, color);
                    count++;
                }
            }

            return count;
        }

        private uint ScalePixelArray(M68kCpuState state, bool alpha)
        {
            if (!TryGetRastPortSurface(state.A[1], out var surface))
            {
                return 0;
            }

            var source = state.A[0];
            var sourceWidth = UWord(state.D[0]);
            var sourceHeight = UWord(state.D[1]);
            var sourceStride = UWord(state.D[2]);
            var destinationX = UWord(state.D[3]);
            var destinationY = UWord(state.D[4]);
            var destinationWidth = UWord(state.D[5]);
            var destinationHeight = UWord(state.D[6]);
            var format = alpha
                ? CyberGraphicsRectangleFormat.Argb
                : (CyberGraphicsRectangleFormat)(byte)state.D[7];
            var globalAlpha = alpha ? state.D[7] : uint.MaxValue;
            var bytesPerPixel = GetRectangleBytesPerPixel(format, surface);
            if (sourceWidth == 0 || sourceHeight == 0 || destinationWidth == 0 || destinationHeight == 0 ||
                bytesPerPixel == 0 || sourceStride < sourceWidth * bytesPerPixel)
            {
                return 0;
            }

            var outputWidth = Math.Min(destinationWidth, Math.Max(0, surface.Width - destinationX));
            var outputHeight = Math.Min(destinationHeight, Math.Max(0, surface.Height - destinationY));
            uint count = 0;
            for (var y = 0; y < outputHeight; y++)
            {
                var sourceY = y * sourceHeight / destinationHeight;
                for (var x = 0; x < outputWidth; x++)
                {
                    var dx = destinationX + x;
                    var dy = destinationY + y;
                    if (!surface.Contains(dx, dy))
                    {
                        continue;
                    }

                    var sourceX = x * sourceWidth / destinationWidth;
                    var offset = ((long)sourceY * sourceStride) + (sourceX * bytesPerPixel);
                    if (!TryAdd(source, offset, bytesPerPixel, out var pixelAddress))
                    {
                        continue;
                    }

                    var color = ReadRectanglePixel(pixelAddress, format, surface);
                    if (alpha)
                    {
                        color = Blend(ReadSurfaceArgb(surface, dx, dy), color, globalAlpha, useSourceAlpha: true, 0);
                    }

                    WriteSurfaceArgb(surface, dx, dy, color);
                    count++;
                }
            }

            return count;
        }

        private uint MovePixelArray(M68kCpuState state)
        {
            if (!TryGetRastPortSurface(state.A[1], out var surface))
            {
                return 0;
            }

            var sourceX = UWord(state.D[0]);
            var sourceY = UWord(state.D[1]);
            var destinationX = UWord(state.D[2]);
            var destinationY = UWord(state.D[3]);
            var width = UWord(state.D[4]);
            var height = UWord(state.D[5]);
            var copyWidth = Math.Min(
                width,
                Math.Min(Math.Max(0, surface.Width - sourceX), Math.Max(0, surface.Width - destinationX)));
            var copyHeight = Math.Min(
                height,
                Math.Min(Math.Max(0, surface.Height - sourceY), Math.Max(0, surface.Height - destinationY)));
            if (copyWidth == 0 || copyHeight == 0)
            {
                return 0;
            }

            var pixels = new uint[copyWidth * copyHeight];
            for (var y = 0; y < copyHeight; y++)
            {
                for (var x = 0; x < copyWidth; x++)
                {
                    pixels[(y * copyWidth) + x] = ReadSurfaceArgb(surface, sourceX + x, sourceY + y);
                }
            }

            uint count = 0;
            for (var y = 0; y < copyHeight; y++)
            {
                for (var x = 0; x < copyWidth; x++)
                {
                    WriteSurfaceArgb(surface, destinationX + x, destinationY + y, pixels[(y * copyWidth) + x]);
                    count++;
                }
            }

            return count;
        }

        private uint InvertPixelArray(M68kCpuState state)
        {
            if (!TryGetRastPortSurface(state.A[1], out var surface))
            {
                return 0;
            }

            return TransformRectangle(
                surface,
                UWord(state.D[0]),
                UWord(state.D[1]),
                UWord(state.D[2]),
                UWord(state.D[3]),
                color => (color & 0xFF00_0000u) | (~color & 0x00FF_FFFFu));
        }

        private uint FillPixelArray(M68kCpuState state)
        {
            if (!TryGetRastPortSurface(state.A[1], out var surface))
            {
                return 0;
            }

            var color = state.D[4];
            return TransformRectangle(
                surface,
                UWord(state.D[0]),
                UWord(state.D[1]),
                UWord(state.D[2]),
                UWord(state.D[3]),
                _ => color);
        }

        private uint ExtractColor(M68kCpuState state)
        {
            if (!TryGetRastPortSurface(state.A[0], out var source) ||
                state.A[1] == 0 ||
                !_bus.IsMappedMemoryRange(state.A[1], 12))
            {
                return 0;
            }

            var destinationBitMap = state.A[1];
            var bytesPerRow = _bus.ReadWord(destinationBitMap);
            var rows = _bus.ReadWord(destinationBitMap + 2);
            var depth = _bus.ReadByte(destinationBitMap + 5);
            var plane = _bus.ReadLong(destinationBitMap + 8);
            if (depth == 0 || plane == 0 || bytesPerRow == 0 || rows == 0 ||
                !_bus.IsMappedMemoryRange(plane, checked(bytesPerRow * rows)))
            {
                return 0;
            }

            var wanted = state.D[0];
            if (state.D[1] > int.MaxValue || state.D[2] > int.MaxValue)
            {
                return 0;
            }

            var sourceX = (int)state.D[1];
            var sourceY = (int)state.D[2];
            var width = checked((int)Math.Min(state.D[3], int.MaxValue));
            var height = checked((int)Math.Min(state.D[4], int.MaxValue));
            for (var y = 0; y < height && y < rows; y++)
            {
                for (var x = 0; x < width && x < bytesPerRow * 8; x++)
                {
                    var match = source.Contains(sourceX + x, sourceY + y) &&
                        ColorsEqualForSurface(source, ReadSurfaceArgb(source, sourceX + x, sourceY + y), wanted);
                    var byteAddress = plane + (uint)(y * bytesPerRow) + (uint)(x >> 3);
                    var mask = (byte)(0x80 >> (x & 7));
                    var value = _bus.ReadByte(byteAddress);
                    _bus.WriteByte(byteAddress, match ? (byte)(value | mask) : (byte)(value & ~mask), 0);
                }
            }

            return 1;
        }

        private uint WriteLutPixelArray(M68kCpuState state)
        {
            if (!TryGetRastPortSurface(state.A[1], out var destination) || state.A[2] == 0)
            {
                return 0;
            }

            var source = state.A[0];
            var sourceX = UWord(state.D[0]);
            var sourceY = UWord(state.D[1]);
            var sourceStride = UWord(state.D[2]);
            var destinationX = UWord(state.D[3]);
            var destinationY = UWord(state.D[4]);
            var width = UWord(state.D[5]);
            var height = UWord(state.D[6]);
            if ((byte)state.D[7] != 0 || sourceStride < sourceX)
            {
                return 0;
            }

            uint count = 0;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (!destination.Contains(destinationX + x, destinationY + y) ||
                        !TryAdd(source, ((long)(sourceY + y) * sourceStride) + sourceX + x, 1, out var sourceAddress))
                    {
                        continue;
                    }

                    var index = _bus.ReadByte(sourceAddress);
                    var tableAddress = state.A[2] + ((uint)index * 4);
                    if (!_bus.IsMappedMemoryRange(tableAddress, 4))
                    {
                        continue;
                    }

                    WriteSurfaceArgb(destination, destinationX + x, destinationY + y, 0xFF00_0000u | _bus.ReadLong(tableAddress));
                    count++;
                }
            }

            return count;
        }

        private void BltTemplateAlpha(M68kCpuState state)
        {
            if (!TryGetRastPortSurface(state.A[1], out var destination))
            {
                return;
            }

            var sourceX = Word(state.D[0]);
            var sourceStride = Word(state.D[1]);
            var destinationX = Word(state.D[2]);
            var destinationY = Word(state.D[3]);
            var width = Word(state.D[4]);
            var height = Word(state.D[5]);
            if (sourceX < 0 || sourceStride <= 0 || width <= 0 || height <= 0)
            {
                return;
            }

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var dx = destinationX + x;
                    var dy = destinationY + y;
                    if (!destination.Contains(dx, dy) ||
                        !TryAdd(state.A[0], ((long)y * sourceStride) + sourceX + x, 1, out var alphaAddress))
                    {
                        continue;
                    }

                    var alpha = _bus.ReadByte(alphaAddress);
                    var sourceColor = (destination.DrawColor & 0x00FF_FFFFu) | ((uint)alpha << 24);
                    WriteSurfaceArgb(
                        destination,
                        dx,
                        dy,
                        Blend(ReadSurfaceArgb(destination, dx, dy), sourceColor, uint.MaxValue, useSourceAlpha: true, 0));
                }
            }
        }

        private uint ProcessPixelArray(M68kCpuState state)
        {
            var x = (int)UWord(state.D[0]);
            var y = (int)UWord(state.D[1]);
            var width = (int)UWord(state.D[2]);
            var height = (int)UWord(state.D[3]);
            var operation = state.D[4];
            var value = state.D[5];
            if (width <= 0 || height <= 0 || operation > 10 ||
                (operation == 10 && (value < 1 || value > 5)))
            {
                return 0;
            }

            var gradientType = GetTagData(state.A[2], ProcessGradientType, 0);
            if (operation == 9 && gradientType > 1)
            {
                return 0;
            }

            var options = new ProcessPixelArrayOptions(
                operation,
                value,
                GetTagData(state.A[2], ProcessFadeFullScale, 255),
                GetTagData(state.A[2], ProcessFadeOffset, 0),
                gradientType,
                GetTagData(state.A[2], ProcessGradientColor1, value),
                GetTagData(state.A[2], ProcessGradientColor2, value),
                GetTagData(state.A[2], ProcessFadeFullScale, 0),
                GetTagData(state.A[2], ProcessFadeOffset, 0),
                operation is 0 or 1
                    ? GetTagData(state.A[2], ProcessRgbMask, 0x00FF_FFFFu)
                    : 0x00FF_FFFFu,
                GetTagData(state.A[2], ProcessGradientSymmetricCenter, 0) != 0);

            if (!TryGetProcessPixelArrayFragments(state.A[1], x, y, width, height, out var fragments))
            {
                return 0;
            }

            uint count = 0;
            foreach (var fragment in fragments)
            {
                if (!TryGetProcessPixelArraySurface(state.A[1], fragment, out var surface))
                {
                    continue;
                }

                count += ProcessPixelArrayFragment(
                    surface,
                    fragment,
                    x,
                    y,
                    width,
                    height,
                    options);
            }

            return count;
        }

        private bool TryGetProcessPixelArrayFragments(
            uint rastPort,
            int x,
            int y,
            int width,
            int height,
            out IReadOnlyList<CyberGraphicsClipFragment> fragments)
        {
            if (_guestServices?.TryGetRastPortClipFragments(
                    rastPort,
                    x,
                    y,
                    width,
                    height,
                    out fragments) == true)
            {
                return true;
            }

            if (!TryGetRastPortSurface(rastPort, out var surface))
            {
                fragments = Array.Empty<CyberGraphicsClipFragment>();
                return false;
            }

            var left = Math.Max(0, x);
            var top = Math.Max(0, y);
            var right = Math.Min(surface.Width, x + width) - 1;
            var bottom = Math.Min(surface.Height, y + height) - 1;
            if (left > right || top > bottom)
            {
                fragments = Array.Empty<CyberGraphicsClipFragment>();
                return true;
            }

            fragments =
            [
                new CyberGraphicsClipFragment(
                    0,
                    left,
                    top,
                    left,
                    top,
                    right - left + 1,
                    bottom - top + 1)
            ];
            return true;
        }

        private bool TryGetProcessPixelArraySurface(
            uint rastPort,
            CyberGraphicsClipFragment fragment,
            out CyberGraphicsSurface surface)
        {
            if (fragment.BitMapAddress != 0)
            {
                return TryResolveBitMapSurface(fragment.BitMapAddress, out surface!);
            }

            return TryGetRastPortSurface(rastPort, out surface!);
        }

        private uint ProcessPixelArrayFragment(
            CyberGraphicsSurface surface,
            CyberGraphicsClipFragment fragment,
            int requestX,
            int requestY,
            int requestWidth,
            int requestHeight,
            ProcessPixelArrayOptions options)
        {
            if (fragment.Width <= 0 || fragment.Height <= 0 ||
                (long)fragment.Width * fragment.Height > int.MaxValue)
            {
                return 0;
            }

            if (options.Operation == 4)
            {
                return BlurRectangle(
                    surface,
                    fragment.BitMapX,
                    fragment.BitMapY,
                    fragment.Width,
                    fragment.Height);
            }

            var gradientLength = options.GradientType == 1 ? requestHeight : requestWidth;
            var gradientFullScale = options.GradientFullScale == 0
                ? gradientLength
                : (int)Math.Min(options.GradientFullScale, int.MaxValue);
            uint count = 0;
            for (var py = 0; py < fragment.Height; py++)
            {
                for (var px = 0; px < fragment.Width; px++)
                {
                    var bitmapX = fragment.BitMapX + px;
                    var bitmapY = fragment.BitMapY + py;
                    if (!surface.Contains(bitmapX, bitmapY))
                    {
                        continue;
                    }

                    var pixelX = fragment.RequestX + px;
                    var pixelY = fragment.RequestY + py;
                    var color = ReadSurfaceArgb(surface, bitmapX, bitmapY);
                    var transformed = options.Operation switch
                    {
                        0 => AdjustBrightness(color, (int)Math.Min(options.Value, 255), brighten: true),
                        1 => AdjustBrightness(color, (int)Math.Min(options.Value, 255), brighten: false),
                        2 => (color & 0x00FF_FFFFu) | ((options.Value & 0xFFu) << 24),
                        3 => MultiplyTint(color, options.Value),
                        5 => ToGrey(color),
                        6 => (color & 0xFF00_0000u) | (~color & 0x00FF_FFFFu),
                        7 => Fade(
                            (color & 0xFF00_0000u) | (~color & 0x00FF_FFFFu),
                            options.Value,
                            options.FadeFullScale,
                            options.FadeOffset),
                        8 => Tint(
                            color,
                            options.Value,
                            GetFadeAmount(options.Value, options.FadeFullScale, options.FadeOffset)),
                        9 => GetGradientColor(
                            options.GradientColor1,
                            options.GradientColor2,
                            options.GradientType == 1 ? pixelY - requestY : pixelX - requestX,
                            gradientFullScale,
                            (int)Math.Min(options.GradientOffset, int.MaxValue),
                            options.GradientSymmetricCenter),
                        10 => ShiftRgb(color, options.Value),
                        _ => 0u
                    };

                    if (options.Operation is 0 or 1)
                    {
                        var rgbMask = options.RgbMask & 0x00FF_FFFFu;
                        transformed = (color & ~rgbMask) | (transformed & rgbMask);
                    }

                    WriteSurfaceArgb(surface, bitmapX, bitmapY, transformed);
                    count++;
                }
            }

            return count;
        }

        private uint BltBitMapAlpha(M68kCpuState state, bool destinationIsRastPort)
        {
            if (!_bitmaps.TryGetValue(state.A[0], out var source))
            {
                return 0;
            }

            var destinationFound = destinationIsRastPort
                ? TryGetRastPortSurface(state.A[1], out var destination)
                : _bitmaps.TryGetValue(state.A[1], out destination);
            if (!destinationFound || destination == null)
            {
                return 0;
            }

            var sourceX = Word(state.D[0]);
            var sourceY = Word(state.D[1]);
            var destinationX = Word(state.D[2]);
            var destinationY = Word(state.D[3]);
            var width = Word(state.D[4]);
            var height = Word(state.D[5]);
            if (width <= 0 || height <= 0)
            {
                return 0;
            }

            var useSourceAlpha = GetTagData(state.A[2], BltUseSourceAlpha, 0) != 0;
            var globalAlpha = GetTagData(state.A[2], BltMixLevel, useSourceAlpha ? uint.MaxValue : 0x8080_8080u);
            var destinationAlpha = GetTagData(state.A[2], BltDestinationAlpha, 0);
            uint count = 0;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (!source.Contains(sourceX + x, sourceY + y) ||
                        !destination.Contains(destinationX + x, destinationY + y))
                    {
                        continue;
                    }

                    var sourceColor = ReadSurfaceArgb(source, sourceX + x, sourceY + y);
                    var destinationColor = ReadSurfaceArgb(destination, destinationX + x, destinationY + y);
                    WriteSurfaceArgb(
                        destination,
                        destinationX + x,
                        destinationY + y,
                        Blend(destinationColor, sourceColor, globalAlpha, useSourceAlpha, destinationAlpha));
                    count++;
                }
            }

            return count;
        }

        private uint ScaleMapRastPortAlpha(M68kCpuState state)
        {
            if (!_bitmaps.TryGetValue(state.A[0], out var source) ||
                !TryGetRastPortSurface(state.A[1], out var destination))
            {
                return 0;
            }

            if (state.D[0] > int.MaxValue || state.D[1] > int.MaxValue ||
                state.D[2] > int.MaxValue || state.D[3] > int.MaxValue ||
                state.D[4] > int.MaxValue || state.D[5] > int.MaxValue ||
                state.D[6] > int.MaxValue || state.D[7] > int.MaxValue)
            {
                return 0;
            }

            var sourceX = (int)state.D[0];
            var sourceY = (int)state.D[1];
            var sourceWidth = checked((int)Math.Min(state.D[2], int.MaxValue));
            var sourceHeight = checked((int)Math.Min(state.D[3], int.MaxValue));
            var destinationX = checked((int)state.D[4]);
            var destinationY = checked((int)state.D[5]);
            var destinationWidth = checked((int)Math.Min(state.D[6], int.MaxValue));
            var destinationHeight = checked((int)Math.Min(state.D[7], int.MaxValue));
            if (sourceWidth <= 0 || sourceHeight <= 0 || destinationWidth <= 0 || destinationHeight <= 0)
            {
                return 0;
            }

            var useSourceAlpha = GetTagData(state.A[2], BltUseSourceAlpha, 1) != 0;
            var globalAlpha = GetTagData(state.A[2], BltMixLevel, uint.MaxValue);
            var destinationAlpha = GetTagData(state.A[2], BltDestinationAlpha, 0);
            var outputWidth = Math.Min(destinationWidth, Math.Max(0, destination.Width - destinationX));
            var outputHeight = Math.Min(destinationHeight, Math.Max(0, destination.Height - destinationY));
            uint count = 0;
            for (var y = 0; y < outputHeight; y++)
            {
                var sy = sourceY + (y * sourceHeight / destinationHeight);
                for (var x = 0; x < outputWidth; x++)
                {
                    var sx = sourceX + (x * sourceWidth / destinationWidth);
                    var dx = destinationX + x;
                    var dy = destinationY + y;
                    if (!source.Contains(sx, sy) || !destination.Contains(dx, dy))
                    {
                        continue;
                    }

                    var sourceColor = ReadSurfaceArgb(source, sx, sy);
                    var destinationColor = ReadSurfaceArgb(destination, dx, dy);
                    WriteSurfaceArgb(destination, dx, dy, Blend(destinationColor, sourceColor, globalAlpha, useSourceAlpha, destinationAlpha));
                    count++;
                }
            }

            return count;
        }

        private uint TransformRectangle(
            CyberGraphicsSurface surface,
            int x,
            int y,
            int width,
            int height,
            Func<uint, uint> transform)
        {
            width = Math.Min(width, Math.Max(0, surface.Width - x));
            height = Math.Min(height, Math.Max(0, surface.Height - y));
            uint count = 0;
            for (var py = 0; py < height; py++)
            {
                for (var px = 0; px < width; px++)
                {
                    if (!surface.Contains(x + px, y + py))
                    {
                        continue;
                    }

                    WriteSurfaceArgb(surface, x + px, y + py, transform(ReadSurfaceArgb(surface, x + px, y + py)));
                    count++;
                }
            }

            return count;
        }

        private uint BlurRectangle(CyberGraphicsSurface surface, int x, int y, int width, int height)
        {
            var pixels = new uint[checked(width * height)];
            for (var py = 0; py < height; py++)
            {
                for (var px = 0; px < width; px++)
                {
                    uint alpha = 0;
                    uint red = 0;
                    uint green = 0;
                    uint blue = 0;
                    uint samples = 0;
                    for (var oy = -1; oy <= 1; oy++)
                    {
                        for (var ox = -1; ox <= 1; ox++)
                        {
                            if (!surface.Contains(x + px + ox, y + py + oy))
                            {
                                continue;
                            }

                            var color = ReadSurfaceArgb(surface, x + px + ox, y + py + oy);
                            alpha += color >> 24;
                            red += (color >> 16) & 0xFF;
                            green += (color >> 8) & 0xFF;
                            blue += color & 0xFF;
                            samples++;
                        }
                    }

                    pixels[(py * width) + px] = samples == 0
                        ? 0
                        : ((alpha / samples) << 24) | ((red / samples) << 16) | ((green / samples) << 8) | (blue / samples);
                }
            }

            uint count = 0;
            for (var py = 0; py < height; py++)
            {
                for (var px = 0; px < width; px++)
                {
                    if (surface.Contains(x + px, y + py))
                    {
                        WriteSurfaceArgb(surface, x + px, y + py, pixels[(py * width) + px]);
                        count++;
                    }
                }
            }

            return count;
        }

        private uint ReadSurfaceArgb(CyberGraphicsSurface surface, int x, int y)
        {
            var offset = (y * surface.BytesPerRow) + (x * surface.BytesPerPixel);
            byte B(int index) => surface.ReadByte(_bus, offset + index);
            ushort W(bool littleEndian = false)
                => littleEndian ? (ushort)(B(0) | (B(1) << 8)) : (ushort)((B(0) << 8) | B(1));

            return surface.PixelFormat switch
            {
                CyberGraphicsPixelFormat.Lut8 => surface.Palette[B(0)],
                CyberGraphicsPixelFormat.Rgb15 => DecodeRgb15(W(), bgr: false, shifted: false),
                CyberGraphicsPixelFormat.Rgb15X => DecodeRgb15(W(), bgr: false, shifted: true),
                CyberGraphicsPixelFormat.Rgb15Pc => DecodeRgb15(W(littleEndian: true), bgr: false, shifted: false),
                CyberGraphicsPixelFormat.Bgr15Pc => DecodeRgb15(W(littleEndian: true), bgr: true, shifted: false),
                CyberGraphicsPixelFormat.Rgb16 => DecodeRgb16(W(), bgr: false),
                CyberGraphicsPixelFormat.Bgr16 => DecodeRgb16(W(), bgr: true),
                CyberGraphicsPixelFormat.Rgb16Pc => DecodeRgb16(W(littleEndian: true), bgr: false),
                CyberGraphicsPixelFormat.Bgr16Pc => DecodeRgb16(W(littleEndian: true), bgr: true),
                CyberGraphicsPixelFormat.Rgb24 => PackArgb(0xFF, B(0), B(1), B(2)),
                CyberGraphicsPixelFormat.Bgr24 => PackArgb(0xFF, B(2), B(1), B(0)),
                CyberGraphicsPixelFormat.Argb32 => PackArgb(B(0), B(1), B(2), B(3)),
                CyberGraphicsPixelFormat.Bgra32 => PackArgb(B(3), B(2), B(1), B(0)),
                CyberGraphicsPixelFormat.Rgba32 => PackArgb(B(3), B(0), B(1), B(2)),
                _ => 0
            };
        }

        private void WriteSurfaceArgb(CyberGraphicsSurface surface, int x, int y, uint color)
        {
            var offset = (y * surface.BytesPerRow) + (x * surface.BytesPerPixel);
            var alpha = (byte)(color >> 24);
            var red = (byte)(color >> 16);
            var green = (byte)(color >> 8);
            var blue = (byte)color;
            void B(int index, byte value) => surface.WriteByte(_bus, offset + index, value);
            void W(ushort value, bool littleEndian = false)
            {
                B(0, littleEndian ? (byte)value : (byte)(value >> 8));
                B(1, littleEndian ? (byte)(value >> 8) : (byte)value);
            }

            switch (surface.PixelFormat)
            {
                case CyberGraphicsPixelFormat.Lut8:
                    B(0, FindNearestPaletteIndex(surface.Palette, color));
                    break;
                case CyberGraphicsPixelFormat.Rgb15:
                    W(EncodeRgb15(red, green, blue, bgr: false, shifted: false));
                    break;
                case CyberGraphicsPixelFormat.Rgb15X:
                    W(EncodeRgb15(red, green, blue, bgr: false, shifted: true));
                    break;
                case CyberGraphicsPixelFormat.Rgb15Pc:
                    W(EncodeRgb15(red, green, blue, bgr: false, shifted: false), littleEndian: true);
                    break;
                case CyberGraphicsPixelFormat.Bgr15Pc:
                    W(EncodeRgb15(red, green, blue, bgr: true, shifted: false), littleEndian: true);
                    break;
                case CyberGraphicsPixelFormat.Rgb16:
                    W(EncodeRgb16(red, green, blue, bgr: false));
                    break;
                case CyberGraphicsPixelFormat.Bgr16:
                    W(EncodeRgb16(red, green, blue, bgr: true));
                    break;
                case CyberGraphicsPixelFormat.Rgb16Pc:
                    W(EncodeRgb16(red, green, blue, bgr: false), littleEndian: true);
                    break;
                case CyberGraphicsPixelFormat.Bgr16Pc:
                    W(EncodeRgb16(red, green, blue, bgr: true), littleEndian: true);
                    break;
                case CyberGraphicsPixelFormat.Rgb24:
                    B(0, red); B(1, green); B(2, blue);
                    break;
                case CyberGraphicsPixelFormat.Bgr24:
                    B(0, blue); B(1, green); B(2, red);
                    break;
                case CyberGraphicsPixelFormat.Argb32:
                    B(0, alpha); B(1, red); B(2, green); B(3, blue);
                    break;
                case CyberGraphicsPixelFormat.Bgra32:
                    B(0, blue); B(1, green); B(2, red); B(3, alpha);
                    break;
                case CyberGraphicsPixelFormat.Rgba32:
                    B(0, red); B(1, green); B(2, blue); B(3, alpha);
                    break;
            }
        }

        internal void WriteSurfacePen(
            CyberGraphicsSurface surface,
            int x,
            int y,
            byte pen,
            byte writeMask = 0xFF)
        {
            if (writeMask == 0)
            {
                return;
            }

            if (surface.PixelFormat == CyberGraphicsPixelFormat.Lut8 && writeMask != 0xFF)
            {
                var offset = checked(y * surface.BytesPerRow + x);
                var current = surface.ReadByte(_bus, offset);
                surface.WriteByte(_bus, offset, (byte)((pen & writeMask) | (current & ~writeMask)));
                return;
            }

            WriteSurfaceArgb(surface, x, y, surface.Palette[pen]);
        }

        private uint ReadRectanglePixel(
            uint address,
            CyberGraphicsRectangleFormat format,
            CyberGraphicsSurface rawSurface)
        {
            byte B(int index) => _bus.ReadByte(address + (uint)index);
            return format switch
            {
                CyberGraphicsRectangleFormat.Rgb => PackArgb(0xFF, B(0), B(1), B(2)),
                CyberGraphicsRectangleFormat.Rgba => PackArgb(B(3), B(0), B(1), B(2)),
                CyberGraphicsRectangleFormat.Argb => PackArgb(B(0), B(1), B(2), B(3)),
                CyberGraphicsRectangleFormat.Lut8 => rawSurface.Palette[B(0)],
                CyberGraphicsRectangleFormat.Grey8 => PackArgb(0xFF, B(0), B(0), B(0)),
                CyberGraphicsRectangleFormat.Raw => ReadRawPixel(address, rawSurface),
                _ => 0
            };
        }

        private void WriteRectanglePixel(
            uint address,
            CyberGraphicsRectangleFormat format,
            CyberGraphicsSurface rawSurface,
            uint color)
        {
            var alpha = (byte)(color >> 24);
            var red = (byte)(color >> 16);
            var green = (byte)(color >> 8);
            var blue = (byte)color;
            void B(int index, byte value) => _bus.WriteByte(address + (uint)index, value, 0);
            switch (format)
            {
                case CyberGraphicsRectangleFormat.Rgb:
                    B(0, red); B(1, green); B(2, blue);
                    break;
                case CyberGraphicsRectangleFormat.Rgba:
                    B(0, red); B(1, green); B(2, blue); B(3, alpha);
                    break;
                case CyberGraphicsRectangleFormat.Argb:
                    B(0, alpha); B(1, red); B(2, green); B(3, blue);
                    break;
                case CyberGraphicsRectangleFormat.Lut8:
                    B(0, FindNearestPaletteIndex(rawSurface.Palette, color));
                    break;
                case CyberGraphicsRectangleFormat.Grey8:
                    B(0, (byte)((red * 77 + green * 150 + blue * 29) >> 8));
                    break;
                case CyberGraphicsRectangleFormat.Raw:
                    WriteRawPixel(address, rawSurface, color);
                    break;
            }
        }

        private uint ReadRawPixel(uint address, CyberGraphicsSurface surface)
        {
            Span<byte> pixel = stackalloc byte[4];
            for (var i = 0; i < surface.BytesPerPixel; i++)
            {
                pixel[i] = _bus.ReadByte(address + (uint)i);
            }

            var bigEndianWord = (ushort)((pixel[0] << 8) | pixel[1]);
            var littleEndianWord = (ushort)(pixel[0] | (pixel[1] << 8));
            return surface.PixelFormat switch
            {
                CyberGraphicsPixelFormat.Lut8 => surface.Palette[pixel[0]],
                CyberGraphicsPixelFormat.Rgb15 => DecodeRgb15(bigEndianWord, bgr: false, shifted: false),
                CyberGraphicsPixelFormat.Rgb15X => DecodeRgb15(bigEndianWord, bgr: false, shifted: true),
                CyberGraphicsPixelFormat.Rgb15Pc => DecodeRgb15(littleEndianWord, bgr: false, shifted: false),
                CyberGraphicsPixelFormat.Bgr15Pc => DecodeRgb15(littleEndianWord, bgr: true, shifted: false),
                CyberGraphicsPixelFormat.Rgb16 => DecodeRgb16(bigEndianWord, bgr: false),
                CyberGraphicsPixelFormat.Bgr16 => DecodeRgb16(bigEndianWord, bgr: true),
                CyberGraphicsPixelFormat.Rgb16Pc => DecodeRgb16(littleEndianWord, bgr: false),
                CyberGraphicsPixelFormat.Bgr16Pc => DecodeRgb16(littleEndianWord, bgr: true),
                CyberGraphicsPixelFormat.Rgb24 => PackArgb(0xFF, pixel[0], pixel[1], pixel[2]),
                CyberGraphicsPixelFormat.Bgr24 => PackArgb(0xFF, pixel[2], pixel[1], pixel[0]),
                CyberGraphicsPixelFormat.Argb32 => PackArgb(pixel[0], pixel[1], pixel[2], pixel[3]),
                CyberGraphicsPixelFormat.Bgra32 => PackArgb(pixel[3], pixel[2], pixel[1], pixel[0]),
                CyberGraphicsPixelFormat.Rgba32 => PackArgb(pixel[3], pixel[0], pixel[1], pixel[2]),
                _ => 0
            };
        }

        private void WriteRawPixel(uint address, CyberGraphicsSurface surface, uint color)
        {
            Span<byte> pixel = stackalloc byte[4];
            var alpha = (byte)(color >> 24);
            var red = (byte)(color >> 16);
            var green = (byte)(color >> 8);
            var blue = (byte)color;
            switch (surface.PixelFormat)
            {
                case CyberGraphicsPixelFormat.Lut8: pixel[0] = FindNearestPaletteIndex(surface.Palette, color); break;
                case CyberGraphicsPixelFormat.Rgb15: StoreWord(pixel, EncodeRgb15(red, green, blue, bgr: false, shifted: false), littleEndian: false); break;
                case CyberGraphicsPixelFormat.Rgb15X: StoreWord(pixel, EncodeRgb15(red, green, blue, bgr: false, shifted: true), littleEndian: false); break;
                case CyberGraphicsPixelFormat.Rgb15Pc: StoreWord(pixel, EncodeRgb15(red, green, blue, bgr: false, shifted: false), littleEndian: true); break;
                case CyberGraphicsPixelFormat.Bgr15Pc: StoreWord(pixel, EncodeRgb15(red, green, blue, bgr: true, shifted: false), littleEndian: true); break;
                case CyberGraphicsPixelFormat.Rgb16: StoreWord(pixel, EncodeRgb16(red, green, blue, bgr: false), littleEndian: false); break;
                case CyberGraphicsPixelFormat.Bgr16: StoreWord(pixel, EncodeRgb16(red, green, blue, bgr: true), littleEndian: false); break;
                case CyberGraphicsPixelFormat.Rgb16Pc: StoreWord(pixel, EncodeRgb16(red, green, blue, bgr: false), littleEndian: true); break;
                case CyberGraphicsPixelFormat.Bgr16Pc: StoreWord(pixel, EncodeRgb16(red, green, blue, bgr: true), littleEndian: true); break;
                case CyberGraphicsPixelFormat.Rgb24: pixel[0] = red; pixel[1] = green; pixel[2] = blue; break;
                case CyberGraphicsPixelFormat.Bgr24: pixel[0] = blue; pixel[1] = green; pixel[2] = red; break;
                case CyberGraphicsPixelFormat.Argb32: pixel[0] = alpha; pixel[1] = red; pixel[2] = green; pixel[3] = blue; break;
                case CyberGraphicsPixelFormat.Bgra32: pixel[0] = blue; pixel[1] = green; pixel[2] = red; pixel[3] = alpha; break;
                case CyberGraphicsPixelFormat.Rgba32: pixel[0] = red; pixel[1] = green; pixel[2] = blue; pixel[3] = alpha; break;
            }

            for (var i = 0; i < surface.BytesPerPixel; i++)
            {
                _bus.WriteByte(address + (uint)i, pixel[i], 0);
            }
        }

        private static void StoreWord(Span<byte> bytes, ushort value, bool littleEndian)
        {
            bytes[0] = littleEndian ? (byte)value : (byte)(value >> 8);
            bytes[1] = littleEndian ? (byte)(value >> 8) : (byte)value;
        }

        private static int GetRectangleBytesPerPixel(CyberGraphicsRectangleFormat format, CyberGraphicsSurface rawSurface)
            => format switch
            {
                CyberGraphicsRectangleFormat.Rgb => 3,
                CyberGraphicsRectangleFormat.Rgba or CyberGraphicsRectangleFormat.Argb => 4,
                CyberGraphicsRectangleFormat.Lut8 or CyberGraphicsRectangleFormat.Grey8 => 1,
                CyberGraphicsRectangleFormat.Raw => rawSurface.BytesPerPixel,
                _ => 0
            };

        private static uint Blend(uint destination, uint source, uint globalAlpha, bool useSourceAlpha, uint destinationAlphaMode)
        {
            var sourceAlpha = useSourceAlpha ? (source >> 24) & 0xFF : 0xFF;
            var global = globalAlpha >> 24;
            var alpha = sourceAlpha * global / 255;
            var inverse = 255 - alpha;
            var red = ((((source >> 16) & 0xFF) * alpha) + (((destination >> 16) & 0xFF) * inverse) + 127) / 255;
            var green = ((((source >> 8) & 0xFF) * alpha) + (((destination >> 8) & 0xFF) * inverse) + 127) / 255;
            var blue = (((source & 0xFF) * alpha) + ((destination & 0xFF) * inverse) + 127) / 255;
            var destinationAlpha = destinationAlphaMode switch
            {
                1 => 0xFFu,
                2 => (source >> 24) & 0xFF,
                3 => (destination >> 24) & 0xFF,
                _ => Math.Max((destination >> 24) & 0xFF, alpha)
            };
            return (destinationAlpha << 24) | (red << 16) | (green << 8) | blue;
        }

        private static uint DecodeRgb15(ushort value, bool bgr, bool shifted)
        {
            if (shifted)
            {
                value >>= 1;
            }

            var first = Expand5((value >> 10) & 0x1F);
            var green = Expand5((value >> 5) & 0x1F);
            var last = Expand5(value & 0x1F);
            return bgr ? PackArgb(0xFF, last, green, first) : PackArgb(0xFF, first, green, last);
        }

        private static uint DecodeRgb16(ushort value, bool bgr)
        {
            var first = Expand5((value >> 11) & 0x1F);
            var green = Expand6((value >> 5) & 0x3F);
            var last = Expand5(value & 0x1F);
            return bgr ? PackArgb(0xFF, last, green, first) : PackArgb(0xFF, first, green, last);
        }

        private static ushort EncodeRgb15(byte red, byte green, byte blue, bool bgr, bool shifted)
        {
            var first = bgr ? blue : red;
            var last = bgr ? red : blue;
            var value = (ushort)(((first >> 3) << 10) | ((green >> 3) << 5) | (last >> 3));
            return shifted ? (ushort)(value << 1) : value;
        }

        private static ushort EncodeRgb16(byte red, byte green, byte blue, bool bgr)
        {
            var first = bgr ? blue : red;
            var last = bgr ? red : blue;
            return (ushort)(((first >> 3) << 11) | ((green >> 2) << 5) | (last >> 3));
        }

        private static byte FindNearestPaletteIndex(uint[] palette, uint color)
        {
            var bestIndex = 0;
            var bestDistance = int.MaxValue;
            var red = (int)((color >> 16) & 0xFF);
            var green = (int)((color >> 8) & 0xFF);
            var blue = (int)(color & 0xFF);
            for (var i = 0; i < palette.Length; i++)
            {
                var candidate = palette[i];
                var dr = red - (int)((candidate >> 16) & 0xFF);
                var dg = green - (int)((candidate >> 8) & 0xFF);
                var db = blue - (int)(candidate & 0xFF);
                var distance = (dr * dr) + (dg * dg) + (db * db);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            return (byte)bestIndex;
        }

        private static uint AdjustBrightness(uint color, int amount, bool brighten)
        {
            static uint Adjust(uint component, int adjustment, bool up)
                => up
                    ? (uint)Math.Min(255, component + adjustment)
                    : (uint)Math.Max(0, component - adjustment);
            return (color & 0xFF00_0000u) |
                (Adjust((color >> 16) & 0xFF, amount, brighten) << 16) |
                (Adjust((color >> 8) & 0xFF, amount, brighten) << 8) |
                Adjust(color & 0xFF, amount, brighten);
        }

        private static uint MultiplyTint(uint color, uint tint)
        {
            uint Multiply(int shift)
                => (((color >> shift) & 0xFF) * ((tint >> shift) & 0xFF) + 127) / 255;
            return (color & 0xFF00_0000u) | (Multiply(16) << 16) | (Multiply(8) << 8) | Multiply(0);
        }

        private static uint Tint(uint color, uint tint, uint amount)
        {
            var alpha = Math.Min(amount, 255);
            var inverse = 255 - alpha;
            uint Mix(int shift) => ((((color >> shift) & 0xFF) * inverse) + (((tint >> shift) & 0xFF) * alpha) + 127) / 255;
            return (color & 0xFF00_0000u) | (Mix(16) << 16) | (Mix(8) << 8) | Mix(0);
        }

        private static uint ToGrey(uint color)
        {
            var grey = ((((color >> 16) & 0xFF) * 77) + (((color >> 8) & 0xFF) * 150) + ((color & 0xFF) * 29)) >> 8;
            return (color & 0xFF00_0000u) | (grey << 16) | (grey << 8) | grey;
        }

        private static uint Fade(uint color, uint value, uint fullScale, uint offset)
            => AdjustBrightness(color, (int)GetFadeAmount(value, fullScale, offset), brighten: false);

        private static uint GetFadeAmount(uint value, uint fullScale, uint offset)
        {
            if (fullScale == 0 || value <= offset)
            {
                return 0;
            }

            return (uint)Math.Min(255UL, ((ulong)(value - offset) * 255UL) / fullScale);
        }

        private static uint GetGradientColor(
            uint first,
            uint second,
            int position,
            int fullScale,
            int offset,
            bool symmetricCenter)
        {
            if (fullScale <= 1)
            {
                return first;
            }

            var phase = (long)position + offset;
            if (symmetricCenter)
            {
                phase = Math.Abs((phase * 2) - (fullScale - 1L));
            }

            var amount = (uint)Math.Clamp(phase * 255L / (fullScale - 1L), 0L, 255L);
            return InterpolateArgb(first, second, amount);
        }

        private static uint InterpolateArgb(uint first, uint second, uint amount)
        {
            var inverse = 255 - Math.Min(amount, 255u);
            uint Mix(int shift)
                => ((((first >> shift) & 0xFF) * inverse) +
                    (((second >> shift) & 0xFF) * Math.Min(amount, 255u)) + 127) / 255;

            return (Mix(24) << 24) | (Mix(16) << 16) | (Mix(8) << 8) | Mix(0);
        }

        private static uint ShiftRgb(uint color, uint operation)
        {
            var alpha = color & 0xFF00_0000u;
            var red = (color >> 16) & 0xFF;
            var green = (color >> 8) & 0xFF;
            var blue = color & 0xFF;
            return operation switch
            {
                1 => alpha | (blue << 16) | (green << 8) | red,
                2 => alpha | (blue << 16) | (red << 8) | green,
                3 => alpha | (green << 16) | (blue << 8) | red,
                4 => alpha | (green << 16) | (red << 8) | blue,
                5 => alpha | (red << 16) | (blue << 8) | green,
                _ => color
            };
        }

        private static bool ColorsEqualForSurface(CyberGraphicsSurface surface, uint left, uint right)
            => surface.PixelFormat == CyberGraphicsPixelFormat.Lut8
                ? FindNearestPaletteIndex(surface.Palette, left) == (byte)right
                : (left & 0x00FF_FFFFu) == (right & 0x00FF_FFFFu);

        private static byte Expand5(int value)
            => (byte)((value << 3) | (value >> 2));

        private static byte Expand6(int value)
            => (byte)((value << 2) | (value >> 4));

        private static uint PackArgb(uint alpha, uint red, uint green, uint blue)
            => (alpha << 24) | (red << 16) | (green << 8) | blue;

        private bool TryAdd(uint address, long offset, int byteCount, out uint result)
        {
            if (offset < 0)
            {
                result = 0;
                return false;
            }

            var candidate = (ulong)address + (ulong)offset;
            result = (uint)candidate;
            return candidate <= uint.MaxValue &&
                _bus.IsMappedMemoryRange(result, byteCount);
        }
    }
}
