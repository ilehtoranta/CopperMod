using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class PaulaTests
{
	[Fact]
	public void ManualAudioTraversesExplicitHighLowAndIdleStates()
	{
		var bus = CreatePaulaComponentBus();
		SchedulePaulaWrite(bus, 0x0A6, 0x0002, 0);
		SchedulePaulaWrite(bus, 0x0AA, 0x7F81, 0);

		bus.Paula.AdvanceTo(0);
		var high = bus.Paula.GetChannelSnapshot(0);
		bus.Paula.AdvanceTo(4);
		var lowMinusOne = bus.Paula.GetChannelSnapshot(0);
		bus.Paula.AdvanceTo(6);
		var low = bus.Paula.GetChannelSnapshot(0);
		bus.Paula.AdvanceTo(8);
		var idle = bus.Paula.GetChannelSnapshot(0);

		Assert.Equal(PaulaAudioState.HighByte, high.State);
		Assert.Equal(PaulaAudioState.ManualPeriodOne, lowMinusOne.State);
		Assert.Equal(PaulaAudioState.LowByte, low.State);
		Assert.Equal(1, low.IrqCheck);
		Assert.Equal(PaulaAudioState.Idle, idle.State);
		Assert.False(idle.DataRequest);
		Assert.False(idle.DmaSpecialRequest);
	}

	[Fact]
	public void ManualIrqCheckIsLatchedOneCckBeforeWordEnds()
	{
		var bus = CreatePaulaComponentBus();
		SchedulePaulaWrite(bus, 0x0A6, 0x0002, 0);
		SchedulePaulaWrite(bus, 0x0AA, 0x7F81, 0);
		SchedulePaulaWrite(bus, 0x09C, 0x0080, 7);
		SchedulePaulaWrite(bus, 0x0AA, 0x4080, 7);

		bus.Paula.AdvanceTo(6);
		Assert.Equal(1, bus.Paula.GetChannelSnapshot(0).IrqCheck);
		bus.Paula.AdvanceTo(8);
		var stopped = bus.Paula.GetChannelSnapshot(0);

		Assert.Equal(PaulaAudioState.Idle, stopped.State);
		Assert.False(stopped.HasDataWord);
		Assert.True(stopped.DataLatchWritten);
	}

	[Fact]
	public void DelayedIntreq2SurvivesIdleAndRetriggersOnDmaEnable()
	{
		var bus = CreatePaulaComponentBus();
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x7F81);
		SchedulePaulaWrite(bus, 0x09A, 0xC080, 0);
		SchedulePaulaWrite(bus, 0x0A2, 0x1000, 0);
		SchedulePaulaWrite(bus, 0x0A4, 0x0001, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x0002, 0);
		SchedulePaulaWrite(bus, 0x096, 0x8201, 0);
		SchedulePaulaWrite(bus, 0x096, 0x0001, 43);
		SchedulePaulaWrite(bus, 0x09C, 0x0080, 47);
		SchedulePaulaWrite(bus, 0x096, 0x8001, 48);

		bus.Paula.AdvanceTo(42);
		var armed = bus.Paula.GetChannelSnapshot(0);
		bus.Paula.AdvanceTo(46);
		var idle = bus.Paula.GetChannelSnapshot(0);
		bus.Paula.AdvanceTo(47);
		Assert.Equal(0, bus.Paula.Intreq & 0x0080);
		bus.Paula.AdvanceTo(48);
		var restarted = bus.Paula.GetChannelSnapshot(0);

		Assert.True(
			armed.State == PaulaAudioState.LowByte,
			$"state={armed.State}, next={armed.NextSampleCycle}, sample={armed.CurrentSample}, data={armed.DataWord:X4}, pending={armed.DelayedInterruptPending}, address={armed.CurrentAddress:X8}, remaining={armed.RemainingWords}");
		Assert.True(armed.DelayedInterruptPending);
		Assert.Equal(PaulaAudioState.Idle, idle.State);
		Assert.True(idle.DelayedInterruptPending);
		Assert.Equal(PaulaAudioState.DmaStartup, restarted.State);
		Assert.False(restarted.DelayedInterruptPending);
		Assert.NotEqual(0, bus.Paula.Intreq & 0x0080);
		Assert.Contains(bus.Paula.DrainInterrupts(), interruptEvent => interruptEvent.Cycle == 48);
	}

	[Fact]
	public void DelayedIntreq2ArmsAtDmaGrantBeforeAudxdatLoadCompletes()
	{
		var bus = CreatePaulaComponentBus();
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x7F81);
		SchedulePaulaWrite(bus, 0x0A2, 0x1000, 0);
		SchedulePaulaWrite(bus, 0x0A4, 0x0001, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x0002, 0);
		SchedulePaulaWrite(bus, 0x096, 0x8201, 0);

		bus.Paula.AdvanceTo(39);
		var beforeGrant = bus.Paula.GetChannelSnapshot(0);
		bus.Paula.AdvanceTo(40);
		var atGrant = bus.Paula.GetChannelSnapshot(0);

		Assert.False(beforeGrant.DelayedInterruptPending);
		Assert.True(atGrant.DelayedInterruptPending);
		Assert.Equal(0x7F81, atGrant.DataWord);
	}

	[Fact]
	public void NormalDmaConsumesDelayedIntreq2AtLowToHighTransition()
	{
		var bus = CreatePaulaComponentBus();
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x7F81);
		SchedulePaulaWrite(bus, 0x09A, 0xC080, 0);
		SchedulePaulaWrite(bus, 0x0A2, 0x1000, 0);
		SchedulePaulaWrite(bus, 0x0A4, 0x0001, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x0002, 0);
		SchedulePaulaWrite(bus, 0x096, 0x8201, 0);
		SchedulePaulaWrite(bus, 0x09C, 0x0080, 43);

		bus.Paula.AdvanceTo(45);
		Assert.True(bus.Paula.GetChannelSnapshot(0).DelayedInterruptPending);
		bus.Paula.AdvanceTo(46);

		var snapshot = bus.Paula.GetChannelSnapshot(0);
		Assert.False(
			snapshot.DelayedInterruptPending,
			$"state={snapshot.State} next={snapshot.NextSampleCycle} word=0x{snapshot.DataWord:X4} " +
			$"intreq=0x{bus.Paula.Intreq:X4}");
		Assert.NotEqual(0, bus.Paula.Intreq & 0x0080);
		Assert.Contains(bus.Paula.DrainInterrupts(), interruptEvent => interruptEvent.Cycle == 46);
	}

	[Fact]
	public void PeriodAttachConsumesDelayedIntreq2AtHighToLowTransition()
	{
		var bus = CreatePaulaComponentBus();
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x0002);
		SchedulePaulaWrite(bus, 0x09A, 0xC080, 0);
		SchedulePaulaWrite(bus, 0x09E, 0x8010, 0);
		SchedulePaulaWrite(bus, 0x0A2, 0x1000, 0);
		SchedulePaulaWrite(bus, 0x0A4, 0x0001, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x0002, 0);
		SchedulePaulaWrite(bus, 0x096, 0x8201, 0);
		SchedulePaulaWrite(bus, 0x09C, 0x0080, 43);

		bus.Paula.AdvanceTo(45);
		Assert.True(bus.Paula.GetChannelSnapshot(0).DelayedInterruptPending);
		bus.Paula.AdvanceTo(50);

		var snapshot = bus.Paula.GetChannelSnapshot(0);
		Assert.False(
			snapshot.DelayedInterruptPending,
			$"state={snapshot.State} next={snapshot.NextSampleCycle} word=0x{snapshot.DataWord:X4} " +
			$"intreq=0x{bus.Paula.Intreq:X4}");
		Assert.NotEqual(0, bus.Paula.Intreq & 0x0080);
		Assert.Contains(bus.Paula.DrainInterrupts(), interruptEvent => interruptEvent.Cycle == 50);
	}

	[Fact]
	public void AudxlenZeroUsesFullSixteenBitDmaCounter()
	{
		var bus = CreatePaulaComponentBus();

		SchedulePaulaWrite(bus, 0x0A4, 0x0000, 0);
		bus.Paula.AdvanceTo(0);

		Assert.Equal(65_536, bus.Paula.GetChannelSnapshot(0).LengthWords);
	}

	[Fact]
	public void ManualAudioDataOutputsHighThenLowByteAndRequestsInterrupt()
	{
		var bus = CreatePaulaComponentBus();
		SchedulePaulaWrite(bus, 0x09A, 0xC080, 0);
		SchedulePaulaWrite(bus, 0x0AA, 0x7F81, 0);
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
	public void ManualAudioUsesHoldingLatchAndContinuesAfterInterruptIsCleared()
	{
		var bus = CreatePaulaComponentBus();
		SchedulePaulaWrite(bus, 0x0A6, 0x0002, 0);
		SchedulePaulaWrite(bus, 0x0AA, 0x7F81, 0);
		SchedulePaulaWrite(bus, 0x09C, 0x0080, 1);
		SchedulePaulaWrite(bus, 0x0AA, 0x4080, 1);
		var buffer = new float[8];

		bus.Paula.RenderSample(0, buffer, 0, 2);
		bus.Paula.RenderSample(4, buffer, 1, 2);
		bus.Paula.RenderSample(8, buffer, 2, 2);
		bus.Paula.RenderSample(12, buffer, 3, 2);

		Assert.True(buffer[0] > 0.20f);
		Assert.True(buffer[2] < -0.20f);
		Assert.True(buffer[4] > 0.10f);
		Assert.True(buffer[6] < -0.20f);
		Assert.Equal(0x4080, bus.Paula.GetChannelSnapshot(0).DataWord);
	}

	[Fact]
	public void ManualAudioStopsAfterFullWordWhenInterruptRemainsPending()
	{
		var bus = CreatePaulaComponentBus();
		SchedulePaulaWrite(bus, 0x0A6, 0x0002, 0);
		SchedulePaulaWrite(bus, 0x0AA, 0x7F81, 0);
		SchedulePaulaWrite(bus, 0x0AA, 0x4080, 1);
		var buffer = new float[8];

		bus.Paula.RenderSample(0, buffer, 0, 2);
		bus.Paula.RenderSample(4, buffer, 1, 2);
		bus.Paula.RenderSample(8, buffer, 2, 2);
		bus.Paula.RenderSample(12, buffer, 3, 2);

		Assert.True(buffer[0] > 0.20f);
		Assert.True(buffer[2] < -0.20f);
		Assert.True(buffer[4] < -0.20f);
		Assert.True(buffer[6] < -0.20f);
		Assert.False(bus.Paula.GetChannelSnapshot(0).HasDataWord);
	}

	[Fact]
	public void CombinedAttachmentAppliesOneWordToVolumeThenPeriodTransitions()
	{
		var bus = CreatePaulaComponentBus();
		SchedulePaulaWrite(bus, 0x09E, 0x8011, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x0002, 0);
		SchedulePaulaWrite(bus, 0x0AA, 0x0020, 0);

		bus.Paula.AdvanceTo(0);
		var afterLoad = bus.Paula.GetChannelSnapshot(1);
		bus.Paula.AdvanceTo(4);
		var afterHighTransition = bus.Paula.GetChannelSnapshot(1);

		Assert.Equal(32, afterLoad.Volume);
		Assert.Equal(428, afterLoad.Period);
		Assert.Equal(32, afterHighTransition.Volume);
		Assert.Equal(32, afterHighTransition.Period);
	}

	[Theory]
	[InlineData(0x0008)]
	[InlineData(0x0080)]
	[InlineData(0x0088)]
	public void ChannelThreeAttachmentSuppressesItsDacOutput(ushort attachBits)
	{
		var bus = CreatePaulaComponentBus();
		bus.WriteWord(0x00DFF09E, (ushort)(0x8000 | attachBits), 0);
		bus.WriteWord(0x00DFF0DA, 0x7F81, 0);
		var buffer = new float[2];

		bus.Paula.RenderSample(0, buffer, 0, 2);

		Assert.Equal(0.0f, buffer[0]);
		Assert.Equal(0.0f, buffer[1]);
	}

	[Fact]
	public void ShortDmaDisableReenableContinuesActiveStreamWithoutRestart()
	{
		var bus = CreatePaulaComponentBus();
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x0000);
		BigEndian.WriteUInt16(bus.ChipRam, 0x1002, 0x1122);
		BigEndian.WriteUInt16(bus.ChipRam, 0x1004, 0x3344);
		BigEndian.WriteUInt16(bus.ChipRam, 0x1006, 0x5566);
		SchedulePaulaWrite(bus, 0x0A2, 0x1000, 0);
		SchedulePaulaWrite(bus, 0x0A4, 0x0004, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x0002, 0);
		SchedulePaulaWrite(bus, 0x096, 0x8201, 0);
		SchedulePaulaWrite(bus, 0x096, 0x0001, 39);
		SchedulePaulaWrite(bus, 0x096, 0x8001, 40);
		var buffer = new float[6];

		bus.Paula.RenderSample(38, buffer, 0, 2);
		bus.Paula.RenderSample(42, buffer, 1, 2);
		bus.Paula.RenderSample(46, buffer, 2, 2);

		Assert.InRange(buffer[0], 0.03f, 0.05f);
		Assert.InRange(buffer[2], 0.06f, 0.08f);
		Assert.InRange(buffer[4], 0.09f, 0.12f);
		Assert.Single(CapturePaulaDmaAccesses(bus), access => access.Request.Address == 0x1000u);
	}

	[Fact]
	public void DmaReenableAfterActiveWordFinishesRestartsFromLocation()
	{
		var bus = CreatePaulaComponentBus();
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x0000);
		BigEndian.WriteUInt16(bus.ChipRam, 0x1002, 0x1122);
		BigEndian.WriteUInt16(bus.ChipRam, 0x1004, 0x3344);
		BigEndian.WriteUInt16(bus.ChipRam, 0x1006, 0x5566);
		SchedulePaulaWrite(bus, 0x0A2, 0x1000, 0);
		SchedulePaulaWrite(bus, 0x0A4, 0x0004, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x0002, 0);
		SchedulePaulaWrite(bus, 0x096, 0x8201, 0);
		SchedulePaulaWrite(bus, 0x096, 0x0001, 39);
		SchedulePaulaWrite(bus, 0x096, 0x8001, 48);
		var buffer = new float[4];

		bus.Paula.RenderSample(38, buffer, 0, 2);
		bus.Paula.RenderSample(46, buffer, 1, 2);
		Assert.False(bus.Paula.GetChannelSnapshot(0).HasDataWord);

		bus.Paula.AdvanceTo(48);

		Assert.True(CapturePaulaDmaAccesses(bus).Count(access => access.Request.Address == 0x1000u) >= 2);
		Assert.Equal(48, bus.Paula.GetChannelSnapshot(0).LastDmaEnableCycle);
	}

	[Fact]
	public void TimedWriteOnlyCustomReadWritesSharedChipBusLatchIntoRegister()
	{
		var bus = CreatePaulaComponentBus();
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x0003);
		var cycle = 0L;

		Assert.Equal(0x0003, bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead));
		Assert.Equal(0xFFFF, bus.ReadWord(0x00DFF0A6, ref cycle, AmigaBusAccessKind.CpuDataRead));
		bus.Paula.AdvanceTo(cycle);

		Assert.Equal(3, bus.Paula.GetChannelSnapshot(0).Period);
	}

	[Fact]
	public void TimedDff000ReadReturnsCurrentSharedChipBusLatch()
	{
		var bus = CreatePaulaComponentBus();
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x1234);
		var cycle = 0L;

		Assert.Equal(0x1234, bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead));

		Assert.Equal(0x1234, bus.ReadWord(0x00DFF000, ref cycle, AmigaBusAccessKind.CpuDataRead));
	}

	[Fact]
	public void TimedWriteOnlyByteReadDuplicatesSelectedLatchByteForSideEffect()
	{
		var bus = CreatePaulaComponentBus();
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x1212);
		var cycle = 0L;

		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);
		Assert.Equal(0xFF, bus.ReadByte(0x00DFF0A8, ref cycle, AmigaBusAccessKind.CpuDataRead));
		bus.Paula.AdvanceTo(cycle);

		Assert.Equal(0x12, bus.Paula.GetChannelSnapshot(0).Volume);
	}

	[Fact]
	public void TimedWriteOnlyLongReadAppliesEachWordPhaseIndependently()
	{
		var bus = CreatePaulaComponentBus();
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x0003);
		var cycle = 0L;

		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);
		Assert.Equal(0xFFFF_FFFFu, bus.ReadLong(0x00DFF0A6, ref cycle, AmigaBusAccessKind.CpuDataRead));
		bus.Paula.AdvanceTo(cycle);

		Assert.Equal(3, bus.Paula.GetChannelSnapshot(0).Period);
		Assert.Equal(64, bus.Paula.GetChannelSnapshot(0).Volume);
	}

	[Fact]
	public void WriteOnlyReadReturnsImmediatelyPrecedingDmaBusWord()
	{
		var bus = CreatePaulaComponentBus();
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x0005);
		var dma = bus.ReadChipWordForDeviceWithResult(
			AmigaBusRequester.Blitter,
			AmigaBusAccessKind.Blitter,
			0x1000,
			34);
		var cycle = dma.BusAccess.GrantedCycle + AgnusChipSlotScheduler.SlotCycles;

		var value = bus.ReadWord(0x00DFF0A6, ref cycle, AmigaBusAccessKind.CpuDataRead);
		bus.Paula.AdvanceTo(cycle);

		Assert.Equal(0x0005, value);
		Assert.Equal(5, bus.Paula.GetChannelSnapshot(0).Period);
	}

	[Fact]
	public void TimedWriteOnlyControlReadUsesNormalCustomWriteDispatch()
	{
		var bus = CreatePaulaComponentBus();
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x8201);
		var cycle = 0L;

		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);
		Assert.Equal(0xFFFF, bus.ReadWord(0x00DFF096, ref cycle, AmigaBusAccessKind.CpuDataRead));
		bus.Paula.AdvanceTo(cycle);

		Assert.Equal(0x0201, bus.Paula.Dmacon & 0x0201);
		Assert.True(bus.Paula.GetChannelSnapshot(0).DmaEnabled);
	}

	[Fact]
	public void JitSlotAwareWriteOnlyReadMatchesInterpreterSideEffect()
	{
		var bus = CreatePaulaComponentBus();
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x0005);
		var cycle = 0L;

		Assert.Equal(0x0005u, bus.ReadJitSlotAwareMemory(ref cycle, 0x00001000, M68kOperandSize.Word));
		Assert.Equal(0xFFFFu, bus.ReadJitSlotAwareMemory(ref cycle, 0x00DFF0A6, M68kOperandSize.Word));
		bus.Paula.AdvanceTo(cycle);

		Assert.Equal(5, bus.Paula.GetChannelSnapshot(0).Period);
	}

	[Fact]
	public void HardwareResetRestoresSharedChipBusLatchToAllOnes()
	{
		var bus = CreatePaulaComponentBus();
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x1234);
		var cycle = 0L;
		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);

		bus.ResetExternalDevices(cycle);
		cycle = 0;

		Assert.Equal(0xFFFF, bus.ReadWord(0x00DFF000, ref cycle, AmigaBusAccessKind.CpuDataRead));
	}

	[Fact]
	public void HostCustomReadRemainsNonDestructive()
	{
		var bus = CreatePaulaComponentBus();
		bus.WriteWord(0x00DFF0A6, 0x0007, 0);
		bus.Paula.AdvanceTo(0);

		Assert.Equal(0x0007, bus.ReadWord(0x00DFF0A6));
		Assert.Equal(7, bus.Paula.GetChannelSnapshot(0).Period);
	}

	[Fact]
	public void AudioOnlyRenderLeavesInterruptsForRegisterTimeline()
	{
		var bus = CreatePaulaComponentBus();
		SchedulePaulaWrite(bus, 0x09A, 0xC080, 0);
		SchedulePaulaWrite(bus, 0x0AA, 0x7F81, 0);
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
	public void CustomRegisterReadAdvancesMainPaulaRegisterTimeline()
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

		Assert.NotEqual(0, intreq & 0x0080);
		Assert.True(afterRead.NextSampleCycle >= beforeRead.NextSampleCycle);
		Assert.True((bus.Paula.Intreq & 0x0080) != 0);
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
	public void CustomRegisterReadPerformsDuePaulaDmaFetches()
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

		Assert.True(dmaReadsAfterPoll > 0);
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

		bus.Paula.AdvanceDmaObservableTo(40);
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
	public void ManualAudioDataRewriteAfterLowBoundaryUpdatesHoldingLatchOnly()
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
		Assert.Equal(unchecked((sbyte)0x81), afterRewrite.CurrentSample);
		Assert.Equal(0x4080, afterRewrite.DataLatch);
		Assert.True(afterRewrite.DataLatchWritten);
		Assert.Equal(8, afterRewrite.NextSampleCycle);
	}

	[Fact]
	public void ManualAudioDataCanBeQueuedBeforeLowByteIsPlayed()
	{
		var bus = CreatePaulaComponentBus();
		SchedulePaulaWrite(bus, 0x0A6, 0x0004, 0);
		SchedulePaulaWrite(bus, 0x0AA, 0x7F81, 0);
		SchedulePaulaWrite(bus, 0x09C, 0x0080, 1);
		SchedulePaulaWrite(bus, 0x0AA, 0x4080, 1);
		var buffer = new float[8];

		bus.Paula.RenderSample(0, buffer, 0, 2);
		bus.Paula.RenderSample(8, buffer, 1, 2);
		bus.Paula.RenderSample(16, buffer, 2, 2);
		bus.Paula.RenderSample(24, buffer, 3, 2);

		Assert.True(buffer[0] > 0.20f);
		Assert.True(buffer[2] < -0.20f);
		Assert.True(buffer[4] > 0.10f);
		Assert.True(buffer[6] < -0.20f);
	}

	[Fact]
	public void IntenaGatesAudioInterruptEventsButNotIntreqPolling()
	{
		var bus = CreatePaulaComponentBus();

		bus.WriteWord(0x00DFF0AA, 0x0102, 0);
		bus.Paula.AdvanceTo(1712);
		Assert.Empty(bus.Paula.DrainInterrupts());
		Assert.True((bus.ReadWord(0x00DFF01E) & 0x0080) != 0);

		bus.WriteWord(0x00DFF09C, 0x0080, 1712);
		bus.WriteWord(0x00DFF09A, 0xC080, 1712);
		bus.WriteWord(0x00DFF0AA, 0x0102, 1712);
		bus.Paula.AdvanceTo(1712);

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
		var diagnostic = $"samples={string.Join(',', buffer)} state={snapshot.State} dma={snapshot.DmaEnabled} " +
			$"addr=0x{snapshot.CurrentAddress:X} remain={snapshot.RemainingWords} word=0x{snapshot.DataWord:X4} " +
			$"sample={snapshot.CurrentSample} next={snapshot.NextSampleCycle}";

		Assert.Equal(0.0f, buffer[0]);
		Assert.True(buffer[2] > 0.10f, diagnostic);
		Assert.True(buffer[4] < -0.10f, diagnostic);
		Assert.True(buffer[6] > 0.10f, diagnostic);
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
		SchedulePaulaWrite(bus, 0x0A0, 0x0000, 0);
		SchedulePaulaWrite(bus, 0x0A2, 0x1000, 0);
		SchedulePaulaWrite(bus, 0x0A4, 0x0001, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x0071, 0);
		SchedulePaulaWrite(bus, 0x096, 0x8201, 0);
		bus.Paula.AdvanceTo(0);
		var afterEnable = bus.Paula.GetChannelSnapshot(0);
		var buffer = new float[4];

		bus.Paula.RenderSample(261, buffer, 0, 2);
		bus.Paula.RenderSample(262, buffer, 1, 2);
		var afterBoundary = bus.Paula.GetChannelSnapshot(0);

		Assert.Equal(113, afterEnable.Period);
		Assert.Equal(34, afterEnable.NextSampleCycle);
		Assert.True(buffer[0] > 0.20f);
		Assert.True(buffer[2] < -0.20f);
		Assert.Equal(488, afterBoundary.NextSampleCycle);
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
	public void LiveAudioDmaKeepsChannelSlotsWhileNastyBlitterStallsPendingCpu()
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
		bus.WriteWord(0x00DFF040, 0x0100); // D-only clear.
		bus.WriteWord(0x00DFF054, 0x0000);
		bus.WriteWord(0x00DFF056, 0x4000);
		bus.WriteWord(0x00DFF096, 0x8640); // DMAEN, BLTEN and BLTPRI.
		bus.WriteWord(0x00DFF058, 0x0200, 0); // Eight rows of 64 words.
		var cpuCycle = AudioSlotCycle(0, 0);

		_ = bus.ReadWord(0x00002000, ref cpuCycle, AmigaBusAccessKind.CpuInstructionFetch);
		bus.Paula.AdvanceDmaObservableTo(1_000);

		var firstRequestedCycles = bus.BusAccesses
			.Where(access => access.Request.Kind == AmigaBusAccessKind.PaulaDma)
			.Select(access => access.RequestedCycle)
			.Take(3)
			.ToArray();
		Assert.Equal(
			new[] { AudioSlotCycle(0, 0), AudioSlotCycle(1, 0), AudioSlotCycle(2, 0) },
			firstRequestedCycles);
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
	public void SparseAudioRenderingDoesNotLeaveStaleDmaLatchQueueRecords()
	{
		var bus = CreateMinimumPeriodDmaUnderrunBus();
		var buffer = new float[2];
		var sampleCycle = 0L;
		for (var sample = 0; sample < 2_000; sample++)
		{
			sampleCycle += 160;
			bus.Paula.RenderSample(sampleCycle, buffer, 0, 2, advanceRegisterObservable: false);
		}

		bus.Paula.AdvanceDmaObservableTo(sampleCycle);

		Assert.All(CapturePaulaDmaLatchQueueCounts(bus), count => Assert.InRange(count, 0, 4));
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
	public void DmaLengthReloadInterruptRemainsLatchedWhileIntreqIsPending()
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
		Assert.Equal(new long[] { 34 }, interruptCycles);
		Assert.True(bus.Paula.GetChannelSnapshot(0).DelayedInterruptPending);
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
		Assert.Single(bus.Paula.DrainInterrupts());
		Assert.True(snapshot.DelayedInterruptPending);
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
		SchedulePaulaWrite(bus, 0x09E, 0x8010, 0);
		SchedulePaulaWrite(bus, 0x0A6, 0x0002, 0);
		SchedulePaulaWrite(bus, 0x0AA, 0x0005, 0);

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

	private static int[] CapturePaulaDmaLatchQueueCounts(AmigaBus bus)
	{
		var queuesField = typeof(Paula).GetField(
			"_dmaReadLatchQueues",
			System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
		Assert.NotNull(queuesField);
		var queues = Assert.IsAssignableFrom<Array>(queuesField.GetValue(bus.Paula));
		var counts = new int[queues.Length];
		for (var i = 0; i < counts.Length; i++)
		{
			var queue = queues.GetValue(i);
			Assert.NotNull(queue);
			var countField = queue.GetType().GetField(
				"_count",
				System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			Assert.NotNull(countField);
			counts[i] = Assert.IsType<int>(countField.GetValue(queue));
		}

		return counts;
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
