using Copper68k;
using Xunit;

namespace CopperMod.Amiga.Lightweight.Tests;

public sealed class LightweightBlitterTests
{
    private const uint SourceA = 0x10000;
    private const uint SourceB = 0x12000;
    private const uint SourceC = 0x14000;
    private const uint DestinationD = 0x16000;
    private const long QuietStartCycle =
        LightweightClock.CpuCyclesPerLine + (80 * LightweightClock.CpuCyclesPerColorClock);

    [Fact]
    public void AreaReadAndWriteCommitAtRetainedFollowingOutputPhases()
    {
        using var machine = new LightweightA500Machine();
        WriteWord(machine, SourceA, 0xCAFE);
        WriteWord(machine, DestinationD, 0x1234);
        ConfigureArea(machine, bltcon0: 0x09F0);
        EnableBlitter(machine, nasty: true);
        var start = StartBlit(machine, 0x0041);

        machine.AdvanceHardwareTo(start + 6);
        Assert.Equal(start + 8, machine.BlitterPendingOutputCycle);
        Assert.Equal(SourceA, machine.GetBlitterPointer(LightweightRegisters.Bltapth));
        Assert.Equal((ushort)0x1234, ReadWord(machine, DestinationD));

        machine.AdvanceHardwareTo(start + 8);
        Assert.Equal(SourceA + 2, machine.GetBlitterPointer(LightweightRegisters.Bltapth));
        Assert.Equal(start + 10, machine.BlitterPendingOutputCycle);
        Assert.Equal((ushort)0x1234, ReadWord(machine, DestinationD));

        machine.AdvanceHardwareTo(start + 10);
        Assert.Equal((ushort)0xCAFE, ReadWord(machine, DestinationD));
        Assert.Equal(DestinationD + 2, machine.GetBlitterPointer(LightweightRegisters.Bltdpth));
        Assert.False(machine.BlitterStatusBusy);
        Assert.False(machine.BlitterZero);
        Assert.Equal(start + 10, machine.BlitterLastCompletionCycle);
        Assert.Equal(start + 10, machine.BlitterLastTerminationCycle);
        Assert.NotEqual(0, machine.Intreq & 0x0040);
    }

    [Fact]
    public void AllEnabledSourcesCompleteInAThenBThenCThenDOrder()
    {
        using var machine = new LightweightA500Machine();
        WriteWord(machine, SourceA, 0xFF00);
        WriteWord(machine, SourceB, 0x0F0F);
        WriteWord(machine, SourceC, 0x3333);
        ConfigureArea(machine, bltcon0: 0x0FCA);
        EnableBlitter(machine, nasty: true);
        var start = StartBlit(machine, 0x0041);

        machine.AdvanceHardwareTo(start + 8);
        Assert.Equal(SourceA + 2, machine.GetBlitterPointer(LightweightRegisters.Bltapth));
        Assert.Equal(SourceB, machine.GetBlitterPointer(LightweightRegisters.Bltbpth));
        machine.AdvanceHardwareTo(start + 10);
        Assert.Equal(SourceB + 2, machine.GetBlitterPointer(LightweightRegisters.Bltbpth));
        Assert.Equal(SourceC, machine.GetBlitterPointer(LightweightRegisters.Bltcpth));
        machine.AdvanceHardwareTo(start + 12);
        Assert.Equal(SourceC + 2, machine.GetBlitterPointer(LightweightRegisters.Bltcpth));
        Assert.Equal((ushort)0, ReadWord(machine, DestinationD));
        machine.AdvanceHardwareTo(start + 14);

        Assert.Equal(
            ApplyMinterm(0xCA, 0xFF00, 0x0F0F, 0x3333),
            ReadWord(machine, DestinationD));
        Assert.Equal(start + 14, machine.BlitterLastOutputCycle);
    }

    [Fact]
    public void AreaMintermTruthTableAndEveryChannelCombinationUseLiveOrLatchedInputs()
    {
        const ushort memoryA = 0xFF00;
        const ushort memoryB = 0x0F0F;
        const ushort memoryC = 0x3333;
        const ushort latchedA = 0x00FF;
        const ushort latchedB = 0xF0F0;
        const ushort latchedC = 0xCCCC;
        const ushort sentinel = 0x5A5A;

        for (var minterm = 0; minterm < 256; minterm++)
        {
            using var machine = new LightweightA500Machine();
            WriteWord(machine, SourceA, memoryA);
            WriteWord(machine, SourceB, memoryB);
            WriteWord(machine, SourceC, memoryC);
            ConfigureArea(machine, (ushort)(0x0F00 | minterm));
            EnableBlitter(machine, nasty: true);
            _ = StartBlit(machine, 0x0041);
            AdvanceUntilBlitterIdle(machine);
            Assert.Equal(
                ApplyMinterm((byte)minterm, memoryA, memoryB, memoryC),
                ReadWord(machine, DestinationD));
        }

        for (var channels = 0; channels < 16; channels++)
        {
            using var machine = new LightweightA500Machine();
            WriteWord(machine, SourceA, memoryA);
            WriteWord(machine, SourceB, memoryB);
            WriteWord(machine, SourceC, memoryC);
            WriteWord(machine, DestinationD, sentinel);
            ConfigureArea(machine, (ushort)((channels << 8) | 0x00CA));
            WriteRegister(machine, LightweightRegisters.Bltadat, latchedA);
            WriteRegister(machine, LightweightRegisters.Bltbdat, latchedB);
            WriteRegister(machine, LightweightRegisters.Bltcdat, latchedC);
            EnableBlitter(machine, nasty: true);
            _ = StartBlit(machine, 0x0041);
            AdvanceUntilBlitterIdle(machine);

            var a = (channels & 8) != 0 ? memoryA : latchedA;
            var b = (channels & 4) != 0 ? memoryB : latchedB;
            var c = (channels & 2) != 0 ? memoryC : latchedC;
            var expected = (channels & 1) != 0
                ? ApplyMinterm(0xCA, a, b, c)
                : sentinel;
            Assert.Equal(expected, ReadWord(machine, DestinationD));
        }
    }

