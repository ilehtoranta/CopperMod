using CopperMod.Amiga;
using CopperMod.Amiga.CustomChips.Blitter;

namespace CopperMod.Amiga.Tests;

public sealed class BlitterBatchAdvancementTests
{
    public static IEnumerable<object[]> AscendingAreaChannelCombinations()
    {
        for (var channels = 1; channels < 16; channels++)
        {
            yield return new object[] { channels };
        }
    }

    [Fact]
    public void BoundedCpuWaitMicroOpAdvanceMatchesSingleSlotLoop()
    {
        var scalar = new AmigaBus(captureBusAccesses: false, enableLiveAgnusDma: false);
        var batched = new AmigaBus(captureBusAccesses: false, enableLiveAgnusDma: false);
        StartAreaCopy(scalar);
        StartAreaCopy(batched);

        var targetCycle = scalar.Blitter.CurrentCycle + 96;
        var slotCycle = AgnusChipSlotScheduler.AlignToSlot(scalar.Blitter.CurrentCycle);
        while (slotCycle < targetCycle &&
            scalar.Blitter.Busy &&
            scalar.Blitter.CanUseCpuWaitAreaMicroOps)
        {
            scalar.Blitter.AdvanceCpuWaitAreaMicroOpTo(slotCycle);

            slotCycle += AgnusChipSlotScheduler.SlotCycles;
        }

        batched.Blitter.AdvanceCpuWaitAreaMicroOpsBefore(targetCycle);
        var expected = scalar.Blitter.CaptureSnapshot();
        var actual = batched.Blitter.CaptureSnapshot();

        Assert.Equal(expected.Busy, actual.Busy);
        Assert.Equal(expected.Zero, actual.Zero);
        Assert.Equal(expected.CurrentCycle, actual.CurrentCycle);
        Assert.Equal(expected.SourceA, actual.SourceA);
        Assert.Equal(expected.DestinationD, actual.DestinationD);
        Assert.Equal(expected.WordX, actual.WordX);
        Assert.Equal(expected.RowY, actual.RowY);
        Assert.Equal(expected.LastDmaCycle, actual.LastDmaCycle);
        Assert.Equal(expected.CompletedMicroOps, actual.CompletedMicroOps);
        Assert.Equal(scalar.ChipRam, batched.ChipRam);
    }

