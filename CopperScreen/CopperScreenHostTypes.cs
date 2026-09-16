namespace CopperScreen;

// Profile metadata only: retained for round trips, never executed by Lightweight.
internal enum CopperScreenMemoryAllocator { Tlsf, Classic }

internal readonly record struct CopperScreenEmulatorFrameTiming(
    double CpuMilliseconds, double HardwareMilliseconds, double DisplayMilliseconds);

// Host-owned BGRA payload. A future CopperStart adapter must convert at its boundary.
internal sealed class ClipboardImage
{
    public ClipboardImage(int width, int height, uint[] bgra32)
    {
        if (width <= 0 || height <= 0 || bgra32 is null || bgra32.Length != checked(width * height))
            throw new ArgumentException("Clipboard image dimensions and pixel buffer do not agree.");
        Width = width; Height = height; Bgra32 = bgra32;
    }
    public int Width { get; }
    public int Height { get; }
    public uint[] Bgra32 { get; }
}
