using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class AmigaEcsBlitterTests
{
    private const uint SourceA = 0x3000;
    private const uint DestinationD = 0x4000;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyBltSizeKeepsOcsDimensionsAndZeroValueMaxima(bool ecs)
    {
        var bus = CreateBus(ecs);
        bus.WriteWord(0x00DFF05C, 7);

        bus.WriteWord(0x00DFF058, 0x0000);

        var snapshot = bus.Blitter.CaptureSnapshot();
        Assert.True(snapshot.Busy);
        Assert.Equal(64, snapshot.WidthWords);
        Assert.Equal(1024, snapshot.Height);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyBltSizeStillStartsTheSameAreaBlitOnBothAgnusModels(bool ecs)
    {
        var bus = CreateBus(ecs);
        WriteChipWord(bus, SourceA, 0xCAFE);
        ConfigureAreaBlit(bus, bltcon0: 0x09F0, sourceA: SourceA, destinationD: DestinationD);
        EnableBlitterDma(bus);

        bus.WriteWord(0x00DFF058, 0x0041);
        RunUntilIdle(bus);

        Assert.Equal(0xCAFE, ReadChipWord(bus, DestinationD));
        var snapshot = bus.Blitter.CaptureSnapshot();
        Assert.Equal(SourceA + 2, snapshot.SourceA);
        Assert.Equal(DestinationD + 2, snapshot.DestinationD);
    }

    [Fact]
    public void EcsZeroSizeFieldsSelectMaximumBigBlitDimensions()
    {
        var bus = CreateBus(ecs: true);

        bus.WriteWord(0x00DFF05C, 0x0000);
        bus.WriteWord(0x00DFF05E, 0x0000);

        var snapshot = bus.Blitter.CaptureSnapshot();
        Assert.True(snapshot.Busy);
        Assert.Equal(2048, snapshot.WidthWords);
        Assert.Equal(32768, snapshot.Height);
    }

    [Fact]
    public void OcsBigBlitRegistersRemainAbsentAndDoNotRetainSizeState()
    {
        var bus = CreateBus(ecs: false);

        bus.WriteWord(0x00DFF05C, 3);
        bus.WriteWord(0x00DFF05E, 2);
        var openBus = bus.ReadWord(0x00DFF05E);

        var snapshot = bus.Blitter.CaptureSnapshot();
        Assert.Equal(0x0002, openBus);
        Assert.False(snapshot.Busy);
        Assert.Equal(0, snapshot.WidthWords);
        Assert.Equal(0, snapshot.Height);
    }

    [Fact]
    public void LegacyBltSizeReplacesSharedEcsDimensionsUsedByBltSizeH()
    {
        var bus = CreateBus(ecs: true);
        bus.WriteWord(0x00DFF05C, 7);
        bus.WriteWord(0x00DFF058, (3 << 6) | 2);
        RunUntilIdle(bus);

        bus.WriteWord(0x00DFF05E, 4);

        var snapshot = bus.Blitter.CaptureSnapshot();
        Assert.True(snapshot.Busy);
        Assert.Equal(4, snapshot.WidthWords);
        Assert.Equal(3, snapshot.Height);
    }

    [Fact]
    public void EcsHorizontalSizeExtendsPastLegacySixBitWidth()
    {
        const int widthWords = 65;
        var bus = CreateBus(ecs: true);
        ConfigureAreaBlit(bus, bltcon0: 0x0100, destinationD: DestinationD);
        EnableBlitterDma(bus);

        bus.WriteWord(0x00DFF05C, 1);
        bus.WriteWord(0x00DFF05E, widthWords);
        RunUntilIdle(bus);

        var snapshot = bus.Blitter.CaptureSnapshot();
        Assert.Equal(widthWords, snapshot.WidthWords);
        Assert.Equal(DestinationD + (widthWords * 2u), snapshot.DestinationD);
    }

    [Fact]
    public void MidBlitEcsSizeWritesQueueRestartWithCapturedDimensions()
    {
        var bus = CreateBus(ecs: true);
        ConfigureAreaBlit(bus, bltcon0: 0x0100, destinationD: DestinationD);
        EnableBlitterDma(bus);
        bus.WriteWord(0x00DFF058, 0x0042);
        var busyCycle = bus.Blitter.CaptureSnapshot().NextDmaCycle + 1;

        bus.Blitter.WriteRegister(0x05C, 3, busyCycle);
        var activeSnapshot = bus.Blitter.CaptureSnapshot();
        Assert.Equal(2, activeSnapshot.WidthWords);
        Assert.Equal(1, activeSnapshot.Height);
        bus.Blitter.WriteRegister(0x05E, 1, busyCycle);
        activeSnapshot = bus.Blitter.CaptureSnapshot();
        Assert.Equal(2, activeSnapshot.WidthWords);
        Assert.Equal(1, activeSnapshot.Height);
        RunUntilIdle(bus);

        var snapshot = bus.Blitter.CaptureSnapshot();
        Assert.Equal(1, snapshot.WidthWords);
        Assert.Equal(3, snapshot.Height);
        Assert.Equal(DestinationD + 10, snapshot.DestinationD);
    }

    [Fact]
    public void EcsBigBlitAppliesModuloAndPublishesFinalPointers()
    {
        var bus = CreateBus(ecs: true);
        foreach (var (offset, value) in new[]
        {
            (0x00, (ushort)0x1100), (0x02, (ushort)0x1101),
            (0x08, (ushort)0x1200), (0x0A, (ushort)0x1201),
            (0x10, (ushort)0x1300), (0x12, (ushort)0x1301)
        })
        {
            WriteChipWord(bus, SourceA + (uint)offset, value);
        }

        ConfigureAreaBlit(bus, bltcon0: 0x09F0, sourceA: SourceA, destinationD: DestinationD);
        bus.WriteWord(0x00DFF064, 4);
        bus.WriteWord(0x00DFF066, 4);
        EnableBlitterDma(bus);
        bus.WriteWord(0x00DFF05C, 3);
        bus.WriteWord(0x00DFF05E, 2);
        RunUntilIdle(bus);

        Assert.Equal(0x1100, ReadChipWord(bus, DestinationD + 0x00));
        Assert.Equal(0x1101, ReadChipWord(bus, DestinationD + 0x02));
        Assert.Equal(0x1200, ReadChipWord(bus, DestinationD + 0x08));
        Assert.Equal(0x1201, ReadChipWord(bus, DestinationD + 0x0A));
        Assert.Equal(0x1300, ReadChipWord(bus, DestinationD + 0x10));
        Assert.Equal(0x1301, ReadChipWord(bus, DestinationD + 0x12));
        var snapshot = bus.Blitter.CaptureSnapshot();
        Assert.Equal(SourceA + 20, snapshot.SourceA);
        Assert.Equal(DestinationD + 20, snapshot.DestinationD);
    }

    [Fact]
    public void EcsBigBlitDescendingModeSubtractsModuloAndPublishesFinalPointers()
    {
        var bus = CreateBus(ecs: true);
        foreach (var (offset, value) in new[]
        {
            (0x12, (ushort)0x2100), (0x10, (ushort)0x2101),
            (0x0A, (ushort)0x2200), (0x08, (ushort)0x2201)
        })
        {
            WriteChipWord(bus, SourceA + (uint)offset, value);
        }

        ConfigureAreaBlit(
            bus,
            bltcon0: 0x09F0,
            bltcon1: 0x0002,
            sourceA: SourceA + 0x12,
            destinationD: DestinationD + 0x12);
        bus.WriteWord(0x00DFF064, 4);
        bus.WriteWord(0x00DFF066, 4);
        EnableBlitterDma(bus);
        bus.WriteWord(0x00DFF05C, 2);
        bus.WriteWord(0x00DFF05E, 2);
        RunUntilIdle(bus);

        Assert.Equal(0x2100, ReadChipWord(bus, DestinationD + 0x12));
        Assert.Equal(0x2101, ReadChipWord(bus, DestinationD + 0x10));
        Assert.Equal(0x2200, ReadChipWord(bus, DestinationD + 0x0A));
        Assert.Equal(0x2201, ReadChipWord(bus, DestinationD + 0x08));
        var snapshot = bus.Blitter.CaptureSnapshot();
        Assert.Equal(SourceA + 0x06, snapshot.SourceA);
        Assert.Equal(DestinationD + 0x06, snapshot.DestinationD);
    }

    [Fact]
    public void EcsLineModeUsesExtendedVerticalSizeAsLineLength()
    {
        const ushort rowStride = 0x20;
        const int lineLength = 1025;
        var bus = CreateBus(ecs: true);
        bus.WriteWord(0x00DFF040, 0x0BCA);
        bus.WriteWord(0x00DFF042, 0x0001);
        WritePointer(bus, 0x00DFF048, DestinationD);
        WritePointer(bus, 0x00DFF050, 0);
        WritePointer(bus, 0x00DFF054, DestinationD);
        bus.WriteWord(0x00DFF060, rowStride);
        bus.WriteWord(0x00DFF072, 0xFFFF);
        bus.WriteWord(0x00DFF074, 0x8000);
        EnableBlitterDma(bus);

        bus.WriteWord(0x00DFF05C, lineLength);
        bus.WriteWord(0x00DFF05E, 1);
        RunUntilIdle(bus);

        Assert.Equal(0x8000, ReadChipWord(bus, DestinationD));
        Assert.Equal(0x4000, ReadChipWord(bus, DestinationD + rowStride));
        Assert.Equal(0x2000, ReadChipWord(bus, DestinationD + (2 * rowStride)));
        var snapshot = bus.Blitter.CaptureSnapshot();
        Assert.True(snapshot.LineMode);
        Assert.Equal(lineLength, snapshot.Height);
        var finalPointer = DestinationD + ((lineLength - 1) * rowStride) +
            (((lineLength - 1) / 16) * 2u);
        Assert.Equal(finalPointer, snapshot.SourceC);
        Assert.Equal(finalPointer, snapshot.DestinationD);
    }

    private static AmigaBus CreateBus(bool ecs)
        => new(chipset: ecs ? AmigaChipset.EcsPal : AmigaChipset.OcsPal);

    private static void ConfigureAreaBlit(
        AmigaBus bus,
        ushort bltcon0,
        ushort bltcon1 = 0,
        uint sourceA = SourceA,
        uint destinationD = DestinationD)
    {
        bus.WriteWord(0x00DFF040, bltcon0);
        bus.WriteWord(0x00DFF042, bltcon1);
        WritePointer(bus, 0x00DFF050, sourceA);
        WritePointer(bus, 0x00DFF054, destinationD);
    }

    private static void EnableBlitterDma(AmigaBus bus)
    {
        bus.WriteWord(0x00DFF096, 0x8240);
        bus.AdvanceDmaTo(0);
    }

    private static void RunUntilIdle(AmigaBus bus)
    {
        bus.Blitter.AdvanceTo(1_000_000);
        Assert.False(bus.Blitter.CaptureSnapshot().Busy);
    }

    private static void WritePointer(AmigaBus bus, uint highRegisterAddress, uint pointer)
    {
        bus.WriteWord(highRegisterAddress, (ushort)(pointer >> 16));
        bus.WriteWord(highRegisterAddress + 2, (ushort)pointer);
    }

    private static void WriteChipWord(AmigaBus bus, uint address, ushort value)
        => BigEndian.WriteUInt16(bus.ChipRam, (int)address, value);

    private static ushort ReadChipWord(AmigaBus bus, uint address)
        => BigEndian.ReadUInt16(bus.ChipRam, (int)address, "ECS blitter test word");
}
