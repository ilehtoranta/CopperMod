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
		Assert.Equal(0x00018000u, sid.Chips[0].DebugState.Voices[2].Accumulator);
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
}
