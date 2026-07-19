using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class CustomRegisterFileTests
{
    private const uint CustomBase = 0x00DFF000;

    public static TheoryData<int, int, int> AllChipsetCombinations => new()
    {
        { (int)DmaChipModel.OcsAgnus, (int)DisplayChipModel.OcsDenise, (int)VideoStandard.Pal },
        { (int)DmaChipModel.OcsAgnus, (int)DisplayChipModel.OcsDenise, (int)VideoStandard.Ntsc },
        { (int)DmaChipModel.OcsAgnus, (int)DisplayChipModel.EcsDenise, (int)VideoStandard.Pal },
        { (int)DmaChipModel.OcsAgnus, (int)DisplayChipModel.EcsDenise, (int)VideoStandard.Ntsc },
        { (int)DmaChipModel.EcsAgnus, (int)DisplayChipModel.OcsDenise, (int)VideoStandard.Pal },
        { (int)DmaChipModel.EcsAgnus, (int)DisplayChipModel.OcsDenise, (int)VideoStandard.Ntsc },
        { (int)DmaChipModel.EcsAgnus, (int)DisplayChipModel.EcsDenise, (int)VideoStandard.Pal },
        { (int)DmaChipModel.EcsAgnus, (int)DisplayChipModel.EcsDenise, (int)VideoStandard.Ntsc },
        { (int)DmaChipModel.OcsAgnus, (int)DisplayChipModel.AgaLisa, (int)VideoStandard.Pal },
        { (int)DmaChipModel.OcsAgnus, (int)DisplayChipModel.AgaLisa, (int)VideoStandard.Ntsc },
        { (int)DmaChipModel.EcsAgnus, (int)DisplayChipModel.AgaLisa, (int)VideoStandard.Pal },
        { (int)DmaChipModel.EcsAgnus, (int)DisplayChipModel.AgaLisa, (int)VideoStandard.Ntsc },
        { (int)DmaChipModel.AgaAlice, (int)DisplayChipModel.OcsDenise, (int)VideoStandard.Pal },
        { (int)DmaChipModel.AgaAlice, (int)DisplayChipModel.OcsDenise, (int)VideoStandard.Ntsc },
        { (int)DmaChipModel.AgaAlice, (int)DisplayChipModel.EcsDenise, (int)VideoStandard.Pal },
        { (int)DmaChipModel.AgaAlice, (int)DisplayChipModel.EcsDenise, (int)VideoStandard.Ntsc },
        { (int)DmaChipModel.AgaAlice, (int)DisplayChipModel.AgaLisa, (int)VideoStandard.Pal },
        { (int)DmaChipModel.AgaAlice, (int)DisplayChipModel.AgaLisa, (int)VideoStandard.Ntsc }
    };

    [Fact]
    public void SnapshotContainsEveryNormalizedCustomWord()
    {
        var bus = new AmigaBus(chipset: AmigaChipset.OcsPal);

        var snapshot = bus.CaptureCustomRegisterFileSnapshot();

        Assert.Equal(256, snapshot.Count);
        for (var index = 0; index < snapshot.Count; index++)
        {
            Assert.Equal((ushort)(index << 1), snapshot[index].Offset);
        }

        Assert.False(snapshot.Get(0x07C).IsPresent);
        Assert.False(snapshot.Get(0x1FC).IsPresent);
        Assert.Equal("RESERVED_0DC", snapshot.Get(0x0DC).Name);
    }

    [Theory]
    [MemberData(nameof(AllChipsetCombinations))]
    public void EveryChipsetProfileResolvesACompleteDefinitionFile(int agnus, int denise, int video)
    {
        var chipset = new AmigaChipset((DmaChipModel)agnus, (DisplayChipModel)denise, (VideoStandard)video);
        var snapshot = new CustomRegisterFile(chipset).CaptureSnapshot();

        Assert.Equal(256, snapshot.Count);
        Assert.All(snapshot, entry =>
        {
            Assert.Equal(0, entry.Offset & 1);
            Assert.False(string.IsNullOrWhiteSpace(entry.Name));
            if (!entry.IsPresent && entry.ImplementationStatus != CustomRegisterImplementationStatus.Unspecified)
            {
                Assert.Equal(CustomRegisterImplementationStatus.Absent, entry.ImplementationStatus);
                Assert.Equal(CustomRegisterWriteTarget.None, entry.WriteTargets);
            }
        });
    }

    [Fact]
    public void AgaProfilesInheritEcsRegisterCapabilitiesIndependently()
    {
        var aliceOnly = new CustomRegisterFile(new AmigaChipset(
            DmaChipModel.AgaAlice,
            DisplayChipModel.OcsDenise,
            VideoStandard.Pal)).CaptureSnapshot();
        var lisaOnly = new CustomRegisterFile(new AmigaChipset(
            DmaChipModel.OcsAgnus,
            DisplayChipModel.AgaLisa,
            VideoStandard.Pal)).CaptureSnapshot();

        Assert.True(aliceOnly.Get(0x05C).IsPresent);
        Assert.False(aliceOnly.Get(0x07C).IsPresent);
        Assert.False(lisaOnly.Get(0x05C).IsPresent);
        Assert.True(lisaOnly.Get(0x07C).IsPresent);
    }

    [Fact]
    public void DisplayIdentificationResetIsResolvedByDisplayChip()
    {
        var ecs = new CustomRegisterFile(AmigaChipset.EcsPal).CaptureSnapshot().Get(0x07C);
        var aga = new CustomRegisterFile(AmigaChipset.AgaPal).CaptureSnapshot().Get(0x07C);

        Assert.Equal((ushort)DisplayChipIdentification.EcsDenise, ecs.ResetValue);
        Assert.Equal(0xFFFF, ecs.ResetKnownMask);
        Assert.Equal((ushort)DisplayChipIdentification.EcsDenise, ecs.StoredValue);
        Assert.Equal((ushort)DisplayChipIdentification.AgaLisa, aga.ResetValue);
        Assert.Equal(0x00FF, aga.ResetKnownMask);
        Assert.Equal((ushort)DisplayChipIdentification.AgaLisa, aga.StoredValue);
    }

    [Fact]
    public void AgaOnlyRegistersRemainExplicitlyUnspecifiedInTheFirstProfileSlice()
    {
        var snapshot = new CustomRegisterFile(AmigaChipset.AgaPal).CaptureSnapshot();

        Assert.Equal(CustomRegisterImplementationStatus.Unspecified, snapshot.Get(0x10C).ImplementationStatus);
        Assert.Equal(CustomRegisterImplementationStatus.Unspecified, snapshot.Get(0x10E).ImplementationStatus);
        Assert.Equal(CustomRegisterImplementationStatus.Unspecified, snapshot.Get(0x11C).ImplementationStatus);
        Assert.Equal(CustomRegisterImplementationStatus.Unspecified, snapshot.Get(0x11E).ImplementationStatus);
        Assert.Equal(CustomRegisterImplementationStatus.Unspecified, snapshot.Get(0x1FC).ImplementationStatus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AgaExecutionFailsFastAtBusConstruction(bool ntsc)
    {
        var chipset = ntsc ? AmigaChipset.AgaNtsc : AmigaChipset.AgaPal;

        var error = Assert.Throws<NotSupportedException>(() => new AmigaBus(chipset: chipset));

        Assert.Contains("AGA Alice/Lisa execution is not implemented", error.Message);
    }

    [Theory]
    [InlineData((int)DmaChipModel.AgaAlice, (int)DisplayChipModel.OcsDenise)]
    [InlineData((int)DmaChipModel.OcsAgnus, (int)DisplayChipModel.AgaLisa)]
    public void MixedAgaExecutionProfilesAlsoFailFast(int dmaChip, int displayChip)
    {
        var chipset = new AmigaChipset(
            (DmaChipModel)dmaChip,
            (DisplayChipModel)displayChip,
            VideoStandard.Pal);

        Assert.Throws<NotSupportedException>(() => new AmigaBus(chipset: chipset));
    }

    [Fact]
    public void ResolvedEntriesSeparateReadWriteMeaningAndExecutionTargets()
    {
        var snapshot = new AmigaBus(chipset: AmigaChipset.EcsPal).CaptureCustomRegisterFileSnapshot();

        var bltddat = snapshot.Get(0x000);
        Assert.Equal("BLTDDAT", bltddat.ReadName);
        Assert.Null(bltddat.WriteName);
        Assert.Equal(CustomRegisterReadHandler.ChipDataBusLatch, bltddat.ReadHandler);
        Assert.Equal(CustomRegisterWriteSemantics.Ignore, bltddat.WriteSemantics);

        var dmacon = snapshot.Get(0x096);
        Assert.Null(dmacon.ReadName);
        Assert.Equal("DMACON", dmacon.WriteName);
        Assert.Equal(CustomRegisterWriteSemantics.SetClear, dmacon.WriteSemantics);
        Assert.True(dmacon.WriteTargets.HasFlag(CustomRegisterWriteTarget.Agnus));
        Assert.True(dmacon.WriteTargets.HasFlag(CustomRegisterWriteTarget.Paula));
        Assert.True(dmacon.WriteTargets.HasFlag(CustomRegisterWriteTarget.Display));
        Assert.True(dmacon.WriteTargets.HasFlag(CustomRegisterWriteTarget.Blitter));
        Assert.True(dmacon.WriteTargets.HasFlag(CustomRegisterWriteTarget.Disk));

        var copjmp1 = snapshot.Get(0x088);
        Assert.Equal(CustomRegisterWriteSemantics.Strobe, copjmp1.WriteSemantics);
        Assert.True(copjmp1.WriteTargets.HasFlag(CustomRegisterWriteTarget.Agnus));
        Assert.True(copjmp1.WriteTargets.HasFlag(CustomRegisterWriteTarget.Display));

        Assert.Equal(CustomRegisterImplementationStatus.Unimplemented, snapshot.Get(0x030).ImplementationStatus);
        Assert.Equal(CustomRegisterImplementationStatus.Absent, snapshot.Get(0x0DC).ImplementationStatus);
    }

    [Fact]
    public void WritesArePublishedGloballyButOnlyDispatchedToTheirResolvedOwner()
    {
        var bus = new AmigaBus(chipset: AmigaChipset.OcsPal);

        bus.WriteWord(CustomBase + 0x040, 0x09F0);
        bus.WriteWord(CustomBase + 0x0A6, 0x0010);

        Assert.Contains(bus.CustomRegisterWrites, write => write.Address == 0x040 && write.Value == 0x09F0);
        Assert.DoesNotContain(bus.Paula.Writes, write => write.Address == 0x040);
        Assert.Contains(bus.Paula.Writes, write => write.Address == 0x0A6 && write.Value == 0x0010);
    }

    [Fact]
    public void DevicePublishedRegisterExposesItsEffectiveMaskedValue()
    {
        var bus = new AmigaBus(chipset: AmigaChipset.EcsPal);
        var reset = bus.CaptureCustomRegisterFileSnapshot().Get(0x1E2);
        Assert.Equal(CustomRegisterStorageMode.DevicePublished, reset.StorageMode);
        Assert.True(reset.HasStoredValue);
        Assert.Equal(0, reset.StoredValue);

        bus.WriteWord(CustomBase + 0x1E2, 0xFFFF);

        var written = bus.CaptureCustomRegisterFileSnapshot().Get(0x1E2);
        Assert.True(written.HasStoredValue);
        Assert.Equal(0x01FF, written.StoredValue);
        var readCycle = 8L;
        Assert.Equal(
            bus.ReadWord(CustomBase + 0x1E2, ref readCycle, AmigaBusAccessKind.CpuDataRead),
            written.StoredValue);
    }

    [Theory]
    [InlineData(0xA500, 0x00F3, 0x000F, (int)CustomRegisterWriteSemantics.MaskedStore, 0xA503)]
    [InlineData(0x0003, 0x800C, 0x000F, (int)CustomRegisterWriteSemantics.SetClear, 0x000F)]
    [InlineData(0x000F, 0x0005, 0x000F, (int)CustomRegisterWriteSemantics.SetClear, 0x000A)]
    [InlineData(0x1234, 0xFFFF, 0xFFFF, (int)CustomRegisterWriteSemantics.Strobe, 0x1234)]
    public void RegisterFileStorageAppliesMasksAndSetClearSemantics(
        int previous,
        int value,
        int mask,
        int semantics,
        int expected)
    {
        Assert.Equal(
            (ushort)expected,
            CustomRegisterFile.ApplyStoredWriteValue(
                (ushort)previous,
                (ushort)value,
                (ushort)mask,
                (CustomRegisterWriteSemantics)semantics));
    }

    [Fact]
    public void SnapshotPresenceIsResolvedForTheSelectedChipset()
    {
        var ocs = new AmigaBus(chipset: AmigaChipset.OcsPal);
        var ecs = new AmigaBus(chipset: AmigaChipset.EcsPal);

        Assert.False(ocs.CaptureCustomRegisterFileSnapshot().Get(0x07C).IsPresent);
        Assert.True(ecs.CaptureCustomRegisterFileSnapshot().Get(0x07C).IsPresent);
        Assert.False(ocs.CaptureCustomRegisterFileSnapshot().Get(0x1C0).IsPresent);
        Assert.True(ecs.CaptureCustomRegisterFileSnapshot().Get(0x1C0).IsPresent);
    }

    [Fact]
    public void CpuByteWritePublishesDuplicatedBusWordAndLane()
    {
        var bus = new AmigaBus(chipset: AmigaChipset.EcsPal);
        long cycle = 0;

        bus.WriteByte(CustomBase + 0x05B, 0x67, ref cycle, AmigaBusAccessKind.CpuDataWrite);

        var entry = bus.CaptureCustomRegisterFileSnapshot().Get(0x05A);
        Assert.True(entry.HasWrite);
        Assert.Equal(0x6767, entry.LastWriteValue);
        Assert.Equal(CustomRegisterObservationWidth.Byte, entry.LastWriteWidth);
        Assert.Equal(CustomRegisterByteLane.Low, entry.LastWriteLane);
        Assert.Equal(AmigaBusRequester.Cpu, entry.LastWriteRequester);
        Assert.Equal(CustomRegisterWriteCause.Explicit, entry.LastWriteCause);
    }

    [Fact]
    public void CpuReadTraceIsOptionalAndDoesNotMutatePublishedState()
    {
        var bus = new AmigaBus(chipset: AmigaChipset.OcsPal);
        long cycle = 0;
        bus.WriteWord(CustomBase + 0x09C, 0x8020, ref cycle, AmigaBusAccessKind.CpuDataWrite);
        var before = bus.CaptureCustomRegisterFileSnapshot().Get(0x01E);

        bus.CaptureCustomRegisterReadTrace(0x01E, 2, 4);
        var value = bus.ReadWord(CustomBase + 0x01E, ref cycle, AmigaBusAccessKind.CpuDataRead);

        var trace = Assert.Single(bus.CustomRegisterReadTrace);
        var after = bus.CaptureCustomRegisterFileSnapshot().Get(0x01E);
        Assert.Equal(value, trace.Value);
        Assert.Equal(0x01E, trace.Address);
        Assert.Equal(before.StoredValue, value);
        Assert.Equal(before.StoredValue, after.StoredValue);
        Assert.Equal(before.StoredValueCycle, after.StoredValueCycle);
    }

    [Fact]
    public void PublishingUnchangedValuePreservesLastChangeCycle()
    {
        var file = new CustomRegisterFile(AmigaChipset.EcsPal);

        Assert.True(file.PublishStoredValue(0x1E2, 0x0042, 10));
        Assert.False(file.PublishStoredValue(0x1E2, 0x0042, 20));

        var entry = file.CaptureSnapshot().Get(0x1E2);
        Assert.Equal(0x0042, entry.StoredValue);
        Assert.Equal(10, entry.StoredValueCycle);
    }

    [Fact]
    public void ChipOwnersPublishResetVisibleValues()
    {
        var snapshot = new AmigaBus(chipset: AmigaChipset.EcsPal)
            .CaptureCustomRegisterFileSnapshot();

        Assert.Equal(CustomRegisterReadHandler.StoredValue, snapshot.Get(0x002).ReadHandler);
        Assert.Equal(0x2000, snapshot.Get(0x002).StoredValue);
        Assert.Equal(0x3000, snapshot.Get(0x018).StoredValue);
        Assert.Equal(0x5500, snapshot.Get(0x016).StoredValue);
        Assert.Equal(0x00FC, snapshot.Get(0x07C).StoredValue);
    }

    [Fact]
    public void PaulaWritesPublishReadableLatchState()
    {
        var bus = new AmigaBus(chipset: AmigaChipset.OcsPal);

        bus.WriteWord(CustomBase + 0x09E, 0x8012);
        bus.WriteWord(CustomBase + 0x09A, 0xC020);
        bus.WriteWord(CustomBase + 0x09C, 0x8024);

        var snapshot = bus.CaptureCustomRegisterFileSnapshot();
        Assert.Equal(0x0012, snapshot.Get(0x010).StoredValue);
        Assert.Equal(0x4020, snapshot.Get(0x01C).StoredValue);
        Assert.Equal(0x0024, snapshot.Get(0x01E).StoredValue);
    }

    [Fact]
    public void DiskAndInputOwnersPublishDiscreteStateChanges()
    {
        var bus = new AmigaBus(chipset: AmigaChipset.OcsPal);

        bus.WriteWord(CustomBase + 0x026, 0xA55A);
        bus.MoveGamePortMouse(0, 3, 5);
        bus.SetGamePortJoystick(1, up: true, down: false, left: false, right: true);
        bus.GamePort0SecondFirePressed = true;

        var snapshot = bus.CaptureCustomRegisterFileSnapshot();
        Assert.Equal(0xA55A, snapshot.Get(0x008).StoredValue);
        Assert.Equal(0x0503, snapshot.Get(0x00A).StoredValue);
        Assert.NotEqual(0, snapshot.Get(0x00C).StoredValue);
        Assert.Equal(0x5100, snapshot.Get(0x016).StoredValue);
    }

    [Fact]
    public void DynamicAndDestructiveReadsRemainDeviceRouted()
    {
        var snapshot = new AmigaBus(chipset: AmigaChipset.EcsPal)
            .CaptureCustomRegisterFileSnapshot();

        Assert.Equal(CustomRegisterReadHandler.BeamPosition, snapshot.Get(0x004).ReadHandler);
        Assert.Equal(CustomRegisterReadHandler.BeamPosition, snapshot.Get(0x006).ReadHandler);
        Assert.Equal(CustomRegisterReadHandler.Disk, snapshot.Get(0x01A).ReadHandler);
        Assert.Equal(CustomRegisterReadHandler.Collision, snapshot.Get(0x00E).ReadHandler);
        Assert.Equal(CustomRegisterReadHandler.Agnus, snapshot.Get(0x1DA).ReadHandler);
    }

    [Fact]
    public void PublishedWordCacheServesBothCpuByteLanes()
    {
        var bus = new AmigaBus(chipset: AmigaChipset.OcsPal);
        long cycle = 0;

        var high = bus.ReadByte(CustomBase + 0x018, ref cycle, AmigaBusAccessKind.CpuDataRead);
        var low = bus.ReadByte(CustomBase + 0x019, ref cycle, AmigaBusAccessKind.CpuDataRead);

        Assert.Equal(0x30, high);
        Assert.Equal(0x00, low);
    }

    [Fact]
    public void BlitterPublishesCompositeDmaconrStatus()
    {
        var bus = new AmigaBus(chipset: AmigaChipset.OcsPal);

        bus.WriteWord(CustomBase + 0x058, 0x0041);

        var dmaconr = bus.CaptureCustomRegisterFileSnapshot().Get(0x002).StoredValue;
        Assert.Equal(0x6000, dmaconr & 0x6000);
    }

    [Fact]
    public void ResetClearsWriteObservations()
    {
        var bus = new AmigaBus(chipset: AmigaChipset.OcsPal);
        long cycle = 0;
        _ = bus.ReadWord(CustomBase + 0x01E, ref cycle, AmigaBusAccessKind.CpuDataRead);
        bus.WriteWord(CustomBase + 0x096, 0x8200, ref cycle, AmigaBusAccessKind.CpuDataWrite);

        bus.Reset();

        var snapshot = bus.CaptureCustomRegisterFileSnapshot();
        Assert.False(snapshot.Get(0x096).HasWrite);
    }
}
