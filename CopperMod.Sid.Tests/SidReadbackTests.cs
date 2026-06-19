namespace CopperMod.Sid.Tests;

public sealed class SidReadbackTests
{
	[Fact]
	public void OscillatorThreeReadUsesOneCycleDelayedWaveformLatch()
	{
		var sid = CreateSid();
		Assert.True(sid.TryWrite(0xD40E, 0x00, 0));
		Assert.True(sid.TryWrite(0xD40F, 0x80, 0));
		Assert.True(sid.TryWrite(0xD412, 0x20, 0));

		Assert.True(sid.TryRead(0xD41B, cycle: 2, out var delayed));
		Assert.True(sid.TryRead(0xD41B, cycle: 3, out var caughtUp));

		Assert.Equal(0x00, delayed);
		Assert.Equal(0x01, caughtUp);
		Assert.Equal(0x00018000u, sid.GetRegisterChipDebugState(0).Voices[2].Accumulator);
	}

	[Fact]
	public void OscillatorThreeReadbackDelayDoesNotDelayAudioOrTraceWaveform()
	{
		var sid = CreateSid();
		var trace = new SidCycleTrace();
		sid.Trace = trace;
		Assert.True(sid.TryWrite(0xD40E, 0x00, 0));
		Assert.True(sid.TryWrite(0xD40F, 0x80, 0));
		Assert.True(sid.TryWrite(0xD412, 0x20, 0));

		Assert.True(sid.TryRead(0xD41B, cycle: 2, out var readback));

		var frame = trace.Frames.Single(frame => frame.Cycle == 2 && frame.VoiceIndex == 2);
		Assert.Equal(0x010u, frame.WaveformDac);
		Assert.Equal(0x00, readback);
	}

	[Fact]
	public void RepeatedOscillatorThreeReadUsesStableLatchWithoutAdvancing()
	{
		var sid = CreateSid();
		Assert.True(sid.TryWrite(0xD40E, 0x00, 0));
		Assert.True(sid.TryWrite(0xD40F, 0x80, 0));
		Assert.True(sid.TryWrite(0xD412, 0x20, 0));

		Assert.True(sid.TryRead(0xD41B, cycle: 3, out var first));
		Assert.True(sid.TryRead(0xD41B, cycle: 3, out var second));

		Assert.Equal(0x01, first);
		Assert.Equal(first, second);
	}

	[Fact]
	public void OscillatorThreeReadDoesNotApplyNoiseWriteback()
	{
		var sid = CreateSid();
		Assert.True(sid.TryWrite(0xD40E, 0x00, 0));
		Assert.True(sid.TryWrite(0xD40F, 0x80, 0));
		Assert.True(sid.TryWrite(0xD412, 0xA0, 0));
		sid.AdvanceTo(1);
		var before = sid.Chips[0].DebugState.Voices[2].NoiseShiftRegister;

		Assert.True(sid.TryRead(0xD41B, cycle: 1, out _));

		Assert.Equal(before, sid.Chips[0].DebugState.Voices[2].NoiseShiftRegister);
	}

	[Fact]
	public void OscillatorThreeReadbackFollowsTwoPhaseNoiseShiftWithoutAdvancingAudio()
	{
		var sid = CreateSid();
		Assert.True(sid.TryWrite(0xD40E, 0x00, 0));
		Assert.True(sid.TryWrite(0xD40F, 0x80, 0));
		Assert.True(sid.TryWrite(0xD412, 0x80, 0));
		var timingBefore = sid.CaptureTimingSnapshot();
		var chipBefore = sid.Chips[0].DebugState;

		Assert.True(sid.TryRead(0xD41B, cycle: 17, out _));
		Assert.Equal(0x7FFFF8u, sid.GetRegisterChipDebugState(0).Voices[2].NoiseShiftRegister);

		Assert.True(sid.TryRead(0xD41B, cycle: 18, out _));

		Assert.Equal(NextNoise(0x7FFFF8), sid.GetRegisterChipDebugState(0).Voices[2].NoiseShiftRegister);
		var timingAfter = sid.CaptureTimingSnapshot();
		Assert.Equal(timingBefore.AudioCycle, timingAfter.AudioCycle);
		Assert.Equal(timingBefore.SampleCycles, timingAfter.SampleCycles);
		Assert.Equal(timingBefore.SampleAccumulator, timingAfter.SampleAccumulator);
		AssertSidChipDebugStateEqual(chipBefore, sid.Chips[0].DebugState);
	}

