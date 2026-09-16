using Copper68k;
using Xunit;

namespace CopperMod.Amiga.Lightweight.Tests;

public sealed class LightweightSpriteDmaTests
{
    private const ushort DmaSetMasterAndSprites = 0x8220;

    [Fact]
    public void VerticalBlankPointerRewriteSurvivesUntilPalResetLine()
    {
        using var machine = CreateStoppedMachine();
        const uint oldAddress = 0x2800, nullAddress = 0x2C00;
        SeedWord(machine, oldAddress, 0xAAAA);
        SeedWord(machine, oldAddress + 2, 0xAAAA);
        SeedWord(machine, nullAddress, 0xFE00);
        SeedWord(machine, nullAddress + 2, 0xFF00);
        SetPointer(machine, 2, oldAddress, 0);
        WriteRegister(machine, LightweightRegisters.DmaconWrite, DmaSetMasterAndSprites, 0);

        // Native boot rewrites SPR2PTL while the old implementation already
        // has an illegal line-zero CTL request in flight.
        machine.AdvanceHardwareTo(At(0, 0x21));
        SetPointer(machine, 2, nullAddress, machine.Cycle);
        Assert.Equal(At(25, 0x17), machine.SpriteDmaNextCycle);
        machine.AdvanceHardwareTo(At(25, 0x17) - 1);
        Assert.Equal(nullAddress, machine.GetLiveSpritePointer(2));
        Assert.Equal(long.MinValue, machine.SpriteDmaLastOutputCycle);

        machine.AdvanceHardwareTo(At(25, 0x1F));
        Assert.Equal(nullAddress, machine.SpriteDmaPendingAddress);
        machine.AdvanceHardwareTo(At(25, 0x22));
        Assert.Equal(nullAddress + 4, machine.GetLiveSpritePointer(2));
        Assert.Equal((ushort)0xFE00, machine.GetSpriteDmaPos(2));
        Assert.Equal((ushort)0xFF00, machine.GetSpriteDmaCtl(2));
    }

    [Theory]
    [InlineData(false, 312)]
    [InlineData(true, 313)]
    public void FieldWrapDefersFreshControlUntilPalResetLine(bool longField, int lines)
    {
        using var machine = CreateStoppedMachine();
        WriteRegister(machine, LightweightRegisters.Vposw, longField ? (ushort)0x8000 : (ushort)0, 0);
        const uint address = 0x2800;
        // Active sprite whose stop is beyond either field boundary.
        var (pos, ctl) = EncodePosition(129, 300, 40);
        SeedWord(machine, address, pos);
        SeedWord(machine, address + 2, ctl);
        SetPointer(machine, 0, address, 0);
        WriteRegister(machine, LightweightRegisters.DmaconWrite, DmaSetMasterAndSprites, 0);
        machine.AdvanceHardwareTo(At(lines, 0));
        var priorOutput = machine.SpriteDmaLastOutputCycle;
        SetPointer(machine, 0, address, machine.Cycle);
        Assert.Equal(At(lines + 25, 0x17), machine.SpriteDmaNextCycle);
        machine.AdvanceHardwareTo(At(lines + 25, 0x17) - 1);
        Assert.Equal(address, machine.GetLiveSpritePointer(0));
        Assert.Equal(priorOutput, machine.SpriteDmaLastOutputCycle);
        machine.AdvanceHardwareTo(At(lines + 25, 0x1A));
        Assert.Equal(address + 4, machine.GetLiveSpritePointer(0));
        Assert.Equal(pos, machine.GetSpriteDmaPos(0));
        Assert.Equal(ctl, machine.GetSpriteDmaCtl(0));
    }

