using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class PaulaConformanceMatrixTests
{
    public static IEnumerable<object[]> MatrixRows => Rows.Select(row => new object[] { row });

    public static IEnumerable<object[]> ExecutableRows => Rows
        .Where(row => row.Status == MatrixRowStatus.Executable)
        .Select(row => new object[] { row });

    [Fact]
    public void PaulaMatrixCoversA500PalOcsFeatureGroups()
    {
        var requiredGroups = new[]
        {
            "dmacon",
            "adkcon",
            "intena-intreq",
            "audio-registers",
            "manual-audio",
            "audio-dma",
            "interrupts",
            "stereo-routing",
            "modulation",
            "bus-access"
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
    public void ExecutablePaulaMatrixRowsPass(object rowObject)
    {
        var row = Assert.IsType<MatrixRow>(rowObject);
        switch (row.Name)
        {
            case "DMACON set and clear semantics":
                DmaconSetAndClearSemantics();
                break;
            case "DMACON master gates audio DMA":
                DmaconMasterGatesAudioDma();
                break;
            case "ADKCON set and clear semantics":
                AdkconSetAndClearSemantics();
                break;
            case "INTENA gates INTREQ delivery":
                IntenaGatesIntreqDelivery();
                break;
            case "audio registers latch pointer length period and volume":
                AudioRegistersLatchPointerLengthPeriodAndVolume();
                break;
            case "manual audio plays high then low byte":
                ManualAudioPlaysHighThenLowByte();
                break;
            case "manual data can be replaced before low byte":
                ManualDataCanBeReplacedBeforeLowByte();
                break;
            case "audio DMA fetches all four channels":
                AudioDmaFetchesAllFourChannels();
                break;
            case "length-one DMA reloads from original location":
                LengthOneDmaReloadsFromOriginalLocation();
                break;
            case "audio DMA completion requests interrupt":
                AudioDmaCompletionRequestsInterrupt();
                break;
            case "channels 0 and 3 route left, 1 and 2 route right":
                ChannelsRouteToStereoPairs();
                break;
            case "volume attach modulates next channel":
                VolumeAttachModulatesNextChannel();
                break;
            case "period attach modulates next channel":
                PeriodAttachModulatesNextChannel();
                break;
            case "Paula DMA uses named bus request path":
                PaulaDmaUsesNamedBusRequestPath();
                break;
            default:
                throw new InvalidOperationException($"No executable assertion is wired for Paula row '{row.Name}'.");
        }
    }

    [Theory]
    [MemberData(nameof(MatrixRows))]
    public void PendingPaulaRowsDocumentTheirReason(object rowObject)
    {
        var row = Assert.IsType<MatrixRow>(rowObject);
        if (row.Status == MatrixRowStatus.Pending)
        {
            Assert.False(string.IsNullOrWhiteSpace(row.Reason));
        }
    }

    private static readonly MatrixRow[] Rows =
    {
        Executable("dmacon", "DMACON set and clear semantics"),
        Executable("dmacon", "DMACON master gates audio DMA"),
        Executable("adkcon", "ADKCON set and clear semantics"),
        Executable("intena-intreq", "INTENA gates INTREQ delivery"),
        Executable("audio-registers", "audio registers latch pointer length period and volume"),
        Executable("manual-audio", "manual audio plays high then low byte"),
        Executable("manual-audio", "manual data can be replaced before low byte"),
        Executable("audio-dma", "audio DMA fetches all four channels"),
        Executable("audio-dma", "length-one DMA reloads from original location"),
        Executable("interrupts", "audio DMA completion requests interrupt"),
        Executable("stereo-routing", "channels 0 and 3 route left, 1 and 2 route right"),
        Executable("modulation", "volume attach modulates next channel"),
        Executable("modulation", "period attach modulates next channel"),
        Executable("bus-access", "Paula DMA uses named bus request path"),
        Pending("pot-analog", "POT counters and analog paddle timing", "Analog POT circuitry is out of scope for current digital game controls."),
        Pending("serial-output", "Paula serial output shifter", "Out of scope until serial peripherals are emulated."),
        Pending("audio-output", "analog filter and exact DAC reconstruction", "Host audio mixing is digital and not an analog circuit simulation."),
        Pending("disk", "disk DMA and DSKDAT edge cases", "Covered by the disk controller conformance matrix.")
    };

    private static void DmaconSetAndClearSemantics()
    {
        var bus = new AmigaBus();

        bus.WriteWord(0x00DFF096, 0x8201, 0);
        bus.Paula.AdvanceTo(0);
        Assert.Equal(0x0201, bus.ReadWord(0x00DFF002) & 0x0201);

        bus.WriteWord(0x00DFF096, 0x0001, 0);
        bus.Paula.AdvanceTo(0);
        Assert.Equal(0x0200, bus.ReadWord(0x00DFF002) & 0x0201);
    }

    private static void DmaconMasterGatesAudioDma()
    {
        var bus = new AmigaBus();
        ConfigureAudioDma(bus, channel: 0, address: 0x1000, lengthWords: 1, period: 2, volume: 64);
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x7F81);

        bus.WriteWord(0x00DFF096, 0x8001, 0);
        bus.Paula.AdvanceTo(0);

        Assert.False(bus.Paula.GetChannelSnapshot(0).DmaEnabled);
        Assert.DoesNotContain(bus.BusAccesses, access => access.Request.Kind == AmigaBusAccessKind.PaulaDma);

        bus.WriteWord(0x00DFF096, 0x8200, 0);
        bus.Paula.AdvanceTo(0);

        Assert.True(bus.Paula.GetChannelSnapshot(0).DmaEnabled);
        Assert.Contains(bus.BusAccesses, access => access.Request.Kind == AmigaBusAccessKind.PaulaDma);
    }

    private static void AdkconSetAndClearSemantics()
    {
        var bus = new AmigaBus();

        bus.WriteWord(0x00DFF09E, 0x8011, 0);
        bus.Paula.AdvanceTo(0);
        Assert.Equal(0x0011, bus.ReadWord(0x00DFF010));

        bus.WriteWord(0x00DFF09E, 0x0010, 0);
        bus.Paula.AdvanceTo(0);
        Assert.Equal(0x0001, bus.ReadWord(0x00DFF010));
    }

    private static void IntenaGatesIntreqDelivery()
    {
        var bus = new AmigaBus();

        bus.WriteWord(0x00DFF0AA, 0x0102, 0);
        bus.Paula.AdvanceTo(0);
        Assert.Empty(bus.Paula.DrainInterrupts());
        Assert.True((bus.ReadWord(0x00DFF01E) & 0x0080) != 0);

        bus.WriteWord(0x00DFF09C, 0x0080, 0);
        bus.WriteWord(0x00DFF09A, 0xC080, 0);
        bus.WriteWord(0x00DFF0AA, 0x0102, 0);
        bus.Paula.AdvanceTo(0);

        Assert.Single(bus.Paula.DrainInterrupts());
        Assert.Equal(4, bus.Paula.GetHighestPendingInterruptLevel());
    }

    private static void AudioRegistersLatchPointerLengthPeriodAndVolume()
    {
        var bus = new AmigaBus();

        ConfigureAudioDma(bus, channel: 2, address: 0x1234, lengthWords: 3, period: 0, volume: 0x7F);
        var snapshot = bus.Paula.GetChannelSnapshot(2);

        Assert.Equal(0x1234u, snapshot.Location);
        Assert.Equal(3, snapshot.LengthWords);
        Assert.Equal(1, snapshot.Period);
        Assert.Equal(64, snapshot.Volume);
    }

    private static void ManualAudioPlaysHighThenLowByte()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF09A, 0xC080, 0);
        bus.WriteWord(0x00DFF0AA, 0x7F81, 0);
        var buffer = new float[4];

        bus.Paula.RenderSample(0, buffer, 0, 2);
        bus.Paula.RenderSample(856, buffer, 1, 2);

        Assert.True(buffer[0] > 0.20f);
        Assert.True(buffer[2] < -0.20f);
        Assert.Contains(bus.Paula.DrainInterrupts(), interruptEvent => interruptEvent.IntreqBit == 0x0080);
    }

    private static void ManualDataCanBeReplacedBeforeLowByte()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF0A6, 0x0004, 0);
        bus.WriteWord(0x00DFF0AA, 0x7F81, 0);
        bus.WriteWord(0x00DFF0AA, 0x4080, 4);
        var buffer = new float[6];

        bus.Paula.RenderSample(0, buffer, 0, 2);
        bus.Paula.RenderSample(4, buffer, 1, 2);
        bus.Paula.RenderSample(12, buffer, 2, 2);

        Assert.True(buffer[0] > 0.20f);
        Assert.True(buffer[2] > 0.10f);
        Assert.True(buffer[4] < -0.20f);
    }

    private static void AudioDmaFetchesAllFourChannels()
    {
        for (var channel = 0; channel < AmigaConstants.PaulaChannelCount; channel++)
        {
            var bus = new AmigaBus();
            var address = (uint)(0x1000 + (channel * 0x100));
            ConfigureAudioDma(bus, channel, address, lengthWords: 1, period: 2, volume: 64);
            BigEndian.WriteUInt16(bus.ChipRam, (int)address, 0x7F81);

            bus.WriteWord(0x00DFF096, (ushort)(0x8200 | (1 << channel)), 0);
            bus.Paula.AdvanceTo(0);

            var snapshot = bus.Paula.GetChannelSnapshot(channel);
            Assert.True(snapshot.DmaEnabled);
            Assert.Equal(bus.MaskChipDmaAddress(address + 2), snapshot.CurrentAddress);
            Assert.Equal(0x7F81, snapshot.DataWord);
        }
    }

    private static void LengthOneDmaReloadsFromOriginalLocation()
    {
        var bus = new AmigaBus();
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x20E0);
        ConfigureAudioDma(bus, channel: 0, address: 0x1000, lengthWords: 1, period: 1, volume: 64);
        bus.WriteWord(0x00DFF09A, 0xC080, 0);
        bus.WriteWord(0x00DFF096, 0x8201, 0);

        bus.Paula.AdvanceTo(4);
        var snapshot = bus.Paula.GetChannelSnapshot(0);

        Assert.Equal(0x1002u, snapshot.CurrentAddress);
        Assert.Equal(0, snapshot.RemainingWords);
        Assert.True(bus.Paula.DrainInterrupts().Count >= 2);
    }

    private static void AudioDmaCompletionRequestsInterrupt()
    {
        var bus = new AmigaBus();
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x7F81);
        ConfigureAudioDma(bus, channel: 0, address: 0x1000, lengthWords: 1, period: 2, volume: 64);
        bus.WriteWord(0x00DFF09A, 0xC080, 0);

        bus.WriteWord(0x00DFF096, 0x8201, 0);
        bus.Paula.AdvanceTo(0);

        Assert.Contains(bus.Paula.DrainInterrupts(), interruptEvent => interruptEvent.Channel == 0);
        Assert.True((bus.ReadWord(0x00DFF01E) & 0x0080) != 0);
    }

    private static void ChannelsRouteToStereoPairs()
    {
        var leftBus = new AmigaBus();
        leftBus.WriteWord(0x00DFF0AA, 0x7F00, 0);
        var leftBuffer = new float[2];
        leftBus.Paula.RenderSample(0, leftBuffer, 0, 2);
        Assert.True(leftBuffer[0] > 0.20f);
        Assert.Equal(0.0f, leftBuffer[1]);

        var rightBus = new AmigaBus();
        rightBus.WriteWord(0x00DFF0BA, 0x7F00, 0);
        var rightBuffer = new float[2];
        rightBus.Paula.RenderSample(0, rightBuffer, 0, 2);
        Assert.Equal(0.0f, rightBuffer[0]);
        Assert.True(rightBuffer[1] > 0.20f);

        var leftBus3 = new AmigaBus();
        leftBus3.WriteWord(0x00DFF0DA, 0x7F00, 0);
        var left3Buffer = new float[2];
        leftBus3.Paula.RenderSample(0, left3Buffer, 0, 2);
        Assert.True(left3Buffer[0] > 0.20f);
        Assert.Equal(0.0f, left3Buffer[1]);
    }

    private static void VolumeAttachModulatesNextChannel()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF09E, 0x8001, 0);
        bus.WriteWord(0x00DFF0A6, 0x0002, 0);
        bus.WriteWord(0x00DFF0AA, 0x0020, 0);
        var buffer = new float[4];

        bus.Paula.RenderSample(0, buffer, 0, 2);
        bus.Paula.RenderSample(4, buffer, 1, 2);

        Assert.Equal(0.0f, buffer[0]);
        Assert.Equal(32, bus.Paula.GetChannelSnapshot(1).Volume);
    }

    private static void PeriodAttachModulatesNextChannel()
    {
        var bus = new AmigaBus();
        bus.WriteWord(0x00DFF09E, 0x8010, 0);
        bus.WriteWord(0x00DFF0A6, 0x0002, 0);
        bus.WriteWord(0x00DFF0AA, 0x0005, 0);

        bus.Paula.AdvanceTo(4);

        Assert.Equal(5, bus.Paula.GetChannelSnapshot(1).Period);
    }

    private static void PaulaDmaUsesNamedBusRequestPath()
    {
        var bus = new AmigaBus();
        BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x7F81);
        ConfigureAudioDma(bus, channel: 0, address: 0x1000, lengthWords: 1, period: 2, volume: 64);

        bus.WriteWord(0x00DFF096, 0x8201, 0);
        bus.Paula.AdvanceTo(0);

        var dma = Assert.Single(bus.BusAccesses, access => access.Request.Kind == AmigaBusAccessKind.PaulaDma);
        Assert.Equal(AmigaBusRequester.Paula, dma.Request.Requester);
        Assert.Equal(AmigaBusAccessTarget.ChipRam, dma.Request.Target);
        Assert.Equal(AmigaBusAccessSize.Word, dma.Request.Size);
        Assert.Equal(0x1000u, dma.Request.Address);
        Assert.False(dma.Request.IsWrite);
    }

    private static void ConfigureAudioDma(AmigaBus bus, int channel, uint address, ushort lengthWords, ushort period, ushort volume)
    {
        var registerBase = 0x00DFF0A0u + (uint)(channel * 0x10);
        bus.WriteWord(registerBase, (ushort)(address >> 16), 0);
        bus.WriteWord(registerBase + 2, (ushort)address, 0);
        bus.WriteWord(registerBase + 4, lengthWords, 0);
        bus.WriteWord(registerBase + 6, period, 0);
        bus.WriteWord(registerBase + 8, volume, 0);
        bus.Paula.AdvanceTo(0);
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