	[Fact]
	public void OscillatorThreeLatchMatchesFastAndTracedSidSystems()
	{
		var fast = CreateSid();
		var traced = CreateSid();
		traced.Trace = new SidCycleTrace();
		foreach (var sid in new[] { fast, traced })
		{
			Assert.True(sid.TryWrite(0xD40E, 0x00, 0));
			Assert.True(sid.TryWrite(0xD40F, 0x80, 0));
			Assert.True(sid.TryWrite(0xD412, 0x20, 0));
			sid.AdvanceTo(8);
		}

		Assert.True(fast.TryRead(0xD41B, cycle: 8, out var fastRead));
		Assert.True(traced.TryRead(0xD41B, cycle: 8, out var tracedRead));

		Assert.Equal(fastRead, tracedRead);
		Assert.Equal(fast.Chips[0].DebugState.Voices[2].Accumulator, traced.Chips[0].DebugState.Voices[2].Accumulator);
	}

	[Fact]
	public void EnvelopeThreeReadUsesCurrentEnvelopeAtReadCycle()
	{
		var sid = CreateSid();
		Assert.True(sid.TryWrite(0xD413, 0x00, 0));
		Assert.True(sid.TryWrite(0xD414, 0xF0, 0));
		Assert.True(sid.TryWrite(0xD412, 0x11, 0));

		Assert.True(sid.TryRead(0xD41C, cycle: 8, out var beforeStep));
		Assert.True(sid.TryRead(0xD41C, cycle: 9, out var step));

		Assert.Equal(0x00, beforeStep);
		Assert.Equal(0x01, step);
	}

	[Fact]
	public void ReadOnlyPotRegistersReturnUnconnectedHighAndRefreshOpenBus()
	{
		var sid = CreateSid();
		Assert.True(sid.TryWrite(0xD400, 0x55, 0));

		Assert.True(sid.TryRead(0xD400, out var writtenBus));
		Assert.True(sid.TryRead(0xD419, out var potX));
		Assert.True(sid.TryRead(0xD401, out var openBusAfterPot));

		Assert.Equal(0x55, writtenBus);
		Assert.Equal(0xFF, potX);
		Assert.Equal(0xFF, openBusAfterPot);
	}

	[Fact]
	public void PotAndOpenBusFutureReadsDoNotAdvanceMainAudioTimeline()
	{
		var sid = CreateSid();
		Assert.True(sid.TryWrite(0xD418, 0x0F, 100));
		var timingBefore = sid.CaptureTimingSnapshot();
		var chipBefore = sid.Chips[0].DebugState;

		Assert.True(sid.TryRead(0xD418, cycle: 100, out var openBus));
		Assert.True(sid.TryRead(0xD419, cycle: 100, out var potX));
		Assert.True(sid.TryRead(0xD400, cycle: 100, out var openBusAfterPot));

		Assert.Equal(0x0F, openBus);
		Assert.Equal(0xFF, potX);
		Assert.Equal(0xFF, openBusAfterPot);
		var timingAfter = sid.CaptureTimingSnapshot();
		Assert.Equal(timingBefore.AudioCycle, timingAfter.AudioCycle);
		Assert.Equal(timingBefore.SampleCycles, timingAfter.SampleCycles);
		Assert.Equal(timingBefore.SampleAccumulator, timingAfter.SampleAccumulator);
		Assert.Equal(timingBefore.ChannelCaptureFrameIndex, timingAfter.ChannelCaptureFrameIndex);
		Assert.Equal(0, timingAfter.RegisterCycle);
		AssertSidChipDebugStateEqual(chipBefore, sid.Chips[0].DebugState);
	}

	[Fact]
	public void OpenBusKeepsLastWriteForShortDelayAndDecaysAfterTtl()
	{
		var shortDelay = CreateSid();
		Assert.True(shortDelay.TryWrite(0xD418, 0x5A, 0));

		Assert.True(shortDelay.TryRead(0xD400, cycle: SidChip.OpenBusDecayCycles - 1, out var retained));

		Assert.Equal(0x5A, retained);

		var expired = CreateSid();
		Assert.True(expired.TryWrite(0xD418, 0x5A, 0));

		Assert.True(expired.TryRead(0xD400, cycle: SidChip.OpenBusDecayCycles, out var decayed));

		Assert.Equal(0x00, decayed);
	}

	[Fact]
	public void OpenBusReadRefreshesDecayTimerWithReturnedValue()
	{
		var sid = CreateSid();
		Assert.True(sid.TryWrite(0xD418, 0x3C, 0));

		Assert.True(sid.TryRead(0xD400, cycle: SidChip.OpenBusDecayCycles - 1, out var first));
		Assert.True(sid.TryRead(0xD401, cycle: (SidChip.OpenBusDecayCycles * 2) - 2, out var second));

		Assert.Equal(0x3C, first);
		Assert.Equal(0x3C, second);
	}

