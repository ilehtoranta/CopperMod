using System.Buffers.Binary;
using Copper68k;
using Xunit;

namespace CopperMod.Amiga.Lightweight.Tests;

public sealed class LightweightDiskSerialTests
{
    [Fact]
    public void SyncRegisterOwnershipSurvivesCpuCopperWritesAndReset()
    {
        using var machine = new LightweightA500Machine();
        Assert.Equal(0x4489, machine.GetCustomRegister(0x07E));
        var cycle = machine.Cycle;
        machine.WriteWord(0xDFF07E, 0xA55A, ref cycle, M68kBusAccessKind.CpuDataWrite);
        Assert.Equal(0xA55A, machine.GetCustomRegister(0x07E));
        machine.WriteCustomRegisterFromCopper(0x07E, 0xFFFF, machine.Cycle);
        Assert.Equal(0xFFFF, machine.GetCustomRegister(0x07E));
        machine.ResetExternalDevices(machine.Cycle);
        Assert.Equal(0x4489, machine.GetCustomRegister(0x07E));
    }

    [Fact]
    public void MountEncodesEveryCylinderAndSideWithIndependentOddEvenDecode()
    {
        var image = new byte[LightweightFloppyDrive.StandardAdfBytes];
        for (var track = 0; track < 160; track++)
            for (var sector = 0; sector < 11; sector++)
                for (var i = 0; i < 512; i++)
                    image[(track * 11 + sector) * 512 + i] = (byte)(track ^ sector ^ i);
        var drive = new LightweightFloppyDrive();
        drive.Mount(image);
        int[] order = [9, 10, 0, 1, 2, 3, 4, 5, 6, 7, 8];
        for (var cylinder = 0; cylinder < 80; cylinder++)
        {
            foreach (var side in new[] { 0, 1 })
            {
                drive.WriteControlPins((byte)(side == 0 ? 0xF5 : 0xF1), 0);
                var track = drive.Track;
                Assert.Equal(12668, track.Length);
                for (var slot = 0; slot < 11; slot++)
                {
                    var encoded = track.Slice(slot * 0x440, 0x440);
                    Assert.Equal(0x44894489u, U32(encoded.Slice(4)));
                    var logicalTrack = cylinder * 2 + side;
                    var sector = order[slot];
                    Assert.Equal(0xFF000000u | (uint)(logicalTrack << 16) |
                        (uint)(sector << 8) | (uint)(11 - slot), Decode(encoded.Slice(8), encoded.Slice(12)));
                    for (var i = 0; i < 512; i += 4)
                        Assert.Equal(U32(image.AsSpan((logicalTrack * 11 + sector) * 512 + i)),
                            Decode(encoded.Slice(0x40 + i), encoded.Slice(0x240 + i)));
                }
            }
            if (cylinder < 79)
            {
                drive.WriteControlPins(0xF5, 0);
                drive.WriteControlPins(0xF4, 0);
            }
        }
    }

    [Fact]
    public void IdealRotationUsesExactRationalRateAndRetainsPhaseAcrossRevolutions()
    {
        using var machine = new LightweightA500Machine();
        var drive = new LightweightFloppyDrive();
        drive.Mount(new byte[LightweightFloppyDrive.StandardAdfBytes]);
        drive.WriteControlPins(0x77, 0);
        var serial = new LightweightDiskSerial();
        serial.OnDriveChanged(drive, 0);
        machine.WriteCustomRegisterFromCopper(0x09E, 0x8100, 0);
        var origin = (drive.ReadyCycle + 1) & ~1L;
        for (long bit = 1; bit <= 5L * LightweightDiskSerial.TrackBits; bit++)
        {
            var denominator = 10L * LightweightDiskSerial.TrackBits;
            var expected = origin + 2 * ((bit * LightweightPaulaAudio.PalCpuFrequency + denominator - 1) / denominator);
            Assert.Equal(expected, serial.NextCycle);
            serial.Step(expected, drive, machine);
        }
        Assert.Equal(0, serial.BitPosition);
    }

    [Fact]
    public void ByteAppearsOnlyAfterEightBitsAndReadClearsReadyNotData()
    {
        using var machine = RunningMachine();
        var first = machine.DiskSerialNextCycle;
        machine.AdvanceHardwareTo(first + 6 * 14);
        Assert.Equal(0, ReadStatus(machine) & 0x8000);
        machine.AdvanceHardwareTo(first + 7 * 14);
        Assert.Equal(0x80AA, ReadStatus(machine));
        Assert.Equal(0x00AA, ReadStatus(machine));
    }

