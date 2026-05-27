using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class AmigaDiskDisplayTests
{
    [Fact]
    public void FloppyDriveReadsStandardAdfSectorsByCylinderHeadAndSector()
    {
        var data = new byte[AmigaDiskImage.StandardAdfSize];
        var expectedOffset = (((3 * AmigaDiskImage.HeadCount) + 1) * AmigaDiskImage.SectorsPerTrack + 7) * AmigaDiskImage.SectorSize;
        data[expectedOffset] = 0x42;
        data[expectedOffset + 511] = 0x99;
        var drive = new AmigaFloppyDrive();
        drive.Insert(AmigaDiskImage.FromAdfBytes(data));

        var sector = drive.ReadSector(3, 1, 7);

        Assert.Equal(AmigaDiskImage.SectorSize, sector.Length);
        Assert.Equal(0x42, sector[0]);
        Assert.Equal(0x99, sector[^1]);
    }

    [Fact]
    public void InvalidAdfSizesAreRejected()
    {
        var data = new byte[AmigaDiskImage.StandardAdfSize - 1];

        Assert.Throws<AmigaEmulationException>(() => AmigaDiskImage.FromAdfBytes(data));
    }

    [Fact]
    public void BootBlockChecksumValidationAcceptsOnesComplementChecksum()
    {
        var bootBlock = new byte[1024];
        bootBlock[0] = (byte)'D';
        bootBlock[1] = (byte)'O';
        bootBlock[2] = (byte)'S';
        BigEndian.WriteUInt32(bootBlock, 8, 0x0000_0370);
        BigEndian.WriteUInt32(bootBlock, 4, CalculateBootChecksum(bootBlock));

        Assert.True(AmigaBootController.HasBootableShape(bootBlock));

        bootBlock[12] ^= 0x01;
        Assert.False(AmigaBootController.HasBootableShape(bootBlock));
    }

    [Fact]
    public void DisplayDecodesOneBitplaneAndPaletteColor()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF182, 0x0F00);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x1000);
        bus.WriteWord(0x00DFF100, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x8000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFFFF0000u, frame[0]);
        Assert.Equal(0xFF000000u, frame[1]);
    }

    [Fact]
    public void DisplayUsesDataFetchWidthForBitplaneLineStride()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF182, 0x0F00);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x0038);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x1000);
        bus.WriteWord(0x00DFF100, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1002, 0x4000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFFFF0000u, frame[0]);
        Assert.Equal(0xFF000000u, frame[16]);
        Assert.Equal(0xFFFF0000u, frame[AmigaConstants.PalLowResWidth + 1]);
    }

    [Fact]
    public void DisplayHonorsDisplayWindowPosition()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF182, 0x0F00);
        bus.WriteWord(0x00DFF08E, 0x2C91);
        bus.WriteWord(0x00DFF090, 0x2DA1);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x0038);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x1000);
        bus.WriteWord(0x00DFF100, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x8000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFF000000u, frame[15]);
        Assert.Equal(0xFFFF0000u, frame[16]);
    }

    [Fact]
    public void DisplayTreatsNormalPalHorizontalStopAsFullLowResWidth()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF182, 0x0F00);
        bus.WriteWord(0x00DFF08E, 0x2C81);
        bus.WriteWord(0x00DFF090, 0x2CC1);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x00D0);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x1000);
        bus.WriteWord(0x00DFF100, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000 + 38, 0x0001);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFFFF0000u, frame[319]);
    }

    [Fact]
    public void DisplayNormalizesOddInterlaceFieldForProgressiveOutput()
    {
        var bus = new AmigaBus();
        var frameCycle = (long)Math.Round(AmigaConstants.A500PalCpuClockHz / AmigaConstants.A500PalVBlankHz);
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF182, 0x0F00);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x00D0);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x1028);
        bus.WriteWord(0x00DFF100, 0x1004);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x8000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame, 0, frameCycle);

        Assert.Equal(0xFFFF0000u, frame[0]);
    }

    [Fact]
    public void DisplayWindowClipsBeamAdvancedBitplaneRows()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF182, 0x0F00);
        bus.WriteWord(0x00DFF08E, 0x2D81);
        bus.WriteWord(0x00DFF090, 0x2E91);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x0038);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x1000);
        bus.WriteWord(0x00DFF100, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1002, 0x8000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFF000000u, frame[0]);
        Assert.Equal(0xFFFF0000u, frame[AmigaConstants.PalLowResWidth]);
    }

    [Fact]
    public void DisplayWindowUsesTopOfBitplaneDataAtWindowStart()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF182, 0x0F00);
        bus.WriteWord(0x00DFF08E, 0x9381);
        bus.WriteWord(0x00DFF090, 0xC3C1);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x00D0);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x1000);
        bus.WriteWord(0x00DFF100, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x8000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFF000000u, frame[0]);
        Assert.Equal(0xFFFF0000u, frame[103 * AmigaConstants.PalLowResWidth]);
    }

    [Fact]
    public void DisplayTreatsLowVerticalStopAsWrappedIntoLaterField()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF182, 0x0F00);
        bus.WriteWord(0x00DFF08E, 0x2841);
        bus.WriteWord(0x00DFF090, 0x44D1);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x00D0);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x1000);
        bus.WriteWord(0x00DFF100, 0x1000);
        for (var row = 0; row < 128; row++)
        {
            BigEndian.WriteUInt16(bus.ChipRam, 0x1000 + (row * 40), 0x8000);
        }
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFFFF0000u, frame[80 * AmigaConstants.PalLowResWidth]);
    }

    [Fact]
    public void DisplayUsesDataFetchStartWhenDisplayWindowBeginsInOverscan()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF182, 0x0F00);
        bus.WriteWord(0x00DFF08E, 0x2841);
        bus.WriteWord(0x00DFF090, 0x44D1);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x00D0);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x1000);
        bus.WriteWord(0x00DFF100, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000 + (24 * 40), 0x8000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFFFF0000u, frame[20 * AmigaConstants.PalLowResWidth]);
    }

    [Fact]
    public void DisplayAppliesBplcon1OddEvenPlaneScrollInSinglePlayfield()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF182, 0x0F00);
        bus.WriteWord(0x00DFF184, 0x00F0);
        bus.WriteWord(0x00DFF186, 0x0FF0);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x0038);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x1000);
        bus.WriteWord(0x00DFF0E4, 0x0000);
        bus.WriteWord(0x00DFF0E6, 0x2000);
        bus.WriteWord(0x00DFF102, 0x0001);
        bus.WriteWord(0x00DFF100, 0x2000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2000, 0xC000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFF00FF00u, frame[0]);
        Assert.Equal(0xFFFFFF00u, frame[1]);
    }

    [Fact]
    public void DisplaySixBitplaneColorIndexesUseExtraHalfBrightFallback()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF1BE, 0x0EEE);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x0038);
        for (var plane = 0; plane < 6; plane++)
        {
            var pointer = (ushort)(0x1000 + (plane * 0x100));
            bus.WriteWord((uint)(0x00DFF0E0 + (plane * 4)), 0x0000);
            bus.WriteWord((uint)(0x00DFF0E2 + (plane * 4)), pointer);
            BigEndian.WriteUInt16(bus.ChipRam, pointer, 0x8000);
        }

        bus.WriteWord(0x00DFF100, 0x6000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFF777777u, frame[0]);
    }

    [Fact]
    public void DisplaySixBitplaneHamModeHoldsAndModifiesPreviousColor()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF184, 0x0120);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x0038);
        for (var plane = 0; plane < 6; plane++)
        {
            var pointer = (ushort)(0x1000 + (plane * 0x100));
            bus.WriteWord((uint)(0x00DFF0E0 + (plane * 4)), 0x0000);
            bus.WriteWord((uint)(0x00DFF0E2 + (plane * 4)), pointer);
        }

        BigEndian.WriteUInt16(bus.ChipRam, 0x1100, 0x8000);
        for (var plane = 0; plane <= 4; plane++)
        {
            BigEndian.WriteUInt16(bus.ChipRam, 0x1000 + (plane * 0x100), (ushort)(BigEndian.ReadUInt16(bus.ChipRam, 0x1000 + (plane * 0x100), "ham test") | 0x4000));
        }

        bus.WriteWord(0x00DFF100, 0x6800);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFF112200u, frame[0]);
        Assert.Equal(0xFF1122FFu, frame[1]);
    }

    [Fact]
    public void CopperBitplaneCountIncreaseStartsNewPlaneAtCurrentRasterRow()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF188, 0x0F00);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x0038);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x00E0);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2402, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2404, 0x00E2);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2406, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2408, 0x00E4);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240A, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240C, 0x00E6);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240E, 0x2000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2410, 0x00E8);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2412, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2414, 0x00EA);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2416, 0x3000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2418, 0x0100);
        BigEndian.WriteUInt16(bus.ChipRam, 0x241A, 0x2000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x241C, 0x2D01);
        BigEndian.WriteUInt16(bus.ChipRam, 0x241E, 0xFFFE);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2420, 0x0100);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2422, 0x3000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2424, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2426, 0xFFFE);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3000, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3002, 0x0000);
        bus.WriteWord(0x00DFF080, 0x0000);
        bus.WriteWord(0x00DFF082, 0x2400);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFF000000u, frame[0]);
        Assert.Equal(0xFFFF0000u, frame[AmigaConstants.PalLowResWidth]);
    }

    [Fact]
    public void DisplayAppliesCpuRegisterWritesAtTheirRasterRows()
    {
        var bus = new AmigaBus();
        var lineCycles = AmigaConstants.A500PalCpuClockHz / AmigaConstants.A500PalVBlankHz / AmigaConstants.A500PalRasterLines;
        var row0Cycle = (long)Math.Round(0x2C * lineCycles);
        var row1Cycle = (long)Math.Round(0x2D * lineCycles);
        var frameCycles = (long)Math.Round(AmigaConstants.A500PalCpuClockHz / AmigaConstants.A500PalVBlankHz);
        bus.WriteWord(0x00DFF180, 0x0000, 0);
        bus.WriteWord(0x00DFF182, 0x0F00, 0);
        bus.WriteWord(0x00DFF092, 0x0038, 0);
        bus.WriteWord(0x00DFF094, 0x0038, 0);
        bus.WriteWord(0x00DFF0E0, 0x0000, 0);
        bus.WriteWord(0x00DFF0E2, 0x1000, 0);
        bus.WriteWord(0x00DFF100, 0x1000, row0Cycle);
        bus.WriteWord(0x00DFF100, 0x0000, row1Cycle);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1002, 0x8000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame, 0, frameCycles);

        Assert.Equal(0xFFFF0000u, frame[0]);
        Assert.Equal(0xFF000000u, frame[AmigaConstants.PalLowResWidth]);
    }

    [Fact]
    public void ByteWritesReachDisplayRegisters()
    {
        var bus = new AmigaBus();
        bus.WriteByte(0x00DFF181, 0x0F, 0);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFF0000FFu, frame[0]);
    }

    [Fact]
    public void CopperMoveUpdatesCustomRegisterState()
    {
        var bus = new AmigaBus();
        BigEndian.WriteUInt16(bus.ChipRam, 0x2000, 0x0180);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2002, 0x00F0);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2004, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2006, 0xFFFE);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        new AmigaCopper().ExecuteList(bus, 0x2000);
        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFF00FF00u, frame[0]);
    }

    [Fact]
    public void DisplayExecutesCopperListFromCop1LcDuringRender()
    {
        var bus = new AmigaBus();
        BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x0180);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2402, 0x000F);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2404, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2406, 0xFFFE);
        bus.WriteWord(0x00DFF080, 0x0000);
        bus.WriteWord(0x00DFF082, 0x2400);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFF0000FFu, frame[0]);
    }

    [Fact]
    public void CopperWaitForEarlierRowDefersFollowingMovesUntilNextFrame()
    {
        var bus = new AmigaBus();
        BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x0180);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2402, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2404, 0x3601);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2406, 0xFF00);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2408, 0x2C01);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240A, 0xFF00);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240C, 0x0180);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240E, 0x0F00);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2410, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2412, 0xFFFE);
        bus.WriteWord(0x00DFF080, 0x0000);
        bus.WriteWord(0x00DFF082, 0x2400);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFF000000u, frame[20 * AmigaConstants.PalLowResWidth]);
    }

    [Fact]
    public void HardwareSpriteOverlaysPaletteColorsSixteenThroughThirtyOne()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF1A2, 0x000F);
        bus.WriteWord(0x00DFF140, 0x0A0A);
        bus.WriteWord(0x00DFF144, 0x8000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFF0000FFu, frame[(10 * AmigaConstants.PalLowResWidth) + 10]);
    }

    [Fact]
    public void BlitterCopiesSourceAToDestinationDWhenBltSizeIsWritten()
    {
        var bus = new AmigaBus();
        BigEndian.WriteUInt16(bus.ChipRam, 0x3000, 0x1234);

        bus.WriteWord(0x00DFF040, 0x09F0);
        bus.WriteWord(0x00DFF050, 0x0000);
        bus.WriteWord(0x00DFF052, 0x3000);
        bus.WriteWord(0x00DFF054, 0x0000);
        bus.WriteWord(0x00DFF056, 0x4000);
        bus.WriteWord(0x00DFF058, 0x0041);

        Assert.Equal(0x1234, BigEndian.ReadUInt16(bus.ChipRam, 0x4000, "blitter destination"));
        Assert.Contains(bus.BusAccesses, access => access.Request.Requester == AmigaBusRequester.Blitter);
    }

    [Fact]
    public void BlitterCombinesEnabledSourcesWithMinterm()
    {
        var bus = new AmigaBus();
        BigEndian.WriteUInt16(bus.ChipRam, 0x3000, 0xF0F0);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3200, 0x0FF0);

        bus.WriteWord(0x00DFF040, 0x0B5A);
        bus.WriteWord(0x00DFF048, 0x0000);
        bus.WriteWord(0x00DFF04A, 0x3200);
        bus.WriteWord(0x00DFF050, 0x0000);
        bus.WriteWord(0x00DFF052, 0x3000);
        bus.WriteWord(0x00DFF054, 0x0000);
        bus.WriteWord(0x00DFF056, 0x4000);
        bus.WriteWord(0x00DFF058, 0x0041);

        Assert.Equal(0xFF00, BigEndian.ReadUInt16(bus.ChipRam, 0x4000, "blitter destination"));
    }

    [Fact]
    public void BlitterIgnoresLowBitOfModulo()
    {
        var bus = new AmigaBus();
        BigEndian.WriteUInt16(bus.ChipRam, 0x3000, 0x1111);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3002, 0x2222);

        bus.WriteWord(0x00DFF040, 0x09F0);
        bus.WriteWord(0x00DFF050, 0x0000);
        bus.WriteWord(0x00DFF052, 0x3000);
        bus.WriteWord(0x00DFF054, 0x0000);
        bus.WriteWord(0x00DFF056, 0x4000);
        bus.WriteWord(0x00DFF064, 0x0001);
        bus.WriteWord(0x00DFF066, 0x0001);
        bus.WriteWord(0x00DFF058, 0x0081);

        Assert.Equal(0x1111, BigEndian.ReadUInt16(bus.ChipRam, 0x4000, "first blit row"));
        Assert.Equal(0x2222, BigEndian.ReadUInt16(bus.ChipRam, 0x4002, "second blit row"));
    }

    [Fact]
    public void BlitterIgnoresLowBitOfPointers()
    {
        var bus = new AmigaBus();
        BigEndian.WriteUInt16(bus.ChipRam, 0x3000, 0x1234);

        bus.WriteWord(0x00DFF040, 0x09F0);
        bus.WriteWord(0x00DFF050, 0x0000);
        bus.WriteWord(0x00DFF052, 0x3001);
        bus.WriteWord(0x00DFF054, 0x0000);
        bus.WriteWord(0x00DFF056, 0x4001);
        bus.WriteWord(0x00DFF058, 0x0041);

        Assert.Equal(0x1234, BigEndian.ReadUInt16(bus.ChipRam, 0x4000, "even destination"));
        Assert.Equal(0, bus.ChipRam[0x4002]);
    }

    [Fact]
    public void BlitterDoesNotAdvanceDisabledSourcePointers()
    {
        var bus = new AmigaBus();
        BigEndian.WriteUInt16(bus.ChipRam, 0x3000, 0x1234);

        bus.WriteWord(0x00DFF050, 0x0000);
        bus.WriteWord(0x00DFF052, 0x3000);
        bus.WriteWord(0x00DFF054, 0x0000);
        bus.WriteWord(0x00DFF056, 0x4000);
        bus.WriteWord(0x00DFF064, 0x0004);
        bus.WriteWord(0x00DFF040, 0x0100);
        bus.WriteWord(0x00DFF058, 0x0081);
        bus.WriteWord(0x00DFF054, 0x0000);
        bus.WriteWord(0x00DFF056, 0x4000);
        bus.WriteWord(0x00DFF040, 0x09F0);
        bus.WriteWord(0x00DFF058, 0x0041);

        Assert.Equal(0x1234, BigEndian.ReadUInt16(bus.ChipRam, 0x4000, "source A pointer after disabled blit"));
    }

    [Fact]
    public void BlitterShiftedDmaStartsWithZeroCarry()
    {
        var bus = new AmigaBus();
        BigEndian.WriteUInt16(bus.ChipRam, 0x3000, 0x000F);

        bus.WriteWord(0x00DFF074, 0xFFFF);
        bus.WriteWord(0x00DFF040, 0x49F0);
        bus.WriteWord(0x00DFF050, 0x0000);
        bus.WriteWord(0x00DFF052, 0x3000);
        bus.WriteWord(0x00DFF054, 0x0000);
        bus.WriteWord(0x00DFF056, 0x4000);
        bus.WriteWord(0x00DFF058, 0x0041);

        Assert.Equal(0x0000, BigEndian.ReadUInt16(bus.ChipRam, 0x4000, "first shifted DMA word"));
    }

    [Fact]
    public void BlitterShiftCarryContinuesAcrossRowsWithinBlit()
    {
        var bus = new AmigaBus();
        BigEndian.WriteUInt16(bus.ChipRam, 0x3000, 0x0001);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3002, 0x0000);

        bus.WriteWord(0x00DFF040, 0x19F0);
        bus.WriteWord(0x00DFF050, 0x0000);
        bus.WriteWord(0x00DFF052, 0x3000);
        bus.WriteWord(0x00DFF054, 0x0000);
        bus.WriteWord(0x00DFF056, 0x4000);
        bus.WriteWord(0x00DFF058, 0x0081);

        Assert.Equal(0x0000, BigEndian.ReadUInt16(bus.ChipRam, 0x4000, "first shifted row"));
        Assert.Equal(0x8000, BigEndian.ReadUInt16(bus.ChipRam, 0x4002, "second shifted row"));
    }

    [Fact]
    public void CiaBPortBControlsFloppyStepDirectionSideAndMotor()
    {
        var bus = new AmigaBus();
        var data = new byte[AmigaDiskImage.StandardAdfSize];
        bus.Disk.Drive0.Insert(AmigaDiskImage.FromAdfBytes(data));

        bus.WriteByte(0x00BFD100, 0xFF, 0);
        bus.WriteByte(0x00BFD100, 0x71, 0);
        bus.WriteByte(0x00BFD100, 0x70, 0);
        bus.WriteByte(0x00BFD100, 0x71, 0);
        bus.WriteByte(0x00BFD100, 0x70, 0);

        var snapshot = bus.Disk.CaptureSnapshot();
        Assert.Equal(2, snapshot.Cylinder);
        Assert.Equal(1, snapshot.Head);
        Assert.True(snapshot.Selected);
        Assert.True(snapshot.MotorOn);
    }

    [Fact]
    public void CiaAPortAReportsDiskChangeTrackZeroAndReady()
    {
        var bus = new AmigaBus();
        var data = new byte[AmigaDiskImage.StandardAdfSize];

        Assert.Equal(0, bus.ReadByte(0x00BFE001) & 0x04);

        bus.Disk.Drive0.Insert(AmigaDiskImage.FromAdfBytes(data));
        var inserted = bus.ReadByte(0x00BFE001);
        Assert.NotEqual(0, inserted & 0x04);
        Assert.Equal(0, inserted & 0x10);
        Assert.NotEqual(0, inserted & 0x20);

        bus.WriteByte(0x00BFD100, 0xFF, 0);
        bus.WriteByte(0x00BFD100, 0x77, 0);
        var ready = bus.ReadByte(0x00BFE001);
        Assert.Equal(0, ready & 0x20);

        bus.Disk.Drive0.Insert(AmigaDiskImage.FromAdfBytes(data), markChanged: true);
        Assert.Equal(0, bus.ReadByte(0x00BFE001) & 0x04);

        bus.WriteByte(0x00BFD100, 0x71, 0);
        bus.WriteByte(0x00BFD100, 0x70, 0);
        Assert.NotEqual(0, bus.ReadByte(0x00BFE001) & 0x04);
    }

    [Fact]
    public void CiaAPortAReportsGamePortFireBitsAsActiveLow()
    {
        var bus = new AmigaBus();

        Assert.NotEqual(0, bus.ReadByte(0x00BFE001) & 0x40);
        Assert.NotEqual(0, bus.ReadByte(0x00BFE001) & 0x80);

        bus.GamePort0FirePressed = true;
        bus.GamePort1FirePressed = true;

        Assert.Equal(0, bus.ReadByte(0x00BFE001) & 0x40);
        Assert.Equal(0, bus.ReadByte(0x00BFE001) & 0x80);
    }

    [Fact]
    public void EnabledDiskBlockInterruptDispatchesLevelOneAutovector()
    {
        var machine = new AmigaMachine(AmigaMachineOptions.ForProfile(AmigaMachineProfile.A500Pal512KBoot));
        machine.Bus.WriteLong(0x64, 0x0000_2000);
        machine.Cpu.Reset(0x1000, 0x3000);

        machine.Bus.WriteWord(0x00DFF09A, 0xC002);
        machine.Bus.WriteWord(0x00DFF09C, 0x8002);
        machine.Bus.Paula.AdvanceTo(0);

        Assert.True(machine.DispatchPendingHardwareInterrupt());
        Assert.Equal(0x0000_2000u, machine.Cpu.State.ProgramCounter);
        Assert.Equal(1, (machine.Cpu.State.StatusRegister >> 8) & 7);
    }

    [Fact]
    public void RasterAdvanceSetsVerticalBlankIntreqOnPalFrameCadence()
    {
        var bus = new AmigaBus();
        var firstFrameCycle = (long)Math.Round(AmigaConstants.A500PalCpuClockHz / AmigaConstants.A500PalVBlankHz);

        bus.AdvanceRasterTo(firstFrameCycle - 1);
        bus.Paula.AdvanceTo(firstFrameCycle - 1);
        Assert.Equal(0, bus.ReadWord(0x00DFF01E) & AmigaConstants.IntreqVerticalBlank);

        bus.AdvanceRasterTo(firstFrameCycle);
        bus.Paula.AdvanceTo(firstFrameCycle);
        Assert.NotEqual(0, bus.ReadWord(0x00DFF01E) & AmigaConstants.IntreqVerticalBlank);

        bus.WriteWord(0x00DFF09C, AmigaConstants.IntreqVerticalBlank, firstFrameCycle);
        bus.Paula.AdvanceTo(firstFrameCycle);
        Assert.Equal(0, bus.ReadWord(0x00DFF01E) & AmigaConstants.IntreqVerticalBlank);

        bus.AdvanceRasterTo(firstFrameCycle * 2);
        bus.Paula.AdvanceTo(firstFrameCycle * 2);
        Assert.NotEqual(0, bus.ReadWord(0x00DFF01E) & AmigaConstants.IntreqVerticalBlank);
    }

    [Fact]
    public void RasterAdvanceUpdatesBeamPositionLongword()
    {
        var bus = new AmigaBus();
        var cycleForLine300 = (long)Math.Ceiling(
            (AmigaConstants.A500PalCpuClockHz / AmigaConstants.A500PalVBlankHz / AmigaConstants.A500PalRasterLines) * 300);

        bus.AdvanceRasterTo(cycleForLine300);

        var beam = (bus.ReadLong(0x00DFF004) >> 8) & 0x01FF;
        Assert.Equal(300u, beam);
    }

    [Fact]
    public void RasterAdvanceAlternatesLongFrameBitOnPalFrameCadence()
    {
        var bus = new AmigaBus();
        var frameCycle = (long)Math.Round(AmigaConstants.A500PalCpuClockHz / AmigaConstants.A500PalVBlankHz);

        bus.AdvanceRasterTo(0);
        Assert.Equal(0, bus.ReadWord(0x00DFF004) & 0x8000);

        bus.AdvanceRasterTo(frameCycle);
        Assert.NotEqual(0, bus.ReadWord(0x00DFF004) & 0x8000);

        bus.AdvanceRasterTo(frameCycle * 2);
        Assert.Equal(0, bus.ReadWord(0x00DFF004) & 0x8000);
    }

    private static uint CalculateBootChecksum(byte[] bootBlock)
    {
        BigEndian.WriteUInt32(bootBlock, 4, 0);
        var sum = 0u;
        for (var offset = 0; offset < 1024; offset += 4)
        {
            var value = BigEndian.ReadUInt32(bootBlock, offset, "boot block checksum word");
            var previous = sum;
            sum += value;
            if (sum < previous)
            {
                sum++;
            }
        }

        return ~sum;
    }
}
