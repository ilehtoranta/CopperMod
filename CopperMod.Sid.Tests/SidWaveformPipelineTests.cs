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
	public void RingModulationUsesPreSyncTargetPhaseOnSourceMsbCycle()
	{
		var chip = CreateTracedChip(out var trace);
		WriteVoice(chip, voice: 0, frequency: 0x8000, control: 0x20);
		WriteVoice(chip, voice: 1, frequency: 0x8000, control: 0x16);

		chip.Render(256);

		var frame = Frame(trace, cycle: 256, voice: 1);
		Assert.True(frame.Events.HasFlag(SidCycleTraceEvents.SyncReset));
		Assert.True(frame.SyncSourceMsb);
		Assert.True(frame.RingModInverted);
		Assert.False(frame.TriangleInverted);
		Assert.Equal(0u, frame.WaveformDac);
	}

	[Fact]
	public void SyncSourceResetSuppressesDownstreamSyncOnSameCycle()
	{
		var chip = CreateTracedChip(out var trace);
		WriteVoice(chip, voice: 0, frequency: 0x8000, control: 0x20);
		WriteVoice(chip, voice: 1, frequency: 0x8000, control: 0x22);
		WriteVoice(chip, voice: 2, frequency: 0x8000, control: 0x22);

		chip.Render(256);

		var firstTarget = Frame(trace, cycle: 256, voice: 1);
		var downstreamTarget = Frame(trace, cycle: 256, voice: 2);
		Assert.True(firstTarget.Events.HasFlag(SidCycleTraceEvents.SyncReset));
		Assert.Equal(0u, firstTarget.Accumulator);
		Assert.False(downstreamTarget.Events.HasFlag(SidCycleTraceEvents.SyncReset));
		Assert.Equal(0x800000u, downstreamTarget.Accumulator);
	}

	[Fact]
	public void RingModulationUsesSourceAccumulatorMsbEvenWhenSourceVoiceIsSilent()
	{
		var chip = CreateTracedChip(out var trace);
		WriteVoice(chip, voice: 0, frequency: 0x8000, control: 0x00);
		WriteVoice(chip, voice: 1, frequency: 0x0000, control: 0x14);

		chip.Render(256);

		var frame = Frame(trace, cycle: 256, voice: 1);
		Assert.True(frame.SyncSourceMsb);
		Assert.True(frame.RingModInverted);
		Assert.True(frame.TriangleInverted);
		Assert.Equal(0u, frame.Accumulator);
		Assert.Equal(0xFFEu, frame.WaveformDac);
	}

	[Fact]
	public void WaveformZeroHoldsPreviousDacOutputBeforeFloatingDecay()
	{
		var chip = CreateTracedChip(SidChipModel.Mos8580, out var trace);
		WriteVoice(chip, voice: 0, frequency: 0x8000, control: 0x20);
		chip.Render(256);

		chip.Write(0x04, 0x00);
		chip.Render(106);

		var active = Frame(trace, cycle: 256, voice: 0);
		var firstFloating = Frame(trace, cycle: 257, voice: 0);
		var laterFloating = Frame(trace, cycle: 362, voice: 0);
		Assert.True(firstFloating.Events.HasFlag(SidCycleTraceEvents.ForwardedWrite));
		Assert.Equal(0x20, active.Waveform);
		Assert.Equal(0x800u, active.WaveformDac);
		Assert.Equal(0x00, firstFloating.Waveform);
		Assert.Equal(active.WaveformDac, firstFloating.WaveformDac);
		Assert.Equal(active.WaveformOutput, firstFloating.WaveformOutput);
		Assert.Equal(active.WaveformDac, laterFloating.WaveformDac);
		Assert.Equal(active.WaveformOutput, laterFloating.WaveformOutput);
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
		Assert.Equal(0xFFEu, triangleAtHalf.WaveformDac);
		Assert.True(triangleAtHalf.TriangleInverted);
	}

	[Fact]
	public void TriangleDacUsesElevenEffectiveBitsShiftedIntoTwelveBitRange()
	{
		var chip = CreateTracedChip(out var trace);
		WriteVoice(chip, voice: 0, frequency: 0x8000, control: 0x10);

		chip.Render(512);

		var triangleFrames = trace.Frames.Where(frame => frame.VoiceIndex == 0).ToArray();
		Assert.All(triangleFrames, frame => Assert.Equal(0u, frame.WaveformDac & 1u));
		Assert.Contains(triangleFrames, frame => frame.WaveformDac == 0xFFEu);
		Assert.DoesNotContain(triangleFrames, frame => frame.WaveformDac == 0xFFFu);
	}

	[Fact]
	public void PulseComparatorRisesOneCycleAfterTopAccumulatorBitsReachPulseWidth()
	{
		var chip = CreateTracedChip(out var trace);
		chip.Write(0x00, 0x00);
		chip.Write(0x01, 0x10);
		chip.Write(0x02, 0x02);
		chip.Write(0x03, 0x00);
		chip.Write(0x04, 0x40);

		chip.Render(4);

		var before = Frame(trace, cycle: 1, voice: 0);
		var crossing = Frame(trace, cycle: 2, voice: 0);
		var delayed = Frame(trace, cycle: 3, voice: 0);
		var held = Frame(trace, cycle: 4, voice: 0);
		Assert.False(before.PulseHigh);
		Assert.Equal(0u, before.WaveformDac);
		Assert.False(crossing.PulseHigh);
		Assert.Equal(0u, crossing.WaveformDac);
		Assert.True(delayed.PulseHigh);
		Assert.Equal(0xFFFu, delayed.WaveformDac);
		Assert.True(held.PulseHigh);
		Assert.Equal(0xFFFu, held.WaveformDac);
	}

	[Fact]
	public void PulseWidthWriteAffectsWaveformOutputOneCycleAfterForwardedCycle()
	{
		var chip = CreateTracedChip(out var trace);
		chip.Write(0x00, 0x00);
		chip.Write(0x01, 0x10);
		chip.Write(0x02, 0x04);
		chip.Write(0x03, 0x00);
		chip.Write(0x04, 0x40);
		chip.Render(1);

		chip.Write(0x02, 0x01);
		chip.Render(2);

		var forwarded = Frame(trace, cycle: 2, voice: 0);
		var delayed = Frame(trace, cycle: 3, voice: 0);
		Assert.True(forwarded.Events.HasFlag(SidCycleTraceEvents.ForwardedWrite));
		Assert.False(forwarded.PulseHigh);
		Assert.Equal(0u, forwarded.WaveformDac);
		Assert.Equal((ushort)0x001, forwarded.PulseWidth);
		Assert.True(delayed.PulseHigh);
		Assert.Equal((ushort)0x001, delayed.PulseWidth);
		Assert.Equal(0xFFFu, delayed.WaveformDac);
	}

	[Fact]
	public void TestBitForcesPulseWaveformHighImmediately()
	{
		var chip = CreateTracedChip(out var trace);
		chip.Write(0x00, 0x00);
		chip.Write(0x01, 0x10);
		chip.Write(0x02, 0xFF);
		chip.Write(0x03, 0x0F);
		chip.Write(0x04, 0x48);

		chip.Render(1);

		var frame = Frame(trace, cycle: 1, voice: 0);
		Assert.True(
			frame.Events.HasFlag(SidCycleTraceEvents.TestBitReset) ||
			frame.Events.HasFlag(SidCycleTraceEvents.TestBitHeld));
		Assert.True(frame.PulseHigh);
		Assert.Equal(0xFFFu, frame.WaveformDac);
	}

	[Fact]
	public void ClearingTestBitKeepsPulseHighForOneLatchedCycle()
	{
		var chip = CreateTracedChip(out var trace);
		chip.Write(0x00, 0x00);
		chip.Write(0x01, 0x10);
		chip.Write(0x02, 0xFF);
		chip.Write(0x03, 0x0F);
		chip.Write(0x04, 0x48);
		chip.Render(2);

		chip.Write(0x04, 0x40);
		chip.Render(2);

		var release = Frame(trace, cycle: 3, voice: 0);
		var delayed = Frame(trace, cycle: 4, voice: 0);
		Assert.True(release.Events.HasFlag(SidCycleTraceEvents.ForwardedWrite));
		Assert.False(release.Events.HasFlag(SidCycleTraceEvents.TestBitHeld));
		Assert.Equal(0x1000u, release.Accumulator);
		Assert.True(release.PulseHigh);
		Assert.Equal(0xFFFu, release.WaveformDac);
		Assert.False(delayed.PulseHigh);
		Assert.Equal(0u, delayed.WaveformDac);
	}

	[Fact]
	public void NoiseWaveformUsesPostShiftRegisterOnSecondNoiseShiftPhase()
	{
		var chip = CreateTracedChip(out var trace);
		WriteVoice(chip, voice: 0, frequency: 0x8000, control: 0x80);

		chip.Render(18);

		var phase1 = Frame(trace, cycle: 17, voice: 0);
		var frame = Frame(trace, cycle: 18, voice: 0);
		Assert.False(phase1.Events.HasFlag(SidCycleTraceEvents.NoiseShift));
		Assert.Equal(ExpectedNoiseDac(0x7FFFF8u), phase1.WaveformDac);
		Assert.True(frame.Events.HasFlag(SidCycleTraceEvents.NoiseShift));
		Assert.True(frame.NoiseUsesPostShiftRegister);
		Assert.Equal(0x7FFFF8u, frame.NoiseShiftRegisterBefore);
		Assert.Equal(NextNoise(0x7FFFF8u), frame.NoiseShiftRegister);
		Assert.Equal(ExpectedNoiseDac(frame.NoiseShiftRegister), frame.WaveformDac);
	}

	[Fact]
	public void Mos6581NoiseCombinedWithOtherWaveformsPullsNoiseOutputBitsLow()
	{
		var chip = CreateTracedChip(out var trace);
		WriteVoice(chip, voice: 0, frequency: 0x8000, control: 0xA0);

		chip.Render(1);

		var frame = Frame(trace, cycle: 1, voice: 0);
		Assert.Equal(0xA0, frame.Waveform);
		Assert.True(frame.Events.HasFlag(SidCycleTraceEvents.NoiseWriteback));
		Assert.Equal(0x2ED768u, frame.NoiseShiftRegister);
		Assert.Equal(0u, frame.NoiseDac);
		Assert.Equal(0u, frame.WaveformDac);
		Assert.True(frame.NoiseUsesPostShiftRegister);
		Assert.InRange(frame.WaveformOutput, -0.01, 0.01);
	}

	[Fact]
	public void Mos6581CombinedWaveformsUseContentionTable()
	{
		var chip = CreateTracedChip(out var trace);
		WriteVoice(chip, voice: 0, frequency: 0x3000, control: 0x30);

		chip.Render(1);

		var frame = Frame(trace, cycle: 1, voice: 0);
		Assert.Equal(0x30, frame.Waveform);
		Assert.True(frame.WaveformDac <= 0x0FFFu);
		Assert.NotEqual(0x002u, frame.WaveformDac);
		Assert.NotEqual(
			SidAnalog.ConvertWaveformDac12(0x002u, SidChipModel.Mos6581) *
				SidAnalog.CombinedWaveformScale(2, SidChipModel.Mos6581),
			frame.WaveformOutput,
			precision: 12);
	}

	[Fact]
	public void Mos6581CombinedWaveformMapperUsesSeparateSourceDacs()
	{
		var collapsedOnly = SidAnalog.MapCombinedWaveformDac12(0x800u, 0x30, SidChipModel.Mos6581);
		var sourceAware = SidAnalog.MapCombinedWaveformDac12(
			triangleDac: 0xFFEu,
			sawDac: 0x800u,
			pulseDac: 0u,
			noiseDac: 0u,
			waveformMask: 0x30,
			model: SidChipModel.Mos6581,
			out var activeWaveforms);

		Assert.Equal(2, activeWaveforms);
		Assert.NotEqual(collapsedOnly, sourceAware);
		Assert.Equal(0u, sourceAware & ~0x0FFFu);
	}

	[Theory]
	[InlineData(0x60)]
	[InlineData(0x70)]
	public void Mos6581PulseCombinedWithOtherWaveformsUsesContentionTable(byte control)
	{
		var chip = CreateTracedChip(out var trace);
		chip.Write(0x00, 0x00);
		chip.Write(0x01, 0x30);
		chip.Write(0x02, 0x01);
		chip.Write(0x03, 0x00);
		chip.Write(0x04, control);

		chip.Render(2);

		var frame = Frame(trace, cycle: 2, voice: 0);
		Assert.Equal(control & 0xF0, frame.Waveform);
		Assert.True(frame.PulseHigh);
		Assert.True(frame.WaveformDac <= 0x0FFFu);
		Assert.True(double.IsFinite(frame.WaveformOutput));
	}

	[Fact]
	public void Mos6581SawPulseSelectionKeepsOnlyWeakSourceAwareResidue()
	{
		var mapped = SidAnalog.MapCombinedWaveformDac12(
			triangleDac: 0,
			sawDac: 0x800,
			pulseDac: 0xFFF,
			noiseDac: 0,
			waveformMask: 0x60,
			model: SidChipModel.Mos6581,
			out var activeWaveforms);
		var mos8580 = SidAnalog.MapCombinedWaveformDac12(
			triangleDac: 0,
			sawDac: 0x800,
			pulseDac: 0xFFF,
			noiseDac: 0,
			waveformMask: 0x60,
			model: SidChipModel.Mos8580,
			out _);

		Assert.Equal(2, activeWaveforms);
		Assert.InRange(mapped, 0u, 0x01FFu);
		Assert.True(mapped < mos8580, $"Expected 6581 selector contention to suppress saw+pulse below 8580 bitwise value, 6581 ${mapped:X3}, 8580 ${mos8580:X3}.");
	}

	[Fact]
	public void Mos6581TriangleSawPulseSelectionCollapsesMaskedResidue()
	{
		var collapsedOnly = SidAnalog.MapCombinedWaveformDac12(0x800u, 0x70, SidChipModel.Mos6581);
		var sourceAware = SidAnalog.MapCombinedWaveformDac12(
			triangleDac: 0xFFE,
			sawDac: 0x800,
			pulseDac: 0xFFF,
			noiseDac: 0,
			waveformMask: 0x70,
			model: SidChipModel.Mos6581,
			out var activeWaveforms);

		Assert.Equal(3, activeWaveforms);
		Assert.InRange(sourceAware, 0u, 0x00FFu);
		Assert.True(sourceAware < collapsedOnly, $"Expected 6581 triangle+saw+pulse selector residue to collapse below the already-mapped value, source-aware ${sourceAware:X3}, collapsed ${collapsedOnly:X3}.");
	}

	[Fact]
	public void Mos6581NoiseCombinedSelectionDoesNotBleedPulledDownSourceResidue()
	{
		var mapped = SidAnalog.MapCombinedWaveformDac12(
			triangleDac: 0,
			sawDac: 0x800,
			pulseDac: 0,
			noiseDac: 0,
			waveformMask: 0xA0,
			model: SidChipModel.Mos6581,
			out var activeWaveforms);

		Assert.Equal(2, activeWaveforms);
		Assert.Equal(0u, mapped);
		Assert.InRange(SidAnalog.ConvertCombinedWaveformDac12(mapped, 0xA0, SidChipModel.Mos6581), -0.01, 0.01);
	}

	[Fact]
	public void Mos8580CombinedWaveformsKeepBitwiseDacCombination()
	{
		var chip = CreateTracedChip(SidChipModel.Mos8580, out var trace);
		WriteVoice(chip, voice: 0, frequency: 0x3000, control: 0x30);

		chip.Render(1);

		var frame = Frame(trace, cycle: 1, voice: 0);
		Assert.Equal(0x30, frame.Waveform);
		Assert.Equal(0x002u, frame.WaveformDac);
	}

	[Fact]
	public void Mos8580ReferenceCombinedNoiseWritesPulledDownBitsBack()
	{
		var chip = new SidChip(
			SidChipModel.Mos8580,
			0xD400,
			sidEmulationProfile: SidEmulationProfile.ReferenceMeasured);
		var trace = new SidCycleTrace();
		chip.Trace = trace;
		WriteVoice(chip, voice: 0, frequency: 0x4000, control: 0xA0);

		chip.Render(1);

		var frame = Frame(trace, cycle: 1, voice: 0);
		Assert.True(frame.Events.HasFlag(SidCycleTraceEvents.NoiseWriteback));
		Assert.NotEqual(0x7FFFF8u, frame.NoiseShiftRegister);
	}

	[Fact]
	public void Mos6581TrianglePulseTraceUsesTriangleDacWhenPulseIsHigh()
	{
		var chip = CreateTracedChip(out var trace);
		chip.Write(0x00, 0x00);
		chip.Write(0x01, 0x70);
		chip.Write(0x02, 0x01);
		chip.Write(0x03, 0x00);
		chip.Write(0x04, 0x50);

		chip.Render(2);

		var frame = Frame(trace, cycle: 2, voice: 0);
		Assert.Equal(0x50, frame.Waveform);
		Assert.True(frame.PulseHigh);
		Assert.Equal(0x01Cu, frame.WaveformDac);
	}

	[Fact]
	public void Mos6581TrianglePulseGateOffUsesSofterContentionBias()
	{
		var gateOff = CreateTrianglePulseVoice(control: 0x50, out var gateOffTrace);
		var gateOn = CreateTrianglePulseVoice(control: 0x51, out var gateOnTrace);

		gateOff.Render(1);
		gateOn.Render(1);
		var gateOffWaveform = Frame(gateOffTrace, cycle: 1, voice: 0).WaveformOutput;
		var gateOnWaveform = Frame(gateOnTrace, cycle: 1, voice: 0).WaveformOutput;

		Assert.True(gateOffWaveform > gateOnWaveform);
		Assert.InRange(gateOffWaveform - gateOnWaveform, 0.54, 0.57);
	}

	private static SidChip CreateTracedChip(out SidCycleTrace trace)
	{
		return CreateTracedChip(SidChipModel.Mos6581, out trace);
	}

	private static SidChip CreateTracedChip(SidChipModel model, out SidCycleTrace trace)
	{
		var chip = new SidChip(model, 0xD400);
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

	private static SidChip CreateTrianglePulseVoice(byte control, out SidCycleTrace trace)
	{
		var chip = CreateTracedChip(out trace);
		chip.Write(0x00, 0x00);
		chip.Write(0x01, 0x70);
		chip.Write(0x02, 0x00);
		chip.Write(0x03, 0x00);
		chip.Write(0x05, 0x00);
		chip.Write(0x06, 0xF0);
		chip.Write(0x04, control);
		chip.Write(0x18, 0x0F);
		return chip;
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
