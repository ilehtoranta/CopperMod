namespace CopperMod.Amiga.Tests;

public sealed class A1200AgaMilestoneTests
{
    private const uint CustomBase = 0x00DFF000;

    [Theory]
    [InlineData((int)MachineProfile.A1200AgaPal, (int)VideoStandard.Pal)]
    [InlineData((int)MachineProfile.A1200AgaNtsc, (int)VideoStandard.Ntsc)]
    public void ProfilesUseA1200MilestoneDefaults(int profileValue, int standardValue)
    {
        var options = MachineOptions.ForProfile((MachineProfile)profileValue);

        Assert.Equal((VideoStandard)standardValue, options.Chipset.VideoStandard);
        Assert.Equal(DmaChipModel.AgaAlice, options.Chipset.DmaChip);
        Assert.Equal(DisplayChipModel.AgaLisa, options.Chipset.DisplayChip);
        Assert.Equal(2 * 1024 * 1024, options.ChipRamSize);
        Assert.Equal(0, options.ExpansionRamSize);
        Assert.Equal(0, options.RealFastRamSize);
        Assert.Equal(1, options.FloppyDriveCount);
        Assert.False(options.RealTimeClockEnabled);
        Assert.Equal(M68kBackendKind.AccurateM68EC020, options.CpuBackend);
        Assert.Equal(AgnusBusArbitrationMode.Legacy, options.AgnusBusArbitration);
    }