    [Fact]
    public void SyncIsLiveAndInterruptDoesNotDependOnDmaOrWordsync()
    {
        using var machine = RunningMachine();
        var first = machine.DiskSerialNextCycle;
        machine.AdvanceHardwareTo(first + 47 * 14); // 4 gap bytes, first 4489.
        Assert.Equal(0x4489, machine.DiskInputShift);
        Assert.NotEqual(0, machine.Intreq & 0x1000);
        Assert.Equal(0, machine.Dmacon);
        Assert.Equal(0, machine.Adkcon & 0x0400);
        Assert.NotEqual(0, ReadStatus(machine) & 0x1000);
        Assert.NotEqual(0, ReadStatus(machine) & 0x1000);
        machine.AdvanceHardwareTo(first + 48 * 14);
        Assert.Equal(0, ReadStatus(machine) & 0x1000);
    }

    [Theory]
    [InlineData(0x8000, 0x0210, 0x4000)]
    [InlineData(0x8000, 0x0200, 0)]
    [InlineData(0x8000, 0x0010, 0)]
    [InlineData(0, 0x0210, 0)]
    [InlineData(0x4000, 0x0210, 0x2000)]
    [InlineData(0xC000, 0x0210, 0x6000)]
    public void StatusReflectsAllThreeDmaBitsAndWriteDirection(int length, int dma, int expected)
    {
        var registers = new LightweightRegisters();
        registers.Reset();
        registers.Write(0x024, (ushort)length);
        registers.Write(0x096, (ushort)(0x8000 | dma));
        var serial = new LightweightDiskSerial();
        Assert.Equal(expected, serial.ReadByteStatus(registers) & 0x6000);
    }

    [Fact]
    public void CpuHighByteReadAlsoClearsByteReady()
    {
        using var machine = RunningMachine();
        machine.AdvanceHardwareTo(machine.DiskSerialNextCycle + 7 * 14);
        var cycle = machine.Cycle;
        Assert.Equal(0x80, machine.ReadByte(0xDFF01A, ref cycle, M68kBusAccessKind.CpuDataRead));
        Assert.Equal(0, ReadStatus(machine) & 0x8000);
    }

    [Fact]
    public void SyncRegisterWritesUseSameComparatorWithoutRepeatedLevelInterrupts()
    {
        using var machine = new LightweightA500Machine();
        machine.WriteCustomRegisterFromCopper(0x07E, 0, 0);
        Assert.NotEqual(0, machine.Intreq & 0x1000);
        machine.WriteCustomRegisterFromCopper(0x09C, 0x1000, 0);
        machine.WriteCustomRegisterFromCopper(0x07E, 0, 0);
        Assert.Equal(0, machine.Intreq & 0x1000);
        machine.WriteCustomRegisterFromCopper(0x07E, 1, 0);
        machine.WriteCustomRegisterFromCopper(0x07E, 0, 0);
        Assert.NotEqual(0, machine.Intreq & 0x1000);
    }

    [Fact]
    public void DeselectAndSideChangeRetainRotationButDeselectStopsReceiver()
    {
        using var machine = RunningMachine();
        machine.AdvanceHardwareTo(machine.DiskSerialNextCycle + 20 * 14);
        WriteCia(machine, 0xBFD100, 0x7F);
        var shift = machine.DiskInputShift;
        var next = machine.DiskSerialNextCycle;
        var bit = machine.DiskBitPosition;
        machine.AdvanceHardwareTo(next + 10 * 14);
        Assert.Equal(shift, machine.DiskInputShift);
        Assert.Equal(bit + 11, machine.DiskBitPosition);
        next = machine.DiskSerialNextCycle;
        WriteCia(machine, 0xBFD100, 0x73); // Reselect head 1, motor stays on.
        Assert.Equal(next, machine.DiskSerialNextCycle);
        machine.AdvanceHardwareTo(next);
        Assert.Equal(bit + 12, machine.DiskBitPosition);
    }

