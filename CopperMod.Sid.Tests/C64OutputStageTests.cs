using CopperMod.Rendering;

namespace CopperMod.Sid.Tests;

public sealed class C64OutputStageTests
{
	private const int SampleRate = 96000;
	private const double LegacyDcBlockCutoffHz = 1.30;
	private const double LegacyOutputLowPassCutoffHz = 24000.0;
	private const float LegacyOutputHeadroom = 1.04f;

	[Fact]
	public void CleanProfileLeavesSamplesUnchanged()
	{
		float[] samples = { -0.75f, -0.1f, 0.0f, 0.25f, 0.9f };
		var expected = samples.ToArray();

		new C64OutputStage(C64OutputProfile.Clean).Process(samples, channels: 1, SampleRate);

		Assert.Equal(expected, samples);
	}

	[Theory]
	[InlineData("step", 1)]
	[InlineData("sine", 1)]
	[InlineData("multi-channel", 2)]
	public void C64ProfileMatchesLegacyOutputPath(string fixture, int channels)
	{
		var samples = CreateFixture(fixture);
		var expected = ProcessLegacyC64Path(samples, channels, SampleRate);

		new C64OutputStage(C64OutputProfile.C64).Process(samples, channels, SampleRate);

		AssertEqualBits(expected, samples);
	}

	[Fact]
	public void C64MeasuredProfilePreservesAcPulsePolarity()
	{
		var positive = CreatePulse(0.80f);
		var negative = CreatePulse(-0.80f);

		new C64OutputStage(C64OutputProfile.C64Measured).Process(positive, channels: 1, SampleRate);
		new C64OutputStage(C64OutputProfile.C64Measured).Process(negative, channels: 1, SampleRate);

		Assert.True(positive[16] > 0.0f, $"Expected positive pulse to remain positive, got {positive[16]:0.000000}.");
		Assert.True(negative[16] < 0.0f, $"Expected negative pulse to remain negative, got {negative[16]:0.000000}.");
		for (var i = 0; i < positive.Length; i++)
		{
			Assert.Equal(positive[i], -negative[i], precision: 7);
		}
	}

	[Fact]
	public void C64MeasuredProfileCompressesHighLevelInputMoreThanLowLevelInput()
	{
		var low = ProcessMeasuredImpulse(0.25f);
		var high = ProcessMeasuredImpulse(1.00f);

		var lowRatio = Math.Abs(low[0]) / 0.25f;
		var highRatio = Math.Abs(high[0]) / 1.00f;

		Assert.True(low[0] > 0.0f);
		Assert.True(high[0] > low[0]);
		Assert.True(
			highRatio < lowRatio,
			$"Expected measured board profile to compress high levels more, low ratio {lowRatio:0.000000}, high ratio {highRatio:0.000000}.");
	}

	[Fact]
	public void C64MeasuredProfileConvertsDcStepToDecayingAcTransient()
	{
		var samples = new float[SampleRate / 2];
		const int stepIndex = 16;
		for (var i = stepIndex; i < samples.Length; i++)
		{
			samples[i] = 0.60f;
		}

		new C64OutputStage(C64OutputProfile.C64Measured).Process(samples, channels: 1, SampleRate);

		var transient = Math.Abs(samples[stepIndex]);
		var tail = Math.Abs(samples[^1]);
		Assert.True(samples[stepIndex] > 0.30f, $"Expected DC step to create positive AC transient, got {samples[stepIndex]:0.000000}.");
		Assert.True(tail < transient * 0.10f, $"Expected AC transient to decay, transient {transient:0.000000}, tail {tail:0.000000}.");
	}

	[Fact]
	public void ResetClearsC64OutputStageState()
	{
		var stage = new C64OutputStage(C64OutputProfile.C64Measured);
		var first = CreateFixture("sine");
		var second = CreateFixture("sine");

		stage.Process(first, channels: 1, SampleRate);
		stage.Reset();
		stage.Process(second, channels: 1, SampleRate);

		AssertEqualBits(first, second);
	}

	private static float[] ProcessMeasuredImpulse(float amplitude)
	{
		var samples = new[] { amplitude, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f };
		new C64OutputStage(C64OutputProfile.C64Measured).Process(samples, channels: 1, SampleRate);
		return samples;
	}

	private static float[] CreatePulse(float amplitude)
	{
		var samples = new float[64];
		samples[16] = amplitude;
		return samples;
	}

	private static float[] CreateFixture(string fixture)
	{
		return fixture switch
		{
			"step" => CreateStepFixture(),
			"sine" => CreateSineFixture(),
			"multi-channel" => CreateMultiChannelFixture(),
			_ => throw new ArgumentOutOfRangeException(nameof(fixture), fixture, "Unknown fixture.")
		};
	}

	private static float[] CreateStepFixture()
	{
		var samples = new float[128];
		for (var i = 32; i < samples.Length; i++)
		{
			samples[i] = 0.45f;
		}

		return samples;
	}

	private static float[] CreateSineFixture()
	{
		var samples = new float[256];
		for (var i = 0; i < samples.Length; i++)
		{
			samples[i] = (float)(Math.Sin(i * 0.17) * 0.55);
		}

		return samples;
	}

	private static float[] CreateMultiChannelFixture()
	{
		var samples = new float[256];
		for (var frame = 0; frame < samples.Length / 2; frame++)
		{
			samples[frame * 2] = frame < 32 ? 0.0f : 0.35f;
			samples[(frame * 2) + 1] = (float)(Math.Cos(frame * 0.11) * 0.42);
		}

		return samples;
	}

	private static float[] ProcessLegacyC64Path(float[] source, int channels, int sampleRate)
	{
		var samples = source.ToArray();
		var lowPassState = new float[channels];
		var dcPreviousInput = new float[channels];
		var dcPreviousOutput = new float[channels];
		var lowPassAlpha = 1.0 - Math.Exp(-2.0 * Math.PI * LegacyOutputLowPassCutoffHz / sampleRate);
		var highPassAlpha = GetHighPassAlpha(LegacyDcBlockCutoffHz, sampleRate);
		for (var i = 0; i < samples.Length; i++)
		{
			var channel = i % channels;
			var lowPassOutput = lowPassState[channel] + ((samples[i] - lowPassState[channel]) * (float)lowPassAlpha);
			lowPassState[channel] = lowPassOutput;
			var highPassOutput = (float)(highPassAlpha * (dcPreviousOutput[channel] + lowPassOutput - dcPreviousInput[channel]));
			dcPreviousInput[channel] = lowPassOutput;
			dcPreviousOutput[channel] = highPassOutput;
			samples[i] = Math.Clamp(highPassOutput * LegacyOutputHeadroom, -1.0f, 1.0f);
		}

		return samples;
	}

	private static double GetHighPassAlpha(double cutoffHz, int sampleRate)
	{
		var rc = 1.0 / (2.0 * Math.PI * cutoffHz);
		var dt = 1.0 / sampleRate;
		return rc / (rc + dt);
	}

	private static void AssertEqualBits(float[] expected, float[] actual)
	{
		Assert.Equal(expected.Length, actual.Length);
		for (var i = 0; i < expected.Length; i++)
		{
			Assert.True(
				BitConverter.SingleToInt32Bits(expected[i]) == BitConverter.SingleToInt32Bits(actual[i]),
				$"Sample {i} differed: expected {expected[i]:0.000000000}, actual {actual[i]:0.000000000}.");
		}
	}
}