    [Fact]
    public void MasksShiftsPointersAndRowModulosFollowAreaWordBoundaries()
    {
        using var machine = new LightweightA500Machine();
        var sourceWords = new ushort[]
        {
            0xFFFF, 0xFFFF, 0xFFFF,
            0x1111, 0x2222,
            0x1234, 0x5678, 0x9ABC
        };
        for (var word = 0; word < sourceWords.Length; word++)
        {
            WriteWord(machine, SourceA + (uint)(word * 2), sourceWords[word]);
        }
        ConfigureArea(machine, bltcon0: 0x49F0);
        WriteRegister(machine, LightweightRegisters.Bltafwm, 0x0F0F);
        WriteRegister(machine, LightweightRegisters.Bltalwm, 0xF0F0);
        WriteRegister(machine, LightweightRegisters.Bltamod, 4);
        WriteRegister(machine, LightweightRegisters.Bltdmod, 4);
        EnableBlitter(machine, nasty: true);
        _ = StartBlit(machine, (ushort)((2 << 6) | 3));
        AdvanceUntilBlitterIdle(machine);

        ushort previous = 0;
        var expected = new ushort[6];
        var sourceIndexes = new[] { 0, 1, 2, 5, 6, 7 };
        for (var word = 0; word < expected.Length; word++)
        {
            var x = word % 3;
            var mask = x == 0 ? (ushort)0x0F0F : x == 2 ? (ushort)0xF0F0 : (ushort)0xFFFF;
            var current = (ushort)(sourceWords[sourceIndexes[word]] & mask);
            expected[word] = ShiftSource(current, ref previous, 4);
        }

        Assert.Equal(expected[0], ReadWord(machine, DestinationD));
        Assert.Equal(expected[1], ReadWord(machine, DestinationD + 2));
        Assert.Equal(expected[2], ReadWord(machine, DestinationD + 4));
        Assert.Equal(expected[3], ReadWord(machine, DestinationD + 10));
        Assert.Equal(expected[4], ReadWord(machine, DestinationD + 12));
        Assert.Equal(expected[5], ReadWord(machine, DestinationD + 14));
        Assert.Equal(SourceA + 16, machine.GetBlitterPointer(LightweightRegisters.Bltapth));
        Assert.Equal(DestinationD + 16, machine.GetBlitterPointer(LightweightRegisters.Bltdpth));
    }

    [Fact]
    public void StartupControlRetriesWhenItsFollowingOutputWouldBeRefreshOwned()
    {
        using var machine = new LightweightA500Machine();
        WriteWord(machine, SourceA, 0xCAFE);
        ConfigureArea(machine, bltcon0: 0x09F0);
        EnableBlitter(machine, nasty: true);
        var lineStart = 2L * LightweightClock.CpuCyclesPerLine;
        var start = WriteRegisterAt(
            machine,
            LightweightRegisters.Bltsize,
            0x0041,
            lineStart + (223 * LightweightClock.CpuCyclesPerColorClock));

        machine.AdvanceHardwareTo(start + 8);

        Assert.Equal(start + 10, machine.BlitterPendingOutputCycle);
        Assert.Equal(start + 8, machine.BlitterLastAcceptedInputCycle);
        machine.AdvanceHardwareTo(start + 12);
        Assert.Equal(start + 14, machine.BlitterPendingOutputCycle);
        machine.AdvanceHardwareTo(start + 14);
        Assert.Equal((ushort)0xCAFE, ReadWord(machine, DestinationD));
    }

    [Fact]
    public void BitplaneOutputKeepsPriorityAndDelaysOnlyIncomingBlitterPhase()
    {
        using var machine = new LightweightA500Machine();
        const uint plane = 0x3000;
        WriteWord(machine, plane, 0x8000);
        WriteRegister(machine, LightweightRegisters.BplPointerFirst, 0);
        WriteRegister(machine, (ushort)(LightweightRegisters.BplPointerFirst + 2), (ushort)plane);
        WriteRegister(machine, LightweightRegisters.Diwstrt, 0x0100);
        WriteRegister(machine, LightweightRegisters.Diwstop, 0x0300);
        WriteRegister(machine, LightweightRegisters.Ddfstrt, 0x0038);
        WriteRegister(machine, LightweightRegisters.Ddfstop, 0x0040);
        WriteRegister(machine, LightweightRegisters.Bplcon0, 0x1000);
        WriteWord(machine, SourceA, 0xCAFE);
        ConfigureArea(machine, bltcon0: 0x09F0);
        WriteRegister(machine, LightweightRegisters.DmaconWrite, 0x8740);
        var lineStart = LightweightClock.CpuCyclesPerLine;
        var start = WriteRegisterAt(
            machine,
            LightweightRegisters.Bltsize,
            0x0041,
            lineStart + (0x3C * LightweightClock.CpuCyclesPerColorClock));

        var bitplaneOutput = lineStart +
            (0x40 * LightweightClock.CpuCyclesPerColorClock);
        machine.AdvanceHardwareTo(bitplaneOutput);

        Assert.Equal(bitplaneOutput, machine.BitplaneLastOutputCycle);
        Assert.NotEqual(bitplaneOutput, machine.BlitterLastOutputCycle);
        Assert.Equal(bitplaneOutput + 2, machine.BlitterPendingOutputCycle);
        Assert.Equal(bitplaneOutput, machine.BlitterLastAcceptedInputCycle);
        machine.AdvanceHardwareTo(start + 14);
        Assert.Equal((ushort)0xCAFE, ReadWord(machine, DestinationD));
    }

