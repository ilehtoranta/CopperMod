using CopperMod.Abstractions;

namespace CopperMod.Rendering;

public sealed class ModuleRenderSettings
{
	public ModuleRenderSettings(
		int sampleRate = AudioRenderOptions.DefaultSampleRate,
		int channelCount = AudioRenderOptions.DefaultChannelCount,
		ModuleRenderOutputMode outputMode = ModuleRenderOutputMode.Raw,
		AmigaOutputProfile amigaOutputProfile = AmigaOutputProfile.A500,
		C64OutputProfile c64OutputProfile = C64OutputProfile.C64,
		bool interpolationEnabled = false)
	{
		if (sampleRate <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
		}

		if (channelCount <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(channelCount), channelCount, "Channel count must be positive.");
		}

		SampleRate = sampleRate;
		ChannelCount = channelCount;
		OutputMode = outputMode;
		AmigaOutputProfile = amigaOutputProfile;
		C64OutputProfile = c64OutputProfile;
		InterpolationEnabled = interpolationEnabled;
	}

	public int SampleRate { get; }

	public int ChannelCount { get; }

	public ModuleRenderOutputMode OutputMode { get; }

	public AmigaOutputProfile AmigaOutputProfile { get; }

	public C64OutputProfile C64OutputProfile { get; }

	public bool InterpolationEnabled { get; }

	public AudioRenderOptions ToAudioRenderOptions()
	{
		return new AudioRenderOptions(SampleRate, ChannelCount, InterpolationEnabled);
	}
}