	[Fact]
	public void PotReadRefreshesOpenBusUntilDecayExpires()
	{
		var sid = CreateSid();
		Assert.True(sid.TryWrite(0xD418, 0x44, 0));

		Assert.True(sid.TryRead(0xD419, cycle: 10, out var pot));
		Assert.True(sid.TryRead(0xD400, cycle: 11, out var refreshed));
		Assert.True(sid.TryRead(0xD401, cycle: 11 + SidChip.OpenBusDecayCycles, out var decayed));

		Assert.Equal(0xFF, pot);
		Assert.Equal(0xFF, refreshed);
		Assert.Equal(0x00, decayed);
	}

	[Fact]
	public void OscillatorAndEnvelopeReadsRefreshOpenBus()
	{
		var sid = CreateSid();
		Assert.True(sid.TryWrite(0xD40E, 0x00, 0));
		Assert.True(sid.TryWrite(0xD40F, 0x80, 0));
		Assert.True(sid.TryWrite(0xD412, 0x20, 0));
		Assert.True(sid.TryWrite(0xD413, 0x00, 0));
		Assert.True(sid.TryWrite(0xD414, 0xF0, 0));

		Assert.True(sid.TryRead(0xD41B, cycle: 3, out var oscillator));
		Assert.True(sid.TryRead(0xD400, cycle: 3, out var busAfterOscillator));
		Assert.True(sid.TryWrite(0xD412, 0x21, 4));
		Assert.True(sid.TryRead(0xD41C, cycle: 13, out var envelope));
		Assert.True(sid.TryRead(0xD401, cycle: 13, out var busAfterEnvelope));

		Assert.Equal(0x01, oscillator);
		Assert.Equal(oscillator, busAfterOscillator);
		Assert.Equal(0x01, envelope);
		Assert.Equal(envelope, busAfterEnvelope);
	}

	[Fact]
	public void FutureOpenBusDecayReadDoesNotAdvanceMainAudioTimeline()
	{
		var sid = CreateSid();
		Assert.True(sid.TryWrite(0xD418, 0x7E, 0));
		var timingBefore = sid.CaptureTimingSnapshot();
		var chipBefore = sid.Chips[0].DebugState;

		Assert.True(sid.TryRead(0xD400, cycle: SidChip.OpenBusDecayCycles, out var openBus));

		Assert.Equal(0x00, openBus);
		var timingAfter = sid.CaptureTimingSnapshot();
		Assert.Equal(timingBefore.AudioCycle, timingAfter.AudioCycle);
		Assert.Equal(timingBefore.SampleCycles, timingAfter.SampleCycles);
		Assert.Equal(timingBefore.SampleAccumulator, timingAfter.SampleAccumulator);
		AssertSidChipDebugStateEqual(chipBefore, sid.Chips[0].DebugState);
	}

	[Fact]
	public void OscillatorThreeFutureReadAdvancesRegisterTimelineOnly()
	{
		var sid = CreateSid();
		var reference = CreateSid();
		foreach (var target in new[] { sid, reference })
		{
			Assert.True(target.TryWrite(0xD40E, 0x00, 0));
			Assert.True(target.TryWrite(0xD40F, 0x80, 0));
			Assert.True(target.TryWrite(0xD412, 0x20, 0));
		}

		var timingBefore = sid.CaptureTimingSnapshot();
		var chipBefore = sid.Chips[0].DebugState;

		Assert.True(sid.TryRead(0xD41B, cycle: 3, out var actual));
		reference.AdvanceTo(3);
		Assert.True(reference.TryRead(0xD41B, cycle: 3, out var expected));

		Assert.Equal(expected, actual);
		var timingAfter = sid.CaptureTimingSnapshot();
		Assert.Equal(timingBefore.AudioCycle, timingAfter.AudioCycle);
		Assert.Equal(timingBefore.SampleCycles, timingAfter.SampleCycles);
		Assert.Equal(timingBefore.SampleAccumulator, timingAfter.SampleAccumulator);
		Assert.Equal(3, timingAfter.RegisterCycle);
		AssertSidChipDebugStateEqual(chipBefore, sid.Chips[0].DebugState);
		Assert.Equal(
			reference.Chips[0].DebugState.Voices[2].Accumulator,
			sid.GetRegisterChipDebugState(0).Voices[2].Accumulator);
	}

