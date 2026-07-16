/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace CopperMod.Amiga
{
    internal enum CyberGraphicsFunction
    {
        Open,
        Close,
        Expunge,
        Reserved,
        IsCyberModeId,
        BestCModeIdTagList,
        CModeRequestTagList,
        AllocCModeListTagList,
        FreeCModeList,
        ScalePixelArray,
        GetCyberMapAttr,
        GetCyberIdAttr,
        ReadRgbPixel,
        WriteRgbPixel,
        ReadPixelArray,
        WritePixelArray,
        MovePixelArray,
        InvertPixelArray,
        FillPixelArray,
        DoCDrawMethodTagList,
        CVideoCtrlTagList,
        LockBitMapTagList,
        UnLockBitMap,
        UnLockBitMapTagList,
        ExtractColor,
        WriteLutPixelArray,
        WritePixelArrayAlpha,
        BltTemplateAlpha,
        ProcessPixelArray,
        BltBitMapAlpha,
        BltBitMapRastPortAlpha,
        ScalePixelArrayAlpha,
        ScaleMapRastPortAlpha
    }

    internal readonly record struct CyberGraphicsVector(
        int Offset,
        CyberGraphicsFunction Function,
        int IntroducedVersion);

    internal enum CyberGraphicsPixelFormat : uint
    {
        Lut8 = 0,
        Rgb15 = 1,
        Rgb15X = 2,
        Rgb15Pc = 3,
        Bgr15Pc = 4,
        Rgb16 = 5,
        Bgr16 = 6,
        Rgb16Pc = 7,
        Bgr16Pc = 8,
        Rgb24 = 9,
        Bgr24 = 10,
        Argb32 = 11,
        Bgra32 = 12,
        Rgba32 = 13
    }

    internal enum CyberGraphicsRectangleFormat : byte
    {
        Rgb = 0,
        Rgba = 1,
        Argb = 2,
        Lut8 = 3,
        Grey8 = 4,
        Raw = 5
    }

    internal readonly record struct CyberGraphicsMode(
        uint DisplayId,
        ushort Width,
        ushort Height,
        ushort Depth,
        CyberGraphicsPixelFormat PixelFormat,
        string Name,
        uint MonitorId = 0,
        string BoardName = "Copper RTG");

    internal interface ICyberGraphicsGuestServices
    {
        uint Allocate(int byteCount);

        void Free(uint address, int byteCount);

        bool InvokeHook(uint entryAddress, uint objectAddress, uint messageAddress);
    }

    internal sealed class CyberGraphicsSurface
    {
        private readonly byte[]? _hostStorage;

        public CyberGraphicsSurface(
            int width,
            int height,
            CyberGraphicsPixelFormat pixelFormat,
            int bytesPerRow = 0,
            uint guestBaseAddress = 0,
            byte[]? hostStorage = null)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            var bytesPerPixel = GetBytesPerPixel(pixelFormat);
            var minimumStride = checked(width * bytesPerPixel);
            if (bytesPerRow == 0)
            {
                bytesPerRow = minimumStride;
            }

            if (bytesPerRow < minimumStride)
            {
                throw new ArgumentOutOfRangeException(nameof(bytesPerRow));
            }

            Width = width;
            Height = height;
            PixelFormat = pixelFormat;
            BytesPerRow = bytesPerRow;
            GuestBaseAddress = guestBaseAddress;
            var requiredBytes = checked(bytesPerRow * height);
            if (guestBaseAddress == 0)
            {
                _hostStorage = hostStorage ?? new byte[requiredBytes];
                if (_hostStorage.Length < requiredBytes)
                {
                    throw new ArgumentException("The host storage is too small for the surface.", nameof(hostStorage));
                }
            }
            else if (hostStorage != null)
            {
                throw new ArgumentException("Host storage and a guest base address cannot be supplied together.", nameof(hostStorage));
            }

            Palette = new uint[256];
            for (var i = 0; i < Palette.Length; i++)
            {
                Palette[i] = 0xFF00_0000u | ((uint)i << 16) | ((uint)i << 8) | (uint)i;
            }
        }

        public int Width { get; }

        public int Height { get; }

        public int BytesPerRow { get; }

        public int BytesPerPixel => GetBytesPerPixel(PixelFormat);

        public int Depth => GetDepth(PixelFormat);

        public CyberGraphicsPixelFormat PixelFormat { get; }

        public uint GuestBaseAddress { get; }

        public uint ColorMapAddress { get; set; }

        public uint DrawColor { get; set; } = 0xFFFF_FFFFu;

        public uint[] Palette { get; }

        public bool Contains(int x, int y)
            => (uint)x < (uint)Width && (uint)y < (uint)Height;

        public byte ReadByte(AmigaBus bus, int offset)
        {
            if ((uint)offset >= (uint)checked(BytesPerRow * Height))
            {
                return 0;
            }

            return GuestBaseAddress != 0
                ? bus.ReadByte(GuestBaseAddress + (uint)offset)
                : _hostStorage![offset];
        }

        public void WriteByte(AmigaBus bus, int offset, byte value)
        {
            if ((uint)offset >= (uint)checked(BytesPerRow * Height))
            {
                return;
            }

            if (GuestBaseAddress != 0)
            {
                bus.WriteByte(GuestBaseAddress + (uint)offset, value, 0);
            }
            else
            {
                _hostStorage![offset] = value;
            }
        }

        public static int GetBytesPerPixel(CyberGraphicsPixelFormat pixelFormat)
            => pixelFormat switch
            {
                CyberGraphicsPixelFormat.Lut8 => 1,
                CyberGraphicsPixelFormat.Rgb15 or
                    CyberGraphicsPixelFormat.Rgb15X or
                    CyberGraphicsPixelFormat.Rgb15Pc or
                    CyberGraphicsPixelFormat.Bgr15Pc or
                    CyberGraphicsPixelFormat.Rgb16 or
                    CyberGraphicsPixelFormat.Bgr16 or
                    CyberGraphicsPixelFormat.Rgb16Pc or
                    CyberGraphicsPixelFormat.Bgr16Pc => 2,
                CyberGraphicsPixelFormat.Rgb24 or CyberGraphicsPixelFormat.Bgr24 => 3,
                CyberGraphicsPixelFormat.Argb32 or
                    CyberGraphicsPixelFormat.Bgra32 or
                    CyberGraphicsPixelFormat.Rgba32 => 4,
                _ => throw new ArgumentOutOfRangeException(nameof(pixelFormat))
            };

        public static int GetDepth(CyberGraphicsPixelFormat pixelFormat)
            => pixelFormat switch
            {
                CyberGraphicsPixelFormat.Lut8 => 8,
                CyberGraphicsPixelFormat.Rgb15 or
                    CyberGraphicsPixelFormat.Rgb15X or
                    CyberGraphicsPixelFormat.Rgb15Pc or
                    CyberGraphicsPixelFormat.Bgr15Pc => 15,
                CyberGraphicsPixelFormat.Rgb16 or
                    CyberGraphicsPixelFormat.Bgr16 or
                    CyberGraphicsPixelFormat.Rgb16Pc or
                    CyberGraphicsPixelFormat.Bgr16Pc => 16,
                CyberGraphicsPixelFormat.Rgb24 or CyberGraphicsPixelFormat.Bgr24 => 24,
                CyberGraphicsPixelFormat.Argb32 or
                    CyberGraphicsPixelFormat.Bgra32 or
                    CyberGraphicsPixelFormat.Rgba32 => 32,
                _ => 0
            };
    }
}