    [Fact]
    public void EnableDuringBlankWaitsForResetLineAndAcceptedOutputSurvivesDisable()
    {
        using var machine = CreateStoppedMachine();
        const uint address = 0x3000;
        SeedWord(machine, address, 0xFE00);
        SeedWord(machine, address + 2, 0xFF00);
        SetPointer(machine, 0, address, 0);
        machine.AdvanceHardwareTo(At(24, 100));
        WriteRegister(machine, LightweightRegisters.DmaconWrite, DmaSetMasterAndSprites, machine.Cycle);
        machine.AdvanceHardwareTo(At(25, 0x17) - 1);
        Assert.Equal(long.MinValue, machine.SpriteDmaLastInputCycle);
        machine.AdvanceHardwareTo(At(25, 0x17));
        Assert.Equal(address, machine.SpriteDmaPendingAddress);
        WriteRegister(machine, LightweightRegisters.DmaconWrite, 0x0020, machine.Cycle);
        machine.AdvanceHardwareTo(At(25, 0x18));
        Assert.Equal(address + 2, machine.GetLiveSpritePointer(0));
        Assert.Equal((ushort)0xFE00, machine.GetSpriteDmaPos(0));
        machine.AdvanceHardwareTo(At(26, 0x36));
        Assert.Equal(At(25, 0x18), machine.SpriteDmaLastOutputCycle);
    }

    [Theory]
    [InlineData(-1, 25)]
    [InlineData(0, 26)]
    [InlineData(1, 26)]
    public void EnableAtBlankEndDoesNotReplayAnElapsedAddressPhase(int offset, int firstLine)
    {
        using var machine = CreateStoppedMachine();
        const uint address = 0x3000;
        SeedWord(machine, address, 0xFE00);
        SeedWord(machine, address + 2, 0xFF00);
        var enableCycle = At(25, 0x17) + offset;
        machine.AdvanceHardwareTo(enableCycle);
        SetPointer(machine, 0, address, enableCycle);
        WriteRegister(machine, LightweightRegisters.DmaconWrite, DmaSetMasterAndSprites, enableCycle);
        machine.AdvanceHardwareTo(At(firstLine, 0x17) - 1);
        Assert.Equal(address, machine.GetLiveSpritePointer(0));
        machine.AdvanceHardwareTo(At(firstLine, 0x17));
        Assert.Equal(address, machine.SpriteDmaPendingAddress);
        machine.AdvanceHardwareTo(At(firstLine, 0x18));
        Assert.Equal(address + 2, machine.GetLiveSpritePointer(0));
    }

    [Fact]
    public void BlankEndDeadlineFollowsMixedFieldLengthsAndReset()
    {
        using var machine = CreateStoppedMachine();
        WriteRegister(machine, LightweightRegisters.DmaconWrite, DmaSetMasterAndSprites, 0);
        long start = 0;
        foreach (var lines in new[] { 312, 313, 312 })
        {
            Assert.Equal(start + At(25, 0x17), machine.SpriteDmaNextCycle);
            WriteRegister(machine, LightweightRegisters.Vposw, lines == 313 ? (ushort)0x8000 : (ushort)0, start);
            start += At(lines, 0);
            machine.AdvanceHardwareTo(start);
        }
        Assert.Equal(start + At(25, 0x17), machine.SpriteDmaNextCycle);
        machine.Reset();
        WriteRegister(machine, LightweightRegisters.DmaconWrite, DmaSetMasterAndSprites, 0);
        Assert.Equal(At(25, 0x17), machine.SpriteDmaNextCycle);
    }

    [Theory]
    [InlineData(5, 100, 25)]
    [InlineData(25, 0, 25)]
    [InlineData(26, 100, 27)]
    public void PeripheralResetKeepsTheLiveFieldOrigin(int line, int horizontal, int firstLine)
    {
        using var machine = CreateStoppedMachine();
        WriteRegister(machine, LightweightRegisters.Vposw, 0, 0);
        var fieldStart = At(312, 0);
        var resetCycle = fieldStart + At(line, horizontal);
        machine.AdvanceHardwareTo(resetCycle);
        machine.ResetExternalDevices(resetCycle);
        Assert.Equal(resetCycle, machine.Cycle);
        const uint address = 0x3000;
        SeedWord(machine, address, 0xFE00);
        SeedWord(machine, address + 2, 0xFF00);
        SetPointer(machine, 0, address, resetCycle);
        WriteRegister(machine, LightweightRegisters.DmaconWrite, DmaSetMasterAndSprites, resetCycle);
        var firstInput = fieldStart + At(firstLine, 0x17);
        Assert.Equal(firstInput, machine.SpriteDmaNextCycle);
        machine.AdvanceHardwareTo(firstInput - 1);
        Assert.Equal(address, machine.GetLiveSpritePointer(0));
        machine.AdvanceHardwareTo(firstInput + 2);
        Assert.Equal(address + 2, machine.GetLiveSpritePointer(0));
    }

