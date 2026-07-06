using CopperMod.Amiga;
using CopperDisk;
using System.Reflection;

namespace CopperMod.Amiga.Tests;

public sealed class HardwareSpecializationTests
{
    [Fact]
    public void BlitterAreaKernelMathMatchesScalarReference()
    {
        foreach (var descending in new[] { false, true })
        {
            foreach (var shiftA in new[] { 0, 1, 7, 15 })
            {
                foreach (var shiftB in new[] { 0, 3, 11, 15 })
                {
                    foreach (var fillMode in new[] { 0, 1, 2, 3 })
                    {
                        var key = new BlitterKernelKey(
                            false,
                            true,
                            true,
                            true,
                            true,
                            0xCA,
                            shiftA,
                            shiftB,
                            descending,
                            fillMode != 0,
                            fillMode == 2,
                            false,
                            false,
                            false,
                            false,
                            false);
                        var kernelState = new BlitterAreaKernelState
                        {
                            PreviousA = 0x1357,
                            PreviousB = 0x2468,
                            FillCarry = fillMode == 3
                        };
                        var referencePreviousA = kernelState.PreviousA;
                        var referencePreviousB = kernelState.PreviousB;
                        var referenceFillCarry = kernelState.FillCarry;

                        var actual = BlitterKernelMath.ExecuteArea(
                            key,
                            ref kernelState,
                            0xF0F0,
                            0x0FF0,
                            0xAAAA,
                            0x3FFC);
                        var expected = ExecuteAreaReference(
                            key,
                            ref referencePreviousA,
                            ref referencePreviousB,
                            ref referenceFillCarry,
                            0xF0F0,
                            0x0FF0,
                            0xAAAA,
                            0x3FFC);

                        Assert.Equal(expected, actual);
                        Assert.Equal(referencePreviousA, kernelState.PreviousA);
                        Assert.Equal(referencePreviousB, kernelState.PreviousB);
                        Assert.Equal(referenceFillCarry, kernelState.FillCarry);
                    }
                }
            }
        }
    }

    [Fact]
    public void PreparedTrackReadsMatchRawTrackAtArbitraryOffsets()
    {
        var track = new AmigaEncodedTrack(new byte[] { 0x44, 0x89, 0xAA, 0x55, 0xF0 }, bitLength: 37);
        var prepared = new AmigaPreparedTrack(track);

        for (var offset = -19; offset < track.BitLength + 32; offset++)
        {
            Assert.Equal(track.ReadByteAtBit(offset), prepared.ReadByte(offset));
            Assert.Equal(track.ReadUInt16AtBit(offset), prepared.ReadUInt16(offset));
        }
    }

    [Fact]
    public void PreparedTrackSyncIndexHandlesRepeatedWrappedAndMissingSync()
    {
        const ushort sync = 0x8001;
        var track = AmigaEncodedTrack.FromBytes(WordsToBytes(sync, 0x0000, sync, sync, 0x0000));
        var prepared = new AmigaPreparedTrack(track);
        Span<int> offsets = stackalloc int[8];

        var count = prepared.CopySyncOffsets(sync, offsets, out var cacheHit);

        Assert.False(cacheHit);
        Assert.Equal(3, count);
        Assert.Equal(new[] { 0, 32, 48 }, offsets[..count].ToArray());
        Assert.Equal(0, prepared.FindSyncOffset(sync, 0));
        Assert.Equal(32, prepared.FindSyncOffset(sync, 40));
        Assert.True(prepared.TryFindSyncWithinNextWord(sync, 20, out var distance, out var syncOffset));
        Assert.Equal(12, distance);
        Assert.Equal(32, syncOffset);

        count = prepared.CopySyncOffsets(sync, offsets, out cacheHit);
        Assert.True(cacheHit);
        Assert.Equal(3, count);
        Assert.Equal(0, prepared.CopySyncOffsets(0xA1A1, offsets, out _));
        Assert.Equal(-1, prepared.FindSyncOffset(0xA1A1, 0));
    }

