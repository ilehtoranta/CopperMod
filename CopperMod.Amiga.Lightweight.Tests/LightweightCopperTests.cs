using Copper68k;
using Xunit;

namespace CopperMod.Amiga.Lightweight.Tests;

public sealed class LightweightCopperTests
{
    private const uint ListAddress = 0x2000;

    [Fact]
    public void MoveCommitsAtRetainedSecondWordOutputPhase()
    {
        using var machine = new LightweightA500Machine();
        var cycle = StartCopper(
            machine,
            (0x0180, 0x0F00),
            (0xFFFF, 0xFFFE));
        var firstInput = machine.CopperLastInstructionFirstInputCycle;

        Assert.Equal(cycle, firstInput);
        Assert.Equal(firstInput + 2, machine.CopperPendingOutputCycle);

        machine.AdvanceHardwareTo(firstInput + 5);
        Assert.Equal((ushort)0, machine.GetCustomRegister(0x180));

        machine.AdvanceHardwareTo(firstInput + 6);

        Assert.Equal(firstInput + 6, machine.CopperLastMoveCycle);
        Assert.Equal((ushort)0x0F00, machine.GetCustomRegister(0x180));
        Assert.Equal(ListAddress + 4, machine.CopperProgramCounter);
    }

    [Fact]
    public void AcceptedCopperOutputSlotDelaysCpuWithoutBlockingConcurrentInput()
    {
        using var machine = new LightweightA500Machine();
        _ = StartCopper(
            machine,
            (0x0180, 0x0123),
            (0xFFFF, 0xFFFE));
        var outputCycle = machine.CopperPendingOutputCycle;
        var cpuCycle = outputCycle;

        _ = machine.ReadWord(
            0x3000,
            ref cpuCycle,
            M68kBusAccessKind.CpuDataRead);

        Assert.Equal(outputCycle + 4, cpuCycle);
        Assert.Equal(outputCycle + 4, machine.CopperLastOutputCycle);
        Assert.Equal(outputCycle + 2, machine.CopperLastAcceptedInputCycle);
        Assert.Equal((ushort)0x0123, machine.GetCustomRegister(0x180));
    }

    [Fact]
    public void WaitUsesTwoControlInputsThenContinuesAfterLiveBeamMatch()
    {
        using var machine = new LightweightA500Machine();
        _ = StartCopper(
            machine,
            (0x0101, 0xFFFE),
            (0x0180, 0x00F0),
            (0xFFFF, 0xFFFE));
        var lineOne = LightweightClock.CpuCyclesPerLine;

        machine.AdvanceHardwareTo(lineOne + 9);
        Assert.Equal((ushort)0, machine.GetCustomRegister(0x180));

        machine.AdvanceHardwareTo(lineOne + 10);

        Assert.Equal((ushort)0x00F0, machine.GetCustomRegister(0x180));
        Assert.Equal(lineOne + 10, machine.CopperLastMoveCycle);
    }

