namespace CopperMod.Rendering;

public sealed class AmigaOutputStage
{
	private const double DcBlockCutoffHz = 18.0;
	private const double A500LowPassCutoffHz = 6200.0;
	private const double LedLowPassCutoffHz = 3300.0;
	private const double ButterworthQ = 0.7071067811865476; // 1/sqrt(2)
	private const float A500StereoSeparation = 0.92f;

	private float[] _lowPassState = Array.Empty<float>();
	private float[] _dcPreviousInput = Array.Empty<float>();
	private float[] _dcPreviousOutput = Array.Empty<float>();

	// Biquad state for 2-pole Butterworth LED filter: x[n-1], x[n-2], y[n-1], y[n-2] per channel.
	private float[] _ledX1 = Array.Empty<float>();
	private float[] _ledX2 = Array.Empty<float>();
	private float[] _ledY1 = Array.Empty<float>();
	private float[] _ledY2 = Array.Empty<float>();

	public AmigaOutputStage(AmigaOutputProfile profile = AmigaOutputProfile.None)
	{
		Profile = profile;
	}

	public AmigaOutputProfile Profile { get; set; }

	public void Reset()
	{
		Array.Clear(_lowPassState);
		Array.Clear(_dcPreviousInput);
		Array.Clear(_dcPreviousOutput);
		Array.Clear(_ledX1);
		Array.Clear(_ledX2);
		Array.Clear(_ledY1);
		Array.Clear(_ledY2);
	}

	public void Process(Span<float> samples, int channels, int sampleRate, bool audioFilterEnabled = false)
	{
		if (Profile == AmigaOutputProfile.None || samples.Length == 0)
		{
			return;
		}

		if (channels <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(channels), channels, "Channel count must be positive.");
		}

		if (sampleRate <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
		}

		EnsureState(channels);
		var lowPassAlpha = GetLowPassAlpha(A500LowPassCutoffHz, sampleRate);
		var ledFilterEnabled = Profile == AmigaOutputProfile.A500LedFilter ||
			(Profile == AmigaOutputProfile.A500 && audioFilterEnabled);
		ComputeButterworthCoefficients(LedLowPassCutoffHz, sampleRate, out var b0, out var b1, out var b2, out var a1, out var a2);
		var highPassAlpha = GetHighPassAlpha(DcBlockCutoffHz, sampleRate);
		var frames = samples.Length / channels;

		for (var frame = 0; frame < frames; frame++)
		{
			var offset = frame * channels;
			if (channels >= 2)
			{
				ApplyStereoSeparation(samples, offset);
			}

			for (var channel = 0; channel < channels; channel++)
			{
				var index = offset + channel;
				var sample = samples[index];
				sample = OnePoleLowPass(sample, channel, lowPassAlpha, _lowPassState);
				if (ledFilterEnabled)
				{
					sample = ButterworthLowPass(sample, channel, b0, b1, b2, a1, a2);
				}

				sample = DcBlock(sample, channel, highPassAlpha);
				samples[index] = SoftLimit(sample);
			}
		}
	}

	private void EnsureState(int channels)
	{
		if (_lowPassState.Length == channels)
		{
			return;
		}

		_lowPassState = new float[channels];
		_dcPreviousInput = new float[channels];
		_dcPreviousOutput = new float[channels];
		_ledX1 = new float[channels];
		_ledX2 = new float[channels];
		_ledY1 = new float[channels];
		_ledY2 = new float[channels];
	}

	private static void ApplyStereoSeparation(Span<float> samples, int offset)
	{
		var left = samples[offset];
		var right = samples[offset + 1];
		var mid = (left + right) * 0.5f;
		var side = (left - right) * 0.5f * A500StereoSeparation;
		samples[offset] = mid + side;
		samples[offset + 1] = mid - side;
	}

	private static float OnePoleLowPass(float sample, int channel, double alpha, float[] state)
	{
		var output = state[channel] + ((sample - state[channel]) * (float)alpha);
		state[channel] = output;
		return output;
	}

	/// <summary>
	/// 2-pole Butterworth low-pass filter (biquad direct form I).
	/// Models the Amiga hardware audio filter (controlled by the power LED).
	/// </summary>
	private float ButterworthLowPass(float sample, int channel, double b0, double b1, double b2, double a1, double a2)
	{
		var x0 = (double)sample;
		var output = b0 * x0 + b1 * _ledX1[channel] + b2 * _ledX2[channel]
			- a1 * _ledY1[channel] - a2 * _ledY2[channel];

		_ledX2[channel] = _ledX1[channel];
		_ledX1[channel] = sample;
		_ledY2[channel] = _ledY1[channel];
		_ledY1[channel] = (float)output;

		return (float)output;
	}

	/// <summary>
	/// Compute biquad coefficients for a 2nd-order Butterworth low-pass filter
	/// using the bilinear transform with frequency pre-warping.
	/// </summary>
	private static void ComputeButterworthCoefficients(
		double cutoffHz, int sampleRate,
		out double b0, out double b1, out double b2,
		out double a1, out double a2)
	{
		var omega = 2.0 * Math.PI * cutoffHz / sampleRate;
		var sinOmega = Math.Sin(omega);
		var cosOmega = Math.Cos(omega);
		var alpha = sinOmega / (2.0 * ButterworthQ);

		var a0 = 1.0 + alpha;
		b0 = (1.0 - cosOmega) / 2.0 / a0;
		b1 = (1.0 - cosOmega) / a0;
		b2 = (1.0 - cosOmega) / 2.0 / a0;
		a1 = (-2.0 * cosOmega) / a0;
		a2 = (1.0 - alpha) / a0;
	}

	private float DcBlock(float sample, int channel, double alpha)
	{
		var output = (float)(alpha * (_dcPreviousOutput[channel] + sample - _dcPreviousInput[channel]));
		_dcPreviousInput[channel] = sample;
		_dcPreviousOutput[channel] = output;
		return output;
	}

	private static double GetLowPassAlpha(double cutoffHz, int sampleRate)
	{
		return 1.0 - Math.Exp(-2.0 * Math.PI * cutoffHz / sampleRate);
	}

	private static double GetHighPassAlpha(double cutoffHz, int sampleRate)
	{
		var rc = 1.0 / (2.0 * Math.PI * cutoffHz);
		var dt = 1.0 / sampleRate;
		return rc / (rc + dt);
	}

	private static float SoftLimit(float sample)
	{
		var shaped = sample * 0.98f;
		shaped -= shaped * shaped * shaped * 0.04f;
		return Math.Clamp(shaped, -1.0f, 1.0f);
	}
}
