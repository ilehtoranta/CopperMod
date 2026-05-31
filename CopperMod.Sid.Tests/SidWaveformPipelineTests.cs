namespace CopperMod.Sid.Tests;

public sealed class SidWaveformPipelineTests
{
	[Fact]
	public void AccumulatorAdvancesBeforeWaveformOutputIsSampled()
	{
		var chip = CreateTracedChip(out var trace);
		WriteVoice(chip, voice: 0, frequency: 0x1000, control: 0x20);

		chip.Render(1);

		var frame = Frame(trace, cycle: 1, voice: 0);
		Assert.Equal(0u, frame.AccumulatorBefore);
		Assert.Equal(0x1000u, frame.Accumulator);
		Assert.Equal(0x001u, frame.WaveformDac);
	}

	[Fact]
	public void ForwardedWaveformWritesAffectOutputOnTheSameSidCycle()
	{
		var sid = CreateTracedSid(out var trace);
		Assert.True(sid.TryWrite(0xD400, 0x00, 0));
		Assert.True(sid.TryWrite(0xD401, 0x10, 0));
		Assert.True(sid.TryWrite(0xD404, 0x20, 0));

		sid.AdvanceTo(1);

		var frame = Frame(trace, cycle: 1, voice: 0);
		Assert.True(frame.Events.HasFlag(SidCycleTraceEvents.ForwardedWrite));
		Assert.Equal(0x20, frame.Waveform);
		Assert.Equal(0x1000u, frame.Accumulator);
		Assert.Equal(0x001u, frame.WaveformDac);
	}

	[Fact]
	public void PhaseEdgesAreVisibleFromTraceBeforeAndAfterAccumulators()
	{
		var chip = CreateTracedChip(out var trace);
		WriteVoice(chip, voice: 0, frequency: 0x8000, control: 0x20);

		chip.Render(512);

		var bit19 = Frame(trace, cycle: 16, voice: 0);
		var msb = Frame(trace, cycle: 256, voice: 0);
		var wrap = Frame(trace, cycle: 512, voice: 0);
		Assert.True(SidVoice.NoiseClockRising(bit19.AccumulatorBefore, bit19.Accumulator));
		Assert.True(SidVoice.MsbRising(msb.AccumulatorBefore, msb.Accumulator));
		Assert.True(wrap.Accumulator < wrap.AccumulatorBefore);
		Assert.Equal(0u, wrap.Accumulator);
		Assert.Equal(0u, wrap.WaveformDac);
	}

	[Fact]
	public void ClearingTestBitResumesOscillatorAndWaveformOnThatCycle()
	{
		var chip = CreateTracedChip(out var trace);
		chip.Write(0x00, 0x00);
		chip.Write(0x01, 0x10);
		chip.Write(0x04, 0x28);
		chip.Render(2);

		chip.Write(0x04, 0x20);
		chip.Render(1);

		var frame = Frame(trace, cycle: 3, voice: 0);
		Assert.True(frame.Events.HasFlag(SidCycleTraceEvents.ForwardedWrite));
		Assert.False(frame.Events.HasFlag(SidCycleTraceEvents.TestBitHeld));
		Assert.Equal(0x1000u, frame.Accumulator);
		Assert.Equal(0x001u, frame.WaveformDac);
	}

	[Theory]
	[InlineData(0, 2)]
	[InlineData(1, 0)]
	[InlineData(2, 1)]
	public void SyncResetsEachVoiceFromItsDocumentedSourceAfterMsbRising(int targetVoice, int sourceVoice)
	{
		var chip = CreateTracedChip(out var trace);
		WriteVoice(chip, sourceVoice, 0x8000, 0x20);
		WriteVoice(chip, targetVoice, 0x8000, 0x22);

		chip.Render(256);

		var source = Frame(trace, cycle: 256, voice: sourceVoice);
		var target = Frame(trace, cycle: 256, voice: targetVoice);
		Assert.True(SidVoice.MsbRising(source.AccumulatorBefore, source.Accumulator));
		Assert.Equal(0x800000u, source.Accumulator);
		Assert.True(target.Events.HasFlag(SidCycleTraceEvents.SyncReset));
		Assert.Equal(0x7F8000u, target.AccumulatorBefore);
		Assert.Equal(0u, target.Accumulator);
	}

	[Fact]
	public void RingModulationUsesSourceMsbAfterSourceAdvanceEvenWhenTargetSyncs()
	{
		var chip = CreateTracedChip(out var trace);
		WriteVoice(chip, voice: 0, frequency: 0x8000, control: 0x20);
		WriteVoice(chip, voice: 1, frequency: 0x8000, control: 0x16);

		chip.Render(256);

		var frame = Frame(trace, cycle: 256, voice: 1);
		Assert.True(frame.Events.HasFlag(SidCycleTraceEvents.SyncReset));
		Assert.True(frame.SyncSourceMsb);
		Assert.True(frame.RingModInverted);
		Assert.True(frame.TriangleInverted);
		Assert.Equal(0xFFFu, frame.WaveformDac);
	}

	[Fact]
	public void SawAndTriangleDacsUseAdvancedAccumulatorBitsAroundHalfCycle()
	{
		var saw = CreateTracedChip(out var sawTrace);
		WriteVoice(saw, voice: 0, frequency: 0x8000, control: 0x20);
		var triangle = CreateTracedChip(out var triangleTrace);
		WriteVoice(triangle, voice: 0, frequency: 0x8000, control: 0x10);

		saw.Render(256);
		triangle.Render(256);

		var sawFrame = Frame(sawTrace, cycle: 256, voice: 0);
		var triangleBeforeHalf = Frame(triangleTrace, cycle: 255, voice: 0);
		var triangleAtHalf = Frame(triangleTrace, cycle: 256, voice: 0);
		Assert.Equal(0x800u, sawFrame.WaveformDac);
		Assert.Equal(0xFF0u, triangleBeforeHalf.WaveformDac);
		Assert.False(triangleBeforeHalf.TriangleInverted);
		Assert.Equal(0xFFFu, triangleAtHalf.WaveformDac);
		Assert.True(triangleAtHalf.TriangleInverted);
	}

