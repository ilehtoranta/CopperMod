using System;

#pragma warning disable CS1591

namespace CopperMod.Sid
{
    public readonly struct Argb32
    {
        public Argb32(byte alpha, byte red, byte green, byte blue)
        {
            Value = ((uint)alpha << 24) | ((uint)red << 16) | ((uint)green << 8) | blue;
        }

        public uint Value { get; }

        public byte Alpha => (byte)(Value >> 24);

        public byte Red => (byte)(Value >> 16);

        public byte Green => (byte)(Value >> 8);

        public byte Blue => (byte)Value;
    }

    public sealed class C64VideoFrame
    {
        public C64VideoFrame(int width, int height, Argb32[] pixels, long frameNumber, TimeSpan sourceTime)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            ArgumentNullException.ThrowIfNull(pixels);
            if (pixels.Length != width * height)
            {
                throw new ArgumentException("Pixel buffer size must match width * height.", nameof(pixels));
            }

            Width = width;
            Height = height;
            Pixels = (Argb32[])pixels.Clone();
            FrameNumber = frameNumber;
            SourceTime = sourceTime;
        }

        public int Width { get; }

        public int Height { get; }

        public Argb32[] Pixels { get; }

        public long FrameNumber { get; }

        public TimeSpan SourceTime { get; }
    }

    public interface IC64VideoFrameProvider
    {
        bool HasVideoFrameSource { get; }

        bool TryGetLatestVideoFrame(out C64VideoFrame frame);
    }
}

#pragma warning restore CS1591
