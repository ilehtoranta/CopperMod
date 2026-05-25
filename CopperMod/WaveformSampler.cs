using AmigaTracker.Abstractions;

namespace CopperMod;

internal static class WaveformSampler
{
	public const int DefaultMaximumBins = 512;

	public static WaveformSnapshot CreateSnapshot(
		ReadOnlySpan<float> interleavedSamples,
		int channelCount,
		int sampleRate,
		int maximumBins = DefaultMaximumBins)
	{
		if (channelCount <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(channelCount));
		}

		if (sampleRate <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(sampleRate));
		}

		if (maximumBins <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(maximumBins));
		}

		var frameCount = interleavedSamples.Length / channelCount;
		if (frameCount == 0)
		{
			return new WaveformSnapshot(Array.Empty<float>(), Array.Empty<float>(), 0, sampleRate);
		}

		var binCount = Math.Min(maximumBins, frameCount);
		var minimums = new float[binCount];
		var maximums = new float[binCount];
		var values = new float[binCount];

		for (var bin = 0; bin < binCount; bin++)
		{
			var startFrame = (int)((long)bin * frameCount / binCount);
			var endFrame = (int)((long)(bin + 1) * frameCount / binCount);
			if (endFrame <= startFrame)
			{
				endFrame = Math.Min(startFrame + 1, frameCount);
			}

			values[bin] = ReadMono(interleavedSamples, ((startFrame + endFrame - 1) / 2) * channelCount, channelCount);
			var minimum = float.PositiveInfinity;
			var maximum = float.NegativeInfinity;
			for (var frame = startFrame; frame < endFrame; frame++)
			{
				var mono = ReadMono(interleavedSamples, frame * channelCount, channelCount);
				minimum = Math.Min(minimum, mono);
				maximum = Math.Max(maximum, mono);
			}

			minimums[bin] = float.IsFinite(minimum) ? minimum : 0.0f;
			maximums[bin] = float.IsFinite(maximum) ? maximum : 0.0f;
		}

		return new WaveformSnapshot(new[] { new WaveformChannelSnapshot(0, minimums, maximums, values, true) }, frameCount, sampleRate);
	}

	public static WaveformSnapshot CreateSnapshot(ModuleChannelWaveform channelWaveform, int maximumBins = DefaultMaximumBins)
	{
		if (channelWaveform is null)
		{
			throw new ArgumentNullException(nameof(channelWaveform));
		}

		if (maximumBins <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(maximumBins));
		}

		var channels = new WaveformChannelSnapshot[channelWaveform.Channels.Count];
		for (var i = 0; i < channelWaveform.Channels.Count; i++)
		{
			var channel = channelWaveform.Channels[i];
			channels[i] = CreateChannelSnapshot(
				channel.ChannelIndex,
				channel.Samples,
				channelWaveform.SampleRate,
				maximumBins,
				channel.IsActive);
		}

		return new WaveformSnapshot(channels, channelWaveform.SourceFrameCount, channelWaveform.SampleRate);
	}

	private static WaveformChannelSnapshot CreateChannelSnapshot(
		int channelIndex,
		ReadOnlySpan<float> samples,
		int sampleRate,
		int maximumBins,
		bool isActive)
	{
		if (sampleRate <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(sampleRate));
		}

		var frameCount = samples.Length;
		if (frameCount == 0)
		{
			return new WaveformChannelSnapshot(channelIndex, Array.Empty<float>(), Array.Empty<float>(), false);
		}

		var binCount = Math.Min(maximumBins, frameCount);
		var minimums = new float[binCount];
		var maximums = new float[binCount];
		var values = new float[binCount];
		for (var bin = 0; bin < binCount; bin++)
		{
			var startFrame = (int)((long)bin * frameCount / binCount);
			var endFrame = (int)((long)(bin + 1) * frameCount / binCount);
			if (endFrame <= startFrame)
			{
				endFrame = Math.Min(startFrame + 1, frameCount);
			}

			values[bin] = samples[(startFrame + endFrame - 1) / 2];
			var minimum = float.PositiveInfinity;
			var maximum = float.NegativeInfinity;
			for (var frame = startFrame; frame < endFrame; frame++)
			{
				var sample = samples[frame];
				minimum = Math.Min(minimum, sample);
				maximum = Math.Max(maximum, sample);
			}

			minimums[bin] = float.IsFinite(minimum) ? minimum : 0.0f;
			maximums[bin] = float.IsFinite(maximum) ? maximum : 0.0f;
		}

		return new WaveformChannelSnapshot(channelIndex, minimums, maximums, values, isActive);
	}

	private static float ReadMono(ReadOnlySpan<float> interleavedSamples, int sampleOffset, int channelCount)
	{
		var mono = 0.0f;
		for (var channel = 0; channel < channelCount; channel++)
		{
			mono += interleavedSamples[sampleOffset + channel];
		}

		return mono / channelCount;
	}
}
