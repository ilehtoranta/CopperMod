using Copper68k;
using Xunit;

namespace CopperMod.Amiga.Lightweight.Tests;

public sealed class LightweightBitplaneTests
{
    private const int DisplayLine = 1;
    private const uint PlaneBase = 0x1000;

    [Theory]
    [InlineData(0x3C, 0x13C)]
    [InlineData(0x2C, 0x12C)]
    [InlineData(0x80, 0x80)]
    [InlineData(0xF4, 0xF4)]
    public void OcsVerticalDmaStopUsesComplementOfBitSeven(int encodedStop, int stopLine)
    {
        // HRM DIWSTOP: V8 = !V7, independently of DIWSTRT.
        using var machine = new LightweightA500Machine();
        ConfigureLowRes(machine, planeCount: 1, ddfStop: 0x0040);
        long cycle = machine.Cycle;
        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Diwstrt, 0x2C81, ref cycle);
        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Diwstop,
            (ushort)((encodedStop << 8) | 0xC1), ref cycle);
        // Sample before the stop edge (within the PAL field even for stop 316).
        var lastLine = Math.Min(stopLine - 1, 310);
        machine.AdvanceHardwareTo((long)(lastLine + 1) * LightweightClock.CpuCyclesPerLine);
        Assert.Equal(PlaneBase + (uint)((lastLine - 44 + 1) * 4), machine.GetLiveBitplanePointer(0));
        if (stopLine < 313)
        {
            var pointer = machine.GetLiveBitplanePointer(0);
            machine.AdvanceHardwareTo((long)(stopLine + 1) * LightweightClock.CpuCyclesPerLine);
            Assert.Equal(pointer, machine.GetLiveBitplanePointer(0));
        }
    }

    [Fact]
    public void DirectCurrentAndFollowingCckRefreshChecksMatchCanonicalSlotCalculation()
    {
        using var machine = new LightweightA500Machine();
        var colorClocksPerLine = LightweightClock.CpuCyclesPerLine /
            LightweightClock.CpuCyclesPerColorClock;

        for (var horizontal = 0; horizontal < colorClocksPerLine; horizontal++)
        {
            var inputCycle = (long)horizontal *
                LightweightClock.CpuCyclesPerColorClock;
            machine.AdvanceHardwareTo(inputCycle);
            var outputCycle = inputCycle + LightweightClock.CpuCyclesPerColorClock;
            var followingSlotAvailable =
                !LightweightBusArbiter.IsMandatoryRefreshSlot(outputCycle);

            Assert.Equal(
                followingSlotAvailable,
                machine.CanBitplaneOwnOutputSlot(outputCycle));
            Assert.Equal(
                followingSlotAvailable,
                machine.CanCopperOwnOutputSlot(outputCycle));
            Assert.Equal(
                followingSlotAvailable,
                machine.CanSpriteOwnOutputSlot(outputCycle));
            Assert.Equal(
                followingSlotAvailable,
                machine.CanBlitterAdvanceControl(inputCycle));
            Assert.Equal(
                LightweightBusArbiter.IsMandatoryRefreshSlot(inputCycle),
                machine.IsHigherPriorityOutputOwned(inputCycle));
        }
    }

    [Fact]
    public void LowResFetchesUseHrmPlaneOrderAndFollowingCckOutput()
    {
        using var machine = new LightweightA500Machine();
        ConfigureLowRes(machine, planeCount: 6, ddfStop: 0x0040);
        var lineStart = (long)DisplayLine * LightweightClock.CpuCyclesPerLine;
        var expected = new (int Horizontal, int Plane)[]
        {
            (0x3A, 3),
            (0x3B, 5),
            (0x3C, 1),
            (0x3E, 2),
            (0x3F, 4),
            (0x40, 0)
        };

        foreach (var (horizontal, plane) in expected)
        {
            var outputCycle = lineStart +
                ((long)horizontal * LightweightClock.CpuCyclesPerColorClock);
            machine.AdvanceHardwareTo(outputCycle);
            Assert.Equal(outputCycle, machine.BitplaneLastOutputCycle);
            Assert.Equal(plane, machine.BitplaneLastPlane);
            Assert.Equal(PlaneBase + (uint)(plane * 0x100), machine.BitplaneLastAddress);
            Assert.Equal((ushort)(0x8000 >> plane), machine.GetBitplaneDataLatch(plane));
        }
    }

    [Fact]
    public void PointerWriteAfterInputCannotRetargetAcceptedOutput()
    {
        using var machine = new LightweightA500Machine();
        ConfigureLowRes(machine, planeCount: 4, ddfStop: 0x0040);
        var inputCycle = ((long)DisplayLine * LightweightClock.CpuCyclesPerLine) +
            (0x39L * LightweightClock.CpuCyclesPerColorClock);
        machine.AdvanceHardwareTo(inputCycle);
        Assert.Equal(PlaneBase + 0x300u, machine.GetLiveBitplanePointer(3));
        long cpuCycle = inputCycle;

        machine.WriteWord(
            LightweightA500Machine.CustomBase +
                LightweightRegisters.BplPointerFirst + (3u * 4u),
            0x0001,
            ref cpuCycle,
            M68kBusAccessKind.CpuDataWrite);

        Assert.Equal(inputCycle + 2, machine.BitplaneLastOutputCycle);
        Assert.Equal(PlaneBase + 0x300u, machine.BitplaneLastAddress);
        Assert.Equal((ushort)(0x8000 >> 3), machine.GetBitplaneDataLatch(3));
        Assert.Equal(PlaneBase + 0x302u, machine.GetLiveBitplanePointer(3));
    }

    [Fact]
    public void FinalFetchGroupAppliesModuloOncePerPlane()
    {
        using var machine = new LightweightA500Machine();
        ConfigureLowRes(machine, planeCount: 1, ddfStop: 0x0040, modulo1: 6);
        var completion = ((long)DisplayLine * LightweightClock.CpuCyclesPerLine) +
            (0x48L * LightweightClock.CpuCyclesPerColorClock);

        machine.AdvanceHardwareTo(completion);

        Assert.Equal(completion, machine.BitplaneLastOutputCycle);
        Assert.Equal(0, machine.BitplaneLastPlane);
        Assert.Equal(PlaneBase + 10u, machine.GetLiveBitplanePointer(0));
        Assert.False(machine.BitplaneRunActive);
    }

    [Fact]
    public void LateDdfStartPreservesFetchPhaseAcrossRasterlineWrap()
    {
        using var machine = new LightweightA500Machine();
        ConfigureLowRes(
            machine,
            planeCount: 6,
            ddfStop: 0x00FC,
            ddfStart: 0x00D4);
        var wrappedFinalOutput =
            ((long)(DisplayLine + 1) * LightweightClock.CpuCyclesPerLine) +
            LightweightClock.CpuCyclesPerColorClock;

        machine.AdvanceHardwareTo(wrappedFinalOutput);

        Assert.Equal(wrappedFinalOutput, machine.BitplaneLastOutputCycle);
        Assert.Equal(0, machine.BitplaneLastPlane);
        Assert.Equal(PlaneBase + 2, machine.BitplaneLastAddress);
        Assert.False(machine.BitplaneRunActive);
    }

    [Fact]
    public void ConsecutiveBitplaneOutputsDelayCpuUntilFirstFreeSlot()
    {
        using var machine = new LightweightA500Machine();
        ConfigureLowRes(machine, planeCount: 6, ddfStop: 0x0040);
        var firstOutput = ((long)DisplayLine * LightweightClock.CpuCyclesPerLine) +
            (0x3AL * LightweightClock.CpuCyclesPerColorClock);
        long cpuCycle = firstOutput;

        _ = machine.ReadWord(0x3000, ref cpuCycle, M68kBusAccessKind.CpuDataRead);

        Assert.Equal(
            ((long)DisplayLine * LightweightClock.CpuCyclesPerLine) +
                (0x3EL * LightweightClock.CpuCyclesPerColorClock),
            cpuCycle);
        Assert.Equal(2, machine.BitplaneLastPlane);
    }

    [Fact]
    public void BitplaneOutputWinsSharedSlotAndCopperRetriesLaterInput()
    {
        using var machine = new LightweightA500Machine();
        ConfigureLowRes(machine, planeCount: 6, ddfStop: 0x0040);
        StartWaitingCopper(machine);
        var lineStart = (long)DisplayLine * LightweightClock.CpuCyclesPerLine;

        machine.AdvanceHardwareTo(lineStart +
            (0x3BL * LightweightClock.CpuCyclesPerColorClock));

        Assert.Equal(
            lineStart + (0x3BL * LightweightClock.CpuCyclesPerColorClock),
            machine.BitplaneLastOutputCycle);
        Assert.NotEqual(machine.BitplaneLastOutputCycle, machine.CopperLastOutputCycle);

        machine.AdvanceHardwareTo(lineStart +
            (0x3DL * LightweightClock.CpuCyclesPerColorClock));

        Assert.Equal(lineStart + (0x3DL * 2), machine.CopperLastMoveCycle);
        Assert.Equal((ushort)0x0F00, machine.GetCustomRegister(0x180));
    }

    [Fact]
    public void EnablingBitplanesAfterDdfStartWaitsForNextRasterline()
    {
        using var machine = new LightweightA500Machine();
        ConfigureLowRes(machine, planeCount: 1, ddfStop: 0x0040, enableBitplaneDma: false);
        var lineOneStart = (long)DisplayLine * LightweightClock.CpuCyclesPerLine;
        var enableCycle = lineOneStart + (0x40L * 2);
        machine.AdvanceHardwareTo(enableCycle);
        long cpuCycle = enableCycle;

        machine.WriteWord(
            LightweightA500Machine.CustomBase + LightweightRegisters.DmaconWrite,
            0x8100,
            ref cpuCycle,
            M68kBusAccessKind.CpuDataWrite);
        machine.AdvanceHardwareTo(lineOneStart + LightweightClock.CpuCyclesPerLine - 1);
        Assert.Equal(long.MinValue, machine.BitplaneLastOutputCycle);

        var nextLineFirstPlaneOutput = lineOneStart + LightweightClock.CpuCyclesPerLine +
            (0x40L * LightweightClock.CpuCyclesPerColorClock);
        machine.AdvanceHardwareTo(nextLineFirstPlaneOutput);

        Assert.Equal(nextLineFirstPlaneOutput, machine.BitplaneLastOutputCycle);
        Assert.Equal(0, machine.BitplaneLastPlane);
    }

    [Fact]
    public void InactiveBitplanesPublishTheNextDdfComparatorPhase()
    {
        using var machine = new LightweightA500Machine();
        ConfigureLowRes(machine, planeCount: 1, ddfStop: 0x0040);
        var lineStart = (long)DisplayLine * LightweightClock.CpuCyclesPerLine;
        machine.AdvanceHardwareTo(
            lineStart + (0x19L * LightweightClock.CpuCyclesPerColorClock));

        Assert.False(machine.BitplaneRunActive);
        Assert.Equal(
            lineStart + (0x38L * LightweightClock.CpuCyclesPerColorClock),
            machine.BitplaneNextCycle);
    }

    [Fact]
    public void DmaDisableRetainsAcceptedOutputAndAbortsFutureFetches()
    {
        using var machine = new LightweightA500Machine();
        ConfigureLowRes(machine, planeCount: 1, ddfStop: 0x00D0);
        var lineStart = (long)DisplayLine * LightweightClock.CpuCyclesPerLine;
        var inputCycle = lineStart + (0x3FL * 2);
        machine.AdvanceHardwareTo(inputCycle);
        Assert.Equal(inputCycle + 2, machine.BitplanePendingOutputCycle);
        long cpuCycle = inputCycle;

        machine.WriteWord(
            LightweightA500Machine.CustomBase + LightweightRegisters.DmaconWrite,
            0x0100,
            ref cpuCycle,
            M68kBusAccessKind.CpuDataWrite);

        Assert.Equal(inputCycle + 2, machine.BitplaneLastOutputCycle);
        Assert.Equal(PlaneBase + 2, machine.GetLiveBitplanePointer(0));
        Assert.False(machine.BitplaneRunActive);
        machine.AdvanceHardwareTo(lineStart + (0x80L * 2));
        Assert.Equal(inputCycle + 2, machine.BitplaneLastOutputCycle);
    }

    private static void ConfigureLowRes(
        LightweightA500Machine machine,
        int planeCount,
        ushort ddfStop,
        short modulo1 = 0,
        bool enableBitplaneDma = true,
        ushort ddfStart = 0x0038)
    {
        long cycle = machine.Cycle;
        for (var plane = 0; plane < planeCount; plane++)
        {
            var pointer = PlaneBase + (uint)(plane * 0x100);
            Write(machine, pointer, (ushort)(0x8000 >> plane), ref cycle);
            Write(machine,
                LightweightA500Machine.CustomBase +
                    LightweightRegisters.BplPointerFirst + (uint)(plane * 4),
                (ushort)(pointer >> 16),
                ref cycle);
            Write(machine,
                LightweightA500Machine.CustomBase +
                    LightweightRegisters.BplPointerFirst + (uint)(plane * 4) + 2,
                (ushort)pointer,
                ref cycle);
        }

        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Diwstrt, 0x0100, ref cycle);
        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Diwstop, 0x0300, ref cycle);
        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Ddfstrt, ddfStart, ref cycle);
        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Ddfstop, ddfStop, ref cycle);
        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Bpl1mod, unchecked((ushort)modulo1), ref cycle);
        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Bplcon0, (ushort)(planeCount << 12), ref cycle);
        Write(
            machine,
            LightweightA500Machine.CustomBase + LightweightRegisters.DmaconWrite,
            enableBitplaneDma ? (ushort)0x8300 : (ushort)0x8200,
            ref cycle);
        machine.Cpu.Cycles = Math.Max(machine.Cpu.Cycles, cycle);
    }

    private static void StartWaitingCopper(LightweightA500Machine machine)
    {
        const uint list = 0x2800;
        long cycle = machine.Cycle;
        Write(machine, list + 0, 0x0139, ref cycle);
        Write(machine, list + 2, 0xFFFE, ref cycle);
        Write(machine, list + 4, 0x0180, ref cycle);
        Write(machine, list + 6, 0x0F00, ref cycle);
        Write(machine, list + 8, 0xFFFF, ref cycle);
        Write(machine, list + 10, 0xFFFE, ref cycle);
        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Cop1lch, 0, ref cycle);
        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Cop1lcl, (ushort)list, ref cycle);
        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.DmaconWrite, 0x8280, ref cycle);
        Write(machine, LightweightA500Machine.CustomBase + LightweightRegisters.Copjmp1, 0, ref cycle);
    }

    private static void Write(
        LightweightA500Machine machine,
        uint address,
        ushort value,
        ref long cycle)
        => machine.WriteWord(address, value, ref cycle, M68kBusAccessKind.CpuDataWrite);
}
