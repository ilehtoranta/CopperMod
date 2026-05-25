namespace CopperMod;

internal sealed class AmigaOutputStage
{
	private const double DcBlockCutoffHz = 18.0;
	private const double A500LowPassCutoffHz = 6200.0;
	private const double LedLowPassCutoffHz = 3300.0;
	private const float A500StereoSeparation = 0.92f;

	private float[] _lowPassState = Array.Empty<float>();
	private float[] _ledLowPassState = Array.Empty<float>();
	private float[] _dcPreviousInput = Array.Empty<float>();
	private float[] _dcPreviousOutput = Array.Empty<float>();

	public AmigaOutputStage(AmigaOutputProfile profile = AmigaOutputProfile.None)
	{
		Profile = profile;
	}

	public AmigaOutputProfile Profile { get; set; }

	public void Reset()
	{
		Array.Clear(_lowPassState);
		Array.Clear(_ledLowPassState);
		Array.Clear(_dcPreviousInput);
		Array.Clear(_dcPreviousOutput);
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
		var ledLowPassAlpha = ledFilterEnabled ? GetLowPassAlpha(LedLowPassCutoffHz, sampleRate) : 0.0;
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
					sample = OnePoleLowPass(sample, channel, ledLowPassAlpha, _ledLowPassState);
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
		_ledLowPassState = new float[channels];
		_dcPreviousInput = new float[channels];
		_dcPreviousOutput = new float[channels];
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