	[Fact]
	public void DigitalReadbackPollingDoesNotChangeLaterAudio()
	{
		var polled = CreateSid();
		var baseline = CreateSid();
		ScheduleFuturePulse(polled, 100);
		ScheduleFuturePulse(baseline, 100);

		Assert.True(polled.TryRead(0xD41B, cycle: 120, out _));

		var polledSample = polled.RenderSample(120);
		var baselineSample = baseline.RenderSample(120);

		Assert.Equal(baselineSample, polledSample);
		AssertSidChipDebugStateEqual(baseline.Chips[0].DebugState, polled.Chips[0].DebugState);
	}

	[Fact]
	public void OpenBusPollingDoesNotChangeLaterAudio()
	{
		var polled = CreateSid();
		var baseline = CreateSid();
		ScheduleFuturePulse(polled, 100);
		ScheduleFuturePulse(baseline, 100);

		Assert.True(polled.TryRead(0xD418, cycle: 100, out var openBus));
		Assert.Equal(0x0F, openBus);

		var polledSample = polled.RenderSample(120);
		var baselineSample = baseline.RenderSample(120);

		Assert.Equal(baselineSample, polledSample);
		AssertSidChipDebugStateEqual(baseline.Chips[0].DebugState, polled.Chips[0].DebugState);
	}

	[Fact]
	public void PendingWritesAreCompactedOnlyAfterAudioAndReadbackConsumeThem()
	{
		var sid = CreateSid();
		for (var i = 1; i <= 80; i++)
		{
			Assert.True(sid.TryWrite(0xD400, (byte)i, i));
		}

		Assert.True(sid.TryRead(0xD400, cycle: 80, out var openBus));
		Assert.Equal(80, openBus);
		var afterBusRead = sid.CaptureTimingSnapshot();
		Assert.Equal(80, afterBusRead.PendingWriteCount);
		Assert.Equal(0, afterBusRead.AudioPendingWriteIndex);
		Assert.Equal(0, afterBusRead.RegisterPendingWriteIndex);
		Assert.Equal(80, afterBusRead.RegisterBusPendingWriteIndex);

		_ = sid.RenderSample(40);
		var beforeAudioCatchup = sid.CaptureTimingSnapshot();
		Assert.Equal(80, beforeAudioCatchup.PendingWriteCount);
		Assert.Equal(40, beforeAudioCatchup.AudioPendingWriteIndex);
		Assert.Equal(0, beforeAudioCatchup.RegisterPendingWriteIndex);
		Assert.Equal(80, beforeAudioCatchup.RegisterBusPendingWriteIndex);

		_ = sid.RenderSample(80);
		var afterAudioCatchup = sid.CaptureTimingSnapshot();
		Assert.Equal(0, afterAudioCatchup.PendingWriteCount);
		Assert.Equal(0, afterAudioCatchup.AudioPendingWriteIndex);
		Assert.Equal(0, afterAudioCatchup.RegisterPendingWriteIndex);
		Assert.Equal(0, afterAudioCatchup.RegisterBusPendingWriteIndex);
	}

	[Fact]
	public void PendingWritesAreCompactedAfterAudioCatchesDigitalReadback()
	{
		var sid = CreateSid();
		for (var i = 1; i <= 80; i++)
		{
			Assert.True(sid.TryWrite(0xD400, (byte)i, i));
		}

		Assert.True(sid.TryRead(0xD41C, cycle: 80, out _));
		var afterDigitalRead = sid.CaptureTimingSnapshot();
		Assert.Equal(80, afterDigitalRead.PendingWriteCount);
		Assert.Equal(0, afterDigitalRead.AudioPendingWriteIndex);
		Assert.Equal(80, afterDigitalRead.RegisterPendingWriteIndex);
		Assert.Equal(80, afterDigitalRead.RegisterBusPendingWriteIndex);

		_ = sid.RenderSample(40);
		var beforeAudioCatchup = sid.CaptureTimingSnapshot();
		Assert.Equal(80, beforeAudioCatchup.PendingWriteCount);
		Assert.Equal(40, beforeAudioCatchup.AudioPendingWriteIndex);
		Assert.Equal(80, beforeAudioCatchup.RegisterPendingWriteIndex);
		Assert.Equal(80, beforeAudioCatchup.RegisterBusPendingWriteIndex);

		_ = sid.RenderSample(80);
		var afterAudioCatchup = sid.CaptureTimingSnapshot();
		Assert.Equal(0, afterAudioCatchup.PendingWriteCount);
		Assert.Equal(0, afterAudioCatchup.AudioPendingWriteIndex);
		Assert.Equal(0, afterAudioCatchup.RegisterPendingWriteIndex);
		Assert.Equal(0, afterAudioCatchup.RegisterBusPendingWriteIndex);
	}

