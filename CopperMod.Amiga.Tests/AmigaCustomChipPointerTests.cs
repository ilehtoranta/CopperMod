using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class AmigaCustomChipPointerTests
{
    [Theory]
    [InlineData(512 * 1024, 0x0007_FFFEu)]
    [InlineData(1024 * 1024, 0x0007_FFFEu)]
    [InlineData(2 * 1024 * 1024, 0x0007_FFFEu)]
    [InlineData(8 * 1024 * 1024, 0x0007_FFFEu)]
    public void CustomChipDmaPointerMaskUsesOcsChipDmaBusWidth(int chipRamSize, uint expectedMask)
    {
        var bus = new AmigaBus(chipRamSize);

        Assert.Equal(expectedMask, bus.ChipDmaAddressMask);
        Assert.Equal(0u, bus.AddChipDmaPointerOffset(expectedMask, 2));
        Assert.Equal(expectedMask, bus.AddChipDmaPointerOffset(0, -2));
    }

    [Theory]
    [InlineData(512 * 1024, 0x0007_FFFEu)]
    [InlineData(1024 * 1024, 0x0007_FFFEu)]
    [InlineData(2 * 1024 * 1024, 0x0007_FFFEu)]
    [InlineData(8 * 1024 * 1024, 0x0007_FFFEu)]
    public void CustomChipPointerRegistersMaskUnusedBitsToOcsChipDmaBusWidth(int chipRamSize, uint expectedPointer)
    {
        var bus = new AmigaBus(chipRamSize);

        bus.WriteWord(0x00DFF0E0, 0x00FF);
        bus.WriteWord(0x00DFF0E2, 0xFFFF);
        bus.Display.RenderFrame(new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight]);
        Assert.Equal(expectedPointer, bus.Display.CaptureSnapshot().BitplanePointers[0]);

        bus.WriteWord(0x00DFF020, 0x00FF);
        bus.WriteWord(0x00DFF022, 0xFFFF);
        var disk = bus.Disk.CaptureSnapshot();
        Assert.Equal(expectedPointer, disk.DiskPointer);
        Assert.Equal((ushort)(expectedPointer >> 16), bus.Disk.ReadWord(0x020));
        Assert.Equal((ushort)(expectedPointer & 0xFFFE), bus.Disk.ReadWord(0x022));

        bus.WriteWord(0x00DFF0A0, 0x00FF);
        bus.WriteWord(0x00DFF0A2, 0xFFFF);
        bus.Paula.AdvanceTo(0);
        Assert.Equal(expectedPointer, bus.Paula.GetChannelSnapshot(0).Location);

        bus.WriteWord(0x00DFF050, 0x00FF);
        bus.WriteWord(0x00DFF052, 0xFFFF);
        bus.WriteWord(0x00DFF058, 0x0041);
        Assert.Equal(expectedPointer, bus.Blitter.CaptureSnapshot().SourceA);
    }

    [Theory]
    [InlineData(512 * 1024)]
    [InlineData(1024 * 1024)]
    [InlineData(2 * 1024 * 1024)]
    [InlineData(8 * 1024 * 1024)]
    public void CopperListPointerWrapsToInstalledChipRamSize(int chipRamSize)
    {
        var bus = new AmigaBus(chipRamSize);
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
    [InlineData(512 * 1024)]
    [InlineData(1024 * 1024)]
    [InlineData(2 * 1024 * 1024)]
    [InlineData(8 * 1024 * 1024)]
    public void SpritePointerWrapsToInstalledChipRamSize(int chipRamSize)
    {
        var bus = new AmigaBus(chipRamSize);
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

    private static uint Pixel(uint[] frame, int x, int y)
    {
        return frame[(y * AmigaConstants.PalLowResWidth) + x];
    }

    private static (ushort Pos, ushort Ctl) EncodeSpritePosition(int x, int y, int height)
    {
        var hStart = x + 128 - AmigaConstants.PalLowResOverscanBorderX;
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