    [Fact]
    public void DestinationOnlyStartupPublishesBusyBeforeFinalRetainedWriteDrains()
    {
        using var machine = new LightweightA500Machine();
        for (var word = 0; word < 4; word++)
        {
            WriteWord(machine, DestinationD + (uint)(word * 2), 0xA5A5);
        }
        ConfigureArea(machine, bltcon0: 0x0100);
        EnableBlitter(machine, nasty: true);
        var start = StartBlit(machine, 0x0044);

        machine.AdvanceHardwareTo(start + 22);
        Assert.True(machine.BlitterStatusBusy);
        Assert.Equal((ushort)0, ReadWord(machine, DestinationD + 4));
        Assert.Equal((ushort)0xA5A5, ReadWord(machine, DestinationD + 6));

        machine.AdvanceHardwareTo(start + 24);
        Assert.False(machine.BlitterStatusBusy);
        Assert.True(machine.BlitterActive);
        Assert.Equal(start + 24, machine.BlitterLastCompletionCycle);
        Assert.Equal((ushort)0xA5A5, ReadWord(machine, DestinationD + 6));

        machine.AdvanceHardwareTo(start + 26);
        Assert.False(machine.BlitterActive);
        Assert.Equal((ushort)0, ReadWord(machine, DestinationD + 6));
        Assert.Equal(start + 26, machine.BlitterLastTerminationCycle);
    }

    [Fact]
    public void EnabledChannelsPauseWithDmaOffAndResumeFromSameStartupPhase()
    {
        using var machine = new LightweightA500Machine();
        WriteWord(machine, SourceA, 0x55AA);
        ConfigureArea(machine, bltcon0: 0x09F0);
        var start = StartBlit(machine, 0x0041);

        machine.AdvanceHardwareTo(start + 100);
        Assert.True(machine.BlitterStatusBusy);
        Assert.Equal(long.MaxValue, machine.BlitterNextCycle);
        Assert.Equal((ushort)0, ReadWord(machine, DestinationD));

        var enableCycle = start + 110;
        EnableBlitter(machine, nasty: true, requestedCycle: enableCycle);
        var enableOutput = enableCycle;
        machine.AdvanceHardwareTo(enableOutput + 10);

        Assert.Equal((ushort)0x55AA, ReadWord(machine, DestinationD));
        Assert.False(machine.BlitterActive);
    }

    [Fact]
    public void NiceModeYieldsFourthEligibleDmaSlotToPendingCpu()
    {
        using var machine = CreateOneWordAllChannelBlit(nasty: false, out var start);
        var cpuCycle = start + 8;

        _ = machine.ReadWord(SourceA, ref cpuCycle, M68kBusAccessKind.CpuDataRead);

        Assert.Equal(start + 16, cpuCycle);
        Assert.Equal(start + 16, machine.BlitterLastOutputCycle);
        Assert.Equal(long.MaxValue, machine.BlitterPendingOutputCycle);
        Assert.False(machine.BlitterActive);
    }

    [Fact]
    public void NastyModeKeepsEveryEligibleDmaSlotAcrossPendingCpu()
    {
        using var machine = CreateOneWordAllChannelBlit(nasty: true, out var start);
        var cpuCycle = start + 8;

        _ = machine.ReadWord(SourceA, ref cpuCycle, M68kBusAccessKind.CpuDataRead);

        Assert.Equal(start + 18, cpuCycle);
        Assert.Equal(start + 14, machine.BlitterLastTerminationCycle);
        Assert.False(machine.BlitterActive);
    }

    [Fact]
    public void BltpriChangesTwoCcksAfterDmaconOutput()
    {
        using var machine = new LightweightA500Machine();
        var setCycle = WriteRegisterAt(
            machine,
            LightweightRegisters.DmaconWrite,
            0x8400,
            QuietStartCycle);

        machine.AdvanceHardwareTo(setCycle + 3);
        Assert.False(machine.BlitterEffectiveNasty);
        machine.AdvanceHardwareTo(setCycle + 4);
        Assert.True(machine.BlitterEffectiveNasty);

        var clearCycle = WriteRegisterAt(
            machine,
            LightweightRegisters.DmaconWrite,
            0x0400,
            setCycle + 20);
        machine.AdvanceHardwareTo(clearCycle + 3);
        Assert.True(machine.BlitterEffectiveNasty);
        machine.AdvanceHardwareTo(clearCycle + 4);
        Assert.False(machine.BlitterEffectiveNasty);
    }