    [Fact]
    public void IndexLatchesCiaBFlagWithMaskOffAndBecomesVisibleWhenEnabled()
    {
        using var machine = RunningMachine();
        var origin = machine.DiskSerialNextCycle - 14;
        var index = origin + LightweightPaulaAudio.PalCpuFrequency / 5;
        machine.AdvanceHardwareTo(index - 2);
        Assert.Equal(0, machine.CiaBPendingInterrupts & 0x10);
        machine.AdvanceHardwareTo(index);
        Assert.NotEqual(0, machine.CiaBPendingInterrupts & 0x10);
        Assert.Equal(0, machine.Intreq & 0x2000);
        WriteCia(machine, 0xBFDD00, 0x90);
        Assert.NotEqual(0, machine.Intreq & 0x2000);
        var cycle = machine.Cycle;
        Assert.Equal(0x90, machine.ReadByte(0xBFDD00, ref cycle, M68kBusAccessKind.CpuDataRead));
        Assert.Equal(0, machine.CiaBPendingInterrupts);
    }

    [Fact]
    public void ReceiverCrossesFieldBoundaryWithoutDiscardingPartialByte()
    {
        using var machine = RunningMachine();
        var first = machine.DiskSerialNextCycle;
        // First arrival is in field 24; extend past field 25 without reset.
        var target = ((first / LightweightClock.CpuCyclesPerFrame) + 1) * LightweightClock.CpuCyclesPerFrame;
        machine.AdvanceHardwareTo(target - 2);
        var bit = machine.DiskBitPosition;
        var next = machine.DiskSerialNextCycle;
        machine.AdvanceHardwareTo(next);
        Assert.Equal(bit + 1, machine.DiskBitPosition);
        Assert.True(machine.CompletedFrames >= 25);
    }

    [Fact]
    public void EjectAndResetRemovePendingInputWithoutRemovingMountedImageOnReset()
    {
        using var machine = RunningMachine();
        machine.AdvanceHardwareTo(machine.DiskSerialNextCycle + 7 * 14);
        var shift = machine.DiskInputShift;
        machine.EjectAdf();
        Assert.Equal(long.MaxValue, machine.DiskSerialNextCycle);
        machine.AdvanceHardwareTo(machine.Cycle + 1000);
        Assert.Equal(shift, machine.DiskInputShift);
        machine.MountAdf(new byte[LightweightFloppyDrive.StandardAdfBytes]);
        Assert.NotEqual(long.MaxValue, machine.DiskSerialNextCycle);
        machine.ResetExternalDevices(machine.Cycle);
        Assert.True(machine.IsAdfMounted);
        Assert.Equal(long.MaxValue, machine.DiskSerialNextCycle);
        Assert.Equal(0, machine.DiskInputShift);
    }

    [Theory]
    [InlineData(0x0200)]
    [InlineData(0x0300)]
    public void UnsupportedRecoveryCannotProduceASuccessfulWorkload(int adkcon)
    {
        using var machine = RunningMachine();
        machine.WriteCustomRegisterFromCopper(0x09E, 0x0300, machine.Cycle);
        machine.WriteCustomRegisterFromCopper(0x09E, (ushort)(0x8000 | adkcon), machine.Cycle);
        machine.AdvanceHardwareTo(machine.DiskSerialNextCycle);
        Assert.Contains("disk serial recovery", machine.UnsupportedActiveFeature!);
    }

    // These are declared ideal-ADF receiver-model checks, not physical Paula
    // rate-switch conformance. See LWA-DISK-009/010 in the issue register.
    [Theory]
    [InlineData(32, 1, 3)] // Sync prefix 01: late pulse adds a settling cell.
    [InlineData(33, 1, 2)] // 10: early pulse.
    [InlineData(34, 0, 2)] // 00: no pulse.
    public void SlowWindowConsumesOnlyArrivedCellsBeforePublishing(int prefix, int bit, int cells)
    {
        using var machine = new LightweightA500Machine();
        var drive = SelectedDrive();
        var serial = new LightweightDiskSerial();
        serial.OnDriveChanged(drive, 0);
        machine.WriteCustomRegisterFromCopper(0x09E, 0x8100, machine.Cycle);
        for (var i = 0; i < prefix; i++) SampleCell(ref serial, drive, machine);
        machine.WriteCustomRegisterFromCopper(0x09E, 0x0100, machine.Cycle);
        var shift = serial.Shift;

        for (var i = 1; i < cells; i++)
        {
            SampleCell(ref serial, drive, machine);
            Assert.Equal(shift, serial.Shift);
        }
        SampleCell(ref serial, drive, machine);

        Assert.Equal((ushort)((shift << 1) | bit), serial.Shift);
        Assert.Equal(prefix + cells, serial.BitPosition);
        Assert.Null(machine.UnsupportedActiveFeature);
    }

