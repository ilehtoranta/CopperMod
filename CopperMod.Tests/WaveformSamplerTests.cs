using AmigaTracker.Abstractions;

namespace CopperMod.Tests;

public sealed class WaveformSamplerTests
{
	[Fact]
	public void CreateSnapshotDownmixesStereoAndPreservesBinPeaks()
	{
		var samples = new[]
		{
			-1.0f, -0.5f,
			0.5f, 1.0f,
			-0.25f, 0.25f,
			0.75f, 0.25f
		};

		var snapshot = WaveformSampler.CreateSnapshot(samples, channelCount: 2, sampleRate: 44100, maximumBins: 2);

		Assert.Equal(2, snapshot.BinCount);
		Assert.Equal(4, snapshot.SourceFrameCount);
		Assert.Equal(44100, snapshot.SampleRate);
		Assert.Equal(-0.75f, snapshot.Minimums[0]);
		Assert.Equal(0.75f, snapshot.Maximums[0]);
		Assert.Equal(-0.75f, snapshot.Channels[0].Values[0]);
		Assert.Equal(0.0f, snapshot.Minimums[1]);
		Assert.Equal(0.5f, snapshot.Maximums[1]);
		Assert.Equal(0.0f, snapshot.Channels[0].Values[1]);
	}

	[Fact]
	public void CreateSnapshotHandlesEmptyInput()
	{
		var snapshot = WaveformSampler.CreateSnapshot(Array.Empty<float>(), channelCount: 2, sampleRate: 44100);

		Assert.Equal(0, snapshot.BinCount);
		Assert.Equal(0, snapshot.SourceFrameCount);
	}

	[Fact]
	public void CreateSnapshotPreservesSeparateTrackerChannels()
	{
		var channelWaveform = new ModuleChannelWaveform(
			new[]
			{
				new ModuleChannelWaveformChannel(0, new[] { -1.0f, 0.0f, 1.0f, 0.5f }, isActive: true),
				new ModuleChannelWaveformChannel(1, new[] { -0.25f, -0.5f, 0.25f, 0.5f }, isActive: true)
			},
			sourceFrameCount: 4,
			sampleRate: 44100);

		var snapshot = WaveformSampler.CreateSnapshot(channelWaveform, maximumBins: 2);

		Assert.Equal(2, snapshot.ChannelCount);
		Assert.Equal(new[] { -1.0f, 0.5f }, snapshot.Channels[0].Minimums);
		Assert.Equal(new[] { 0.0f, 1.0f }, snapshot.Channels[0].Maximums);
		Assert.Equal(new[] { -1.0f, 1.0f }, snapshot.Channels[0].Values);
		Assert.Equal(new[] { -0.5f, 0.25f }, snapshot.Channels[1].Minimums);
		Assert.Equal(new[] { -0.25f, 0.5f }, snapshot.Channels[1].Maximums);
		Assert.Equal(new[] { -0.25f, 0.25f }, snapshot.Channels[1].Values);
	}
}
