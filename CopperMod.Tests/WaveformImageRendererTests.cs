namespace CopperMod.Tests;

public sealed class WaveformImageRendererTests
{
	private static readonly Terminal.Gui.Drawing.Color Background = new(8, 10, 14, 255);
	private static readonly Terminal.Gui.Drawing.Color Peak = new(255, 224, 168, 255);
	private static readonly Terminal.Gui.Drawing.Color[] ChannelColors =
	{
		new(238, 170, 92, 255),
		new(86, 190, 170, 255),
		new(120, 154, 238, 255),
		new(238, 108, 128, 255)
	};

	[Fact]
	public void RenderCreatesRequestedImageAndDrawsWaveformPixels()
	{
		var snapshot = new WaveformSnapshot(
			new[]
			{
				new WaveformChannelSnapshot(0, new[] { -0.5f, -1.0f, 0.1f }, new[] { 0.5f, -0.25f, 0.9f }, true),
				new WaveformChannelSnapshot(1, new[] { -0.25f, -0.1f, -0.5f }, new[] { 0.1f, 0.25f, 0.5f }, true),
				new WaveformChannelSnapshot(2, new[] { 0.0f, 0.0f, 0.0f }, new[] { 0.0f, 0.0f, 0.0f }, false),
				new WaveformChannelSnapshot(3, new[] { -1.0f, -0.75f, -0.5f }, new[] { 1.0f, 0.75f, 0.5f }, true)
			},
			sourceFrameCount: 3,
			sampleRate: 44100);

		var image = WaveformImageRenderer.Render(snapshot, width: 16, height: 16);

		Assert.Equal(16, image.GetLength(0));
		Assert.Equal(16, image.GetLength(1));
		Assert.True(CountDistinctColors(image) > 4);
	}

	[Fact]
	public void RenderLeavesOnePixelGapAtLaneEdges()
	{
		const int width = 32;
		const int height = 24;
		var snapshot = new WaveformSnapshot(
			new[]
			{
				CreateActiveChannel(0, new[] { -1.0f, 1.0f, -1.0f, 1.0f }),
				CreateActiveChannel(1, new[] { 1.0f, -1.0f, 1.0f, -1.0f }),
				CreateActiveChannel(2, new[] { -1.0f, 1.0f, -1.0f, 1.0f }),
				CreateActiveChannel(3, new[] { 1.0f, -1.0f, 1.0f, -1.0f })
			},
			sourceFrameCount: 4,
			sampleRate: 44100);

		var image = WaveformImageRenderer.Render(snapshot, width, height);

		for (var channelIndex = 0; channelIndex < 4; channelIndex++)
		{
			var laneTop = channelIndex * height / 4;
			var laneBottom = ((channelIndex + 1) * height / 4) - 1;
			AssertRowIsBackground(image, laneTop);
			AssertRowIsBackground(image, laneBottom);
		}
	}

	[Fact]
	public void RenderKeepsWaveformColorTiedToSourceChannel()
	{
		var snapshot = new WaveformSnapshot(
			new[] { CreateActiveChannel(2, new[] { -1.0f, 1.0f, -1.0f, 1.0f }) },
			sourceFrameCount: 4,
			sampleRate: 44100);

		var image = WaveformImageRenderer.Render(snapshot, width: 16, height: 8);

		Assert.True(ContainsColor(image, ChannelColors[2]));
		Assert.False(ContainsColor(image, ChannelColors[0]));
		Assert.False(ContainsColor(image, Peak));
	}

	private static WaveformChannelSnapshot CreateActiveChannel(int channelIndex, float[] values)
	{
		var minimums = values.Select(value => Math.Min(0.0f, value)).ToArray();
		var maximums = values.Select(value => Math.Max(0.0f, value)).ToArray();
		return new WaveformChannelSnapshot(channelIndex, minimums, maximums, values, true);
	}

	private static void AssertRowIsBackground(Terminal.Gui.Drawing.Color[,] image, int y)
	{
		for (var x = 0; x < image.GetLength(0); x++)
		{
			Assert.Equal(Background, image[x, y]);
		}
	}

	private static bool ContainsColor(Terminal.Gui.Drawing.Color[,] image, Terminal.Gui.Drawing.Color color)
	{
		for (var y = 0; y < image.GetLength(1); y++)
		{
			for (var x = 0; x < image.GetLength(0); x++)
			{
				if (image[x, y].Equals(color))
				{
					return true;
				}
			}
		}

		return false;
	}

	private static int CountDistinctColors<T>(T[,] image)
		where T : notnull
	{
		var colors = new HashSet<T>();
		for (var y = 0; y < image.GetLength(1); y++)
		{
			for (var x = 0; x < image.GetLength(0); x++)
			{
				colors.Add(image[x, y]);
			}
		}

		return colors.Count;
	}
}
