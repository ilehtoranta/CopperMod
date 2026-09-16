using Copper68k;
using Xunit;

namespace CopperMod.Amiga.Lightweight.Tests;

public sealed class LightweightFloppyDriveTests
{
    [Fact]
    public void MountOwnsStandardAdfAndRejectsOtherGeometryWithoutLosingMedia()
    {
        var drive = new LightweightFloppyDrive();
        var image = new byte[LightweightFloppyDrive.StandardAdfBytes];
        image[0] = 0x42;
        drive.Mount(image);
        image[0] = 0;
        Assert.Equal(0x42, drive.Image[0]);
        Assert.Throws<ArgumentException>(() => drive.Mount(new byte[512]));
        Assert.Equal(0x42, drive.Image[0]);
    }

    [Fact]
    public void MotorLatchesOnlyAtSelectEdgeAndKeepsRunningWhenDeselected()
    {
        var drive = new LightweightFloppyDrive();
        drive.WriteControlPins(0x7F, 10); // Motor low, not selected.
        Assert.False(drive.MotorOn);
        drive.WriteControlPins(0x77, 20);
        Assert.True(drive.MotorOn);
        var ready = drive.ReadyCycle;
        drive.WriteControlPins(0xF7, 30); // Motor high while still selected.
        Assert.True(drive.MotorOn);
        drive.WriteControlPins(0xFF, 40);
        Assert.True(drive.MotorOn);
        Assert.Equal(ready, drive.ReadyCycle);
        drive.WriteControlPins(0xF7, 50);
        Assert.False(drive.MotorOn);
        Assert.Equal(long.MaxValue, drive.ReadyCycle);
    }

    [Fact]
    public void ReadyIsObservedAtExactModelDeadlineWithoutAdvancingAnotherClock()
    {
        var drive = MountedDrive();
        drive.WriteControlPins(0x77, 100);
        var ready = 100 + LightweightFloppyDrive.MotorSpinUpCycles;
        Assert.NotEqual(0, drive.ReadInputPins(ready - 1) & 0x20);
        Assert.Equal(0, drive.ReadInputPins(ready) & 0x20);
        drive.WriteControlPins(0x7F, ready + 1);
        Assert.Equal(0xFF, drive.ReadInputPins(ready + 1));
        drive.WriteControlPins(0x77, ready + 2);
        Assert.Equal(ready, drive.ReadyCycle);
        Assert.Equal(0, drive.ReadInputPins(ready + 2) & 0x20);
    }

    [Fact]
    public void StepIsSelectedFallingEdgeAndDoesNotRepeatWhileHeldLow()
    {
        var drive = MountedDrive();
        drive.WriteControlPins(0x75, 0); // Select, direction in, step high.
        drive.WriteControlPins(0x74, 10);
        Assert.Equal(1, drive.Cylinder);
        drive.WriteControlPins(0x74, 20);
        drive.WriteControlPins(0x75, 30);
        Assert.Equal(1, drive.Cylinder);
        drive.WriteControlPins(0x77, 40); // Direction out before next pulse.
        drive.WriteControlPins(0x76, 50);
        Assert.Equal(0, drive.Cylinder);
        drive.WriteControlPins(0x77, 60);
        drive.WriteControlPins(0x76, 70);
        Assert.Equal(0, drive.Cylinder);
        drive.WriteControlPins(0x7D, 80); // Deselected inward pulse.
        drive.WriteControlPins(0x7C, 90);
        Assert.Equal(0, drive.Cylinder);
    }

    [Fact]
    public void ChangeRequiresInsertedMediaAndStepWhileTrackZeroIsPhysical()
    {
        var drive = MountedDrive();
        drive.WriteControlPins(0xF7, 0);
        Assert.Equal(0, drive.ReadInputPins(0) & 0x1C);
        drive.WriteControlPins(0xF6, 10); // Step outward at track zero clears change.
        Assert.NotEqual(0, drive.ReadInputPins(10) & 4);
        drive.Eject();
        drive.WriteControlPins(0xF7, 20);
        drive.WriteControlPins(0xF6, 30);
        Assert.True(drive.DiskChanged);
        Assert.Equal(0, drive.ReadInputPins(30) & 0x10);
        Assert.NotEqual(0, drive.ReadInputPins(30) & 0x28);
        drive.Mount(new byte[LightweightFloppyDrive.StandardAdfBytes]);
        Assert.True(drive.DiskChanged);
    }

