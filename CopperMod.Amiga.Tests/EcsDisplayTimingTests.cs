using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class EcsDisplayTimingTests
{
    private const uint CustomBase = 0x00DFF000;

    [Fact]
    public void ExplicitOcsPalBeamMatchesLegacyDefaultAtEveryBoundarySample()
    {
        var legacy = new AmigaBus();
        var explicitOcs = new AmigaBus(chipset: AmigaChipset.OcsPal);
        var samples = new long[]
        {
            0,
            1,
            AmigaConstants.A500PalCpuCyclesPerRasterLine - 1,
            AmigaConstants.A500PalCpuCyclesPerRasterLine,
            AmigaConstants.A500PalCpuCyclesPerFrame - 1L,
            AmigaConstants.A500PalCpuCyclesPerFrame,
            AmigaConstants.A500PalCpuCyclesPerFrame + AmigaConstants.A500PalCpuCyclesPerRasterLine
        };

        Assert.Equal(legacy.LineCycles, explicitOcs.LineCycles);
        foreach (var cycle in samples)
        {
            Assert.Equal(legacy.GetBeamPosition(cycle), explicitOcs.GetBeamPosition(cycle));
            Assert.Equal(legacy.GetLineStartCycle(cycle), explicitOcs.GetLineStartCycle(cycle));
            Assert.Equal(legacy.GetLineStopCycle(cycle), explicitOcs.GetLineStopCycle(cycle));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NtscLinesAlternateBetween227And228ColorClocks(bool ecs)
    {
        var bus = new AmigaBus(chipset: ecs ? AmigaChipset.EcsNtsc : AmigaChipset.OcsNtsc);

        AssertBeam(bus, 0, line: 0, horizontal: 0, lineCycles: 454, lineStart: 0);
        AssertBeam(bus, 453, line: 0, horizontal: 226, lineCycles: 454, lineStart: 0);
        AssertBeam(bus, 454, line: 1, horizontal: 0, lineCycles: 456, lineStart: 454);
        AssertBeam(bus, 909, line: 1, horizontal: 227, lineCycles: 456, lineStart: 454);
        AssertBeam(bus, 910, line: 2, horizontal: 0, lineCycles: 454, lineStart: 910);
    }

    [Fact]
    public void NtscFieldsAlternateBetween263And262LinesWithoutLosingPhase()
    {
        var bus = new AmigaBus(chipset: AmigaChipset.EcsNtsc);
        var longFieldCycles = RasterTiming.Ntsc.GetFrameCycles(263);
        var shortFieldCycles = RasterTiming.Ntsc.GetFrameCycles(262);

        var first = bus.GetBeamPosition(0);
        var second = bus.GetBeamPosition(longFieldCycles);
        var third = bus.GetBeamPosition(longFieldCycles + shortFieldCycles);

        Assert.True(first.IsLongFrame);
        Assert.Equal(263, first.RasterLines);
        Assert.Equal(1, second.FrameNumber);
        Assert.False(second.IsLongFrame);
        Assert.Equal(262, second.RasterLines);
        Assert.Equal(2, third.FrameNumber);
        Assert.True(third.IsLongFrame);
        Assert.Equal(263, third.RasterLines);
    }

    [Fact]
    public void NtscHorizontalTodAndVerticalBlankSchedulingFollowExplicitLineStarts()
    {
        var bus = new AmigaBus(chipset: AmigaChipset.EcsNtsc);
        var longFieldCycles = RasterTiming.Ntsc.GetFrameCycles(263);
        var shortFieldCycles = RasterTiming.Ntsc.GetFrameCycles(262);

        Assert.Equal(454, bus.NextHorizontalSyncCycle);
        Assert.Equal(longFieldCycles, bus.NextVerticalBlankCycle);

        bus.AdvanceRasterTo(454);
        Assert.Equal(910, bus.NextHorizontalSyncCycle);

        bus.AdvanceRasterTo(longFieldCycles);
        Assert.Equal(longFieldCycles + shortFieldCycles, bus.NextVerticalBlankCycle);
    }

    [Fact]
    public void NtscFixedDmaAndCpuSlotsRemainRelativeToEachExplicitLineStart()
    {
        var bus = new AmigaBus(chipset: AmigaChipset.EcsNtsc);
        const long thirdLineStart = 910;

        Assert.Equal(thirdLineStart + (0x08 * 2), bus.PredictDiskDmaGrantCycle(thirdLineStart));
        Assert.True(bus.TryReserveDiskDmaWordExactSlot(
            0x2000,
            isWrite: false,
            thirdLineStart + (0x08 * 2),
            out var disk));
        Assert.Equal(thirdLineStart + (0x08 * 2), disk.GrantedCycle);

        Assert.True(bus.TryReservePaulaDmaWordExactSlot(
            channel: 0,
            address: 0x2200,
            slotCycle: thirdLineStart + (0x10 * 2),
            out var audio));
        Assert.Equal(thirdLineStart + (0x10 * 2), audio.GrantedCycle);

        Assert.True(bus.TryReserveSpriteDmaWordExactSlot(
            0x2400,
            thirdLineStart + (0x18 * 2),
            out var sprite));
        Assert.Equal(thirdLineStart + (0x18 * 2), sprite.GrantedCycle);

        var engine = new AgnusHrmSlotEngine();
        engine.BeamPositionProvider = bus.GetBeamPosition;
        engine.LineStartCycleProvider = bus.GetLineStartCycle;
        engine.NextLineStartCycleProvider = bus.GetNextLineStartCycle;
        var request = new AmigaBusAccessRequest(
            AmigaBusRequester.Cpu,
            AmigaBusAccessKind.CpuDataRead,
            AmigaBusAccessTarget.ChipRam,
            0x2600,
            AmigaBusAccessSize.Word,
            thirdLineStart,
            isWrite: false);
        var grant = engine.Arbitrate(
            request,
            new AmigaBusAccessResult(request, thirdLineStart, thirdLineStart));

        Assert.Equal(thirdLineStart + 2, grant.GrantedCycle);
    }

    [Fact]
    public void VariableBeamTotalsReplaceNtscAlternationFromTheWriteCycle()
    {
        var bus = new AmigaBus(chipset: AmigaChipset.EcsNtsc);
        bus.WriteWord(CustomBase + 0x1C0, 99, 0);
        bus.WriteWord(CustomBase + 0x1C8, 199, 0);
        bus.WriteWord(CustomBase + 0x1DC, AgnusRegisterBank.VarBeamEnable, 0);

        Assert.Equal(200, bus.LineCycles);
        Assert.Equal(0, bus.GetBeamPosition(199).BeamLine);
        Assert.Equal(99, bus.GetBeamPosition(199).BeamHorizontal);
        Assert.Equal(1, bus.GetBeamPosition(200).BeamLine);
        Assert.Equal(200, bus.GetBeamPosition(0).RasterLines);
        Assert.Equal(40_000, bus.GetNextFrameStartCycle(0));
    }

    [Fact]
    public void NtscEcsBeamRegisterResetTotalsMatchTheSelectedProfile()
    {
        var bus = new AmigaBus(chipset: AmigaChipset.EcsNtsc);
        var cycle = 0L;

        Assert.Equal(226, bus.ReadWord(CustomBase + 0x1C0, ref cycle, AmigaBusAccessKind.CpuDataRead));
        Assert.Equal(262, bus.ReadWord(CustomBase + 0x1C8, ref cycle, AmigaBusAccessKind.CpuDataRead));
    }

    [Fact]
    public void EcsNtscDisplayUsesNtscCaptureGeometryWithoutPalDimensions()
    {
        var bus = new AmigaBus(chipset: AmigaChipset.EcsNtsc);

        Assert.Equal(AmigaConstants.NtscLowResWidth * 4, bus.Display.Width);
        Assert.Equal(AmigaConstants.NtscLowResHeight * 2, bus.Display.Height);

        var frame = new uint[bus.Display.Width * bus.Display.Height];
        bus.Display.RenderFrame(frame);
        Assert.All(frame, pixel => Assert.Equal(0xFF000000u, pixel));
    }

    [Fact]
    public void MidFrameSyncWritesRescheduleFutureEventsWithoutReanchoringTheBeam()
    {
        var bus = new AmigaBus(chipset: AmigaChipset.EcsNtsc);
        const ushort beamcon0 = AgnusRegisterBank.VarHSyncEnable |
            AgnusRegisterBank.VarVSyncEnable |
            AgnusRegisterBank.VarVBlankEnable;

        bus.WriteWord(CustomBase + 0x1DC, beamcon0, 0);
        bus.WriteWord(CustomBase + 0x1DE, 10, 100);
        bus.WriteWord(CustomBase + 0x1E0, 40, 100);
        bus.WriteWord(CustomBase + 0x1CC, 50, 100);

        var readCycle = 200L;
        Assert.Equal(beamcon0, bus.ReadWord(CustomBase + 0x1DC, ref readCycle, AmigaBusAccessKind.CpuDataRead));
        Assert.Equal(10, bus.ReadWord(CustomBase + 0x1DE, ref readCycle, AmigaBusAccessKind.CpuDataRead));
        Assert.True(bus.AgnusRegisters.VariableHSyncEnabled);
        Assert.Equal(0, bus.GetBeamPosition(100).BeamLine);
        Assert.Equal(100, bus.GetBeamPosition(100).CurrentCycle);
        Assert.Equal(454 + 20, bus.NextHorizontalSyncCycle);
        Assert.Equal(bus.GetLineStartCycle(0, 50), bus.NextVerticalBlankCycle);
    }

    private static void AssertBeam(
        AmigaBus bus,
        long cycle,
        int line,
        int horizontal,
        int lineCycles,
        long lineStart)
    {
        var beam = bus.GetBeamPosition(cycle);
        Assert.Equal(line, beam.BeamLine);
        Assert.Equal(horizontal, beam.BeamHorizontal);
        Assert.Equal(lineCycles, bus.GetLineCyclesAt(cycle));
        Assert.Equal(lineStart, bus.GetLineStartCycle(cycle));
    }
}
