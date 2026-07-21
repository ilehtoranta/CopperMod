using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class CopperWaitControlPipelineTests
{
    [Theory]
    [InlineData(0x6241, 44640)]
    [InlineData(0x6443, 45548)]
    public void IsolatedIncomingRgaUsesCopperControlPhasePolarity(
        ushort wait,
        long expectedWriteCycle)
    {
        var bus = CreateFivePlaneBus();
        const uint copperList = 0x2400;
        WriteCopperList(
            bus,
            copperList,
            (wait, 0xFFFE),
            (0x0180, 0x0F00),
            (0xFFFF, 0xFFFE));
        SetCopperPointer(bus, copperList);
        StartCopper(bus);
        var targetLine = (wait >> 8) + 2;
        bus.AdvanceDmaTo(targetLine * AmigaConstants.A500PalCpuCyclesPerRasterLine);

        var write = Assert.Single(bus.Display.CopperDisplayWrites
            .Where(candidate => candidate.Address == 0x180));
        Assert.Equal(expectedWriteCycle, write.Cycle);
    }

    [Theory]
    [InlineData(0xAA45, 52, 76, 96)]
    [InlineData(0xAC47, 52, 76, 96)]
    public void FivePlaneImmediateWaitRunsPreserveCoordinatePhase(
        ushort wait,
        long firstInterval,
        long secondInterval,
        long thirdInterval)
    {
        var bus = CreateFivePlaneBus();
        const uint copperList = 0x2400;
        WriteCopperList(
            bus,
            copperList,
            (wait, 0xFFFE),
            (0x0180, 0x0F00),
            (wait, 0xFFFE),
            (wait, 0xFFFE),
            (0x0180, 0x0FF0),
            (wait, 0xFFFE),
            (wait, 0xFFFE),
            (wait, 0xFFFE),
            (0x0180, 0x00FF),
            (wait, 0xFFFE),
            (wait, 0xFFFE),
            (wait, 0xFFFE),
            (wait, 0xFFFE),
            (0x0180, 0x0000),
            (0xFFFF, 0xFFFE));
        SetCopperPointer(bus, copperList);
        StartCopper(bus);
        var targetLine = (wait >> 8) + 3;
        bus.AdvanceDmaTo(targetLine * AmigaConstants.A500PalCpuCyclesPerRasterLine);

        var writes = bus.Display.CopperDisplayWrites
            .Where(write => write.Address == 0x180)
            .Take(4)
            .ToArray();
        Assert.Equal(4, writes.Length);
        var intervals = writes
            .Zip(writes.Skip(1), (first, second) => second.Cycle - first.Cycle)
            .ToArray();
        Assert.Equal(new[] { firstInterval, secondInterval, thirdInterval }, intervals);
    }

    private static AmigaBus CreateFivePlaneBus()
    {
        var bus = new AmigaBus(
            captureBusAccesses: true,
            enableLiveAgnusDma: true,
            enableLiveDisplayDma: true);
        bus.WriteWord(0x00DFF08E, 0x2C71);
        bus.WriteWord(0x00DFF090, 0x2CD1);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x00D0);
        bus.WriteWord(0x00DFF100, 0x5200);
        for (var plane = 0; plane < 5; plane++)
        {
            var pointerRegister = 0x00DFF0E0u + ((uint)plane * 4u);
            var pointer = 0x4000u + ((uint)plane * 0x1000u);
            bus.WriteWord(pointerRegister, (ushort)(pointer >> 16));
            bus.WriteWord(pointerRegister + 2, (ushort)pointer);
        }

        return bus;
    }

    private static void StartCopper(AmigaBus bus)
        => bus.WriteWord(0x00DFF096, 0x8380);

    private static void WriteCopperList(
        AmigaBus bus,
        uint address,
        params (ushort First, ushort Second)[] instructions)
    {
        for (var index = 0; index < instructions.Length; index++)
        {
            BigEndian.WriteUInt16(bus.ChipRam, (int)address + (index * 4), instructions[index].First);
            BigEndian.WriteUInt16(bus.ChipRam, (int)address + (index * 4) + 2, instructions[index].Second);
        }
    }

    private static void SetCopperPointer(AmigaBus bus, uint address)
    {
        bus.WriteWord(0x00DFF080, (ushort)(address >> 16));
        bus.WriteWord(0x00DFF082, (ushort)address);
    }
}
