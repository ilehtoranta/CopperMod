namespace CopperMod;

internal sealed class WaveformSnapshot
{
	public WaveformSnapshot(float[] minimums, float[] maximums, int sourceFrameCount, int sampleRate)
		: this(new[] { new WaveformChannelSnapshot(0, minimums, maximums, true) }, sourceFrameCount, sampleRate)
	{
	}

	public WaveformSnapshot(IReadOnlyList<WaveformChannelSnapshot> channels, int sourceFrameCount, int sampleRate)
	{
		if (channels is null)
		{
			throw new ArgumentNullException(nameof(channels));
		}

		if (sourceFrameCount < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(sourceFrameCount));
		}

		if (sampleRate <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(sampleRate));
		}

		var copiedChannels = channels.ToArray();
		if (copiedChannels.Any(channel => channel is null))
		{
			throw new ArgumentException("Waveform channels cannot contain null entries.", nameof(channels));
		}

		var expectedBinCount = copiedChannels.Length == 0 ? 0 : copiedChannels[0].BinCount;
		for (var i = 1; i < copiedChannels.Length; i++)
		{
			if (copiedChannels[i].BinCount != expectedBinCount)
			{
				throw new ArgumentException("All waveform channels must have the same bin count.", nameof(channels));
			}
		}

		Channels = copiedChannels;
		SourceFrameCount = sourceFrameCount;
		SampleRate = sampleRate;
	}

	public IReadOnlyList<WaveformChannelSnapshot> Channels { get; }

	public float[] Minimums => Channels.Count == 0 ? Array.Empty<float>() : Channels[0].Minimums;

	public float[] Maximums => Channels.Count == 0 ? Array.Empty<float>() : Channels[0].Maximums;

	public int SourceFrameCount { get; }

	public int SampleRate { get; }

	public int ChannelCount => Channels.Count;

	public int BinCount => Channels.Count == 0 ? 0 : Channels[0].BinCount;
}

internal sealed class WaveformChannelSnapshot
{
	public WaveformChannelSnapshot(int channelIndex, float[] minimums, float[] maximums, bool isActive)
		: this(channelIndex, minimums, maximums, CreateMidpointValues(minimums, maximums), isActive)
	{
	}

	public WaveformChannelSnapshot(int channelIndex, float[] minimums, float[] maximums, float[] values, bool isActive)
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

		if (minimums.Length != maximums.Length)
		{
			throw new ArgumentException("Waveform minimum and maximum arrays must have the same length.", nameof(maximums));
		}

		if (values is null)
		{
			throw new ArgumentNullException(nameof(values));
		}

		if (values.Length != minimums.Length)
		{
			throw new ArgumentException("Waveform value array must have the same length as the minimum and maximum arrays.", nameof(values));
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

	private static float[] CreateMidpointValues(float[] minimums, float[] maximums)
	{
		if (minimums is null || maximums is null || minimums.Length != maximums.Length)
		{
			return Array.Empty<float>();
		}

		var values = new float[minimums.Length];
		for (var i = 0; i < values.Length; i++)
		{
			values[i] = (minimums[i] + maximums[i]) * 0.5f;
		}

		return values;
	}
}

internal sealed class WaveformSnapshotEventArgs : EventArgs
{
	public WaveformSnapshotEventArgs(WaveformSnapshot snapshot)
	{
		Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
	}

	public WaveformSnapshot Snapshot { get; }
}
