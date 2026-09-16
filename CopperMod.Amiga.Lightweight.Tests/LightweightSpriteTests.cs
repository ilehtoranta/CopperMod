using Copper68k;
using Xunit;

namespace CopperMod.Amiga.Lightweight.Tests;

public sealed class LightweightSpriteTests
{
    private const int Width = LightweightVideo.PalRasterWidth;
    private const int SpriteX = 129;
    private const int SpriteY = 44;

    [Fact]
    public void ManualDataAArmsOnTheFollowingDeniseInputStage()
    {
        using var machine = CreateStoppedMachine();
        long cycle = machine.Cycle;
        var (pos, ctl) = EncodePosition(SpriteX, SpriteY, height: 1);
        Write(machine, Register(0, 0), pos, ref cycle);
        Write(machine, Register(0, 2), ctl, ref cycle);
        Write(machine, Register(0, 6), 0, ref cycle);
        Assert.False(machine.IsManualSpriteArmed(0));

        var writeCycle = machine.Cycle;
        machine.WriteCustomRegisterFromCopper(
            LightweightRegisters.SpriteDataFirst,
            0x8000,
            writeCycle);

        Assert.False(machine.IsManualSpriteArmed(0));
        Assert.Equal(writeCycle + LightweightClock.CpuCyclesPerColorClock,
            machine.SpriteNextCycle);
        machine.AdvanceHardwareTo(writeCycle + 1);
        Assert.False(machine.IsManualSpriteArmed(0));
        machine.AdvanceHardwareTo(writeCycle + 2);

        Assert.True(machine.IsManualSpriteArmed(0));
        Assert.Equal(writeCycle + 2, machine.SpriteLastRegisterInputCycle);
    }

    [Fact]
    public void ManualSpriteUsesMsbFirstPaletteAndTransparentPixels()
    {
        using var machine = CreateStoppedMachine();
        long cycle = machine.Cycle;
        ConfigureWindow(machine, ref cycle);
        Write(machine, Color(0), 0x000F, ref cycle);
        Write(machine, Color(17), 0x0F00, ref cycle);
        WriteManualSprite(machine, sprite: 0, SpriteX, SpriteY,
            dataA: 0x8001, dataB: 0, attached: false, ref cycle);

        machine.ExecuteFrame();
        var frame = machine.Framebuffer.Span;

        Assert.Equal(Red, Pixel(frame, SpriteX, SpriteY));
        Assert.Equal(Blue, Pixel(frame, SpriteX + 1, SpriteY));
        Assert.Equal(Red, Pixel(frame, SpriteX + 15, SpriteY));
    }

    [Fact]
    public void AllEightManualRegisterSetsSelectTheirOcsPaletteGroups()
    {
        using var machine = CreateStoppedMachine();
        long cycle = machine.Cycle;
        ConfigureWindow(machine, ref cycle);
        for (var sprite = 0; sprite < 8; sprite++)
        {
            var pixel = (sprite % 3) + 1;
            var colorIndex = 16 + ((sprite / 2) * 4) + pixel;
            var color = UniqueColor(colorIndex);
            Write(machine, Color(colorIndex), color, ref cycle);
            var dataA = (pixel & 1) != 0 ? (ushort)0x8000 : (ushort)0;
            var dataB = (pixel & 2) != 0 ? (ushort)0x8000 : (ushort)0;
            WriteManualSprite(machine, sprite, SpriteX + (sprite * 20), SpriteY,
                dataA, dataB, attached: false, ref cycle);
        }

        machine.ExecuteFrame();
        var frame = machine.Framebuffer.Span;

        for (var sprite = 0; sprite < 8; sprite++)
        {
            var pixel = (sprite % 3) + 1;
            var colorIndex = 16 + ((sprite / 2) * 4) + pixel;
            Assert.Equal(ToArgb(UniqueColor(colorIndex)),
                Pixel(frame, SpriteX + (sprite * 20), SpriteY));
        }
    }

    [Fact]
    public void AttachedPairUsesFourBitPaletteAndBeatsLaterSpritePair()
    {
        using var machine = CreateStoppedMachine();
        long cycle = machine.Cycle;
        ConfigureWindow(machine, ref cycle);
        Write(machine, Color(20), 0x00F0, ref cycle);
        Write(machine, Color(21), 0x000F, ref cycle);
        WriteManualSprite(machine, sprite: 2, SpriteX, SpriteY,
            dataA: 0x8000, dataB: 0, attached: false, ref cycle);
        WriteManualSprite(machine, sprite: 0, SpriteX, SpriteY,
            dataA: 0, dataB: 0, attached: false, ref cycle);
        WriteManualSprite(machine, sprite: 1, SpriteX, SpriteY,
            dataA: 0x8000, dataB: 0, attached: true, ref cycle);

        machine.ExecuteFrame();

        Assert.Equal(Green, Pixel(machine.Framebuffer.Span, SpriteX, SpriteY));
    }

