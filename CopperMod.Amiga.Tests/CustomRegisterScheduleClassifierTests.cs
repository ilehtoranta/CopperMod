namespace CopperMod.Amiga.Tests;

public sealed class CustomRegisterScheduleClassifierTests
{
    [Fact]
    public void PotentialImpactIsNormalizedAndUnknownWritesRemainConservative()
    {
        Assert.Equal(
            HardwareScheduleImpact.Composition,
            CustomRegisterScheduleClassifier.GetPotentialImpact(AmigaChipset.OcsPal, 0x181));
        Assert.Equal(
            HardwareScheduleImpact.All,
            CustomRegisterScheduleClassifier.GetPotentialImpact(AmigaChipset.EcsPal, 0x0DC));
        Assert.Equal(
            HardwareScheduleImpact.None,
            CustomRegisterScheduleClassifier.GetPotentialEventScheduleImpact(
                AmigaChipset.EcsPal,
                (ushort)CustomRegister.Bpl1mod));
    }

    [Fact]
    public void DiwHighImpactFollowsTheOwningChipModels()
    {
        var ecsAgnus = new AmigaChipset(DmaChipModel.EcsAgnus, DisplayChipModel.OcsDenise, VideoStandard.Pal);
        var ecsDenise = new AmigaChipset(DmaChipModel.OcsAgnus, DisplayChipModel.EcsDenise, VideoStandard.Pal);

        Assert.Equal(
            HardwareScheduleImpact.Bitplane,
            CustomRegisterScheduleClassifier.GetChangedImpact(ecsAgnus, 0x1E4, 0, 0x2121));
        Assert.Equal(
            HardwareScheduleImpact.Composition,
            CustomRegisterScheduleClassifier.GetChangedImpact(ecsDenise, 0x1E4, 0, 0x2121));
        Assert.Equal(
            HardwareScheduleImpact.Bitplane | HardwareScheduleImpact.Composition,
            CustomRegisterScheduleClassifier.GetChangedImpact(AmigaChipset.EcsPal, 0x1E4, 0, 0x2121));
        Assert.Equal(
            HardwareScheduleImpact.None,
            CustomRegisterScheduleClassifier.GetChangedImpact(AmigaChipset.OcsPal, 0x1E4, 0, 0x2121));
        Assert.Equal(
            HardwareScheduleImpact.None,
            CustomRegisterScheduleClassifier.GetChangedImpact(AmigaChipset.EcsPal, 0x1E4, 0x2121, 0x2121));
    }

    [Theory]
    [InlineData(0x1C0)]
    [InlineData(0x1C8)]
    [InlineData(0x1D8)]
    [InlineData(0x1DC)]
    [InlineData(0x1DE)]
    [InlineData(0x1E0)]
    [InlineData(0x1E2)]
    public void EcsProgrammableBeamWritesHaveRasterImpact(ushort offset)
    {
        Assert.Equal(
            HardwareScheduleImpact.Raster,
            CustomRegisterScheduleClassifier.GetPotentialImpact(AmigaChipset.EcsPal, offset));
        Assert.Equal(
            HardwareScheduleImpact.None,
            CustomRegisterScheduleClassifier.GetPotentialImpact(AmigaChipset.OcsPal, offset));
    }

    [Fact]
    public void ReadOnlyHorizontalBeamPositionHasNoWriteImpact()
    {
        Assert.Equal(
            HardwareScheduleImpact.None,
            CustomRegisterScheduleClassifier.GetPotentialImpact(AmigaChipset.EcsPal, 0x1DA));
    }

    [Fact]
    public void Bplcon0ChangedImpactSeparatesFetchAndCompositionFields()
    {
        Assert.Equal(
            HardwareScheduleImpact.Bitplane | HardwareScheduleImpact.Composition,
            CustomRegisterScheduleClassifier.GetChangedImpact(AmigaChipset.EcsPal, 0x100, 0, 0x0040));
        Assert.Equal(
            HardwareScheduleImpact.Composition,
            CustomRegisterScheduleClassifier.GetChangedImpact(AmigaChipset.OcsPal, 0x100, 0, 0x0040));
        Assert.Equal(
            HardwareScheduleImpact.Composition,
            CustomRegisterScheduleClassifier.GetChangedImpact(AmigaChipset.EcsPal, 0x100, 0, 0x0400));
        Assert.Equal(
            HardwareScheduleImpact.None,
            CustomRegisterScheduleClassifier.GetChangedImpact(AmigaChipset.EcsPal, 0x100, 0x2041, 0x2041));
    }

    [Fact]
    public void Bplcon3ChangedImpactIsMaskedAndFieldSensitive()
    {
        Assert.Equal(
            HardwareScheduleImpact.Sprite | HardwareScheduleImpact.Composition,
            CustomRegisterScheduleClassifier.GetChangedImpact(AmigaChipset.EcsPal, 0x106, 0, 0x0002));
        Assert.Equal(
            HardwareScheduleImpact.Composition,
            CustomRegisterScheduleClassifier.GetChangedImpact(AmigaChipset.EcsPal, 0x106, 0, 0x0010));
        Assert.Equal(
            HardwareScheduleImpact.None,
            CustomRegisterScheduleClassifier.GetChangedImpact(AmigaChipset.EcsPal, 0x106, 0, 0x0008));
        Assert.Equal(
            HardwareScheduleImpact.None,
            CustomRegisterScheduleClassifier.GetChangedImpact(AmigaChipset.OcsPal, 0x106, 0, 0x0037));
    }

    [Fact]
    public void ExtendedBlitterSizesAreEcsScheduleImpacts()
    {
        Assert.Equal(
            HardwareScheduleImpact.Blitter,
            CustomRegisterScheduleClassifier.GetPotentialImpact(AmigaChipset.EcsPal, 0x05C));
        Assert.Equal(
            HardwareScheduleImpact.Blitter,
            CustomRegisterScheduleClassifier.GetPotentialImpact(AmigaChipset.EcsPal, 0x05E));
        Assert.Equal(
            HardwareScheduleImpact.None,
            CustomRegisterScheduleClassifier.GetPotentialImpact(AmigaChipset.OcsPal, 0x05C));
        Assert.Equal(
            HardwareScheduleImpact.None,
            CustomRegisterScheduleClassifier.GetPotentialImpact(AmigaChipset.OcsPal, 0x05E));
        Assert.Equal(
            HardwareScheduleImpact.Blitter,
            CustomRegisterScheduleClassifier.GetPotentialImpact(AmigaChipset.OcsPal, 0x058));
    }
}
