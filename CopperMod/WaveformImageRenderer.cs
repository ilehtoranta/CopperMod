using TgColor = Terminal.Gui.Drawing.Color;

namespace CopperMod;

internal static class WaveformImageRenderer
{
	public const int MinimumWidth = 128;
	public const int MinimumHeight = 72;
	public const int DefaultWidth = 480;
	public const int DefaultHeight = 160;
	public const int MaximumWidth = 768;
	public const int MaximumHeight = 256;
	private const int MaximumRenderedChannels = 4;
	private const int LaneVerticalPadding = 1;
	private static readonly TgColor Background = new(8, 10, 14, 255);
	private static readonly TgColor Grid = new(26, 30, 36, 255);
	private static readonly TgColor CenterLine = new(58, 64, 72, 255);
	private static readonly TgColor[] ChannelColors =
	{
		new(238, 170, 92, 255),
		new(86, 190, 170, 255),
		new(120, 154, 238, 255),
		new(238, 108, 128, 255)
	};

	public static TgColor[,] Render(WaveformSnapshot snapshot, int width = DefaultWidth, int height = DefaultHeight)
	{
		if (snapshot is null)
		{
			throw new ArgumentNullException(nameof(snapshot));
		}

		if (width <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(width));
		}

		if (height <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(height));
		}

		var image = new TgColor[width, height];
		Fill(image, Background);

		var renderedChannels = Math.Min(MaximumRenderedChannels, snapshot.ChannelCount);
		if (renderedChannels == 0 || snapshot.BinCount == 0)
		{
			return image;
		}

		for (var channelIndex = 0; channelIndex < renderedChannels; channelIndex++)
		{
			var channel = snapshot.Channels[channelIndex];
			var laneTop = channelIndex * height / renderedChannels;
			var laneBottom = ((channelIndex + 1) * height / renderedChannels) - 1;
			var drawTop = laneTop + LaneVerticalPadding;
			var drawBottom = laneBottom - LaneVerticalPadding;
			if (drawBottom <= drawTop)
			{
				continue;
			}

			DrawHorizontalLine(image, drawTop + ((drawBottom - drawTop) / 2), CenterLine);
			var color = channel.IsActive ? ChannelColors[channel.ChannelIndex % ChannelColors.Length] : Grid;
			int? previousY = null;
			for (var x = 0; x < width; x++)
			{
				var bin = Math.Min(channel.BinCount - 1, (int)((long)x * channel.BinCount / width));
				var value = channel.Values[bin];
				var y = SampleToY(value, drawTop, drawBottom);
				if (previousY.HasValue)
				{
					DrawVerticalSegment(image, x, previousY.Value, y, color);
				}
				else
				{
					image[x, y] = color;
				}

				previousY = y;
			}
		}

		return image;
	}

	private static void Fill(TgColor[,] image, TgColor color)
	{
		var width = image.GetLength(0);
		var height = image.GetLength(1);
		for (var y = 0; y < height; y++)
		{
			for (var x = 0; x < width; x++)
			{
				image[x, y] = color;
			}
		}
	}

	private static void DrawHorizontalLine(TgColor[,] image, int y, TgColor color)
	{
		var width = image.GetLength(0);
		var height = image.GetLength(1);
		if ((uint)y >= (uint)height)
		{
			return;
		}

		for (var x = 0; x < width; x++)
		{
			image[x, y] = color;
		}
	}

	private static int SampleToY(float sample, int top, int bottom)
	{
		var clamped = Math.Clamp(sample, -1.0f, 1.0f);
		var normalized = (clamped + 1.0f) * 0.5f;
		return Math.Clamp((int)MathF.Round(top + ((1.0f - normalized) * (bottom - top))), top, bottom);
	}

	private static void DrawVerticalSegment(TgColor[,] image, int x, int y0, int y1, TgColor color)
	{
		if (y0 > y1)
		{
			(y0, y1) = (y1, y0);
		}

		for (var y = y0; y <= y1; y++)
		{
			image[x, y] = color;
		}
	}
}
