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

        Assert.Equal(0xFF000000u, frame[63]);
        Assert.Equal(0xFFFF0000u, frame[64]);
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