    [Fact]
    public void AllEightChannelsUseTheirTwoFixedPhysicalSlots()
    {
        using var machine = CreateStoppedMachine();
        for (var channel = 0; channel < 8; channel++)
        {
            var address = 0x2000u + (uint)(channel * 0x20);
            var (pos, ctl) = EncodePosition(129 + channel, 35, height: 1);
            SeedWord(machine, address, pos);
            SeedWord(machine, address + 2, ctl);
            SetPointer(machine, channel, address, cycle: 0);
        }
        WriteRegister(machine, LightweightRegisters.DmaconWrite,
            DmaSetMasterAndSprites, cycle: 0);

        for (var channel = 0; channel < 8; channel++)
        {
            var address = 0x2000u + (uint)(channel * 0x20);
            for (var word = 0; word < 2; word++)
            {
                var horizontal = 0x18 + (channel * 4) + (word * 2);
                machine.AdvanceHardwareTo(At(line: 25, horizontal));

                Assert.Equal(At(25, horizontal - 1), machine.SpriteDmaLastInputCycle);
                Assert.Equal(At(25, horizontal), machine.SpriteDmaLastOutputCycle);
                Assert.Equal(channel, machine.SpriteDmaLastChannel);
                Assert.Equal(word, machine.SpriteDmaLastWord);
                Assert.Equal(address + (uint)(word * 2), machine.SpriteDmaLastAddress);
            }

            Assert.Equal(address + 4, machine.GetLiveSpritePointer(channel));
            Assert.Equal(machine.GetLiveSpritePointer(channel),
                machine.GetSpritePointer(channel));
        }
    }

    [Fact]
    public void MasterAndSprenAreBothRequiredAndLateEnableDoesNotReplay()
    {
        using var machine = CreateStoppedMachine();
        const uint address = 0x2400;
        var (pos, ctl) = EncodePosition(129, 35, height: 1);
        SeedWord(machine, address, pos);
        SeedWord(machine, address + 2, ctl);
        SetPointer(machine, channel: 0, address, cycle: 0);
        WriteRegister(machine, LightweightRegisters.DmaconWrite, 0x8020, cycle: 0);

        machine.AdvanceHardwareTo(At(25, 100));
        Assert.Equal(long.MinValue, machine.SpriteDmaLastOutputCycle);

        var enableCycle = At(25, 100);
        WriteRegister(machine, LightweightRegisters.DmaconWrite, 0x8200, enableCycle);
        machine.AdvanceHardwareTo(At(26, 0x17) - 1);
        Assert.Equal(long.MinValue, machine.SpriteDmaLastInputCycle);

        machine.AdvanceHardwareTo(At(26, 0x17));
        Assert.Equal(At(26, 0x17), machine.SpriteDmaLastInputCycle);
        Assert.Equal(At(26, 0x18), machine.SpriteDmaPendingOutputCycle);
        machine.AdvanceHardwareTo(At(26, 0x18));
        Assert.Equal(address, machine.SpriteDmaLastAddress);
    }

