namespace CopperMod.Amiga.Tests;

public sealed class DisplayGeometryDecoderTests
{
    [Fact]
    public void OcsDisplayWindowUsesLegacyImplicitStopBitsAndVerticalWrap()
    {
        var window = DisplayGeometryDecoder.DecodeDisplayWindow(
            DisplayChipModel.OcsDenise,
            0xF081,
            0x20C1,
            0x2F2F,
            diwHighValid: true);

        Assert.Equal(new DisplayWindow(0x081, 0x1C1, 0x0F0, 0x120), window);
    }

    [Fact]
    public void EcsDisplayWindowDecodesHorizontalAndAllVerticalHighBits()
    {
        var window = DisplayGeometryDecoder.DecodeDisplayWindow(
            DisplayChipModel.EcsDenise,
            0x3412,
            0x7856,
            0x2F2F,
            diwHighValid: true);

        Assert.Equal(new DisplayWindow(0x112, 0x156, 0xF34, 0xF78), window);
    }

    [Fact]
    public void EcsDisplayWindowWrapsInNineAndTwelveBitDomains()
    {
        var window = DisplayGeometryDecoder.DecodeDisplayWindow(
            DmaChipModel.EcsAgnus,
            0xF0F0,
            0x1010,
            0x000F,
            diwHighValid: true);

        Assert.Equal(new DisplayWindow(0x0F0, 0x210, 0xFF0, 0x1010), window);
    }

    [Fact]
    public void InvalidDiwHighFallsBackToLegacyDecode()
    {
        var ecs = DisplayGeometryDecoder.DecodeDisplayWindow(
            DisplayChipModel.EcsDenise,
            0x2C81,
            0x2CC1,
            0x2F2F,
            diwHighValid: false);
        var ocs = DisplayGeometryDecoder.DecodeDisplayWindow(
            DisplayChipModel.OcsDenise,
            0x2C81,
            0x2CC1,
            0,
            diwHighValid: false);

        Assert.Equal(ocs, ecs);
    }

    [Fact]
    public void MixedChipModelsDecodeTheirOwnDiwHighSupport()
    {
        var agnus = DisplayGeometryDecoder.DecodeDisplayWindow(
            DmaChipModel.EcsAgnus,
            0x2C81,
            0x2CC1,
            0x2020,
            diwHighValid: true);
        var denise = DisplayGeometryDecoder.DecodeDisplayWindow(
            DisplayChipModel.OcsDenise,
            0x2C81,
            0x2CC1,
            0x2020,
            diwHighValid: true);

        Assert.Equal(0x181, agnus.HorizontalStart);
        Assert.Equal(0x1C1, agnus.HorizontalStop);
        Assert.Equal(0x081, denise.HorizontalStart);
        Assert.Equal(0x1C1, denise.HorizontalStop);
    }

    [Theory]
    [InlineData(false, 0x0000, 0x003C, 0x00B4, 0x0040, 0x00B8, 0)]
    [InlineData(false, 0x8000, 0x003E, 0x00B6, 0x003C, 0x00B4, 1)]
    [InlineData(true, 0x0040, 0x003D, 0x00B5, 0x003C, 0x00B4, 2)]
    public void DataFetchWindowUsesAgnusResolutionAndComparatorAlignment(
        bool ecs,
        ushort bplcon0,
        ushort ddfStart,
        ushort ddfStop,
        int expectedStart,
        int expectedStop,
        int expectedResolution)
    {
        var window = DisplayGeometryDecoder.DecodeDataFetchWindow(
            ecs ? DmaChipModel.EcsAgnus : DmaChipModel.OcsAgnus,
            bplcon0,
            ddfStart,
            ddfStop);

        Assert.Equal(new DataFetchWindow(expectedStart, expectedStop, (DeniseResolution)expectedResolution), window);
    }

    [Theory]
    [InlineData(0x0038, 0x00B0, 16)]
    [InlineData(0x003C, 0x00B0, 16)]
    [InlineData(0x003C, 0x00B4, 16)]
    public void LowResolutionFetchCountPreservesSecondHalfComparatorRules(
        ushort ddfStart,
        ushort ddfStop,
        int expectedWords)
    {
        var window = DisplayGeometryDecoder.DecodeDataFetchWindow(
            DmaChipModel.OcsAgnus,
            0,
            ddfStart,
            ddfStop);

        Assert.Equal(
            expectedWords,
            DisplayGeometryDecoder.GetDataFetchWordCount(window, ddfStart, ddfStop, maximum: 128));
    }

    [Theory]
    [InlineData(0x8000, 0x003C, 0x00D0, 40)]
    [InlineData(0x0040, 0x003C, 0x00D0, 76)]
    public void ExtendedResolutionFetchCountsUseEffectiveComparatorBounds(
        ushort bplcon0,
        ushort ddfStart,
        ushort ddfStop,
        int expectedWords)
    {
        var window = DisplayGeometryDecoder.DecodeDataFetchWindow(
            DmaChipModel.EcsAgnus,
            bplcon0,
            ddfStart,
            ddfStop);

        Assert.Equal(
            expectedWords,
            DisplayGeometryDecoder.GetDataFetchWordCount(window, ddfStart, ddfStop, maximum: 128));
    }

    [Fact]
    public void ReversedEffectiveDataFetchRangeProducesNoFetches()
    {
        var window = DisplayGeometryDecoder.DecodeDataFetchWindow(
            DmaChipModel.EcsAgnus,
            0,
            0x00D0,
            0x0038);

        Assert.Equal(0, DisplayGeometryDecoder.GetDataFetchWordCount(window, 0x00D0, 0x0038, maximum: 128));
    }
}