    [Fact]
    public void AttachedOddManualSpriteWithoutArmedEvenPartnerIsTransparent()
    {
        using var machine = CreateStoppedMachine();
        long cycle = machine.Cycle;
        ConfigureWindow(machine, ref cycle);
        Write(machine, Color(20), 0x0F00, ref cycle);
        WriteManualSprite(machine, sprite: 1, SpriteX, SpriteY,
            dataA: 0x8000, dataB: 0, attached: true, ref cycle);

        machine.ExecuteFrame();

        Assert.Equal(Black, Pixel(machine.Framebuffer.Span, SpriteX, SpriteY));
    }

    [Fact]
    public void EvenSpriteHasPriorityOverOddSpriteWithinPalettePair()
    {
        using var machine = CreateStoppedMachine();
        long cycle = machine.Cycle;
        ConfigureWindow(machine, ref cycle);
        Write(machine, Color(17), 0x0F00, ref cycle);
        Write(machine, Color(18), 0x00F0, ref cycle);
        WriteManualSprite(machine, sprite: 1, SpriteX, SpriteY,
            dataA: 0, dataB: 0x8000, attached: false, ref cycle);
        WriteManualSprite(machine, sprite: 0, SpriteX, SpriteY,
            dataA: 0x8000, dataB: 0, attached: false, ref cycle);

        machine.ExecuteFrame();

        Assert.Equal(Red, Pixel(machine.Framebuffer.Span, SpriteX, SpriteY));
    }

    [Fact]
    public void ControlWriteDisarmsAndZeroHeightCannotExposeStaleData()
    {
        using var machine = CreateStoppedMachine();
        long cycle = machine.Cycle;
        ConfigureWindow(machine, ref cycle);
        Write(machine, Color(17), 0x0F00, ref cycle);
        WriteManualSprite(machine, sprite: 0, SpriteX, SpriteY,
            dataA: 0x8000, dataB: 0, attached: false, ref cycle);
        var (sameLinePos, zeroHeightCtl) = EncodePosition(
            SpriteX, SpriteY, height: 0);
        Write(machine, Register(0, 0), sameLinePos, ref cycle);
        Write(machine, Register(0, 2), zeroHeightCtl, ref cycle);

        machine.ExecuteFrame();

        Assert.False(machine.IsManualSpriteArmed(0));
        Assert.Equal(Black, Pixel(machine.Framebuffer.Span, SpriteX, SpriteY));
    }

    [Theory]
    [InlineData(0x0000, Red)]
    [InlineData(0x0008, Green)]
    public void Bplcon2PlacesSpritePairAroundNormalPlayfield(
        ushort bplcon2,
        int expected)
    {
        using var machine = CreateStoppedMachine();
        long cycle = machine.Cycle;
        ConfigureOnePlane(machine, ref cycle);
        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Bplcon2,
            bplcon2, ref cycle);
        Write(machine, Color(17), 0x00F0, ref cycle);
        WriteManualSprite(machine, sprite: 0, SpriteX, SpriteY,
            dataA: 0x8000, dataB: 0, attached: false, ref cycle);

        machine.ExecuteFrame();