    [Fact]
    public void CopperBfdWaitObservesFinalPhysicalBlitterDrain()
    {
        using var machine = new LightweightA500Machine();
        const uint list = 0x2000;
        WriteWord(machine, list + 0, 0x0001);
        WriteWord(machine, list + 2, 0x7FFE);
        WriteWord(machine, list + 4, 0x0180);
        WriteWord(machine, list + 6, 0x0F00);
        WriteWord(machine, list + 8, 0xFFFF);
        WriteWord(machine, list + 10, 0xFFFE);
        WritePointer(machine, LightweightRegisters.Cop1lch, list);
        for (var word = 0; word < 4; word++)
        {
            WriteWord(machine, DestinationD + (uint)(word * 2), 0xA5A5);
        }
        ConfigureArea(machine, bltcon0: 0x0100);
        WriteRegister(machine, LightweightRegisters.DmaconWrite, 0x8240);
        var start = StartBlit(machine, 0x0044);
        WriteRegister(machine, LightweightRegisters.DmaconWrite, 0x8080);
        WriteRegister(machine, LightweightRegisters.Copjmp1, 0);

        for (var cycle = start + 2;
             cycle <= start + 80 && machine.BlitterStatusBusy;
             cycle += LightweightClock.CpuCyclesPerColorClock)
        {
            machine.AdvanceHardwareTo(cycle);
        }
        Assert.False(machine.BlitterStatusBusy);
        Assert.True(machine.BlitterActive);
        Assert.Equal((ushort)0, machine.GetCustomRegister(0x180));

        machine.AdvanceHardwareTo(machine.BlitterLastCompletionCycle + 40);
        Assert.False(machine.BlitterActive);
        Assert.Equal((ushort)0x0F00, machine.GetCustomRegister(0x180));
        Assert.True(machine.CopperLastMoveCycle > machine.BlitterLastTerminationCycle);
    }

    [Fact]
    public void DescendingShiftPointersAndEvenModuloMoveBackward()
    {
        for (var shift = 0; shift < 16; shift++)
        {
            using var machine = new LightweightA500Machine();
            WriteWord(machine, SourceA + 14, 0x5678);
            WriteWord(machine, SourceA + 12, 0x1234);
            WriteWord(machine, SourceA + 6, 0xABCD);
            WriteWord(machine, SourceA + 4, 0x0F0F);
            ConfigureArea(
                machine,
                bltcon0: (ushort)(0x0900 | (shift << 12) | 0x00F0),
                bltcon1: 0x0002);
            WritePointer(machine, LightweightRegisters.Bltapth, SourceA + 14);
            WritePointer(machine, LightweightRegisters.Bltdpth, DestinationD + 14);
            WriteRegister(machine, LightweightRegisters.Bltamod, 5);
            WriteRegister(machine, LightweightRegisters.Bltdmod, 5);
            EnableBlitter(machine, nasty: true);
            _ = StartBlit(machine, (ushort)((2 << 6) | 2));
            AdvanceUntilBlitterIdle(machine);

            ushort previous = 0;
            var expected0 = ShiftSource(0x5678, ref previous, shift, descending: true);
            var expected1 = ShiftSource(0x1234, ref previous, shift, descending: true);
            var expected2 = ShiftSource(0xABCD, ref previous, shift, descending: true);
            var expected3 = ShiftSource(0x0F0F, ref previous, shift, descending: true);
            Assert.Equal(expected0, ReadWord(machine, DestinationD + 14));
            Assert.Equal(expected1, ReadWord(machine, DestinationD + 12));
            Assert.Equal(expected2, ReadWord(machine, DestinationD + 6));
            Assert.Equal(expected3, ReadWord(machine, DestinationD + 4));
            Assert.Equal(SourceA + 2, machine.GetBlitterPointer(LightweightRegisters.Bltapth));
            Assert.Equal(DestinationD + 2, machine.GetBlitterPointer(LightweightRegisters.Bltdpth));
        }
    }

    [Theory]
    [InlineData(0x000A, false, false)]
    [InlineData(0x000E, false, true)]
    [InlineData(0x0012, true, false)]
    [InlineData(0x0016, true, true)]
    public void DescendingFillUsesInclusiveExclusiveCarryAndIdleCPhase(
        ushort bltcon1,
        bool exclusive,
        bool carryIn)
    {
        using var machine = new LightweightA500Machine();
        WriteWord(machine, SourceA, 0x0008);
        ConfigureArea(machine, bltcon0: 0x09F0, bltcon1: bltcon1);
        EnableBlitter(machine, nasty: true);
        var start = StartBlit(machine, 0x0041);
        AdvanceUntilBlitterIdle(machine);

        var carry = carryIn;
        var expected = ApplyFill(0x0008, exclusive, ref carry);
        Assert.Equal(expected, ReadWord(machine, DestinationD));
        Assert.Equal(start + 12, machine.BlitterLastTerminationCycle);
    }

    [Fact]
    public void FillCarryRestartsAtEachAreaRow()
    {
        using var machine = new LightweightA500Machine();
        WriteWord(machine, SourceA + 2, 0x0001);
        WriteWord(machine, SourceA, 0x0001);
        ConfigureArea(machine, bltcon0: 0x09F0, bltcon1: 0x0012);
        WritePointer(machine, LightweightRegisters.Bltapth, SourceA + 2);
        WritePointer(machine, LightweightRegisters.Bltdpth, DestinationD + 2);
        EnableBlitter(machine, nasty: true);
        _ = StartBlit(machine, 0x0081);
        AdvanceUntilBlitterIdle(machine);

        var carry = false;
        var expected = ApplyFill(0x0001, true, ref carry);
        Assert.Equal(expected, ReadWord(machine, DestinationD + 2));
        Assert.Equal(expected, ReadWord(machine, DestinationD));
    }

