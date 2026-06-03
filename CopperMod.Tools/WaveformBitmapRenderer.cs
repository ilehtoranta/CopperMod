namespace CopperMod.Tools;

internal static class WaveformBitmapRenderer
{
	public const int MinimumWidth = 128;
	public const int MinimumHeight = 72;
	public const int DefaultWidth = 1024;
	public const int DefaultHeight = 256;
	public const int MaximumWidth = 8192;
	public const int MaximumHeight = 4096;
	public const int MaximumRenderedChannels = 4;
	private const int LaneVerticalPadding = 2;
	private const float MinimumNormalizedPeak = 0.01f;
	private static readonly WaveformBitmapColor Background = new(8, 10, 14);
	private static readonly WaveformBitmapColor Grid = new(26, 30, 36);
	private static readonly WaveformBitmapColor CenterLine = new(58, 64, 72);
	private static readonly WaveformBitmapColor[] ChannelColors =
	{
		new(238, 170, 92),
		new(86, 190, 170),
		new(120, 154, 238),
		new(238, 108, 128)
	};

	public static WaveformBitmapColor[,] Render(WaveformBitmapSnapshot snapshot, int width = DefaultWidth, int height = DefaultHeight)
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

		var image = new WaveformBitmapColor[width, height];
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
			var displayScale = GetDisplayScale(channel);
			int? previousY = null;
			for (var x = 0; x < width; x++)
			{
				var bin = Math.Min(channel.BinCount - 1, (int)((long)x * channel.BinCount / width));
				var minimum = ScaleForDisplay(channel.Minimums[bin], displayScale);
				var maximum = ScaleForDisplay(channel.Maximums[bin], displayScale);
				if (minimum > maximum)
				{
					(minimum, maximum) = (maximum, minimum);
				}

				DrawVerticalSegment(
					image,
					x,
					SampleToY(maximum, drawTop, drawBottom),
					SampleToY(minimum, drawTop, drawBottom),
					color);

				var value = ScaleForDisplay(channel.Values[bin], displayScale);
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

	private static void Fill(WaveformBitmapColor[,] image, WaveformBitmapColor color)
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

	private static void DrawHorizontalLine(WaveformBitmapColor[,] image, int y, WaveformBitmapColor color)
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

	private static float GetDisplayScale(WaveformBitmapChannelSnapshot channel)
	{
		var peak = 0.0f;
		for (var i = 0; i < channel.BinCount; i++)
		{
			peak = Math.Max(peak, Math.Abs(FiniteOrZero(channel.Minimums[i])));
			peak = Math.Max(peak, Math.Abs(FiniteOrZero(channel.Maximums[i])));
			peak = Math.Max(peak, Math.Abs(FiniteOrZero(channel.Values[i])));
		}

		return peak >= MinimumNormalizedPeak && peak < 1.0f ? 1.0f / peak : 1.0f;
	}

	private static float ScaleForDisplay(float sample, float displayScale)
	{
		return Math.Clamp(FiniteOrZero(sample) * displayScale, -1.0f, 1.0f);
	}

	private static float FiniteOrZero(float value)
	{
		return float.IsFinite(value) ? value : 0.0f;
	}

	private static void DrawVerticalSegment(WaveformBitmapColor[,] image, int x, int y0, int y1, WaveformBitmapColor color)
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