    [Fact]
    public void PreparedTrackByteSyncIndexMatchesRawTrackAtArbitraryOffsets()
    {
        var track = new AmigaEncodedTrack(new byte[] { 0x44, 0x89, 0xAA, 0x55, 0xF0 }, bitLength: 37);
        var prepared = new AmigaPreparedTrack(track);
        Span<int> offsets = stackalloc int[64];

        foreach (var sync in new byte[] { 0x44, 0x89, 0x92, 0xA9, 0xF0, 0x00 })
        {
            var count = prepared.CopyByteSyncOffsets(sync, offsets, out _);
            var expected = new List<int>();
            for (var offset = 0; offset < track.BitLength; offset++)
            {
                if (track.ReadByteAtBit(offset) == sync)
                {
                    expected.Add(offset);
                }
            }

            Assert.Equal(expected, offsets[..count].ToArray());
        }
    }

    [Fact]
    public void BlitterSpecializationCountersRecordKernelUse()
    {
        var bus = new AmigaBus(enableHardwareSpecialization: true);
        BigEndian.WriteUInt16(bus.ChipRam, 0x3000, 0x1234);
        bus.WriteWord(0x00DFF096, 0x8240);
        bus.WriteWord(0x00DFF040, 0x09F0);
        bus.WriteWord(0x00DFF050, 0x0000);
        bus.WriteWord(0x00DFF052, 0x3000);
        bus.WriteWord(0x00DFF054, 0x0000);
        bus.WriteWord(0x00DFF056, 0x4000);
        bus.WriteWord(0x00DFF058, 0x0041);
        bus.AdvanceDmaTo(10_000);

        var snapshot = bus.Blitter.CaptureSnapshot();
        Assert.False(snapshot.Busy);
        Assert.Equal(0x1234, BigEndian.ReadUInt16(bus.ChipRam, 0x4000, "blitter result"));
        Assert.True(snapshot.SpecializationCounters.KernelMisses >= 1);
        Assert.True(snapshot.SpecializationCounters.GeneratedKernels >= 1);
        Assert.Equal(0, snapshot.SpecializationCounters.ScalarFallbacks);
        Assert.True(snapshot.SpecializationCounters.SlotQueueAttempts >= 1);
        Assert.True(snapshot.SpecializationCounters.SlotQueueEnabledBlits >= 1);
        Assert.True(snapshot.SpecializationCounters.SlotQueueWords >= 1);
        Assert.True(snapshot.SpecializationCounters.SlotQueueCommittedOps >= 2);
        Assert.True(snapshot.SpecializationCounters.SpecializedReservations >= 2);
        Assert.True(snapshot.SpecializationCounters.RowPipelineAttempts >= 1);
        Assert.True(snapshot.SpecializationCounters.RowPipelineUsed >= 1);
        Assert.True(snapshot.SpecializationCounters.RowPipelineWords >= 1);
        Assert.True(snapshot.SpecializationCounters.RowPipelineCompletions >= 1);
        Assert.True(snapshot.SpecializationCounters.AToDRowWords >= 1);
    }