    [Fact]
    public void AcceptedAddressSurvivesPointerWriteAndLaterInputUsesNewPointer()
    {
        using var machine = CreateStoppedMachine();
        const uint firstAddress = 0x2800;
        const uint replacementAddress = 0x2C00;
        var (pos, ctl) = EncodePosition(129, 35, height: 1);
        SeedWord(machine, firstAddress, pos);
        SeedWord(machine, replacementAddress, ctl);
        SetPointer(machine, channel: 0, firstAddress, cycle: 0);
        WriteRegister(machine, LightweightRegisters.DmaconWrite,
            DmaSetMasterAndSprites, cycle: 0);

        machine.AdvanceHardwareTo(At(25, 0x17));
        Assert.Equal(firstAddress, machine.SpriteDmaPendingAddress);
        Assert.Equal(At(25, 0x18), machine.SpriteDmaPendingOutputCycle);

        machine.AdvanceHardwareTo(At(25, 0x17) + 1);
        SetPointer(machine, channel: 0, replacementAddress, machine.Cycle);
        machine.AdvanceHardwareTo(At(25, 0x18));
        Assert.Equal(firstAddress, machine.SpriteDmaLastAddress);
        Assert.Equal(firstAddress + 2, machine.GetLiveSpritePointer(0));

        machine.AdvanceHardwareTo(At(25, 0x18) + 1);
        SetPointer(machine, channel: 0, replacementAddress, machine.Cycle);
        machine.AdvanceHardwareTo(At(25, 0x1A));
        Assert.Equal(replacementAddress, machine.SpriteDmaLastAddress);
        Assert.Equal(replacementAddress + 2, machine.GetLiveSpritePointer(0));
        Assert.Equal(pos, machine.GetSpriteDmaPos(0));
        Assert.Equal(ctl, machine.GetSpriteDmaCtl(0));
    }

    [Fact]
    public void ControlDataAndTerminatorAdvanceOnePhysicalWordAtATime()
    {
        using var machine = CreateStoppedMachine();
        const uint address = 0x3000;
        var (pos, ctl) = EncodePosition(129, y: 27, height: 2);
        SeedWord(machine, address, pos);
        SeedWord(machine, address + 2, ctl);
        SeedWord(machine, address + 4, 0xA001);
        SeedWord(machine, address + 6, 0xB001);
        SeedWord(machine, address + 8, 0xA002);
        SeedWord(machine, address + 10, 0xB002);
        SeedWord(machine, address + 12, 0);
        SeedWord(machine, address + 14, 0);
        SetPointer(machine, channel: 0, address, cycle: 0);
        WriteRegister(machine, LightweightRegisters.DmaconWrite,
            DmaSetMasterAndSprites, cycle: 0);

        machine.AdvanceHardwareTo(At(25, 0x1A));
        Assert.True(machine.IsSpriteDmaActive(0));
        Assert.Equal(address + 4, machine.GetLiveSpritePointer(0));

        machine.AdvanceHardwareTo(At(26, 0x36));
        Assert.Equal(At(25, 0x36), machine.SpriteDmaLastOutputCycle);
        Assert.Equal(address + 4, machine.GetLiveSpritePointer(0));

        machine.AdvanceHardwareTo(At(27, 0x1A));
        Assert.Equal((ushort)0xA001, machine.GetSpriteDmaDataA(0));
        Assert.Equal((ushort)0xB001, machine.GetSpriteDmaDataB(0));
        Assert.Equal(address + 8, machine.GetLiveSpritePointer(0));

        machine.AdvanceHardwareTo(At(28, 0x1A));
        Assert.Equal((ushort)0xA002, machine.GetSpriteDmaDataA(0));
        Assert.Equal((ushort)0xB002, machine.GetSpriteDmaDataB(0));
        Assert.Equal(address + 12, machine.GetLiveSpritePointer(0));

        machine.AdvanceHardwareTo(At(29, 0x1A));
        Assert.True(machine.IsSpriteDmaExhausted(0));
        Assert.False(machine.IsSpriteDmaActive(0));
        Assert.Equal(address + 16, machine.GetLiveSpritePointer(0));
    }

