using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class PaulaTests
{
	[Fact]
	public void ManualAudioDataOutputsHighThenLowByteAndRequestsInterrupt()
	{
		var bus = CreatePaulaComponentBus();
		bus.WriteWord(0x00DFF09A, 0xC080, 0);
		bus.WriteWord(0x00DFF0AA, 0x7F81, 0);
		var buffer = new float[4];

		bus.Paula.RenderSample(0, buffer, 0, 2);
		bus.Paula.RenderSample(856, buffer, 1, 2);

		Assert.True(buffer[0] > 0.20f);
		Assert.True(buffer[2] < -0.20f);
		var interruptEvent = Assert.Single(bus.Paula.DrainInterrupts());
		Assert.Equal(0, interruptEvent.Channel);
		Assert.Equal(0x0080, interruptEvent.IntreqBit);
		Assert.True((bus.ReadWord(0x00DFF01E) & 0x0080) != 0);
	}

	[Fact]
	public void AudioOnlyRenderLeavesInterruptsForRegisterTimeline()
	{
		var bus = CreatePaulaComponentBus();
		bus.WriteWord(0x00DFF09A, 0xC080, 0);
		bus.WriteWord(0x00DFF0AA, 0x7F81, 0);
		var buffer = new float[4];

		bus.Paula.RenderSample(0, buffer, 0, 2, advanceRegisterObservable: false);
		bus.Paula.RenderSample(856, buffer, 1, 2, advanceRegisterObservable: false);
		Assert.Empty(bus.Paula.DrainInterrupts());

		bus.Paula.AdvanceRegisterObservableTo(856);
		var interruptEvent = Assert.Single(bus.Paula.DrainInterrupts());

		Assert.True(buffer[0] > 0.20f);
		Assert.True(buffer[2] < -0.20f);
		Assert.Equal(0, interruptEvent.Channel);
		Assert.Equal(0x0080, interruptEvent.IntreqBit);
		Assert.True((bus.Paula.Intreq & 0x0080) != 0);
	}

	[Fact]
	public void ManualAudioDataTransitionsOnExactIntegerPeriodCycles()
	{
		var bus = CreatePaulaComponentBus();
		SchedulePaulaWrite(bus, 0x0A6, 0x0003, 0);
		SchedulePaulaWrite(bus, 0x0AA, 0x7F81, 0);
		var buffer = new float[4];

		bus.Paula.RenderSample(5, buffer, 0, 2);
		var beforeBoundary = bus.Paula.GetChannelSnapshot(0);
		bus.Paula.RenderSample(6, buffer, 1, 2);
		var atBoundary = bus.Paula.GetChannelSnapshot(0);

		Assert.True(buffer[0] > 0.20f);
		Assert.True(buffer[2] < -0.20f);
		Assert.True(beforeBoundary.NextByteIsLow);
		Assert.Equal(6, beforeBoundary.NextSampleCycle);
		Assert.False(atBoundary.NextByteIsLow);
		Assert.Equal(12, atBoundary.NextSampleCycle);
	}

	[Fact]
	public void CustomRegisterReadDoesNotAdvanceMainPaulaAudioTimeline()
	{
		var bus = CreatePaulaComponentBus();
		SchedulePaulaWrite(bus, 0x0A6, 0x0003, 0);
		SchedulePaulaWrite(bus, 0x0A8, 0x0040, 0);
		SchedulePaulaWrite(bus, 0x0AA, 0x7F81, 0);
		var beforeRead = bus.Paula.GetChannelSnapshot(0);

		var cycle = 20L;
		var intreq = bus.ReadWord(0x00DFF01E, ref cycle, AmigaBusAccessKind.CpuDataRead);
		var afterRead = bus.Paula.GetChannelSnapshot(0);
		var buffer = new float[4];
		bus.Paula.RenderSample(5, buffer, 0, 2);
		bus.Paula.RenderSample(6, buffer, 1, 2);

		Assert.True((intreq & 0x0080) != 0);
		Assert.Equal(beforeRead.CurrentSample, afterRead.CurrentSample);
		Assert.Equal(beforeRead.HasDataWord, afterRead.HasDataWord);
		Assert.Equal(beforeRead.NextSampleCycle, afterRead.NextSampleCycle);
		Assert.True(buffer[0] > 0.20f);
		Assert.True(buffer[2] < -0.20f);
	}

	[Fact]
	public void RegisterWakeCandidateIgnoresManualAudioSampleBoundary()
	{
		var bus = CreatePaulaComponentBus();
		SchedulePaulaWrite(bus, 0x0A6, 0x0003, 0);
		SchedulePaulaWrite(bus, 0x0AA, 0x7F81, 0);
		bus.Paula.AdvanceRegisterObservableTo(0);
		var buffer = new float[4];

		var candidate = bus.Paula.GetNextWakeCandidateCycle(0, 6);
		bus.Paula.RenderSample(5, buffer, 0, 2);
		bus.Paula.RenderSample(6, buffer, 1, 2);

		Assert.Null(candidate);
		Assert.True(buffer[0] > 0.20f);
		Assert.True(buffer[2] < -0.20f);
	}

	[Fact]
	public void RegisterWakeCandidateSkipsDmaLowByteSampleBoundary()
	{
		var bus = CreatePaulaComponentBus();
		bus.ChipRam[0x1000] = 0x7F;
		bus.ChipRam[0x1001] = 0x81;
		bus.ChipRam[0x1002] = 0x40;
		bus.ChipRam[0x1003] = 0xC0;
		SchedulePaulaWrite(bus, 0x0A2, 0x1000, 0);
		SchedulePaulaWrite(bus, 0x0A4, 0x0002, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x000A, 0);
		SchedulePaulaWrite(bus, 0x096, 0x8201, 0);

		bus.Paula.AdvanceRegisterObservableTo(42);
		var lowByteCandidate = bus.Paula.GetNextWakeCandidateCycle(42, 58);
		var nextWordCandidate = bus.Paula.GetNextWakeCandidateCycle(42, 78);

		Assert.Null(lowByteCandidate);
		Assert.Equal(78, nextWordCandidate);
	}

	[Fact]
	public void RegisterWakeCandidateKeepsAttachedSourceSampleBoundary()
	{
		var bus = CreatePaulaComponentBus();
		SchedulePaulaWrite(bus, 0x09E, 0x8010, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x0002, 0);
		SchedulePaulaWrite(bus, 0x0AA, 0x0005, 0);
		bus.Paula.AdvanceRegisterObservableTo(0);

		var candidate = bus.Paula.GetNextWakeCandidateCycle(0, 4);

		Assert.Equal(4, candidate);
	}

	[Fact]
	public void RegisterDmaWordOutputKeepsLowByteBoundaryWhenAttachWriteIsPending()
	{
		var bus = CreatePaulaComponentBus();
		bus.ChipRam[0x1000] = 0x00;
		bus.ChipRam[0x1001] = 0x05;
		SchedulePaulaWrite(bus, 0x0A2, 0x1000, 0);
		SchedulePaulaWrite(bus, 0x0A4, 0x0001, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x000A, 0);
		SchedulePaulaWrite(bus, 0x096, 0x8201, 0);
		SchedulePaulaWrite(bus, 0x09E, 0x8010, 50);

		bus.Paula.AdvanceRegisterObservableTo(42);
		var attachCandidate = bus.Paula.GetNextWakeCandidateCycle(42, 58);
		bus.Paula.AdvanceRegisterObservableTo(50);
		var lowByteCandidate = bus.Paula.GetNextWakeCandidateCycle(50, 58);

		Assert.Equal(50, attachCandidate);
		Assert.Equal(58, lowByteCandidate);
	}

	[Fact]
	public void SerialDataReadReportsIdleTransmitEmptyWithoutReceiveBufferData()
	{
		var bus = CreatePaulaComponentBus();

		var serdatr = bus.ReadWord(0x00DFF018);
		Assert.Equal(0x3000, serdatr);
		Assert.NotEqual(0x007F, serdatr & 0x007F);
	}

	[Fact]
	public void LateDmaconWriteStillUpdatesRegisterTimelineState()
	{
		var bus = CreatePaulaComponentBus();

		SchedulePaulaWrite(bus, 0x096, 0x8200, 10);
		bus.Paula.AdvanceTo(100);
		SchedulePaulaWrite(bus, 0x096, 0x8040, 20);
		bus.Paula.AdvanceTo(100);

		Assert.Equal(0x0240, bus.Paula.Dmacon & 0x0240);
	}

	[Fact]
	public void CustomRegisterReadDmaFetchIsReusedWhenAudioTimelineCatchesUp()
	{
		var bus = CreatePaulaComponentBus();
		bus.ChipRam[0x1000] = 0x7F;
		bus.ChipRam[0x1001] = 0x81;
		SchedulePaulaWrite(bus, 0x0A2, 0x1000, 0);
		SchedulePaulaWrite(bus, 0x0A4, 0x0001, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x0064, 0);
		SchedulePaulaWrite(bus, 0x096, 0x8201, 0);

		var cycle = 40L;
		_ = bus.ReadWord(0x00DFF01E, ref cycle, AmigaBusAccessKind.CpuDataRead);
		var dmaReadsAfterPoll = CountPaulaDmaReads(bus);
		var buffer = new float[2];
		bus.Paula.RenderSample(38, buffer, 0, 2);

		Assert.Equal(3, dmaReadsAfterPoll);
		Assert.Equal(dmaReadsAfterPoll, CountPaulaDmaReads(bus));
		Assert.True(buffer[0] > 0.20f);
	}

	[Fact]
	public void RegisterTimelineDmaLatchIsConsumedByAudioTimelineWithoutSecondGrant()
	{
		var bus = CreatePaulaComponentBus();
		bus.ChipRam[0x1000] = 0x7F;
		bus.ChipRam[0x1001] = 0x81;
		SchedulePaulaWrite(bus, 0x0A2, 0x1000, 0);
		SchedulePaulaWrite(bus, 0x0A4, 0x0001, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x0064, 0);
		SchedulePaulaWrite(bus, 0x096, 0x8201, 0);

		var pollCycle = 40L;
		_ = bus.ReadWord(0x00DFF01E, ref pollCycle, AmigaBusAccessKind.CpuDataRead);
		var grantsAfterPoll = CapturePaulaDmaGrants(bus);
		var buffer = new float[2];

		bus.Paula.RenderSample(38, buffer, 0, 2);
		var grantsAfterAudio = CapturePaulaDmaGrants(bus);

		Assert.Equal(grantsAfterPoll, grantsAfterAudio);
		Assert.Equal(grantsAfterPoll.Length, grantsAfterPoll.Distinct().Count());
		Assert.True(buffer[0] > 0.20f);
	}

	[Fact]
	public void DmaObservableAdvanceFetchesWithoutAdvancingAudioTimeline()
	{
		var bus = CreatePaulaComponentBus();
		bus.ChipRam[0x1000] = 0x7F;
		bus.ChipRam[0x1001] = 0x81;
		SchedulePaulaWrite(bus, 0x0A2, 0x1000, 0);
		SchedulePaulaWrite(bus, 0x0A4, 0x0001, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x0001, 0);
		SchedulePaulaWrite(bus, 0x096, 0x8201, 0);
		var before = bus.Paula.GetChannelSnapshot(0);

		bus.Paula.AdvanceDmaObservableTo(42);
		var after = bus.Paula.GetChannelSnapshot(0);
		var grantsAfterDma = CapturePaulaDmaGrants(bus);

		Assert.Equal(before.CurrentSample, after.CurrentSample);
		Assert.Equal(before.HasDataWord, after.HasDataWord);
		Assert.Equal(before.NextSampleCycle, after.NextSampleCycle);
		Assert.Equal(new long[] { 0, 34, 38, 42 }, grantsAfterDma.Select(grant => grant.RequestedCycle));
		Assert.NotEqual(0, bus.Paula.Intreq & 0x0080);
	}

	[Fact]
	public void AudioTimelineReusesDmaObservableLatchesWithoutDuplicateGrant()
	{
		var bus = CreatePaulaComponentBus();
		bus.ChipRam[0x1000] = 0x7F;
		bus.ChipRam[0x1001] = 0x81;
		SchedulePaulaWrite(bus, 0x0A2, 0x1000, 0);
		SchedulePaulaWrite(bus, 0x0A4, 0x0001, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x0001, 0);
		SchedulePaulaWrite(bus, 0x096, 0x8201, 0);
		bus.Paula.AdvanceDmaObservableTo(42);
		var grantsAfterDma = CapturePaulaDmaGrants(bus);
		var buffer = new float[2];

		bus.Paula.RenderSample(38, buffer, 0, 2, advanceRegisterObservable: false);
		var grantsAfterAudio = CapturePaulaDmaGrants(bus);

		Assert.Equal(grantsAfterDma, grantsAfterAudio);
		Assert.True(buffer[0] > 0.20f);
	}

	[Fact]
	public void LiveAudioDmaSlotWithoutPendingRequestDoesNotReadChipRam()
	{
		var bus = CreateLivePaulaSlotBus();

		bus.Paula.AdvanceDmaObservableTo(AudioSlotCycle(0, 0));

		Assert.Empty(CapturePaulaDmaAccesses(bus));
	}

	[Fact]
	public void LiveAudioDmaPendingRequestIsServedAtExactChannelSlot()
	{
		var bus = CreateLivePaulaSlotBus();
		bus.ChipRam[0x1000] = 0x7F;
		bus.ChipRam[0x1001] = 0x81;
		SchedulePaulaWrite(bus, 0x0A2, 0x1000, 0);
		SchedulePaulaWrite(bus, 0x0A4, 0x0001, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x0001, 0);
		SchedulePaulaWrite(bus, 0x096, 0x8201, 0);
		var slot = AudioSlotCycle(0, 0);

		bus.Paula.AdvanceDmaObservableTo(slot);

		var dma = Assert.Single(CapturePaulaDmaAccesses(bus));
		Assert.Equal(slot, dma.RequestedCycle);
		Assert.Equal(slot, dma.GrantedCycle);
		Assert.Equal(slot + AmigaConstants.A500PalCpuCyclesPerColorClock, dma.CompletedCycle);
	}

	[Fact]
	public void LiveAudioDmaRequestCreatedAfterSlotWaitsForNextLineSlot()
	{
		var bus = CreateLivePaulaSlotBus();
		bus.ChipRam[0x1000] = 0x7F;
		bus.ChipRam[0x1001] = 0x81;
		var firstSlot = AudioSlotCycle(0, 0);
		var nextLineSlot = AudioSlotCycle(1, 0);
		SchedulePaulaWrite(bus, 0x0A2, 0x1000, firstSlot + 2);
		SchedulePaulaWrite(bus, 0x0A4, 0x0001, firstSlot + 2);
		SchedulePaulaWrite(bus, 0x0A6, 0x0001, firstSlot + 2);
		SchedulePaulaWrite(bus, 0x096, 0x8201, firstSlot + 2);

		bus.Paula.AdvanceDmaObservableTo(nextLineSlot);

		var dma = Assert.Single(CapturePaulaDmaAccesses(bus));
		Assert.Equal(nextLineSlot, dma.RequestedCycle);
		Assert.Equal(nextLineSlot, dma.GrantedCycle);
	}

	[Fact]
	public void LiveAudioDmaObservableWordIsReusedByAudioTimelineWithoutDuplicateGrant()
	{
		var bus = CreateLivePaulaSlotBus();
		bus.ChipRam[0x1000] = 0x7F;
		bus.ChipRam[0x1001] = 0x81;
		SchedulePaulaWrite(bus, 0x0A2, 0x1000, 0);
		SchedulePaulaWrite(bus, 0x0A4, 0x0001, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x0001, 0);
		SchedulePaulaWrite(bus, 0x096, 0x8201, 0);
		var prefetchCompletion = AudioSlotCycle(1, 0) + AmigaConstants.A500PalCpuCyclesPerColorClock;
		bus.Paula.AdvanceDmaObservableTo(prefetchCompletion);
		var grantsAfterDma = CapturePaulaDmaGrants(bus);
		var buffer = new float[2];

		bus.Paula.RenderSample(prefetchCompletion, buffer, 0, 2, advanceRegisterObservable: false);
		var grantsAfterAudio = CapturePaulaDmaGrants(bus);

		Assert.Equal(grantsAfterDma, grantsAfterAudio);
		Assert.True(buffer[0] > 0.20f);
	}

	[Fact]
	public void SaturatedInterruptSourceAdvanceAppliesWritesWithoutDmaCatchUp()
	{
		var bus = CreatePaulaComponentBus();
		bus.ChipRam[0x1000] = 0x7F;
		bus.ChipRam[0x1001] = 0x81;
		SchedulePaulaWrite(bus, 0x0A2, 0x1000, 0);
		SchedulePaulaWrite(bus, 0x0A4, 0x0001, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x0001, 0);
		SchedulePaulaWrite(bus, 0x096, 0x8201, 0);
		SchedulePaulaWrite(bus, 0x09C, 0x8780, 0);
		var before = bus.Paula.GetChannelSnapshot(0);

		bus.Paula.AdvanceInterruptSourcesTo(42);
		var after = bus.Paula.GetChannelSnapshot(0);

		Assert.Equal(0x0201, bus.Paula.Dmacon & 0x0201);
		Assert.Equal(0x0780, bus.Paula.Intreq & 0x0780);
		Assert.Equal(before.CurrentSample, after.CurrentSample);
		Assert.Equal(before.HasDataWord, after.HasDataWord);
		Assert.Equal(before.NextSampleCycle, after.NextSampleCycle);
		Assert.Empty(CapturePaulaDmaGrants(bus));
		Assert.Equal(1, bus.Paula.InterruptSourceDmaSkippedCount);
		Assert.Equal(0, bus.Paula.InterruptSourceDmaForcedCatchUpCount);
	}

	[Fact]
	public void InterruptSourceAdvanceFallsBackToDmaAfterAudioBitIsCleared()
	{
		var bus = CreatePaulaComponentBus();
		bus.ChipRam[0x1000] = 0x7F;
		bus.ChipRam[0x1001] = 0x81;
		SchedulePaulaWrite(bus, 0x09C, 0x8780, 0);
		SchedulePaulaWrite(bus, 0x09A, 0xC080, 0);
		SchedulePaulaWrite(bus, 0x0A2, 0x1000, 0);
		SchedulePaulaWrite(bus, 0x0A4, 0x0001, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x0001, 0);
		SchedulePaulaWrite(bus, 0x096, 0x8201, 0);
		SchedulePaulaWrite(bus, 0x09C, 0x0080, 1);

		bus.Paula.AdvanceInterruptSourcesTo(42);

		Assert.NotEmpty(CapturePaulaDmaGrants(bus));
		Assert.NotEqual(0, bus.Paula.Intreq & 0x0080);
		Assert.Contains(bus.Paula.DrainInterrupts(), interruptEvent => interruptEvent.Channel == 0);
		Assert.Equal(0, bus.Paula.InterruptSourceDmaSkippedCount);
		Assert.Equal(1, bus.Paula.InterruptSourceDmaForcedCatchUpCount);
	}

	[Fact]
	public void SaturatedInterruptSourceIntenaWriteRefreshesCpuVisibilityWithoutDmaCatchUp()
	{
		var bus = CreatePaulaComponentBus();
		SchedulePaulaWrite(bus, 0x09C, 0x8780, 0);
		SchedulePaulaWrite(bus, 0x09A, 0xC080, 10);

		bus.Paula.AdvanceInterruptSourcesTo(10);

		Assert.Equal(0x4080, bus.Paula.Intena & 0x4080);
		Assert.Equal(0x0080, bus.Paula.ActiveInterruptBits & 0x0080);
		Assert.Empty(CapturePaulaDmaGrants(bus));
		Assert.Equal(1, bus.Paula.InterruptSourceDmaSkippedCount);
		Assert.Equal(0, bus.Paula.InterruptSourceDmaForcedCatchUpCount);
	}

	[Fact]
	public void RegisterDmaFastForwardMatchesSlowPathForStableDma()
	{
		var fast = CreateStableRegisterDmaBus();
		var slow = CreateStableRegisterDmaBus();
		fast.Paula.RegisterDmaFastForwardEnabled = true;
		slow.Paula.RegisterDmaFastForwardEnabled = false;

		fast.Paula.AdvanceRegisterObservableTo(120);
		slow.Paula.AdvanceRegisterObservableTo(120);

		Assert.Equal(slow.Paula.GetChannelSnapshot(0), fast.Paula.GetChannelSnapshot(0));
		Assert.Equal(CapturePaulaDmaGrants(slow), CapturePaulaDmaGrants(fast));
		Assert.Equal(
			slow.Paula.DrainInterrupts().Select(interruptEvent => interruptEvent.Cycle).ToArray(),
			fast.Paula.DrainInterrupts().Select(interruptEvent => interruptEvent.Cycle).ToArray());
		Assert.True(fast.Paula.RegisterDmaFastForwardIterationCount > 0);
	}

	[Fact]
	public void RegisterDmaFastForwardDoesNotBypassAttachedModulationBoundary()
	{
		var fast = CreateAttachedPeriodDmaBus();
		var slow = CreateAttachedPeriodDmaBus();
		fast.Paula.RegisterDmaFastForwardEnabled = true;
		slow.Paula.RegisterDmaFastForwardEnabled = false;

		fast.Paula.AdvanceRegisterObservableTo(100);
		slow.Paula.AdvanceRegisterObservableTo(100);

		Assert.Equal(slow.Paula.GetChannelSnapshot(0), fast.Paula.GetChannelSnapshot(0));
		Assert.Equal(slow.Paula.GetChannelSnapshot(1), fast.Paula.GetChannelSnapshot(1));
		Assert.Equal(CapturePaulaDmaGrants(slow), CapturePaulaDmaGrants(fast));
		Assert.Equal(0, fast.Paula.RegisterDmaFastForwardIterationCount);
	}

	[Fact]
	public void RegisterDmaFastForwardPreservesZeroPeriodDmaTiming()
	{
		var fast = CreateZeroPeriodDmaBus();
		var slow = CreateZeroPeriodDmaBus();
		fast.Paula.RegisterDmaFastForwardEnabled = true;
		slow.Paula.RegisterDmaFastForwardEnabled = false;

		fast.Paula.AdvanceRegisterObservableTo(262_182);
		slow.Paula.AdvanceRegisterObservableTo(262_182);

		Assert.Equal(slow.Paula.GetChannelSnapshot(0), fast.Paula.GetChannelSnapshot(0));
		Assert.Equal(CapturePaulaDmaGrants(slow), CapturePaulaDmaGrants(fast));
	}

	[Fact]
	public void RegisterReadAheadDoesNotChangeLaterManualAudioPlayback()
	{
		var polled = CreatePaulaComponentBus();
		var baseline = CreatePaulaComponentBus();
		ScheduleManualRewriteSequence(polled);
		ScheduleManualRewriteSequence(baseline);
		var cycle = 20L;
		_ = polled.ReadWord(0x00DFF01E, ref cycle, AmigaBusAccessKind.CpuDataRead);
		var polledBuffer = new float[6];
		var baselineBuffer = new float[6];

		RenderManualRewriteSequence(polled, polledBuffer);
		RenderManualRewriteSequence(baseline, baselineBuffer);

		Assert.Equal(baselineBuffer, polledBuffer);
	}

	[Fact]
	public void ManualAudioDataRewriteAfterLowBoundaryIsCausal()
	{
		var bus = CreatePaulaComponentBus();
		SchedulePaulaWrite(bus, 0x0A6, 0x0002, 0);
		SchedulePaulaWrite(bus, 0x0AA, 0x7F81, 0);
		SchedulePaulaWrite(bus, 0x0AA, 0x4080, 5);
		var buffer = new float[6];

		bus.Paula.RenderSample(3, buffer, 0, 2);
		var beforeBoundary = bus.Paula.GetChannelSnapshot(0);
		bus.Paula.RenderSample(4, buffer, 1, 2);
		var atBoundary = bus.Paula.GetChannelSnapshot(0);
		bus.Paula.RenderSample(5, buffer, 2, 2);
		var afterRewrite = bus.Paula.GetChannelSnapshot(0);

		Assert.Equal(0x7F, beforeBoundary.CurrentSample);
		Assert.Equal(unchecked((sbyte)0x81), atBoundary.CurrentSample);
		Assert.Equal(0x40, afterRewrite.CurrentSample);
		Assert.Equal(9, afterRewrite.NextSampleCycle);
	}

	[Fact]
	public void ManualAudioDataCanBeReplacedBeforeLowByteIsPlayed()
	{
		var bus = CreatePaulaComponentBus();
		bus.WriteWord(0x00DFF0A6, 0x0004, 0);
		bus.WriteWord(0x00DFF0AA, 0x7F81, 0);
		bus.WriteWord(0x00DFF0AA, 0x4080, 4);
		var buffer = new float[6];

		bus.Paula.RenderSample(0, buffer, 0, 2);
		bus.Paula.RenderSample(4, buffer, 1, 2);
		bus.Paula.RenderSample(12, buffer, 2, 2);

		Assert.True(buffer[0] > 0.20f);
		Assert.True(buffer[2] > 0.10f);
		Assert.True(buffer[4] < -0.20f);
	}

	[Fact]
	public void IntenaGatesAudioInterruptEventsButNotIntreqPolling()
	{
		var bus = CreatePaulaComponentBus();

		bus.WriteWord(0x00DFF0AA, 0x0102, 0);
		bus.Paula.AdvanceTo(0);
		Assert.Empty(bus.Paula.DrainInterrupts());
		Assert.True((bus.ReadWord(0x00DFF01E) & 0x0080) != 0);

		bus.WriteWord(0x00DFF09C, 0x0080, 0);
		bus.WriteWord(0x00DFF09A, 0xC080, 0);
		bus.WriteWord(0x00DFF0AA, 0x0102, 0);
		bus.Paula.AdvanceTo(0);

		Assert.Single(bus.Paula.DrainInterrupts());
		Assert.True((bus.ReadWord(0x00DFF01C) & 0x4080) == 0x4080);
	}

	[Fact]
	public void DmaFetchesWordsAdvancesPointerAndReloadsLength()
	{
		var bus = CreatePaulaComponentBus();
		bus.ChipRam[0x1000] = 0x7F;
		bus.ChipRam[0x1001] = 0x81;
		bus.ChipRam[0x1002] = 0x40;
		bus.ChipRam[0x1003] = 0xC0;
		bus.WriteWord(0x00DFF09A, 0xC080, 0);
		bus.WriteWord(0x00DFF0A0, 0x0000, 0);
		bus.WriteWord(0x00DFF0A2, 0x1000, 0);
		bus.WriteWord(0x00DFF0A4, 0x0002, 0);
		bus.WriteWord(0x00DFF0A6, 0x0002, 0);
		bus.WriteWord(0x00DFF096, 0x8201, 0);
		var buffer = new float[8];

		bus.Paula.RenderSample(34, buffer, 0, 2);
		bus.Paula.RenderSample(38, buffer, 1, 2);
		bus.Paula.RenderSample(42, buffer, 2, 2);
		bus.Paula.RenderSample(46, buffer, 3, 2);
		var snapshot = bus.Paula.GetChannelSnapshot(0);

		Assert.Equal(0.0f, buffer[0]);
		Assert.True(buffer[2] > 0.10f);
		Assert.True(buffer[4] < -0.10f);
		Assert.True(buffer[6] > 0.20f);
		Assert.Equal(0x1004u, snapshot.CurrentAddress);
		Assert.Equal(0, snapshot.RemainingWords);
		Assert.Contains(bus.Paula.DrainInterrupts(), interruptEvent => interruptEvent.Channel == 0);
	}

	[Fact]
	public void DmaFetchRequestCyclesRemainExactAcrossPeriods()
	{
		var bus = CreatePaulaComponentBus();
		bus.ChipRam[0x1000] = 0x7F;
		bus.ChipRam[0x1001] = 0x81;
		bus.ChipRam[0x1002] = 0x40;
		bus.ChipRam[0x1003] = 0xC0;
		SchedulePaulaWrite(bus, 0x0A2, 0x1000, 0);
		SchedulePaulaWrite(bus, 0x0A4, 0x0002, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x0003, 0);
		SchedulePaulaWrite(bus, 0x096, 0x8201, 0);

		bus.Paula.AdvanceTo(70);

		var requestedCycles = bus.BusAccesses
			.Where(access => access.Request.Kind == AmigaBusAccessKind.PaulaDma)
			.Select(access => access.RequestedCycle)
			.ToArray();
		Assert.Equal(new long[] { 0, 34, 38, 50 }, requestedCycles);
		Assert.Equal(74, bus.Paula.GetChannelSnapshot(0).NextSampleCycle);
	}

	[Fact]
	public void DmaAudioWordBecomesAudibleOnlyAfterGrantedSlotCompletes()
	{
		var bus = CreatePaulaComponentBus();
		bus.ChipRam[0x1000] = 0x7F;
		bus.ChipRam[0x1001] = 0x81;
		SchedulePaulaWrite(bus, 0x0A2, 0x1000, 0);
		SchedulePaulaWrite(bus, 0x0A4, 0x0001, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x0001, 0);
		SchedulePaulaWrite(bus, 0x096, 0x8201, 20);
		var buffer = new float[4];

		bus.Paula.RenderSample(37, buffer, 0, 2);
		var beforeCompletion = bus.Paula.GetChannelSnapshot(0);
		bus.Paula.RenderSample(38, buffer, 1, 2);
		var atCompletion = bus.Paula.GetChannelSnapshot(0);
		var dma = bus.BusAccesses
			.Where(access => access.Request.Kind == AmigaBusAccessKind.PaulaDma)
			.ToArray();

		Assert.Equal(3, dma.Length);
		Assert.Equal(20, dma[0].RequestedCycle);
		Assert.Equal(32, dma[0].GrantedCycle);
		Assert.Equal(34, dma[0].CompletedCycle);
		Assert.Equal(34, dma[1].RequestedCycle);
		Assert.Equal(36, dma[1].GrantedCycle);
		Assert.Equal(38, dma[1].CompletedCycle);
		Assert.Equal(38, dma[2].RequestedCycle);
		Assert.Equal(40, dma[2].GrantedCycle);
		Assert.Equal(42, dma[2].CompletedCycle);
		Assert.Equal(0, beforeCompletion.CurrentSample);
		Assert.False(beforeCompletion.HasDataWord);
		Assert.Equal(38, beforeCompletion.NextSampleCycle);
		Assert.True(buffer[2] > 0.20f);
		Assert.Equal(0x7F, atCompletion.CurrentSample);
		Assert.True(atCompletion.NextByteIsLow);
		Assert.Equal(40, atCompletion.NextSampleCycle);
	}

	[Fact]
	public void DmaAudioUsesAudxperBelowRecommendedMinimumForSampleTiming()
	{
		var bus = CreateDefaultMinimumPaulaBus();
		bus.ChipRam[0x1000] = 0x7F;
		bus.ChipRam[0x1001] = 0x81;
		bus.WriteWord(0x00DFF0A0, 0x0000, 0);
		bus.WriteWord(0x00DFF0A2, 0x1000, 0);
		bus.WriteWord(0x00DFF0A4, 0x0001, 0);
		bus.WriteWord(0x00DFF0A6, 0x0071, 0);
		bus.WriteWord(0x00DFF096, 0x8201, 0);
		bus.Paula.AdvanceTo(0);
		var afterEnable = bus.Paula.GetChannelSnapshot(0);
		var buffer = new float[4];

		bus.Paula.RenderSample(263, buffer, 0, 2);
		bus.Paula.RenderSample(264, buffer, 1, 2);
		var afterBoundary = bus.Paula.GetChannelSnapshot(0);

		Assert.Equal(113, afterEnable.Period);
		Assert.Equal(34, afterEnable.NextSampleCycle);
		Assert.True(buffer[0] > 0.20f);
		Assert.True(buffer[2] < -0.20f);
		Assert.Equal(490, afterBoundary.NextSampleCycle);
	}

	[Fact]
	public void AudxperZeroIsLatchedRawAndUsesFullSixteenBitEffectivePeriod()
	{
		var bus = CreatePaulaComponentBus();
		bus.ChipRam[0x1000] = 0x7F;
		bus.ChipRam[0x1001] = 0x81;
		SchedulePaulaWrite(bus, 0x0A2, 0x1000, 0);
		SchedulePaulaWrite(bus, 0x0A4, 0x0001, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x0000, 0);
		SchedulePaulaWrite(bus, 0x096, 0x8201, 0);

		bus.Paula.AdvanceTo(0);
		var afterEnable = bus.Paula.GetChannelSnapshot(0);
		bus.Paula.AdvanceTo(262_181);
		var beforeNextDmaFetch = bus.BusAccesses.Count(access => access.Request.Kind == AmigaBusAccessKind.PaulaDma);
		bus.Paula.AdvanceTo(262_182);
		var afterNextDmaFetch = bus.BusAccesses.Count(access => access.Request.Kind == AmigaBusAccessKind.PaulaDma);

		Assert.Equal(0, afterEnable.Period);
		Assert.Equal(34, afterEnable.NextSampleCycle);
		Assert.Equal(3, beforeNextDmaFetch);
		Assert.Equal(4, afterNextDmaFetch);
	}

	[Fact]
	public void PartialPlaybackLiveDmaRefillsAtNextChannelSlotWhenWordIsNeeded()
	{
		var bus = new AmigaBus(
			enableLiveAgnusDma: true,
			enableLiveDisplayDma: false);
		bus.ChipRam[0x1000] = 0x7F;
		bus.ChipRam[0x1001] = 0x81;
		SchedulePaulaWrite(bus, 0x0A2, 0x1000, 0);
		SchedulePaulaWrite(bus, 0x0A4, 0x0001, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x0001, 0);
		SchedulePaulaWrite(bus, 0x096, 0x8201, 0);

		bus.Paula.AdvanceTo(1_000);

		var requestedCycles = bus.BusAccesses
			.Where(access => access.Request.Kind == AmigaBusAccessKind.PaulaDma)
			.Select(access => access.RequestedCycle)
			.ToArray();
		Assert.Equal(new[] { AudioSlotCycle(0, 0), AudioSlotCycle(1, 0), AudioSlotCycle(2, 0) }, requestedCycles);
	}

	[Fact]
	public void LiveDmaBelowMinimumPeriodRepeatsSampleWithoutStretchingPeriodClock()
	{
		var bus = new AmigaBus(
			enableLiveAgnusDma: true,
			enableLiveDisplayDma: false);
		bus.ChipRam[0x1000] = 0x00;
		bus.ChipRam[0x1001] = 0x00;
		bus.ChipRam[0x1002] = 0x7F;
		bus.ChipRam[0x1003] = 0x81;
		bus.ChipRam[0x1004] = 0x40;
		bus.ChipRam[0x1005] = 0xC0;
		SchedulePaulaWrite(bus, 0x0A2, 0x1000, 0);
		SchedulePaulaWrite(bus, 0x0A4, 0x0003, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x0071, 0);
		SchedulePaulaWrite(bus, 0x096, 0x8201, 0);
		var buffer = new float[8];

		bus.Paula.RenderSample(488, buffer, 0, 2);
		bus.Paula.RenderSample(714, buffer, 1, 2);
		bus.Paula.RenderSample(940, buffer, 2, 2);
		var afterUnderrun = bus.Paula.GetChannelSnapshot(0);
		bus.Paula.RenderSample(1166, buffer, 3, 2);

		Assert.True(buffer[0] > 0.20f);
		Assert.True(buffer[2] < -0.20f);
		Assert.True(buffer[4] < -0.20f);
		Assert.True(buffer[6] > 0.10f);
		Assert.Equal(1166, afterUnderrun.NextSampleCycle);
	}

	[Fact]
	public void DmaAudioBelowMinimumPeriodRepeatsLowByteUntilNextDmaWordArrives()
	{
		var probe = CreateMinimumPeriodDmaUnderrunBus();
		probe.Paula.AdvanceTo(2_000);
		var dma = probe.BusAccesses
			.Where(access => access.Request.Kind == AmigaBusAccessKind.PaulaDma)
			.ToArray();
		var firstWordCycle = dma[1].CompletedCycle;
		var secondWordCycle = dma[2].CompletedCycle;
		var bus = CreateMinimumPeriodDmaUnderrunBus();
		var buffer = new float[8];

		bus.Paula.RenderSample(firstWordCycle, buffer, 0, 2);
		bus.Paula.RenderSample(firstWordCycle + 2, buffer, 1, 2);
		bus.Paula.RenderSample(secondWordCycle - 2, buffer, 2, 2);
		bus.Paula.RenderSample(secondWordCycle, buffer, 3, 2);
		var lateBus = CreateMinimumPeriodDmaUnderrunBus();
		var lateBuffer = new float[2];
		lateBus.Paula.RenderSample(secondWordCycle - 2, lateBuffer, 0, 2);
		var details = $"dma={string.Join(",", dma.Select(access => $"{access.RequestedCycle}->{access.CompletedCycle}"))}; samples={string.Join(",", buffer)}";

		Assert.True(buffer[0] > 0.20f, details);
		Assert.True(buffer[2] < -0.20f, details);
		Assert.True(buffer[4] < -0.20f, details);
		Assert.True(buffer[6] > 0.10f, details);
		Assert.True(lateBuffer[0] < -0.20f, $"{details}; late={string.Join(",", lateBuffer)}");
	}

	[Fact]
	public void FullLiveDmaRefillsAtNextChannelSlotWhenWordIsNeeded()
	{
		var bus = new AmigaBus(
			enableLiveAgnusDma: true,
			enableLiveDisplayDma: true);
		bus.ChipRam[0x1000] = 0x7F;
		bus.ChipRam[0x1001] = 0x81;
		SchedulePaulaWrite(bus, 0x0A2, 0x1000, 0);
		SchedulePaulaWrite(bus, 0x0A4, 0x0001, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x0001, 0);
		SchedulePaulaWrite(bus, 0x096, 0x8201, 0);

		bus.Paula.AdvanceTo(1_000);

		var requestedCycles = bus.BusAccesses
			.Where(access => access.Request.Kind == AmigaBusAccessKind.PaulaDma)
			.Select(access => access.RequestedCycle)
			.ToArray();
		Assert.Equal(new[] { AudioSlotCycle(0, 0), AudioSlotCycle(1, 0), AudioSlotCycle(2, 0) }, requestedCycles);
	}

	[Fact]
	public void DmaLengthReloadInterruptsUseExactSampleBoundary()
	{
		var bus = CreatePaulaComponentBus();
		bus.ChipRam[0x1000] = 0x20;
		bus.ChipRam[0x1001] = 0xE0;
		SchedulePaulaWrite(bus, 0x09A, 0xC080, 0);
		SchedulePaulaWrite(bus, 0x0A2, 0x1000, 0);
		SchedulePaulaWrite(bus, 0x0A4, 0x0001, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x0002, 0);
		SchedulePaulaWrite(bus, 0x096, 0x8201, 0);

		bus.Paula.AdvanceTo(46);

		var interruptCycles = bus.Paula.DrainInterrupts()
			.Where(interruptEvent => interruptEvent.Channel == 0)
			.Select(interruptEvent => interruptEvent.Cycle)
			.ToArray();
		Assert.Contains(34, interruptCycles);
		Assert.Contains(38, interruptCycles);
		Assert.All(interruptCycles, cycle => Assert.Contains(cycle, new long[] { 34, 38, 42 }));
		Assert.Equal(50, bus.Paula.GetChannelSnapshot(0).NextSampleCycle);
	}

	[Fact]
	public void DmaLengthOneReloadsFromOriginalLocation()
	{
		var bus = CreatePaulaComponentBus();
		bus.ChipRam[0x1000] = 0x20;
		bus.ChipRam[0x1001] = 0xE0;
		bus.WriteWord(0x00DFF09A, 0xC080, 0);
		bus.WriteWord(0x00DFF0A2, 0x1000, 0);
		bus.WriteWord(0x00DFF0A4, 0x0001, 0);
		bus.WriteWord(0x00DFF0A6, 0x0001, 0);
		bus.WriteWord(0x00DFF096, 0x8201, 0);

		bus.Paula.AdvanceTo(42);
		var snapshot = bus.Paula.GetChannelSnapshot(0);

		Assert.Equal(0x1002u, snapshot.CurrentAddress);
		Assert.Equal(0, snapshot.RemainingWords);
		Assert.True(bus.Paula.DrainInterrupts().Count >= 2);
	}

	[Fact]
	public void AdkconUsesSetClearSemantics()
	{
		var bus = CreatePaulaComponentBus();

		bus.WriteWord(0x00DFF09E, 0x8011, 0);
		bus.Paula.AdvanceTo(0);
		Assert.Equal(0x0011, bus.ReadWord(0x00DFF010));
		bus.WriteWord(0x00DFF09E, 0x0010, 0);
		bus.Paula.AdvanceTo(0);
		Assert.Equal(0x0001, bus.ReadWord(0x00DFF010));
	}

	[Fact]
	public void AdkconVolumeAttachMutesSourceAndModulatesNextChannelVolume()
	{
		var bus = CreatePaulaComponentBus();
		bus.WriteWord(0x00DFF09E, 0x8001, 0);
		bus.WriteWord(0x00DFF0A6, 0x0002, 0);
		bus.WriteWord(0x00DFF0AA, 0x0020, 0);
		var buffer = new float[4];

		bus.Paula.RenderSample(0, buffer, 0, 2);
		bus.Paula.RenderSample(4, buffer, 1, 2);
		var source = bus.Paula.GetChannelSnapshot(0);
		var target = bus.Paula.GetChannelSnapshot(1);

		Assert.Equal(0.0f, buffer[0]);
		Assert.Equal(0.0f, buffer[2]);
		Assert.Equal(32, target.Volume);
		Assert.Equal(0x0020, source.DataWord);
	}

	[Fact]
	public void AdkconPeriodAttachModulatesNextChannelPeriod()
	{
		var bus = CreatePaulaComponentBus();
		bus.WriteWord(0x00DFF09E, 0x8010, 0);
		bus.WriteWord(0x00DFF0A6, 0x0002, 0);
		bus.WriteWord(0x00DFF0AA, 0x0005, 0);

		bus.Paula.AdvanceTo(4);

		Assert.Equal(5, bus.Paula.GetChannelSnapshot(1).Period);
	}

	private static int CountPaulaDmaReads(AmigaBus bus)
		=> bus.BusAccesses.Count(access => access.Request.Kind == AmigaBusAccessKind.PaulaDma);

	private static (uint Address, long RequestedCycle, long GrantedCycle)[] CapturePaulaDmaGrants(AmigaBus bus)
		=> bus.BusAccesses
			.Where(access => access.Request.Kind == AmigaBusAccessKind.PaulaDma)
			.Select(access => (access.Request.Address, access.RequestedCycle, access.GrantedCycle))
			.ToArray();

	private static AmigaBusAccessResult[] CapturePaulaDmaAccesses(AmigaBus bus)
		=> bus.BusAccesses
			.Where(access => access.Request.Kind == AmigaBusAccessKind.PaulaDma)
			.ToArray();

	private static void ScheduleManualRewriteSequence(AmigaBus bus)
	{
		SchedulePaulaWrite(bus, 0x0A6, 0x0002, 0);
		SchedulePaulaWrite(bus, 0x0AA, 0x7F81, 0);
		SchedulePaulaWrite(bus, 0x0AA, 0x4080, 5);
	}

	private static void RenderManualRewriteSequence(AmigaBus bus, float[] buffer)
	{
		bus.Paula.RenderSample(3, buffer, 0, 2);
		bus.Paula.RenderSample(4, buffer, 1, 2);
		bus.Paula.RenderSample(5, buffer, 2, 2);
	}

	private static void SchedulePaulaWrite(AmigaBus bus, ushort offset, ushort value, long cycle)
	{
		bus.Paula.ScheduleWrite(cycle, offset, value);
	}

	private static AmigaBus CreatePaulaComponentBus()
	{
		return new AmigaBus(
			enableLiveAgnusDma: false,
			audioDmaMinimumPeriod: 1);
	}

	private static AmigaBus CreateLivePaulaSlotBus()
	{
		return new AmigaBus(
			enableLiveAgnusDma: true,
			enableLiveDisplayDma: false,
			audioDmaMinimumPeriod: 1);
	}

	private static long AudioSlotCycle(int line, int channel)
	{
		return ((long)line * AmigaConstants.A500PalCpuCyclesPerRasterLine) +
			((AgnusHrmOcsSlotTable.FirstPaulaHorizontal + (channel * 2)) *
				AmigaConstants.A500PalCpuCyclesPerColorClock);
	}

	private static AmigaBus CreateDefaultMinimumPaulaBus()
	{
		return new AmigaBus(
			enableLiveAgnusDma: false);
	}

	private static AmigaBus CreateStableRegisterDmaBus()
	{
		var bus = CreatePaulaComponentBus();
		for (var i = 0; i < 32; i++)
		{
			bus.ChipRam[0x1000 + i] = (byte)(0x10 + i);
		}

		SchedulePaulaWrite(bus, 0x09A, 0xC080, 0);
		SchedulePaulaWrite(bus, 0x0A2, 0x1000, 0);
		SchedulePaulaWrite(bus, 0x0A4, 0x0008, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x0002, 0);
		SchedulePaulaWrite(bus, 0x096, 0x8201, 0);
		return bus;
	}

	private static AmigaBus CreateZeroPeriodDmaBus()
	{
		var bus = CreatePaulaComponentBus();
		bus.ChipRam[0x1000] = 0x7F;
		bus.ChipRam[0x1001] = 0x81;
		SchedulePaulaWrite(bus, 0x0A2, 0x1000, 0);
		SchedulePaulaWrite(bus, 0x0A4, 0x0001, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x0000, 0);
		SchedulePaulaWrite(bus, 0x096, 0x8201, 0);
		return bus;
	}

	private static AmigaBus CreateAttachedPeriodDmaBus()
	{
		var bus = CreatePaulaComponentBus();
		bus.ChipRam[0x1000] = 0x00;
		bus.ChipRam[0x1001] = 0x05;
		bus.ChipRam[0x1002] = 0x00;
		bus.ChipRam[0x1003] = 0x06;
		SchedulePaulaWrite(bus, 0x09E, 0x8010, 0);
		SchedulePaulaWrite(bus, 0x0A2, 0x1000, 0);
		SchedulePaulaWrite(bus, 0x0A4, 0x0002, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x0002, 0);
		SchedulePaulaWrite(bus, 0x096, 0x8201, 0);
		return bus;
	}

	private static AmigaBus CreateMinimumPeriodDmaUnderrunBus()
	{
		var bus = new AmigaBus(
			enableLiveAgnusDma: true,
			enableLiveDisplayDma: false);
		bus.ChipRam[0x1000] = 0x00;
		bus.ChipRam[0x1001] = 0x00;
		bus.ChipRam[0x1002] = 0x7F;
		bus.ChipRam[0x1003] = 0x81;
		bus.ChipRam[0x1004] = 0x40;
		bus.ChipRam[0x1005] = 0xC0;
		SchedulePaulaWrite(bus, 0x0A2, 0x1000, 0);
		SchedulePaulaWrite(bus, 0x0A4, 0x0003, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x0001, 0);
		SchedulePaulaWrite(bus, 0x096, 0x8201, 0);
		return bus;
	}
}