    [Fact]
    public void A1200RequiresRomBackedKickstart30()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new Machine(MachineOptions.ForProfile(MachineProfile.A1200AgaPal)));
        Assert.Throws<InvalidOperationException>(() =>
            new Machine(MachineOptions.ForProfile(MachineProfile.A1200AgaPal)
                .WithKickstart(KickstartConfiguration.FromRomImage(KickstartVersion.Kickstart31, new byte[8]))));

        using var machine = CreateMachine();
        Assert.IsType<M68EC020Interpreter>(machine.Cpu);
        Assert.Equal(AmigaConstants.EcsChipDmaAddressMask, machine.Bus.ChipDmaAddressMask);
    }

    [Fact]
    public void CompleteAliceLisaRunsButMixedAgaProfilesRemainRejected()
    {
        _ = new AmigaBus(chipRamSize: 2 * 1024 * 1024, chipset: AmigaChipset.AgaPal);
        Assert.Throws<NotSupportedException>(() => new AmigaBus(
            chipset: new AmigaChipset(DmaChipModel.AgaAlice, DisplayChipModel.EcsDenise, VideoStandard.Pal)));
        Assert.Throws<NotSupportedException>(() => new AmigaBus(
            chipset: new AmigaChipset(DmaChipModel.EcsAgnus, DisplayChipModel.AgaLisa, VideoStandard.Pal)));
    }

    [Fact]
    public void AgaRegistersExposePresenceMasksAndEightBitplanes()
    {
        var file = new CustomRegisterFile(AmigaChipset.AgaPal);

        Assert.Equal(0x00F8, file.GetStoredValue(0x07C));
        Assert.Equal(0xFF5F, file.Get(0x100).WritableMask);
        Assert.Equal(0xFEF7, file.Get(0x106).WritableMask);
        Assert.Equal(0xFFFF, file.Get(0x10C).WritableMask);
        Assert.Equal(0x0011, file.Get(0x10C).ResetValue);
        Assert.Equal(0x00FF, file.Get(0x10E).WritableMask);
        Assert.True(file.Get(0x11C).IsPresent);
        Assert.True(file.Get(0x11E).IsPresent);
        Assert.Equal(0xC00F, file.Get(0x1FC).WritableMask);
        Assert.Equal(8, Display.GetAgnusBitplaneFetchPlaneCount(
            DmaChipModel.AgaAlice, DisplayChipModel.AgaLisa, DeniseResolution.LowRes, 0x7010));
        Assert.Equal(8, Display.GetDeniseBitplaneDecodePlaneCount(
            DisplayChipModel.AgaLisa, DeniseResolution.LowRes, 0x7010));
    }

    [Fact]
    public void UnsupportedAgaModesAreTracedWithHardwareContext()
    {
        using var machine = CreateMachine();
        machine.Cpu.State.ProgramCounter = 0x00F8_1234;

        machine.Bus.WriteWord(CustomBase + 0x1FC, 0x0001);

        var trace = Assert.Single(machine.Bus.AgaUnsupportedFeatureTrace);
        Assert.Equal(0x1FC, trace.Register);
        Assert.Equal(0x0001, trace.Value);
        Assert.Equal(0x00F8_1234u, trace.ProgramCounter);
        Assert.True(trace.BeamLine >= 0);
        Assert.True(trace.BeamHorizontal >= 0);
    }

    [Fact]
    public void EmptyA1200PlatformIoHasPhysicalNoDeviceResponsesAndNoInterrupts()
    {
        var io = new A1200PlatformIo();

        Assert.Equal(0xFF, io.ReadByte(A1200PlatformIo.IdeStart + 0x201C));
        Assert.Equal(0xFF, io.ReadByte(A1200PlatformIo.PcmciaStart));
        Assert.False(io.InterruptAsserted);

        io.WriteByte(A1200PlatformIo.GayleInterruptEnable, 0xA5);
        io.WriteByte(A1200PlatformIo.GayleConfiguration, 0xFF);
        Assert.Equal(0xA5, io.ReadByte(A1200PlatformIo.GayleInterruptEnable));
        Assert.Equal(0x0F, io.ReadByte(A1200PlatformIo.GayleConfiguration));
        Assert.False(io.InterruptAsserted);

        var identification = 0;
        for (var bit = 0; bit < 8; bit++)
        {
            identification = (identification << 1) |
                (io.ReadByte(A1200PlatformIo.GayleIdentification) == 0 ? 0 : 1);
        }
        Assert.Equal(0xD1, identification);
    }

    [Fact]
    public void A1200PlatformIoHonorsByteLanesAndSplitsMisalignedReads()
    {
        using var machine = CreateMachine();
        var cycle = 0L;

        machine.Bus.WriteByte(A1200PlatformIo.GayleInterruptEnable, 0x5A, ref cycle, AmigaBusAccessKind.CpuDataWrite);
        Assert.Equal(0x5A, machine.Bus.ReadByte(
            A1200PlatformIo.GayleInterruptEnable, ref cycle, AmigaBusAccessKind.CpuDataRead));
        Assert.Equal(0x5AFF, machine.Bus.ReadWord(
            A1200PlatformIo.GayleInterruptEnable, ref cycle, AmigaBusAccessKind.CpuDataRead));
        Assert.Equal(0xFFFF, machine.Bus.ReadWord(
            A1200PlatformIo.IdeStart + 1, ref cycle, AmigaBusAccessKind.CpuDataRead));
    }

    [Fact]
    public void A1200CpuProfileUsesNativeLongChipAndRomBusesAndBytePlatformIo()
    {
        var profile = M68020CpuProfile.A1200Ec02014Mhz;

        Assert.Equal(2, profile.NativeCyclesPerMachineCycle);
        Assert.Equal(M68020BusWidth.Long, profile.GetBusTimingRule(0x0010_0000).Width);
        Assert.Equal(M68020BusWidth.Long, profile.GetBusTimingRule(0x00F8_0000).Width);
        Assert.Equal(M68020BusWidth.Word, profile.GetBusTimingRule(0x00DF_F000).Width);
        Assert.Equal(M68020BusWidth.Byte, profile.GetBusTimingRule(A1200PlatformIo.IdeStart).Width);
        Assert.Equal(M68020BusWidth.Byte, profile.GetBusTimingRule(A1200PlatformIo.PcmciaStart).Width);
    }

    [Fact]
    public void LisaPaletteBankAndLoctBuildA24BitEntry()
    {
        using var machine = CreateMachine();

        machine.Bus.WriteWord(CustomBase + 0x106, 0x4000); // BANK=2, high nibbles
        machine.Bus.WriteWord(CustomBase + 0x182, 0x0A5C); // palette entry 65
        machine.Bus.WriteWord(CustomBase + 0x106, 0x4200); // BANK=2, LOCT=1
        machine.Bus.WriteWord(CustomBase + 0x182, 0x0317);
        machine.Bus.SynchronizeLiveDisplayThrough(128);

        Assert.Equal(0xFFA3_51C7u, machine.Bus.Display.GetCurrentConvertedColor(65));
    }

    [Fact]
    public void GayleStateReturnsToHardwareResetValues()
    {
        var io = new A1200PlatformIo();
        io.WriteByte(A1200PlatformIo.GayleInterruptEnable, 0xA5);
        io.WriteByte(A1200PlatformIo.GayleConfiguration, 0x0F);

        io.Reset();

        Assert.Equal(0, io.InterruptEnable);
        Assert.Equal(0, io.Configuration);
        Assert.False(io.InterruptAsserted);
    }

    private static Machine CreateMachine()
        => new(MachineOptions.ForProfile(MachineProfile.A1200AgaPal)
            .WithKickstart(KickstartConfiguration.FromRomImage(
                KickstartVersion.Kickstart30,
                new byte[512 * 1024])));
}