        Assert.Equal(expected, Pixel(machine.Framebuffer.Span, SpriteX, SpriteY));
    }

    [Fact]
    public void DataWriteAfterHorizontalMatchStartsOnFollowingLine()
    {
        using var machine = CreateStoppedMachine();
        long cycle = machine.Cycle;
        ConfigureWindow(machine, ref cycle);
        Write(machine, Color(17), 0x0F00, ref cycle);
        var (pos, ctl) = EncodePosition(SpriteX, SpriteY, height: 1);
        Write(machine, Register(0, 0), pos, ref cycle);
        Write(machine, Register(0, 2), ctl, ref cycle);
        Write(machine, Register(0, 6), 0, ref cycle);
        var afterMatch = ((long)SpriteY * LightweightClock.CpuCyclesPerLine) +
            (100L * LightweightClock.CpuCyclesPerColorClock);
        machine.AdvanceHardwareTo(afterMatch);
        cycle = afterMatch;

        Write(machine, Register(0, 4), 0x8000, ref cycle);
        machine.ExecuteFrame();

        Assert.Equal(Black, Pixel(machine.Framebuffer.Span, SpriteX, SpriteY));
        Assert.Equal(Red, Pixel(machine.Framebuffer.Span, SpriteX, SpriteY + 1));
    }

    [Fact]
    public void SpriteShifterAdvancesWhileClippedByDisplayWindow()
    {
        using var machine = CreateStoppedMachine();
        long cycle = machine.Cycle;
        ConfigureWindow(machine, ref cycle);
        Write(machine, Color(17), 0x0F00, ref cycle);
        WriteManualSprite(machine, sprite: 0, SpriteX - 4, SpriteY,
            dataA: 0xF800, dataB: 0, attached: false, ref cycle);

        machine.ExecuteFrame();
        var frame = machine.Framebuffer.Span;

        Assert.Equal(Black, Pixel(frame, SpriteX - 1, SpriteY));
        Assert.Equal(Red, Pixel(frame, SpriteX, SpriteY));
        Assert.Equal(Black, Pixel(frame, SpriteX + 1, SpriteY));
    }

    [Fact]
    public void AdjacentPixelsWithinOneCckRetainIndependentSpriteBits()
    {
        using var machine = CreateStoppedMachine();
        long cycle = machine.Cycle;
        ConfigureWindow(machine, ref cycle);
        Write(machine, Color(17), 0x0F00, ref cycle);
        Write(machine, Color(18), 0x00F0, ref cycle);
        WriteManualSprite(machine, sprite: 0, SpriteX, SpriteY,
            dataA: 0x4000, dataB: 0x2000, attached: false, ref cycle);

        machine.ExecuteFrame();
        var frame = machine.Framebuffer.Span;

        Assert.Equal(Red, Pixel(frame, SpriteX + 1, SpriteY));
        Assert.Equal(Green, Pixel(frame, SpriteX + 2, SpriteY));
    }

    private static LightweightA500Machine CreateStoppedMachine()
    {
        var machine = new LightweightA500Machine();
        long cycle = machine.Cycle;
        Write(machine, 0x1000, 0x4E72, ref cycle);
        Write(machine, 0x1002, 0x2700, ref cycle);
        machine.Reset();
        return machine;
    }

    private static void ConfigureWindow(
        LightweightA500Machine machine,
        ref long cycle)
    {
        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Diwstrt,
            0x2C81, ref cycle);
        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Diwstop,
            0x2CC1, ref cycle);
    }

    private static void ConfigureOnePlane(
        LightweightA500Machine machine,
        ref long cycle)
    {
        const uint pointer = 0x2000;
        Write(machine, pointer, 0x8000, ref cycle);
        Write(machine, pointer + 2, 0, ref cycle);
        ConfigureWindow(machine, ref cycle);
        Write(machine, Color(0), 0, ref cycle);
        Write(machine, Color(1), 0x0F00, ref cycle);
        Write(machine,
            LightweightA500Machine.CustomBase + LightweightRegisters.BplPointerFirst,
            0, ref cycle);
        Write(machine,
            LightweightA500Machine.CustomBase + LightweightRegisters.BplPointerFirst + 2,
            (ushort)pointer, ref cycle);
        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Ddfstrt,
            0x0038, ref cycle);
        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Ddfstop,
            0x0040, ref cycle);
        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Bplcon0,
            0x1000, ref cycle);
        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.DmaconWrite,
            0x8300, ref cycle);
    }

    private static void WriteManualSprite(
        LightweightA500Machine machine,
        int sprite,
        int x,
        int y,
        ushort dataA,
        ushort dataB,
        bool attached,
        ref long cycle)
    {
        var (pos, ctl) = EncodePosition(x, y, height: 1, attached);
        Write(machine, Register(sprite, 0), pos, ref cycle);
        Write(machine, Register(sprite, 2), ctl, ref cycle);
        Write(machine, Register(sprite, 6), dataB, ref cycle);
        Write(machine, Register(sprite, 4), dataA, ref cycle);
    }

    private static (ushort Pos, ushort Ctl) EncodePosition(
        int x,
        int y,
        int height,
        bool attached = false)
    {
        var horizontal = x - 1;
        var stop = y + height;
        var pos = (ushort)(((y & 0xFF) << 8) | ((horizontal >> 1) & 0xFF));
        var ctl = (ushort)(((stop & 0xFF) << 8) |
            (horizontal & 1) |
            ((stop & 0x100) != 0 ? 0x0002 : 0) |
            ((y & 0x100) != 0 ? 0x0004 : 0) |
            (attached ? 0x0080 : 0));
        return (pos, ctl);
    }

    private static uint Register(int sprite, int registerOffset)
        => LightweightA500Machine.CustomBase +
            (uint)(LightweightRegisters.SpritePosFirst + (sprite * 8) + registerOffset);

    private static uint Color(int index)
        => LightweightA500Machine.CustomBase +
            (uint)(LightweightRegisters.ColorFirst + (index * 2));

    private static int Pixel(ReadOnlySpan<int> frame, int x, int y)
        => frame[(y * Width) + x];

    private static ushort UniqueColor(int index)
        => (ushort)(((index & 0x0F) << 8) |
            (((index * 3) & 0x0F) << 4) |
            ((index * 5) & 0x0F));

    private static int ToArgb(ushort encoded)
    {
        var r = ((encoded >> 8) & 0x0F) * 17;
        var g = ((encoded >> 4) & 0x0F) * 17;
        var b = (encoded & 0x0F) * 17;
        return unchecked((int)(0xFF000000u |
            ((uint)r << 16) | ((uint)g << 8) | (uint)b));
    }

    private static void Write(
        LightweightA500Machine machine,
        uint address,
        ushort value,
        ref long cycle)
        => machine.WriteWord(address, value, ref cycle, M68kBusAccessKind.CpuDataWrite);

    private const int Black = unchecked((int)0xFF000000);
    private const int Red = unchecked((int)0xFFFF0000);
    private const int Green = unchecked((int)0xFF00FF00);
    private const int Blue = unchecked((int)0xFF0000FF);
}
