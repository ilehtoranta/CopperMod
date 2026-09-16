using Copper68k;
using Xunit;

namespace CopperMod.Amiga.Lightweight.Tests;

public sealed class LightweightHiresTests
{
    private const int Width = 908;
    private const int Black = unchecked((int)0xFF000000), Red = unchecked((int)0xFFFF0000);

    [Fact]
    public void HiresUsesHrmFourTwoThreeOneOrderAndFollowingCckCompletion()
    {
        using var m = Create();
        Configure(m, 4, line: 1);
        int[] order = [3, 1, 2, 0, 3, 1, 2, 0];
        for (var slot = 0; slot < order.Length; slot++)
        {
            var cycle = 454L + 2 * (0x3D + slot);
            m.AdvanceHardwareTo(cycle - 2);
            Assert.Equal(cycle, m.BitplanePendingOutputCycle);
            m.AdvanceHardwareTo(cycle);
            Assert.Equal(order[slot], m.BitplaneLastPlane);
            Assert.Equal(cycle, m.BitplaneLastOutputCycle);
        }
    }

    [Fact]
    public void HiresFetchesFortyWordsAndAppliesModuloOnlyOncePerPlane()
    {
        using var m = Create();
        Configure(m, 4, line: 1);
        W(m, 0x108, 6); W(m, 0x10A, 10);
        m.AdvanceHardwareTo(454 + 2 * 0xDC);
        for (var plane = 0; plane < 4; plane++)
            Assert.Equal(Address(plane) + 80u + (plane % 2 == 0 ? 6u : 10u), m.GetLiveBitplanePointer(plane));
    }

    [Fact]
    public void FourPlaneHiresContendsWithCpuUntilLastPhysicalWordCompletes()
    {
        using var m = Create();
        Configure(m, 4, line: 1);
        m.AdvanceHardwareTo(454 + 2 * 0x3D);
        var cycle = m.Cycle;
        m.ReadWord(0x10000, ref cycle, M68kBusAccessKind.CpuDataRead);
        Assert.Equal(454 + 2 * 0xDE, cycle);
    }

    [Fact]
    public void HiresRetainsSixteenIndependentPixelsAtNormalWindowStart()
    {
        using var m = Create();
        Configure(m, 1);
        var cycle = m.Cycle;
        m.WriteWord(Address(0), 0x8001, ref cycle, M68kBusAccessKind.CpuDataWrite);
        m.ExecuteFrame();
        var pixels = m.Framebuffer.Span;
        Assert.Equal(Width * 313, pixels.Length);
        Assert.Equal(Black, pixels[44 * Width + 257]);
        Assert.Equal(Red, pixels[44 * Width + 258]);
        Assert.Equal(Black, pixels[44 * Width + 259]);
        Assert.Equal(Black, pixels[44 * Width + 272]);
        Assert.Equal(Red, pixels[44 * Width + 273]);
        Assert.Equal(Black, pixels[44 * Width + 274]);
        Assert.Null(m.UnsupportedActiveFeature);
    }

