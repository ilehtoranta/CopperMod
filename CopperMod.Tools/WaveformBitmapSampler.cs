namespace CopperMod.Tools;

internal sealed class WaveformBitmapSampler
{
	private readonly int _channelCount;
	private readonly long? _targetFrameCount;
	private readonly ChannelAccumulator[] _channels;
	private long _sourceFrameCount;

	public WaveformBitmapSampler(int channelCount, int sampleRate, int maximumBins, long? targetFrameCount)
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

		_channelCount = channelCount;
		_targetFrameCount = targetFrameCount is > 0 ? targetFrameCount.Value : null;
		SampleRate = sampleRate;

		var binCount = _targetFrameCount.HasValue
			? (int)Math.Min(maximumBins, _targetFrameCount.Value)
			: maximumBins;
		var capturedChannels = Math.Min(channelCount, WaveformBitmapRenderer.MaximumRenderedChannels);
		_channels = new ChannelAccumulator[capturedChannels];
		for (var channel = 0; channel < _channels.Length; channel++)
		{
			_channels[channel] = new ChannelAccumulator(channel, binCount);
		}
	}

	public int SampleRate { get; }

	public void AddSamples(float[] samples, int sampleCount)
	{
		if (samples is null)
		{
			throw new ArgumentNullException(nameof(samples));
		}

		if (sampleCount < 0 || sampleCount > samples.Length)
		{
			throw new ArgumentOutOfRangeException(nameof(sampleCount));
		}

		var frameCount = sampleCount / _channelCount;
		for (var frame = 0; frame < frameCount; frame++)
		{
			var bin = GetBin(_sourceFrameCount);
			if (bin >= 0)
			{
				var sampleOffset = frame * _channelCount;
				for (var channel = 0; channel < _channels.Length; channel++)
				{
					_channels[channel].Add(bin, samples[sampleOffset + channel]);
				}
			}

			_sourceFrameCount++;
		}
	}

	public WaveformBitmapSnapshot CreateSnapshot()
	{
		var channels = new WaveformBitmapChannelSnapshot[_channels.Length];
		for (var channel = 0; channel < _channels.Length; channel++)
		{
			channels[channel] = _channels[channel].CreateSnapshot();
		}

		return new WaveformBitmapSnapshot(channels, _sourceFrameCount, SampleRate);
	}

	private int GetBin(long sourceFrame)
	{
		if (_channels.Length == 0 || _channels[0].BinCount == 0)
		{
			return -1;
		}

		if (_targetFrameCount.HasValue)
		{
			var bin = (int)(sourceFrame * (double)_channels[0].BinCount / _targetFrameCount.Value);
			return Math.Clamp(bin, 0, _channels[0].BinCount - 1);
		}

		return (int)Math.Min(sourceFrame, _channels[0].BinCount - 1);
	}

	private sealed class ChannelAccumulator
	{
		private readonly bool[] _hasSamples;
		private readonly float[] _minimums;
		private readonly float[] _maximums;
		private readonly float[] _values;
		private bool _isActive;

		public ChannelAccumulator(int channelIndex, int binCount)
		{
			ChannelIndex = channelIndex;
			_hasSamples = new bool[binCount];
			_minimums = new float[binCount];
			_maximums = new float[binCount];
			_values = new float[binCount];
			Array.Fill(_minimums, float.PositiveInfinity);
			Array.Fill(_maximums, float.NegativeInfinity);
		}

		public int ChannelIndex { get; }

		public int BinCount => _minimums.Length;

		public void Add(int bin, float sample)
		{
			if (!float.IsFinite(sample))
			{
				sample = 0.0f;
			}

			if (!_hasSamples[bin])
			{
				_minimums[bin] = sample;
				_maximums[bin] = sample;
				_values[bin] = sample;
				_hasSamples[bin] = true;
			}
			else
			{
				_minimums[bin] = Math.Min(_minimums[bin], sample);
				_maximums[bin] = Math.Max(_maximums[bin], sample);
				_values[bin] = (_minimums[bin] + _maximums[bin]) * 0.5f;
			}

			_isActive |= Math.Abs(sample) > float.Epsilon;
		}

		public WaveformBitmapChannelSnapshot CreateSnapshot()
		{
			for (var bin = 0; bin < _minimums.Length; bin++)
			{
				if (_hasSamples[bin])
				{
					continue;
				}

				_minimums[bin] = 0.0f;
				_maximums[bin] = 0.0f;
				_values[bin] = 0.0f;
			}

			return new WaveformBitmapChannelSnapshot(
				ChannelIndex,
				(float[])_minimums.Clone(),
				(float[])_maximums.Clone(),
				(float[])_values.Clone(),
				_isActive);
		}
	}
}
