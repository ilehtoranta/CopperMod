/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.Video.Rtg.CyberGraphics
{
    internal readonly record struct CyberGraphicsRtgFrame(
        int Width,
        int Height,
        uint[] Bgra,
        uint ViewPortAddress,
        uint BitMapAddress);

    internal readonly record struct CyberGraphicsDisplayLayer(
        uint ViewPortAddress,
        bool IsRtg,
        int X,
        int Y,
        int Width,
        int Height,
        int SourceX,
        int SourceY,
        int SourceWidth,
        int SourceHeight,
        uint BackgroundColor,
        bool DpmsOff,
        uint[]? Bgra);

    internal sealed record CyberGraphicsDisplayComposition(
        int Width,
        int Height,
        bool TopIsRtg,
        bool TopDpmsOff,
        IReadOnlyList<CyberGraphicsDisplayLayer> Layers);

    internal sealed class CyberGraphicsRtgDevice
    {
        public const uint MonitorId = 0x4350_0000u;

        private static readonly (ushort Width, ushort Height)[] Resolutions =
        [
            (640, 480), (800, 600), (1024, 768), (1152, 864),
            (1280, 720), (1280, 800), (1280, 1024), (1366, 768),
            (1440, 900), (1600, 900), (1600, 1200), (1680, 1050),
            (1920, 1080), (1920, 1200), (2560, 1440), (2560, 1600),
            (3840, 2160)
        ];

        private static readonly (CyberGraphicsPixelFormat Format, ushort Depth, uint Ordinal, string Name)[] Formats =
        [
            (CyberGraphicsPixelFormat.Lut8, 8, 1, "LUT8"),
            (CyberGraphicsPixelFormat.Rgb16, 16, 2, "RGB16"),
            (CyberGraphicsPixelFormat.Argb32, 32, 3, "ARGB32"),
            (CyberGraphicsPixelFormat.Rgb15, 15, 4, "RGB15")
        ];

        private readonly AmigaBus _bus;
        private readonly Dictionary<uint, CyberGraphicsSurface> _surfacesByBase = new Dictionary<uint, CyberGraphicsSurface>();
        private readonly Dictionary<uint, uint[]> _scanoutBuffers = new Dictionary<uint, uint[]>();
        private readonly Dictionary<uint, uint> _bitmapBases = new Dictionary<uint, uint>();
        private readonly Dictionary<uint, uint> _viewPortBitmaps = new Dictionary<uint, uint>();
        private uint _frontViewPort;

        public CyberGraphicsRtgDevice(AmigaBus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        }

        public bool IsAvailable => _bus.RtgVram.Active;

        public uint FrontViewPort => _frontViewPort;

        public IEnumerable<CyberGraphicsMode> CreateModes()
        {
            for (var resolutionIndex = 0; resolutionIndex < Resolutions.Length; resolutionIndex++)
            {
                var resolution = Resolutions[resolutionIndex];
                foreach (var format in Formats)
                {
                    var displayId = MonitorId | ((uint)(resolutionIndex + 1) << 4) | format.Ordinal;
                    yield return new CyberGraphicsMode(
                        displayId,
                        resolution.Width,
                        resolution.Height,
                        format.Depth,
                        format.Format,
                        $"Copper RTG {resolution.Width}x{resolution.Height} {format.Name}",
                        MonitorId,
                        "Copper RTG");
                }
            }
        }

        public CyberGraphicsSurface? AllocateSurface(
            int width,
            int height,
            CyberGraphicsPixelFormat pixelFormat)
        {
            if (!IsAvailable || width <= 0 || height <= 0)
            {
                return null;
            }

            var bytesPerPixel = CyberGraphicsSurface.GetBytesPerPixel(pixelFormat);
            var bytesPerRow = checked((width * bytesPerPixel + 63) & ~63);
            var byteCount = checked((long)bytesPerRow * height);
            var baseAddress = _bus.AllocateRtgVram(byteCount);
            if (baseAddress == 0)
            {
                return null;
            }

            var surface = new CyberGraphicsSurface(width, height, pixelFormat, bytesPerRow, baseAddress);
            _surfacesByBase.Add(baseAddress, surface);
            return surface;
        }

        public bool FreeSurface(CyberGraphicsSurface surface)
        {
            ArgumentNullException.ThrowIfNull(surface);
            if (surface.GuestBaseAddress == 0 || !_surfacesByBase.Remove(surface.GuestBaseAddress))
            {
                return false;
            }

            var removedBitMaps = new HashSet<uint>();
            foreach (var mapping in new List<KeyValuePair<uint, uint>>(_bitmapBases))
            {
                if (mapping.Value == surface.GuestBaseAddress)
                {
                    _bitmapBases.Remove(mapping.Key);
                    removedBitMaps.Add(mapping.Key);
                }
            }

            foreach (var mapping in new List<KeyValuePair<uint, uint>>(_viewPortBitmaps))
            {
                if (removedBitMaps.Contains(mapping.Value))
                {
                    _viewPortBitmaps.Remove(mapping.Key);
                    if (_frontViewPort == mapping.Key)
                    {
                        _frontViewPort = 0;
                    }
                }
            }

            _scanoutBuffers.Remove(surface.GuestBaseAddress);

            return _bus.FreeRtgVram(surface.GuestBaseAddress);
        }

        public bool TryGetSurfaceByBase(uint baseAddress, out CyberGraphicsSurface surface)
            => _surfacesByBase.TryGetValue(baseAddress, out surface!);

        public void RegisterBitMap(uint bitMapAddress, CyberGraphicsSurface surface)
        {
            if (surface.GuestBaseAddress != 0)
            {
                _bitmapBases[bitMapAddress] = surface.GuestBaseAddress;
            }
        }

        public void UnregisterBitMap(uint bitMapAddress)
        {
            _bitmapBases.Remove(bitMapAddress);
            foreach (var mapping in new List<KeyValuePair<uint, uint>>(_viewPortBitmaps))
            {
                if (mapping.Value == bitMapAddress)
                {
                    _viewPortBitmaps.Remove(mapping.Key);
                    if (_frontViewPort == mapping.Key)
                    {
                        _frontViewPort = 0;
                    }
                }
            }
        }

        public void RegisterViewPort(uint viewPortAddress, uint bitMapAddress)
            => _viewPortBitmaps[viewPortAddress] = bitMapAddress;

        public void UnregisterViewPort(uint viewPortAddress)
        {
            _viewPortBitmaps.Remove(viewPortAddress);
            if (_frontViewPort == viewPortAddress)
            {
                _frontViewPort = 0;
            }
        }

        public void SelectFrontViewPort(uint viewPortAddress)
        {
            _frontViewPort = _viewPortBitmaps.ContainsKey(viewPortAddress) ? viewPortAddress : 0;
        }

        public bool TryRenderFrontFrame(out CyberGraphicsRtgFrame frame)
        {
            frame = default;
            if (!IsAvailable ||
                _frontViewPort == 0 ||
                !_viewPortBitmaps.TryGetValue(_frontViewPort, out var bitMap) ||
                !_bitmapBases.TryGetValue(bitMap, out var baseAddress) ||
                !_surfacesByBase.TryGetValue(baseAddress, out var surface))
            {
                return false;
            }

            if (!_scanoutBuffers.TryGetValue(baseAddress, out var bgra))
            {
                bgra = new uint[checked(surface.Width * surface.Height)];
                _scanoutBuffers.Add(baseAddress, bgra);
            }
            for (var y = 0; y < surface.Height; y++)
            {
                for (var x = 0; x < surface.Width; x++)
                {
                    bgra[y * surface.Width + x] = ReadSurfaceArgb(surface, x, y);
                }
            }

            frame = new CyberGraphicsRtgFrame(surface.Width, surface.Height, bgra, _frontViewPort, bitMap);
            _bus.RtgVram.ClearDirtyPages();
            return true;
        }

        public uint[] GetSurfacePixels(CyberGraphicsSurface surface)
        {
            ArgumentNullException.ThrowIfNull(surface);
            if (!_scanoutBuffers.TryGetValue(surface.GuestBaseAddress, out var bgra) ||
                bgra.Length != checked(surface.Width * surface.Height))
            {
                bgra = new uint[checked(surface.Width * surface.Height)];
                _scanoutBuffers[surface.GuestBaseAddress] = bgra;
            }

            for (var y = 0; y < surface.Height; y++)
            {
                for (var x = 0; x < surface.Width; x++)
                {
                    bgra[y * surface.Width + x] = ReadSurfaceArgb(surface, x, y);
                }
            }

            return bgra;
        }

        public void Reset()
        {
            _surfacesByBase.Clear();
            _scanoutBuffers.Clear();
            _bitmapBases.Clear();
            _viewPortBitmaps.Clear();
            _frontViewPort = 0;
        }

        private uint ReadSurfaceArgb(CyberGraphicsSurface surface, int x, int y)
        {
            var offset = checked(y * surface.BytesPerRow + x * surface.BytesPerPixel);
            byte B(int relative) => _bus.ReadByte(surface.GuestBaseAddress + (uint)(offset + relative));
            ushort W(bool littleEndian = false)
                => littleEndian ? (ushort)(B(0) | (B(1) << 8)) : (ushort)((B(0) << 8) | B(1));
            return surface.PixelFormat switch
            {
                CyberGraphicsPixelFormat.Lut8 => surface.Palette[B(0)],
                CyberGraphicsPixelFormat.Rgb15 => DecodeRgb15(W(), bgr: false, shifted: false),
                CyberGraphicsPixelFormat.Rgb15X => DecodeRgb15(W(), bgr: false, shifted: true),
                CyberGraphicsPixelFormat.Rgb15Pc => DecodeRgb15(W(littleEndian: true), bgr: false, shifted: false),
                CyberGraphicsPixelFormat.Bgr15Pc => DecodeRgb15(W(littleEndian: true), bgr: true, shifted: false),
                CyberGraphicsPixelFormat.Rgb16 => DecodeRgb16(W()),
                CyberGraphicsPixelFormat.Argb32 => ((uint)B(0) << 24) | ((uint)B(1) << 16) | ((uint)B(2) << 8) | B(3),
                _ => 0xFF00_0000u
            };
        }

        private static uint DecodeRgb16(ushort value)
        {
            var red5 = (uint)((value >> 11) & 0x1F);
            var green6 = (uint)((value >> 5) & 0x3F);
            var blue5 = (uint)(value & 0x1F);
            var red = (red5 << 3) | (red5 >> 2);
            var green = (green6 << 2) | (green6 >> 4);
            var blue = (blue5 << 3) | (blue5 >> 2);
            return 0xFF00_0000u | (red << 16) | (green << 8) | blue;
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
            return bgr
                ? 0xFF00_0000u | (last << 16) | (green << 8) | first
                : 0xFF00_0000u | (first << 16) | (green << 8) | last;
        }

        private static uint Expand5(int value)
            => (uint)((value << 3) | (value >> 2));
    }
}