    [Fact]
    public void WideLowresOutputDuplicatesPixelsWithoutChangingTheirPhases()
    {
        using var m = Create();
        Configure(m, 1);
        W(m, 0x100, 0x1200); W(m, 0x092, 0x38); W(m, 0x094, 0xD0);
        var cycle = m.Cycle;
        m.WriteWord(Address(0), 0x8001, ref cycle, M68kBusAccessKind.CpuDataWrite);
        m.ExecuteFrame();
        var pixels = m.Framebuffer.Span;
        Assert.Equal(Red, pixels[44 * Width + 258]);
        Assert.Equal(Red, pixels[44 * Width + 259]);
        Assert.Equal(Black, pixels[44 * Width + 260]);
        Assert.Equal(Red, pixels[44 * Width + 288]);
        Assert.Equal(Red, pixels[44 * Width + 289]);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
    [InlineData(12)] [InlineData(13)] [InlineData(14)] [InlineData(15)]
    public void HiresScrollUsesLoresUnitsModuloEight(int scroll)
    {
        using var m = Create();
        Configure(m, 1);
        W(m, 0x102, (ushort)scroll);
        var cycle = m.Cycle;
        m.WriteWord(Address(0), 0x8000, ref cycle, M68kBusAccessKind.CpuDataWrite);
        m.ExecuteFrame();
        var x = 258 + 2 * (scroll & 7);
        Assert.Equal(Black, m.Framebuffer.Span[44 * Width + x - 1]);
        Assert.Equal(Red, m.Framebuffer.Span[44 * Width + x]);
        Assert.Equal(Black, m.Framebuffer.Span[44 * Width + x + 1]);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SpriteAdvancesOnceAndPriorityIsResolvedForBothHiresHalves(bool attached)
    {
        using var m = Create();
        Configure(m, 1);
        W(m, 0x104, 0); // Playfield in front, except at transparent pixels.
        W(m, 0x1A2, 0x0F0); W(m, 0x1AA, 0x0F0);
        W(m, 0x140, 0x2C40); W(m, 0x142, 0x2D00); // Comparator 128, output at 129.
        W(m, 0x146, 0); W(m, 0x144, 0x8000);
        if (attached)
        {
            W(m, 0x148, 0x2C40); W(m, 0x14A, 0x2D80);
            W(m, 0x14E, 0); W(m, 0x14C, 0x8000);
        }
        var cycle = m.Cycle;
        m.WriteWord(Address(0), 0x8000, ref cycle, M68kBusAccessKind.CpuDataWrite);
        m.ExecuteFrame();
        Assert.Equal(Red, m.Framebuffer.Span[44 * Width + 258]);
        Assert.Equal(unchecked((int)0xFF00FF00), m.Framebuffer.Span[44 * Width + 259]);
        Assert.Equal(Black, m.Framebuffer.Span[44 * Width + 260]);
    }

    [Fact]
    public void ModeAndScrollChangesAreDeterministicAcrossSingleCycleAdvancement()
    {
        using var coarse = Create(); using var single = Create();
        Configure(coarse, 4); Configure(single, 4);
        var target = 44 * 454L + 90 * 2;
        coarse.AdvanceHardwareTo(target);
        while (single.Cycle < target) single.AdvanceHardwareTo(single.Cycle + 1);
        W(coarse, 0x100, 0x4200); W(single, 0x100, 0x4200);
        W(coarse, 0x102, 0x73); W(single, 0x102, 0x73);
        target += 48;
        coarse.AdvanceHardwareTo(target);
        while (single.Cycle < target) single.AdvanceHardwareTo(single.Cycle + 1);
        W(coarse, 0x100, 0xC200); W(single, 0x100, 0xC200);
        coarse.ExecuteFrame(); single.ExecuteFrame();
        Assert.Equal(coarse.Framebuffer.ToArray(), single.Framebuffer.ToArray());
        for (var p = 0; p < 4; p++)
            Assert.Equal(coarse.GetLiveBitplanePointer(p), single.GetLiveBitplanePointer(p));
        Assert.Null(coarse.UnsupportedActiveFeature);
    }

    [Fact]
    public void AcceptedHiresAddressSurvivesPointerWriteAndDmaDisable()
    {
        using var m = Create(); Configure(m, 4, line: 1);
        var cycle = 454 + 2 * 0x3CL;
        m.AdvanceHardwareTo(cycle);
        Assert.Equal(cycle + 2, m.BitplanePendingOutputCycle);
        m.WriteCustomRegisterFromCopper(0x0EC, 4, cycle);
        m.WriteCustomRegisterFromCopper(0x096, 0x0100, cycle);
        m.AdvanceHardwareTo(cycle + 2);
        Assert.Equal(Address(3), m.BitplaneLastAddress);
        Assert.Equal(Address(3) + 2, m.GetLiveBitplanePointer(3));
    }

    [Fact]
    public void HiresSteadyFramesDoNotAllocateAndShortFieldTailIsCleared()
    {
        using var m = Create(); Configure(m, 4);
        W(m, 0x108, unchecked((ushort)-80)); W(m, 0x10A, unchecked((ushort)-80));
        m.ExecuteFrame(); m.ExecuteFrame();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 8; i++) m.ExecuteFrame();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        W(m, 0x02A, 0); // Current field becomes short.
        m.ExecuteFrame();
        Assert.True(m.Framebuffer.Span.Slice(312 * Width).IndexOfAnyExcept(0) < 0);
    }

    [Theory]
    [InlineData(97, 300)] [InlineData(98, 301)] [InlineData(99, 300)]
    [InlineData(128, 301)] [InlineData(129, 300)] [InlineData(130, 301)]
    [InlineData(200, 300)] [InlineData(201, 301)]
    public void HiresClipsEachHalfOfCckAtWindowAndBlankingEdges(int start, int stop)
    {
        using var m = Create(); Configure(m, 1);
        W(m, 0x08E, (ushort)(0x2C00 | start));
        W(m, 0x090, (ushort)(0x2D00 | (stop - 256)));
        W(m, 0x180, 0x00F); // Distinguish border blue from physical blank black.
        var cycle = m.Cycle;
        for (var word = 0; word < 40; word++)
            m.WriteWord(Address(0) + (uint)(word * 2), 0xFFFF, ref cycle, M68kBusAccessKind.CpuDataWrite);
        m.ExecuteFrame();
        for (var x = 0; x < Width; x++)
        {
            var lowX = x / 2;
            var expected = lowX < 98 ? Black :
                lowX >= start && lowX < stop && x >= 258 ? Red : unchecked((int)0xFF0000FF);
            Assert.Equal(expected, m.Framebuffer.Span[44 * Width + x]);
        }
    }

    [Theory]
    [InlineData(97, 300, 0)] [InlineData(98, 301, 7)]
    [InlineData(99, 300, 15)] [InlineData(128, 301, 0)]
    [InlineData(129, 300, 7)] [InlineData(130, 301, 15)]
    public void WideLowresMatchesNarrowAcrossWindowScrollAndSpriteEdges(int start, int stop, int scroll)
    {
        using var narrow = new LightweightA500Machine();
        using var wide = Create();
        foreach (var m in new[] { narrow, wide })
        {
            m.Cpu.Stopped = true;
            W(m, 0x08E, (ushort)(0x2C00 | start));
            W(m, 0x090, (ushort)(0x2F00 | (stop - 256)));
            W(m, 0x092, 0x38); W(m, 0x094, 0xD0);
            W(m, 0x180, 0x00F); W(m, 0x182, 0xF00); W(m, 0x1A2, 0x0F0);
            W(m, 0x0E0, 1); W(m, 0x0E2, 0);
            W(m, 0x100, 0x1200); W(m, 0x102, (ushort)scroll);
            W(m, 0x140, 0x2C40); W(m, 0x142, 0x2F01);
            W(m, 0x146, 0); W(m, 0x144, 0xA55A);
            var cycle = m.Cycle;
            for (var word = 0; word < 60; word++)
                m.WriteWord(Address(0) + (uint)(word * 2), (ushort)(0xA53C ^ word * 137),
                    ref cycle, M68kBusAccessKind.CpuDataWrite);
            W(m, 0x096, 0x8300);
        }
        for (var field = 0; field < 2; field++)
        {
            narrow.ExecuteFrame(); wide.ExecuteFrame();
            Assert.Equal(narrow.Cycle, wide.Cycle);
            Assert.Equal(narrow.GetLiveBitplanePointer(0), wide.GetLiveBitplanePointer(0));
            var pixels = narrow.Framebuffer.Span;
            var expanded = wide.Framebuffer.Span;
            for (var pixel = 0; pixel < pixels.Length; pixel++)
            {
                Assert.Equal(pixels[pixel], expanded[pixel * 2]);
                Assert.Equal(pixels[pixel], expanded[pixel * 2 + 1]);
            }
        }
        Assert.Null(wide.UnsupportedActiveFeature);
        Assert.Null(narrow.UnsupportedActiveFeature);
    }

    internal static LightweightA500Machine Create()
    {
        var m = new LightweightA500Machine(new() { FramebufferWidth = Width });
        m.Cpu.Stopped = true;
        return m;
    }
    private static uint Address(int plane) => 0x10000u + (uint)(plane * 0x1000);
    internal static void W(LightweightA500Machine m, ushort register, ushort value)
    {
        var cycle = m.Cycle;
        m.WriteWord(0xDFF000u + register, value, ref cycle, M68kBusAccessKind.CpuDataWrite);
    }
    internal static void Configure(LightweightA500Machine m, int planes, int line = 44)
    {
        W(m, 0x08E, (ushort)((line << 8) | 0x81)); W(m, 0x090, (ushort)(((line + 1) << 8) | 0xC1));
        W(m, 0x092, 0x3C); W(m, 0x094, 0xD4); W(m, 0x182, 0xF00);
        for (var plane = 0; plane < planes; plane++)
        {
            W(m, (ushort)(0xE0 + plane * 4), (ushort)(Address(plane) >> 16));
            W(m, (ushort)(0xE2 + plane * 4), (ushort)Address(plane));
        }
        W(m, 0x100, (ushort)(0x8200 | planes << 12)); W(m, 0x096, 0x8300);
    }
}
