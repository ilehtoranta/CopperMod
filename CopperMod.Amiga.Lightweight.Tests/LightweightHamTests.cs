using Copper68k;
using Xunit;

namespace CopperMod.Amiga.Lightweight.Tests;

public sealed class LightweightHamTests
{
    // PAL OCS: HRM ch.3 Hold-And-Modify Mode; RKM Libraries ch.27:
    // each line starts from COLOR00; codes 01/10/11 modify B/R/G.
    [Theory]
    [InlineData(454, 0x6800)]
    [InlineData(908, 0x6800)]
    [InlineData(908, 0x6804)]
    public void DmaPixelsSelectAndModifyInOrderWithoutChangingPalette(int width, int mode)
    {
        using var m = Create(width, (ushort)mode, [0x1A, 1, 0x2B, 0x3C, 0x1D, 1]);
        m.ExecuteFrame();
        Assert.Null(m.UnsupportedActiveFeature);
        int[] expected = [0x12A, 0x456, 0xB56, 0xBC6, 0xBCD, 0x456];
        for (var i = 0; i < expected.Length; i++) Pixel(m, width, 129 + i, 44, expected[i]);
        Pixel(m, width, 128, 44, 0x123);
        Assert.Equal((ushort)0x456, m.GetCustomRegister(0x182));
    }

    [Fact]
    public void FivePlaneHamUsesPaletteAndBlueModification()
    {
        using var m = Create(454, 0x5800, [1, 0x1A, 0x10, 1]);
        m.ExecuteFrame();
        Assert.Null(m.UnsupportedActiveFeature);
        Pixel(m, 454, 129, 44, 0x456);
        Pixel(m, 454, 130, 44, 0x45A);
        Pixel(m, 454, 131, 44, 0x450);
        Pixel(m, 454, 132, 44, 0x456);
    }

    [Fact]
    public void LeftWindowBoundaryStartsWithBackgroundNotHiddenShifterData()
    {
        using var m = Create(454, 0x6800, [1, 0x2B, 0x3C, 0x1D], startX: 132);
        m.ExecuteFrame();
        Pixel(m, 454, 131, 44, 0x123);
        Pixel(m, 454, 132, 44, 0x12D);
    }

    [Fact]
    public void SpriteOverlayDoesNotEnterHamHoldColor()
    {
        using var m = Create(908, 0x6800, [1, 0x2B, 0x3C]);
        long cycle = m.Cycle;
        Write(m, 0x1A2, 0xF00, ref cycle); // COLOR17
        Write(m, 0x104, 0x0020, ref cycle); // sprites in front
        Write(m, 0x140, 0x2C40, ref cycle); // output x130 (comparator x129)
        Write(m, 0x142, 0x2D01, ref cycle);
        Write(m, 0x146, 0, ref cycle);
        Write(m, 0x144, 0x8000, ref cycle);
        m.ExecuteFrame();
        Pixel(m, 908, 129, 44, 0x456);
        Pixel(m, 908, 130, 44, 0xF00);
        Pixel(m, 908, 131, 44, 0xBC6);
    }

    [Fact]
    public void PaletteWriteAffectsNextDirectCodeButNotPreviouslyHeldComponents()
    {
        using var m = Create(454, 0x6800, [1, 0x2B, 0x3C, 0x1D, 1]);
        var writeCycle = 44L * LightweightClock.CpuCyclesPerLine + 132;
        m.AdvanceHardwareTo(writeCycle);
        m.WriteCustomRegisterFromCopper(0x182, 0x789, writeCycle);
        m.ExecuteFrame();
        Pixel(m, 454, 131, 44, 0xBC6);
        Pixel(m, 454, 132, 44, 0xBCD);
        Pixel(m, 454, 133, 44, 0x789);
    }

    [Fact]
    public void NextInterlacedFieldStartsFromBackgroundAgain()
    {
        using var m = Create(908, 0x6804, [0x1A, 0x2B, 0x3C]);
        m.ExecuteFrame();
        Pixel(m, 908, 129, 44, 0x12A);
        long cycle = m.Cycle;
        for (int plane = 0; plane < 6; plane++)
            m.WriteLong(0xDFF0E0u + (uint)(plane * 4), 0x2000u + (uint)(plane * 0x100),
                ref cycle, M68kBusAccessKind.CpuDataWrite);
        m.ExecuteFrame();
        Pixel(m, 908, 129, 44, 0x12A);
        Assert.Equal(2, m.CompletedFrames);
    }

    [Theory]
    [InlineData(0x4800)]
    [InlineData(0xC800)]
    [InlineData(0x6C00)]
    public void UnimplementedHamCombinationsStillStop(int mode)
    {
        using var m = Create(908, (ushort)mode, [1]);
        m.AdvanceHardwareTo(m.Cycle + 4);
        Assert.NotNull(m.UnsupportedActiveFeature);
    }

    private static LightweightA500Machine Create(int width, ushort mode, int[] codes, int startX = 129)
    {
        var m = new LightweightA500Machine(new() { FramebufferWidth = width });
        long cycle = 0;
        m.WriteWord(0x1000, 0x4E72, ref cycle, M68kBusAccessKind.CpuDataWrite);
        m.WriteWord(0x1002, 0x2700, ref cycle, M68kBusAccessKind.CpuDataWrite);
        m.Reset();
        cycle = m.Cycle;
        for (int plane = 0; plane < 6; plane++)
        {
            uint address = 0x2000u + (uint)(plane * 0x100);
            ushort word = 0;
            for (int pixel = 0; pixel < codes.Length; pixel++)
                word |= (ushort)(((codes[pixel] >> plane) & 1) << (15 - pixel));
            m.WriteWord(address, word, ref cycle, M68kBusAccessKind.CpuDataWrite);
            m.WriteLong(0xDFF0E0u + (uint)(plane * 4), address, ref cycle, M68kBusAccessKind.CpuDataWrite);
        }
        Write(m, 0x180, 0x123, ref cycle);
        Write(m, 0x182, 0x456, ref cycle);
        Write(m, 0x08E, (ushort)(0x2C00 | startX), ref cycle);
        Write(m, 0x090, 0x2CC1, ref cycle);
        Write(m, 0x092, 0x38, ref cycle);
        Write(m, 0x094, 0x40, ref cycle);
        Write(m, 0x100, mode, ref cycle);
        Write(m, 0x096, 0x8300, ref cycle);
        m.Cpu.Cycles = cycle;
        return m;
    }

    private static void Write(LightweightA500Machine m, ushort offset, ushort value, ref long cycle)
        => m.WriteWord(0xDFF000u + offset, value, ref cycle, M68kBusAccessKind.CpuDataWrite);

    private static void Pixel(LightweightA500Machine m, int width, int x, int y, int rgb)
    {
        var expected = unchecked((int)0xFF000000) | ((rgb & 0xF00) << 8) * 17 |
            ((rgb & 0x0F0) << 4) * 17 | (rgb & 15) * 17;
        for (var duplicate = 0; duplicate < width / 454; duplicate++)
            Assert.Equal(expected, m.Framebuffer.Span[y * width + x * (width / 454) + duplicate]);
    }
}
