using Copper68k;
using Xunit;

namespace CopperMod.Amiga.Lightweight.Tests;

public sealed class LightweightVideoTests
{
    private const int Width = 454;
    private const int Height = 313;

    [Fact]
    public void HostReceivesUncroppedReusablePalField()
    {
        using var machine = new LightweightA500Machine();

        Assert.Equal(Width * Height, machine.Framebuffer.Length);
        Assert.Empty(machine.AudioSamples.ToArray()); // No completed field yet.
    }

    [Fact]
    public void FixedBlankingRemainsBlackWhileColorZeroFillsVisibleBorder()
    {
        using var machine = CreateStoppedMachine();
        long cycle = machine.Cycle;
        Write(machine, LightweightA500Machine.CustomBase + 0x180, 0x0F00, ref cycle);

        machine.ExecuteFrame();
        var frame = machine.Framebuffer.Span;

        Assert.Equal(Black, Pixel(frame, 97, 30));
        Assert.Equal(Red, Pixel(frame, 98, 30));
        Assert.Equal(Black, Pixel(frame, 100, 25));
        Assert.Equal(Red, Pixel(frame, 100, 26));
        Assert.Equal(Red, Pixel(frame, 100, 310));
        Assert.Equal(Black, Pixel(frame, 100, 311));
    }

    [Fact]
    public void LowResDmaWordLoadsAfterItsLatchPixelAndShiftsSixteenPixels()
    {
        using var machine = CreateStoppedMachine();
        ConfigureOnePlane(machine, diwStart: 0x2C81, firstWord: 0x8001);

        machine.ExecuteFrame();
        var frame = machine.Framebuffer.Span;

        Assert.Equal(Black, Pixel(frame, 128, 44));
        Assert.Equal(Red, Pixel(frame, 129, 44));
        Assert.Equal(Black, Pixel(frame, 130, 44));
        Assert.Equal(Black, Pixel(frame, 143, 44));
        Assert.Equal(Red, Pixel(frame, 144, 44));
        Assert.Equal(Black, Pixel(frame, 145, 44));
    }

    [Fact]
    public void DiwClipsAlreadyShiftingBitplaneDataWithoutMovingIt()
    {
        using var machine = CreateStoppedMachine();
        ConfigureOnePlane(machine, diwStart: 0x2C90, firstWord: 0xFFFF, colorZero: 0x000F);

        machine.ExecuteFrame();
        var frame = machine.Framebuffer.Span;

        Assert.Equal(Blue, Pixel(frame, 143, 44));
        Assert.Equal(Red, Pixel(frame, 144, 44));
    }

    [Fact]
    public void CpuPaletteWriteChangesThePixelsOfItsPhysicalOutputCck()
    {
        using var machine = CreateStoppedMachine();
        long cycle = machine.Cycle;
        Write(machine, LightweightA500Machine.CustomBase + 0x180, 0x0F00, ref cycle);
        var writeCycle = (30L * LightweightClock.CpuCyclesPerLine) + (100L * 2);
        machine.AdvanceHardwareTo(writeCycle);
        cycle = writeCycle;

        Write(machine, LightweightA500Machine.CustomBase + 0x180, 0x00F0, ref cycle);
        machine.ExecuteFrame();
        var frame = machine.Framebuffer.Span;

        Assert.Equal(Red, Pixel(frame, 197, 30));
        Assert.Equal(Green, Pixel(frame, 198, 30));
        Assert.Equal(Green, Pixel(frame, 199, 30));
    }

    [Fact]
    public void ManualBpl1DatUsesTheSameFollowingInputAndReloadBoundary()
    {
        using var machine = CreateStoppedMachine();
        long cycle = machine.Cycle;
        Write(machine, LightweightA500Machine.CustomBase + 0x180, 0x0000, ref cycle);
        Write(machine, LightweightA500Machine.CustomBase + 0x182, 0x0F00, ref cycle);
        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Diwstrt, 0x2C81, ref cycle);
        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Diwstop, 0x2CC1, ref cycle);
        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Bplcon0, 0x1000, ref cycle);
        var dataCycle = (44L * LightweightClock.CpuCyclesPerLine) + (64L * 2);
        machine.AdvanceHardwareTo(dataCycle);
        cycle = dataCycle;

        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.BpldatFirst, 0x8000, ref cycle);
        machine.ExecuteFrame();
        var frame = machine.Framebuffer.Span;

        Assert.Equal(Black, Pixel(frame, 128, 44));
        Assert.Equal(Red, Pixel(frame, 129, 44));
        Assert.Equal(Black, Pixel(frame, 130, 44));
    }

    [Fact]
    public void LaceSelectsAlternatingLongAndShortPalFields()
    {
        using var machine = CreateStoppedMachine();
        long cycle = machine.Cycle;
        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Bplcon0, 0x0004, ref cycle);

        machine.ExecuteFrame();
        var firstBoundary = machine.Cycle;
        Assert.False(machine.IsLongField);
        machine.ExecuteFrame();

        Assert.Equal(LightweightClock.PalShortFieldCycles, machine.Cycle - firstBoundary);
        Assert.True(machine.IsLongField);
        Assert.Equal(2, machine.CompletedFrames);
    }

    [Fact]
    public void UnsupportedDisplayModeIsReportedInsteadOfSilentlyAccepted()
    {
        using var machine = CreateStoppedMachine();
        long cycle = machine.Cycle;
        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Bplcon0, 0x9000, ref cycle);
        machine.AdvanceHardwareTo(cycle + 2);

        Assert.Equal("OCS hires output", machine.UnsupportedActiveFeature);

        cycle = machine.Cycle;
        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Bplcon0, 0x1000, ref cycle);
        machine.AdvanceHardwareTo(cycle + 2);

        Assert.Equal("OCS hires output", machine.UnsupportedActiveFeature);
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

    private static void ConfigureOnePlane(
        LightweightA500Machine machine,
        ushort diwStart,
        ushort firstWord,
        ushort colorZero = 0)
    {
        const uint pointer = 0x2000;
        long cycle = machine.Cycle;
        Write(machine, pointer, firstWord, ref cycle);
        Write(machine, pointer + 2, 0, ref cycle);
        Write(machine, LightweightA500Machine.CustomBase + 0x180, colorZero, ref cycle);
        Write(machine, LightweightA500Machine.CustomBase + 0x182, 0x0F00, ref cycle);
        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.BplPointerFirst, 0, ref cycle);
        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.BplPointerFirst + 2, (ushort)pointer, ref cycle);
        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Diwstrt, diwStart, ref cycle);
        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Diwstop, 0x2CC1, ref cycle);
        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Ddfstrt, 0x0038, ref cycle);
        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Ddfstop, 0x0040, ref cycle);
        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Bplcon0, 0x1000, ref cycle);
        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.DmaconWrite, 0x8300, ref cycle);
        machine.Cpu.Cycles = cycle;
    }

    private static int Pixel(ReadOnlySpan<int> frame, int x, int y)
        => frame[(y * Width) + x];

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
