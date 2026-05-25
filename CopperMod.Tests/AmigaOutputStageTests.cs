namespace CopperMod.Tests;

public sealed class AmigaOutputStageTests
{
	[Fact]
	public void NoneProfileLeavesSamplesUntouched()
	{
		var stage = new AmigaOutputStage(AmigaOutputProfile.None);
		var samples = new[] { -0.5f, 0.25f, 0.75f, -0.125f };
		var original = samples.ToArray();

		stage.Process(samples, channels: 2, sampleRate: 44100);

		Assert.Equal(original, samples);
	}

	[Fact]
	public void A500ProfileAttenuatesHighFrequencyContent()
	{
		var stage = new AmigaOutputStage(AmigaOutputProfile.A500);
		var samples = new float[2048];
		for (var i = 0; i < samples.Length; i++)
		{
			samples[i] = (i & 1) == 0 ? 0.75f : -0.75f;
		}

		var before = AverageAbsolute(samples);
		stage.Process(samples, channels: 1, sampleRate: 44100);
		var after = AverageAbsolute(samples);

		Assert.True(after < before * 0.75f);
	}

	[Fact]
	public void A500LedFilterIsDarkerThanA500Profile()
	{
		var a500 = new AmigaOutputStage(AmigaOutputProfile.A500);
		var led = new AmigaOutputStage(AmigaOutputProfile.A500LedFilter);
		var source = new float[2048];
		for (var i = 0; i < source.Length; i++)
		{
			source[i] = (i & 1) == 0 ? 0.75f : -0.75f;
		}

		var regular = source.ToArray();
		var ledFiltered = source.ToArray();
		a500.Process(regular, channels: 1, sampleRate: 44100);
		led.Process(ledFiltered, channels: 1, sampleRate: 44100);

		Assert.True(AverageAbsolute(ledFiltered) < AverageAbsolute(regular));
	}

	[Fact]
	public void A500ProfileUsesLedFilterWhenHardwareFilterIsEnabled()
	{
		var filterOff = new AmigaOutputStage(AmigaOutputProfile.A500);
		var filterOn = new AmigaOutputStage(AmigaOutputProfile.A500);
		var source = new float[2048];
		for (var i = 0; i < source.Length; i++)
		{
			source[i] = (i & 1) == 0 ? 0.75f : -0.75f;
		}

		var regular = source.ToArray();
		var ledFiltered = source.ToArray();
		filterOff.Process(regular, channels: 1, sampleRate: 44100, audioFilterEnabled: false);
		filterOn.Process(ledFiltered, channels: 1, sampleRate: 44100, audioFilterEnabled: true);

		Assert.True(AverageAbsolute(ledFiltered) < AverageAbsolute(regular));
	}

	[Fact]
	public void ForcedLedProfileIgnoresHardwareFilterOffState()
	{
		var auto = new AmigaOutputStage(AmigaOutputProfile.A500);
		var forcedLed = new AmigaOutputStage(AmigaOutputProfile.A500LedFilter);
		var source = new float[2048];
		for (var i = 0; i < source.Length; i++)
		{
			source[i] = (i & 1) == 0 ? 0.75f : -0.75f;
		}

		var regular = source.ToArray();
		var ledFiltered = source.ToArray();
		auto.Process(regular, channels: 1, sampleRate: 44100, audioFilterEnabled: false);
		forcedLed.Process(ledFiltered, channels: 1, sampleRate: 44100, audioFilterEnabled: false);

		Assert.True(AverageAbsolute(ledFiltered) < AverageAbsolute(regular));
	}

	[Fact]
	public void A500ProfileNarrowsHardStereoSlightly()
	{
		var stage = new AmigaOutputStage(AmigaOutputProfile.A500);
		var samples = new[] { 0.5f, -0.5f };

		stage.Process(samples, channels: 2, sampleRate: 44100);

		Assert.True(samples[0] < 0.5f);
		Assert.True(samples[1] > -0.5f);
	}

	private static double AverageAbsolute(float[] samples)
	{
		var sum = 0.0;
		for (var i = 0; i < samples.Length; i++)
		{
			sum += Math.Abs(samples[i]);
		}

		return sum / samples.Length;
	}
}