	[Fact]
	public void PendingWritesCompactWhenOnlyAudioConsumesThem()
	{
		var sid = CreateSid();
		for (var i = 1; i <= 80; i++)
		{
			Assert.True(sid.TryWrite(0xD400, (byte)i, i));
		}

		_ = sid.RenderSample(80);
		var afterAudio = sid.CaptureTimingSnapshot();
		Assert.Equal(0, afterAudio.PendingWriteCount);
		Assert.Equal(0, afterAudio.AudioPendingWriteIndex);
		Assert.Equal(0, afterAudio.RegisterPendingWriteIndex);
		Assert.Equal(0, afterAudio.RegisterBusPendingWriteIndex);
	}

	[Fact]
	public void MirroredDefaultSidReadUsesSameReadbackRegister()
	{
		var sid = CreateSid();
		Assert.True(sid.TryWrite(0xD40E, 0x00, 0));
		Assert.True(sid.TryWrite(0xD40F, 0x80, 0));
		Assert.True(sid.TryWrite(0xD412, 0x20, 0));

		Assert.True(sid.TryRead(0xD43B, cycle: 2, out var mirroredOscillator));

		Assert.Equal(0x00, mirroredOscillator);
	}

	private static SidSystem CreateSid()
	{
		return new SidSystem(new[] { new SidChipPlacement(0, SidConstants.DefaultSidBaseAddress) }, SidChipModel.Mos6581);
	}

	private static void ScheduleFuturePulse(SidSystem sid, long cycle)
	{
		Assert.True(sid.TryWrite(0xD400, 0x00, cycle));
		Assert.True(sid.TryWrite(0xD401, 0x10, cycle));
		Assert.True(sid.TryWrite(0xD402, 0x00, cycle));
		Assert.True(sid.TryWrite(0xD403, 0x08, cycle));
		Assert.True(sid.TryWrite(0xD405, 0x00, cycle));
		Assert.True(sid.TryWrite(0xD406, 0xF0, cycle));
		Assert.True(sid.TryWrite(0xD404, 0x41, cycle));
		Assert.True(sid.TryWrite(0xD418, 0x0F, cycle));
	}

	private static uint NextNoise(uint value)
	{
		var feedback = ((value >> 22) ^ (value >> 17)) & 1;
		return ((value << 1) | feedback) & 0x7FFFFF;
	}

	private static void AssertSidChipDebugStateEqual(SidChipDebugState expected, SidChipDebugState actual)
	{
		Assert.Equal(expected.ForwardedRegisters, actual.ForwardedRegisters);
		Assert.Equal(expected.FilterProfile, actual.FilterProfile);
		Assert.Equal(expected.FilterCutoffRegister, actual.FilterCutoffRegister);
		Assert.Equal(expected.FilterCutoffHz, actual.FilterCutoffHz);
		Assert.Equal(expected.FilterResonanceNibble, actual.FilterResonanceNibble);
		Assert.Equal(expected.FilterMode, actual.FilterMode);
		Assert.Equal(expected.FilterDamping, actual.FilterDamping);
		Assert.Equal(expected.LowPassOutput, actual.LowPassOutput);
		Assert.Equal(expected.BandPassOutput, actual.BandPassOutput);
		Assert.Equal(expected.HighPassOutput, actual.HighPassOutput);
		Assert.Equal(expected.Voices.Length, actual.Voices.Length);
		for (var i = 0; i < expected.Voices.Length; i++)
		{
			AssertSidVoiceDebugStateEqual(expected.Voices[i], actual.Voices[i]);
		}
	}

	private static void AssertSidVoiceDebugStateEqual(SidVoiceDebugState expected, SidVoiceDebugState actual)
	{
		Assert.Equal(expected.Accumulator, actual.Accumulator);
		Assert.Equal(expected.NoiseShiftRegister, actual.NoiseShiftRegister);
		Assert.Equal(expected.NoiseDac, actual.NoiseDac);
		Assert.Equal(expected.EnvelopeCounter, actual.EnvelopeCounter);
		Assert.Equal(expected.RateCounter, actual.RateCounter);
		Assert.Equal(expected.ExponentialCounter, actual.ExponentialCounter);
		Assert.Equal(expected.EnvelopeState, actual.EnvelopeState);
		Assert.Equal(expected.Control, actual.Control);
	}
}