    [Fact]
    public void LineModeRetainsFourStartupAndIdleCIdleDPhases()
    {
        using var machine = new LightweightA500Machine();
        WriteWord(machine, DestinationD, 0x1234);
        ConfigureLine(machine, DestinationD, rowStride: 0x20, bltcon1: 0x0001);
        EnableBlitter(machine, nasty: true);
        var start = StartBlit(machine, 0x0042);

        machine.AdvanceHardwareTo(start + 8);
        Assert.Equal(start + 10, machine.BlitterPendingOutputCycle);
        Assert.Equal(long.MinValue, machine.BlitterLastOutputCycle);

        machine.AdvanceHardwareTo(start + 12);
        var cData = machine.GetCustomRegister(LightweightRegisters.Bltcdat);
        Assert.True(
            cData == 0x1234,
            $"BLTCDAT=0x{cData:X4}, cycle={machine.Cycle}, " +
            $"next={machine.BlitterNextCycle}, pending={machine.BlitterPendingOutputCycle}, " +
            $"lastInput={machine.BlitterLastAcceptedInputCycle}, lastOutput={machine.BlitterLastOutputCycle}, " +
            $"BLTCPT=0x{machine.GetBlitterPointer(LightweightRegisters.Bltcpth):X}");
        Assert.Equal(start + 12, machine.BlitterLastOutputCycle);
        Assert.Equal(start + 14, machine.BlitterPendingOutputCycle);
        Assert.Equal((ushort)0x1234, ReadWord(machine, DestinationD));

        machine.AdvanceHardwareTo(start + 14);
        Assert.True(machine.BlitterStatusBusy);
        Assert.Equal(start + 16, machine.BlitterPendingOutputCycle);
        Assert.Equal((ushort)0x1234, ReadWord(machine, DestinationD));

        machine.AdvanceHardwareTo(start + 16);
        Assert.False(machine.BlitterStatusBusy);
        Assert.False(machine.BlitterActive);
        Assert.Equal(start + 16, machine.BlitterLastCompletionCycle);
        Assert.Equal(start + 16, machine.BlitterLastTerminationCycle);
        Assert.Equal(start + 16, machine.BlitterLastOutputCycle);
        Assert.Equal((ushort)0x9234, ReadWord(machine, DestinationD));
        Assert.NotEqual(0, machine.Intreq & 0x0040);
    }

    [Fact]
    public void LineOctantsAndSignSelectTheHrmMajorAndMinorSteps()
    {
        const int rowStride = 0x20;
        for (var octant = 0; octant < 8; octant++)
        {
            foreach (var sign in new[] { false, true })
            {
                using var machine = new LightweightA500Machine();
                var baseAddress = DestinationD + 0x1000;
                var octantBits = (ushort)(octant << 2);
                var bltcon1 = (ushort)(0x0001 | octantBits |
                    (sign ? 0x0040 : 0));
                ConfigureLine(
                    machine,
                    baseAddress,
                    rowStride,
                    bltcon1,
                    initialAccumulator: sign ? (short)-2 : (short)0);
                EnableBlitter(machine, nasty: true);
                _ = StartBlit(machine, 0x0082);
                AdvanceUntilBlitterIdle(machine);

                var expected = GetLineDelta(octantBits, sign);
                Assert.True(IsLinePixelSet(machine, baseAddress, rowStride, 0, 0));
                Assert.True(IsLinePixelSet(
                    machine,
                    baseAddress,
                    rowStride,
                    expected.X,
                    expected.Y));
            }
        }
    }

    [Fact]
    public void LineTextureUsesTheConfiguredStartPhase()
    {
        for (var shift = 0; shift < 16; shift++)
        {
            using var machine = new LightweightA500Machine();
            var baseAddress = DestinationD + 0x1800;
            ConfigureLine(
                machine,
                baseAddress,
                rowStride: 0x20,
                bltcon1: (ushort)(0x0001 | (shift << 12)),
                texture: 0x8000);
            EnableBlitter(machine, nasty: true);
            _ = StartBlit(machine, 0x0042);
            AdvanceUntilBlitterIdle(machine);

            Assert.Equal(
                shift == 0,
                IsLinePixelSet(machine, baseAddress, 0x20, 0, 0));
        }
    }

    [Fact]
    public void OcsLineRequiresCButIgnoresDEnableAndLastWordMask()
    {
        var baseAddress = DestinationD + 0x2000;
        using (var withoutC = new LightweightA500Machine())
        {
            ConfigureLine(
                withoutC,
                baseAddress,
                rowStride: 0x20,
                bltcon1: 0x0001,
                channelMask: 0x0900);
            EnableBlitter(withoutC, nasty: true);
            _ = StartBlit(withoutC, 0x0042);
            AdvanceUntilBlitterIdle(withoutC);
            Assert.False(IsLinePixelSet(withoutC, baseAddress, 0x20, 0, 0));
        }

        using var withoutD = new LightweightA500Machine();
        ConfigureLine(
            withoutD,
            baseAddress,
            rowStride: 0x20,
            bltcon1: 0x0001,
            channelMask: 0x0A00);
        WriteRegister(withoutD, LightweightRegisters.Bltalwm, 0x0000);
        EnableBlitter(withoutD, nasty: true);
        _ = StartBlit(withoutD, 0x0042);
        AdvanceUntilBlitterIdle(withoutD);
        Assert.True(IsLinePixelSet(withoutD, baseAddress, 0x20, 0, 0));
    }