    [Fact]
    public void SatisfiedSkipSuppressesMoveButDoesNotSkipFollowingWait()
    {
        using var machine = new LightweightA500Machine();
        _ = StartCopper(
            machine,
            (0x0001, 0xFFFF),
            (0x0180, 0x0F00),
            (0x0001, 0xFFFE),
            (0x0180, 0x00F0),
            (0xFFFF, 0xFFFE));

        machine.AdvanceHardwareTo(160);

        Assert.Equal((ushort)0x00F0, machine.GetCustomRegister(0x180));
        Assert.True(machine.CopperWaiting);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JumpInvalidatesDecodeButRetainsAlreadyAcceptedRamOutput(bool readStrobe)
    {
        using var machine = new LightweightA500Machine();
        long cycle = machine.Cycle;
        machine.WriteWord(
            LightweightA500Machine.CustomBase + LightweightRegisters.Cop2lch,
            0,
            ref cycle,
            M68kBusAccessKind.CpuDataWrite);
        machine.WriteWord(
            LightweightA500Machine.CustomBase + LightweightRegisters.Cop2lcl,
            0x3000,
            ref cycle,
            M68kBusAccessKind.CpuDataWrite);
        _ = StartCopper(
            machine,
            (0x0180, 0x0F00),
            (0xFFFF, 0xFFFE));
        var retainedOutput = machine.CopperPendingOutputCycle;
        cycle = machine.Cycle;
        if (readStrobe)
            _ = machine.ReadWord(LightweightA500Machine.CustomBase + LightweightRegisters.Copjmp2,
                ref cycle, M68kBusAccessKind.CpuDataRead);
        else machine.WriteWord(
            LightweightA500Machine.CustomBase + LightweightRegisters.Copjmp2,
            0,
            ref cycle,
            M68kBusAccessKind.CpuDataWrite);

        Assert.Equal(retainedOutput, machine.CopperLastOutputCycle);
        Assert.Equal(0x3000u, machine.CopperProgramCounter & 0x7FFEu);
        Assert.Equal((ushort)0, machine.GetCustomRegister(0x180));
    }

    // OCS PAL: HRM chapter 2, "Copper Loops and Branches and Comparison
    // Enable": VP7 cannot be masked; IR2 bit 15 is BFD, not a compare enable.
    [Theory]
    [InlineData(0xCC01, 0xFF00, 204)]
    [InlineData(0xCC01, 0x7F00, 204)]
    [InlineData(0x8001, 0x8000, 128)]
    [InlineData(0x8001, 0x0000, 128)]
    public void WaitAlwaysComparesVerticalHighBit(int first, int second, int targetLine)
    {
        using var machine = new LightweightA500Machine();
        _ = StartCopper(machine,
            ((ushort)first, (ushort)second),
            (0x0180, 0x00F0),
            (0xFFFF, 0xFFFE));
        var targetCycle = (long)targetLine * LightweightClock.CpuCyclesPerLine;

        machine.AdvanceHardwareTo(targetCycle - 2);
        Assert.Equal((ushort)0, machine.GetCustomRegister(0x180));

        machine.AdvanceHardwareTo(targetCycle + 40);
        Assert.Equal((ushort)0x00F0, machine.GetCustomRegister(0x180));
        Assert.InRange(machine.CopperLastMoveCycle, targetCycle, targetCycle + 40);
    }

    [Theory]
    [InlineData(76, 0xFF01, false)]
    [InlineData(76, 0x7F01, false)]
    [InlineData(204, 0xFF01, true)]
    [InlineData(204, 0x7F01, true)]
    public void SkipAlwaysComparesVerticalHighBit(int line, int second, bool skips)
    {
        using var machine = new LightweightA500Machine();
        machine.AdvanceHardwareTo((long)line * LightweightClock.CpuCyclesPerLine);
        var cycle = StartCopper(machine,
            (0xCC01, (ushort)second),
            (0x0180, 0x00F0),
            (0xFFFF, 0xFFFE));

        machine.AdvanceHardwareTo(cycle + 160);

        Assert.Equal((ushort)(skips ? 0 : 0x00F0), machine.GetCustomRegister(0x180));
    }

    [Theory]
    [InlineData(0x8F00)]
    [InlineData(0x0F00)]
    public void ObservedVerticalHighBitMakesLowerMaskedTargetAlreadySatisfied(int second)
    {
        using var machine = new LightweightA500Machine();
        machine.AdvanceHardwareTo(128L * LightweightClock.CpuCyclesPerLine);
        var cycle = StartCopper(machine,
            (0x0F01, (ushort)second),
            (0x0180, 0x00F0),
            (0xFFFF, 0xFFFE));

        machine.AdvanceHardwareTo(cycle + 160);

        Assert.Equal((ushort)0x00F0, machine.GetCustomRegister(0x180));
        Assert.Equal(128, machine.BeamLine);
    }

    private static long StartCopper(
        LightweightA500Machine machine,
        params (ushort First, ushort Second)[] instructions)
    {
        long cycle = machine.Cycle;
        for (var index = 0; index < instructions.Length; index++)
        {
            machine.WriteWord(
                ListAddress + (uint)(index * 4),
                instructions[index].First,
                ref cycle,
                M68kBusAccessKind.CpuDataWrite);
            machine.WriteWord(
                ListAddress + (uint)(index * 4) + 2,
                instructions[index].Second,
                ref cycle,
                M68kBusAccessKind.CpuDataWrite);
        }

        machine.WriteWord(
            LightweightA500Machine.CustomBase + LightweightRegisters.Cop1lch,
            (ushort)(ListAddress >> 16),
            ref cycle,
            M68kBusAccessKind.CpuDataWrite);
        machine.WriteWord(
            LightweightA500Machine.CustomBase + LightweightRegisters.Cop1lcl,
            (ushort)ListAddress,
            ref cycle,
            M68kBusAccessKind.CpuDataWrite);
        machine.WriteWord(
            LightweightA500Machine.CustomBase + LightweightRegisters.DmaconWrite,
            0x8280,
            ref cycle,
            M68kBusAccessKind.CpuDataWrite);
        machine.WriteWord(
            LightweightA500Machine.CustomBase + LightweightRegisters.Copjmp1,
            0,
            ref cycle,
            M68kBusAccessKind.CpuDataWrite);
        return cycle;
    }
}
