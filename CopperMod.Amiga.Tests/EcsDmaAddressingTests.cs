using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class EcsDmaAddressingTests
{
    private const uint BoundaryWord = 0x000F_FFFE;
    private const uint UpperMegabyte = 0x0010_0000;
    private const int StandardX = AmigaConstants.PalLowResOverscanBorderX;
    private const int StandardY = AmigaConstants.PalLowResOverscanBorderY;

    [Fact]
    public void CopperInstructionFetchCrossesFromLowerToUpperMegabyte()
    {
        var bus = CreateBus(enableLiveAgnusDma: false);
        WriteChipWord(bus, BoundaryWord, 0x0180);
        WriteChipWord(bus, UpperMegabyte, 0x0F00);
        WriteChipWord(bus, UpperMegabyte + 2, 0xFFFF);
        WriteChipWord(bus, UpperMegabyte + 4, 0xFFFE);

        new AmigaCopper().ExecuteList(bus, BoundaryWord);

        Assert.Contains(bus.CustomRegisterWrites, write => write.Address == 0x0180 && write.Value == 0x0F00);
        Assert.Contains(bus.BusAccesses, access =>
            access.Request.Requester == AmigaBusRequester.Copper &&
            access.Request.Address == BoundaryWord);
        Assert.Contains(bus.BusAccesses, access =>
            access.Request.Requester == AmigaBusRequester.Copper &&
            access.Request.Address == UpperMegabyte);
    }

    [Fact]
    public void BlitterSourceCrossesFromLowerToUpperMegabyteAndPublishesPointer()
    {
        const uint destination = 0x0012_0000;
        var bus = CreateBus(enableLiveAgnusDma: false);
        WriteChipWord(bus, BoundaryWord, 0x1234);
        WriteChipWord(bus, UpperMegabyte, 0x5678);
        WritePointer(bus, 0x050, BoundaryWord);
        WritePointer(bus, 0x054, destination);
        bus.WriteWord(0x00DFF040, 0x09F0);
        bus.WriteWord(0x00DFF042, 0x0000);
        bus.WriteWord(0x00DFF096, 0x8240);
        bus.AdvanceDmaTo(0);

        bus.WriteWord(0x00DFF058, 0x0042);
        bus.AdvanceDmaTo(10_000);

        Assert.Equal(0x1234, ReadChipWord(bus, destination));
        Assert.Equal(0x5678, ReadChipWord(bus, destination + 2));
        Assert.Equal(UpperMegabyte + 2, bus.Blitter.CaptureSnapshot().SourceA);
    }

    [Fact]
    public void AudioDmaCrossesFromLowerToUpperMegabyteAndPublishesCurrentAddress()
    {
        var bus = CreateBus(enableLiveAgnusDma: false, audioDmaMinimumPeriod: 1);
        WriteChipWord(bus, BoundaryWord, 0x7F81);
        WriteChipWord(bus, UpperMegabyte, 0x4080);
        WritePointer(bus, 0x0A0, BoundaryWord);
        bus.WriteWord(0x00DFF0A4, 2, 0);
        bus.WriteWord(0x00DFF0A6, 2, 0);
        bus.WriteWord(0x00DFF0A8, 64, 0);
        bus.WriteWord(0x00DFF096, 0x8201, 0);

        bus.Paula.AdvanceTo(80);

        var dma = bus.BusAccesses
            .Where(access => access.Request.Kind == AmigaBusAccessKind.PaulaDma)
            .Take(2)
            .ToArray();
        Assert.Equal(2, dma.Length);
        Assert.Equal(BoundaryWord, dma[0].Request.Address);
        Assert.Equal(UpperMegabyte, dma[1].Request.Address);
        Assert.Equal(UpperMegabyte + 2, bus.Paula.GetChannelSnapshot(0).CurrentAddress);
    }

    [Fact]
    public void BitplaneFetchCrossesFromLowerToUpperMegabyte()
    {
        var bus = CreateBus(enableLiveAgnusDma: true);
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF182, 0x0F00);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x0040);
        WritePointer(bus, 0x0E0, BoundaryWord);
        bus.WriteWord(0x00DFF100, 0x1000);
        bus.WriteWord(0x00DFF096, 0x8300);
        WriteChipWord(bus, BoundaryWord, 0x8000);
        WriteChipWord(bus, UpperMegabyte, 0x4000);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame, 0, AmigaConstants.A500PalCpuCyclesPerFrame);

        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY));
        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX + 17, StandardY));
        Assert.Contains(bus.BusAccesses, access =>
            access.Request.Requester == AmigaBusRequester.Bitplane &&
            access.Request.Address == BoundaryWord);
        Assert.Contains(bus.BusAccesses, access =>
            access.Request.Requester == AmigaBusRequester.Bitplane &&
            access.Request.Address == UpperMegabyte);
    }

    [Fact]
    public void SpriteControlFetchCrossesFromLowerToUpperMegabyte()
    {
        var bus = CreateBus(enableLiveAgnusDma: true);
        bus.WriteWord(0x00DFF180, 0x0000);
        bus.WriteWord(0x00DFF1A2, 0x0F00);
        bus.WriteWord(0x00DFF096, 0x8220);
        bus.Paula.AdvanceTo(0);
        var (pos, ctl) = EncodeSpritePosition(StandardX, StandardY, 1);
        WriteChipWord(bus, BoundaryWord, pos);
        WriteChipWord(bus, UpperMegabyte, ctl);
        WriteChipWord(bus, UpperMegabyte + 2, 0x8000);
        WriteChipWord(bus, UpperMegabyte + 4, 0x0000);
        WriteChipWord(bus, UpperMegabyte + 6, 0x0000);
        WriteChipWord(bus, UpperMegabyte + 8, 0x0000);
        WritePointer(bus, 0x120, BoundaryWord);
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];

        bus.Display.RenderFrame(frame, 0, AmigaConstants.A500PalCpuCyclesPerFrame);

        Assert.Equal(0xFFFF0000u, Pixel(frame, StandardX, StandardY));
        Assert.Contains(bus.BusAccesses, access =>
            access.Request.Requester == AmigaBusRequester.Sprite &&
            access.Request.Address == BoundaryWord);
        Assert.Contains(bus.BusAccesses, access =>
            access.Request.Requester == AmigaBusRequester.Sprite &&
            access.Request.Address == UpperMegabyte);
    }

    [Fact]
    public void DiskReadDmaCrossesFromLowerToUpperMegabyte()
    {
        var bus = CreateBus(enableLiveAgnusDma: false);
        bus.Disk.Drive0.Insert(AmigaDiskImage.FromEncodedTracks(CreateTrackSet(0x1234, 0x5678)));
        SelectDriveAndStartMotor(bus);
        var readyCycle = Math.Max(1, (long)Math.Round(AmigaConstants.A500PalCpuClockHz * 0.5)) +
            AmigaConstants.A500PalCpuCyclesPerCiaTick;
        bus.AdvanceDmaTo(readyCycle);
        bus.WriteWord(0x00DFF096, 0x8210, readyCycle);
        bus.Paula.AdvanceTo(readyCycle);
        WritePointer(bus, 0x020, BoundaryWord, readyCycle);
        bus.WriteWord(0x00DFF024, 0x8002, readyCycle);
        bus.WriteWord(0x00DFF024, 0x8002, readyCycle);

        var completion = bus.Disk.CaptureSnapshot().ActiveDmaCompletionCycle;
        bus.AdvanceDmaTo(completion);

        Assert.Equal(0x1234, ReadChipWord(bus, BoundaryWord));
        Assert.Equal(0x5678, ReadChipWord(bus, UpperMegabyte));
        Assert.Equal(UpperMegabyte + 2, bus.Disk.CaptureSnapshot().DiskPointer);
    }

    private static AmigaBus CreateBus(bool enableLiveAgnusDma, int audioDmaMinimumPeriod = 1)
        => new(
            chipRamSize: 2 * 1024 * 1024,
            enableLiveAgnusDma: enableLiveAgnusDma,
            audioDmaMinimumPeriod: audioDmaMinimumPeriod,
            chipset: AmigaChipset.EcsPal);

    private static void WritePointer(AmigaBus bus, uint offset, uint address, long cycle = 0)
    {
        bus.WriteWord(0x00DFF000 + offset, (ushort)(address >> 16), cycle);
        bus.WriteWord(0x00DFF002 + offset, (ushort)address, cycle);
    }

    private static void WriteChipWord(AmigaBus bus, uint address, ushort value)
        => BigEndian.WriteUInt16(bus.ChipRam, (int)address, value);

    private static ushort ReadChipWord(AmigaBus bus, uint address)
        => BigEndian.ReadUInt16(bus.ChipRam, (int)address, "ECS DMA boundary word");

    private static uint Pixel(uint[] frame, int x, int y)
        => frame[(y * AmigaConstants.PalLowResWidth) + x];

    private static (ushort Pos, ushort Ctl) EncodeSpritePosition(int x, int y, int height)
    {
        var hStart = x + 129 - AmigaConstants.PalLowResOverscanBorderX;
        var vStart = y + (0x2C - AmigaConstants.PalLowResOverscanBorderY);
        var vStop = vStart + height;
        var pos = (ushort)(((vStart & 0xFF) << 8) | ((hStart >> 1) & 0xFF));
        var ctl = (ushort)(((vStop & 0xFF) << 8) |
            (hStart & 1) |
            ((vStop & 0x100) != 0 ? 0x0002 : 0) |
            ((vStart & 0x100) != 0 ? 0x0004 : 0));
        return (pos, ctl);
    }

    private static void SelectDriveAndStartMotor(AmigaBus bus)
    {
        bus.WriteByte(0x00BFD100, 0xFF, 0);
        bus.WriteByte(0x00BFD300, 0xFF, 0);
        bus.WriteByte(0x00BFD100, 0x77, 0);
    }

    private static AmigaEncodedTrack[] CreateTrackSet(params ushort[] words)
    {
        var bytes = new byte[words.Length * 2];
        for (var index = 0; index < words.Length; index++)
        {
            bytes[index * 2] = (byte)(words[index] >> 8);
            bytes[(index * 2) + 1] = (byte)words[index];
        }

        var track = AmigaEncodedTrack.FromBytes(bytes);
        var tracks = new AmigaEncodedTrack[AmigaDiskImage.TrackCount];
        Array.Fill(tracks, track);
        return tracks;
    }
}
