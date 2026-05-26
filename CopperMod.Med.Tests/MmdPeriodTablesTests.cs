using CopperMod.Med;

namespace CopperMod.Med.Tests;

public sealed class MmdPeriodTablesTests
{
    [Theory]
    [InlineData(-8, 0, false, 907)]
    [InlineData(-1, 0, false, 862)]
    [InlineData(0, 0, false, 856)]
    [InlineData(1, 0, false, 850)]
    [InlineData(7, 0, false, 814)]
    [InlineData(7, 35, false, 108)]
    public void UsesOriginalFinetunePeriodTables(sbyte finetune, int noteIndex, bool extended, int expected)
    {
        Assert.Equal(expected, MmdPeriodTables.GetPeriodByIndex(noteIndex, finetune, extended));
    }

    [Theory]
    [InlineData(-8, 0, 3628)]
    [InlineData(0, 0, 3424)]
    [InlineData(1, 0, 3400)]
    [InlineData(7, 0, 3256)]
    [InlineData(7, 24, 814)]
    public void ExtendedLowTablesFollowSelectedFinetune(sbyte finetune, int noteIndex, int expected)
    {
        Assert.Equal(expected, MmdPeriodTables.GetPeriodByIndex(noteIndex, finetune, useExtendedLowTable: true));
    }

    [Fact]
    public void SampleStepUsesPaulaAudioClockWithoutExtraDivider()
    {
        var expected = MmdConstants.PalPaulaClock / 428.0 / 44100.0;

        Assert.Equal(expected, MmdPeriodTables.GetSampleStep(428, 44100), precision: 12);
    }
}
