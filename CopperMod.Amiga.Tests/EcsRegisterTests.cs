using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class EcsRegisterTests
{
    private const uint CustomBase = 0x00DFF000;

    public static TheoryData<ushort, int, ushort> EcsOnlyRegisterMatrix => new()
    {
        { 0x05A, (int)CustomRegisterPresence.EcsAgnus, 0x00FF },
        { 0x05C, (int)CustomRegisterPresence.EcsAgnus, 0x7FFF },
        { 0x05E, (int)CustomRegisterPresence.EcsAgnus, 0x07FF },
        { 0x07C, (int)CustomRegisterPresence.EcsDenise, 0x0000 },
        { 0x106, (int)CustomRegisterPresence.EcsDenise, 0x0037 },
        { 0x1C0, (int)CustomRegisterPresence.EcsAgnus, 0x01FF },
        { 0x1C2, (int)CustomRegisterPresence.EcsAgnus, 0x01FF },
        { 0x1C4, (int)CustomRegisterPresence.EcsAgnus, 0x01FF },
        { 0x1C6, (int)CustomRegisterPresence.EcsAgnus, 0x01FF },
        { 0x1C8, (int)CustomRegisterPresence.EcsAgnus, 0x07FF },
        { 0x1CA, (int)CustomRegisterPresence.EcsAgnus, 0x07FF },
        { 0x1CC, (int)CustomRegisterPresence.EcsAgnus, 0x07FF },
        { 0x1CE, (int)CustomRegisterPresence.EcsAgnus, 0x07FF },
        { 0x1D0, (int)CustomRegisterPresence.EcsAgnus, 0x01FF },
        { 0x1D2, (int)CustomRegisterPresence.EcsAgnus, 0x01FF },
        { 0x1D4, (int)CustomRegisterPresence.EcsAgnus, 0x01FF },
        { 0x1D6, (int)CustomRegisterPresence.EcsAgnus, 0x01FF },
        { 0x1D8, (int)CustomRegisterPresence.EcsAgnus, 0x01FF },
        { 0x1DA, (int)CustomRegisterPresence.EcsAgnus, 0x0000 },
        { 0x1DC, (int)CustomRegisterPresence.EcsAgnus, 0x7FFF },
        { 0x1DE, (int)CustomRegisterPresence.EcsAgnus, 0x01FF },
        { 0x1E0, (int)CustomRegisterPresence.EcsAgnus, 0x07FF },
        { 0x1E2, (int)CustomRegisterPresence.EcsAgnus, 0x01FF },
        { 0x1E4, (int)CustomRegisterPresence.EcsAgnusOrDenise, 0x2F2F }
    };

    [Theory]
    [MemberData(nameof(EcsOnlyRegisterMatrix))]
    public void MetadataDefinesEcsPresenceAndWritableMask(
        ushort offset,
        int presence,
        ushort expectedMask)
    {
        var descriptor = CustomRegisterMetadata.Get(offset);

        Assert.Equal((CustomRegisterPresence)presence, descriptor.Presence);
        Assert.False(descriptor.IsPresent(AmigaChipset.OcsPal));
        Assert.Equal(0, descriptor.GetWritableMask(AmigaChipset.OcsPal));
        Assert.True(descriptor.IsPresent(AmigaChipset.EcsPal));
        Assert.Equal(expectedMask, descriptor.GetWritableMask(AmigaChipset.EcsPal));
    }

    [Fact]
    public void MetadataSeparatesMixedChipOwnership()
    {
        var ecsAgnus = new AmigaChipset(AgnusModel.Ecs, DeniseModel.Ocs, VideoStandard.Pal);
        var ecsDenise = new AmigaChipset(AgnusModel.Ocs, DeniseModel.Ecs, VideoStandard.Pal);

        Assert.True(CustomRegisterMetadata.Get(0x05C).IsPresent(ecsAgnus));
        Assert.False(CustomRegisterMetadata.Get(0x05C).IsPresent(ecsDenise));
        Assert.False(CustomRegisterMetadata.Get(0x106).IsPresent(ecsAgnus));
        Assert.True(CustomRegisterMetadata.Get(0x106).IsPresent(ecsDenise));
        Assert.True(CustomRegisterMetadata.Get(0x1E4).IsPresent(ecsAgnus));
        Assert.True(CustomRegisterMetadata.Get(0x1E4).IsPresent(ecsDenise));
    }

    [Fact]
    public void FmodeRemainsAbsentOnEverySupportedChipset()
    {
        var fmode = CustomRegisterMetadata.Get(0x1FC);

        Assert.False(fmode.IsPresent(AmigaChipset.OcsPal));
        Assert.False(fmode.IsPresent(AmigaChipset.EcsPal));
        Assert.False(fmode.IsPresent(AmigaChipset.EcsNtsc));
        Assert.Equal(CustomRegisterReadback.OpenBus, fmode.Readback);
    }

    [Fact]
    public void Bltcon0lIsAbsentOnOcsAndDoesNotChangeBlitterControl()
    {
        var bus = new AmigaBus(chipset: AmigaChipset.OcsPal);
        bus.WriteWord(CustomBase + 0x040, 0xABCD);

        bus.WriteWord(CustomBase + 0x05A, 0x1256);

        Assert.Equal(0xABCD, bus.Blitter.CaptureSnapshot().Bltcon0);
        Assert.Equal(0x1256, bus.ReadWord(CustomBase + 0x05A));
        Assert.Equal(
            HardwareScheduleImpact.None,
            CustomRegisterScheduleClassifier.GetPotentialImpact(AmigaChipset.OcsPal, 0x05A));
    }

    [Fact]
    public void Bltcon0lCpuWordWriteReplacesOnlyLowControlByteOnEcs()
    {
        var bus = new AmigaBus(chipset: AmigaChipset.EcsPal);
        bus.WriteWord(CustomBase + 0x040, 0xABCD);

        bus.WriteWord(CustomBase + 0x05A, 0x1256);

        Assert.Equal(0xAB56, bus.Blitter.CaptureSnapshot().Bltcon0);
    }

    [Theory]
    [InlineData(0x05A)]
    [InlineData(0x05B)]
    public void Bltcon0lCpuByteWritesMirrorAcrossTheCustomWordBus(uint offset)
    {
        var bus = new AmigaBus(chipset: AmigaChipset.EcsPal);
        bus.WriteWord(CustomBase + 0x040, 0xABCD);

        bus.WriteByte(CustomBase + offset, 0x67, 0);

        Assert.Equal(0xAB67, bus.Blitter.CaptureSnapshot().Bltcon0);
    }

    [Fact]
    public void Bltcon0lCopperWriteUsesTheSharedMaskedHandler()
    {
        var bus = new AmigaBus(chipset: AmigaChipset.EcsPal);
        bus.WriteWord(CustomBase + 0x040, 0xABCD);

        bus.WriteDeviceWord(
            AmigaBusRequester.Copper,
            AmigaBusAccessKind.Copper,
            CustomBase + 0x05A,
            0x9867,
            requestedCycle: 0);

        Assert.Equal(0xAB67, bus.Blitter.CaptureSnapshot().Bltcon0);
    }
}