    [Theory]
    [InlineData(0x18, 0, true)]
    [InlineData(0x1C, 1, false)]
    [InlineData(0x20, 2, false)]
    [InlineData(0x30, 7, false)]
    [InlineData(0x38, 8, false)]
    public void EarlyLowResolutionDdfStealsOnlyItsDocumentedSpriteSuffix(
        int ddfStart,
        int fullyAvailableChannels,
        bool spriteZeroPosOnly)
    {
        using var machine = CreateStoppedMachine();
        for (var channel = 0; channel < 8; channel++)
        {
            var address = 0x3800u + (uint)(channel * 0x20);
            var (pos, ctl) = EncodePosition(129 + channel, 45, height: 1);
            SeedWord(machine, address, pos);
            SeedWord(machine, address + 2, ctl);
            SetPointer(machine, channel, address, cycle: 0);
        }

        WriteRegister(machine, LightweightRegisters.Bplcon0, 0x1000, cycle: 0);
        WriteRegister(machine, LightweightRegisters.Ddfstrt, (ushort)ddfStart, cycle: 0);
        WriteRegister(machine, LightweightRegisters.Diwstrt, 0x0000, cycle: 0);
        WriteRegister(machine, LightweightRegisters.Diwstop, 0x0100, cycle: 0);
        WriteRegister(machine, LightweightRegisters.DmaconWrite, 0x8320, cycle: 0);
        machine.AdvanceHardwareTo(At(25, 0x36));

        for (var channel = 0; channel < 8; channel++)
        {
            var initial = 0x3800u + (uint)(channel * 0x20);
            var expectedAdvance = channel < fullyAvailableChannels
                ? 4u
                : spriteZeroPosOnly && channel == 0
                    ? 2u
                    : 0u;
            Assert.Equal(initial + expectedAdvance,
                machine.GetLiveSpritePointer(channel));
        }
    }

    [Fact]
    public void PendingSpriteOutputBlocksCpuCopperAndBlitterAtThatSlot()
    {
        using var machine = CreateStoppedMachine();
        const uint address = 0x4400;
        var (pos, ctl) = EncodePosition(129, 35, height: 1);
        SeedWord(machine, address, pos);
        SeedWord(machine, address + 2, ctl);
        SetPointer(machine, channel: 0, address, cycle: 0);
        WriteRegister(machine, LightweightRegisters.DmaconWrite,
            DmaSetMasterAndSprites, cycle: 0);
        machine.AdvanceHardwareTo(At(25, 0x17));

        var outputCycle = At(25, 0x18);
        Assert.False(machine.CanCopperOwnOutputSlot(outputCycle));
        Assert.False(machine.CanBlitterAdvanceControl(At(25, 0x17)));

        long cpuCycle = outputCycle;
        _ = machine.ReadWord(
            0x1000,
            ref cpuCycle,
            M68kBusAccessKind.CpuDataRead);

        Assert.Equal(At(25, 0x1A), cpuCycle);
        Assert.Equal(At(25, 0x1A), machine.SpriteDmaLastOutputCycle);
        Assert.Equal(address + 2, machine.SpriteDmaLastAddress);
    }

    private static LightweightA500Machine CreateStoppedMachine()
    {
        var machine = new LightweightA500Machine();
        machine.WriteChipWordDma(0x1000, 0x4E72);
        machine.WriteChipWordDma(0x1002, 0x2700);
        machine.Reset();
        return machine;
    }

    private static void SetPointer(
        LightweightA500Machine machine,
        int channel,
        uint address,
        long cycle)
    {
        var high = (ushort)(LightweightRegisters.SpritePointerFirst + (channel * 4));
        WriteRegister(machine, high, (ushort)(address >> 16), cycle);
        WriteRegister(machine, (ushort)(high + 2), (ushort)address, cycle);
    }

    private static void SeedWord(
        LightweightA500Machine machine,
        uint address,
        ushort value)
        => machine.WriteChipWordDma(address, value);

    private static void WriteRegister(
        LightweightA500Machine machine,
        ushort offset,
        ushort value,
        long cycle)
        => machine.WriteCustomRegisterFromCopper(offset, value, cycle);

    private static long At(int line, int horizontal)
        => ((long)line * LightweightClock.CpuCyclesPerLine) +
            ((long)horizontal * LightweightClock.CpuCyclesPerColorClock);

    private static (ushort Pos, ushort Ctl) EncodePosition(
        int x,
        int y,
        int height)
    {
        var horizontal = x - 1;
        var stop = y + height;
        var pos = (ushort)(((y & 0xFF) << 8) | ((horizontal >> 1) & 0xFF));
        var ctl = (ushort)(((stop & 0xFF) << 8) |
            (horizontal & 1) |
            ((stop & 0x100) != 0 ? 0x0002 : 0) |
            ((y & 0x100) != 0 ? 0x0004 : 0));
        return (pos, ctl);
    }
}
