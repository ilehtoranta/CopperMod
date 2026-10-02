namespace CopperMod.Sid.Tests;

public sealed class SidNoiseReferenceTests
{
    // Register/output vectors independently derived from the reference's 23-bit
    // register convention: reset release 0x7ffffe; DAC taps 20,18,14,11,9,5,2,0.
    // See docs/SID-noise-investigation-2026-09-26.md for the coordinate proof.
    [Theory]
    [InlineData((int)SidChipModel.Mos6581, (int)SidEmulationProfile.Balanced)]
    [InlineData((int)SidChipModel.Mos6581, (int)SidEmulationProfile.ReferenceMeasured)]
    [InlineData((int)SidChipModel.Mos8580, (int)SidEmulationProfile.Balanced)]
    [InlineData((int)SidChipModel.Mos8580, (int)SidEmulationProfile.ReferenceMeasured)]
    public void ShortTestReleaseKeepsRegisterAndDacInTheSameCoordinateSystem(int modelValue, int profileValue)
    {
        var model = (SidChipModel)modelValue;
        var voice = new SidVoice();
        voice.ConfigureEmulationProfile((SidEmulationProfile)profileValue, model);
        voice.Reset();
        voice.Write(4, 0x80);
        for (var shift = 0; shift < 1137; shift++) Shift(voice);

        // Both conventions agree here. Simply clocking pure noise cannot expose
        // the mismatched TEST feedback operation in the old representation.
        Assert.Equal(0xC50u, voice.GetDebugState().NoiseDac);
        voice.Write(4, 0x88);
        voice.ClockOscillator();
        voice.ClockNoise(false);
        Assert.Equal(0xC50u, voice.GetDebugState().NoiseDac);
        voice.Write(4, 0x80);
        voice.ClockOscillator();
        voice.ClockNoise(false);

        uint[] expectedDacs = [0x370, 0xA20, 0x420, 0x2C0, 0xCC0, 0x550, 0x900, 0xCA0, 0x280, 0xB90, 0x150, 0x120];
        foreach (var expected in expectedDacs)
        {
            Assert.Equal(expected, voice.GetDebugState().NoiseDac);
            voice.RefreshRegisterObservableReadback(null, model);
            Assert.Equal((byte)(expected >> 4), voice.ReadOscillator(null, model));
            Shift(voice);
        }
    }

    private static void Shift(SidVoice voice)
    {
        voice.ClockNoise(true);
        voice.ClockNoise(false);
        voice.ClockNoise(false);
    }
}