    [Fact]
    public void OcsLineUsesDPointerOnlyForFirstPixelAndCModuloForRows()
    {
        const int rowStride = 0x20;
        var baseAddress = DestinationD + 0x2800;
        var firstPixelAddress = DestinationD + 0x3000;
        using var machine = new LightweightA500Machine();
        ConfigureLine(
            machine,
            baseAddress,
            rowStride,
            bltcon1: 0x0041,
            initialAccumulator: -2,
            aModulo: -4,
            dModulo: 0x40,
            destinationD: firstPixelAddress);
        EnableBlitter(machine, nasty: true);
        _ = StartBlit(machine, 0x0082);
        AdvanceUntilBlitterIdle(machine);

        Assert.Equal((ushort)0x8000, ReadWord(machine, firstPixelAddress));
        Assert.Equal((ushort)0x8000, ReadWord(machine, baseAddress + rowStride));
        Assert.Equal(
            machine.GetBlitterPointer(LightweightRegisters.Bltcpth),
            machine.GetBlitterPointer(LightweightRegisters.Bltdpth));
        Assert.Equal(baseAddress + rowStride,
            machine.GetBlitterPointer(LightweightRegisters.Bltcpth));
    }

    [Fact]
    public void OcsLineLeavesAErrorPointerAloneWhenAChannelIsDisabled()
    {
        using var machine = new LightweightA500Machine();
        var baseAddress = DestinationD + 0x3800;
        ConfigureLine(
            machine,
            baseAddress,
            rowStride: 0x20,
            bltcon1: 0x0001,
            initialAccumulator: 0x1234,
            channelMask: 0x0300);
        EnableBlitter(machine, nasty: true);
        _ = StartBlit(machine, 0x0042);
        AdvanceUntilBlitterIdle(machine);

        Assert.Equal((uint)0x1234,
            machine.GetBlitterPointer(LightweightRegisters.Bltapth));
        Assert.True(IsLinePixelSet(machine, baseAddress, 0x20, 0, 0));
    }

    [Fact]
    public void LineBChannelReloadsEachPatternWordTwiceAndAdvancesByModulo()
    {
        const int rowStride = 0x20;
        using var machine = new LightweightA500Machine();
        var baseAddress = DestinationD + 0x4000;
        WriteWord(machine, SourceB, 0x8000);
        WriteWord(machine, SourceB + 2, 0x4000);
        ConfigureLine(
            machine,
            baseAddress,
            rowStride,
            bltcon1: 0x0001,
            bModulo: 3,
            channelMask: 0x0F00);
        EnableBlitter(machine, nasty: true);
        var start = StartBlit(machine, 0x0082);

        machine.AdvanceHardwareTo(start + 12);
        Assert.Equal((ushort)0x8000,
            machine.GetCustomRegister(LightweightRegisters.Bltbdat));
        Assert.Equal(SourceB + 2,
            machine.GetBlitterPointer(LightweightRegisters.Bltbpth));
        AdvanceUntilBlitterIdle(machine);

        Assert.True(IsLinePixelSet(machine, baseAddress, rowStride, 0, 0));
        Assert.True(IsLinePixelSet(machine, baseAddress, rowStride, 1, 1));
        Assert.Equal((ushort)0x4000,
            machine.GetCustomRegister(LightweightRegisters.Bltbdat));
        Assert.Equal(SourceB + 4,
            machine.GetBlitterPointer(LightweightRegisters.Bltbpth));
        Assert.Equal(start + 24, machine.BlitterLastCompletionCycle);
    }

    [Fact]
    public void NoncanonicalLineWidthIsReportedInsteadOfSilentlyAccepted()
    {
        using var machine = new LightweightA500Machine();
        ConfigureLine(
            machine,
            DestinationD + 0x4400,
            rowStride: 0x20,
            bltcon1: 0x0001);
        EnableBlitter(machine, nasty: true);
        _ = StartBlit(machine, 0x0041);

        Assert.Equal(
            "OCS line-mode BLTSIZE widths other than two are not modeled",
            machine.UnsupportedActiveFeature);
    }

    [Fact]
    public void SingleDotLineSkipsRepeatedPixelsOnTheSameRasterRow()
    {
        using var machine = new LightweightA500Machine();
        var baseAddress = DestinationD + 0x4800;
        ConfigureLine(
            machine,
            baseAddress,
            rowStride: 0x20,
            bltcon1: 0x0053,
            initialAccumulator: -2);
        EnableBlitter(machine, nasty: true);
        var start = StartBlit(machine, 0x00C2);
        AdvanceUntilBlitterIdle(machine);

        Assert.Equal((ushort)0x8000, ReadWord(machine, baseAddress));
        Assert.Equal(start + 32, machine.BlitterLastCompletionCycle);
    }

    [Fact]
    public void DeferredLineRestartReceivesItsOwnFourStartupClocks()
    {
        using var machine = new LightweightA500Machine();
        ConfigureLine(
            machine,
            DestinationD + 0x5000,
            rowStride: 0x20,
            bltcon1: 0x0001);
        EnableBlitter(machine, nasty: true);
        var firstStart = StartBlit(machine, 0x0042);
        WriteRegister(machine, LightweightRegisters.Bltsize, 0x0042);
        AdvanceUntilBlitterIdle(machine);

        Assert.Equal(firstStart + 32, machine.BlitterLastCompletionCycle);
        Assert.Equal(firstStart + 32, machine.BlitterLastTerminationCycle);
    }