    [Fact]
    public void ResetStopsMotorButRetainsMediaAndHeadPosition()
    {
        var drive = MountedDrive();
        drive.WriteControlPins(0x71, 0); // Side bit low selects head 1.
        drive.WriteControlPins(0x70, 10);
        Assert.Equal(1, drive.Head);
        Assert.Equal(1, drive.Cylinder);
        drive.ResetControl();
        Assert.True(drive.Mounted);
        Assert.Equal(1, drive.Cylinder);
        Assert.False(drive.DiskChanged);
        Assert.False(drive.MotorOn);
        Assert.False(drive.Selected);
    }

    [Fact]
    public void CiaDataDirectionControlsRealPinsAndStatusUsesInputBitsOnly()
    {
        using var machine = new LightweightA500Machine();
        machine.MountAdf(new byte[LightweightFloppyDrive.StandardAdfBytes]);
        long cycle = 0;
        Write(machine, 0xBFD100, 0x75, ref cycle);
        Assert.Equal(0xFC, Read(machine, 0xBFE001, ref cycle) & 0xFC);
        Write(machine, 0xBFD300, 0xFF, ref cycle); // DDR now drives the saved latch.
        Assert.Equal(0, Read(machine, 0xBFE001, ref cycle) & 0x1C);
        Write(machine, 0xBFD100, 0x74, ref cycle);
        Assert.Equal(0x14, Read(machine, 0xBFE001, ref cycle) & 0x14);
        Write(machine, 0xBFD300, 0, ref cycle); // Select released by DDR, not PRB.
        Assert.Equal(0xFC, Read(machine, 0xBFE001, ref cycle) & 0xFC);
        Write(machine, 0xBFD300, 0xFF, ref cycle);
        Write(machine, 0xBFE001, 0xFF, ref cycle);
        Write(machine, 0xBFE201, 0xFF, ref cycle); // Outputs read their own latch.
        Assert.Equal(0xFF, Read(machine, 0xBFE001, ref cycle));
    }

    [Fact]
    public void MountEjectAndResetStayAtHostBoundaryAndWriteDmaIsUnsupported()
    {
        using var machine = new LightweightA500Machine();
        var cycle = machine.Cycle;
        machine.MountAdf(new byte[LightweightFloppyDrive.StandardAdfBytes]);
        Assert.Equal(cycle, machine.Cycle);
        machine.Reset();
        Assert.True(machine.IsAdfMounted);
        machine.ResetExternalDevices(100);
        Assert.True(machine.IsAdfMounted);
        machine.EjectAdf();
        Assert.False(machine.IsAdfMounted);
        machine.WriteCustomRegisterFromCopper(0x024, 0x8001, machine.Cycle);
        Assert.Null(machine.UnsupportedActiveFeature);
        machine.WriteCustomRegisterFromCopper(0x024, 0x4000, machine.Cycle);
        machine.WriteCustomRegisterFromCopper(0x024, 0xC001, machine.Cycle);
        machine.WriteCustomRegisterFromCopper(0x024, 0xC001, machine.Cycle);
        Assert.Contains("disk", machine.UnsupportedActiveFeature!);
    }

    private static LightweightFloppyDrive MountedDrive()
    {
        var drive = new LightweightFloppyDrive();
        drive.Mount(new byte[LightweightFloppyDrive.StandardAdfBytes]);
        return drive;
    }

    [Fact]
    public void SeekBeyondStandardGeometryIsReportedInsteadOfWrapping()
    {
        var drive = MountedDrive();
        for (var cylinder = 1; cylinder <= 79; cylinder++)
        {
            drive.WriteControlPins(0x75, cylinder * 20);
            Assert.True(drive.WriteControlPins(0x74, cylinder * 20 + 10));
        }
        drive.WriteControlPins(0x75, 1600);
        Assert.False(drive.WriteControlPins(0x74, 1610));
        Assert.Equal(79, drive.Cylinder);
    }

    [Fact]
    public void SteadyDriveControlAndObservationDoNotAllocate()
    {
        var drive = MountedDrive();
        drive.WriteControlPins(0xF7, 0);
        _ = drive.ReadInputPins(0);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var checksum = 0;
        for (var i = 1; i <= 10000; i++)
        {
            drive.WriteControlPins((byte)((i & 1) == 0 ? 0xF7 : 0xFF), i);
            checksum += drive.ReadInputPins(i);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.NotEqual(0, checksum);
    }

    private static void Write(LightweightA500Machine machine, uint address, byte value, ref long cycle)
        => machine.WriteByte(address, value, ref cycle, M68kBusAccessKind.CpuDataWrite);
    private static byte Read(LightweightA500Machine machine, uint address, ref long cycle)
        => machine.ReadByte(address, ref cycle, M68kBusAccessKind.CpuDataRead);
}
