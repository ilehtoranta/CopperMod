using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class AmigaCiaConformanceMatrixTests
{
    public static IEnumerable<object[]> MatrixRows => Rows.Select(row => new object[] { row });

    public static IEnumerable<object[]> ExecutableRows => Rows
        .Where(row => row.Status == MatrixRowStatus.Executable)
        .Select(row => new object[] { row });

    [Fact]
    public void CiaMatrixCoversA500PalOcsFeatureGroups()
    {
        var requiredGroups = new[]
        {
            "reset",
            "ports",
            "icr",
            "timer-a",
            "timer-b",
            "tod",
            "keyboard-serial",
            "disk-port",
            "flag"
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
    public void ExecutableCiaMatrixRowsPass(object rowObject)
    {
        var row = Assert.IsType<MatrixRow>(rowObject);
        switch (row.Name)
        {
            case "reset defaults":
                ResetDefaults();
                break;
            case "ports combine DDR latch and input pins":
                PortsCombineDdrLatchAndInputPins();
                break;
            case "CIA-A port A controls LED/filter and overlay":
                CiaAPortAControlsLedFilterAndOverlay();
                break;
            case "CIA-B port B drives disk control pins":
                CiaBPortBDrivesDiskControlPins();
                break;
            case "ICR masks, reports, and clears pending bits":
                IcrMasksReportsAndClearsPendingBits();
                break;
            case "Timer A continuous underflow":
                TimerAContinuousUnderflow();
                break;
            case "CPU reads advance CIA to access cycle":
                CpuReadsAdvanceCiaToAccessCycle();
                break;
            case "Timer A one-shot stops":
                TimerAOneShotStops();
                break;
            case "force load reloads timer counter":
                ForceLoadReloadsTimerCounter();
                break;
            case "Timer B CPU-cycle mode underflows":
                TimerBCpuCycleModeUnderflows();
                break;
            case "Timer B counts Timer A underflows":
                TimerBCountsTimerAUnderflows();
                break;
            case "TOD increments on PAL vertical and horizontal events":
                TodIncrementsOnPalVerticalAndHorizontalEvents();
                break;
            case "TOD alarm sets ICR":
                TodAlarmSetsIcr();
                break;
            case "keyboard serial data queues CIA-A interrupt":
                KeyboardSerialDataQueuesCiaAInterrupt();
                break;
            case "FLAG pin queues interrupt when enabled":
                FlagPinQueuesInterruptWhenEnabled();
                break;
            default:
                throw new InvalidOperationException($"No executable assertion is wired for CIA row '{row.Name}'.");
        }
    }

    [Theory]
    [MemberData(nameof(MatrixRows))]
    public void PendingCiaRowsDocumentTheirReason(object rowObject)
    {
        var row = Assert.IsType<MatrixRow>(rowObject);
        if (row.Status == MatrixRowStatus.Pending)
        {
            Assert.False(string.IsNullOrWhiteSpace(row.Reason));
        }
    }

    private static readonly MatrixRow[] Rows =
    {
        Executable("reset", "reset defaults"),
        Executable("ports", "ports combine DDR latch and input pins"),
        Executable("ports", "CIA-A port A controls LED/filter and overlay"),
        Executable("disk-port", "CIA-B port B drives disk control pins"),
        Executable("icr", "ICR masks, reports, and clears pending bits"),
        Executable("timer-a", "Timer A continuous underflow"),
        Executable("timer-a", "CPU reads advance CIA to access cycle"),
        Executable("timer-a", "Timer A one-shot stops"),
        Executable("timer-a", "force load reloads timer counter"),
        Executable("timer-b", "Timer B CPU-cycle mode underflows"),
        Executable("timer-b", "Timer B counts Timer A underflows"),
        Executable("tod", "TOD increments on PAL vertical and horizontal events"),
        Executable("tod", "TOD alarm sets ICR"),
        Executable("keyboard-serial", "keyboard serial data queues CIA-A interrupt"),
        Executable("flag", "FLAG pin queues interrupt when enabled"),
        Pending("timer-a", "Timer A CNT external pulse mode", "External CNT pin modelling is outside current keyboard/game/demo inputs."),
        Pending("timer-b", "Timer B CNT and Timer-A-with-CNT modes", "CNT pin level and pulse source are not modelled yet."),
        Pending("tod", "undocumented CIA TICK debounce delays TOD alarm interrupt", "Thread-derived 14-16 E-clock delay needs exact phase modelling before implementation."),
        Pending("keyboard-serial", "full CIA serial output shifter", "A500 keyboard input path is implemented; peripheral serial output is out of scope."),
        Pending("ports", "parallel printer/peripheral handshake breadth", "Out of scope for game/demo-relevant A500 PAL OCS.")
    };

    private static void ResetDefaults()
    {
        var bus = new AmigaBus();

        Assert.Equal(0xFC, bus.CiaA.ReadPortLatch(0));
        Assert.Equal(0x03, bus.CiaA.ReadRegister(0x02));
        Assert.Equal(0x00, bus.CiaB.ReadPortLatch(0));
        Assert.Equal(0x00, bus.CiaA.InterruptMask);
        Assert.Equal(0x00, bus.CiaB.PendingInterrupts);
        Assert.Equal(0x00, bus.CiaA.ReadRegister(0x08));
    }

    private static void PortsCombineDdrLatchAndInputPins()
    {
        var cia = new AmigaCia(AmigaCiaId.B);
        cia.Reset();
        var events = new List<AmigaCiaInterruptEvent>();

        Assert.Equal(0xFF, cia.ReadRegister(1));

        cia.WriteRegister(1, 0x00, 0, events);
        cia.WriteRegister(3, 0x0F, 0, events);

        Assert.Equal(0xF0, cia.ReadRegister(1));
        Assert.Equal(0x00, cia.ReadPortLatch(1));
    }

    private static void CiaAPortAControlsLedFilterAndOverlay()
    {
        var bus = new AmigaBus();
        var rom = new byte[0x40000];
        rom[0] = 0x12;
        bus.MapReadOnlyMemory(0x00FC0000, rom);

        Assert.Equal(0x12, bus.ReadByte(0x00000000));
        bus.WriteByte(0x00BFE001, 0x00, 0);
        Assert.True(bus.AudioFilterEnabled);
        Assert.Equal(0x00, bus.ReadByte(0x00000000));

        bus.WriteByte(0x00BFE001, 0x03, 0);
        Assert.False(bus.AudioFilterEnabled);
        Assert.Equal(0x12, bus.ReadByte(0x00000000));
    }

    private static void CiaBPortBDrivesDiskControlPins()
    {
        var bus = new AmigaBus();

        bus.WriteByte(0x00BFD100, 0x77, 0);
        Assert.False(bus.Disk.CaptureSnapshot().Selected);

        bus.WriteByte(0x00BFD300, 0xFF, 0);
        Assert.True(bus.Disk.CaptureSnapshot().Selected);
    }

    private static void IcrMasksReportsAndClearsPendingBits()
    {
        var bus = new AmigaBus();

        bus.SetCiaInterrupts(AmigaCiaId.B, 0x80 | AmigaCia.TimerAInterruptMask, 10);
        Assert.Empty(bus.DrainCiaInterrupts());
        Assert.Equal(AmigaCia.TimerAInterruptMask, bus.CiaB.PendingInterrupts);
        Assert.Equal(AmigaCia.TimerAInterruptMask, bus.ReadByte(0x00BFDD00));
        Assert.Equal(0x00, bus.ReadByte(0x00BFDD00));

        bus.SetCiaInterrupts(AmigaCiaId.B, 0x80 | AmigaCia.TimerAInterruptMask, 20);
        bus.AbleCiaInterrupts(AmigaCiaId.B, 0x80 | AmigaCia.TimerAInterruptMask, 30);
        var interruptEvent = Assert.Single(bus.DrainCiaInterrupts());
        Assert.Equal(AmigaCiaId.B, interruptEvent.Cia);
        Assert.Equal(AmigaCia.TimerAInterruptMask, interruptEvent.IcrBits);
        Assert.Equal(30, interruptEvent.Cycle);
        Assert.Equal(0x81, bus.ReadByte(0x00BFDD00));
    }

    private static void TimerAContinuousUnderflow()
    {
        var bus = new AmigaBus();

        bus.WriteByte(0x00BFD400, 0x03, 0);
        bus.WriteByte(0x00BFD500, 0x00, 0);
        bus.WriteByte(0x00BFDD00, 0x81, 0);
        bus.WriteByte(0x00BFDE00, 0x11, 0);

        Assert.Equal(40, bus.GetNextCiaInterruptCycle(100));
        bus.AdvanceCiasTo(40);

        var interruptEvent = Assert.Single(bus.DrainCiaInterrupts());
        Assert.Equal(AmigaCia.TimerAInterruptMask, interruptEvent.IcrBits);
        Assert.Equal(40, interruptEvent.Cycle);
        Assert.Equal(70, bus.GetNextCiaInterruptCycle(100));
    }

    private static void CpuReadsAdvanceCiaToAccessCycle()
    {
        var bus = new AmigaBus();
        bus.WriteByte(0x00BFD400, 0x03, 0);
        bus.WriteByte(0x00BFD500, 0x00, 0);
        bus.WriteByte(0x00BFDD00, 0x81, 0);
        bus.WriteByte(0x00BFDE00, 0x11, 0);

        var cycle = 20L;
        Assert.Equal(0x01, bus.ReadByte(0x00BFD400, ref cycle, AmigaBusAccessKind.CpuDataRead));
        Assert.Equal(30, cycle);

        cycle = 30L;
        Assert.Equal(0x81, bus.ReadByte(0x00BFDD00, ref cycle, AmigaBusAccessKind.CpuDataRead));
        Assert.Equal(40, cycle);
    }

    private static void TimerAOneShotStops()
    {
        var bus = new AmigaBus();

        bus.WriteByte(0x00BFD400, 0x02, 0);
        bus.WriteByte(0x00BFD500, 0x00, 0);
        bus.WriteByte(0x00BFDD00, 0x81, 0);
        bus.WriteByte(0x00BFDE00, 0x19, 0);
        bus.AdvanceCiasTo(100);

        var interruptEvent = Assert.Single(bus.DrainCiaInterrupts());
        Assert.Equal(30, interruptEvent.Cycle);
        Assert.Null(bus.GetNextCiaInterruptCycle(1_000));
    }

    private static void ForceLoadReloadsTimerCounter()
    {
        var cia = new AmigaCia(AmigaCiaId.A);
        cia.Reset();
        var events = new List<AmigaCiaInterruptEvent>();
        cia.WriteRegister(0x04, 0x05, 0, events);
        cia.WriteRegister(0x05, 0x00, 0, events);
        cia.WriteRegister(0x0E, 0x01, 0, events);
        cia.AdvanceTo(20, events);

        cia.WriteRegister(0x0E, 0x11, 20, events);

        Assert.Equal(0x05, cia.ReadRegister(0x04));
        Assert.Equal(0x00, cia.ReadRegister(0x05));
    }

    private static void TimerBCpuCycleModeUnderflows()
    {
        var bus = new AmigaBus();

        bus.WriteByte(0x00BFD600, 0x02, 0);
        bus.WriteByte(0x00BFD700, 0x00, 0);
        bus.WriteByte(0x00BFDD00, 0x82, 0);
        bus.WriteByte(0x00BFDF00, 0x11, 0);
        bus.AdvanceCiasTo(30);

        var interruptEvent = Assert.Single(bus.DrainCiaInterrupts());
        Assert.Equal(AmigaCiaId.B, interruptEvent.Cia);
        Assert.Equal(AmigaCia.TimerBInterruptMask, interruptEvent.IcrBits);
    }

    private static void TimerBCountsTimerAUnderflows()
    {
        var bus = new AmigaBus();

        bus.WriteByte(0x00BFD600, 0x02, 0);
        bus.WriteByte(0x00BFD700, 0x00, 0);
        bus.WriteByte(0x00BFDD00, 0x82, 0);
        bus.WriteByte(0x00BFDF00, 0x51, 0);
        bus.WriteByte(0x00BFD400, 0x01, 0);
        bus.WriteByte(0x00BFD500, 0x00, 0);
        bus.WriteByte(0x00BFDE00, 0x11, 0);

        bus.AdvanceCiasTo(29);
        Assert.Empty(bus.DrainCiaInterrupts());

        bus.AdvanceCiasTo(30);
        var interruptEvent = Assert.Single(bus.DrainCiaInterrupts());
        Assert.Equal(AmigaCia.TimerBInterruptMask, interruptEvent.IcrBits);
        Assert.Equal(30, interruptEvent.Cycle);
    }

    private static void TodIncrementsOnPalVerticalAndHorizontalEvents()
    {
        var bus = new AmigaBus();
        var frameCycles = FrameCycles();
        var lineCycles = AmigaConstants.A500PalCpuCyclesPerRasterLine;

        bus.AdvanceRasterTo(lineCycles);
        Assert.Equal(0x01, bus.ReadByte(0x00BFD800));

        bus.AdvanceRasterTo(frameCycles);
        Assert.Equal(0x01, bus.ReadByte(0x00BFE801));
    }

    private static void TodAlarmSetsIcr()
    {
        var cia = new AmigaCia(AmigaCiaId.A);
        cia.Reset();
        var events = new List<AmigaCiaInterruptEvent>();
        cia.WriteRegister(0x0D, 0x80 | AmigaCia.TodInterruptMask, 0, events);
        cia.WriteRegister(0x0F, 0x80, 0, events);
        cia.WriteRegister(0x08, 0x01, 0, events);
        cia.WriteRegister(0x09, 0x00, 0, events);
        cia.WriteRegister(0x0A, 0x00, 0, events);
        cia.WriteRegister(0x0F, 0x00, 0, events);

        cia.IncrementTod(100, events);

        var interruptEvent = Assert.Single(events);
        Assert.Equal(AmigaCia.TodInterruptMask, interruptEvent.IcrBits);
        Assert.Equal(0x84, cia.ReadRegister(0x0D));
    }

    private static void KeyboardSerialDataQueuesCiaAInterrupt()
    {
        var bus = new AmigaBus();
        bus.AbleCiaInterrupts(AmigaCiaId.A, 0x80 | AmigaCia.SerialInterruptMask, 0);

        bus.Keyboard.KeyDown(AmigaRawKey.Return, 100);

        var interruptEvent = Assert.Single(bus.DrainCiaInterrupts());
        Assert.Equal(AmigaCiaId.A, interruptEvent.Cia);
        Assert.Equal(AmigaCia.SerialInterruptMask, interruptEvent.IcrBits);
        var serialData = bus.ReadByte(0x00BFEC01);
        Assert.Equal((byte)AmigaRawKey.Return, AmigaKeyboard.DecodeSerialData(serialData));
    }

    private static void FlagPinQueuesInterruptWhenEnabled()
    {
        var cia = new AmigaCia(AmigaCiaId.B);
        cia.Reset();
        var events = new List<AmigaCiaInterruptEvent>();
        cia.AbleInterrupts(0x80 | AmigaCia.FlagInterruptMask, 0, events);

        cia.PulseFlag(200, events);

        var interruptEvent = Assert.Single(events);
        Assert.Equal(AmigaCiaId.B, interruptEvent.Cia);
        Assert.Equal(AmigaCia.FlagInterruptMask, interruptEvent.IcrBits);
        Assert.Equal(200, interruptEvent.Cycle);
    }

    private static long FrameCycles()
    {
        return AmigaConstants.A500PalCpuCyclesPerFrame;
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
