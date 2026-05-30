using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class AmigaBitplaneConformanceMatrixTests
{
    private const int StandardX = AmigaConstants.PalLowResOverscanBorderX;
    private const int StandardY = AmigaConstants.PalLowResOverscanBorderY;

    public static IEnumerable<object[]> MatrixRows => Rows.Select(row => new object[] { row });

    public static IEnumerable<object[]> ExecutableRows => Rows
        .Where(row => row.Status == MatrixRowStatus.Executable)
        .Select(row => new object[] { row });

    [Fact]
    public void BitplaneMatrixCoversA500PalOcsFeatureGroups()
    {
        var requiredGroups = new[]
        {
            "bplcon",
            "plane-count",
            "resolution",
            "interlace",
            "display-window",
            "fetch-window",
            "modulo",
            "scroll",
            "dual-playfield",
            "ehb-ham",
            "palette",
            "dma-control"
        };

        var groups = Rows.Select(row => row.Group).Distinct().ToArray();
        foreach (var required in requiredGroups)
        {
            Assert.Contains(required, groups);
        }

        Assert.All(Rows.Where(row => row.Status == MatrixRowStatus.Pending), row => Assert.False(string.IsNullOrWhiteSpace(row.Reason)));
        Assert.True(Rows.Count(row => row.Status == MatrixRowStatus.Executable) >= 12);
    }

    [Theory]
    [MemberData(nameof(ExecutableRows))]
    public void ExecutableBitplaneMatrixRowsPass(object rowObject)
    {
        var row = Assert.IsType<MatrixRow>(rowObject);
        switch (row.Name)
        {
            case "BPLCON0 enables lowres bitplanes":
                Bplcon0EnablesLowResBitplanes();
                break;
            case "plane count selects color index bits":
                PlaneCountSelectsColorIndexBits();
                break;
            case "lowres pixels are doubled in highres output":
                LowResPixelsAreDoubledInHighResOutput();
                break;
            case "hires keeps separate subpixels":
                HiresKeepsSeparateSubpixels();
                break;
            case "interlace alternates field rows":
                InterlaceAlternatesFieldRows();
                break;
            case "DIW clips and positions the playfield":
                DisplayWindowClipsAndPositionsPlayfield();
                break;
            case "DDF controls line stride":
                DataFetchControlsLineStride();
                break;
            case "BPL1MOD advances odd plane rows":
                Bpl1ModAdvancesOddPlaneRows();
                break;
            case "BPLCON1 scrolls odd and even planes":
                Bplcon1ScrollsOddAndEvenPlanes();
                break;
            case "dual playfield separates color groups and priority":
                DualPlayfieldSeparatesColorGroupsAndPriority();
                break;
            case "EHB uses half-bright colors":
                ExtraHalfBrightUsesHalfBrightColors();
                break;
            case "HAM holds and modifies color":
                HamHoldsAndModifiesColor();
                break;
            case "Copper or CPU palette changes affect later rows":
                TimedPaletteChangesAffectLaterRows();
                break;
            case "DMAEN and BPLEN gate timed bitplane fetches":
                DmaEnableGatesTimedBitplaneFetches();
                break;
            default:
                throw new InvalidOperationException($"No executable assertion is wired for bitplane row '{row.Name}'.");
        }
    }

    [Theory]
    [MemberData(nameof(MatrixRows))]
    public void PendingBitplaneRowsDocumentTheirReason(object rowObject)
    {
        var row = Assert.IsType<MatrixRow>(rowObject);
        if (row.Status == MatrixRowStatus.Pending)
        {
            Assert.False(string.IsNullOrWhiteSpace(row.Reason));
        }
    }

    private static readonly MatrixRow[] Rows =
    {
        Executable("bplcon", "BPLCON0 enables lowres bitplanes"),
        Executable("plane-count", "plane count selects color index bits"),
        Executable("resolution", "lowres pixels are doubled in highres output"),
        Executable("resolution", "hires keeps separate subpixels"),
        Executable("interlace", "interlace alternates field rows"),
        Executable("display-window", "DIW clips and positions the playfield"),
        Executable("fetch-window", "DDF controls line stride"),
        Executable("modulo", "BPL1MOD advances odd plane rows"),
        Executable("scroll", "BPLCON1 scrolls odd and even planes"),
        Executable("dual-playfield", "dual playfield separates color groups and priority"),
        Executable("ehb-ham", "EHB uses half-bright colors"),
        Executable("ehb-ham", "HAM holds and modifies color"),
        Executable("palette", "Copper or CPU palette changes affect later rows"),
        Executable("dma-control", "DMAEN and BPLEN gate timed bitplane fetches"),
        Pending("fetch-window", "cycle-exact fetch slot contention at DDF edges", "The renderer models fetched words, not every OCS DMA slot yet."),
        Pending("dma-control", "bitplane DMA starvation of late sprite slots", "Covered as pending in sprite matrix until the shared DMA scheduler is exact."),
        Pending("resolution", "ECS/AGA superhires and productivity modes", "Out of scope for A500 PAL OCS."),
        Pending("palette", "genlock/borderblank analog display effects", "Out of scope for game/demo-relevant OCS digital framebuffer tests.")
    };

    private static void Bplcon0EnablesLowResBitplanes()
    {
        var bus = CreateDisplayBus();
        SetBitplanePointer(bus, 0, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x8000);
        bus.WriteWord(0x00DFF100, 0x1000);

        var frame = RenderLowResFrame(bus);

        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY));
        Assert.Equal(0xFF000000u, Pixel(frame, StandardX + 1, StandardY));
    }

    private static void PlaneCountSelectsColorIndexBits()
    {
        for (var planeCount = 1; planeCount <= 5; planeCount++)
        {
            var bus = CreateDisplayBus();
            var colorIndex = (1 << planeCount) - 1;
            bus.WriteWord((uint)(0x00DFF180 + (colorIndex * 2)), 0x0F00);
            for (var plane = 0; plane < planeCount; plane++)
            {
                var pointer = (uint)(0x1000 + (plane * 0x100));
                SetBitplanePointer(bus, plane, pointer);
                BigEndian.WriteUInt16(bus.ChipRam, (int)pointer, 0x8000);
            }

            bus.WriteWord(0x00DFF100, (ushort)(planeCount << 12));
            var frame = RenderLowResFrame(bus);

            Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY));
        }
    }

    private static void LowResPixelsAreDoubledInHighResOutput()
    {
        var bus = CreateDisplayBus();
        SetBitplanePointer(bus, 0, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x8000);
        bus.WriteWord(0x00DFF100, 0x1000);
        var frame = new uint[bus.Display.Width * bus.Display.Height];

        bus.Display.RenderFrame(frame);

        var x = StandardX * 2;
        var y = StandardY * 2;
        Assert.Equal(0xFFFF0000u, HighResPixel(frame, bus.Display.Width, x, y));
        Assert.Equal(0xFFFF0000u, HighResPixel(frame, bus.Display.Width, x + 1, y));
        Assert.Equal(0xFF000000u, HighResPixel(frame, bus.Display.Width, x + 2, y));
    }

    private static void HiresKeepsSeparateSubpixels()
    {
        var bus = CreateDisplayBus();
        bus.WriteWord(0x00DFF092, 0x003C);
        bus.WriteWord(0x00DFF094, 0x00D0);
        SetBitplanePointer(bus, 0, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1050, 0x4000);
        bus.WriteWord(0x00DFF100, 0x9000);
        var frame = new uint[bus.Display.Width * bus.Display.Height];

        bus.Display.RenderFrame(frame);

        var y = StandardY * 2;
        Assert.Equal(0xFFFF0000u, HighResPixel(frame, bus.Display.Width, StandardX * 2, y));
        Assert.Equal(0xFF000000u, HighResPixel(frame, bus.Display.Width, (StandardX * 2) + 1, y));
        Assert.Equal(0xFF000000u, HighResPixel(frame, bus.Display.Width, StandardX * 2, y + 2));
        Assert.Equal(0xFFFF0000u, HighResPixel(frame, bus.Display.Width, (StandardX * 2) + 1, y + 2));
    }

    private static void InterlaceAlternatesFieldRows()
    {
        var bus = CreateDisplayBus();
        var frameCycle = FrameCycles();
        SetBitplanePointer(bus, 0, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x8000);
        bus.WriteWord(0x00DFF100, 0x1004);
        bus.WriteWord(0x00DFF096, 0x8300);
        var frame = new uint[bus.Display.Width * bus.Display.Height];
        Array.Fill(frame, 0xFF000000u);

        bus.Display.RenderFrame(frame, 0, frameCycle);
        Assert.Equal(0xFFFF0000u, HighResPixel(frame, bus.Display.Width, StandardX * 2, StandardY * 2));
        Assert.Equal(0xFF000000u, HighResPixel(frame, bus.Display.Width, StandardX * 2, (StandardY * 2) + 1));

        bus.Display.RenderFrame(frame, frameCycle, frameCycle * 2);
        Assert.Equal(0xFFFF0000u, HighResPixel(frame, bus.Display.Width, StandardX * 2, (StandardY * 2) + 1));
    }

    private static void DisplayWindowClipsAndPositionsPlayfield()
    {
        var bus = CreateDisplayBus();
        bus.WriteWord(0x00DFF08E, 0x2C91);
        bus.WriteWord(0x00DFF090, 0x2DA1);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x0040);
        SetBitplanePointer(bus, 0, 0x1000);
        bus.WriteWord(0x00DFF100, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1002, 0x8000);

        var frame = RenderLowResFrame(bus);

        Assert.Equal(0xFF000000u, Pixel(frame, StandardX + 15, StandardY));
        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX + 16, StandardY));
    }

    private static void DataFetchControlsLineStride()
    {
        var bus = CreateDisplayBus();
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x0038);
        SetBitplanePointer(bus, 0, 0x1000);
        bus.WriteWord(0x00DFF100, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1002, 0x4000);

        var frame = RenderLowResFrame(bus);

        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY));
        Assert.Equal(0xFF000000u, Pixel(frame, StandardX + 16, StandardY));
        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX + 1, StandardY + 1));
    }

    private static void Bpl1ModAdvancesOddPlaneRows()
    {
        var bus = CreateDisplayBus();
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x0038);
        bus.WriteWord(0x00DFF108, 0x0002);
        SetBitplanePointer(bus, 0, 0x1000);
        bus.WriteWord(0x00DFF100, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1002, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1004, 0x8000);

        var frame = RenderLowResFrame(bus);

        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY));
        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY + 1));
    }

    private static void Bplcon1ScrollsOddAndEvenPlanes()
    {
        var bus = CreateDisplayBus();
        bus.WriteWord(0x00DFF184, 0x00F0);
        bus.WriteWord(0x00DFF186, 0x0FF0);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x0038);
        SetBitplanePointer(bus, 0, 0x1000);
        SetBitplanePointer(bus, 1, 0x2000);
        bus.WriteWord(0x00DFF102, 0x0001);
        bus.WriteWord(0x00DFF100, 0x2000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2000, 0xC000);

        var frame = RenderLowResFrame(bus);

        Assert.Equal(0xFF00FF00u, Pixel(frame, StandardX, StandardY));
        Assert.Equal(0xFFFFFF00u, Pixel(frame, StandardX + 1, StandardY));
    }

    private static void DualPlayfieldSeparatesColorGroupsAndPriority()
    {
        var bus = CreateDisplayBus();
        bus.WriteWord(0x00DFF192, 0x00F0);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x0038);
        SetBitplanePointer(bus, 0, 0x1000);
        SetBitplanePointer(bus, 1, 0x1100);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0xC000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1100, 0x8000);
        bus.WriteWord(0x00DFF100, 0x2400);

        var frame = RenderLowResFrame(bus);
        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY));

        bus.WriteWord(0x00DFF104, 0x0040);
        Array.Clear(frame);
        bus.Display.RenderFrame(frame);
        Assert.Equal(0xFF00FF00u, Pixel(frame, StandardX, StandardY));
    }

    private static void ExtraHalfBrightUsesHalfBrightColors()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF1BE, 0x0EEE);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x0038);
        for (var plane = 0; plane < 6; plane++)
        {
            var pointer = (uint)(0x1000 + (plane * 0x100));
            SetBitplanePointer(bus, plane, pointer);
            BigEndian.WriteUInt16(bus.ChipRam, (int)pointer, 0x8000);
        }

        bus.WriteWord(0x00DFF100, 0x6000);
        var frame = RenderLowResFrame(bus);

        Assert.Equal(0xFF777777u, Pixel(frame, StandardX, StandardY));
    }

    private static void HamHoldsAndModifiesColor()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF184, 0x0120);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x0038);
        for (var plane = 0; plane < 6; plane++)
        {
            SetBitplanePointer(bus, plane, (uint)(0x1000 + (plane * 0x100)));
        }

        BigEndian.WriteUInt16(bus.ChipRam, 0x1100, 0x8000);
        for (var plane = 0; plane <= 4; plane++)
        {
            var offset = 0x1000 + (plane * 0x100);
            BigEndian.WriteUInt16(bus.ChipRam, offset, (ushort)(BigEndian.ReadUInt16(bus.ChipRam, offset, "HAM test") | 0x4000));
        }

        bus.WriteWord(0x00DFF100, 0x6800);
        var frame = RenderLowResFrame(bus);

        Assert.Equal(0xFF112200u, Pixel(frame, StandardX, StandardY));
        Assert.Equal(0xFF1122FFu, Pixel(frame, StandardX + 1, StandardY));
    }

    private static void TimedPaletteChangesAffectLaterRows()
    {
        var bus = CreateDisplayBus();
        var lineCycles = AmigaConstants.A500PalCpuClockHz / AmigaConstants.A500PalVBlankHz / AmigaConstants.A500PalRasterLines;
        var row1Cycle = (long)Math.Round(0x2D * lineCycles);
        var frameCycles = FrameCycles();
        SetBitplanePointer(bus, 0, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1028, 0x8000);
        bus.WriteWord(0x00DFF096, 0x8300, 0);
        bus.WriteWord(0x00DFF100, 0x1000, 0);
        bus.WriteWord(0x00DFF182, 0x00F0, row1Cycle);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame, 0, frameCycles);

        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY));
        Assert.Equal(0xFF00FF00u, Pixel(frame, StandardX, StandardY + 1));
    }

    private static void DmaEnableGatesTimedBitplaneFetches()
    {
        var bus = CreateDisplayBus();
        var frameCycles = FrameCycles();
        SetBitplanePointer(bus, 0, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x8000);
        bus.WriteWord(0x00DFF100, 0x1000);

        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
        bus.Display.RenderFrame(frame, 0, frameCycles);
        Assert.Equal(0xFF000000u, Pixel(frame, StandardX, StandardY));

        bus.WriteWord(0x00DFF096, 0x8300, frameCycles);
        Array.Clear(frame);
        bus.Display.RenderFrame(frame, frameCycles, frameCycles * 2);
        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY));
    }

    private static AmigaBus CreateDisplayBus()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF182, 0x0F00);
        return bus;
    }

    private static void SetBitplanePointer(AmigaBus bus, int plane, uint address)
    {
        var offset = (uint)(0x00DFF0E0 + (plane * 4));
        bus.WriteWord(offset, (ushort)(address >> 16));
        bus.WriteWord(offset + 2, (ushort)address);
    }

    private static uint[] RenderLowResFrame(AmigaBus bus)
    {
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
        bus.Display.RenderFrame(frame);
        return frame;
    }

    private static uint Pixel(uint[] frame, int x, int y)
    {
        return frame[(y * AmigaConstants.PalLowResWidth) + x];
    }

    private static uint HighResPixel(uint[] frame, int width, int x, int y)
    {
        return frame[(y * width) + x];
    }

    private static long FrameCycles()
    {
        return (long)Math.Round(AmigaConstants.A500PalCpuClockHz / AmigaConstants.A500PalVBlankHz);
    }

    private static MatrixRow Executable(string group, string name)
    {
        return new MatrixRow(group, name, MatrixRowStatus.Executable, string.Empty);
    }

    private static MatrixRow Pending(string group, string name, string reason)
    {
        return new MatrixRow(group, name, MatrixRowStatus.Pending, reason);
    }

    private sealed record MatrixRow(string Group, string Name, MatrixRowStatus Status, string Reason)
    {
        public override string ToString() => $"{Group}: {Name} ({Status})";
    }

    private enum MatrixRowStatus
    {
        Executable,
        Pending
    }
}
