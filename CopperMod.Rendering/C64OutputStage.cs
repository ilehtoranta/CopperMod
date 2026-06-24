namespace CopperMod.Rendering;

public sealed class C64OutputStage
{
	private static readonly C64OutputStageProfile C64Profile = new C64OutputStageProfile(
		DcBlockCutoffHz: 1.30,
		OutputLowPassCutoffHz: 24000.0,
		OutputHeadroom: 1.04f,
		PreCouplingDrive: 0.0f,
		PostCouplingDrive: 0.0f);

	private static readonly C64OutputStageProfile C64MeasuredProfile = new C64OutputStageProfile(
		DcBlockCutoffHz: 1.30,
		OutputLowPassCutoffHz: 24000.0,
		OutputHeadroom: 1.04f,
		PreCouplingDrive: 0.08f,
		PostCouplingDrive: 0.0f);

	private float[] _lowPassState = Array.Empty<float>();
	private float[] _dcPreviousInput = Array.Empty<float>();
	private float[] _dcPreviousOutput = Array.Empty<float>();

	public C64OutputStage(C64OutputProfile profile = C64OutputProfile.C64)
	{
		Profile = profile;
	}

	public C64OutputProfile Profile { get; set; }

	public void Reset()
	{
		Array.Clear(_lowPassState);
		Array.Clear(_dcPreviousInput);
		Array.Clear(_dcPreviousOutput);
	}

	public void Process(Span<float> samples, int channels, int sampleRate)
	{
		if (Profile == C64OutputProfile.Clean || samples.Length == 0)
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
		var profile = GetProfile(Profile);
		var lowPassAlpha = GetLowPassAlpha(profile.OutputLowPassCutoffHz, sampleRate);
		var highPassAlpha = GetHighPassAlpha(profile.DcBlockCutoffHz, sampleRate);
		for (var i = 0; i < samples.Length; i++)
		{
			var channel = i % channels;
			var sample = samples[i];
			sample = ApplyPreCouplingSaturation(sample, profile.PreCouplingDrive);
			sample = OnePoleLowPass(sample, channel, lowPassAlpha);
			sample = DcBlock(sample, channel, highPassAlpha);
			sample = SoftClip(sample, profile.PostCouplingDrive);
			samples[i] = Math.Clamp(sample * profile.OutputHeadroom, -1.0f, 1.0f);
		}
	}

	private static C64OutputStageProfile GetProfile(C64OutputProfile profile)
	{
		return profile switch
		{
			C64OutputProfile.C64Measured => C64MeasuredProfile,
			_ => C64Profile
		};
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
	}

	private float OnePoleLowPass(float sample, int channel, double alpha)
	{
		var output = _lowPassState[channel] + ((sample - _lowPassState[channel]) * (float)alpha);
		_lowPassState[channel] = output;
		return output;
	}

	private float DcBlock(float sample, int channel, double alpha)
	{
		var output = (float)(alpha * (_dcPreviousOutput[channel] + sample - _dcPreviousInput[channel]));
		_dcPreviousInput[channel] = sample;
		_dcPreviousOutput[channel] = output;
		return output;
	}

	private static float SoftClip(float sample, float drive)
	{
		if (drive <= 0.0f)
		{
			return sample;
		}

		var driven = sample * (1.0f + drive);
		return driven / (1.0f + (Math.Abs(driven) * drive));
	}

	private static float ApplyPreCouplingSaturation(float sample, float drive)
	{
		if (drive <= 0.0f)
		{
			return sample;
		}

		return sample / (1.0f + (Math.Abs(sample) * drive));
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

	private readonly record struct C64OutputStageProfile(
		double DcBlockCutoffHz,
		double OutputLowPassCutoffHz,
		float OutputHeadroom,
		float PreCouplingDrive,
		float PostCouplingDrive);
}
