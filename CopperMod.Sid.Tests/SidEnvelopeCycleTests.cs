using System.Reflection;

namespace CopperMod.Sid.Tests;

public sealed class SidEnvelopeCycleTests
{
	private const int Attack = 0;
	private const int Decay = 1;
	private const int Sustain = 2;
	private const int Release = 3;
	private static readonly int[] RatePeriods =
	[
		9, 32, 63, 95, 149, 220, 267, 313,
		392, 977, 1954, 3126, 3907, 11720, 19532, 31251
	];

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(8)]
	[InlineData(15)]
	public void AttackRatePeriodStepsOnExactCycle(int attackNibble)
	{
		var chip = CreateVoice(attackDecay: (byte)(attackNibble << 4), sustainRelease: 0xF0, control: 0x11);
		var period = RatePeriods[attackNibble];

		chip.Render(period - 1);

		Assert.Equal(0, chip.DebugState.Voices[0].EnvelopeCounter);
		Assert.Equal(period - 1, chip.DebugState.Voices[0].RateCounter);

		chip.Render(1);

		Assert.Equal(1, chip.DebugState.Voices[0].EnvelopeCounter);
		Assert.Equal(0, chip.DebugState.Voices[0].RateCounter);
	}

	[Fact]
	public void AttackToDecayUsesDecayPeriodAfterCounterReachesMaximum()
	{
		var chip = CreateVoice(attackDecay: 0x00, sustainRelease: 0x00, control: 0x11);

		chip.Render(RatePeriods[0] * 255);

		Assert.Equal(0xFF, chip.DebugState.Voices[0].EnvelopeCounter);
		Assert.Equal(Decay, chip.DebugState.Voices[0].EnvelopeState);
		Assert.Equal(0, chip.DebugState.Voices[0].RateCounter);

		chip.Render(RatePeriods[0] - 1);

		Assert.Equal(0xFF, chip.DebugState.Voices[0].EnvelopeCounter);

		chip.Render(1);

		Assert.Equal(0xFE, chip.DebugState.Voices[0].EnvelopeCounter);
		Assert.Equal(Decay, chip.DebugState.Voices[0].EnvelopeState);
	}

	[Fact]
	public void GateFallingDoesNotResetRateCounterBeforeReleaseStep()
	{
		var chip = CreateVoice(attackDecay: 0x00, sustainRelease: 0x00, control: 0x11);
		chip.Render(RatePeriods[0] * 255);
		chip.Render(4);

		Assert.Equal(4, chip.DebugState.Voices[0].RateCounter);

		chip.Write(0x04, 0x10);
		chip.Render(4);

		Assert.Equal(0xFF, chip.DebugState.Voices[0].EnvelopeCounter);
		Assert.Equal(8, chip.DebugState.Voices[0].RateCounter);
		Assert.Equal(Release, chip.DebugState.Voices[0].EnvelopeState);

		chip.Render(1);

		Assert.Equal(0xFE, chip.DebugState.Voices[0].EnvelopeCounter);
		Assert.Equal(0, chip.DebugState.Voices[0].RateCounter);
	}

	[Fact]
	public void FasterRateWriteWaitsForFifteenBitRateCounterWrap()
	{
		var chip = CreateVoice(attackDecay: 0xF0, sustainRelease: 0xF0, control: 0x11);
		chip.Render(20);

		Assert.Equal(20, chip.DebugState.Voices[0].RateCounter);

		chip.Write(0x05, 0x00);
		chip.Render(32756);

		Assert.Equal(0, chip.DebugState.Voices[0].EnvelopeCounter);
		Assert.Equal(8, chip.DebugState.Voices[0].RateCounter);

		chip.Render(1);

		Assert.Equal(1, chip.DebugState.Voices[0].EnvelopeCounter);
		Assert.Equal(0, chip.DebugState.Voices[0].RateCounter);
	}

	[Fact]
	public void SustainHoldStillAdvancesRateCounter()
	{
		var chip = CreateVoice(attackDecay: 0x00, sustainRelease: 0xE0, control: 0x11);
		RunToSustain(chip);

		Assert.Equal(0xEE, chip.DebugState.Voices[0].EnvelopeCounter);
		Assert.Equal(Sustain, chip.DebugState.Voices[0].EnvelopeState);
		Assert.Equal(0, chip.DebugState.Voices[0].RateCounter);

		chip.Render(4);

		Assert.Equal(0xEE, chip.DebugState.Voices[0].EnvelopeCounter);
		Assert.Equal(4, chip.DebugState.Voices[0].RateCounter);

		chip.Write(0x04, 0x10);
		chip.Render(4);

		Assert.Equal(0xEE, chip.DebugState.Voices[0].EnvelopeCounter);
		Assert.Equal(8, chip.DebugState.Voices[0].RateCounter);

		chip.Render(1);

		Assert.Equal(0xED, chip.DebugState.Voices[0].EnvelopeCounter);
		Assert.Equal(0, chip.DebugState.Voices[0].RateCounter);
	}

	[Fact]
	public void LoweringSustainLevelRestartsDecayOnNextDecayMatch()
	{
		var chip = CreateVoice(attackDecay: 0x00, sustainRelease: 0xE0, control: 0x11);
		RunToSustain(chip);

		chip.Write(0x06, 0xD0);
		chip.Render(RatePeriods[0] - 1);

		Assert.Equal(0xEE, chip.DebugState.Voices[0].EnvelopeCounter);
		Assert.Equal(8, chip.DebugState.Voices[0].RateCounter);

		chip.Render(1);

		Assert.Equal(0xED, chip.DebugState.Voices[0].EnvelopeCounter);
		Assert.Equal(Decay, chip.DebugState.Voices[0].EnvelopeState);
		Assert.Equal(0, chip.DebugState.Voices[0].RateCounter);
	}

	[Fact]
	public void ReleaseExponentialCounterChangesPeriodAtThresholds()
	{
		var chip = CreateVoice(attackDecay: 0x00, sustainRelease: 0x00, control: 0x10);
		SetEnvelope(chip, envelope: 0x5E, state: Release);

		chip.Render(RatePeriods[0]);

		Assert.Equal(0x5D, chip.DebugState.Voices[0].EnvelopeCounter);
		Assert.Equal(0, chip.DebugState.Voices[0].ExponentialCounter);

		chip.Render(RatePeriods[0]);

		Assert.Equal(0x5D, chip.DebugState.Voices[0].EnvelopeCounter);
		Assert.Equal(1, chip.DebugState.Voices[0].ExponentialCounter);

		chip.Render(RatePeriods[0]);

		Assert.Equal(0x5C, chip.DebugState.Voices[0].EnvelopeCounter);
		Assert.Equal(0, chip.DebugState.Voices[0].ExponentialCounter);
	}

	[Fact]
	public void ReleaseStopsAtZeroWithoutUnderflow()
	{
		var chip = CreateVoice(attackDecay: 0x00, sustainRelease: 0x00, control: 0x10);
		SetEnvelope(chip, envelope: 1, state: Release);

		chip.Render(RatePeriods[0] * 31);

		Assert.Equal(0, chip.DebugState.Voices[0].EnvelopeCounter);

		chip.Render(RatePeriods[0] * 4);

		Assert.Equal(0, chip.DebugState.Voices[0].EnvelopeCounter);
		Assert.Equal(Release, chip.DebugState.Voices[0].EnvelopeState);
	}

	private static SidChip CreateVoice(byte attackDecay, byte sustainRelease, byte control)
	{
		var chip = new SidChip(SidChipModel.Mos6581, 0xD400);
		chip.Write(0x05, attackDecay);
		chip.Write(0x06, sustainRelease);
		chip.Write(0x04, control);
		return chip;
	}

	private static void RunToSustain(SidChip chip)
	{
		chip.Render((RatePeriods[0] * 255) + (RatePeriods[0] * 17));
	}

	private static void SetEnvelope(SidChip chip, int envelope, int state)
	{
		var voice = GetVoice(chip);
		SetField(voice, "_envelopeCounter", envelope);
		SetField(voice, "_envelopeState", state);
		SetField(voice, "_rateCounter", 0);
		SetField(voice, "_exponentialCounter", 0);
	}

	private static SidVoice GetVoice(SidChip chip)
	{
		var voices = (SidVoice[])typeof(SidChip)
			.GetField("_voices", BindingFlags.Instance | BindingFlags.NonPublic)!
			.GetValue(chip)!;
		return voices[0];
	}

	private static void SetField(SidVoice voice, string name, object value)
	{
		typeof(SidVoice)
			.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
			.SetValue(voice, value);
	}
}
