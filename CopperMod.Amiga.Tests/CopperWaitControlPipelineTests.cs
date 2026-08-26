using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class CopperWaitControlPipelineTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FourPlanePreTailToLineTailRunPreservesPhysicalPhase(
        bool enableHardwareSpecialization)
    {
        var bus = CreateFivePlaneBus(enableHardwareSpecialization);
        bus.WriteWord(0x00DFF100, 0x4200);
        const uint copperList = 0x2600;
        WriteCopperList(bus, copperList,
            (0xF643, 0xFFFE), (0x0180, 0x0F00), (0x0180, 0x0000),
            (0xF663, 0xFFFE), (0x0180, 0x0FF0),
            (0xF671, 0xFFFE), (0x0180, 0x0000),
            (0xF683, 0xFFFE), (0x0180, 0x00FF),
            (0xF693, 0xFFFE), (0xF693, 0xFFFE), (0x0180, 0x0000),
            (0xF6B3, 0xFFFE), (0x0180, 0x0F0F),
            (0xF6C5, 0xFFFE), (0xF6C5, 0xFFFE), (0xF6C5, 0xFFFE),
            (0x0180, 0x0000), (0xFFFF, 0xFFFE));
        SetCopperPointer(bus, copperList);
        StartCopper(bus);
        bus.AdvanceDmaTo(0xF8 * AmigaConstants.A500PalCpuCyclesPerRasterLine);

        Assert.Equal(
            new[]
            {
                (246, 72), (246, 76), (246, 104), (246, 120),
                (246, 136), (246, 162), (246, 184), (247, 8)
            },
            bus.Display.CopperDisplayWrites
                .Where(write => write.Address == 0x180)
                .Select(write =>
                {
                    var beam = bus.GetBeamPosition(write.Cycle);
                    return (beam.BeamLine, beam.BeamHorizontal);
                }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FivePlaneWaitRunPreservesEveryMoveVisibilityCycle(bool enableHardwareSpecialization)
    {
        var bus = CreateFivePlaneBus(enableHardwareSpecialization);
        const uint copperList = 0x2400;
        WriteCopperList(bus, copperList,
            (0x6241, 0xFFFE), (0x0180, 0x0F00), (0x0180, 0x0000),
            (0x6261, 0xFFFE), (0x0180, 0x0FF0),
            (0x6261, 0xFFFE), (0x0180, 0x0000),
            (0x6281, 0xFFFE), (0x0180, 0x00FF),
            (0x6281, 0xFFFE), (0x6281, 0xFFFE), (0x0180, 0x0000),
            (0x62B1, 0xFFFE), (0x0180, 0x0F0F),
            (0x62B1, 0xFFFE), (0x62B1, 0xFFFE), (0x62B1, 0xFFFE), (0x0180, 0x0000),
            (0xFFFF, 0xFFFE));
        SetCopperPointer(bus, copperList);
        StartCopper(bus);
        bus.AdvanceDmaTo(0x64 * AmigaConstants.A500PalCpuCyclesPerRasterLine);

        Assert.Equal(
            new long[] { 44640, 44652, 44704, 44736, 44768, 44824, 44864, 44940 },
            bus.Display.CopperDisplayWrites
                .Where(write => write.Address == 0x180)
                .Select(write => write.Cycle));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FourPlaneWaitRunPreservesControlAndBusPhase(
        bool enableHardwareSpecialization)
    {
        var bus = CreateFivePlaneBus(enableHardwareSpecialization);
        bus.WriteWord(0x00DFF100, 0x4200);
        const uint copperList = 0x2400;
        WriteCopperList(bus, copperList,
            (0x6241, 0xFFFE), (0x0180, 0x0F00), (0x0180, 0x0000),
            (0x6261, 0xFFFE), (0x0180, 0x0FF0),
            (0x6261, 0xFFFE), (0x0180, 0x0000),
            (0x6281, 0xFFFE), (0x0180, 0x00FF),
            (0x6281, 0xFFFE), (0x6281, 0xFFFE), (0x0180, 0x0000),
            (0x62B1, 0xFFFE), (0x0180, 0x0F0F),
            (0x62B1, 0xFFFE), (0x62B1, 0xFFFE), (0x62B1, 0xFFFE),
            (0x0180, 0x0000),
            (0x6443, 0xFFFE), (0x0180, 0x0F00), (0x0180, 0x0000),
            (0x6463, 0xFFFE), (0x0180, 0x0FF0),
            (0x6265, 0xFFFE), (0x0180, 0x0000),
            (0x6483, 0xFFFE), (0x0180, 0x00FF),
            (0x6485, 0xFFFE), (0x6485, 0xFFFE), (0x0180, 0x0000),
            (0x64B3, 0xFFFE), (0x0180, 0x0F0F),
            (0x64B5, 0xFFFE), (0x64B5, 0xFFFE), (0x64B5, 0xFFFE),
            (0x0180, 0x0000),
            (0xFFFF, 0xFFFE));
        SetCopperPointer(bus, copperList);
        StartCopper(bus);
        bus.AdvanceDmaTo(0x66 * AmigaConstants.A500PalCpuCyclesPerRasterLine);

        Assert.Equal(
            new long[] { 44636, 44644, 44700, 44724, 44764, 44804, 44860, 44916 },
            bus.Display.CopperDisplayWrites
                .Where(write => write.Address == 0x180)
                .Take(8)
                .Select(write => write.Cycle));

        Assert.Equal(
            new[]
            {
                (0x2404u, 70), (0x2406u, 72), (0x2408u, 74), (0x240Au, 76),
                (0x2410u, 102), (0x2412u, 104), (0x2418u, 114), (0x241Au, 116),
                (0x2420u, 134), (0x2422u, 136), (0x242Cu, 154), (0x242Eu, 156),
                (0x2434u, 182), (0x2436u, 184), (0x2444u, 210), (0x2446u, 212)
            },
            bus.BusAccesses
                .Where(access =>
                    access.Request.Kind == AmigaBusAccessKind.Copper &&
                    access.Request.Address is
                        0x2404 or 0x2406 or 0x2408 or 0x240A or
                        0x2410 or 0x2412 or 0x2418 or 0x241A or
                        0x2420 or 0x2422 or 0x242C or 0x242E or
                        0x2434 or 0x2436 or 0x2444 or 0x2446)
                .Select(access =>
                    (access.Request.Address,
                     bus.GetBeamPosition(access.GrantedCycle).BeamHorizontal)));

        Assert.Equal(
            new[] { 72, 76, 104, 118, 136, 158, 184, 214 },
            bus.Display.CopperDisplayWrites
                .Where(write =>
                    write.Address == 0x180 &&
                    bus.GetBeamPosition(write.Cycle).BeamLine == 100)
                .Select(write =>
                    bus.GetBeamPosition(write.Cycle).BeamHorizontal));
    }

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
        Assert.True(
            intervals.SequenceEqual(new[] { firstInterval, secondInterval, thirdInterval }),
            $"intervals={string.Join(',', intervals)}; " +
            string.Join(" | ", bus.Display.CopperWaitTransitions
                .Where(transition => transition.WaitFirst == wait)
                .Select(transition =>
                    $"cmp={transition.ComparisonCycle},sat={transition.SatisfiedCycle}," +
                    $"restart={transition.RestartCycle},carry={transition.CarryPending}/" +
                    $"{transition.CarrySkipCount},run={transition.SatisfiedWaitRunCount}," +
                    $"blocked={transition.WaitRunControlBlocked},incoming=" +
                    $"{transition.RestartIncomingRgaBlocked},inherit=" +
                    $"{transition.InheritedAdjacentControlPhaseCreated}/" +
                    $"{transition.ReusedInheritedAdjacentControlPhase},reuse=" +
                    $"{transition.ReusedRunControlPhase}")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedWaitRunAtPhysicalLineTailReusesCarriedControlPhase(
        bool enableHardwareSpecialization)
    {
        var bus = CreateFivePlaneBus(enableHardwareSpecialization);
        const uint copperList = 0x2400;
        WriteCopperList(
            bus,
            copperList,
            (0xF643, 0xFFFE), (0x0180, 0x0F00), (0x0180, 0x0000),
            (0xF663, 0xFFFE), (0x0180, 0x0FF0),
            (0xF671, 0xFFFE), (0x0180, 0x0000),
            (0xF683, 0xFFFE), (0x0180, 0x00FF),
            (0xF693, 0xFFFE), (0xF693, 0xFFFE), (0x0180, 0x0000),
            (0xF6B3, 0xFFFE), (0x0180, 0x0F0F),
            (0xF6C5, 0xFFFE), (0xF6C5, 0xFFFE), (0xF6C5, 0xFFFE),
            (0x0180, 0x0000),
            (0xFFFF, 0xFFFE));
        SetCopperPointer(bus, copperList);
        StartCopper(bus);
        bus.AdvanceDmaTo(248 * AmigaConstants.A500PalCpuCyclesPerRasterLine);

        var finalWrite = bus.Display.CopperDisplayWrites
            .Last(write => write.Address == 0x180);
        var lineStart = 246L * AmigaConstants.A500PalCpuCyclesPerRasterLine;
        Assert.Equal(lineStart + (226L * AmigaConstants.A500PalCpuCyclesPerColorClock), finalWrite.Cycle);

        var finalMoveOpcode = copperList + (17u * 4u);
        var finalMoveTransfers = bus.BusAccesses
            .Where(access =>
                access.Request.Kind == AmigaBusAccessKind.Copper &&
                (access.Request.Address == finalMoveOpcode ||
                 access.Request.Address == finalMoveOpcode + 2))
            .ToArray();
        Assert.Equal(2, finalMoveTransfers.Length);
        Assert.Equal(
            new[] { 224L, 226L },
            finalMoveTransfers.Select(access =>
                (access.GrantedCycle - lineStart) /
                AmigaConstants.A500PalCpuCyclesPerColorClock));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedWaitRunPastPhysicalLineTailClosesPresentationRow(
        bool enableHardwareSpecialization)
    {
        var bus = CreateFivePlaneBus(enableHardwareSpecialization);
        const uint copperList = 0x2400;
        WriteCopperList(
            bus,
            copperList,
            (0xF647, 0xFFFE), (0x0180, 0x0F00), (0x0180, 0x0000),
            (0xF667, 0xFFFE), (0x0180, 0x0FF0),
            (0xF675, 0xFFFE), (0x0180, 0x0000),
            (0xF687, 0xFFFE), (0x0180, 0x00FF),
            (0xF697, 0xFFFE), (0xF697, 0xFFFE), (0x0180, 0x0000),
            (0xF6B7, 0xFFFE), (0x0180, 0x0F0F),
            (0xF6C9, 0xFFFE), (0xF6C9, 0xFFFE), (0xF6C9, 0xFFFE),
            (0x0180, 0x0000),
            (0xFFFF, 0xFFFE));
        SetCopperPointer(bus, copperList);
        var frameStart = (long)AmigaConstants.A500PalCpuCyclesPerFrame;
        bus.AdvanceDmaTo(frameStart);
        var frame = new uint[716 * 285];
        var stopCycle = frameStart + (248 * AmigaConstants.A500PalCpuCyclesPerRasterLine);
        bus.Display.BeginPresentationFrame(new PresentationFrameTarget(frame), frameStart, stopCycle);
        StartCopper(bus);
        bus.AdvanceDmaTo(stopCycle);
        bus.Display.CompletePresentationFrame(stopCycle);

        var row = 246 - 26;
        var finalWrite = bus.Display.CopperDisplayWrites.Last(write => write.Address == 0x180);
        var line247 = frameStart + (247L * AmigaConstants.A500PalCpuCyclesPerRasterLine);
        Assert.Equal(
            line247 + (10L * AmigaConstants.A500PalCpuCyclesPerColorClock),
            finalWrite.Cycle);
        var finalMoveOpcode = copperList + (17u * 4u);
        Assert.Equal(
            new[] { 8L, 10L },
            bus.BusAccesses
                .Where(access =>
                    access.Request.Kind == AmigaBusAccessKind.Copper &&
                    (access.Request.Address == finalMoveOpcode ||
                     access.Request.Address == finalMoveOpcode + 2))
                .Select(access =>
                    (access.GrantedCycle - line247) /
                    AmigaConstants.A500PalCpuCyclesPerColorClock));
        var nonBlack = Enumerable.Range(0, 716)
            .Where(x => frame[(row * 716) + x] != 0xFF000000u)
            .ToArray();
        Assert.True(nonBlack.Length > 0, $"row contains no color; writes={string.Join(',', bus.Display.CopperDisplayWrites.Select(write => write.Cycle))}");
        Assert.NotEqual(0xFF000000u, frame[(row * 716) + 703]);
        Assert.All(frame.AsSpan((row * 716) + 704, 12).ToArray(), pixel =>
            Assert.Equal(0xFF000000u, pixel));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LineEndWaitWithDdfMovesClosesTrailingPresentationPixels(
        bool enableHardwareSpecialization)
    {
        var bus = CreateFivePlaneBus(enableHardwareSpecialization);
        const uint copperList = 0x2400;
        WriteCopperList(
            bus,
            copperList,
            (0x0100, 0x4200),
            (0x3001, 0xFFFE),
            (0x0180, 0x0F00),
            (0x30D9, 0xFFFE),
            (0x0092, 0x0038),
            (0x0094, 0x00D0),
            (0x0180, 0x0000),
            (0xFFFF, 0xFFFE));
        SetCopperPointer(bus, copperList);
        var frameStart = (long)AmigaConstants.A500PalCpuCyclesPerFrame;
        bus.AdvanceDmaTo(frameStart);
        var frame = new uint[716 * 285];
        var stopCycle = frameStart + (52 * AmigaConstants.A500PalCpuCyclesPerRasterLine);
        bus.Display.BeginPresentationFrame(new PresentationFrameTarget(frame), frameStart, stopCycle);
        StartCopper(bus);
        bus.AdvanceDmaTo(stopCycle);
        bus.Display.CompletePresentationFrame(stopCycle);

        var row = 48 - 26;
        var tail = frame.AsSpan((row * 716) + 712, 4).ToArray();
        Assert.True(
            tail.All(pixel => pixel == 0xFF000000u),
            string.Join(
                " | ",
                bus.Display.CopperDisplayWrites
                    .Where(write => write.Address is 0x092 or 0x094 or 0x180)
                    .Select(write =>
                    {
                        var beam = bus.GetBeamPosition(write.Cycle);
                        return $"{write.Address:X3}={write.Value:X4}@v{beam.BeamLine}h{beam.BeamHorizontal}";
                    })));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FourPlaneWaitRunCarriesFinalPreloadColorIntoLeftEdge(
        bool enableHardwareSpecialization)
    {
        var bus = CreateFivePlaneBus(enableHardwareSpecialization);
        const uint copperList = 0x2400;
        WriteCopperList(
            bus,
            copperList,
            (0x0100, 0x4200),
            (0x5801, 0xFFFE),
            (0x0180, 0x0F00),
            (0x58D9, 0xFFFE),
            (0x0092, 0x0038),
            (0x0094, 0x00D0),
            (0x0180, 0x0000),
            (0x6201, 0xFFFE),
            (0x0180, 0x0F00),
            (0x0180, 0x0FF0),
            (0x0180, 0x00FF),
            (0x0180, 0x0F00),
            (0x0180, 0x0FF0),
            (0x0180, 0x00FF),
            (0x0180, 0x0F00),
            (0x0180, 0x0FF0),
            (0x0180, 0x00FF),
            (0x0180, 0x0F00),
            (0x0180, 0x0FF0),
            (0x0180, 0x00FF),
            (0x0180, 0x0F00),
            (0x0180, 0x0FF0),
            (0x0180, 0x00FF),
            (0x0180, 0x0F00),
            (0x0180, 0x0FF0),
            (0x0180, 0x00FF),
            (0x0180, 0x0000),
            (0xFFFF, 0xFFFE));
        SetCopperPointer(bus, copperList);
        var frameStart = (long)AmigaConstants.A500PalCpuCyclesPerFrame;
        bus.AdvanceDmaTo(frameStart);
        var frame = new uint[716 * 285];
        var stopCycle = frameStart + (100 * AmigaConstants.A500PalCpuCyclesPerRasterLine);
        bus.Display.BeginPresentationFrame(new PresentationFrameTarget(frame), frameStart, stopCycle);
        StartCopper(bus);
        bus.AdvanceDmaTo(stopCycle);
        bus.Display.CompletePresentationFrame(stopCycle);

        var row = 98 - 26;
        Assert.True(
            frame[(row * 716)] == 0xFF00FFFFu,
            string.Join(
                " | ",
                bus.Display.CopperDisplayWrites
                    .Where(write => write.Address == 0x180)
                    .Select(write =>
                    {
                        var beam = bus.GetBeamPosition(write.Cycle);
                        return $"{write.Value:X4}@v{beam.BeamLine}h{beam.BeamHorizontal}";
                    })));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FourPlaneLineTailWaitFoldsFirstPostWrapPaletteWriteOnce(
        bool enableHardwareSpecialization)
    {
        var bus = CreateFivePlaneBus(enableHardwareSpecialization);
        const uint copperList = 0x2400;
        WriteCopperList(
            bus,
            copperList,
            (0x0100, 0x4200),
            (0x5801, 0xFFFE),
            (0x0180, 0x0F00),
            (0x58D9, 0xFFFE),
            (0x0092, 0x0038),
            (0x0094, 0x00D0),
            (0x0180, 0x0000),
            (0x62DB, 0xFFFE),
            (0x0180, 0x0F00),
            (0x0180, 0x0FF0),
            (0x0180, 0x00FF),
            (0x0180, 0x0F00),
            (0x0180, 0x0FF0),
            (0x0180, 0x00FF),
            (0x0180, 0x0F00),
            (0x0180, 0x0FF0),
            (0x0180, 0x00FF),
            (0x0180, 0x0F00),
            (0x0180, 0x0FF0),
            (0x0180, 0x00FF),
            (0x0180, 0x0F00),
            (0x0180, 0x0FF0),
            (0x0180, 0x00FF),
            (0x0180, 0x0F00),
            (0x0180, 0x0FF0),
            (0x0180, 0x00FF),
            (0x0180, 0x0000),
            (0xFFFF, 0xFFFE));
        SetCopperPointer(bus, copperList);
        var frameStart = (long)AmigaConstants.A500PalCpuCyclesPerFrame;
        bus.AdvanceDmaTo(frameStart);
        var frame = new uint[716 * 285];
        var stopCycle = frameStart + (102 * AmigaConstants.A500PalCpuCyclesPerRasterLine);
        bus.Display.BeginPresentationFrame(new PresentationFrameTarget(frame), frameStart, stopCycle);
        StartCopper(bus);
        bus.AdvanceDmaTo(stopCycle);
        bus.Display.CompletePresentationFrame(stopCycle);
        var tail = frame.AsSpan(((98 - 26) * 716) + 680, 36).ToArray();
        Assert.All(tail.AsSpan(0, 4).ToArray(), pixel => Assert.Equal(0xFF000000u, pixel));
        Assert.All(tail.AsSpan(4, 20).ToArray(), pixel => Assert.Equal(0xFFFF0000u, pixel));
        Assert.All(tail.AsSpan(24, 12).ToArray(), pixel => Assert.Equal(0xFFFFFF00u, pixel));

        var boundaryGrants = bus.BusAccesses
            .Where(access =>
            {
                var beam = bus.GetBeamPosition(access.GrantedCycle);
                return access.Request.Kind == AmigaBusAccessKind.Copper &&
                       access.Request.Address is >= 0x2420 and <= 0x2426 &&
                       beam.BeamLine is 98 or 99;
            })
            .Select(access =>
            {
                var beam = bus.GetBeamPosition(access.GrantedCycle);
                return (beam.BeamLine, beam.BeamHorizontal);
            })
            .ToArray();
        Assert.Equal(
            new[] { (98, 224), (98, 226), (99, 8), (99, 10) },
            boundaryGrants);

        var nextRow = frame.AsSpan((99 - 26) * 716, 92).ToArray();
        Assert.All(nextRow.AsSpan(0, 4).ToArray(), pixel => Assert.Equal(0xFFFFFF00u, pixel));
        Assert.All(nextRow.AsSpan(4, 16).ToArray(), pixel => Assert.Equal(0xFF00FFFFu, pixel));
        Assert.All(nextRow.AsSpan(20, 16).ToArray(), pixel => Assert.Equal(0xFFFF0000u, pixel));
        Assert.All(nextRow.AsSpan(36, 16).ToArray(), pixel => Assert.Equal(0xFFFFFF00u, pixel));
        Assert.All(nextRow.AsSpan(52, 16).ToArray(), pixel => Assert.Equal(0xFF00FFFFu, pixel));
        Assert.All(nextRow.AsSpan(68, 24).ToArray(), pixel => Assert.Equal(0xFF000000u, pixel));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InheritedRgaCarryIsConsumedOnceAcrossImmediateWaitGroups(
        bool enableHardwareSpecialization)
    {
        var bus = CreateFivePlaneBus(enableHardwareSpecialization);
        const uint copperList = 0x2400;
        WriteCopperList(
            bus,
            copperList,
            (0x6245, 0xFFFE), (0x0180, 0x0F00), (0x0180, 0x0000),
            (0x6265, 0xFFFE), (0x0180, 0x0FF0),
            (0x6265, 0xFFFE), (0x0180, 0x0000),
            (0x6285, 0xFFFE), (0x0180, 0x00FF),
            (0x6285, 0xFFFE), (0x6285, 0xFFFE), (0x0180, 0x0000),
            (0x62B5, 0xFFFE), (0x0180, 0x0F0F),
            (0x62B5, 0xFFFE), (0x62B5, 0xFFFE), (0x62B5, 0xFFFE),
            (0x0180, 0x0000),
            (0x6447, 0xFFFE), (0x0180, 0x0F00), (0x0180, 0x0000),
            (0x6467, 0xFFFE), (0x0180, 0x0FF0),
            (0x6269, 0xFFFE), (0x0180, 0x0000),
            (0x6487, 0xFFFE), (0x0180, 0x00FF),
            (0x6489, 0xFFFE), (0x6489, 0xFFFE), (0x0180, 0x0000),
            (0x64B7, 0xFFFE), (0x0180, 0x0F0F),
            (0x64B9, 0xFFFE), (0x64B9, 0xFFFE), (0x64B9, 0xFFFE),
            (0x0180, 0x0000),
            (0xFFFF, 0xFFFE));
        SetCopperPointer(bus, copperList);
        var frameStart = (long)AmigaConstants.A500PalCpuCyclesPerFrame;
        bus.AdvanceDmaTo(frameStart);
        var frame = new uint[716 * 285];
        var stopCycle = frameStart + (102 * AmigaConstants.A500PalCpuCyclesPerRasterLine);
        bus.Display.BeginPresentationFrame(new PresentationFrameTarget(frame), frameStart, stopCycle);
        StartCopper(bus);
        bus.AdvanceDmaTo(stopCycle);
        bus.Display.CompletePresentationFrame(stopCycle);

        var writes = bus.Display.CopperDisplayWrites
            .Where(write => write.Address == 0x180)
            .Select(write => write.Cycle)
            .Take(16)
            .ToArray();
        var line98 = frameStart + (98L * AmigaConstants.A500PalCpuCyclesPerRasterLine);
        var line100 = frameStart + (100L * AmigaConstants.A500PalCpuCyclesPerRasterLine);
        Assert.Equal(
            new[]
            {
                line98 + 156, line98 + 164, line98 + 220, line98 + 252,
                line98 + 284, line98 + 336, line98 + 380, line98 + 452,
                line100 + 160, line100 + 172, line100 + 224, line100 + 256,
                line100 + 288, line100 + 340, line100 + 384, line100 + 456
            },
            writes);
        Assert.Contains(
            bus.Display.CaptureCopperTimelineSegments(74),
            segment => segment.XStart == 346 && segment.XStop == 358 && segment.Color0 == 0);
        Assert.NotEqual(0xFF000000u, frame[(74 * 716) + 691]);
        Assert.Equal(0xFF000000u, frame[(74 * 716) + 692]);
    }



    private static AmigaBus CreateFivePlaneBus(bool enableHardwareSpecialization = false)
    {
        var bus = new AmigaBus(
            captureBusAccesses: true,
            enableLiveAgnusDma: true,
            enableLiveDisplayDma: true,
            enableHardwareSpecialization: enableHardwareSpecialization);
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
