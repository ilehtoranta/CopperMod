namespace CopperMod.Tools;

internal sealed class WaveformBitmapSnapshot
{
	public WaveformBitmapSnapshot(IReadOnlyList<WaveformBitmapChannelSnapshot> channels, long sourceFrameCount, int sampleRate)
	{
		Channels = channels ?? throw new ArgumentNullException(nameof(channels));
		if (sourceFrameCount < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(sourceFrameCount));
		}

		if (sampleRate <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(sampleRate));
		}

		SourceFrameCount = sourceFrameCount;
		SampleRate = sampleRate;
	}

	public IReadOnlyList<WaveformBitmapChannelSnapshot> Channels { get; }

	public long SourceFrameCount { get; }

	public int SampleRate { get; }

	public int ChannelCount => Channels.Count;

	public int BinCount => Channels.Count == 0 ? 0 : Channels[0].BinCount;
}

internal sealed class WaveformBitmapChannelSnapshot
{
	public WaveformBitmapChannelSnapshot(int channelIndex, float[] minimums, float[] maximums, float[] values, bool isActive)
	{
		if (channelIndex < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(channelIndex));
		}

		if (minimums is null)
		{
			throw new ArgumentNullException(nameof(minimums));
		}

		if (maximums is null)
		{
			throw new ArgumentNullException(nameof(maximums));
		}

		if (values is null)
		{
			throw new ArgumentNullException(nameof(values));
		}

		if (minimums.Length != maximums.Length || minimums.Length != values.Length)
		{
			throw new ArgumentException("Waveform arrays must have the same length.", nameof(values));
		}

		ChannelIndex = channelIndex;
		Minimums = minimums;
		Maximums = maximums;
		Values = values;
		IsActive = isActive;
	}

	public int ChannelIndex { get; }

	public float[] Minimums { get; }

	public float[] Maximums { get; }

	public float[] Values { get; }

	public bool IsActive { get; }

	public int BinCount => Minimums.Length;
}