    [Theory]
    [MemberData(nameof(AscendingAreaChannelCombinations))]
    public void BoundedAreaMicroOpsMatchReferenceForEveryChannelCombination(int channels)
    {
        var reference = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: false);
        var bounded = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: false);
        reference.SetBlitterAdvanceMode(BlitterAdvanceMode.Reference);
        bounded.SetBlitterAdvanceMode(BlitterAdvanceMode.Bounded);
        StartAreaBlit(reference, channels);
        StartAreaBlit(bounded, channels);

        reference.AdvanceDmaTo(reference.Blitter.CurrentCycle + 10_000);
        bounded.AdvanceDmaTo(bounded.Blitter.CurrentCycle + 10_000);

        var expected = reference.Blitter.CaptureSnapshot();
        var actual = bounded.Blitter.CaptureSnapshot();
        Assert.True(
            expected.CurrentCycle == actual.CurrentCycle,
            $"channels={channels},expected={expected.CurrentCycle},actual={actual.CurrentCycle}," +
            $"referenceAdvance={expected.AdvanceCounters.BoundedAttempts}/{expected.AdvanceCounters.BoundedUses}," +
            $"boundedAdvance={actual.AdvanceCounters.BoundedAttempts}/{actual.AdvanceCounters.BoundedUses}," +
            $"reference=[{string.Join(';', reference.BusAccesses.Select(ToComparableAccess))}]," +
            $"bounded=[{string.Join(';', bounded.BusAccesses.Select(ToComparableAccess))}]");
        Assert.Equal(expected.Busy, actual.Busy);
        Assert.Equal(expected.Zero, actual.Zero);
        Assert.Equal(expected.CurrentCycle, actual.CurrentCycle);
        Assert.Equal(expected.SourceA, actual.SourceA);
        Assert.Equal(expected.SourceB, actual.SourceB);
        Assert.Equal(expected.SourceC, actual.SourceC);
        Assert.Equal(expected.DestinationD, actual.DestinationD);
        Assert.Equal(expected.WordX, actual.WordX);
        Assert.Equal(expected.RowY, actual.RowY);
        Assert.Equal(expected.LastDmaCycle, actual.LastDmaCycle);
        Assert.Equal(expected.CompletedMicroOps, actual.CompletedMicroOps);
        Assert.Equal(reference.ChipRam, bounded.ChipRam);
        Assert.Equal(
            reference.BusAccesses.Select(ToComparableAccess),
            bounded.BusAccesses.Select(ToComparableAccess));
    }

    [Fact]
    public void BoundedAreaMicroOpsCompleteAnAdmittedWordLikeReference()
    {
        var reference = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: false);
        var bounded = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: false);
        reference.SetBlitterAdvanceMode(BlitterAdvanceMode.Reference);
        bounded.SetBlitterAdvanceMode(BlitterAdvanceMode.Bounded);
        StartAreaBlit(reference, channels: 0x0F);
        StartAreaBlit(bounded, channels: 0x0F);

        for (var step = 0; step < 64 && reference.Blitter.Busy; step++)
        {
            var targetCycle = Math.Max(reference.Blitter.CurrentCycle, bounded.Blitter.CurrentCycle) +
                AgnusChipSlotScheduler.SlotCycles;
            reference.AdvanceDmaTo(targetCycle);
            bounded.AdvanceDmaTo(targetCycle);

            var expected = reference.Blitter.CaptureSnapshot();
            var actual = bounded.Blitter.CaptureSnapshot();
            Assert.Equal(expected.CurrentCycle, actual.CurrentCycle);
            Assert.Equal(expected.SourceA, actual.SourceA);
            Assert.Equal(expected.SourceB, actual.SourceB);
            Assert.Equal(expected.SourceC, actual.SourceC);
            Assert.Equal(expected.DestinationD, actual.DestinationD);
            Assert.Equal(expected.WordX, actual.WordX);
            Assert.Equal(expected.RowY, actual.RowY);
            Assert.Equal(expected.CompletedMicroOps, actual.CompletedMicroOps);
            Assert.Equal(reference.ChipRam, bounded.ChipRam);
            Assert.Equal(
                reference.BusAccesses.Select(ToComparableAccess),
                bounded.BusAccesses.Select(ToComparableAccess));
        }
    }

    [Theory]
    [InlineData(0x0002)]
    [InlineData(0x000A)]
    [InlineData(0x0012)]
    [InlineData(0x0016)]
    public void BoundedDescendingAndFillAreaMicroOpsMatchReference(ushort bltcon1)
    {
        var reference = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: false);
        var bounded = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: false);
        reference.SetBlitterAdvanceMode(BlitterAdvanceMode.Reference);
        bounded.SetBlitterAdvanceMode(BlitterAdvanceMode.Bounded);
        bounded.Blitter.SetBoundedExtendedModesEnabledForTest(true);
        StartDescendingAreaBlit(reference, bltcon1);
        StartDescendingAreaBlit(bounded, bltcon1);

        reference.AdvanceDmaTo(reference.Blitter.CurrentCycle + 10_000);
        bounded.AdvanceDmaTo(bounded.Blitter.CurrentCycle + 10_000);

        var expected = reference.Blitter.CaptureSnapshot();
        var actual = bounded.Blitter.CaptureSnapshot();
        Assert.Equal(expected.Busy, actual.Busy);
        Assert.Equal(expected.Zero, actual.Zero);
        Assert.Equal(expected.CurrentCycle, actual.CurrentCycle);
        Assert.Equal(expected.SourceA, actual.SourceA);
        Assert.Equal(expected.SourceB, actual.SourceB);
        Assert.Equal(expected.SourceC, actual.SourceC);
        Assert.Equal(expected.DestinationD, actual.DestinationD);
        Assert.Equal(expected.LastDmaCycle, actual.LastDmaCycle);
        Assert.Equal(expected.CompletedMicroOps, actual.CompletedMicroOps);
        Assert.Equal(reference.ChipRam, bounded.ChipRam);
        Assert.Equal(
            reference.BusAccesses.Select(ToComparableAccess),
            bounded.BusAccesses.Select(ToComparableAccess));
    }

    [Theory]
    [InlineData(0x0001)]
    [InlineData(0x0005)]
    [InlineData(0x0019)]
    [InlineData(0x001B)]
    public void BoundedLineMicroOpsMatchReference(ushort bltcon1)
    {
        var reference = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: false);
        var bounded = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: false);
        reference.SetBlitterAdvanceMode(BlitterAdvanceMode.Reference);
        bounded.SetBlitterAdvanceMode(BlitterAdvanceMode.Bounded);
        bounded.Blitter.SetBoundedExtendedModesEnabledForTest(true);
        StartLineBlit(reference, bltcon1);
        StartLineBlit(bounded, bltcon1);

        reference.AdvanceDmaTo(reference.Blitter.CurrentCycle + 10_000);
        bounded.AdvanceDmaTo(bounded.Blitter.CurrentCycle + 10_000);

        var expected = reference.Blitter.CaptureSnapshot();
        var actual = bounded.Blitter.CaptureSnapshot();
        Assert.Equal(expected.Busy, actual.Busy);
        Assert.Equal(expected.Zero, actual.Zero);
        Assert.Equal(expected.CurrentCycle, actual.CurrentCycle);
        Assert.Equal(expected.SourceA, actual.SourceA);
        Assert.Equal(expected.SourceB, actual.SourceB);
        Assert.Equal(expected.SourceC, actual.SourceC);
        Assert.Equal(expected.DestinationD, actual.DestinationD);
        Assert.Equal(expected.LastDmaCycle, actual.LastDmaCycle);
        Assert.Equal(expected.CompletedMicroOps, actual.CompletedMicroOps);
        Assert.Equal(reference.ChipRam, bounded.ChipRam);
        Assert.Equal(
            reference.BusAccesses.Select(ToComparableAccess),
            bounded.BusAccesses.Select(ToComparableAccess));
    }

    [Fact]
    public void BoundedAreaMicroOpsMatchReferenceAcrossLiveBitplaneCollision()
    {
        var reference = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
        var bounded = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
        reference.SetBlitterAdvanceMode(BlitterAdvanceMode.Reference);
        bounded.SetBlitterAdvanceMode(BlitterAdvanceMode.Bounded);
        bounded.Blitter.SetBoundedFixedSlotExecutionEnabledForTest(true);
        bounded.SetHardwareSchedulerHostProfilingEnabled(true);
        var fetchCycle = GetLiveBitplaneFetchCycle();
        StartLiveBitplaneCollisionBlit(reference, fetchCycle);
        StartLiveBitplaneCollisionBlit(bounded, fetchCycle);

        reference.AdvanceDmaTo(fetchCycle + 32);
        bounded.AdvanceDmaTo(fetchCycle + 32);

        var expected = reference.Blitter.CaptureSnapshot();
        var actual = bounded.Blitter.CaptureSnapshot();
        var referenceTrace = string.Join(",", reference.BusAccesses
            .Where(access => access.Request.Requester == AmigaBusRequester.Blitter)
            .Select(access => $"{(access.Request.IsWrite ? 'W' : 'R')}@{access.Request.RequestedCycle}->{access.GrantedCycle}"));
        var boundedTrace = string.Join(",", bounded.BusAccesses
            .Where(access => access.Request.Requester == AmigaBusRequester.Blitter)
            .Select(access => $"{(access.Request.IsWrite ? 'W' : 'R')}@{access.Request.RequestedCycle}->{access.GrantedCycle}"));
        Assert.True(
            expected.Busy == actual.Busy,
            $"ref=busy:{expected.Busy},cycle:{expected.CurrentCycle},word:{expected.WordX},row:{expected.RowY},ops:{expected.CompletedMicroOps}; " +
            $"bounded=busy:{actual.Busy},cycle:{actual.CurrentCycle},word:{actual.WordX},row:{actual.RowY},ops:{actual.CompletedMicroOps}," +
            $"slots:{actual.AdvanceCounters.SlotsExamined},display:{actual.AdvanceCounters.DisplayPreparations},barriers:{actual.AdvanceCounters.Barriers},fallbacks:{actual.AdvanceCounters.Fallbacks}");
        Assert.True(
            expected.Zero == actual.Zero,
            $"ref=zero:{expected.Zero},cycle:{expected.CurrentCycle},src:{expected.SourceA:X8},dst:{expected.DestinationD:X8},last:{expected.LastDmaCycle},ops:{expected.CompletedMicroOps}; " +
            $"bounded=zero:{actual.Zero},cycle:{actual.CurrentCycle},src:{actual.SourceA:X8},dst:{actual.DestinationD:X8},last:{actual.LastDmaCycle},ops:{actual.CompletedMicroOps}," +
            $"slots:{actual.AdvanceCounters.SlotsExamined},display:{actual.AdvanceCounters.DisplayPreparations},barriers:{actual.AdvanceCounters.Barriers},fallbacks:{actual.AdvanceCounters.Fallbacks}; " +
            $"refTrace={referenceTrace}; boundedTrace={boundedTrace}");
        Assert.Equal(expected.CurrentCycle, actual.CurrentCycle);
        Assert.Equal(expected.SourceA, actual.SourceA);
        Assert.Equal(expected.DestinationD, actual.DestinationD);
        Assert.Equal(expected.LastDmaCycle, actual.LastDmaCycle);
        Assert.Equal(expected.CompletedMicroOps, actual.CompletedMicroOps);
        Assert.Equal(reference.ChipRam, bounded.ChipRam);
        Assert.Equal(
            reference.BusAccesses.Select(ToComparableAccess),
            bounded.BusAccesses.Select(ToComparableAccess));
    }

    [Fact]
    public void BoundedFixedSlotDiagnosticsStayColdWhenProfilingIsDisabled()
    {
        var bus = new AmigaBus(captureBusAccesses: false, enableLiveAgnusDma: true);
        bus.SetBlitterAdvanceMode(BlitterAdvanceMode.Bounded);
        bus.Blitter.SetBoundedFixedSlotExecutionEnabledForTest(true);
        var fetchCycle = GetLiveBitplaneFetchCycle();
        StartLiveBitplaneCollisionBlit(bus, fetchCycle, (ushort)((4 << 6) | 8));

        bus.AdvanceDmaTo(fetchCycle + AmigaConstants.A500PalCpuCyclesPerRasterLine);

        var scheduler = bus.CaptureHardwareSchedulerSnapshot();
        Assert.Equal(0, scheduler.DeferredCpuWaitFixedImageBuilds);
        Assert.Equal(0, scheduler.DeferredCpuWaitFixedImageHits);
        Assert.Equal(0, scheduler.DeferredCpuWaitFixedImageMisses);
        Assert.Equal(0, scheduler.DeferredCpuWaitFixedImageInvalidations);
        Assert.Equal(0, scheduler.DeferredCpuWaitFixedImagePredictedSlots);
    }

    [Fact]
    public void BoundedFixedSlotScopeMatchesReferenceAtEveryLiveDisplaySlot()
    {
        var reference = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
        var bounded = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
        reference.SetBlitterAdvanceMode(BlitterAdvanceMode.Reference);
        bounded.SetBlitterAdvanceMode(BlitterAdvanceMode.Bounded);
        bounded.Blitter.SetBoundedFixedSlotExecutionEnabledForTest(true);
        var fetchCycle = GetLiveBitplaneFetchCycle();
        StartLiveBitplaneCollisionBlit(reference, fetchCycle, (ushort)((4 << 6) | 8));
        StartLiveBitplaneCollisionBlit(bounded, fetchCycle, (ushort)((4 << 6) | 8));

        for (var targetCycle = fetchCycle - 4;
            targetCycle <= fetchCycle + AmigaConstants.A500PalCpuCyclesPerRasterLine;
            targetCycle += AgnusChipSlotScheduler.SlotCycles)
        {
            reference.AdvanceDmaTo(targetCycle);
            bounded.AdvanceDmaTo(targetCycle);

            var expected = reference.Blitter.CaptureSnapshot();
            var actual = bounded.Blitter.CaptureSnapshot();
            var stateMatches = expected.Busy == actual.Busy &&
                expected.Zero == actual.Zero &&
                expected.CurrentCycle == actual.CurrentCycle &&
                expected.SourceA == actual.SourceA &&
                expected.DestinationD == actual.DestinationD &&
                expected.WordX == actual.WordX &&
                expected.RowY == actual.RowY &&
                expected.CompletedMicroOps == actual.CompletedMicroOps;
            var traceMatches = reference.BusAccesses.Select(ToComparableAccess)
                .SequenceEqual(bounded.BusAccesses.Select(ToComparableAccess));
            if (!stateMatches || !traceMatches || !reference.ChipRam.SequenceEqual(bounded.ChipRam))
            {
                var referenceTrace = string.Join(",", reference.BusAccesses
                    .Where(access => access.Request.Requester == AmigaBusRequester.Blitter)
                    .Select(access => $"{(access.Request.IsWrite ? 'W' : 'R')}@{access.Request.RequestedCycle}->{access.GrantedCycle}"));
                var boundedTrace = string.Join(",", bounded.BusAccesses
                    .Where(access => access.Request.Requester == AmigaBusRequester.Blitter)
                    .Select(access => $"{(access.Request.IsWrite ? 'W' : 'R')}@{access.Request.RequestedCycle}->{access.GrantedCycle}"));
                Assert.Fail(
                    $"target={targetCycle}; ref={expected.CurrentCycle}/{expected.WordX}/{expected.RowY}/{expected.CompletedMicroOps}; " +
                    $"bounded={actual.CurrentCycle}/{actual.WordX}/{actual.RowY}/{actual.CompletedMicroOps}; " +
                    $"refTrace={referenceTrace}; boundedTrace={boundedTrace}");
            }
        }
    }

    [Fact]
    public void BoundedFixedSlotScopeMatchesReferenceAtEveryLiveSpriteSlot()
    {
        var reference = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
        var bounded = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
        reference.SetBlitterAdvanceMode(BlitterAdvanceMode.Reference);
        bounded.SetBlitterAdvanceMode(BlitterAdvanceMode.Bounded);
        bounded.Blitter.SetBoundedFixedSlotExecutionEnabledForTest(true);
        var row = AmigaConstants.PalLowResOverscanBorderY;
        var lineStart = GetOutputRowStartCycle(row);
        StartLiveSpriteCollisionBlit(reference, lineStart, (ushort)((4 << 6) | 8));
        StartLiveSpriteCollisionBlit(bounded, lineStart, (ushort)((4 << 6) | 8));

        for (var targetCycle = lineStart - 4;
            targetCycle <= lineStart + AmigaConstants.A500PalCpuCyclesPerRasterLine;
            targetCycle += AgnusChipSlotScheduler.SlotCycles)
        {
            reference.AdvanceDmaTo(targetCycle);
            bounded.AdvanceDmaTo(targetCycle);

            var expected = reference.Blitter.CaptureSnapshot();
            var actual = bounded.Blitter.CaptureSnapshot();
            Assert.True(
                expected.Busy == actual.Busy &&
                expected.Zero == actual.Zero &&
                expected.CurrentCycle == actual.CurrentCycle &&
                expected.SourceA == actual.SourceA &&
                expected.DestinationD == actual.DestinationD &&
                expected.WordX == actual.WordX &&
                expected.RowY == actual.RowY &&
                expected.CompletedMicroOps == actual.CompletedMicroOps &&
                reference.ChipRam.SequenceEqual(bounded.ChipRam) &&
                reference.BusAccesses.Select(ToComparableAccess)
                    .SequenceEqual(bounded.BusAccesses.Select(ToComparableAccess)),
                $"target={targetCycle}; ref={expected.CurrentCycle}/{expected.WordX}/{expected.RowY}/{expected.CompletedMicroOps}; " +
                $"bounded={actual.CurrentCycle}/{actual.WordX}/{actual.RowY}/{actual.CompletedMicroOps}");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BoundedFixedSlotScopeMatchesReferenceWithInterleavedCpuReads(bool nasty)
    {
        var reference = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
        var bounded = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
        reference.SetBlitterAdvanceMode(BlitterAdvanceMode.Reference);
        bounded.SetBlitterAdvanceMode(BlitterAdvanceMode.Bounded);
        bounded.Blitter.SetBoundedFixedSlotExecutionEnabledForTest(true);
        var fetchCycle = GetLiveBitplaneFetchCycle();
        StartLiveBitplaneCollisionBlit(reference, fetchCycle, (ushort)((16 << 6) | 16), nasty);
        StartLiveBitplaneCollisionBlit(bounded, fetchCycle, (ushort)((16 << 6) | 16), nasty);
        var referenceCycle = fetchCycle - 4;
        var boundedCycle = fetchCycle - 4;

        for (var access = 0; access < 64; access++)
        {
            var expectedValue = reference.ReadWord(
                0x00000100,
                ref referenceCycle,
                AmigaBusAccessKind.CpuDataRead);
            var actualValue = bounded.ReadWord(
                0x00000100,
                ref boundedCycle,
                AmigaBusAccessKind.CpuDataRead);
            var expected = reference.Blitter.CaptureSnapshot();
            var actual = bounded.Blitter.CaptureSnapshot();
            Assert.True(
                expectedValue == actualValue &&
                referenceCycle == boundedCycle &&
                expected.Busy == actual.Busy &&
                expected.Zero == actual.Zero &&
                expected.CurrentCycle == actual.CurrentCycle &&
                expected.SourceA == actual.SourceA &&
                expected.DestinationD == actual.DestinationD &&
                expected.WordX == actual.WordX &&
                expected.RowY == actual.RowY &&
                expected.CompletedMicroOps == actual.CompletedMicroOps &&
                reference.BusAccesses.Select(ToComparableAccess)
                    .SequenceEqual(bounded.BusAccesses.Select(ToComparableAccess)),
                $"access={access},cpu={referenceCycle}/{boundedCycle}," +
                $"ref={expected.CurrentCycle}/{expected.WordX}/{expected.RowY}/{expected.CompletedMicroOps}," +
                $"bounded={actual.CurrentCycle}/{actual.WordX}/{actual.RowY}/{actual.CompletedMicroOps}");

            referenceCycle += 2;
            boundedCycle += 2;
        }

        Assert.Equal(reference.ChipRam, bounded.ChipRam);
    }

    [Fact]
    public void BoundedAreaMicroOpsMatchReferenceWithInterleavedPaulaDma()
    {
        var reference = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: false);
        var bounded = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: false);
        reference.SetBlitterAdvanceMode(BlitterAdvanceMode.Reference);
        bounded.SetBlitterAdvanceMode(BlitterAdvanceMode.Bounded);
        StartFastPaulaDma(reference);
        StartFastPaulaDma(bounded);
        StartAreaBlit(reference, channels: 0x0F);
        StartAreaBlit(bounded, channels: 0x0F);

        reference.AdvanceDmaTo(reference.Blitter.CurrentCycle + 10_000);
        bounded.AdvanceDmaTo(bounded.Blitter.CurrentCycle + 10_000);

        var expected = reference.Blitter.CaptureSnapshot();
        var actual = bounded.Blitter.CaptureSnapshot();
        Assert.Equal(expected.Busy, actual.Busy);
        Assert.Equal(expected.Zero, actual.Zero);
        Assert.Equal(expected.CurrentCycle, actual.CurrentCycle);
        Assert.Equal(expected.SourceA, actual.SourceA);
        Assert.Equal(expected.SourceB, actual.SourceB);
        Assert.Equal(expected.SourceC, actual.SourceC);
        Assert.Equal(expected.DestinationD, actual.DestinationD);
        Assert.Equal(expected.WordX, actual.WordX);
        Assert.Equal(expected.RowY, actual.RowY);
        Assert.Equal(expected.LastDmaCycle, actual.LastDmaCycle);
        Assert.Equal(expected.CompletedMicroOps, actual.CompletedMicroOps);
        Assert.Equal(reference.Paula.GetChannelSnapshot(0), bounded.Paula.GetChannelSnapshot(0));
        Assert.Equal(reference.ChipRam, bounded.ChipRam);
        Assert.Equal(
            reference.BusAccesses.Select(ToComparableAccess),
            bounded.BusAccesses.Select(ToComparableAccess));
    }

    [Fact]
    public void BoundedAreaMicroOpsRemainExactWithLiveDisplayAndPaulaDma()
    {
        var reference = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
        var bounded = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
        reference.SetBlitterAdvanceMode(BlitterAdvanceMode.Reference);
        bounded.SetBlitterAdvanceMode(BlitterAdvanceMode.Bounded);
        bounded.Blitter.SetBoundedFixedSlotExecutionEnabledForTest(true);
        bounded.SetHardwareSchedulerHostProfilingEnabled(true);
        StartFastPaulaDma(reference);
        StartFastPaulaDma(bounded);
        var fetchCycle = GetLiveBitplaneFetchCycle();
        StartLiveBitplaneCollisionBlit(reference, fetchCycle, (ushort)((4 << 6) | 8));
        StartLiveBitplaneCollisionBlit(bounded, fetchCycle, (ushort)((4 << 6) | 8));

        reference.AdvanceDmaTo(fetchCycle + AmigaConstants.A500PalCpuCyclesPerRasterLine);
        bounded.AdvanceDmaTo(fetchCycle + AmigaConstants.A500PalCpuCyclesPerRasterLine);

        var expected = reference.Blitter.CaptureSnapshot();
        var actual = bounded.Blitter.CaptureSnapshot();
        Assert.Equal(expected.Busy, actual.Busy);
        Assert.Equal(expected.Zero, actual.Zero);
        Assert.Equal(expected.CurrentCycle, actual.CurrentCycle);
        Assert.Equal(expected.SourceA, actual.SourceA);
        Assert.Equal(expected.DestinationD, actual.DestinationD);
        Assert.Equal(expected.WordX, actual.WordX);
        Assert.Equal(expected.RowY, actual.RowY);
        Assert.Equal(expected.LastDmaCycle, actual.LastDmaCycle);
        Assert.Equal(expected.CompletedMicroOps, actual.CompletedMicroOps);
        Assert.Equal(reference.Paula.GetChannelSnapshot(0), bounded.Paula.GetChannelSnapshot(0));
        Assert.Equal(reference.ChipRam, bounded.ChipRam);
        Assert.Equal(
            reference.BusAccesses.Select(ToComparableAccess),
            bounded.BusAccesses.Select(ToComparableAccess));
    }

    private static void StartAreaCopy(AmigaBus bus)
    {
        for (var offset = 0; offset < 128; offset += 2)
        {
            BigEndian.WriteUInt16(bus.ChipRam, 0x3000 + offset, (ushort)(0x4100 + offset));
        }

        bus.WriteWord(0x00DFF040, 0x09F0);
        bus.WriteWord(0x00DFF042, 0x0000);
        bus.WriteWord(0x00DFF050, 0x0000);
        bus.WriteWord(0x00DFF052, 0x3000);
        bus.WriteWord(0x00DFF054, 0x0000);
        bus.WriteWord(0x00DFF056, 0x4000);
        bus.WriteWord(0x00DFF096, 0x8240);
        bus.WriteWord(0x00DFF058, (ushort)((8 << 6) | 8));
    }

    private static void StartFastPaulaDma(AmigaBus bus)
    {
        for (var offset = 0; offset < 64; offset += 2)
        {
            BigEndian.WriteUInt16(bus.ChipRam, 0x7000 + offset, (ushort)(0x4000 + offset));
        }

        bus.WriteWord(0x00DFF0A0, 0x0000, 0);
        bus.WriteWord(0x00DFF0A2, 0x7000, 0);
        bus.WriteWord(0x00DFF0A4, 0x0020, 0);
        bus.WriteWord(0x00DFF0A6, 0x0002, 0);
        bus.WriteWord(0x00DFF096, 0x8201, 0);
        bus.Paula.AdvanceTo(0);
    }

    private static void StartAreaBlit(AmigaBus bus, int channels)
    {
        for (var offset = 0; offset < 256; offset += 2)
        {
            BigEndian.WriteUInt16(bus.ChipRam, 0x2000 + offset, (ushort)(0x1200 + (offset * 3)));
            BigEndian.WriteUInt16(bus.ChipRam, 0x3000 + offset, (ushort)(0x3400 ^ (offset * 5)));
            BigEndian.WriteUInt16(bus.ChipRam, 0x4000 + offset, (ushort)(0x5600 + (offset * 7)));
            BigEndian.WriteUInt16(bus.ChipRam, 0x5000 + offset, 0xA55A);
        }

        var bltcon0 = (ushort)((3 << 12) | (channels << 8) | 0xCA);
        var bltcon1 = (ushort)(5 << 12);
        bus.WriteWord(0x00DFF096, 0x8240);
        var cycle = Math.Max(bus.Blitter.CurrentCycle, bus.CausalBusExecutor.ExecutedThroughCycle);
        bus.ClearPendingCpuSlotRequest();
        bus.Paula.AdvanceDmaObservableTo(cycle);
        bus.Disk.AdvanceEventsTo(cycle);
        bus.Blitter.WriteRegister(0x040, bltcon0, cycle);
        bus.Blitter.WriteRegister(0x042, bltcon1, cycle);
        bus.Blitter.WriteRegister(0x044, 0x7FFE, cycle);
        bus.Blitter.WriteRegister(0x046, 0xFFFC, cycle);
        bus.Blitter.WriteRegister(0x050, 0x0000, cycle);
        bus.Blitter.WriteRegister(0x052, 0x2000, cycle);
        bus.Blitter.WriteRegister(0x04C, 0x0000, cycle);
        bus.Blitter.WriteRegister(0x04E, 0x3000, cycle);
        bus.Blitter.WriteRegister(0x048, 0x0000, cycle);
        bus.Blitter.WriteRegister(0x04A, 0x4000, cycle);
        bus.Blitter.WriteRegister(0x054, 0x0000, cycle);
        bus.Blitter.WriteRegister(0x056, 0x5000, cycle);
        bus.Blitter.WriteRegister(0x064, 0x0002, cycle);
        bus.Blitter.WriteRegister(0x062, 0x0004, cycle);
        bus.Blitter.WriteRegister(0x060, 0x0006, cycle);
        bus.Blitter.WriteRegister(0x066, 0x0008, cycle);
        bus.Blitter.WriteRegister(0x058, (ushort)((3 << 6) | 5), cycle);
    }

    private static void StartLiveBitplaneCollisionBlit(
        AmigaBus bus,
        long fetchCycle,
        ushort blitSize = 0x0041,
        bool nasty = false)
    {
        for (var offset = 0; offset < 0x1000; offset += 2)
        {
            BigEndian.WriteUInt16(bus.ChipRam, 0x3000 + offset, (ushort)(0xCAFE ^ offset));
        }
        BigEndian.WriteUInt16(bus.ChipRam, 0x4000, 0x1234);
        bus.WriteWord(0x00DFF040, 0x09F0);
        bus.WriteWord(0x00DFF042, 0x0000);
        bus.WriteWord(0x00DFF050, 0x0000);
        bus.WriteWord(0x00DFF052, 0x3000);
        bus.WriteWord(0x00DFF054, 0x0000);
        bus.WriteWord(0x00DFF056, 0x4000);
        bus.WriteWord(0x00DFF092, 0x0038);
        bus.WriteWord(0x00DFF094, 0x0038);
        bus.WriteWord(0x00DFF0E0, 0x0000);
        bus.WriteWord(0x00DFF0E2, 0x2000);
        bus.WriteWord(0x00DFF100, 0x1000);
        bus.WriteWord(0x00DFF096, (ushort)(nasty ? 0x8740 : 0x8340));
        bus.EnableLiveAgnusDma();
        bus.Blitter.WriteRegister(0x058, blitSize, fetchCycle - 4);
    }

    private static void StartLiveSpriteCollisionBlit(
        AmigaBus bus,
        long lineStart,
        ushort blitSize)
    {
        const uint spriteList = 0x7000;
        var row = AmigaConstants.PalLowResOverscanBorderY;
        var (pos, ctl) = EncodeSpritePosition(
            AmigaConstants.PalLowResOverscanBorderX,
            row,
            height: 1);
        BigEndian.WriteUInt16(bus.ChipRam, (int)spriteList, pos);
        BigEndian.WriteUInt16(bus.ChipRam, (int)spriteList + 2, ctl);
        BigEndian.WriteUInt16(bus.ChipRam, (int)spriteList + 4, 0x8000);
        BigEndian.WriteUInt16(bus.ChipRam, (int)spriteList + 6, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, (int)spriteList + 8, 0x0000);
        BigEndian.WriteUInt16(bus.ChipRam, (int)spriteList + 10, 0x0000);
        bus.WriteWord(0x00DFF120, (ushort)(spriteList >> 16));
        bus.WriteWord(0x00DFF122, (ushort)spriteList);

        for (var offset = 0; offset < 0x200; offset += 2)
        {
            BigEndian.WriteUInt16(bus.ChipRam, 0x3000 + offset, (ushort)(0x4000 + offset));
        }

        bus.WriteWord(0x00DFF040, 0x09F0);
        bus.WriteWord(0x00DFF042, 0x0000);
        bus.WriteWord(0x00DFF050, 0x0000);
        bus.WriteWord(0x00DFF052, 0x3000);
        bus.WriteWord(0x00DFF054, 0x0000);
        bus.WriteWord(0x00DFF056, 0x4000);
        bus.WriteWord(0x00DFF096, 0x8260);
        bus.EnableLiveAgnusDma();
        bus.Blitter.WriteRegister(0x058, blitSize, lineStart - 4);
    }

    private static (ushort Pos, ushort Ctl) EncodeSpritePosition(int x, int y, int height)
    {
        var hStart = x + 129 - AmigaConstants.PalLowResOverscanBorderX;
        var vStart = y + (0x2C - AmigaConstants.PalLowResOverscanBorderY);
        var vStop = vStart + height;
        var pos = (ushort)(((vStart & 0xFF) << 8) | ((hStart >> 1) & 0xFF));
        var ctl = (ushort)(((vStop & 0xFF) << 8) |
            (hStart & 0x0001) |
            ((vStop & 0x100) != 0 ? 0x0002 : 0) |
            ((vStart & 0x100) != 0 ? 0x0004 : 0));
        return (pos, ctl);
    }

    private static long GetOutputRowStartCycle(int row)
    {
        var line = (0x2C - AmigaConstants.PalLowResOverscanBorderY) + row;
        return (long)line * AmigaConstants.A500PalCpuCyclesPerRasterLine;
    }

    private static void StartDescendingAreaBlit(AmigaBus bus, ushort bltcon1)
    {
        for (var offset = 0; offset < 128; offset += 2)
        {
            BigEndian.WriteUInt16(bus.ChipRam, 0x3000 + offset, (ushort)(0x1111 ^ (offset * 31)));
            BigEndian.WriteUInt16(bus.ChipRam, 0x4000 + offset, (ushort)(0x2222 + (offset * 17)));
            BigEndian.WriteUInt16(bus.ChipRam, 0x5000 + offset, (ushort)(0x3333 ^ (offset * 7)));
            BigEndian.WriteUInt16(bus.ChipRam, 0x6000 + offset, 0x55AA);
        }

        bus.WriteWord(0x00DFF040, 0x0FCA);
        bus.WriteWord(0x00DFF042, bltcon1);
        bus.WriteWord(0x00DFF044, 0x7FFE);
        bus.WriteWord(0x00DFF046, 0xFFFC);
        bus.WriteWord(0x00DFF050, 0x0000);
        bus.WriteWord(0x00DFF052, 0x307E);
        bus.WriteWord(0x00DFF04C, 0x0000);
        bus.WriteWord(0x00DFF04E, 0x407E);
        bus.WriteWord(0x00DFF048, 0x0000);
        bus.WriteWord(0x00DFF04A, 0x507E);
        bus.WriteWord(0x00DFF054, 0x0000);
        bus.WriteWord(0x00DFF056, 0x607E);
        bus.WriteWord(0x00DFF064, 0x0002);
        bus.WriteWord(0x00DFF062, 0x0004);
        bus.WriteWord(0x00DFF060, 0x0006);
        bus.WriteWord(0x00DFF066, 0x0008);
        bus.WriteWord(0x00DFF096, 0x8240);
        bus.WriteWord(0x00DFF058, (ushort)((3 << 6) | 5));
    }

    private static void StartLineBlit(AmigaBus bus, ushort bltcon1)
    {
        for (var offset = 0; offset < 0x400; offset += 2)
        {
            BigEndian.WriteUInt16(bus.ChipRam, 0x7000 + offset, (ushort)(0x0101 * (offset & 0x0F)));
            BigEndian.WriteUInt16(bus.ChipRam, 0x7800 + offset, (ushort)(0x8000 >> ((offset / 2) & 0x0F)));
        }

        bus.WriteWord(0x00DFF040, 0x0FCA);
        bus.WriteWord(0x00DFF042, bltcon1);
        bus.WriteWord(0x00DFF048, 0x0000);
        bus.WriteWord(0x00DFF04A, 0x7000);
        bus.WriteWord(0x00DFF04C, 0x0000);
        bus.WriteWord(0x00DFF04E, 0x7800);
        bus.WriteWord(0x00DFF050, 0x0000);
        bus.WriteWord(0x00DFF052, 0x0000);
        bus.WriteWord(0x00DFF054, 0x0000);
        bus.WriteWord(0x00DFF056, 0x7000);
        bus.WriteWord(0x00DFF060, 0x0020);
        bus.WriteWord(0x00DFF062, 0x0002);
        bus.WriteWord(0x00DFF064, 0xFFFC);
        bus.WriteWord(0x00DFF066, 0x0020);
        bus.WriteWord(0x00DFF072, 0xA55A);
        bus.WriteWord(0x00DFF074, 0x8000);
        bus.WriteWord(0x00DFF096, 0x8240);
        bus.WriteWord(0x00DFF058, (ushort)((16 << 6) | 2));
    }

    private static long GetLiveBitplaneFetchCycle()
    {
        const int lowResolutionPlaneZeroSlot = 7;
        var row = AmigaConstants.PalLowResOverscanBorderY;
        var line = (0x2C - AmigaConstants.PalLowResOverscanBorderY) + row;
        var lineStart = (long)line * AmigaConstants.A500PalCpuCyclesPerRasterLine;
        return lineStart + ((0x38 + lowResolutionPlaneZeroSlot) * AgnusChipSlotScheduler.SlotCycles);
    }

    private static object ToComparableAccess(AmigaBusAccessResult access)
        => new
        {
            access.Request.Requester,
            access.Request.Kind,
            access.Request.Target,
            access.Request.Address,
            access.Request.Size,
            access.Request.RequestedCycle,
            access.GrantedCycle,
            access.CompletedCycle,
            access.Request.IsWrite
        };
}
