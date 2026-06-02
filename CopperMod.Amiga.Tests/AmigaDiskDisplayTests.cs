using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class AmigaDiskDisplayTests
{
    private const int StandardX = AmigaConstants.PalLowResOverscanBorderX;
    private const int StandardY = AmigaConstants.PalLowResOverscanBorderY;

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

        Assert.False(drive.Disk!.HasPreservedTrackData);
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
    public void EncodedTrackBackedImagesExposeDecodedStandardSectors()
    {
        var data = new byte[AmigaDiskImage.StandardAdfSize];
        var expectedOffset = (((7 * AmigaDiskImage.HeadCount) + 1) * AmigaDiskImage.SectorsPerTrack + 4) * AmigaDiskImage.SectorSize;
        data[expectedOffset] = 0x7A;
        data[expectedOffset + 511] = 0xC3;
        var sectorDisk = AmigaDiskImage.FromAdfBytes(data);
        var tracks = new byte[AmigaDiskImage.CylinderCount * AmigaDiskImage.HeadCount][];
        for (var cylinder = 0; cylinder < AmigaDiskImage.CylinderCount; cylinder++)
        {
            for (var head = 0; head < AmigaDiskImage.HeadCount; head++)
            {
                tracks[(cylinder * AmigaDiskImage.HeadCount) + head] = AmigaDosTrackEncoder.EncodeTrack(sectorDisk, cylinder, head);
            }
        }

        var decodedDisk = AmigaDiskImage.FromEncodedTracks(tracks);
        var sector = decodedDisk.ReadSector(7, 1, 4);

        Assert.True(decodedDisk.HasCompleteSectorData);
        Assert.True(decodedDisk.HasPreservedTrackData);
        Assert.Equal(0x7A, sector[0]);
        Assert.Equal(0xC3, sector[^1]);
    }

    [Fact]
    public void PartialTrackBackedSectorViewsAreNotTreatedAsCompleteAmigaDosDisks()
    {
        var data = new byte[AmigaDiskImage.StandardAdfSize];
        data[0] = (byte)'D';
        data[1] = (byte)'O';
        data[2] = (byte)'S';
        data[3] = 0;
        var sectorDisk = AmigaDiskImage.FromAdfBytes(data);
        var tracks = new byte[AmigaDiskImage.CylinderCount * AmigaDiskImage.HeadCount][];
        tracks[0] = AmigaDosTrackEncoder.EncodeTrack(sectorDisk, 0, 0);

        var decodedDisk = AmigaDiskImage.FromEncodedTracks(tracks);

        Assert.False(decodedDisk.HasCompleteSectorData);
        Assert.True(decodedDisk.HasPreservedTrackData);
        Assert.False(AmigaDosFileSystem.IsSupported(decodedDisk));
    }

    [Fact]
    public void EncodedTrackReadsAcrossByteBoundariesAndWrapsAtBitLength()
    {
        var track = new AmigaEncodedTrack(new byte[] { 0x12, 0x34, 0x50 }, 20);

        Assert.Equal(0x12, track.ReadByte(0));
        Assert.Equal(0x2345, track.ReadUInt16(4));
        Assert.Equal(0x5123, track.ReadUInt16(16));
        Assert.Equal(0x2345u, track.ReadUInt32(4) >> 16);
    }

    [Fact]
    public void EncodedTrackBackedImagesDecodeAmigaDosSectorsAtNonByteBitOffsets()
    {
        var data = new byte[AmigaDiskImage.StandardAdfSize];
        data[0] = (byte)'D';
        data[1] = (byte)'O';
        data[2] = (byte)'S';
        data[3] = 0;
        data[AmigaDiskImage.SectorSize] = 0x42;
        data[AmigaDiskImage.SectorSize + 511] = 0x99;
        var sectorDisk = AmigaDiskImage.FromAdfBytes(data);
        var tracks = CreateUnformattedEncodedTrackSet();
        tracks[0] = ShiftTrackBits(AmigaDosTrackEncoder.EncodeTrack(sectorDisk, 0, 0), 3);

        var decodedDisk = AmigaDiskImage.FromEncodedTracks(tracks);
        var sector = decodedDisk.ReadSector(0, 0, 1);

        Assert.False(decodedDisk.HasCompleteSectorData);
        Assert.True(decodedDisk.HasPreservedTrackData);
        Assert.Equal((byte)'D', decodedDisk.BootBlock[0]);
        Assert.Equal((byte)'O', decodedDisk.BootBlock[1]);
        Assert.Equal((byte)'S', decodedDisk.BootBlock[2]);
        Assert.Equal(0x42, sector[0]);
        Assert.Equal(0x99, sector[^1]);
    }

    [Fact]
    public void ShadowOfTheBeastIpfZipLoadsManagedEncodedTracksWhenPresent()
    {
        var path = TryFindWorkspaceFile(
            "CopperScreen",
            "TestImages",
            "Shadow of the Beast (1989)(Psygnosis)(US)(Disk 1 of 2).zip");
        if (path == null)
        {
            return;
        }

        var disk = AmigaDiskImage.Load(path);

        var track = disk.ReadEncodedTrack(0, 0);
        Assert.True(disk.HasPreservedTrackData);
        Assert.True(track.BitLength > 0);
        Assert.True(ContainsWord(track, 0x4489));
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

        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY));
        Assert.Equal(0xFF000000u, Pixel(frame, StandardX + 1, StandardY));
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

        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY));
        Assert.Equal(0xFF000000u, Pixel(frame, StandardX + 16, StandardY));
        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX + 1, StandardY + 1));
    }

    [Fact]
    public void DisplayUsesHighResolutionDataFetchWidthForBitplaneLineStride()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF182, 0x0F00);
        bus.WriteWord(0x00DFF092, 0x003C);
        bus.WriteWord(0x00DFF094, 0x00D4);
        bus.WriteWord(0x00DFF108, 0x0050);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x1000);
        bus.WriteWord(0x00DFF100, 0x9000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0xC000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1078, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x10A0, 0xC000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY));
        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY + 1));
    }

    [Fact]
    public void DisplayHighResolutionStandardDdfStopUsesFullWindowStride()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF182, 0x0F00);
        bus.WriteWord(0x00DFF092, 0x003C);
        bus.WriteWord(0x00DFF094, 0x00D0);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x1000);
        bus.WriteWord(0x00DFF100, 0x9000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0xC000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1050, 0xC000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY));
        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY + 1));
    }

    [Fact]
    public void DisplayHighResolutionLowResolutionOutputUsesRealSubpixelColors()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x005A);
        bus.WriteWord(0x00DFF182, 0x0FFF);
        bus.WriteWord(0x00DFF092, 0x003C);
        bus.WriteWord(0x00DFF094, 0x00D0);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x1000);
        bus.WriteWord(0x00DFF100, 0x9000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1050, 0x4000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFFFFFFFFu, Pixel(frame, StandardX, StandardY));
        Assert.Equal(0xFFFFFFFFu, Pixel(frame, StandardX, StandardY + 1));
    }

    [Fact]
    public void DisplayHighResolutionOutputKeepsSeparateSubpixels()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF182, 0x0F00);
        bus.WriteWord(0x00DFF092, 0x003C);
        bus.WriteWord(0x00DFF094, 0x00D0);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x1000);
        bus.WriteWord(0x00DFF100, 0x9000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1050, 0x4000);
        var frame = new uint[bus.Display.Width * bus.Display.Height];

        bus.Display.RenderFrame(frame);

        var firstLine = StandardY * 2;
        var secondLine = (StandardY + 1) * 2;
        Assert.Equal(0xFFFF0000u, HighResPixel(frame, bus.Display.Width, StandardX * 2, firstLine));
        Assert.Equal(0xFF000000u, HighResPixel(frame, bus.Display.Width, (StandardX * 2) + 1, firstLine));
        Assert.Equal(0xFFFF0000u, HighResPixel(frame, bus.Display.Width, StandardX * 2, firstLine + 1));
        Assert.Equal(0xFF000000u, HighResPixel(frame, bus.Display.Width, (StandardX * 2) + 1, firstLine + 1));
        Assert.Equal(0xFF000000u, HighResPixel(frame, bus.Display.Width, StandardX * 2, secondLine));
        Assert.Equal(0xFFFF0000u, HighResPixel(frame, bus.Display.Width, (StandardX * 2) + 1, secondLine));
        Assert.Equal(0xFF000000u, HighResPixel(frame, bus.Display.Width, StandardX * 2, secondLine + 1));
        Assert.Equal(0xFFFF0000u, HighResPixel(frame, bus.Display.Width, (StandardX * 2) + 1, secondLine + 1));
    }

    [Fact]
    public void DisplayInterlaceFullHeightOutputAlternatesFields()
    {
        var bus = new AmigaBus();
        var frameCycle = AmigaConstants.A500PalCpuCyclesPerFrame;
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF182, 0x0F00);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x1000);
        bus.WriteWord(0x00DFF100, 0x1004);
        bus.WriteWord(0x00DFF096, 0x8300);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x8000);
        var frame = new uint[bus.Display.Width * bus.Display.Height];
        Array.Fill(frame, 0xFF000000u);

        bus.Display.RenderFrame(frame, 0, frameCycle);

        Assert.Equal(0xFFFF0000u, HighResPixel(frame, bus.Display.Width, StandardX * 2, StandardY * 2));
        Assert.Equal(0xFF000000u, HighResPixel(frame, bus.Display.Width, StandardX * 2, (StandardY * 2) + 1));

        bus.Display.RenderFrame(frame, frameCycle, frameCycle * 2);

        Assert.Equal(0xFFFF0000u, HighResPixel(frame, bus.Display.Width, StandardX * 2, StandardY * 2));
        Assert.Equal(0xFFFF0000u, HighResPixel(frame, bus.Display.Width, StandardX * 2, (StandardY * 2) + 1));
    }

    [Fact]
    public void DisplayContinuesBitplaneFetchesPastAdjacentPlanePointers()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF182, 0x0F00);
        bus.WriteWord(0x00DFF184, 0x00F0);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x00D0);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x1000);
        bus.WriteWord(0x00DFF0E4, 0x0000);
        bus.WriteWord(0x00DFF0E6, 0x1050);
        bus.WriteWord(0x00DFF100, 0x2000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1078, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x10A0, 0x8000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY));
        Assert.Equal(0xFF00FF00u, Pixel(frame, StandardX, StandardY + 1));
        Assert.Equal(0xFF00FF00u, Pixel(frame, StandardX, StandardY + 2));
    }

    [Fact]
    public void TimedDisplayReadsBitplaneMemoryAtRasterRowCycle()
    {
        var bus = new AmigaBus();
        var lineCycles = AmigaConstants.A500PalCpuCyclesPerRasterLine;
        var writeAfterFirstVisibleRow = CycleForOutputRow(StandardY + 1, lineCycles);
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF182, 0x0F00);
        bus.WriteWord(0x00DFF096, 0x8300);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x0038);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x1000);
        bus.WriteWord(0x00DFF100, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x8000);
        bus.WriteWord(0x00001000, 0x0000, writeAfterFirstVisibleRow);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame, 0, writeAfterFirstVisibleRow + 1);

        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY));
    }

    [Fact]
    public void TimedDisplayReadsBitplaneMemoryAtDataFetchSlotWithinRow()
    {
        var lineCycles = AmigaConstants.A500PalCpuCyclesPerRasterLine;
        var fetchCycle = CycleForOutputRowHorizontal(StandardY, 0x38, lineCycles);
        var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;

        var beforeFetchBus = CreateOneBitplaneFetchRaceBus();
        beforeFetchBus.WriteChipWordForDeviceWithResult(
            AmigaBusRequester.Blitter,
            AmigaBusAccessKind.Blitter,
            0x1000,
            0x8000,
            fetchCycle - AgnusChipSlotScheduler.SlotCycles);
        var beforeFetchFrame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
        beforeFetchBus.Display.RenderFrame(beforeFetchFrame, 0, frameCycles);
        Assert.Equal(0xFFFF0000u, Pixel(beforeFetchFrame, StandardX, StandardY));

        var afterFetchBus = CreateOneBitplaneFetchRaceBus();
        afterFetchBus.WriteChipWordForDeviceWithResult(
            AmigaBusRequester.Blitter,
            AmigaBusAccessKind.Blitter,
            0x1000,
            0x8000,
            fetchCycle + AgnusChipSlotScheduler.SlotCycles);
        var afterFetchFrame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
        afterFetchBus.Display.RenderFrame(afterFetchFrame, 0, frameCycles);
        Assert.Equal(0xFF000000u, Pixel(afterFetchFrame, StandardX, StandardY));

        static AmigaBus CreateOneBitplaneFetchRaceBus()
        {
            var bus = new AmigaBus();
            bus.WriteWord(0x00DFF180, 0x0000);
            bus.WriteWord(0x00DFF182, 0x0F00);
            bus.WriteWord(0x00DFF096, 0x8300);
            bus.WriteWord(0x00DFF092, 0x0038);
            bus.WriteWord(0x00DFF094, 0x0038);
            bus.WriteWord(0x00DFF0E0, 0x0000);
            bus.WriteWord(0x00DFF0E2, 0x1000);
            bus.WriteWord(0x00DFF100, 0x1000);
            BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x0000);
            return bus;
        }
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
        bus.WriteWord(0x00DFF094, 0x0040);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x1000);
        bus.WriteWord(0x00DFF100, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1002, 0x8000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFF000000u, Pixel(frame, StandardX + 15, StandardY));
        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX + 16, StandardY));
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

        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX + 319, StandardY));
    }

    [Fact]
    public void DisplayFramesIncludePalLowResOverscanAroundStandardWindow()
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

        Assert.Equal(AmigaConstants.PalHighResWidth, bus.Display.Width);
        Assert.Equal(AmigaConstants.PalHighResHeight, bus.Display.Height);
        Assert.Equal(0xFF000000u, Pixel(frame, StandardX - 1, StandardY));
        Assert.Equal(0xFF000000u, Pixel(frame, StandardX, StandardY - 1));
        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY));
    }

    [Fact]
    public void DisplayCanRenderLeftAndTopOverscan()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF182, 0x0F00);
        bus.WriteWord(0x00DFF08E, 0x1C71);
        bus.WriteWord(0x00DFF090, 0x2C91);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x0038);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x1000);
        bus.WriteWord(0x00DFF100, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x8000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFFFF0000u, Pixel(frame, 0, 0));
    }

    [Fact]
    public void CopperBitplanePointerLoadedBeforeViewportSkipsHiddenTopRows()
    {
        var bus = new AmigaBus();
        BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x008E);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2402, 0x1081);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2404, 0x0090);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2406, 0x2CC1);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2408, 0x0180);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240A, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240C, 0x0182);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240E, 0x0F00);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2410, 0x00E0);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2412, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2414, 0x00E2);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2416, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2418, 0x0100);
        BigEndian.WriteUInt16(bus.ChipRam, 0x241A, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x241C, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x241E, 0xFFFE);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000 + (12 * 40), 0x8000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
        bus.WriteWord(0x00DFF080, 0x0000);
        bus.WriteWord(0x00DFF082, 0x2400);

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, 0));
    }

    [Fact]
    public void DisplayHonorsInterlacedFieldBitplanePointer()
    {
        var bus = new AmigaBus();
        var frameCycle = AmigaConstants.A500PalCpuCyclesPerFrame;
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF182, 0x0F00);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x00D0);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x1028);
        bus.WriteWord(0x00DFF100, 0x1004);
        bus.WriteWord(0x00DFF096, 0x8300);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1028, 0x8000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame, 0, frameCycle);

        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY));
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

        Assert.Equal(0xFF000000u, Pixel(frame, StandardX, StandardY));
        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY + 1));
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

        Assert.Equal(0xFF000000u, Pixel(frame, StandardX, StandardY));
        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY + 103));
    }

    [Fact]
    public void DisplayWindowRebasesBitplaneRowsWhenDiwStartMovesAfterPointerSetup()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF182, 0x0F00);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x00D0);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x1000);
        bus.WriteWord(0x00DFF100, 0x1000);
        bus.WriteWord(0x00DFF08E, 0x9381);
        bus.WriteWord(0x00DFF090, 0xC3C1);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x8000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFF000000u, Pixel(frame, StandardX, StandardY));
        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY + 103));
    }

    [Fact]
    public void DisplayTreatsEqualVerticalStopAsWrappedIntoLaterField()
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
        for (var row = 0; row < 256; row++)
        {
            BigEndian.WriteUInt16(bus.ChipRam, 0x1000 + (row * 40), 0x8000);
        }
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY + 255));
        Assert.Equal(0xFF000000u, Pixel(frame, StandardX, StandardY + 256));
    }

    [Fact]
    public void DisplayTreatsLowVerticalStopAsWrappedIntoLaterField()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF182, 0x0F00);
        bus.WriteWord(0x00DFF08E, 0x0581);
        bus.WriteWord(0x00DFF090, 0x40C1);
        bus.WriteWord(0x00DFF092, 0x003C);
        bus.WriteWord(0x00DFF094, 0x00D0);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x1000);
        bus.WriteWord(0x00DFF100, 0x1000);
        for (var row = 0; row < 320; row++)
        {
            BigEndian.WriteUInt16(bus.ChipRam, 0x1000 + (row * 40), 0x8000);
        }
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, 35));
        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, AmigaConstants.PalLowResHeight - 8));
    }

    [Fact]
    public void LiveDisplayWindowDoesNotRetroactivelyOpenWrappedWindowWhenStartIsWrittenLate()
    {
        var bus = new AmigaBus(agnusTimingMode: AgnusTimingMode.SlotEngine);
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF182, 0x0F00);
        bus.WriteWord(0x00DFF08E, 0xF081);
        bus.WriteWord(0x00DFF090, 0xF1C1);
        bus.WriteWord(0x00DFF092, 0x003C);
        bus.WriteWord(0x00DFF094, 0x00D0);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x1000);
        bus.WriteWord(0x00DFF100, 0x1000);
        bus.WriteWord(0x00DFF096, 0x8300);
        for (var row = 0; row < 320; row++)
        {
            BigEndian.WriteUInt16(bus.ChipRam, 0x1000 + (row * 40), 0x8000);
        }

        var lateCycle = AmigaConstants.A500PalCpuCyclesPerRasterLine * 24L;
        bus.WriteWord(0x00DFF08E, 0x0581, lateCycle);
        bus.WriteWord(0x00DFF090, 0x40C1, lateCycle + 4);
        var frameCycle = AmigaConstants.A500PalCpuCyclesPerFrame;
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.AdvanceDmaTo(frameCycle);
        bus.Display.RenderFrame(frame, 0, frameCycle);

        Assert.Equal(0xFF000000u, Pixel(frame, StandardX, StandardY + 240));
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

        Assert.Equal(0xFFFF0000u, Pixel(frame, 0, 36));
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

        Assert.Equal(0xFF00FF00u, Pixel(frame, StandardX, StandardY));
        Assert.Equal(0xFFFFFF00u, Pixel(frame, StandardX + 1, StandardY));
    }

    [Fact]
    public void DisplayFineScrollUsesDataFetchedBeforeRightShiftedWindow()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF182, 0x0F00);
        bus.WriteWord(0x00DFF08E, 0x2C90);
        bus.WriteWord(0x00DFF090, 0xF4B0);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x00D0);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x1000);
        bus.WriteWord(0x00DFF102, 0x000E);
        bus.WriteWord(0x00DFF100, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x4000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX + 15, StandardY));
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

        Assert.Equal(0xFF777777u, Pixel(frame, StandardX, StandardY));
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

        Assert.Equal(0xFF112200u, Pixel(frame, StandardX, StandardY));
        Assert.Equal(0xFF1122FFu, Pixel(frame, StandardX + 1, StandardY));
    }

    [Fact]
    public void DisplayDualPlayfieldUsesSeparatedColorGroupsAndPriority()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF182, 0x0F00);
        bus.WriteWord(0x00DFF192, 0x00F0);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x0038);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x1000);
        bus.WriteWord(0x00DFF0E4, 0x0000);
        bus.WriteWord(0x00DFF0E6, 0x1100);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0xC000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1100, 0x8000);
        bus.WriteWord(0x00DFF100, 0x2400);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY));
        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX + 1, StandardY));

        bus.WriteWord(0x00DFF104, 0x0040);
        Array.Clear(frame);
        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFF00FF00u, Pixel(frame, StandardX, StandardY));
        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX + 1, StandardY));
    }

    [Fact]
    public void CopperCanSwitchFromDualPlayfieldToNormalStatusBand()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1100, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1200, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x0182);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2402, 0x0F00);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2404, 0x0192);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2406, 0x00F0);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2408, 0x0092);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240A, 0x0038);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240C, 0x0094);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240E, 0x0038);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2410, 0x00E0);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2412, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2414, 0x00E2);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2416, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2418, 0x00E4);
        BigEndian.WriteUInt16(bus.ChipRam, 0x241A, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x241C, 0x00E6);
        BigEndian.WriteUInt16(bus.ChipRam, 0x241E, 0x1100);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2420, 0x0100);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2422, 0x2400);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2424, 0x2D01);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2426, 0xFFFE);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2428, 0x0182);
        BigEndian.WriteUInt16(bus.ChipRam, 0x242A, 0x000F);
        BigEndian.WriteUInt16(bus.ChipRam, 0x242C, 0x00E0);
        BigEndian.WriteUInt16(bus.ChipRam, 0x242E, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2430, 0x00E2);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2432, 0x1200);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2434, 0x0100);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2436, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2438, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x243A, 0xFFFE);
        bus.WriteWord(0x00DFF080, 0x0000);
        bus.WriteWord(0x00DFF082, 0x2400);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY));
        Assert.Equal(0xFF0000FFu, Pixel(frame, StandardX, StandardY + 1));
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

        Assert.Equal(0xFF000000u, Pixel(frame, StandardX, StandardY));
        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY + 1));
    }

    [Fact]
    public void CopperPointerWritesWhileBitplanesDisabledSetBaseForLaterEnable()
    {
        var bus = new AmigaBus();
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1100, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x0180);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2402, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2404, 0x0182);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2406, 0x0F00);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2408, 0x0184);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240A, 0x00F0);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240C, 0x0186);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240E, 0x0FF0);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2410, 0x00E0);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2412, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2414, 0x00E2);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2416, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2418, 0x00E4);
        BigEndian.WriteUInt16(bus.ChipRam, 0x241A, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x241C, 0x00E6);
        BigEndian.WriteUInt16(bus.ChipRam, 0x241E, 0x1100);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2420, 0x0100);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2422, 0x2000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2424, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2426, 0xFFFE);
        bus.WriteWord(0x00DFF080, 0x0000);
        bus.WriteWord(0x00DFF082, 0x2400);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFFFFFF00u, Pixel(frame, StandardX, StandardY));
    }

    [Fact]
    public void CopperModuloChangeAppliesFromCurrentRasterRowForward()
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
        BigEndian.WriteUInt16(bus.ChipRam, 0x1002, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1004, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1006, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x2D01);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2402, 0xFFFE);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2404, 0x0108);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2406, 0x0002);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2408, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240A, 0xFFFE);
        bus.WriteWord(0x00DFF080, 0x0000);
        bus.WriteWord(0x00DFF082, 0x2400);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY));
        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY + 1));
        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY + 2));
    }

    [Fact]
    public void DisplayAppliesCpuRegisterWritesAtTheirRasterRows()
    {
        var bus = CreateLegacyDiskDisplayBus();
        var lineCycles = AmigaConstants.A500PalCpuCyclesPerRasterLine;
        var row0Cycle = 0x2C * lineCycles;
        var row1Cycle = 0x2D * lineCycles;
        var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
        bus.WriteWord(0x00DFF180, 0x0000, 0);
        bus.WriteWord(0x00DFF182, 0x0F00, 0);
        bus.WriteWord(0x00DFF092, 0x0038, 0);
        bus.WriteWord(0x00DFF094, 0x0038, 0);
        bus.WriteWord(0x00DFF0E0, 0x0000, 0);
        bus.WriteWord(0x00DFF0E2, 0x1000, 0);
        bus.WriteWord(0x00DFF096, 0x8300, 0);
        bus.WriteWord(0x00DFF100, 0x1000, row0Cycle);
        bus.WriteWord(0x00DFF100, 0x0000, row1Cycle);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1002, 0x8000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame, 0, frameCycles);

        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY));
        Assert.Equal(0xFF000000u, Pixel(frame, StandardX, StandardY + 1));
    }

    [Fact]
    public void TimedDisplayRequiresMasterAndBitplaneDmaForBitplaneFetch()
    {
        var bus = new AmigaBus();
        var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF182, 0x0F00);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x0038);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x1000);
        bus.WriteWord(0x00DFF100, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x8000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame, 0, frameCycles);

        Assert.Equal(0xFF000000u, Pixel(frame, StandardX, StandardY));

        Array.Clear(frame);
        bus.WriteWord(0x00DFF096, 0x8300, frameCycles);
        bus.Display.RenderFrame(frame, frameCycles, frameCycles * 2);

        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY));
    }

    [Fact]
    public void BitplaneDmaEnableStartsActivePointersAtEnableRasterRow()
    {
        var bus = new AmigaBus();
        var lineCycles = AmigaConstants.A500PalCpuCyclesPerRasterLine;
        var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
        var pointerWriteRow = StandardY + 5;
        var enableRow = StandardY + 10;
        var pointerWriteCycle = CycleForOutputRow(pointerWriteRow, lineCycles);
        var enableCycle = CycleForOutputRow(enableRow, lineCycles);
        bus.WriteWord(0x00DFF180, 0x0000, 0);
        bus.WriteWord(0x00DFF182, 0x0F00, 0);
        bus.WriteWord(0x00DFF092, 0x0038, 0);
        bus.WriteWord(0x00DFF094, 0x0038, 0);
        bus.WriteWord(0x00DFF100, 0x1000, 0);
        bus.WriteWord(0x00DFF096, 0x8200, 0);
        bus.WriteWord(0x00DFF0E0, 0x0000, pointerWriteCycle);
        bus.WriteWord(0x00DFF0E2, 0x2000, pointerWriteCycle);
        bus.WriteWord(0x00DFF096, 0x8100, enableCycle);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2000, 0x8000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame, 0, frameCycles);

        Assert.Equal(0xFF000000u, Pixel(frame, StandardX, enableRow - 1));
        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, enableRow));
    }

    [Fact]
    public void TimedDisplayRequiresMasterAndCopperDmaForCopperMoves()
    {
        var bus = new AmigaBus();
        var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
        BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x0180);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2402, 0x0F00);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2404, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2406, 0xFFFE);
        bus.WriteWord(0x00DFF080, 0x0000);
        bus.WriteWord(0x00DFF082, 0x2400);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame, 0, frameCycles);

        Assert.Equal(0xFF000000u, Pixel(frame, 0, 0));

        Array.Clear(frame);
        bus.WriteWord(0x00DFF096, 0x8280, frameCycles);
        bus.Display.RenderFrame(frame, frameCycles, frameCycles * 2);

        Assert.Equal(0xFFFF0000u, Pixel(frame, 0, 0));
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
    public void CopperHorizontalWaitBehindBeamContinuesBecauseComparisonIsGreaterOrEqual()
    {
        var bus = new AmigaBus();
        BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x0001);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2402, 0x00FE);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2404, 0x0180);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2406, 0x0F00);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2408, 0x0001);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240A, 0x00FE);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240C, 0x0180);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240E, 0x00F0);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2410, 0x0001);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2412, 0x00FE);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2414, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2416, 0xFFFE);
        bus.WriteWord(0x00DFF080, 0x0000);
        bus.WriteWord(0x00DFF082, 0x2400);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFF00FF00u, Pixel(frame, 0, 0));
    }

    [Fact]
    public void CopperMoveAffectsDisplayAtSecondInstructionBusCycle()
    {
        var bus = new AmigaBus();
        var waitV = 0x2C - StandardY;
        BigEndian.WriteUInt16(bus.ChipRam, 0x2400, (ushort)((waitV << 8) | 0x0041));
        BigEndian.WriteUInt16(bus.ChipRam, 0x2402, 0xFFFE);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2404, 0x0180);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2406, 0x00F0);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2408, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240A, 0xFFFE);
        bus.WriteWord(0x00DFF180, 0x0F00);
        bus.WriteWord(0x00DFF080, 0x0000);
        bus.WriteWord(0x00DFF082, 0x2400);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFFFF0000u, Pixel(frame, 28, 0));
        Assert.Equal(0xFFFF0000u, Pixel(frame, 31, 0));
        Assert.Equal(0xFF00FF00u, Pixel(frame, 32, 0));
    }

    [Fact]
    public void CopperMoveReadsDataWordAtSecondInstructionBusCycle()
    {
        var bus = CreateLegacyDiskDisplayBus();
        var lineCycles = AmigaConstants.A500PalCpuCyclesPerRasterLine;
        var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
        var waitV = 0x2C - StandardY;
        var moveIr1Cycle = CycleForOutputRowHorizontal(0, 0x46, lineCycles);
        var rewriteCycle = moveIr1Cycle + CopperHpToCpuCyclesForTest(1);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2400, (ushort)((waitV << 8) | 0x0041));
        BigEndian.WriteUInt16(bus.ChipRam, 0x2402, 0xFFFE);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2404, 0x0180);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2406, 0x0F00);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2408, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240A, 0xFFFE);
        bus.WriteWord(0x00DFF080, 0x0000);
        bus.WriteWord(0x00DFF082, 0x2400);
        bus.WriteWord(0x00DFF096, 0x8280);
        bus.WriteWord(0x00002406, 0x00F0, rewriteCycle);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame, 0, frameCycles);

        Assert.Equal(0xFF00FF00u, Pixel(frame, 32, 0));
    }

    [Fact]
    public void CopperEndOfLineHorizontalWaitRendersWholeOutputRow()
    {
        var bus = new AmigaBus();
        BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x0001);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2402, 0x00FE);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2404, 0x0180);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2406, 0x0F00);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2408, (ushort)(((0x2C - StandardY) << 8) | 0x00E3));
        BigEndian.WriteUInt16(bus.ChipRam, 0x240A, 0xFFFE);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240C, 0x0180);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240E, 0x00F0);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2410, 0x0001);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2412, 0x00FE);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2414, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2416, 0xFFFE);
        bus.WriteWord(0x00DFF080, 0x0000);
        bus.WriteWord(0x00DFF082, 0x2400);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFFFF0000u, Pixel(frame, 0, 0));
        Assert.Equal(0xFFFF0000u, Pixel(frame, AmigaConstants.PalLowResWidth - 1, 0));
        Assert.Equal(0xFF00FF00u, Pixel(frame, 0, 1));
    }

    [Fact]
    public void CopperWaitMaskFindsNextMatchingBeamPosition()
    {
        var bus = new AmigaBus();
        BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x0180);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2402, 0x0F00);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2404, 0x2001);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2406, 0xFF00);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2408, 0x0F01);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240A, 0x8F00);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240C, 0x0180);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240E, 0x00F0);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2410, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2412, 0xFFFE);
        bus.WriteWord(0x00DFF080, 0x0000);
        bus.WriteWord(0x00DFF082, 0x2400);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        var triggerY = 0x2F - (0x2C - StandardY);
        Assert.Equal(0xFFFF0000u, Pixel(frame, 0, triggerY - 1));
        Assert.Equal(0xFF00FF00u, Pixel(frame, 0, triggerY));
    }

    [Fact]
    public void CopperSkipSkipsFollowingMoveWhenBeamAlreadyReachedPosition()
    {
        var bus = new AmigaBus();
        BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x0001);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2402, 0x00FF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2404, 0x0180);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2406, 0x0F00);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2408, 0x0180);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240A, 0x000F);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240C, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240E, 0xFFFE);
        bus.WriteWord(0x00DFF080, 0x0000);
        bus.WriteWord(0x00DFF082, 0x2400);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFF0000FFu, Pixel(frame, 0, 0));
    }

    [Fact]
    public void CopperJumpTwoContinuesRenderingFromSecondCopperList()
    {
        var bus = new AmigaBus();
        BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x0084);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2402, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2404, 0x0086);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2406, 0x2600);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2408, 0x008A);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240A, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240C, 0x0180);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240E, 0x0F00);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2410, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2412, 0xFFFE);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2600, 0x0180);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2602, 0x00F0);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2604, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2606, 0xFFFE);
        bus.WriteWord(0x00DFF080, 0x0000);
        bus.WriteWord(0x00DFF082, 0x2400);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFF00FF00u, Pixel(frame, 0, 0));
    }

    [Fact]
    public void CopperLocationHighWordIgnoresUnusedDmaAddressBits()
    {
        var bus = new AmigaBus();
        BigEndian.WriteUInt16(bus.ChipRam, 0x0420, 0x0180);
        BigEndian.WriteUInt16(bus.ChipRam, 0x0422, 0x0F00);
        BigEndian.WriteUInt16(bus.ChipRam, 0x0424, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x0426, 0xFFFE);
        bus.WriteWord(0x00DFF080, 0x0140);
        bus.WriteWord(0x00DFF082, 0x0420);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFFFF0000u, Pixel(frame, 0, 0));
    }

    [Fact]
    public void CpuCop2LcWriteBeforeSameFrameCopJmp2ControlsJump()
    {
        var bus = CreateLegacyDiskDisplayBus();
        var lineCycles = AmigaConstants.A500PalCpuCyclesPerRasterLine;
        var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
        var writeDuringBlankCycle = lineCycles * 4L;
        BigEndian.WriteUInt16(bus.ChipRam, 0x2400, (ushort)(((0x2C - StandardY) << 8) | 0x0001));
        BigEndian.WriteUInt16(bus.ChipRam, 0x2402, 0xFFFE);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2404, 0x008A);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2406, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2408, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240A, 0xFFFE);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2600, 0x0180);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2602, 0x0F00);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2604, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2606, 0xFFFE);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2800, 0x0180);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2802, 0x00F0);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2804, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2806, 0xFFFE);
        bus.WriteWord(0x00DFF080, 0x0000);
        bus.WriteWord(0x00DFF082, 0x2400);
        bus.WriteWord(0x00DFF084, 0x0000);
        bus.WriteWord(0x00DFF086, 0x2600);
        bus.WriteWord(0x00DFF096, 0x8280);
        bus.WriteWord(0x00DFF086, 0x2800, writeDuringBlankCycle);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame, 0, frameCycles);

        Assert.Equal(0xFF00FF00u, Pixel(frame, 0, 0));
    }

    [Fact]
    public void CpuCop2LcWriteAfterSameFrameCopJmp2DoesNotRetroactivelyChangeJump()
    {
        var bus = CreateLegacyDiskDisplayBus();
        var lineCycles = AmigaConstants.A500PalCpuCyclesPerRasterLine;
        var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
        var writeAfterJumpCycle = CycleForOutputRow(StandardY + 2, lineCycles);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2400, (ushort)(((0x2C - StandardY) << 8) | 0x0001));
        BigEndian.WriteUInt16(bus.ChipRam, 0x2402, 0xFFFE);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2404, 0x008A);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2406, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2408, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240A, 0xFFFE);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2600, 0x0180);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2602, 0x0F00);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2604, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2606, 0xFFFE);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2800, 0x0180);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2802, 0x00F0);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2804, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2806, 0xFFFE);
        bus.WriteWord(0x00DFF080, 0x0000);
        bus.WriteWord(0x00DFF082, 0x2400);
        bus.WriteWord(0x00DFF084, 0x0000);
        bus.WriteWord(0x00DFF086, 0x2600);
        bus.WriteWord(0x00DFF096, 0x8280);
        bus.WriteWord(0x00DFF086, 0x2800, writeAfterJumpCycle);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame, 0, frameCycles);

        Assert.Equal(0xFFFF0000u, Pixel(frame, 0, 0));
    }

    [Fact]
    public void CopperDmaEnableMidFrameResumesCopperAtEnableCycle()
    {
        var bus = CreateLegacyDiskDisplayBus();
        var lineCycles = AmigaConstants.A500PalCpuCyclesPerRasterLine;
        var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
        var enableCycle = CycleForOutputRow(5, lineCycles);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x0180);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2402, 0x0F00);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2404, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2406, 0xFFFE);
        bus.WriteWord(0x00DFF080, 0x0000);
        bus.WriteWord(0x00DFF082, 0x2400);
        bus.WriteWord(0x00DFF096, 0x8200);
        bus.WriteWord(0x00DFF096, 0x8080, enableCycle);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame, 0, frameCycles);

        Assert.Equal(0xFF000000u, Pixel(frame, 0, 4));
        Assert.Equal(0xFFFF0000u, Pixel(frame, 0, 5));
    }

    [Fact]
    public void CopperInstructionFetchUsesChipRamContentsAtFetchCycle()
    {
        var bus = CreateLegacyDiskDisplayBus();
        var lineCycles = AmigaConstants.A500PalCpuCyclesPerRasterLine;
        var frameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
        var rewriteCycle = CycleForOutputRow(2, lineCycles);
        var waitV = 0x2C - StandardY + 5;
        BigEndian.WriteUInt16(bus.ChipRam, 0x2400, (ushort)((waitV << 8) | 0x0001));
        BigEndian.WriteUInt16(bus.ChipRam, 0x2402, 0xFFFE);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2404, 0x0180);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2406, 0x0F00);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2408, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240A, 0xFFFE);
        bus.WriteWord(0x00DFF080, 0x0000);
        bus.WriteWord(0x00DFF082, 0x2400);
        bus.WriteWord(0x00DFF096, 0x8280);
        bus.WriteWord(0x00002406, 0x00F0, rewriteCycle);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame, 0, frameCycles);

        Assert.Equal(0xFF000000u, Pixel(frame, 0, 4));
        Assert.Equal(0xFF00FF00u, Pixel(frame, 0, 5));
    }

    [Fact]
    public void CopperMoveStreamAdvancesAcrossRasterRows()
    {
        var bus = new AmigaBus();
        var waitV = 0x2C - StandardY;
        BigEndian.WriteUInt16(bus.ChipRam, 0x2400, (ushort)((waitV << 8) | 0x00E3));
        BigEndian.WriteUInt16(bus.ChipRam, 0x2402, 0xFFFE);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2404, 0x0180);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2406, 0x0F00);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2408, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240A, 0xFFFE);
        bus.WriteWord(0x00DFF080, 0x0000);
        bus.WriteWord(0x00DFF082, 0x2400);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFF000000u, Pixel(frame, 0, 0));
        Assert.Equal(0xFFFF0000u, Pixel(frame, 0, 1));
    }

    [Fact]
    public void DisplayCopperMoveToIntreqReachesPaulaInterruptState()
    {
        var bus = new AmigaBus();
        BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x009C);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2402, (ushort)(0x8000 | AmigaConstants.IntreqCopper));
        BigEndian.WriteUInt16(bus.ChipRam, 0x2404, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2406, 0xFFFE);
        bus.WriteWord(0x00DFF09A, (ushort)(0xC000 | AmigaConstants.IntreqCopper));
        bus.WriteWord(0x00DFF080, 0x0000);
        bus.WriteWord(0x00DFF082, 0x2400);
        bus.Paula.AdvanceTo(0);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);
        bus.Paula.AdvanceTo(10);

        Assert.NotEqual(0, bus.ReadWord(0x00DFF01E) & AmigaConstants.IntreqCopper);
        Assert.Equal(3, bus.Paula.GetHighestPendingInterruptLevel());
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

        Assert.Equal(0xFF000000u, Pixel(frame, 0, 20));
    }

    [Fact]
    public void HardwareSpriteOverlaysPaletteColorsSixteenThroughThirtyOne()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF1A2, 0x000F);
        var (pos, ctl) = EncodeSpritePosition(StandardX, StandardY, 1);
        bus.WriteWord(0x00DFF140, pos);
        bus.WriteWord(0x00DFF142, ctl);
        bus.WriteWord(0x00DFF144, 0x8000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFF0000FFu, Pixel(frame, StandardX, StandardY));
    }

    [Fact]
    public void LiveDisplayCaptureDoesNotRestartSpriteDmaAfterTerminatorWithCopperPointerWrite()
    {
        var bus = new AmigaBus(agnusTimingMode: AgnusTimingMode.SlotEngine);
        var firstY = StandardY + 4;
        var reusedY = StandardY + 80;
        var (firstPos, firstCtl) = EncodeSpritePosition(StandardX, firstY, 1);
        var (reusedPos, reusedCtl) = EncodeSpritePosition(StandardX + 24, reusedY, 1);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, firstPos);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1002, firstCtl);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1004, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1006, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1008, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x100A, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1100, reusedPos);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1102, reusedCtl);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1104, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1106, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1108, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x110A, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x0120);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2402, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2404, 0x0122);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2406, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2408, CopperWaitForOutputRow(reusedY - 4));
        BigEndian.WriteUInt16(bus.ChipRam, 0x240A, 0xFFFE);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240C, 0x0120);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240E, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2410, 0x0122);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2412, 0x1100);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2414, CopperWaitForOutputRow(reusedY + 8));
        BigEndian.WriteUInt16(bus.ChipRam, 0x2416, 0xFFFE);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2418, 0x0120);
        BigEndian.WriteUInt16(bus.ChipRam, 0x241A, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x241C, 0x0122);
        BigEndian.WriteUInt16(bus.ChipRam, 0x241E, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2420, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2422, 0xFFFE);
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF1A2, 0x0F00);
        bus.WriteWord(0x00DFF080, 0x0000);
        bus.WriteWord(0x00DFF082, 0x2400);
        bus.WriteWord(0x00DFF096, 0x82A0);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame, 0, AmigaConstants.A500PalCpuCyclesPerFrame);

        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, firstY));
        Assert.Equal(0xFF000000u, Pixel(frame, StandardX + 24, reusedY));
    }

    [Fact]
    public void LiveSpriteDmaTerminatorPreventsLaterPointerWriteFromStartingSameFieldSprite()
    {
        var bus = new AmigaBus(agnusTimingMode: AgnusTimingMode.SlotEngine);
        var latePointerRow = StandardY + 40;
        var lateSpriteY = latePointerRow + 8;
        var (latePos, lateCtl) = EncodeSpritePosition(StandardX, lateSpriteY, 1);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1002, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3000, latePos);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3002, lateCtl);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3004, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3006, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3008, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x300A, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2400, CopperWaitForOutputRow(latePointerRow));
        BigEndian.WriteUInt16(bus.ChipRam, 0x2402, 0xFFFE);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2404, 0x0120);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2406, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2408, 0x0122);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240A, 0x3000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240C, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240E, 0xFFFE);
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF1A2, 0x0F00);
        bus.WriteWord(0x00DFF120, 0x0000);
        bus.WriteWord(0x00DFF122, 0x1000);
        bus.WriteWord(0x00DFF080, 0x0000);
        bus.WriteWord(0x00DFF082, 0x2400);
        bus.WriteWord(0x00DFF096, 0x82A0);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame, 0, AmigaConstants.A500PalCpuCyclesPerFrame);

        Assert.Equal(0xFF000000u, Pixel(frame, StandardX, lateSpriteY));
        Assert.Equal(0, bus.Display.CaptureSnapshot().LastSpriteNonZeroPixels);
    }

    [Fact]
    public void LiveDisplayCaptureDoesNotRenderSpritePointerLoadedAfterSpriteRange()
    {
        var bus = new AmigaBus(agnusTimingMode: AgnusTimingMode.SlotEngine);
        var latePointerRow = StandardY + 40;
        var (pos, ctl) = EncodeSpritePosition(StandardX, StandardY, 1);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3000, pos);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3002, ctl);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3004, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3006, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3008, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x300A, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2400, CopperWaitForOutputRow(latePointerRow));
        BigEndian.WriteUInt16(bus.ChipRam, 0x2402, 0xFFFE);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2404, 0x0120);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2406, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2408, 0x0122);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240A, 0x3000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240C, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240E, 0xFFFE);
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF1A2, 0x0F00);
        bus.WriteWord(0x00DFF080, 0x0000);
        bus.WriteWord(0x00DFF082, 0x2400);
        bus.WriteWord(0x00DFF096, 0x82A0);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame, 0, AmigaConstants.A500PalCpuCyclesPerFrame);

        Assert.Equal(0xFF000000u, Pixel(frame, StandardX, StandardY));
        Assert.Equal(0, bus.Display.CaptureSnapshot().LastSpriteNonZeroPixels);
    }

    [Fact]
    public void HardwareSpritePositionUsesReferenceManualCoordinateOffset()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF1A2, 0x000F);
        bus.WriteWord(0x00DFF140, 0x6D60);
        bus.WriteWord(0x00DFF142, 0x7200);
        bus.WriteWord(0x00DFF144, 0x8000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFF0000FFu, Pixel(frame, StandardX + 128, StandardY + 65));
    }

    [Fact]
    public void HardwareSpriteIsClippedByDisplayWindowLeftEdge()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF1A2, 0x0F00);
        bus.WriteWord(0x00DFF08E, 0x2C91);
        bus.WriteWord(0x00DFF090, 0x2CC1);
        var (pos, ctl) = EncodeSpritePosition(StandardX + 8, StandardY, 1);
        bus.WriteWord(0x00DFF140, pos);
        bus.WriteWord(0x00DFF142, ctl);
        bus.WriteWord(0x00DFF144, 0xFFFF);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFF000000u, Pixel(frame, StandardX + 8, StandardY));
        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX + 16, StandardY));
    }

    [Fact]
    public void HardwareSpriteIsClippedByDisplayWindowRightEdge()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF1A2, 0x0F00);
        var (pos, ctl) = EncodeSpritePosition(StandardX + 312, StandardY, 1);
        bus.WriteWord(0x00DFF140, pos);
        bus.WriteWord(0x00DFF142, ctl);
        bus.WriteWord(0x00DFF144, 0xFFFF);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX + 319, StandardY));
        Assert.Equal(0xFF000000u, Pixel(frame, StandardX + 320, StandardY));
    }

    [Fact]
    public void LowerNumberedHardwareSpriteHasHigherPriority()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF1A2, 0x0F00);
        bus.WriteWord(0x00DFF1AA, 0x00F0);
        var (pos, ctl) = EncodeSpritePosition(StandardX, StandardY, 1);
        bus.WriteWord(0x00DFF140, pos);
        bus.WriteWord(0x00DFF142, ctl);
        bus.WriteWord(0x00DFF144, 0x8000);
        bus.WriteWord(0x00DFF150, pos);
        bus.WriteWord(0x00DFF152, ctl);
        bus.WriteWord(0x00DFF154, 0x8000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY));
    }

    [Fact]
    public void HardwareSpritePriorityHonorsBplcon2PlayfieldPlacement()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF182, 0x0F00);
        bus.WriteWord(0x00DFF192, 0x00F0);
        bus.WriteWord(0x00DFF1A2, 0x000F);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x0038);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x1000);
        bus.WriteWord(0x00DFF0E4, 0x0000);
        bus.WriteWord(0x00DFF0E6, 0x1100);
        bus.WriteWord(0x00DFF104, 0x0020);
        bus.WriteWord(0x00DFF100, 0x2400);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1100, 0x4000);
        var (pos, ctl) = EncodeSpritePosition(StandardX, StandardY, 1);
        bus.WriteWord(0x00DFF140, pos);
        bus.WriteWord(0x00DFF142, ctl);
        bus.WriteWord(0x00DFF144, 0xC000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY));
        Assert.Equal(0xFF0000FFu, Pixel(frame, StandardX + 1, StandardY));
    }

    [Fact]
    public void HardwareSpritePriorityUsesBplcon2FromItsRasterSpan()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF096, 0x8220);
        bus.Paula.AdvanceTo(0);
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF182, 0x0F00);
        bus.WriteWord(0x00DFF1A2, 0x000F);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x0038);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x1000);
        bus.WriteWord(0x00DFF100, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1002, 0x8000);
        var (pos, ctl) = EncodeSpritePosition(StandardX, StandardY, 2);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3000, pos);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3002, ctl);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3004, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3006, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3008, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x300A, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x300C, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x300E, 0x0000);
        bus.WriteWord(0x00DFF120, 0x0000);
        bus.WriteWord(0x00DFF122, 0x3000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x0104);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2402, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2404, 0x2D01);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2406, 0xFF00);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2408, 0x0104);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240A, 0x0008);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240C, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240E, 0xFFFE);
        bus.WriteWord(0x00DFF080, 0x0000);
        bus.WriteWord(0x00DFF082, 0x2400);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY));
        Assert.Equal(0xFF0000FFu, Pixel(frame, StandardX, StandardY + 1));
    }

    [Fact]
    public void HardwareSpriteDmaListRendersMultipleRowsFromPointer()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF096, 0x8220);
        bus.Paula.AdvanceTo(0);
        bus.WriteWord(0x00DFF1A2, 0x0F00);
        bus.WriteWord(0x00DFF1A4, 0x00F0);
        var (pos, ctl) = EncodeSpritePosition(24, 30, 2);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3000, pos);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3002, ctl);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3004, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3006, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3008, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x300A, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x300C, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x300E, 0x0000);
        bus.WriteWord(0x00DFF120, 0x0000);
        bus.WriteWord(0x00DFF122, 0x3000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFFFF0000u, Pixel(frame, 24, 30));
        Assert.Equal(0xFF00FF00u, Pixel(frame, 24, 31));
    }

    [Fact]
    public void TimedHardwareSpriteReadsDataMemoryAtSpriteLineCycle()
    {
        var bus = new AmigaBus();
        var lineCycles = AmigaConstants.A500PalCpuCyclesPerRasterLine;
        var writeAfterSpriteLine = CycleForOutputRow(StandardY + 1, lineCycles);
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF096, 0x8220);
        bus.WriteWord(0x00DFF1A2, 0x0F00);
        var (pos, ctl) = EncodeSpritePosition(StandardX, StandardY, 1);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3000, pos);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3002, ctl);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3004, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3006, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3008, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x300A, 0x0000);
        bus.WriteWord(0x00DFF120, 0x0000);
        bus.WriteWord(0x00DFF122, 0x3000);
        bus.WriteWord(0x00003004, 0x0000, writeAfterSpriteLine);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame, 0, writeAfterSpriteLine + 1);

        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY));
    }

    [Fact]
    public void HardwareSpriteDmaListRendersMultipleControlBlocksFromPointer()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF096, 0x8220);
        bus.Paula.AdvanceTo(0);
        bus.WriteWord(0x00DFF1A2, 0x0F00);
        bus.WriteWord(0x00DFF1A4, 0x00F0);
        var (firstPos, firstCtl) = EncodeSpritePosition(24, 30, 1);
        var (secondPos, secondCtl) = EncodeSpritePosition(40, 60, 1);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3000, firstPos);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3002, firstCtl);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3004, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3006, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3008, secondPos);
        BigEndian.WriteUInt16(bus.ChipRam, 0x300A, secondCtl);
        BigEndian.WriteUInt16(bus.ChipRam, 0x300C, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x300E, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3010, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3012, 0x0000);
        bus.WriteWord(0x00DFF120, 0x0000);
        bus.WriteWord(0x00DFF122, 0x3000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFFFF0000u, Pixel(frame, 24, 30));
        Assert.Equal(0xFF00FF00u, Pixel(frame, 40, 60));
    }

    [Fact]
    public void CopperCanReuseHardwareSpriteDmaChannelWithinFrame()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF096, 0x8220);
        bus.Paula.AdvanceTo(0);
        bus.WriteWord(0x00DFF1A2, 0x0F00);
        bus.WriteWord(0x00DFF1A4, 0x00F0);
        var (firstPos, firstCtl) = EncodeSpritePosition(24, 30, 1);
        var (secondPos, secondCtl) = EncodeSpritePosition(40, 60, 1);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3000, firstPos);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3002, firstCtl);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3004, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3006, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3100, secondPos);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3102, secondCtl);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3104, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3106, 0x8000);
        var waitV = 0x2C - StandardY + 45;
        BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x0120);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2402, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2404, 0x0122);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2406, 0x3000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2408, (ushort)((waitV << 8) | 0x0001));
        BigEndian.WriteUInt16(bus.ChipRam, 0x240A, 0xFF00);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240C, 0x0120);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240E, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2410, 0x0122);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2412, 0x3100);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2414, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2416, 0xFFFE);
        bus.WriteWord(0x00DFF080, 0x0000);
        bus.WriteWord(0x00DFF082, 0x2400);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFFFF0000u, Pixel(frame, 24, 30));
        Assert.Equal(0xFF00FF00u, Pixel(frame, 40, 60));
        Assert.True(bus.Display.CaptureSnapshot().LastSpriteNonZeroPixels >= 2);
    }

    [Fact]
    public void CopperSpritePointerLoadedAfterBeamDoesNotRenderEarlierSpriteBlocks()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF096, 0x8220);
        bus.Paula.AdvanceTo(0);
        bus.WriteWord(0x00DFF1A2, 0x0F00);
        bus.WriteWord(0x00DFF1A4, 0x00F0);
        var (earlyPos, earlyCtl) = EncodeSpritePosition(24, 20, 1);
        var (latePos, lateCtl) = EncodeSpritePosition(40, 60, 1);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3000, earlyPos);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3002, earlyCtl);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3004, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3006, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3008, latePos);
        BigEndian.WriteUInt16(bus.ChipRam, 0x300A, lateCtl);
        BigEndian.WriteUInt16(bus.ChipRam, 0x300C, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x300E, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3010, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3012, 0x0000);
        var waitV = 0x2C - StandardY + 40;
        BigEndian.WriteUInt16(bus.ChipRam, 0x2400, (ushort)((waitV << 8) | 0x0001));
        BigEndian.WriteUInt16(bus.ChipRam, 0x2402, 0xFF00);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2404, 0x0120);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2406, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2408, 0x0122);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240A, 0x3000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240C, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240E, 0xFFFE);
        bus.WriteWord(0x00DFF080, 0x0000);
        bus.WriteWord(0x00DFF082, 0x2400);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFF000000u, Pixel(frame, 24, 20));
        Assert.Equal(0xFF00FF00u, Pixel(frame, 40, 60));
    }

    [Fact]
    public void HardwareSpriteUsesPaletteFromItsRasterSpan()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF096, 0x8220);
        bus.Paula.AdvanceTo(0);
        var (pos, ctl) = EncodeSpritePosition(StandardX, StandardY, 1);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3000, pos);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3002, ctl);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3004, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3006, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3008, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x300A, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x01A2);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2402, 0x0F00);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2404, 0x0120);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2406, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2408, 0x0122);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240A, 0x3000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240C, 0x2D01);
        BigEndian.WriteUInt16(bus.ChipRam, 0x240E, 0xFF00);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2410, 0x01A2);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2412, 0x00F0);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2414, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2416, 0xFFFE);
        bus.WriteWord(0x00DFF080, 0x0000);
        bus.WriteWord(0x00DFF082, 0x2400);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY));
    }

    [Fact]
    public void HardwareSpriteCtlWriteDisarmsManualSprite()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF1A2, 0x000F);
        var (pos, ctl) = EncodeSpritePosition(StandardX, StandardY, 1);
        bus.WriteWord(0x00DFF140, pos);
        bus.WriteWord(0x00DFF142, ctl);
        bus.WriteWord(0x00DFF144, 0x8000);
        bus.WriteWord(0x00DFF142, ctl);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFF000000u, Pixel(frame, StandardX, StandardY));
    }

    [Fact]
    public void HardwareSpriteDmaListIsIgnoredWhenSpriteDmaIsDisabled()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF1A2, 0x0F00);
        var (pos, ctl) = EncodeSpritePosition(24, 30, 1);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3000, pos);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3002, ctl);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3004, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3006, 0x0000);
        bus.WriteWord(0x00DFF120, 0x0000);
        bus.WriteWord(0x00DFF122, 0x3000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFF000000u, Pixel(frame, 24, 30));
    }

    [Fact]
    public void OddHardwareSpriteUsesSamePaletteGroupAsPreviousEvenSprite()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF1A2, 0x0F00);
        bus.WriteWord(0x00DFF1A6, 0x00F0);
        var (pos, ctl) = EncodeSpritePosition(StandardX, StandardY, 1);
        bus.WriteWord(0x00DFF148, pos);
        bus.WriteWord(0x00DFF14A, ctl);
        bus.WriteWord(0x00DFF14C, 0x8000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY));
    }

    [Fact]
    public void AttachedHardwareSpritePairUsesCombinedFourBitPalette()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF096, 0x8220);
        bus.Paula.AdvanceTo(0);
        bus.WriteWord(0x00DFF1AA, 0x00F0);
        var (evenPos, evenCtl) = EncodeSpritePosition(32, 40, 1);
        var (oddPos, oddCtl) = EncodeSpritePosition(32, 40, 1, attached: true);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3000, evenPos);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3002, evenCtl);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3004, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3006, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3100, oddPos);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3102, oddCtl);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3104, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3106, 0x0000);
        bus.WriteWord(0x00DFF120, 0x0000);
        bus.WriteWord(0x00DFF122, 0x3000);
        bus.WriteWord(0x00DFF124, 0x0000);
        bus.WriteWord(0x00DFF126, 0x3100);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFF00FF00u, Pixel(frame, 32, 40));
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
        EnableBlitterDma(bus);
        bus.WriteWord(0x00DFF058, 0x0041);
        RunBlitterUntilIdle(bus);

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
        EnableBlitterDma(bus);
        bus.WriteWord(0x00DFF058, 0x0041);
        RunBlitterUntilIdle(bus);

        Assert.Equal(0xFF00, BigEndian.ReadUInt16(bus.ChipRam, 0x4000, "blitter destination"));
    }

    [Fact]
    public void BlitterLineModeDrawsBoundedPixel()
    {
        var bus = new AmigaBus();
        BigEndian.WriteUInt16(bus.ChipRam, 0x4000, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x4002, 0x5678);

        bus.WriteWord(0x00DFF040, 0x0BCA);
        bus.WriteWord(0x00DFF042, 0x0001);
        bus.WriteWord(0x00DFF048, 0x0000);
        bus.WriteWord(0x00DFF04A, 0x4000);
        bus.WriteWord(0x00DFF054, 0x0000);
        bus.WriteWord(0x00DFF056, 0x4000);
        bus.WriteWord(0x00DFF072, 0xFFFF);
        bus.WriteWord(0x00DFF074, 0x8000);
        EnableBlitterDma(bus);
        bus.WriteWord(0x00DFF058, 0x0042);
        RunBlitterUntilIdle(bus);

        Assert.Equal(0x8000, BigEndian.ReadUInt16(bus.ChipRam, 0x4000, "line-mode destination"));
        Assert.Equal(0x5678, BigEndian.ReadUInt16(bus.ChipRam, 0x4002, "line-mode overflow sentinel"));
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
        EnableBlitterDma(bus);
        bus.WriteWord(0x00DFF058, 0x0081);
        RunBlitterUntilIdle(bus);

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
        EnableBlitterDma(bus);
        bus.WriteWord(0x00DFF058, 0x0041);
        RunBlitterUntilIdle(bus);

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
        EnableBlitterDma(bus);
        bus.WriteWord(0x00DFF058, 0x0081);
        RunBlitterUntilIdle(bus);
        bus.WriteWord(0x00DFF054, 0x0000);
        bus.WriteWord(0x00DFF056, 0x4000);
        bus.WriteWord(0x00DFF040, 0x09F0);
        bus.WriteWord(0x00DFF058, 0x0041);
        RunBlitterUntilIdle(bus);

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
        EnableBlitterDma(bus);
        bus.WriteWord(0x00DFF058, 0x0041);
        RunBlitterUntilIdle(bus);

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
        EnableBlitterDma(bus);
        bus.WriteWord(0x00DFF058, 0x0081);
        RunBlitterUntilIdle(bus);

        Assert.Equal(0x0000, BigEndian.ReadUInt16(bus.ChipRam, 0x4000, "first shifted row"));
        Assert.Equal(0x8000, BigEndian.ReadUInt16(bus.ChipRam, 0x4002, "second shifted row"));
    }

    [Fact]
    public void BlitterLatchesModuloAndMasksForActiveBlit()
    {
        var bus = new AmigaBus();
        for (var i = 0; i < 6; i++)
        {
            BigEndian.WriteUInt16(bus.ChipRam, 0x3000 + (i * 2), (ushort)(0x1100 + i));
        }

        bus.WriteWord(0x00DFF040, 0x09F0);
        bus.WriteWord(0x00DFF044, 0xFFFF);
        bus.WriteWord(0x00DFF046, 0xFFFF);
        bus.WriteWord(0x00DFF050, 0x0000);
        bus.WriteWord(0x00DFF052, 0x3000);
        bus.WriteWord(0x00DFF054, 0x0000);
        bus.WriteWord(0x00DFF056, 0x4000);
        EnableBlitterDma(bus);
        bus.WriteWord(0x00DFF058, 0x00C2);

        bus.WriteWord(0x00DFF044, 0x0000);
        bus.WriteWord(0x00DFF046, 0x0000);
        bus.WriteWord(0x00DFF064, 0x0020);
        bus.WriteWord(0x00DFF066, 0x0020);
        RunBlitterUntilIdle(bus);

        for (var i = 0; i < 6; i++)
        {
            Assert.Equal((ushort)(0x1100 + i), BigEndian.ReadUInt16(bus.ChipRam, 0x4000 + (i * 2), $"latched blit word {i}"));
        }
    }

    [Fact]
    public void BlitterDisabledSourceAMaskShiftsInZeroOnFirstWord()
    {
        var bus = new AmigaBus();
        BigEndian.WriteUInt16(bus.ChipRam, 0x3000, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3002, 0xFFFF);

        bus.WriteWord(0x00DFF040, 0x57CA);
        bus.WriteWord(0x00DFF042, 0x5000);
        bus.WriteWord(0x00DFF044, 0xFFFF);
        bus.WriteWord(0x00DFF046, 0xFE00);
        bus.WriteWord(0x00DFF04C, 0x0000);
        bus.WriteWord(0x00DFF04E, 0x3000);
        bus.WriteWord(0x00DFF048, 0x0000);
        bus.WriteWord(0x00DFF04A, 0x4000);
        bus.WriteWord(0x00DFF054, 0x0000);
        bus.WriteWord(0x00DFF056, 0x4000);
        bus.WriteWord(0x00DFF074, 0xFFFF);
        EnableBlitterDma(bus);
        bus.WriteWord(0x00DFF058, 0x0042);
        RunBlitterUntilIdle(bus);

        Assert.Equal(0x07FF, BigEndian.ReadUInt16(bus.ChipRam, 0x4000, "first masked cookie-cut word"));
        Assert.Equal(0xFFF0, BigEndian.ReadUInt16(bus.ChipRam, 0x4002, "last masked cookie-cut word"));
    }

    [Fact]
    public void BlitterImmediateSourceAUsesShiftLatchedWhenDataRegisterWasWritten()
    {
        var bus = new AmigaBus();

        bus.WriteWord(0x00DFF040, 0x41F0);
        bus.WriteWord(0x00DFF074, 0x8000);
        bus.WriteWord(0x00DFF040, 0x01F0);
        bus.WriteWord(0x00DFF054, 0x0000);
        bus.WriteWord(0x00DFF056, 0x4000);
        EnableBlitterDma(bus);
        bus.WriteWord(0x00DFF058, 0x0041);
        RunBlitterUntilIdle(bus);

        Assert.Equal(0x0800, BigEndian.ReadUInt16(bus.ChipRam, 0x4000, "source A immediate shift"));
    }

    [Fact]
    public void BlitterBusyBitRemainsSetUntilDmaCyclesComplete()
    {
        var bus = new AmigaBus();
        for (var i = 0; i < 4; i++)
        {
            BigEndian.WriteUInt16(bus.ChipRam, 0x3000 + (i * 2), (ushort)(0x1000 + i));
        }

        bus.WriteWord(0x00DFF040, 0x09F0);
        bus.WriteWord(0x00DFF050, 0x0000);
        bus.WriteWord(0x00DFF052, 0x3000);
        bus.WriteWord(0x00DFF054, 0x0000);
        bus.WriteWord(0x00DFF056, 0x4000);
        EnableBlitterDma(bus);
        bus.WriteWord(0x00DFF058, 0x0044);

        Assert.NotEqual(0, bus.ReadWord(0x00DFF002) & 0x4000);

        bus.AdvanceDmaTo(1);
        Assert.NotEqual(0, bus.ReadWord(0x00DFF002) & 0x4000);

        RunBlitterUntilIdle(bus);
        Assert.Equal(0, bus.ReadWord(0x00DFF002) & 0x4000);
        Assert.Equal(0x1003, BigEndian.ReadUInt16(bus.ChipRam, 0x4006, "last blit word"));
    }

    [Fact]
    public void BlitterRequestsLevelThreeInterruptOnCompletion()
    {
        var machine = new AmigaMachine(AmigaMachineOptions
            .ForProfile(AmigaMachineProfile.A500Pal512KBoot)
            .WithLiveAgnusDma(false));
        var bus = machine.Bus;
        BigEndian.WriteUInt16(bus.ChipRam, 0x3000, 0x1234);
        machine.Bus.WriteLong(0x6C, 0x0000_2000);
        machine.Cpu.Reset(0x1000, 0x3000);

        bus.WriteWord(0x00DFF09A, 0xC040);
        bus.WriteWord(0x00DFF040, 0x09F0);
        bus.WriteWord(0x00DFF050, 0x0000);
        bus.WriteWord(0x00DFF052, 0x3000);
        bus.WriteWord(0x00DFF054, 0x0000);
        bus.WriteWord(0x00DFF056, 0x4000);
        EnableBlitterDma(bus);
        bus.WriteWord(0x00DFF058, 0x0041);
        RunBlitterUntilIdle(bus);

        Assert.NotEqual(0, bus.ReadWord(0x00DFF01E) & AmigaConstants.IntreqBlitter);
        Assert.True(machine.DispatchPendingHardwareInterrupt());
        Assert.Equal(0x0000_2000u, machine.Cpu.State.ProgramCounter);
        Assert.Equal(3, (machine.Cpu.State.StatusRegister >> 8) & 7);
    }

    [Fact]
    public void BlitterZeroFlagTracksAllZeroAndNonZeroOutput()
    {
        var bus = new AmigaBus();

        bus.WriteWord(0x00DFF040, 0x0100);
        bus.WriteWord(0x00DFF054, 0x0000);
        bus.WriteWord(0x00DFF056, 0x4000);
        EnableBlitterDma(bus);
        bus.WriteWord(0x00DFF058, 0x0041);
        RunBlitterUntilIdle(bus);
        Assert.NotEqual(0, bus.ReadWord(0x00DFF002) & 0x2000);

        BigEndian.WriteUInt16(bus.ChipRam, 0x3000, 0x0001);
        bus.WriteWord(0x00DFF040, 0x09F0);
        bus.WriteWord(0x00DFF050, 0x0000);
        bus.WriteWord(0x00DFF052, 0x3000);
        bus.WriteWord(0x00DFF054, 0x0000);
        bus.WriteWord(0x00DFF056, 0x4000);
        bus.WriteWord(0x00DFF058, 0x0041);
        RunBlitterUntilIdle(bus);

        Assert.Equal(0, bus.ReadWord(0x00DFF002) & 0x2000);
    }

    [Fact]
    public void BlitterWaitsForDmaMasterAndBlitterEnable()
    {
        var bus = new AmigaBus();
        BigEndian.WriteUInt16(bus.ChipRam, 0x3000, 0x1234);

        bus.WriteWord(0x00DFF040, 0x09F0);
        bus.WriteWord(0x00DFF050, 0x0000);
        bus.WriteWord(0x00DFF052, 0x3000);
        bus.WriteWord(0x00DFF054, 0x0000);
        bus.WriteWord(0x00DFF056, 0x4000);
        bus.WriteWord(0x00DFF058, 0x0041);

        bus.AdvanceDmaTo(1000);
        Assert.True(bus.Blitter.CaptureSnapshot().Busy);
        Assert.Equal(0, BigEndian.ReadUInt16(bus.ChipRam, 0x4000, "gated destination"));

        EnableBlitterDma(bus, 1000);
        bus.AdvanceDmaTo(2000);

        Assert.False(bus.Blitter.CaptureSnapshot().Busy);
        Assert.Equal(0x1234, BigEndian.ReadUInt16(bus.ChipRam, 0x4000, "ungated destination"));
    }

    [Fact]
    public void BlitterNastyModeStallsCpuChipAndPseudoFastAccessButNotRealFastRam()
    {
        var nastyBus = new AmigaBus(
            expansionRamSize: 0x10000,
            realFastRamSize: 0x10000);
        BigEndian.WriteUInt16(nastyBus.ChipRam, 0x3000, 0x1234);
        ConfigureFourWordCopyBlit(nastyBus);
        nastyBus.WriteWord(0x00DFF096, 0x8640);
        nastyBus.AdvanceDmaTo(0);
        nastyBus.WriteWord(0x00DFF058, 0x0044);

        var expansionCycle = 0L;
        _ = nastyBus.ReadWord(AmigaConstants.A500BootPseudoFastRamBase, ref expansionCycle, AmigaBusAccessKind.CpuDataRead);
        Assert.True(expansionCycle > 0);
        Assert.True(nastyBus.Blitter.CaptureSnapshot().Busy);

        var realFastCycle = 0L;
        _ = nastyBus.ReadWord(AmigaConstants.A500RealFastRamBase, ref realFastCycle, AmigaBusAccessKind.CpuDataRead);
        Assert.Equal(0, realFastCycle);
        Assert.True(nastyBus.Blitter.CaptureSnapshot().Busy);

        var chipCycle = 0L;
        _ = nastyBus.ReadWord(0x00001000, ref chipCycle, AmigaBusAccessKind.CpuDataRead);
        Assert.True(chipCycle > 0);
        Assert.True(nastyBus.Blitter.CaptureSnapshot().Busy);

        var normalBus = new AmigaBus();
        BigEndian.WriteUInt16(normalBus.ChipRam, 0x3000, 0x1234);
        ConfigureFourWordCopyBlit(normalBus);
        EnableBlitterDma(normalBus);
        normalBus.WriteWord(0x00DFF058, 0x0044);

        var normalCycle = 0L;
        _ = normalBus.ReadWord(0x00001000, ref normalCycle, AmigaBusAccessKind.CpuDataRead);
        Assert.Equal(2 * AgnusChipSlotScheduler.SlotCycles, normalCycle);
        Assert.True(normalBus.Blitter.CaptureSnapshot().Busy);
    }

    [Fact]
    public void BlitterBusAccessLogShowsOrderedDmaReadsAndWrites()
    {
        var bus = new AmigaBus();
        BigEndian.WriteUInt16(bus.ChipRam, 0x3000, 0x1234);

        bus.WriteWord(0x00DFF040, 0x09F0);
        bus.WriteWord(0x00DFF050, 0x0000);
        bus.WriteWord(0x00DFF052, 0x3000);
        bus.WriteWord(0x00DFF054, 0x0000);
        bus.WriteWord(0x00DFF056, 0x4000);
        EnableBlitterDma(bus);
        bus.WriteWord(0x00DFF058, 0x0041);
        RunBlitterUntilIdle(bus);

        var blitterDma = bus.BusAccesses
            .Where(access => access.Request.Requester == AmigaBusRequester.Blitter && access.Request.Kind == AmigaBusAccessKind.Blitter)
            .ToArray();
        Assert.Equal(2, blitterDma.Length);
        Assert.False(blitterDma[0].Request.IsWrite);
        Assert.Equal(0x3000u, blitterDma[0].Request.Address);
        Assert.True(blitterDma[1].Request.IsWrite);
        Assert.Equal(0x4000u, blitterDma[1].Request.Address);
        Assert.True(blitterDma[0].RequestedCycle <= blitterDma[1].RequestedCycle);
    }

    [Theory]
    [InlineData(0x000A, 0x0008, 0xFFF8)]
    [InlineData(0x0012, 0x0008, 0xFFF0)]
    [InlineData(0x000E, 0x0000, 0xFFFF)]
    [InlineData(0x0016, 0x0000, 0xFFFF)]
    public void BlitterAreaFillHandlesInclusiveExclusiveAndInitialCarry(ushort bltcon1, ushort source, ushort expected)
    {
        var bus = new AmigaBus();
        BigEndian.WriteUInt16(bus.ChipRam, 0x3000, source);

        bus.WriteWord(0x00DFF040, 0x09F0);
        bus.WriteWord(0x00DFF042, bltcon1);
        bus.WriteWord(0x00DFF050, 0x0000);
        bus.WriteWord(0x00DFF052, 0x3000);
        bus.WriteWord(0x00DFF054, 0x0000);
        bus.WriteWord(0x00DFF056, 0x4000);
        EnableBlitterDma(bus);
        bus.WriteWord(0x00DFF058, 0x0041);
        RunBlitterUntilIdle(bus);

        Assert.Equal(expected, BigEndian.ReadUInt16(bus.ChipRam, 0x4000, "filled word"));
    }

    [Fact]
    public void BlitterAreaFillCarriesAcrossDescendingWordsAndResetsEachRow()
    {
        var bus = new AmigaBus();
        BigEndian.WriteUInt16(bus.ChipRam, 0x3000, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3002, 0x0008);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3004, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3006, 0x0008);

        bus.WriteWord(0x00DFF040, 0x09F0);
        bus.WriteWord(0x00DFF042, 0x000A);
        bus.WriteWord(0x00DFF050, 0x0000);
        bus.WriteWord(0x00DFF052, 0x3006);
        bus.WriteWord(0x00DFF054, 0x0000);
        bus.WriteWord(0x00DFF056, 0x4006);
        EnableBlitterDma(bus);
        bus.WriteWord(0x00DFF058, 0x0082);
        RunBlitterUntilIdle(bus);

        Assert.Equal(0xFFFF, BigEndian.ReadUInt16(bus.ChipRam, 0x4000, "row 0 carry word"));
        Assert.Equal(0xFFF8, BigEndian.ReadUInt16(bus.ChipRam, 0x4002, "row 0 edge word"));
        Assert.Equal(0xFFFF, BigEndian.ReadUInt16(bus.ChipRam, 0x4004, "row 1 carry word"));
        Assert.Equal(0xFFF8, BigEndian.ReadUInt16(bus.ChipRam, 0x4006, "row 1 edge word"));
    }

    [Fact]
    public void BlitterAreaFillRunsAfterMinterm()
    {
        var bus = new AmigaBus();
        BigEndian.WriteUInt16(bus.ChipRam, 0x3000, 0xFFF7);

        bus.WriteWord(0x00DFF040, 0x090F);
        bus.WriteWord(0x00DFF042, 0x000A);
        bus.WriteWord(0x00DFF050, 0x0000);
        bus.WriteWord(0x00DFF052, 0x3000);
        bus.WriteWord(0x00DFF054, 0x0000);
        bus.WriteWord(0x00DFF056, 0x4000);
        EnableBlitterDma(bus);
        bus.WriteWord(0x00DFF058, 0x0041);
        RunBlitterUntilIdle(bus);

        Assert.Equal(0xFFF8, BigEndian.ReadUInt16(bus.ChipRam, 0x4000, "fill-after-minterm destination"));
    }

    [Theory]
    [InlineData(0x0000, 1, 1)]
    [InlineData(0x0004, 1, -1)]
    [InlineData(0x0008, -1, 1)]
    [InlineData(0x000C, -1, -1)]
    [InlineData(0x0010, 1, 1)]
    [InlineData(0x0014, -1, 1)]
    [InlineData(0x0018, 1, -1)]
    [InlineData(0x001C, -1, -1)]
    public void BlitterLineModeUsesOctantBitsForMinorAndMajorSteps(ushort octantBits, int expectedSecondX, int expectedSecondY)
    {
        var bus = new AmigaBus();
        const uint Base = 0x4200;
        const int RowStride = 8;

        ConfigureLineBlit(bus, Base, RowStride, (ushort)(0x0001 | octantBits));
        bus.WriteWord(0x00DFF058, 0x0082);
        RunBlitterUntilIdle(bus);

        Assert.True(IsLinePixelSet(bus, Base, RowStride, 0, 0), "Expected the first line pixel.");
        Assert.True(IsLinePixelSet(bus, Base, RowStride, expectedSecondX, expectedSecondY), "Expected the second line pixel.");
    }

    [Theory]
    [InlineData(0x0000, 0, 1)]
    [InlineData(0x0004, 0, -1)]
    [InlineData(0x0008, 0, 1)]
    [InlineData(0x000C, 0, -1)]
    [InlineData(0x0010, 1, 0)]
    [InlineData(0x0014, -1, 0)]
    [InlineData(0x0018, 1, 0)]
    [InlineData(0x001C, -1, 0)]
    public void BlitterLineModeUsesOctantBitsForMajorOnlyStepsWhenSignIsSet(ushort octantBits, int expectedSecondX, int expectedSecondY)
    {
        var bus = new AmigaBus();
        const uint Base = 0x4200;
        const int RowStride = 8;

        ConfigureLineBlit(bus, Base, RowStride, (ushort)(0x0041 | octantBits), initialAccumulator: -2);
        bus.WriteWord(0x00DFF058, 0x0082);
        RunBlitterUntilIdle(bus);

        Assert.True(IsLinePixelSet(bus, Base, RowStride, 0, 0), "Expected the first line pixel.");
        Assert.True(IsLinePixelSet(bus, Base, RowStride, expectedSecondX, expectedSecondY), "Expected the major-axis line pixel.");
    }

    [Fact]
    public void BlitterLineModeUsesCModuloForDestinationStrideWhenDModuloIsZero()
    {
        var bus = new AmigaBus();
        const uint Base = 0x4200;
        const int RowStride = 8;

        ConfigureLineBlit(
            bus,
            Base,
            RowStride,
            0x0041,
            initialAccumulator: -2,
            aModulo: -4,
            bModulo: 0,
            dModulo: 0);
        bus.WriteWord(0x00DFF058, 0x0102);
        RunBlitterUntilIdle(bus);

        Assert.True(IsLinePixelSet(bus, Base, RowStride, 0, 0), "Expected the first vertical line pixel.");
        Assert.True(IsLinePixelSet(bus, Base, RowStride, 0, 1), "Expected the second vertical line pixel.");
        Assert.True(IsLinePixelSet(bus, Base, RowStride, 0, 2), "Expected the third vertical line pixel.");
        Assert.True(IsLinePixelSet(bus, Base, RowStride, 0, 3), "Expected the fourth vertical line pixel.");
    }

    [Fact]
    public void BlitterLineSingleDotModeDrawsOnlyOnePixelPerRow()
    {
        var bus = new AmigaBus();
        const uint Base = 0x4200;
        const int RowStride = 8;

        ConfigureLineBlit(bus, Base, RowStride, 0x0007);
        bus.WriteWord(0x00DFF058, 0x0082);
        RunBlitterUntilIdle(bus);

        Assert.Equal(0x8000, BigEndian.ReadUInt16(bus.ChipRam, (int)Base, "single-dot row"));
    }

    [Fact]
    public void BlitterLineTextureStartPhaseControlsFirstPixel()
    {
        var bus = new AmigaBus();
        const uint Base = 0x4200;

        ConfigureLineBlit(bus, Base, 8, 0x1001, texture: 0x8000);
        bus.WriteWord(0x00DFF058, 0x0042);
        RunBlitterUntilIdle(bus);

        Assert.Equal(0, BigEndian.ReadUInt16(bus.ChipRam, (int)Base, "texture-skipped first pixel"));
    }

    [Fact]
    public void BlitterLineXorModeCanEraseAnExistingPixel()
    {
        var bus = new AmigaBus();
        const uint Base = 0x4200;
        BigEndian.WriteUInt16(bus.ChipRam, (int)Base, 0x8000);

        ConfigureLineBlit(bus, Base, 8, 0x0001, minterm: 0x4A);
        bus.WriteWord(0x00DFF058, 0x0042);
        RunBlitterUntilIdle(bus);

        Assert.Equal(0, BigEndian.ReadUInt16(bus.ChipRam, (int)Base, "xor-erased pixel"));
    }

    [Fact]
    public void CiaBPortBControlsFloppyStepDirectionSideAndMotor()
    {
        var bus = new AmigaBus();
        var data = new byte[AmigaDiskImage.StandardAdfSize];
        bus.Disk.Drive0.Insert(AmigaDiskImage.FromAdfBytes(data));

        bus.WriteByte(0x00BFD100, 0xFF, 0);
        bus.WriteByte(0x00BFD300, 0xFF, 0);
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
    public void CiaBPortBOnlyStepsSelectedFloppyDrive()
    {
        var bus = new AmigaBus();
        var data = new byte[AmigaDiskImage.StandardAdfSize];
        bus.Disk.Drive0.Insert(AmigaDiskImage.FromAdfBytes(data));

        bus.WriteByte(0x00BFD100, 0xFF, 0);
        bus.WriteByte(0x00BFD300, 0xFF, 0);
        bus.WriteByte(0x00BFD100, 0xFE, 0);
        bus.WriteByte(0x00BFD100, 0xFF, 0);

        var snapshot = bus.Disk.CaptureSnapshot();
        Assert.Equal(0, snapshot.Cylinder);
        Assert.False(snapshot.Selected);

        bus.WriteByte(0x00BFD100, 0xF5, 0);
        bus.WriteByte(0x00BFD100, 0xF4, 0);

        snapshot = bus.Disk.CaptureSnapshot();
        Assert.Equal(1, snapshot.Cylinder);
        Assert.True(snapshot.Selected);
    }

    [Fact]
    public void CiaBPortBDoesNotTreatOtherDriveSelectBitsAsDf0()
    {
        var bus = new AmigaBus();
        var data = new byte[AmigaDiskImage.StandardAdfSize];
        bus.Disk.Drive0.Insert(AmigaDiskImage.FromAdfBytes(data));

        bus.WriteByte(0x00BFD100, 0xFF, 0);
        bus.WriteByte(0x00BFD300, 0xFF, 0);
        bus.WriteByte(0x00BFD100, 0x7F, 0);
        bus.WriteByte(0x00BFD100, 0xEF, 0);
        bus.WriteByte(0x00BFD100, 0xEE, 0);

        var snapshot = bus.Disk.CaptureSnapshot();
        Assert.False(snapshot.Selected);
        Assert.False(snapshot.MotorOn);
        Assert.Equal(-1, snapshot.SelectedDrive);
        Assert.Equal(0, snapshot.Cylinder);
        Assert.NotEqual(0, bus.ReadByte(0x00BFE001) & 0x20);
    }

    [Fact]
    public void DiskDmaRequiresTwoMatchingDsklenWritesToStart()
    {
        var bus = new AmigaBus();
        var data = new byte[AmigaDiskImage.StandardAdfSize];
        data[0] = 0x12;
        data[1] = 0x34;
        bus.Disk.Drive0.Insert(AmigaDiskImage.FromAdfBytes(data));
        var cycle = PrepareDiskDma(bus);

        bus.WriteWord(0x00DFF020, 0x0000, cycle);
        bus.WriteWord(0x00DFF022, 0x4000, cycle);
        bus.WriteWord(0x00DFF024, 0x8002, cycle);

        Assert.Equal(0, bus.Disk.CaptureSnapshot().TransferCount);

        bus.WriteWord(0x00DFF024, 0x8002, cycle);

        var snapshot = bus.Disk.CaptureSnapshot();
        Assert.Equal(1, snapshot.TransferCount);
        Assert.Equal(2, snapshot.LastTransferWords);
        Assert.Equal(0x4000u, snapshot.LastTransferAddress);
    }

    [Fact]
    public void DiskDmaReadsFromSelectedExternalDrive()
    {
        var bus = new AmigaBus(floppyDriveCount: 2);
        bus.Disk.Drive0.Insert(AmigaDiskImage.FromEncodedTracks(CreateSingleWordTrackSet(0xABCD)));
        bus.Disk.Drive1.Insert(AmigaDiskImage.FromEncodedTracks(CreateSingleWordTrackSet(0x1234)));
        var cycle = PrepareDiskDma(bus, driveIndex: 1);

        WriteDsklenStartSequence(bus, 0x4000, 0x0001, cycle);
        CompleteDiskDma(bus, cycle, 0x0001);

        var snapshot = bus.Disk.CaptureSnapshot();
        Assert.Equal(1, snapshot.SelectedDrive);
        Assert.Equal(1, snapshot.LastTransferDrive);
        Assert.Equal(0x1234, BigEndian.ReadUInt16(bus.ChipRam, 0x4000, "external drive DMA word"));
    }

    [Fact]
    public void DiskDmaRequiresDmaconMasterAndDiskEnableBits()
    {
        var bus = CreateLegacyDiskDisplayBus();
        bus.Disk.Drive0.Insert(AmigaDiskImage.FromAdfBytes(new byte[AmigaDiskImage.StandardAdfSize]));
        SelectDf0AndStartMotor(bus);

        WriteDsklenStartSequence(bus, 0x4000, 0x0002);
        CompleteDiskDma(bus, 0, 0x0002);

        Assert.Equal(0, bus.Disk.CaptureSnapshot().TransferCount);
        Assert.Equal(0, BigEndian.ReadUInt16(bus.ChipRam, 0x4000, "DMA without DMACON"));

        var cycle = GetMotorReadyCycle(bus, 0, 10L);
        bus.AdvanceDmaTo(cycle);
        bus.WriteWord(0x00DFF096, 0x8210, cycle);
        bus.Paula.AdvanceTo(cycle);
        bus.AdvanceDmaTo(cycle);
        CompleteDiskDma(bus, cycle, 0x0002);

        Assert.Equal(1, bus.Disk.CaptureSnapshot().TransferCount);
        Assert.NotEqual(0, BigEndian.ReadUInt16(bus.ChipRam, 0x4000, "DMA with DMACON"));
    }

    [Fact]
    public void DskbytrReportsAndClearsByteReadyStatus()
    {
        var bus = new AmigaBus();
        bus.Disk.Drive0.Insert(AmigaDiskImage.FromAdfBytes(new byte[AmigaDiskImage.StandardAdfSize]));
        SelectDf0AndStartMotor(bus);
        var readyCycle = GetMotorReadyCycle(bus, 0);

        bus.AdvanceDmaTo(readyCycle + DiskByteCycleCount(1));

        var first = bus.ReadWord(0x00DFF01A);
        Assert.NotEqual(0, first & 0x8000);
        Assert.Equal(0xAA, first & 0x00FF);

        var second = bus.ReadWord(0x00DFF01A);
        Assert.Equal(0, second & 0x8000);
        Assert.Equal(0xAA, second & 0x00FF);
    }

    [Fact]
    public void DskbytrReportsDmaOnAndDiskWriteBitsFromControlRegisters()
    {
        var bus = new AmigaBus();
        bus.Disk.Drive0.Insert(AmigaDiskImage.FromAdfBytes(new byte[AmigaDiskImage.StandardAdfSize]));
        var cycle = PrepareDiskDma(bus);

        WriteDsklenStartSequence(bus, 0x4000, 0x0020, cycle);
        Assert.NotEqual(0, bus.ReadWord(0x00DFF01A) & 0x4000);

        bus.WriteWord(0x00DFF024, 0x4000, cycle);
        Assert.NotEqual(0, bus.ReadWord(0x00DFF01A) & 0x2000);
        Assert.Equal(0, bus.ReadWord(0x00DFF01A) & 0x4000);
    }

    [Fact]
    public void DskSyncSetsIntreqIndependentOfWordsync()
    {
        var bus = new AmigaBus();
        bus.Disk.Drive0.Insert(AmigaDiskImage.FromAdfBytes(new byte[AmigaDiskImage.StandardAdfSize]));
        SelectDf0AndStartMotor(bus);
        var readyCycle = GetMotorReadyCycle(bus, 0);
        bus.AdvanceDmaTo(readyCycle);
        bus.WriteWord(0x00DFF07E, 0x4489, readyCycle);

        var firstSyncCycle = readyCycle + DiskByteCycleCount(6);
        bus.AdvanceDmaTo(firstSyncCycle - 1);
        Assert.Equal(0, bus.ReadWord(0x00DFF01E) & 0x1000);

        bus.AdvanceDmaTo(firstSyncCycle);

        Assert.NotEqual(0, bus.ReadWord(0x00DFF01E) & 0x1000);
        Assert.NotEqual(0, bus.ReadWord(0x00DFF01A) & 0x1000);
    }

    [Fact]
    public void FloppyIndexPulseSetsCiaBFlagInterrupt()
    {
        var bus = new AmigaBus();
        bus.Disk.Drive0.Insert(AmigaDiskImage.FromAdfBytes(new byte[AmigaDiskImage.StandardAdfSize]));
        bus.AbleCiaInterrupts(AmigaCiaId.B, 0x80 | AmigaCia.FlagInterruptMask, 0);
        bus.WriteByte(0x00BFD100, 0xFF, 0);
        bus.WriteByte(0x00BFD300, 0xFF, 0);
        bus.WriteByte(0x00BFD100, 0x77, 0);
        bus.WriteByte(0x00BFD100, 0xFF, 0);
        var indexCycle = (long)Math.Round(AmigaConstants.A500PalCpuClockHz / 5);

        bus.AdvanceCiasTo(indexCycle - 1);
        Assert.Empty(bus.DrainCiaInterrupts());

        bus.AdvanceCiasTo(indexCycle);

        var interruptEvent = Assert.Single(bus.DrainCiaInterrupts());
        Assert.Equal(AmigaCiaId.B, interruptEvent.Cia);
        Assert.Equal(AmigaCia.FlagInterruptMask, interruptEvent.IcrBits);
    }

    [Fact]
    public void DiskDmaBlockInterruptArrivesAfterLoaderCanClearOldRequest()
    {
        var bus = CreateLegacyDiskDisplayBus();
        var data = new byte[AmigaDiskImage.StandardAdfSize];
        bus.Disk.Drive0.Insert(AmigaDiskImage.FromAdfBytes(data));

        StartDiskDma(bus, 0x4000, 0x0100);
        var clearCycle = GetMotorReadyCycle(bus, 0);
        bus.WriteWord(0x00DFF09C, 0x0002, clearCycle);
        bus.Paula.AdvanceTo(clearCycle);

        Assert.Equal(0, bus.ReadWord(0x00DFF01E) & 0x0002);

        var completionCycle = bus.Disk.CaptureSnapshot().ActiveDmaCompletionCycle;

        bus.AdvanceCiasTo(completionCycle - 1);
        bus.Paula.AdvanceTo(completionCycle - 1);
        Assert.Equal(0, bus.ReadWord(0x00DFF01E) & 0x0002);

        bus.AdvanceCiasTo(completionCycle);
        bus.Paula.AdvanceTo(completionCycle);
        Assert.NotEqual(0, bus.ReadWord(0x00DFF01E) & 0x0002);
    }

    [Fact]
    public void DiskDmaStopCancelsUntransferredWordsBeforeOverlappingRestart()
    {
        var bus = new AmigaBus();
        var data = new byte[AmigaDiskImage.StandardAdfSize];
        bus.Disk.Drive0.Insert(AmigaDiskImage.FromAdfBytes(data));
        bus.WriteWord(0x00DFF09E, 0x8400);
        bus.Paula.AdvanceTo(0);

        StartDiskDma(bus, 0x6000, 0x1540, 0);
        var stopCycle = GetMotorReadyCycle(bus, 0) + 144;
        bus.WriteWord(0x00DFF024, 0x0000, stopCycle);
        var restartCycle = stopCycle + 4;
        StartDiskDma(bus, 0x6006, 0x0220, restartCycle);
        CompleteDiskDma(bus, restartCycle, 0x0220);

        Assert.Equal(0, BigEndian.ReadUInt16(bus.ChipRam, 0x6000 + 0x0880, "cancelled long DMA body"));
        Assert.Equal(0x4489, BigEndian.ReadUInt16(bus.ChipRam, 0x6006, "short DMA synced word"));
        Assert.Equal(0xFF00_090Bu, DecodeOddEvenLong(bus.ChipRam, 0x6008, 0x600C));
    }

    [Fact]
    public void DiskDmaAdvancesDskptToNextWord()
    {
        var bus = new AmigaBus();
        var data = new byte[AmigaDiskImage.StandardAdfSize];
        bus.Disk.Drive0.Insert(AmigaDiskImage.FromAdfBytes(data));

        StartDiskDma(bus, 0x4000, 0x0100);
        CompleteDiskDma(bus, 0, 0x0100);

        Assert.Equal(0x4200u, bus.Disk.CaptureSnapshot().DiskPointer);
    }

    [Fact]
    public void AmigaDosTrackEncoderWritesValidMfmClockBitsAndDecodableSectorData()
    {
        var data = new byte[AmigaDiskImage.StandardAdfSize];
        var firstPhysicalSectorOffset = 9 * AmigaDiskImage.SectorSize;
        for (var i = 0; i < AmigaDiskImage.SectorSize; i++)
        {
            data[firstPhysicalSectorOffset + i] = (byte)i;
        }

        var disk = AmigaDiskImage.FromAdfBytes(data);
        var track = AmigaDosTrackEncoder.EncodeTrack(disk, 0, 0);

        Assert.Equal(0x4489, BigEndian.ReadUInt16(track, 0x04, "first sync"));
        Assert.Equal(0x4489, BigEndian.ReadUInt16(track, 0x06, "second sync"));
        Assert.NotEqual(0u, BigEndian.ReadUInt32(track, 0x08, "encoded header odd") & 0xAAAA_AAAAu);
        Assert.Equal(0xFF00_090Bu, DecodeOddEvenLong(track, 0x08, 0x0C));
        Assert.Equal(BigEndian.ReadUInt32(data, firstPhysicalSectorOffset, "source sector data"), DecodeOddEvenLong(track, 0x40, 0x240));
    }

    [Fact]
    public void WordSyncedDiskDmaContinuesFromCurrentRawTrackPosition()
    {
        var bus = new AmigaBus();
        var data = new byte[AmigaDiskImage.StandardAdfSize];
        bus.Disk.Drive0.Insert(AmigaDiskImage.FromAdfBytes(data));
        bus.WriteWord(0x00DFF09E, 0x8400);
        bus.Paula.AdvanceTo(0);

        var cycle = 0L;
        StartDiskDma(bus, 0x4000, 0x0020, cycle);
        cycle = CompleteDiskDma(bus, cycle, 0x0020);
        StartDiskDma(bus, 0x4100, 0x0020, cycle);
        CompleteDiskDma(bus, cycle, 0x0020);

        Assert.Equal(0x4489, BigEndian.ReadUInt16(bus.ChipRam, 0x4000, "first synced DMA word"));
        Assert.Equal(0x4489, BigEndian.ReadUInt16(bus.ChipRam, 0x4100, "second synced DMA word"));
        Assert.Equal(0xFF00_090Bu, DecodeOddEvenLong(bus.ChipRam, 0x4002, 0x4006));
        Assert.Equal(0xFF00_0A0Au, DecodeOddEvenLong(bus.ChipRam, 0x4102, 0x4106));
    }

    [Fact]
    public void WordSyncedDiskDmaReadsNonByteAlignedBitstreamWords()
    {
        var bus = CreateLegacyDiskDisplayBus();
        var tracks = CreateUnformattedEncodedTrackSet();
        var rawTrack = new byte[8];
        BigEndian.WriteUInt16(rawTrack, 0, 0x4489);
        BigEndian.WriteUInt16(rawTrack, 2, 0x4489);
        BigEndian.WriteUInt16(rawTrack, 4, 0xABCD);
        BigEndian.WriteUInt16(rawTrack, 6, 0x1357);
        tracks[0] = ShiftTrackBits(rawTrack, 3);
        bus.Disk.Drive0.Insert(AmigaDiskImage.FromEncodedTracks(tracks));
        bus.WriteWord(0x00DFF09E, 0x8400);
        bus.Paula.AdvanceTo(0);

        StartDiskDma(bus, 0x4000, 0x0003);
        CompleteDiskDma(bus, 0, 0x0003);

        Assert.Equal(0x4489, BigEndian.ReadUInt16(bus.ChipRam, 0x4000, "non-byte-aligned sync DMA word"));
        Assert.Equal(0xABCD, BigEndian.ReadUInt16(bus.ChipRam, 0x4002, "non-byte-aligned payload word"));
        Assert.Equal(0x1357, BigEndian.ReadUInt16(bus.ChipRam, 0x4004, "non-byte-aligned second payload word"));
        Assert.NotEqual(0, bus.ReadWord(0x00DFF01E) & 0x1000);
        Assert.NotEqual(0, bus.ReadWord(0x00DFF01E) & 0x0002);
    }

    [Fact]
    public void DiskDmaDoesNotStartUntilDriveIsSelectedAndMotorOn()
    {
        var bus = new AmigaBus();
        var data = new byte[AmigaDiskImage.StandardAdfSize];
        bus.Disk.Drive0.Insert(AmigaDiskImage.FromAdfBytes(data));
        bus.WriteWord(0x00DFF096, 0x8210);
        bus.WriteWord(0x00DFF09E, 0x8400);
        bus.Paula.AdvanceTo(0);

        WriteDsklenStartSequence(bus, 0x4000, 0x0020, 1_000_000);

        CompleteDiskDma(bus, 1_000_000, 0x0020);
        Assert.Equal(0, bus.Disk.CaptureSnapshot().TransferCount);
        Assert.Equal(0, BigEndian.ReadUInt16(bus.ChipRam, 0x4000, "unselected DMA word"));

        StartDiskDma(bus, 0x4000, 0x0020, 1_000_000);
        CompleteDiskDma(bus, 1_000_000, 0x0020);
        Assert.Equal(0x4489, BigEndian.ReadUInt16(bus.ChipRam, 0x4000, "synced DMA word"));
        Assert.Equal(0xFF00_090Bu, DecodeOddEvenLong(bus.ChipRam, 0x4002, 0x4006));
    }

    [Fact]
    public void WordSyncedDiskDmaCanResynchronizeAcrossSectorBoundary()
    {
        var bus = new AmigaBus();
        var data = new byte[AmigaDiskImage.StandardAdfSize];
        bus.Disk.Drive0.Insert(AmigaDiskImage.FromAdfBytes(data));
        bus.WriteWord(0x00DFF09E, 0x8400);
        bus.Paula.AdvanceTo(0);

        var cycle = 0L;
        StartDiskDma(bus, 0x4000, 0x0220, cycle);
        cycle = CompleteDiskDma(bus, cycle, 0x0220);
        StartDiskDma(bus, 0x5000, 0x0020, cycle);
        CompleteDiskDma(bus, cycle, 0x0020);

        Assert.Equal(0x4489, BigEndian.ReadUInt16(bus.ChipRam, 0x5000, "synced DMA word"));
        var headerOffset = BigEndian.ReadUInt16(bus.ChipRam, 0x5002, "possible second sync") == 0x4489
            ? 0x5004
            : 0x5002;
        Assert.Equal(0xFF00_0009u, DecodeOddEvenLong(bus.ChipRam, headerOffset, headerOffset + 4));
    }

    [Fact]
    public void WordSyncedFullTrackDmaIncludesEverySectorHeaderAfterWrap()
    {
        var bus = new AmigaBus();
        var data = new byte[AmigaDiskImage.StandardAdfSize];
        bus.Disk.Drive0.Insert(AmigaDiskImage.FromAdfBytes(data));
        bus.WriteWord(0x00DFF09E, 0x8400);
        bus.Paula.AdvanceTo(0);

        var cycle = 0L;
        StartDiskDma(bus, 0x4000, 0x0020, cycle);
        cycle = CompleteDiskDma(bus, cycle, 0x0020);
        StartDiskDma(bus, 0x5000, 0x1900, cycle);
        CompleteDiskDma(bus, cycle, 0x1900);

        var sectors = new SortedSet<int>();
        for (var address = 0x5000; address + 0x10 < 0x5000 + 0x3200; address += 2)
        {
            if (BigEndian.ReadUInt16(bus.ChipRam, address, "candidate sync") != 0x4489 ||
                BigEndian.ReadUInt16(bus.ChipRam, address + 2, "candidate second sync") != 0x4489)
            {
                continue;
            }

            var header = DecodeOddEvenLong(bus.ChipRam, address + 4, address + 8);
            if ((header & 0xFFFF_0000u) == 0xFF00_0000u)
            {
                sectors.Add((int)((header >> 8) & 0xFF));
            }
        }

        Assert.Equal(Enumerable.Range(0, AmigaDiskImage.SectorsPerTrack), sectors);
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
        bus.WriteByte(0x00BFD300, 0xFF, 0);
        bus.WriteByte(0x00BFD100, 0x77, 0);
        var notReady = bus.ReadByte(0x00BFE001);
        Assert.NotEqual(0, notReady & 0x20);

        var readyCycle = GetMotorReadyCycle(bus, 0);
        bus.AdvanceDmaTo(readyCycle);
        var ready = bus.ReadByte(0x00BFE001);
        Assert.Equal(0, ready & 0x20);

        bus.Disk.Drive0.Insert(AmigaDiskImage.FromAdfBytes(data), markChanged: true);
        Assert.Equal(0, bus.ReadByte(0x00BFE001) & 0x04);

        bus.WriteByte(0x00BFD100, 0x71, 0);
        bus.WriteByte(0x00BFD100, 0x70, 0);
        Assert.NotEqual(0, bus.ReadByte(0x00BFE001) & 0x04);
    }

    [Fact]
    public void CiaAPortAReportsDisconnectedExternalDriveAsAbsent()
    {
        var bus = new AmigaBus();
        var data = new byte[AmigaDiskImage.StandardAdfSize];
        bus.Disk.Drive0.Insert(AmigaDiskImage.FromAdfBytes(data));

        bus.WriteByte(0x00BFD100, 0xFF, 0);
        bus.WriteByte(0x00BFD300, 0xFF, 0);
        bus.WriteByte(0x00BFD100, 0x6F, 0);

        var absentExternal = bus.ReadByte(0x00BFE001);
        Assert.Equal(0x3C, absentExternal & 0x3C);
        Assert.Equal(-1, bus.Disk.CaptureSnapshot().SelectedDrive);
    }

    [Fact]
    public void CiaAPortAReportsConnectedExternalDriveStatus()
    {
        var bus = new AmigaBus(floppyDriveCount: 2);
        var data = new byte[AmigaDiskImage.StandardAdfSize];
        bus.WriteByte(0x00BFD100, 0xFF, 0);
        bus.WriteByte(0x00BFD300, 0xFF, 0);
        bus.WriteByte(0x00BFD100, 0x6F, 0);

        var emptyExternal = bus.ReadByte(0x00BFE001);
        Assert.Equal(0, emptyExternal & 0x04);
        Assert.NotEqual(0, emptyExternal & 0x10);
        Assert.NotEqual(0, emptyExternal & 0x20);

        bus.Disk.Drive1.Insert(AmigaDiskImage.FromAdfBytes(data));
        bus.WriteByte(0x00BFD100, 0xFF, 0);
        bus.WriteByte(0x00BFD100, 0x6F, 0);

        var notReadyExternal = bus.ReadByte(0x00BFE001);
        Assert.NotEqual(0, notReadyExternal & 0x20);

        var readyCycle = GetMotorReadyCycle(bus, 1);
        bus.AdvanceDmaTo(readyCycle);
        var presentExternal = bus.ReadByte(0x00BFE001);
        Assert.NotEqual(0, presentExternal & 0x04);
        Assert.Equal(0, presentExternal & 0x10);
        Assert.Equal(0, presentExternal & 0x20);
    }

    [Fact]
    public void CiaAPortAReportsReadyOnlyWhenInsertedMotorIsOn()
    {
        var bus = new AmigaBus();
        bus.Disk.Drive0.Insert(AmigaDiskImage.FromAdfBytes(new byte[AmigaDiskImage.StandardAdfSize]));
        bus.WriteByte(0x00BFD100, 0xFF, 0);
        bus.WriteByte(0x00BFD300, 0xFF, 0);

        Assert.NotEqual(0, bus.ReadByte(0x00BFE001) & 0x20);

        bus.WriteByte(0x00BFD100, 0xF7, 0);
        Assert.NotEqual(0, bus.ReadByte(0x00BFE001) & 0x20);

        bus.WriteByte(0x00BFD100, 0xFF, 0);
        bus.WriteByte(0x00BFD100, 0x77, 0);
        Assert.NotEqual(0, bus.ReadByte(0x00BFE001) & 0x20);

        var readyCycle = GetMotorReadyCycle(bus, 0);
        bus.AdvanceDmaTo(readyCycle);
        Assert.Equal(0, bus.ReadByte(0x00BFE001) & 0x20);

        bus.WriteByte(0x00BFD100, 0xFF, 0);
        Assert.Equal(0, bus.ReadByte(0x00BFE001) & 0x20);
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
    public void JoyDatReportsMouseCountersAndJoystickDirections()
    {
        var bus = new AmigaBus();

        bus.MoveGamePortMouse(0, 3, -2);
        bus.SetGamePortJoystick(1, up: true, down: false, left: false, right: true);

        Assert.Equal(0xFE03, bus.ReadWord(0x00DFF00A));
        Assert.Equal(0x0103, bus.ReadWord(0x00DFF00C));
    }

    [Theory]
    [InlineData(true, false, false, false, 0x0100)]
    [InlineData(false, true, false, false, 0x0001)]
    [InlineData(false, false, true, false, 0x0300)]
    [InlineData(false, false, false, true, 0x0003)]
    [InlineData(true, false, true, false, 0x0200)]
    [InlineData(false, true, false, true, 0x0002)]
    public void JoyDatReportsJoystickQuadratureDirections(bool up, bool down, bool left, bool right, ushort expected)
    {
        var bus = new AmigaBus();

        bus.SetGamePortJoystick(1, up, down, left, right);

        Assert.Equal(expected, bus.ReadWord(0x00DFF00C));
    }

    [Fact]
    public void PotgorReportsSecondFireButtonsAsActiveLow()
    {
        var bus = new AmigaBus();

        Assert.Equal(0x4400, bus.ReadWord(0x00DFF016) & 0x4400);

        bus.GamePort0SecondFirePressed = true;
        bus.GamePort1SecondFirePressed = true;

        Assert.Equal(0, bus.ReadWord(0x00DFF016) & 0x4400);
    }

    [Fact]
    public void EnabledDiskBlockInterruptDispatchesLevelOneAutovector()
    {
        var machine = new AmigaMachine(AmigaMachineOptions
            .ForProfile(AmigaMachineProfile.A500Pal512KBoot)
            .WithLiveAgnusDma(false));
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
        var firstFrameCycle = AmigaConstants.A500PalCpuCyclesPerFrame;

        bus.AdvanceRasterTo(firstFrameCycle - 1);
        bus.Paula.AdvanceTo(firstFrameCycle - 1);
        Assert.Equal(0, bus.ReadWord(0x00DFF01E) & AmigaConstants.IntreqVerticalBlank);

        bus.AdvanceRasterTo(firstFrameCycle);
        bus.Paula.AdvanceTo(firstFrameCycle);
        Assert.NotEqual(0, bus.ReadWord(0x00DFF01E) & AmigaConstants.IntreqVerticalBlank);

        var clearCycle = (long)firstFrameCycle;
        bus.WriteWord(
            0x00DFF09C,
            AmigaConstants.IntreqVerticalBlank,
            ref clearCycle,
            AmigaBusAccessKind.CpuDataWrite);
        bus.Paula.AdvanceTo(clearCycle);
        Assert.Equal(0, bus.ReadWord(0x00DFF01E) & AmigaConstants.IntreqVerticalBlank);

        bus.AdvanceRasterTo(firstFrameCycle * 2);
        bus.Paula.AdvanceTo(firstFrameCycle * 2);
        Assert.NotEqual(0, bus.ReadWord(0x00DFF01E) & AmigaConstants.IntreqVerticalBlank);
    }

    [Fact]
    public void RasterAdvanceUpdatesBeamPositionLongword()
    {
        var bus = new AmigaBus();
        var cycleForLine300 = AmigaConstants.A500PalCpuCyclesPerRasterLine * 300L;

        bus.AdvanceRasterTo(cycleForLine300);

        var beam = (bus.ReadLong(0x00DFF004) >> 8) & 0x01FF;
        Assert.Equal(300u, beam);
    }

    [Fact]
    public void RasterAdvanceAlternatesLongFrameBitOnPalFrameCadence()
    {
        var bus = new AmigaBus();
        var frameCycle = AmigaConstants.A500PalCpuCyclesPerFrame;

        bus.AdvanceRasterTo(0);
        Assert.Equal(0, bus.ReadWord(0x00DFF004) & 0x8000);

        bus.AdvanceRasterTo(frameCycle);
        Assert.NotEqual(0, bus.ReadWord(0x00DFF004) & 0x8000);

        bus.AdvanceRasterTo(frameCycle * 2);
        Assert.Equal(0, bus.ReadWord(0x00DFF004) & 0x8000);
    }

    private static uint Pixel(uint[] frame, int x, int y)
    {
        return frame[(y * AmigaConstants.PalLowResWidth) + x];
    }

    private static uint HighResPixel(uint[] frame, int width, int x, int y)
    {
        return frame[(y * width) + x];
    }

    private static long CycleForOutputRow(int row, double lineCycles)
    {
        var standardVStart = 0x2C - AmigaConstants.PalLowResOverscanBorderY;
        return (long)Math.Round((standardVStart + row) * lineCycles);
    }

    private static long CycleForOutputRowHorizontal(int row, int horizontal, double lineCycles)
    {
        return CycleForOutputRow(row, lineCycles) + CopperHpToCpuCyclesForTest(horizontal);
    }

    private static long CopperHpToCpuCyclesForTest(int hpUnits)
    {
        return Math.Max(1, hpUnits * AmigaConstants.A500PalCpuCyclesPerColorClock);
    }

    private static (ushort Pos, ushort Ctl) EncodeSpritePosition(int x, int y, int height, bool attached = false)
    {
        var hStart = x + 64 - AmigaConstants.PalLowResOverscanBorderX;
        var vStart = y + (0x2C - AmigaConstants.PalLowResOverscanBorderY);
        var vStop = vStart + height;
        var pos = (ushort)(((vStart & 0xFF) << 8) | ((hStart >> 1) & 0xFF));
        var ctl = (ushort)(((vStop & 0xFF) << 8) |
            (hStart & 0x0001) |
            ((vStop & 0x100) != 0 ? 0x0002 : 0) |
            ((vStart & 0x100) != 0 ? 0x0004 : 0) |
            (attached ? 0x0080 : 0));
        return (pos, ctl);
    }

    private static ushort CopperWaitForOutputRow(int row)
        => (ushort)((((0x2C - AmigaConstants.PalLowResOverscanBorderY) + row) << 8) | 0x0001);

    private static void EnableBlitterDma(AmigaBus bus, long cycle = 0)
    {
        bus.WriteWord(0x00DFF096, 0x8240, cycle);
        bus.AdvanceDmaTo(cycle);
    }

    private static void RunBlitterUntilIdle(AmigaBus bus, long cycle = 100_000)
    {
        bus.AdvanceDmaTo(cycle);
        Assert.False(bus.Blitter.CaptureSnapshot().Busy);
    }

    private static void ConfigureFourWordCopyBlit(AmigaBus bus)
    {
        bus.WriteWord(0x00DFF040, 0x09F0);
        bus.WriteWord(0x00DFF050, 0x0000);
        bus.WriteWord(0x00DFF052, 0x3000);
        bus.WriteWord(0x00DFF054, 0x0000);
        bus.WriteWord(0x00DFF056, 0x4000);
    }

    private static void ConfigureLineBlit(
        AmigaBus bus,
        uint baseAddress,
        ushort rowStride,
        ushort bltcon1,
        ushort texture = 0xFFFF,
        byte minterm = 0xCA,
        short initialAccumulator = 0,
        short aModulo = 0,
        short bModulo = 0,
        short? dModulo = null)
    {
        bus.WriteWord(0x00DFF040, (ushort)(0x0B00 | minterm));
        bus.WriteWord(0x00DFF042, bltcon1);
        bus.WriteWord(0x00DFF048, (ushort)(baseAddress >> 16));
        bus.WriteWord(0x00DFF04A, (ushort)baseAddress);
        bus.WriteWord(0x00DFF050, 0x0000);
        bus.WriteWord(0x00DFF052, unchecked((ushort)initialAccumulator));
        bus.WriteWord(0x00DFF054, (ushort)(baseAddress >> 16));
        bus.WriteWord(0x00DFF056, (ushort)baseAddress);
        bus.WriteWord(0x00DFF060, rowStride);
        bus.WriteWord(0x00DFF062, unchecked((ushort)bModulo));
        bus.WriteWord(0x00DFF064, unchecked((ushort)aModulo));
        bus.WriteWord(0x00DFF066, unchecked((ushort)(dModulo ?? (short)rowStride)));
        bus.WriteWord(0x00DFF072, texture);
        bus.WriteWord(0x00DFF074, 0x8000);
        EnableBlitterDma(bus);
    }

    private static bool IsLinePixelSet(AmigaBus bus, uint baseAddress, int rowStride, int x, int y)
    {
        var wordOffset = Math.DivRem(x, 16, out var bit);
        if (bit < 0)
        {
            bit += 16;
            wordOffset--;
        }

        var address = (int)baseAddress + (y * rowStride) + (wordOffset * 2);
        var word = BigEndian.ReadUInt16(bus.ChipRam, address, "line pixel word");
        return (word & (0x8000 >> bit)) != 0;
    }

    private static void StartDiskDma(AmigaBus bus, uint targetAddress, ushort words, long cycle = 0)
    {
        var readyCycle = PrepareDiskDma(bus, cycle);
        WriteDsklenStartSequence(bus, targetAddress, words, readyCycle);
    }

    private static long PrepareDiskDma(AmigaBus bus, long cycle = 0, int driveIndex = 0)
    {
        SelectDriveAndStartMotor(bus, driveIndex, cycle);
        var readyCycle = GetMotorReadyCycle(bus, driveIndex, cycle);
        bus.AdvanceDmaTo(readyCycle);
        bus.WriteWord(0x00DFF096, 0x8210, readyCycle);
        bus.Paula.AdvanceTo(readyCycle);
        return readyCycle;
    }

    private static void SelectDf0AndStartMotor(AmigaBus bus, long cycle = 0)
    {
        SelectDriveAndStartMotor(bus, 0, cycle);
    }

    private static void SelectDriveAndStartMotor(AmigaBus bus, int driveIndex, long cycle = 0)
    {
        bus.WriteByte(0x00BFD100, 0xFF, cycle);
        bus.WriteByte(0x00BFD300, 0xFF, cycle);
        bus.WriteByte(0x00BFD100, (byte)(0x7F & ~(1 << (driveIndex + 3))), cycle);
    }

    private static long GetMotorReadyCycle(AmigaBus bus, int driveIndex, long cycle = 0)
    {
        var drive = GetDrive(bus, driveIndex);
        var readyCycle = drive.MotorOnCycle + MotorReadyDelayCycles();
        return Math.Max(cycle, readyCycle);
    }

    private static long MotorReadyDelayCycles()
    {
        return Math.Max(1, (long)Math.Round(AmigaConstants.A500PalCpuClockHz * 0.5));
    }

    private static AmigaFloppyDrive GetDrive(AmigaBus bus, int driveIndex)
    {
        return driveIndex switch
        {
            0 => bus.Disk.Drive0,
            1 => bus.Disk.Drive1,
            2 => bus.Disk.Drive2,
            3 => bus.Disk.Drive3,
            _ => throw new ArgumentOutOfRangeException(nameof(driveIndex))
        };
    }

    private static void WriteDsklenStartSequence(AmigaBus bus, uint targetAddress, ushort words, long cycle = 0)
    {
        bus.WriteWord(0x00DFF020, (ushort)(targetAddress >> 16), cycle);
        bus.WriteWord(0x00DFF022, (ushort)targetAddress, cycle);
        var dsklen = (ushort)(0x8000 | words);
        bus.WriteWord(0x00DFF024, dsklen, cycle);
        bus.WriteWord(0x00DFF024, dsklen, cycle);
    }

    private static long CompleteDiskDma(AmigaBus bus, long startCycle, ushort words)
    {
        _ = words;
        var snapshot = bus.Disk.CaptureSnapshot();
        if (!snapshot.ActiveDma)
        {
            return startCycle;
        }

        var completionCycle = snapshot.ActiveDmaCompletionCycle;
        bus.AdvanceCiasTo(completionCycle);
        bus.Paula.AdvanceTo(completionCycle);
        return completionCycle;
    }

    private static long DiskByteCycleCount(int byteCount)
    {
        return (long)Math.Ceiling(
            AmigaConstants.A500PalCpuClockHz / (AmigaDosTrackEncoder.EncodedTrackBytes * 5) * byteCount);
    }

    private static AmigaBus CreateLegacyDiskDisplayBus(
        int expansionRamSize = 0,
        int floppyDriveCount = 1)
    {
        return new AmigaBus(
            expansionRamSize: expansionRamSize,
            floppyDriveCount: floppyDriveCount,
            enableLiveAgnusDma: false,
            agnusTimingMode: AgnusTimingMode.LegacyReservation);
    }

    private static byte[][] CreateSingleWordTrackSet(ushort firstWord)
    {
        var tracks = new byte[AmigaDiskImage.TrackCount][];
        for (var trackIndex = 0; trackIndex < tracks.Length; trackIndex++)
        {
            tracks[trackIndex] = AmigaDosTrackEncoder.CreateUnformattedTrack();
        }

        BigEndian.WriteUInt16(tracks[0], 0, firstWord);
        return tracks;
    }

    private static AmigaEncodedTrack[] CreateUnformattedEncodedTrackSet()
    {
        var tracks = new AmigaEncodedTrack[AmigaDiskImage.TrackCount];
        for (var trackIndex = 0; trackIndex < tracks.Length; trackIndex++)
        {
            tracks[trackIndex] = AmigaEncodedTrack.FromBytes(AmigaDosTrackEncoder.CreateUnformattedTrack());
        }

        return tracks;
    }

    private static AmigaEncodedTrack ShiftTrackBits(byte[] source, int shiftBits)
    {
        var bitLength = source.Length * 8;
        var shifted = new byte[source.Length];
        shiftBits = AmigaEncodedTrack.Mod(shiftBits, bitLength);
        for (var bit = 0; bit < bitLength; bit++)
        {
            if (!ReadBit(source, bit))
            {
                continue;
            }

            WriteBit(shifted, (bit + shiftBits) % bitLength);
        }

        return new AmigaEncodedTrack(shifted, bitLength);
    }

    private static bool ReadBit(ReadOnlySpan<byte> data, int bitOffset)
    {
        return ((data[bitOffset >> 3] >> (7 - (bitOffset & 7))) & 1) != 0;
    }

    private static void WriteBit(Span<byte> data, int bitOffset)
    {
        data[bitOffset >> 3] = (byte)(data[bitOffset >> 3] | (1 << (7 - (bitOffset & 7))));
    }

    private static uint DecodeOddEvenLong(ReadOnlySpan<byte> data, int oddOffset, int evenOffset)
    {
        var odd = BigEndian.ReadUInt32(data, oddOffset, "odd MFM longword");
        var even = BigEndian.ReadUInt32(data, evenOffset, "even MFM longword");
        return (((odd & 0x5555_5555u) << 1) | (even & 0x5555_5555u)) & 0xFFFF_FFFFu;
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

    private static bool ContainsWord(ReadOnlySpan<byte> data, ushort expected)
    {
        for (var offset = 0; offset + 1 < data.Length; offset += 2)
        {
            if (BigEndian.ReadUInt16(data, offset, "encoded track word") == expected)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsWord(AmigaEncodedTrack track, ushort expected)
    {
        for (var bitOffset = 0; bitOffset < track.BitLength; bitOffset++)
        {
            if (track.ReadUInt16(bitOffset) == expected)
            {
                return true;
            }
        }

        return false;
    }

    private static string? TryFindWorkspaceFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
