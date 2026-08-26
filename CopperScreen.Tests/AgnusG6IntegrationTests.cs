using CopperMod.Amiga;
using CopperMod.Amiga.CustomChips.Agnus;

namespace CopperScreen.Tests;

public sealed class AgnusG6IntegrationTests
{
    [Fact]
    public void ExplicitSupportedOptInRoutesCpuChipWordThroughLiveKernel()
    {
        using var machine = new Machine(
            MachineOptions.ForProfile(MachineProfile.A500PalFullEmulationSkeleton)
                .WithBusAccessLogging(true)
                .WithAgnusBusArbitration(AgnusBusArbitrationMode.SlotKernel));
        machine.Bus.ChipRam[0x100] = 0x12;
        machine.Bus.ChipRam[0x101] = 0x34;
        long cycle = 32;

        var value = machine.Bus.ReadWord(
            0x100,
            ref cycle,
            AmigaBusAccessKind.CpuDataRead);
        var diagnostics = machine.Bus.AgnusLiveSlotKernelDiagnostics;

        Assert.Equal(0x1234, value);
        Assert.Equal(AgnusBusArbitrationMode.SlotKernel, machine.Bus.AgnusBusArbitration);
        Assert.True(machine.Bus.AgnusSlotKernelSelected);
        Assert.Equal(1, diagnostics.CpuGrantCalls);
        Assert.Equal(1, diagnostics.CpuWordPhases);
        Assert.True(diagnostics.SlotIterations >= 1);
        Assert.Equal(
            diagnostics.RangeAdvanceCalls,
            diagnostics.ForbiddenCalls);
    }

    [Fact]
    public void DefaultAndForcedRollbackDoNotEnterLiveKernel()
    {
        using var defaults = new Machine(
            MachineOptions.ForProfile(MachineProfile.A500PalFullEmulationSkeleton));
        using var rollback = new Machine(
            MachineOptions.ForProfile(MachineProfile.A500PalFullEmulationSkeleton)
                .WithAgnusBusArbitration(AgnusBusArbitrationMode.ForcedLegacy));
        long defaultCycle = 32;
        long rollbackCycle = 32;

        _ = defaults.Bus.ReadWord(
            0x100,
            ref defaultCycle,
            AmigaBusAccessKind.CpuDataRead);
        _ = rollback.Bus.ReadWord(
            0x100,
            ref rollbackCycle,
            AmigaBusAccessKind.CpuDataRead);

        Assert.False(defaults.Bus.AgnusSlotKernelSelected);
        Assert.False(rollback.Bus.AgnusSlotKernelSelected);
        Assert.Equal(0, defaults.Bus.AgnusLiveSlotKernelDiagnostics.CpuGrantCalls);
        Assert.Equal(0, rollback.Bus.AgnusLiveSlotKernelDiagnostics.CpuGrantCalls);
    }

    [Theory]
    [InlineData((int)MachineProfile.A500PlusEcsPal)]
    [InlineData((int)MachineProfile.A500PlusEcsNtsc)]
    public void UnsupportedEcsProfilesRemainLegacyWithDefaultOrExplicitRequest(
        int profileValue)
    {
        var profile = (MachineProfile)profileValue;
        using var defaults = new Machine(MachineOptions.ForProfile(profile));
        using var requested = new Machine(
            MachineOptions.ForProfile(profile)
                .WithAgnusBusArbitration(AgnusBusArbitrationMode.SlotKernel));

        Assert.False(defaults.Bus.AgnusSlotKernelSelected);
        Assert.False(requested.Bus.AgnusSlotKernelSelected);
        Assert.Equal(AgnusBusArbitrationMode.Legacy, defaults.Bus.AgnusBusArbitration);
        Assert.Equal(AgnusBusArbitrationMode.Legacy, requested.Bus.AgnusBusArbitration);
    }
}