    [Fact]
    public void LineBusyWaitsForFinalWritePastMandatoryRefresh()
    {
        using var machine = new LightweightA500Machine();
        var destination = DestinationD + 0x5800;
        ConfigureLine(
            machine,
            destination,
            rowStride: 0x20,
            bltcon1: 0x0001);
        EnableBlitter(machine, nasty: true);
        var lineStart = 3L * LightweightClock.CpuCyclesPerLine;
        var start = WriteRegisterAt(
            machine,
            LightweightRegisters.Bltsize,
            0x0042,
            lineStart + (219 * LightweightClock.CpuCyclesPerColorClock));

        machine.AdvanceHardwareTo(start + 16);
        Assert.True(machine.BlitterStatusBusy);
        Assert.Equal((ushort)0, ReadWord(machine, destination));
        Assert.Equal(start + 18, machine.BlitterPendingOutputCycle);

        machine.AdvanceHardwareTo(start + 18);
        Assert.False(machine.BlitterStatusBusy);
        Assert.Equal((ushort)0x8000, ReadWord(machine, destination));
        Assert.Equal(start + 18, machine.BlitterLastCompletionCycle);
    }

    [Fact]
    public void LineStartupAndIdlePhasesOverlapMandatoryRefresh()
    {
        using var machine = new LightweightA500Machine();
        var destination = DestinationD + 0x6000;
        ConfigureLine(
            machine,
            destination,
            rowStride: 0x20,
            bltcon1: 0x0001);
        EnableBlitter(machine, nasty: true);
        var lineStart = 3L * LightweightClock.CpuCyclesPerLine;
        var start = WriteRegisterAt(
            machine,
            LightweightRegisters.Bltsize,
            0x0042,
            lineStart + (222 * LightweightClock.CpuCyclesPerColorClock));
        AdvanceUntilBlitterIdle(machine);

        Assert.Equal(start + 16, machine.BlitterLastCompletionCycle);
        Assert.Equal((ushort)0x8000, ReadWord(machine, destination));
    }

    private static LightweightA500Machine CreateOneWordAllChannelBlit(
        bool nasty,
        out long start)
    {
        var machine = new LightweightA500Machine();
        WriteWord(machine, SourceA, 0xFF00);
        WriteWord(machine, SourceB, 0x0F0F);
        WriteWord(machine, SourceC, 0x3333);
        ConfigureArea(machine, bltcon0: 0x0FCA);
        EnableBlitter(machine, nasty);
        start = StartBlit(machine, 0x0041);
        return machine;
    }

    private static void ConfigureArea(
        LightweightA500Machine machine,
        ushort bltcon0,
        ushort bltcon1 = 0)
    {
        WriteRegister(machine, LightweightRegisters.Bltcon0, bltcon0);
        WriteRegister(machine, LightweightRegisters.Bltcon1, bltcon1);
        WriteRegister(machine, LightweightRegisters.Bltafwm, 0xFFFF);
        WriteRegister(machine, LightweightRegisters.Bltalwm, 0xFFFF);
        WritePointer(machine, LightweightRegisters.Bltapth, SourceA);
        WritePointer(machine, LightweightRegisters.Bltbpth, SourceB);
        WritePointer(machine, LightweightRegisters.Bltcpth, SourceC);
        WritePointer(machine, LightweightRegisters.Bltdpth, DestinationD);
        WriteRegister(machine, LightweightRegisters.Bltamod, 0);
        WriteRegister(machine, LightweightRegisters.Bltbmod, 0);
        WriteRegister(machine, LightweightRegisters.Bltcmod, 0);
        WriteRegister(machine, LightweightRegisters.Bltdmod, 0);
    }

    private static void ConfigureLine(
        LightweightA500Machine machine,
        uint baseAddress,
        int rowStride,
        ushort bltcon1,
        ushort texture = 0xFFFF,
        byte minterm = 0xCA,
        short initialAccumulator = 0,
        short aModulo = 0,
        short bModulo = 0,
        short? dModulo = null,
        ushort channelMask = 0x0B00,
        uint sourceB = SourceB,
        uint? destinationD = null)
    {
        WriteRegister(machine, LightweightRegisters.Bltcon0,
            (ushort)(channelMask | minterm));
        WriteRegister(machine, LightweightRegisters.Bltcon1, bltcon1);
        WriteRegister(machine, LightweightRegisters.Bltafwm, 0xFFFF);
        WriteRegister(machine, LightweightRegisters.Bltalwm, 0xFFFF);
        WritePointer(machine, LightweightRegisters.Bltapth,
            unchecked((uint)(ushort)initialAccumulator));
        WritePointer(machine, LightweightRegisters.Bltbpth, sourceB);
        WritePointer(machine, LightweightRegisters.Bltcpth, baseAddress);
        WritePointer(machine, LightweightRegisters.Bltdpth,
            destinationD ?? baseAddress);
        WriteRegister(machine, LightweightRegisters.Bltamod,
            unchecked((ushort)aModulo));
        WriteRegister(machine, LightweightRegisters.Bltbmod,
            unchecked((ushort)bModulo));
        WriteRegister(machine, LightweightRegisters.Bltcmod,
            unchecked((ushort)rowStride));
        WriteRegister(machine, LightweightRegisters.Bltdmod,
            unchecked((ushort)(dModulo ?? (short)rowStride)));
        WriteRegister(machine, LightweightRegisters.Bltbdat, texture);
        WriteRegister(machine, LightweightRegisters.Bltadat, 0x8000);
    }