	[Fact]
	public void PulseComparatorTogglesWhenTopAccumulatorBitsReachPulseWidth()
	{
		var chip = CreateTracedChip(out var trace);
		chip.Write(0x00, 0x00);
		chip.Write(0x01, 0x10);
		chip.Write(0x02, 0x02);
		chip.Write(0x03, 0x00);
		chip.Write(0x04, 0x40);

		chip.Render(2);

		var before = Frame(trace, cycle: 1, voice: 0);
		var crossing = Frame(trace, cycle: 2, voice: 0);
		Assert.False(before.PulseHigh);
		Assert.Equal(0u, before.WaveformDac);
		Assert.True(crossing.PulseHigh);
		Assert.Equal(0xFFFu, crossing.WaveformDac);
	}

	[Fact]
	public void PulseWidthWriteAffectsComparatorBeforeWaveformOutputOnForwardedCycle()
	{
		var chip = CreateTracedChip(out var trace);
		chip.Write(0x00, 0x00);
		chip.Write(0x01, 0x10);
		chip.Write(0x02, 0x04);
		chip.Write(0x03, 0x00);
		chip.Write(0x04, 0x40);
		chip.Render(1);

		chip.Write(0x02, 0x01);
		chip.Render(1);

		var frame = Frame(trace, cycle: 2, voice: 0);
		Assert.True(frame.Events.HasFlag(SidCycleTraceEvents.ForwardedWrite));
		Assert.True(frame.PulseHigh);
		Assert.Equal((ushort)0x001, frame.PulseWidth);
		Assert.Equal(0xFFFu, frame.WaveformDac);
	}

	[Fact]
	public void NoiseWaveformUsesPostShiftRegisterOnBitNineteenRisingCycle()
	{
		var chip = CreateTracedChip(out var trace);
		WriteVoice(chip, voice: 0, frequency: 0x8000, control: 0x80);

		chip.Render(16);

		var frame = Frame(trace, cycle: 16, voice: 0);
		Assert.True(frame.Events.HasFlag(SidCycleTraceEvents.NoiseShift));
		Assert.True(frame.NoiseUsesPostShiftRegister);
		Assert.Equal(NextNoise(0x7FFFF8u), frame.NoiseShiftRegister);
		Assert.Equal(ExpectedNoiseDac(frame.NoiseShiftRegister), frame.WaveformDac);
	}

	[Fact]
	public void CombinedWaveformsTraceBitwiseDacCombination()
	{
		var chip = CreateTracedChip(out var trace);
		WriteVoice(chip, voice: 0, frequency: 0x3000, control: 0x30);

		chip.Render(1);

		var frame = Frame(trace, cycle: 1, voice: 0);
		Assert.Equal(0x30, frame.Waveform);
		Assert.Equal(0x002u, frame.WaveformDac);
	}

	[Fact]
	public void Mos6581TrianglePulseTraceUsesContentionDacForTiming()
	{
		var chip = CreateTracedChip(out var trace);
		chip.Write(0x00, 0x00);
		chip.Write(0x01, 0x70);
		chip.Write(0x02, 0x00);
		chip.Write(0x03, 0x00);
		chip.Write(0x04, 0x50);

		chip.Render(1);

		var frame = Frame(trace, cycle: 1, voice: 0);
		Assert.Equal(0x50, frame.Waveform);
		Assert.True(frame.PulseHigh);
		Assert.Equal(0x00Fu, frame.WaveformDac);
	}

	private static SidChip CreateTracedChip(out SidCycleTrace trace)
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);
		trace = new SidCycleTrace();
		chip.Trace = trace;
		return chip;
	}

	private static SidSystem CreateTracedSid(out SidCycleTrace trace)
	{
		var sid = new SidSystem(new[] { new SidChipPlacement(0, SidConstants.DefaultSidBaseAddress) }, SidChipModel.Mos6581);
		trace = new SidCycleTrace();
		sid.Trace = trace;
		return sid;
	}

	private static SidCycleTraceFrame Frame(SidCycleTrace trace, long cycle, int voice)
	{
		return trace.Frames.Single(frame => frame.Cycle == cycle && frame.VoiceIndex == voice);
	}

	private static void WriteVoice(SidChip chip, int voice, ushort frequency, byte control)
	{
		var offset = voice * 7;
		chip.Write((byte)(offset + 0), (byte)(frequency & 0xFF));
		chip.Write((byte)(offset + 1), (byte)(frequency >> 8));
		chip.Write((byte)(offset + 4), control);
	}

	private static uint ExpectedNoiseDac(uint value)
	{
		var dac = 0u;
		dac |= ((value >> 22) & 1u) << 11;
		dac |= ((value >> 20) & 1u) << 10;
		dac |= ((value >> 16) & 1u) << 9;
		dac |= ((value >> 13) & 1u) << 8;
		dac |= ((value >> 11) & 1u) << 7;
		dac |= ((value >> 7) & 1u) << 6;
		dac |= ((value >> 4) & 1u) << 5;
		dac |= ((value >> 2) & 1u) << 4;
		return dac;
	}

	private static uint NextNoise(uint value)
	{
		var feedback = ((value >> 22) ^ (value >> 17)) & 1;
		return ((value << 1) | feedback) & 0x7FFFFF;
	}
}
