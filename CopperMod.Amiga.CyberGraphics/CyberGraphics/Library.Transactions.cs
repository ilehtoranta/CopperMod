/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.Video.Rtg.CyberGraphics
{
    /// <summary>Provider-owned immutable RTG pixel snapshot.</summary>
    internal sealed class CyberGraphicsBitMapSnapshot
    {
        private byte[] _pixels = Array.Empty<byte>();
        private uint[] _palette = Array.Empty<uint>();

        internal void Reset(CyberGraphicsSurface surface, AmigaBus bus)
        {
            Width = surface.Width;
            Height = surface.Height;
            BytesPerRow = surface.BytesPerRow;
            PixelFormat = surface.PixelFormat;
            ColorMapAddress = surface.ColorMapAddress;
            var byteCount = checked(surface.BytesPerRow * surface.Height);
            if (_pixels.Length != byteCount)
                Array.Resize(ref _pixels, byteCount);
            for (var offset = 0; offset < byteCount; offset++)
                _pixels[offset] = surface.ReadByte(bus, offset);
            if (_palette.Length != surface.Palette.Length)
                Array.Resize(ref _palette, surface.Palette.Length);
            Array.Copy(surface.Palette, _palette, surface.Palette.Length);
            InUse = true;
        }

        internal void Release() => InUse = false;

        internal int Width { get; private set; }
        internal int Height { get; private set; }
        internal int BytesPerRow { get; private set; }
        internal CyberGraphicsPixelFormat PixelFormat { get; private set; }
        internal byte[] Pixels => _pixels;
        internal uint ColorMapAddress { get; private set; }
        internal uint[] Palette => _palette;
        internal bool InUse { get; private set; }
    }

    /// <summary>
    /// Provider-owned immutable snapshot of only the pixels reserved for a
    /// guest Layers Hook. Keeping the rectangle explicit prevents rollback
    /// from erasing unrelated guest writes made while the callback ran.
    /// </summary>
    internal sealed class CyberGraphicsBitMapRectangleSnapshot
    {
        private byte[] _pixels = Array.Empty<byte>();

        internal void Reset(
            CyberGraphicsSurface surface,
            AmigaBus bus,
            int surfaceWidth,
            int surfaceHeight,
            int surfaceBytesPerRow,
            CyberGraphicsPixelFormat pixelFormat,
            int x,
            int y,
            int width,
            int height,
            int byteCount)
        {
            SurfaceWidth = surfaceWidth;
            SurfaceHeight = surfaceHeight;
            SurfaceBytesPerRow = surfaceBytesPerRow;
            PixelFormat = pixelFormat;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            if (_pixels.Length != byteCount)
                Array.Resize(ref _pixels, byteCount);
            var rowBytes = checked(width * surface.BytesPerPixel);
            for (var row = 0; row < height; row++)
            {
                var sourceOffset = checked(
                    (y + row) * surface.BytesPerRow +
                    x * surface.BytesPerPixel);
                var destinationOffset = checked(row * rowBytes);
                for (var offset = 0; offset < rowBytes; offset++)
                    _pixels[destinationOffset + offset] =
                        surface.ReadByte(bus, sourceOffset + offset);
            }
            InUse = true;
        }

        internal void Release() => InUse = false;

        internal int SurfaceWidth { get; private set; }
        internal int SurfaceHeight { get; private set; }
        internal int SurfaceBytesPerRow { get; private set; }
        internal CyberGraphicsPixelFormat PixelFormat { get; private set; }
        internal int X { get; private set; }
        internal int Y { get; private set; }
        internal int Width { get; private set; }
        internal int Height { get; private set; }
        internal byte[] Pixels => _pixels;
        internal bool InUse { get; private set; }
    }

    /// <summary>Non-escaping workspace for serialized RTG-to-RTG blits.</summary>
    internal sealed class CyberGraphicsRtgBlitScratch
    {
        private uint[] _pixels = Array.Empty<uint>();
        private bool[] _valid = Array.Empty<bool>();
        private bool _acquired;

        internal bool TryAcquire(
            int cellCount,
            out uint[] pixels,
            out bool[] valid)
        {
            pixels = _pixels;
            valid = _valid;
            if (_acquired || cellCount <= 0)
                return false;
            _acquired = true;
            if (_pixels.Length < cellCount)
                Array.Resize(ref _pixels, cellCount);
            if (_valid.Length < cellCount)
                Array.Resize(ref _valid, cellCount);
            Array.Clear(_valid, 0, cellCount);
            pixels = _pixels;
            valid = _valid;
            return true;
        }

        internal void Release() => _acquired = false;

        internal void Reset()
        {
            if (_acquired)
                return;
            _pixels = Array.Empty<uint>();
            _valid = Array.Empty<bool>();
        }
    }

    internal sealed partial class CyberGraphicsLibrary
    {
        private const int MaximumTransactionSnapshotBytes = 256 * 1024 * 1024;
        private const int MaximumPooledTransactionSnapshots = 256;
        private const int MaximumPooledTransactionSnapshotBytes = 4 * 1024 * 1024;
        private readonly Stack<CyberGraphicsBitMapSnapshot>
            _bitMapTransactionSnapshotPool = new();
        private readonly Stack<CyberGraphicsBitMapRectangleSnapshot>
            _rectangleTransactionSnapshotPool = new();

        internal CyberGraphicsBitMapSnapshot? CaptureBitMapSnapshot(uint bitMap)
        {
            if (!_bitmaps.TryGetValue(bitMap, out var surface))
                return null;
            var byteCount = (long)surface.BytesPerRow * surface.Height;
            if (byteCount <= 0 || byteCount > MaximumTransactionSnapshotBytes)
                return null;
            var snapshot = _bitMapTransactionSnapshotPool.Count != 0
                ? _bitMapTransactionSnapshotPool.Pop()
                : new CyberGraphicsBitMapSnapshot();
            snapshot.Reset(surface, _bus);
            return snapshot;
        }

        internal bool RestoreBitMapSnapshot(
            uint bitMap,
            CyberGraphicsBitMapSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            if (!_bitmaps.TryGetValue(bitMap, out var surface) ||
                !Matches(surface, snapshot))
            {
                return false;
            }
            for (var offset = 0; offset < snapshot.Pixels.Length; offset++)
                surface.WriteByte(_bus, offset, snapshot.Pixels[offset]);
            return true;
        }

        internal CyberGraphicsBitMapRectangleSnapshot? CaptureBitMapRectangleSnapshot(
            uint bitMap,
            int x,
            int y,
            int width,
            int height,
            int maximumSnapshotBytes)
        {
            if (!_bitmaps.TryGetValue(bitMap, out var surface) ||
                width <= 0 || height <= 0 || maximumSnapshotBytes < 0)
            {
                return null;
            }

            var rightLong = (long)x + width;
            var bottomLong = (long)y + height;
            var left = (int)Math.Min(surface.Width, Math.Max(0L, x));
            var top = (int)Math.Min(surface.Height, Math.Max(0L, y));
            var right = (int)Math.Min(surface.Width, Math.Max(0L, rightLong));
            var bottom = (int)Math.Min(surface.Height, Math.Max(0L, bottomLong));
            var clippedWidth = Math.Max(0, right - left);
            var clippedHeight = Math.Max(0, bottom - top);
            var rowBytesLong = (long)clippedWidth * surface.BytesPerPixel;
            var byteCountLong = rowBytesLong * clippedHeight;
            if (rowBytesLong > int.MaxValue || byteCountLong > maximumSnapshotBytes)
                return null;

            var snapshot = _rectangleTransactionSnapshotPool.Count != 0
                ? _rectangleTransactionSnapshotPool.Pop()
                : new CyberGraphicsBitMapRectangleSnapshot();
            snapshot.Reset(
                surface,
                _bus,
                surface.Width,
                surface.Height,
                surface.BytesPerRow,
                surface.PixelFormat,
                left,
                top,
                clippedWidth,
                clippedHeight,
                checked((int)byteCountLong));
            return snapshot;
        }

        internal void ReleaseBitMapTransactionSnapshot(
            CyberGraphicsBitMapSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            if (!snapshot.InUse)
                return;
            snapshot.Release();
            if (snapshot.Pixels.Length <= MaximumPooledTransactionSnapshotBytes &&
                _bitMapTransactionSnapshotPool.Count < MaximumPooledTransactionSnapshots)
                _bitMapTransactionSnapshotPool.Push(snapshot);
        }

        internal void ReleaseRectangleTransactionSnapshot(
            CyberGraphicsBitMapRectangleSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            if (!snapshot.InUse)
                return;
            snapshot.Release();
            if (snapshot.Pixels.Length <= MaximumPooledTransactionSnapshotBytes &&
                _rectangleTransactionSnapshotPool.Count < MaximumPooledTransactionSnapshots)
                _rectangleTransactionSnapshotPool.Push(snapshot);
        }

        internal void ResetLayersTransactionSnapshotPools()
        {
            _bitMapTransactionSnapshotPool.Clear();
            _rectangleTransactionSnapshotPool.Clear();
        }

        internal bool RestoreBitMapRectangleSnapshot(
            uint bitMap,
            CyberGraphicsBitMapRectangleSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            if (!_bitmaps.TryGetValue(bitMap, out var surface) ||
                surface.Width != snapshot.SurfaceWidth ||
                surface.Height != snapshot.SurfaceHeight ||
                surface.BytesPerRow != snapshot.SurfaceBytesPerRow ||
                surface.PixelFormat != snapshot.PixelFormat)
            {
                return false;
            }

            var rowBytes = checked(snapshot.Width * surface.BytesPerPixel);
            if (snapshot.Pixels.Length != checked(rowBytes * snapshot.Height))
                return false;
            for (var row = 0; row < snapshot.Height; row++)
            {
                var sourceOffset = checked(row * rowBytes);
                var destinationOffset = checked(
                    (snapshot.Y + row) * surface.BytesPerRow +
                    snapshot.X * surface.BytesPerPixel);
                for (var offset = 0; offset < rowBytes; offset++)
                {
                    surface.WriteByte(
                        _bus,
                        destinationOffset + offset,
                        snapshot.Pixels[sourceOffset + offset]);
                }
            }
            return true;
        }

        internal bool CopyFromBitMapSnapshot(
            CyberGraphicsBitMapSnapshot snapshot,
            uint destinationBitMap,
            int sourceX,
            int sourceY,
            int destinationX,
            int destinationY,
            int width,
            int height,
            byte minterm,
            uint maskPlane)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            if (width <= 0 || height <= 0)
                return false;
            var source = new CyberGraphicsSurface(
                snapshot.Width,
                snapshot.Height,
                snapshot.PixelFormat,
                snapshot.BytesPerRow,
                hostStorage: (byte[])snapshot.Pixels.Clone());
            source.AssociateColorMap(
                snapshot.ColorMapAddress,
                (uint[])snapshot.Palette.Clone());
            var temporaryKey = FindTemporaryBitMapKey();
            if (temporaryKey == 0)
                return false;
            _bitmaps.Add(temporaryKey, source);
            try
            {
                var copied = _bitmaps.ContainsKey(destinationBitMap)
                    ? BlitRtgToRtg(
                        temporaryKey,
                        sourceX,
                        sourceY,
                        destinationBitMap,
                        destinationX,
                        destinationY,
                        width,
                        height,
                        minterm,
                        0xFF,
                        maskPlane)
                    : BlitRtgToPlanar(
                        temporaryKey,
                        sourceX,
                        sourceY,
                        destinationBitMap,
                        destinationX,
                        destinationY,
                        width,
                        height,
                        minterm,
                        0xFF,
                        maskPlane);
                return copied > 0;
            }
            finally
            {
                _bitmaps.Remove(temporaryKey);
            }
        }

        private uint FindTemporaryBitMapKey()
        {
            for (var candidate = uint.MaxValue; candidate != 0; candidate--)
            {
                if (!_bitmaps.ContainsKey(candidate))
                    return candidate;
            }
            return 0;
        }

        private static bool Matches(
            CyberGraphicsSurface surface,
            CyberGraphicsBitMapSnapshot snapshot)
            => surface.Width == snapshot.Width &&
                surface.Height == snapshot.Height &&
                surface.BytesPerRow == snapshot.BytesPerRow &&
                surface.PixelFormat == snapshot.PixelFormat &&
                snapshot.Pixels.Length == checked(surface.BytesPerRow * surface.Height);
    }
}