    private static bool IsLinePixelSet(
        LightweightA500Machine machine,
        uint baseAddress,
        int rowStride,
        int x,
        int y)
    {
        var wordX = x >= 0 ? x / 16 : -((15 - x) / 16);
        var bit = x & 15;
        var address = unchecked((uint)((int)baseAddress +
            (y * rowStride) + (wordX * 2)));
        return (ReadWord(machine, address) & (0x8000 >> bit)) != 0;
    }

    private static (int X, int Y) GetLineDelta(ushort octantBits, bool sign)
    {
        var sud = (octantBits & 0x0010) != 0;
        var sul = (octantBits & 0x0008) != 0;
        var aul = (octantBits & 0x0004) != 0;
        var x = 0;
        var y = 0;
        if (!sign)
        {
            if (sud) y += sul ? -1 : 1;
            else x += sul ? -1 : 1;
        }
        if (sud) x += aul ? -1 : 1;
        else y += aul ? -1 : 1;
        return (x, y);
    }

    private static void EnableBlitter(
        LightweightA500Machine machine,
        bool nasty,
        long? requestedCycle = null)
    {
        var value = (ushort)(0x8240 | (nasty ? 0x0400 : 0));
        if (requestedCycle.HasValue)
        {
            _ = WriteRegisterAt(
                machine,
                LightweightRegisters.DmaconWrite,
                value,
                requestedCycle.Value);
        }
        else
        {
            WriteRegister(machine, LightweightRegisters.DmaconWrite, value);
        }
    }

    private static long StartBlit(LightweightA500Machine machine, ushort size)
        => WriteRegisterAt(
            machine,
            LightweightRegisters.Bltsize,
            size,
            Math.Max(QuietStartCycle, machine.Cycle));

    private static long WriteRegisterAt(
        LightweightA500Machine machine,
        ushort offset,
        ushort value,
        long requestedCycle)
    {
        var cycle = requestedCycle;
        machine.WriteWord(
            LightweightA500Machine.CustomBase + offset,
            value,
            ref cycle,
            M68kBusAccessKind.CpuDataWrite);
        return cycle - LightweightClock.CpuCyclesPerColorClock;
    }

    private static void WriteRegister(
        LightweightA500Machine machine,
        ushort offset,
        ushort value)
    {
        var cycle = machine.Cycle;
        machine.WriteWord(
            LightweightA500Machine.CustomBase + offset,
            value,
            ref cycle,
            M68kBusAccessKind.CpuDataWrite);
    }

    private static void WritePointer(
        LightweightA500Machine machine,
        ushort highOffset,
        uint pointer)
    {
        WriteRegister(machine, highOffset, (ushort)(pointer >> 16));
        WriteRegister(machine, (ushort)(highOffset + 2), (ushort)pointer);
    }

    private static void WriteWord(
        LightweightA500Machine machine,
        uint address,
        ushort value)
    {
        var cycle = machine.Cycle;
        machine.WriteWord(
            address,
            value,
            ref cycle,
            M68kBusAccessKind.CpuDataWrite);
    }

    private static ushort ReadWord(
        LightweightA500Machine machine,
        uint address)
        => (ushort)((machine.ChipRam.Span[(int)address] << 8) |
            machine.ChipRam.Span[(int)address + 1]);

    private static void AdvanceUntilBlitterIdle(LightweightA500Machine machine)
    {
        while (machine.BlitterActive)
        {
            Assert.NotEqual(long.MaxValue, machine.BlitterNextCycle);
            machine.AdvanceHardwareTo(machine.BlitterNextCycle);
        }
    }

    private static ushort ShiftSource(
        ushort current,
        ref ushort previous,
        int shift,
        bool descending = false)
    {
        uint combined = descending
            ? ((uint)current << 16) | previous
            : ((uint)previous << 16) | current;
        var value = shift == 0
            ? current
            : descending
                ? (ushort)(combined >> (16 - (shift & 15)))
                : (ushort)(combined >> (shift & 15));
        previous = current;
        return value;
    }

    private static ushort ApplyFill(
        ushort value,
        bool exclusive,
        ref bool carry)
    {
        ushort output = 0;
        for (var bit = 0; bit < 16; bit++)
        {
            var mask = (ushort)(1 << bit);
            var input = (value & mask) != 0;
            if (exclusive)
            {
                if (carry) output |= mask;
                if (input) carry = !carry;
            }
            else
            {
                if (carry || input) output |= mask;
                if (input) carry = !carry;
            }
        }
        return output;
    }

    private static ushort ApplyMinterm(
        byte minterm,
        ushort sourceA,
        ushort sourceB,
        ushort sourceC)
    {
        ushort output = 0;
        for (var bit = 0; bit < 16; bit++)
        {
            var index = (((sourceA >> bit) & 1) << 2) |
                (((sourceB >> bit) & 1) << 1) |
                ((sourceC >> bit) & 1);
            if ((minterm & (1 << index)) != 0)
            {
                output |= (ushort)(1 << bit);
            }
        }
        return output;
    }
}