    [Theory]
    [InlineData(32, 1, 3)]
    [InlineData(33, 1, 2)]
    [InlineData(34, 0, 2)]
    public void FastChangeRetainsAcceptedSlowWindowThenResumesOneCellInput(int prefix, int bit, int cells)
    {
        using var machine = new LightweightA500Machine();
        var drive = SelectedDrive();
        var serial = new LightweightDiskSerial();
        serial.OnDriveChanged(drive, 0);
        machine.WriteCustomRegisterFromCopper(0x09E, 0x8100, machine.Cycle);
        for (var i = 0; i < prefix; i++) SampleCell(ref serial, drive, machine);
        machine.WriteCustomRegisterFromCopper(0x09E, 0x0100, machine.Cycle);
        var shift = serial.Shift;
        SampleCell(ref serial, drive, machine);
        var pendingArrival = serial.NextCycle;
        machine.WriteCustomRegisterFromCopper(0x09E, 0x8100, machine.Cycle);
        Assert.Equal(pendingArrival, serial.NextCycle);
        Assert.Equal(shift, serial.Shift);

        for (var i = 1; i < cells; i++) SampleCell(ref serial, drive, machine);
        shift = (ushort)((shift << 1) | bit);
        Assert.Equal(shift, serial.Shift);
        var nextBit = (drive.Track[serial.BitPosition >> 3] >> (7 - (serial.BitPosition & 7))) & 1;
        SampleCell(ref serial, drive, machine);

        Assert.Equal((ushort)((shift << 1) | nextBit), serial.Shift);
        Assert.Equal(prefix + cells + 1, serial.BitPosition);
        Assert.Null(machine.UnsupportedActiveFeature);
    }

    [Fact]
    public void PartialByteSurvivesRateChangeAndSlowSyncStillLatchesWithoutDma()
    {
        using var machine = new LightweightA500Machine();
        var drive = SelectedDrive();
        var serial = new LightweightDiskSerial();
        serial.OnDriveChanged(drive, 0);
        machine.WriteCustomRegisterFromCopper(0x09E, 0x8100, machine.Cycle);
        for (var i = 0; i < 7; i++) SampleCell(ref serial, drive, machine);
        Assert.Equal(0x55, serial.Shift);
        var status = new LightweightRegisters();
        Assert.Equal(0, serial.ReadByteStatus(status) & 0x8000);
        machine.WriteCustomRegisterFromCopper(0x07E, 0x00AB, machine.Cycle);
        machine.WriteCustomRegisterFromCopper(0x09E, 0x0100, machine.Cycle);
        SampleCell(ref serial, drive, machine); // 01 + settling cell.
        SampleCell(ref serial, drive, machine);
        Assert.Equal(0, serial.ReadByteStatus(status) & 0x8000);
        SampleCell(ref serial, drive, machine);

        Assert.Equal(0x90AB, serial.ReadByteStatus(status));
        Assert.Equal(0x10AB, serial.ReadByteStatus(status));
        Assert.NotEqual(0, machine.Intreq & 0x1000);
        Assert.Equal(0, machine.Dmacon);
        Assert.Null(machine.UnsupportedActiveFeature);
    }

    [Fact]
    public void SlowRecoveredWordUsesExistingPhysicalRamDmaPhases()
    {
        using var machine = RunningMachine();
        machine.WriteChipWordDma(0x1000, 0);
        machine.WriteChipWordDma(0x1002, 0);
        machine.WriteCustomRegisterFromCopper(0x09E, 0x0100, machine.Cycle);
        machine.WriteCustomRegisterFromCopper(0x022, 0x1000, machine.Cycle);
        machine.WriteCustomRegisterFromCopper(0x096, 0x8210, machine.Cycle);
        machine.WriteCustomRegisterFromCopper(0x024, 0x8001, machine.Cycle);
        machine.WriteCustomRegisterFromCopper(0x024, 0x8001, machine.Cycle);
        var first = machine.DiskSerialNextCycle;
        machine.AdvanceHardwareTo(first + 30 * 14); // Only 15 recovered bits.
        Assert.Equal(0u, U32(machine.ChipRam.Span.Slice(0x1000)));
        Assert.Equal(1, machine.DiskDmaRemaining);
        machine.AdvanceHardwareTo(first + 31 * 14 + LightweightClock.CpuCyclesPerLine);

        Assert.Equal(0xFFFF0000u, U32(machine.ChipRam.Span.Slice(0x1000)));
        Assert.False(machine.DiskDmaActive);
        Assert.NotEqual(0, machine.Intreq & 2);
        Assert.Null(machine.UnsupportedActiveFeature);
    }

