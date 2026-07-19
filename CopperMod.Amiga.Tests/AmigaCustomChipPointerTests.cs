using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class AmigaCustomChipPointerTests
{
    private const int StandardX = AmigaConstants.PalLowResOverscanBorderX;
    private const int StandardY = AmigaConstants.PalLowResOverscanBorderY;

    [Theory]
    [InlineData(false, 512 * 1024, 0x000F_FFFEu)]
    [InlineData(false, 1024 * 1024, 0x000F_FFFEu)]
    [InlineData(true, 512 * 1024, 0x001F_FFFEu)]
    [InlineData(true, 1024 * 1024, 0x001F_FFFEu)]
    [InlineData(true, 2 * 1024 * 1024, 0x001F_FFFEu)]
    public void CustomChipDmaPointerMaskUsesSelectedAgnusBusWidth(
        bool ecsAgnus,
        int chipRamSize,
        uint expectedMask)
    {
        var bus = CreateBus(ecsAgnus, chipRamSize);

        Assert.Equal(expectedMask, bus.ChipDmaAddressMask);
        Assert.Equal(0u, bus.AddChipDmaPointerOffset(expectedMask, 2));
        Assert.Equal(expectedMask, bus.AddChipDmaPointerOffset(0, -2));
    }

    [Theory]
    [InlineData(false, 0x0008_0000u, 0x0010_0000u)]
    [InlineData(true, 0x0010_0000u, 0x0020_0000u)]
    public void AgnusDmaWidthRetainsItsTopAddressBitAndDropsTheNext(
        bool ecsAgnus,
        uint retainedBit,
        uint droppedBit)
    {
        var bus = CreateBus(ecsAgnus, 512 * 1024);

        Assert.Equal(retainedBit, bus.MaskChipDmaAddress(retainedBit));
        Assert.Equal(0u, bus.MaskChipDmaAddress(droppedBit));
    }

    [Theory]
    [InlineData(false, 512 * 1024, 0x000F_FFFEu)]
    [InlineData(false, 1024 * 1024, 0x000F_FFFEu)]
    [InlineData(true, 512 * 1024, 0x001F_FFFEu)]
    [InlineData(true, 1024 * 1024, 0x001F_FFFEu)]
    [InlineData(true, 2 * 1024 * 1024, 0x001F_FFFEu)]
    public void EveryCustomChipPointerRegisterUsesSelectedAgnusBusWidth(
        bool ecsAgnus,
        int chipRamSize,
        uint expectedPointer)
    {
        var bus = CreateBus(ecsAgnus, chipRamSize);

        for (var plane = 0; plane < 6; plane++)
        {
            WritePointer(bus, 0x0E0 + (plane * 4));
            Assert.Equal(expectedPointer, bus.AgnusRegisters.GetBitplanePointer(plane));
        }

        for (var sprite = 0; sprite < 8; sprite++)
        {
            WritePointer(bus, 0x120 + (sprite * 4));
            Assert.Equal(expectedPointer, bus.AgnusRegisters.GetSpritePointer(sprite));
        }

        WritePointer(bus, 0x080);
        WritePointer(bus, 0x084);
        Assert.Equal(expectedPointer, bus.AgnusRegisters.CopperListPointer1);
        Assert.Equal(expectedPointer, bus.AgnusRegisters.CopperListPointer2);

        WritePointer(bus, 0x020);
        var disk = bus.Disk.CaptureSnapshot();
        Assert.Equal(expectedPointer, disk.DiskPointer);
        Assert.Equal((ushort)(expectedPointer >> 16), bus.Disk.ReadWord(0x020));
        Assert.Equal((ushort)(expectedPointer & 0xFFFE), bus.Disk.ReadWord(0x022));

        for (var channel = 0; channel < AmigaConstants.PaulaChannelCount; channel++)
        {
            WritePointer(bus, 0x0A0 + (channel * 0x10));
        }

        bus.Paula.AdvanceTo(0);
        for (var channel = 0; channel < AmigaConstants.PaulaChannelCount; channel++)
        {
            Assert.Equal(expectedPointer, bus.Paula.GetChannelSnapshot(channel).Location);
        }

        WritePointer(bus, 0x050);
        WritePointer(bus, 0x04C);
        WritePointer(bus, 0x048);
        WritePointer(bus, 0x054);
        bus.WriteWord(0x00DFF058, 0x0041);
        var blitter = bus.Blitter.CaptureSnapshot();
        Assert.Equal(expectedPointer, blitter.SourceA);
        Assert.Equal(expectedPointer, blitter.SourceB);
        Assert.Equal(expectedPointer, blitter.SourceC);
        Assert.Equal(expectedPointer, blitter.DestinationD);

        bus.Display.RenderFrame(new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight]);
        Assert.All(bus.Display.CaptureSnapshot().BitplanePointers, pointer => Assert.Equal(expectedPointer, pointer));
    }

    [Theory]
    [InlineData(false, 512 * 1024)]
    [InlineData(false, 1024 * 1024)]
    [InlineData(true, 512 * 1024)]
    [InlineData(true, 1024 * 1024)]
    [InlineData(true, 2 * 1024 * 1024)]
    public void CopperDmaAppliesAgnusApertureBeforePhysicalRamMirroring(
        bool ecsAgnus,
        int chipRamSize)
    {
        var bus = CreateBus(ecsAgnus, chipRamSize);
        BigEndian.WriteUInt16(bus.ChipRam, 0x0420, 0x0180);
        BigEndian.WriteUInt16(bus.ChipRam, 0x0422, 0x0F00);
        BigEndian.WriteUInt16(bus.ChipRam, 0x0424, 0xFFFF);
        BigEndian.WriteUInt16(bus.ChipRam, 0x0426, 0xFFFE);
        bus.WriteWord(0x00DFF080, (ushort)(chipRamSize >> 16));
        bus.WriteWord(0x00DFF082, 0x0420);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFFFF0000u, frame[0]);
    }

    [Theory]
    [InlineData(false, 512 * 1024)]
    [InlineData(false, 1024 * 1024)]
    [InlineData(true, 512 * 1024)]
    [InlineData(true, 1024 * 1024)]
    [InlineData(true, 2 * 1024 * 1024)]
    public void SpriteDmaAppliesAgnusApertureBeforePhysicalRamMirroring(
        bool ecsAgnus,
        int chipRamSize)
    {
        var bus = CreateBus(ecsAgnus, chipRamSize);
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF096, 0x8220);
        bus.Paula.AdvanceTo(0);
        bus.WriteWord(0x00DFF1A2, 0x0F00);
        var (pos, ctl) = EncodeSpritePosition(AmigaConstants.PalLowResOverscanBorderX, 30, 1);
        BigEndian.WriteUInt16(bus.ChipRam, 0x0420, pos);
        BigEndian.WriteUInt16(bus.ChipRam, 0x0422, ctl);
        BigEndian.WriteUInt16(bus.ChipRam, 0x0424, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x0426, 0x0000);
        bus.WriteWord(0x00DFF120, (ushort)(chipRamSize >> 16));
        bus.WriteWord(0x00DFF122, 0x0420);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame);

        Assert.Equal(0xFFFF0000u, Pixel(frame, AmigaConstants.PalLowResOverscanBorderX, 30));
    }

    [Fact]
    public void SmallerPhysicalRamMirrorsWithoutNarrowingTheOcsDmaPointer()
    {
        var bus = CreateBus(ecsAgnus: false, 512 * 1024);
        BigEndian.WriteUInt16(bus.ChipRam, 0, 0x1234);

        Assert.Equal(0x0008_0000u, bus.MaskChipDmaAddress(0x0008_0000u));
        Assert.Equal(0x1234, bus.ReadChipWordForPresentation(0x0008_0000u));
        Assert.Equal(
            bus.GetChipRamPhysicalOffset(0),
            bus.GetChipRamPhysicalOffset(0x0018_0000u));
    }

    [Fact]
    public void PresentationHistoryUsesPhysicalOffsetsAcrossCpuAndDmaAliases()
    {
        var bus = new AmigaBus(512 * 1024, captureBusAccesses: false);
        BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x1234);
        var cycle = 20L;

        bus.WriteWord(0x0018_2400, 0x9ABC, ref cycle, AmigaBusAccessKind.CpuDataWrite);

        var reservation = Assert.NotNull(bus.Agnus.CaptureSnapshot().LastFixedDmaReservation);
        Assert.Equal(0x1234, bus.ReadChipWordForPresentation(0x0008_2400, reservation.GrantedCycle - 1));
        Assert.Equal(0x9ABC, bus.ReadChipWordForPresentation(0x0008_2400, reservation.GrantedCycle));
    }

    [Fact]
    public void EcsDmaCanReachTheDistinctUpperMegabyte()
    {
        var bus = CreateBus(ecsAgnus: true, 2 * 1024 * 1024);
        BigEndian.WriteUInt16(bus.ChipRam, 0x000000, 0x1111);
        BigEndian.WriteUInt16(bus.ChipRam, 0x100000, 0x2222);

        Assert.Equal(0x1111, bus.ReadChipWordForPresentation(0x0000_0000u));
        Assert.Equal(0x2222, bus.ReadChipWordForPresentation(0x0010_0000u));
        Assert.Equal(0u, bus.MaskChipDmaAddress(0x0020_0000u));
    }

    [Fact]
    public void BitplanePresentationAndLiveDmaWrapRowsAtTheOcsAperture()
    {
        var presentationBus = CreateBoundaryBitplaneBus(enableLiveDma: false);
        var liveBus = CreateBoundaryBitplaneBus(enableLiveDma: true);
        var expected = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
        var actual = new uint[expected.Length];

        presentationBus.Display.RenderFrame(expected);
        liveBus.Display.RenderFrame(actual);

        Assert.Equal(0xFFFF0000u, Pixel(expected, StandardX, StandardY));
        Assert.Equal(0xFFFF0000u, Pixel(expected, StandardX + 1, StandardY + 1));
        Assert.Equal(Pixel(expected, StandardX, StandardY), Pixel(actual, StandardX, StandardY));
        Assert.Equal(Pixel(expected, StandardX + 1, StandardY + 1), Pixel(actual, StandardX + 1, StandardY + 1));
    }

    private static AmigaBus CreateBus(bool ecsAgnus, int chipRamSize)
        => new(
            chipRamSize,
            chipset: new AmigaChipset(
                ecsAgnus ? AgnusModel.Ecs : AgnusModel.Ocs,
                DeniseModel.Ocs,
                VideoStandard.Pal));

    private static void WritePointer(AmigaBus bus, int registerOffset)
    {
        bus.WriteWord(0x00DFF000u + (uint)registerOffset, 0x00FF);
        bus.WriteWord(0x00DFF002u + (uint)registerOffset, 0xFFFF);
    }

    private static AmigaBus CreateBoundaryBitplaneBus(bool enableLiveDma)
    {
        var bus = new AmigaBus(enableLiveAgnusDma: enableLiveDma);
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF182, 0x0F00);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x0038);
        bus.WriteWord(0x00DFF0E0, 0x000F);
        bus.WriteWord(0x00DFF0E2, 0xFFFE);
        bus.WriteWord(0x00DFF100, 0x1000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x0F_FFFE, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, 0x000000, 0x4000);
        if (enableLiveDma)
        {
            bus.WriteWord(0x00DFF096, 0x8300);
        }

        return bus;
    }

    private static uint Pixel(uint[] frame, int x, int y)
        => frame[(y * AmigaConstants.PalLowResWidth) + x];

    private static (ushort Pos, ushort Ctl) EncodeSpritePosition(int x, int y, int height)
    {
        var hStart = x + 129 - AmigaConstants.PalLowResOverscanBorderX;
        var vStart = y + (0x2C - AmigaConstants.PalLowResOverscanBorderY);
        var vStop = vStart + height;
        var pos = (ushort)(((vStart & 0xFF) << 8) | ((hStart >> 1) & 0xFF));
        var ctl = (ushort)(((vStop & 0xFF) << 8) |
            (hStart & 0x0001) |
            ((vStop & 0x100) != 0 ? 0x0002 : 0) |
            ((vStart & 0x100) != 0 ? 0x0004 : 0));
        return (pos, ctl);
    }
}