    [Fact]
    public void HardwareSpecializationIsDisabledByDefault()
    {
        var bus = new AmigaBus();
        BigEndian.WriteUInt16(bus.ChipRam, 0x3000, 0x1234);
        bus.WriteWord(0x00DFF096, 0x8240);
        bus.WriteWord(0x00DFF040, 0x09F0);
        bus.WriteWord(0x00DFF050, 0x0000);
        bus.WriteWord(0x00DFF052, 0x3000);
        bus.WriteWord(0x00DFF054, 0x0000);
        bus.WriteWord(0x00DFF056, 0x4000);
        bus.WriteWord(0x00DFF058, 0x0041);
        bus.AdvanceDmaTo(10_000);

        var snapshot = bus.Blitter.CaptureSnapshot();
        Assert.False(snapshot.Busy);
        Assert.Equal(0x1234, BigEndian.ReadUInt16(bus.ChipRam, 0x4000, "blitter result"));
        Assert.Equal(0, snapshot.SpecializationCounters.KernelHits);
        Assert.Equal(0, snapshot.SpecializationCounters.KernelMisses);
        Assert.Equal(0, snapshot.SpecializationCounters.GeneratedKernels);
        Assert.Equal(0, snapshot.SpecializationCounters.ScalarFallbacks);
        Assert.Equal(0, snapshot.SpecializationCounters.SlotQueueAttempts);
        Assert.Equal(0, snapshot.SpecializationCounters.SlotQueueEnabledBlits);
        Assert.Equal(0, snapshot.SpecializationCounters.SlotQueueWords);
        Assert.Equal(0, snapshot.SpecializationCounters.SlotQueueCommittedOps);
        Assert.Equal(0, snapshot.SpecializationCounters.SpecializedReservations);
        Assert.Equal(0, snapshot.SpecializationCounters.RowPipelineAttempts);
        Assert.Equal(0, snapshot.SpecializationCounters.RowPipelineUsed);
        Assert.Equal(0, snapshot.SpecializationCounters.RowPipelineWords);
        Assert.Equal(0, snapshot.SpecializationCounters.RowPipelineCompletions);
        Assert.Equal(0, snapshot.SpecializationCounters.AToDRowWords);
    }

    [Fact]
    public void DiskPreparedTrackAccelerationStaysEnabledWhenHardwareSpecializationIsDisabled()
    {
        var bus = new AmigaBus(enableHardwareSpecialization: false);
        bus.Disk.Drive0.Insert(AmigaDiskImage.FromEncodedTracks(CreateTrackSetWithWords(0x4489, 0x1234, 0x5678)));
        SelectDriveAndStartMotor(bus);
        var readyCycle = AdvanceToMotorReady(bus);
        bus.WriteWord(0x00DFF096, 0x8210, readyCycle);
        bus.Paula.AdvanceTo(readyCycle);
        SetDiskPointer(bus, 0x4000, readyCycle);
        WriteDsklenStartSequence(bus, words: 2, readyCycle);

        bus.AdvanceDmaTo(bus.Disk.CaptureSnapshot().ActiveDmaCompletionCycle);

        Assert.True(GetDrive0PreparedTrack(bus).HasRollingWords);
        Assert.Equal(0, bus.Disk.CaptureSnapshot().SpecializationCounters.RollingWindowHits);
    }

    private static ushort ExecuteAreaReference(
        BlitterKernelKey key,
        ref ushort previousA,
        ref ushort previousB,
        ref bool fillCarry,
        ushort rawA,
        ushort rawB,
        ushort rawC,
        ushort mask)
    {
        rawA = (ushort)(rawA & mask);
        var sourceA = ShiftSourceReference(rawA, ref previousA, key.ShiftA, key.Descending);
        var sourceB = ShiftSourceReference(rawB, ref previousB, key.ShiftB, key.Descending);
        var output = ApplyMintermReference(key.Minterm, sourceA, sourceB, rawC);
        return key.FillEnabled
            ? ApplyFillReference(output, key.FillExclusive, ref fillCarry)
            : output;
    }

    private static ushort ShiftSourceReference(ushort current, ref ushort previous, int shift, bool descending)
    {
        shift &= 0x0F;
        if (shift == 0)
        {
            previous = current;
            return current;
        }

        var combined = descending
            ? ((uint)current << 16) | previous
            : ((uint)previous << 16) | current;
        var value = descending
            ? (ushort)(combined >> (16 - shift))
            : (ushort)(combined >> shift);
        previous = current;
        return value;
    }

