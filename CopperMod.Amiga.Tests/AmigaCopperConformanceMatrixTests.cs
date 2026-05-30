using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class AmigaCopperConformanceMatrixTests
{
    private const uint CopperList = 0x2400;

    public static IEnumerable<object[]> MatrixRows => Rows.Select(row => new object[] { row });

    public static IEnumerable<object[]> ExecutableRows => Rows
        .Where(row => row.Status == MatrixRowStatus.Executable)
        .Select(row => new object[] { row });

    [Fact]
    public void CopperMatrixCoversA500PalOcsFeatureGroups()
    {
        var requiredGroups = new[]
        {
            "move",
            "wait",
            "skip",
            "list-control",
            "dma-control",
            "beam-compare",
            "blitter-wait",
            "interrupts",
            "register-masking",
            "restricted-registers"
        };

        var groups = Rows.Select(row => row.Group).Distinct().ToArray();
        foreach (var required in requiredGroups)
        {
            Assert.Contains(required, groups);
        }

        Assert.All(Rows.Where(row => row.Status == MatrixRowStatus.Pending), row => Assert.False(string.IsNullOrWhiteSpace(row.Reason)));
        Assert.True(Rows.Count(row => row.Status == MatrixRowStatus.Executable) >= 10);
    }

    [Theory]
    [MemberData(nameof(ExecutableRows))]
    public void ExecutableCopperMatrixRowsPass(object rowObject)
    {
        var row = Assert.IsType<MatrixRow>(rowObject);
        switch (row.Name)
        {
            case "MOVE writes custom registers":
                MoveWritesCustomRegisters();
                break;
            case "END stops list execution":
                EndStopsListExecution();
                break;
            case "COP1LC starts display copper list":
                Cop1LcStartsDisplayCopperList();
                break;
            case "COPJMP2 switches to second list":
                CopJmp2SwitchesToSecondList();
                break;
            case "DMAEN and COPEN gate timed copper execution":
                DmaEnableGatesTimedCopperExecution();
                break;
            case "horizontal compare is greater-or-equal":
                HorizontalCompareIsGreaterOrEqual();
                break;
            case "WAIT masks beam bits":
                WaitMasksBeamBits();
                break;
            case "SKIP suppresses the following instruction":
                SkipSuppressesFollowingInstruction();
                break;
            case "BFD set ignores blitter busy":
                BfdSetIgnoresBlitterBusy();
                break;
            case "Copper MOVE can request INTREQ":
                CopperMoveCanRequestIntreq();
                break;
            case "COPxLC high word masks unused DMA bits":
                CopperLocationHighWordMasksUnusedDmaBits();
                break;
            default:
                throw new InvalidOperationException($"No executable assertion is wired for copper row '{row.Name}'.");
        }
    }

    [Theory]
    [MemberData(nameof(MatrixRows))]
    public void PendingCopperRowsDocumentTheirReason(object rowObject)
    {
        var row = Assert.IsType<MatrixRow>(rowObject);
        if (row.Status == MatrixRowStatus.Pending)
        {
            Assert.False(string.IsNullOrWhiteSpace(row.Reason));
        }
    }

    private static readonly MatrixRow[] Rows =
    {
        Executable("move", "MOVE writes custom registers"),
        Executable("move", "END stops list execution"),
        Executable("list-control", "COP1LC starts display copper list"),
        Executable("list-control", "COPJMP2 switches to second list"),
        Executable("dma-control", "DMAEN and COPEN gate timed copper execution"),
        Executable("beam-compare", "horizontal compare is greater-or-equal"),
        Executable("wait", "WAIT masks beam bits"),
        Executable("skip", "SKIP suppresses the following instruction"),
        Executable("blitter-wait", "BFD set ignores blitter busy"),
        Executable("interrupts", "Copper MOVE can request INTREQ"),
        Executable("register-masking", "COPxLC high word masks unused DMA bits"),
        Pending("blitter-wait", "BFD clear waits for live blitter completion cycle", "Needs shared copper/blitter DMA slot timing."),
        Pending("restricted-registers", "Copper danger register protection via COPCON", "Copper register write restrictions are not modelled yet."),
        Pending("dma-control", "cycle slot contention with bitplane and sprite DMA", "Full DMA slot scheduler coverage lives outside the current display renderer.")
    };

    private static void MoveWritesCustomRegisters()
    {
        var bus = new AmigaBus();
        WriteCopperList(bus, CopperList, (0x0180, 0x00F0), (0xFFFF, 0xFFFE));

        new AmigaCopper().ExecuteList(bus, CopperList);
        var frame = RenderLowResFrame(bus);

        Assert.Equal(0xFF00FF00u, frame[0]);
    }

    private static void EndStopsListExecution()
    {
        var bus = new AmigaBus();
        WriteCopperList(bus, CopperList, (0x0180, 0x0F00), (0xFFFF, 0xFFFE), (0x0180, 0x00F0));

        new AmigaCopper().ExecuteList(bus, CopperList);
        var frame = RenderLowResFrame(bus);

        Assert.Equal(0xFFFF0000u, frame[0]);
    }

    private static void Cop1LcStartsDisplayCopperList()
    {
        var bus = new AmigaBus();
        WriteCopperList(bus, CopperList, (0x0180, 0x000F), (0xFFFF, 0xFFFE));
        SetCopperPointer(bus, 1, CopperList);

        var frame = RenderLowResFrame(bus);

        Assert.Equal(0xFF0000FFu, frame[0]);
    }

    private static void CopJmp2SwitchesToSecondList()
    {
        var bus = new AmigaBus();
        WriteCopperList(
            bus,
            CopperList,
            (0x0084, 0x0000),
            (0x0086, 0x2600),
            (0x008A, 0x0000),
            (0x0180, 0x0F00),
            (0xFFFF, 0xFFFE));
        WriteCopperList(bus, 0x2600, (0x0180, 0x00F0), (0xFFFF, 0xFFFE));
        SetCopperPointer(bus, 1, CopperList);

        var frame = RenderLowResFrame(bus);

        Assert.Equal(0xFF00FF00u, frame[0]);
    }

    private static void DmaEnableGatesTimedCopperExecution()
    {
        var bus = new AmigaBus();
        var frameCycles = FrameCycles();
        WriteCopperList(bus, CopperList, (0x0180, 0x0F00), (0xFFFF, 0xFFFE));
        SetCopperPointer(bus, 1, CopperList);

        var frame = RenderLowResFrame(bus, 0, frameCycles);
        Assert.Equal(0xFF000000u, frame[0]);

        bus.WriteWord(0x00DFF096, 0x8280, frameCycles);
        Array.Clear(frame);
        bus.Display.RenderFrame(frame, frameCycles, frameCycles * 2);

        Assert.Equal(0xFFFF0000u, frame[0]);
    }

    private static void HorizontalCompareIsGreaterOrEqual()
    {
        var bus = new AmigaBus();
        WriteCopperList(
            bus,
            CopperList,
            (0x0001, 0x00FE),
            (0x0180, 0x0F00),
            (0x0001, 0x00FE),
            (0x0180, 0x00F0),
            (0x0001, 0x00FE),
            (0xFFFF, 0xFFFE));
        SetCopperPointer(bus, 1, CopperList);

        var frame = RenderLowResFrame(bus);

        Assert.Equal(0xFF00FF00u, Pixel(frame, 0, 0));
    }

    private static void WaitMasksBeamBits()
    {
        var bus = new AmigaBus();
        WriteCopperList(
            bus,
            CopperList,
            (0x0180, 0x0F00),
            (0x2001, 0xFF00),
            (0x0F01, 0x8F00),
            (0x0180, 0x00F0),
            (0xFFFF, 0xFFFE));
        SetCopperPointer(bus, 1, CopperList);

        var frame = RenderLowResFrame(bus);
        var triggerY = 0x2F - (0x2C - AmigaConstants.PalLowResOverscanBorderY);

        Assert.Equal(0xFFFF0000u, Pixel(frame, 0, triggerY - 1));
        Assert.Equal(0xFF00FF00u, Pixel(frame, 0, triggerY));
    }

    private static void SkipSuppressesFollowingInstruction()
    {
        var bus = new AmigaBus();
        WriteCopperList(
            bus,
            CopperList,
            (0x0001, 0x00FF),
            (0x0180, 0x0F00),
            (0x0180, 0x000F),
            (0xFFFF, 0xFFFE));
        SetCopperPointer(bus, 1, CopperList);

        var frame = RenderLowResFrame(bus);

        Assert.Equal(0xFF0000FFu, Pixel(frame, 0, 0));
    }

    private static void BfdSetIgnoresBlitterBusy()
    {
        var bus = new AmigaBus();
        StartLongBlit(bus);
        WriteCopperList(
            bus,
            CopperList,
            (0x0001, 0x80FE),
            (0x0180, 0x0F00),
            (0xFFFF, 0xFFFE));
        SetCopperPointer(bus, 1, CopperList);

        var frame = RenderLowResFrame(bus);

        Assert.True(bus.Blitter.CaptureSnapshot().Busy);
        Assert.Equal(0xFFFF0000u, Pixel(frame, 0, 0));
    }

    private static void CopperMoveCanRequestIntreq()
    {
        var bus = new AmigaBus();
        WriteCopperList(bus, CopperList, (0x009C, (ushort)(0x8000 | AmigaConstants.IntreqCopper)), (0xFFFF, 0xFFFE));
        bus.WriteWord(0x00DFF09A, (ushort)(0xC000 | AmigaConstants.IntreqCopper));
        SetCopperPointer(bus, 1, CopperList);
        _ = RenderLowResFrame(bus);
        bus.Paula.AdvanceTo(0);

        Assert.NotEqual(0, bus.ReadWord(0x00DFF01E) & AmigaConstants.IntreqCopper);
        Assert.Equal(3, bus.Paula.GetHighestPendingInterruptLevel());
    }

    private static void CopperLocationHighWordMasksUnusedDmaBits()
    {
        var bus = new AmigaBus();
        WriteCopperList(bus, 0x0420, (0x0180, 0x0F00), (0xFFFF, 0xFFFE));
        bus.WriteWord(0x00DFF080, 0x0140);
        bus.WriteWord(0x00DFF082, 0x0420);

        var frame = RenderLowResFrame(bus);

        Assert.Equal(0xFFFF0000u, Pixel(frame, 0, 0));
    }

    private static void StartLongBlit(AmigaBus bus)
    {
        bus.WriteWord(0x00DFF040, 0x09F0, 0);
        bus.WriteWord(0x00DFF042, 0x0000, 0);
        bus.WriteWord(0x00DFF050, 0x0000, 0);
        bus.WriteWord(0x00DFF052, 0x3000, 0);
        bus.WriteWord(0x00DFF054, 0x0000, 0);
        bus.WriteWord(0x00DFF056, 0x4000, 0);
        bus.WriteWord(0x00DFF096, 0x8240, 0);
        bus.WriteWord(0x00DFF058, (ushort)((8 << 6) | 8), 0);
    }

    private static void WriteCopperList(AmigaBus bus, uint address, params (ushort First, ushort Second)[] instructions)
    {
        for (var i = 0; i < instructions.Length; i++)
        {
            BigEndian.WriteUInt16(bus.ChipRam, (int)address + (i * 4), instructions[i].First);
            BigEndian.WriteUInt16(bus.ChipRam, (int)address + (i * 4) + 2, instructions[i].Second);
        }
    }

    private static void SetCopperPointer(AmigaBus bus, int list, uint address)
    {
        var highOffset = list == 1 ? 0x00DFF080u : 0x00DFF084u;
        bus.WriteWord(highOffset, (ushort)(address >> 16));
        bus.WriteWord(highOffset + 2, (ushort)address);
    }

    private static uint[] RenderLowResFrame(AmigaBus bus)
    {
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
        bus.Display.RenderFrame(frame);
        return frame;
    }

    private static uint[] RenderLowResFrame(AmigaBus bus, long frameStartCycle, long frameEndCycle)
    {
        var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
        bus.Display.RenderFrame(frame, frameStartCycle, frameEndCycle);
        return frame;
    }

    private static uint Pixel(uint[] frame, int x, int y)
    {
        return frame[(y * AmigaConstants.PalLowResWidth) + x];
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
