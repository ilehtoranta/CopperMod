namespace CopperMod.Sid.Tests;

public sealed class SidCycleTraceTests
{
	[Fact]
	public void TraceCapturesForwardedWriteAndGateEdgeOnFirstRenderedCycle()
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);
		var trace = new SidCycleTrace();
		chip.Trace = trace;
		chip.Write(0x05, 0x00);
		chip.Write(0x06, 0xF0);
		chip.Write(0x04, 0x21);

		chip.Render(1);

		var frame = Frame(trace, cycle: 1, voice: 0);
		Assert.True(frame.Events.HasFlag(SidCycleTraceEvents.ForwardedWrite));
		Assert.True(frame.Events.HasFlag(SidCycleTraceEvents.GateRising));
		Assert.Equal(0x21, frame.Control);
		Assert.Equal(0, frame.EnvelopeCounter);
		Assert.Equal(1, frame.RateCounter);
	}

	[Fact]
	public void TraceRecordsAccumulatorBeforeAndAfterEveryCycle()
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);
		var trace = new SidCycleTrace();
		chip.Trace = trace;
		chip.Write(0x00, 0x34);
		chip.Write(0x01, 0x12);

		chip.Render(5);

		var frame = Frame(trace, cycle: 5, voice: 0);
		Assert.Equal(0x1234, frame.Frequency);
		Assert.Equal(0x1234u * 4u, frame.AccumulatorBefore);
		Assert.Equal(0x1234u * 5u, frame.Accumulator);
	}

	[Fact]
	public void TraceMarksTestBitResetAndHoldCycles()
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);
		var trace = new SidCycleTrace();
		chip.Write(0x00, 0x00);
		chip.Write(0x01, 0x80);
		chip.Render(2);
		chip.Trace = trace;
		chip.Write(0x04, 0x28);

		chip.Render(1);

		var resetFrame = Frame(trace, cycle: 3, voice: 0);
		Assert.True(resetFrame.Events.HasFlag(SidCycleTraceEvents.ForwardedWrite));
		Assert.True(resetFrame.Events.HasFlag(SidCycleTraceEvents.TestBitReset));
		Assert.Equal(0u, resetFrame.Accumulator);

		chip.Render(1);

		var holdFrame = Frame(trace, cycle: 4, voice: 0);
		Assert.False(holdFrame.Events.HasFlag(SidCycleTraceEvents.ForwardedWrite));
		Assert.True(holdFrame.Events.HasFlag(SidCycleTraceEvents.TestBitHeld));
		Assert.Equal(0u, holdFrame.Accumulator);
	}

	[Fact]
	public void TraceMarksNoiseShiftOnSecondNoiseShiftPhase()
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);
		var trace = new SidCycleTrace();
		chip.Trace = trace;
		chip.Write(0x00, 0x00);
		chip.Write(0x01, 0x80);

		chip.Render(18);

		var clockRise = Frame(trace, cycle: 16, voice: 0);
		var phase1 = Frame(trace, cycle: 17, voice: 0);
		var shift = Frame(trace, cycle: 18, voice: 0);
		Assert.False(clockRise.Events.HasFlag(SidCycleTraceEvents.NoiseShift));
		Assert.False(phase1.Events.HasFlag(SidCycleTraceEvents.NoiseShift));
		Assert.Equal(0x7FFFF8u, phase1.NoiseShiftRegister);
		Assert.True(shift.Events.HasFlag(SidCycleTraceEvents.NoiseShift));
		Assert.Equal(0x7FFFF8u, shift.NoiseShiftRegisterBefore);
		Assert.Equal(NextNoise(0x7FFFF8u), shift.NoiseShiftRegister);
	}

	[Fact]
	public void TraceMarksSyncResetOnSourceMsbRisingCycle()
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);
		var trace = new SidCycleTrace();
		chip.Trace = trace;
		WriteVoice(chip, voice: 0, frequency: 0x8000, control: 0x20);
		WriteVoice(chip, voice: 1, frequency: 0x8000, control: 0x22);

		chip.Render(256);

		var source = Frame(trace, cycle: 256, voice: 0);
		var target = Frame(trace, cycle: 256, voice: 1);
		Assert.Equal(0x800000u, source.Accumulator);
		Assert.True(target.Events.HasFlag(SidCycleTraceEvents.SyncReset));
		Assert.Equal(0x7F8000u, target.AccumulatorBefore);
		Assert.Equal(0u, target.Accumulator);
	}

	[Fact]
	public void TraceCapturesWaveformAndVoiceOutputBeforeMixer()
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);
		var trace = new SidCycleTrace();
		chip.Trace = trace;
		chip.Write(0x00, 0x00);
		chip.Write(0x01, 0x80);
		chip.Write(0x04, 0x20);

		chip.Render(1);

		var frame = Frame(trace, cycle: 1, voice: 0);
		Assert.Equal(0x20, frame.Waveform);
		Assert.True(double.IsFinite(frame.WaveformOutput));
		Assert.True(double.IsFinite(frame.VoiceOutput));
		Assert.NotEqual(0.0, frame.WaveformOutput);
		Assert.Equal(
			frame.WaveformOutput * SidAnalog.ConvertEnvelope(frame.EnvelopeCounter, SidChipModel.Mos6581),
			frame.VoiceOutput,
			12);
	}

	[Fact]
	public void TraceRecordsAttackEnvelopeStepAtExactRateCycle()
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);
		var trace = new SidCycleTrace();
		chip.Trace = trace;
		chip.Write(0x05, 0x00);
		chip.Write(0x06, 0xF0);
		chip.Write(0x04, 0x11);

		chip.Render(9);

		var beforeStep = Frame(trace, cycle: 8, voice: 0);
		var step = Frame(trace, cycle: 9, voice: 0);
		Assert.False(beforeStep.Events.HasFlag(SidCycleTraceEvents.EnvelopeStep));
		Assert.Equal(0, beforeStep.EnvelopeCounter);
		Assert.Equal(8, beforeStep.RateCounter);
		Assert.True(step.Events.HasFlag(SidCycleTraceEvents.EnvelopeStep));
		Assert.Equal(0, step.EnvelopeCounterBefore);
		Assert.Equal(1, step.EnvelopeCounter);
		Assert.Equal(0, step.RateCounter);
	}

	[Fact]
	public void SidSystemTraceUsesAbsoluteCycleForForwardedVoiceWrites()
	{
		var sid = new SidSystem(new[] { new SidChipPlacement(0, SidConstants.DefaultSidBaseAddress) }, SidChipModel.Mos6581);
		var trace = new SidCycleTrace();
		sid.Trace = trace;

		Assert.True(sid.TryWrite(0xD404, 0x21, 100));
		sid.AdvanceTo(100);

		Assert.DoesNotContain(trace.Frames, frame => frame.Events.HasFlag(SidCycleTraceEvents.ForwardedWrite));

		sid.AdvanceTo(101);

		var forwarded = Frame(trace, cycle: 101, voice: 0);
		Assert.True(forwarded.Events.HasFlag(SidCycleTraceEvents.ForwardedWrite));
		Assert.True(forwarded.Events.HasFlag(SidCycleTraceEvents.GateRising));
		Assert.Equal(0x21, forwarded.Control);
	}

	[Fact]
	public void SidSystemSameCycleWritesForwardOnlyLastValue()
	{
		var sid = new SidSystem(new[] { new SidChipPlacement(0, SidConstants.DefaultSidBaseAddress) }, SidChipModel.Mos6581);
		var trace = new SidCycleTrace();
		sid.Trace = trace;

		Assert.True(sid.TryWrite(0xD404, 0x20, 100));
		Assert.True(sid.TryWrite(0xD404, 0x21, 100));
		sid.AdvanceTo(101);

		Assert.Equal(0x21, sid.Chips[0].DebugState.ForwardedRegisters[0x04]);
		var forwarded = Assert.Single(trace.Frames, frame =>
			frame.Cycle == 101 &&
			frame.VoiceIndex == 0 &&
			frame.Events.HasFlag(SidCycleTraceEvents.ForwardedWrite));
		Assert.Equal(0x21, forwarded.Control);
	}

	[Fact]
	public void RenderSampleDoesNotForwardWriteAtItsEndingCycle()
	{
		var sid = new SidSystem(new[] { new SidChipPlacement(0, SidConstants.DefaultSidBaseAddress) }, SidChipModel.Mos6581);
		var trace = new SidCycleTrace();
		sid.Trace = trace;

		Assert.True(sid.TryWrite(0xD404, 0x21, 100));
		_ = sid.RenderSample(100);

		Assert.DoesNotContain(trace.Frames, frame => frame.Events.HasFlag(SidCycleTraceEvents.ForwardedWrite));
		Assert.Equal(0x21, sid.Chips[0].Registers[0x04]);
		Assert.Equal(0x00, sid.Chips[0].DebugState.ForwardedRegisters[0x04]);

		_ = sid.RenderSample(101);

		var forwarded = Assert.Single(trace.Frames, frame =>
			frame.Cycle == 101 &&
			frame.VoiceIndex == 0 &&
			frame.Events.HasFlag(SidCycleTraceEvents.ForwardedWrite));
		Assert.Equal(0x21, forwarded.Control);
	}

	[Fact]
	public void RenderSampleBoundaryForwardingMatchesDirectCycleAdvance()
	{
		var direct = CreateTracedSid(out var directTrace);
		var sampled = CreateTracedSid(out var sampledTrace);
		Assert.True(direct.TryWrite(0xD404, 0x21, 100));
		Assert.True(sampled.TryWrite(0xD404, 0x21, 100));

		direct.AdvanceTo(101);
		_ = sampled.RenderSample(100);
		_ = sampled.RenderSample(101);

		Assert.Equal([101L], ForwardedCycles(directTrace));
		Assert.Equal(ForwardedCycles(directTrace), ForwardedCycles(sampledTrace));
	}

	private static SidCycleTraceFrame Frame(SidCycleTrace trace, long cycle, int voice)
	{
		return trace.Frames.Single(frame => frame.Cycle == cycle && frame.VoiceIndex == voice);
	}

	private static SidSystem CreateTracedSid(out SidCycleTrace trace)
	{
		var sid = new SidSystem(new[] { new SidChipPlacement(0, SidConstants.DefaultSidBaseAddress) }, SidChipModel.Mos6581);
		trace = new SidCycleTrace();
		sid.Trace = trace;
		return sid;
	}

	private static long[] ForwardedCycles(SidCycleTrace trace)
	{
		return trace.Frames
			.Where(frame => frame.VoiceIndex == 0 && frame.Events.HasFlag(SidCycleTraceEvents.ForwardedWrite))
			.Select(frame => frame.Cycle)
			.ToArray();
	}

	private static void WriteVoice(SidChip chip, int voice, ushort frequency, byte control)
	{
		var offset = voice * 7;
		chip.Write((byte)(offset + 0), (byte)(frequency & 0xFF));
		chip.Write((byte)(offset + 1), (byte)(frequency >> 8));
		chip.Write((byte)(offset + 4), control);
	}

	private static uint NextNoise(uint value)
	{
		var feedback = ((value >> 22) ^ (value >> 17)) & 1;
		return ((value << 1) | feedback) & 0x7FFFFF;
	}
}