    private static LightweightFloppyDrive SelectedDrive()
    {
        var drive = new LightweightFloppyDrive();
        drive.Mount(new byte[LightweightFloppyDrive.StandardAdfBytes]);
        drive.WriteControlPins(0x77, 0);
        return drive;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void AcceptedSlowWindowSurvivesFieldOrLineBoundary(int line)
    {
        using var machine = new LightweightA500Machine();
        var drive = SelectedDrive();
        var serial = new LightweightDiskSerial();
        var boundary = 30L * LightweightClock.CpuCyclesPerFrame + line * LightweightClock.CpuCyclesPerLine;
        machine.AdvanceHardwareTo(boundary - 16);
        serial.OnDriveChanged(drive, machine.Cycle);
        SampleCell(ref serial, drive, machine); // First pulse two cycles before wrap.
        Assert.Equal(boundary - 2, machine.Cycle);
        Assert.Equal(0, serial.Shift);
        machine.AdvanceHardwareTo(boundary);
        Assert.Equal(0, serial.Shift);
        SampleCell(ref serial, drive, machine);
        Assert.Equal(1, serial.Shift);
        Assert.Equal(2, serial.BitPosition);
        Assert.Null(machine.UnsupportedActiveFeature);
    }

    [Fact]
    public void ResetDiscardsIncompleteSlowWindow()
    {
        using var machine = new LightweightA500Machine();
        var drive = SelectedDrive();
        var serial = new LightweightDiskSerial();
        serial.OnDriveChanged(drive, 0);
        SampleCell(ref serial, drive, machine);
        serial.Reset();
        Assert.Equal(long.MaxValue, serial.NextCycle);
        serial.OnDriveChanged(drive, machine.Cycle);
        SampleCell(ref serial, drive, machine);
        Assert.Equal(0, serial.Shift);
        SampleCell(ref serial, drive, machine);
        Assert.Equal(1, serial.Shift);
    }

    private static void SampleCell(ref LightweightDiskSerial serial, LightweightFloppyDrive drive,
        LightweightA500Machine machine)
    {
        machine.AdvanceHardwareTo(serial.NextCycle);
        serial.Step(machine.Cycle, drive, machine);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SerialExecutionAndIndexDoNotAllocate(bool slow)
    {
        using var machine = RunningMachine();
        if (slow) machine.WriteCustomRegisterFromCopper(0x09E, 0x0100, machine.Cycle);
        machine.AdvanceHardwareTo(machine.DiskSerialNextCycle + 1000);
        var target = machine.Cycle + LightweightPaulaAudio.PalCpuFrequency;
        var before = GC.GetAllocatedBytesForCurrentThread();
        machine.AdvanceHardwareTo(target);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.NotEqual(0, machine.CiaBPendingInterrupts & 0x10);
        Assert.Null(machine.UnsupportedActiveFeature);
    }

    private static uint U32(ReadOnlySpan<byte> data) => BinaryPrimitives.ReadUInt32BigEndian(data);
    private static uint Decode(ReadOnlySpan<byte> odd, ReadOnlySpan<byte> even)
        => ((U32(odd) & 0x55555555u) << 1) | (U32(even) & 0x55555555u);

    private static LightweightA500Machine RunningMachine()
    {
        var machine = new LightweightA500Machine();
        machine.MountAdf(new byte[LightweightFloppyDrive.StandardAdfBytes]);
        machine.WriteCustomRegisterFromCopper(0x09E, 0x8100, machine.Cycle);
        WriteCia(machine, 0xBFD100, 0x77);
        WriteCia(machine, 0xBFD300, 0xFF);
        return machine;
    }

    private static void WriteCia(LightweightA500Machine machine, uint address, byte value)
    {
        var cycle = machine.Cycle;
        machine.WriteByte(address, value, ref cycle, M68kBusAccessKind.CpuDataWrite);
    }

    private static ushort ReadStatus(LightweightA500Machine machine)
    {
        var cycle = machine.Cycle;
        return machine.ReadWord(0xDFF01A, ref cycle, M68kBusAccessKind.CpuDataRead);
    }
}
