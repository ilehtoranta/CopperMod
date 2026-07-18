using CopperMod.Amiga.Core;
using CopperMod.Amiga.Runtime;

namespace CopperMod.Amiga.Tests;

public sealed class AgnusEcsBeamControlTests
{
    private const uint Custom = 0x00DFF000;

    [Fact]
    public void OcsPalBeamFoundationRetainsExistingGeometry()
    {
        var bus = new AmigaBus();

        var last = bus.GetBeamPosition(AmigaConstants.A500PalCpuCyclesPerRasterLine - 1);
        var next = bus.GetBeamPosition(AmigaConstants.A500PalCpuCyclesPerRasterLine);

        Assert.Equal(0, last.BeamLine);
        Assert.Equal(AmigaConstants.A500PalColorClocksPerRasterLine - 1, last.BeamHorizontal);
        Assert.Equal(1, next.BeamLine);
        Assert.Equal(0, next.BeamHorizontal);
        Assert.Equal(AmigaConstants.A500PalCpuCyclesPerRasterLine, bus.LineCycles);
    }

    [Theory]
    [InlineData(0x1C0, 0xFFFF, 0x01FF)]
    [InlineData(0x1C2, 0xFFFF, 0x01FF)]
    [InlineData(0x1C4, 0xFFFF, 0x01FF)]
    [InlineData(0x1C6, 0xFFFF, 0x01FF)]
    [InlineData(0x1C8, 0xFFFF, 0x07FF)]
    [InlineData(0x1CA, 0xFFFF, 0x07FF)]
    [InlineData(0x1CC, 0xFFFF, 0x07FF)]
    [InlineData(0x1CE, 0xFFFF, 0x07FF)]
    [InlineData(0x1D0, 0xFFFF, 0x01FF)]
    [InlineData(0x1D2, 0xFFFF, 0x01FF)]
    [InlineData(0x1D4, 0xFFFF, 0x01FF)]
    [InlineData(0x1D6, 0xFFFF, 0x01FF)]
    [InlineData(0x1D8, 0xFFFF, 0x01FF)]
    [InlineData(0x1DC, 0xFFFF, 0x7FFF)]
    [InlineData(0x1DE, 0xFFFF, 0x01FF)]
    [InlineData(0x1E0, 0xFFFF, 0x07FF)]
    [InlineData(0x1E2, 0xFFFF, 0x01FF)]
    public void EcsRegistersStoreMaskedValues(ushort offset, ushort written, ushort expected)
    {
        var bus = CreateEcsBus();
        bus.WriteWord(Custom + offset, written, 0);

        var readCycle = 8L;
        Assert.Equal(expected, bus.ReadWord(Custom + offset, ref readCycle, AmigaBusAccessKind.CpuDataRead));
    }

    [Fact]
    public void StoredTotalsRemainInertUntilVariableBeamIsEnabled()
    {
        var bus = CreateEcsBus();
        bus.WriteWord(Custom + 0x1C0, 99, 0);
        bus.WriteWord(Custom + 0x1C8, 199, 0);

        Assert.Equal(AmigaConstants.A500PalCpuCyclesPerRasterLine, bus.LineCycles);
        Assert.Equal(AmigaConstants.A500PalLongRasterLines, bus.GetBeamPosition(0).RasterLines);
    }

    [Fact]
    public void VariableBeamTotalsChangeLineAndFrameGeometry()
    {
        var bus = CreateEcsBus();
        bus.WriteWord(Custom + 0x1C0, 99, 0);
        bus.WriteWord(Custom + 0x1C8, 199, 0);
        bus.WriteWord(Custom + 0x1DC, 0x0080, 0);

        Assert.Equal(200, bus.LineCycles);
        var endOfLine = bus.GetBeamPosition(199);
        var nextLine = bus.GetBeamPosition(200);
        Assert.Equal(0, endOfLine.BeamLine);
        Assert.Equal(99, endOfLine.BeamHorizontal);
        Assert.Equal(1, nextLine.BeamLine);
        Assert.Equal(200, nextLine.RasterLines);
    }

    [Fact]
    public void HhposrReturnsCurrentExtendedHorizontalPosition()
    {
        var bus = CreateEcsBus();
        var cycle = 40L;
        var readCycle = cycle;

        var value = bus.ReadWord(Custom + 0x1DA, ref readCycle, AmigaBusAccessKind.CpuDataRead);

        Assert.Equal(bus.GetBeamPosition(cycle).BeamHorizontal, value & 0x01FF);
    }

    private static AmigaBus CreateEcsBus()
        => new(chipset: AmigaChipset.EcsPal);
}