    private static ushort ApplyMintermReference(byte minterm, ushort sourceA, ushort sourceB, ushort sourceC)
    {
        ushort value = 0;
        for (var bit = 0; bit < 16; bit++)
        {
            var mask = 1 << bit;
            var selector = 0;
            if ((sourceA & mask) != 0)
            {
                selector |= 4;
            }

            if ((sourceB & mask) != 0)
            {
                selector |= 2;
            }

            if ((sourceC & mask) != 0)
            {
                selector |= 1;
            }

            if (((minterm >> selector) & 1) != 0)
            {
                value |= (ushort)mask;
            }
        }

        return value;
    }

    private static ushort ApplyFillReference(ushort value, bool exclusive, ref bool fillCarry)
    {
        ushort output = 0;
        for (var bit = 0; bit < 16; bit++)
        {
            var mask = (ushort)(1 << bit);
            var input = (value & mask) != 0;
            if (exclusive)
            {
                if (fillCarry)
                {
                    output |= mask;
                }

                if (input)
                {
                    fillCarry = !fillCarry;
                }

                continue;
            }

            if (fillCarry || input)
            {
                output |= mask;
            }

            if (input)
            {
                fillCarry = !fillCarry;
            }
        }

        return output;
    }

    private static byte[] WordsToBytes(params ushort[] words)
    {
        var data = new byte[words.Length * 2];
        for (var i = 0; i < words.Length; i++)
        {
            BigEndian.WriteUInt16(data, i * 2, words[i]);
        }

        return data;
    }

    private static AmigaPreparedTrack GetDrive0PreparedTrack(AmigaBus bus)
    {
        var streamsField = typeof(AmigaDiskController).GetField("_streams", BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new MissingFieldException(typeof(AmigaDiskController).FullName, "_streams");
        var streams = (Array)(streamsField.GetValue(bus.Disk) ??
            throw new InvalidOperationException("Disk streams were not initialized."));
        var stream = streams.GetValue(0) ??
            throw new InvalidOperationException("Drive 0 disk stream was not initialized.");
        var preparedTrackProperty = stream.GetType().GetProperty("PreparedTrack", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
            throw new MissingMemberException(stream.GetType().FullName, "PreparedTrack");
        return (AmigaPreparedTrack)(preparedTrackProperty.GetValue(stream) ??
            throw new InvalidOperationException("Prepared track was not initialized."));
    }

    private static AmigaEncodedTrack[] CreateTrackSetWithWords(params ushort[] words)
    {
        var tracks = new AmigaEncodedTrack[AmigaDiskImage.TrackCount];
        var blankTrack = AmigaEncodedTrack.FromBytes(WordsToBytes(0xAAAA));
        for (var trackIndex = 0; trackIndex < tracks.Length; trackIndex++)
        {
            tracks[trackIndex] = blankTrack;
        }

        tracks[0] = AmigaEncodedTrack.FromBytes(WordsToBytes(words));
        return tracks;
    }

    private static void SelectDriveAndStartMotor(AmigaBus bus, long cycle = 0)
    {
        bus.WriteByte(0x00BFD100, 0xFF, cycle);
        bus.WriteByte(0x00BFD300, 0xFF, cycle);
        bus.WriteByte(0x00BFD100, 0x77, cycle);
    }

    private static long AdvanceToMotorReady(AmigaBus bus, long cycle = 0)
    {
        var readyCycle = cycle + Math.Max(1, (long)Math.Round(AmigaConstants.A500PalCpuClockHz * 0.5));
        bus.AdvanceDmaTo(readyCycle);
        return readyCycle;
    }

    private static void SetDiskPointer(AmigaBus bus, uint targetAddress, long cycle)
    {
        bus.WriteWord(0x00DFF020, (ushort)(targetAddress >> 16), cycle);
        bus.WriteWord(0x00DFF022, (ushort)targetAddress, cycle);
    }

    private static void WriteDsklenStartSequence(AmigaBus bus, ushort words, long cycle)
    {
        var dsklen = (ushort)(0x8000 | words);
        bus.WriteWord(0x00DFF024, dsklen, cycle);
        bus.WriteWord(0x00DFF024, dsklen, cycle);
    }
}
