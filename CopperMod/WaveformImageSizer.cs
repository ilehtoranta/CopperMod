namespace CopperMod;

internal static class WaveformImageSizer
{
	public const int MaximumPixels = 36_000;

	public static (int Width, int Height) Compute(int columns, int rows, int cellWidth, int cellHeight)
	{
		columns = Math.Max(1, columns);
		rows = Math.Max(1, rows);
		cellWidth = Math.Max(1, cellWidth);
		cellHeight = Math.Max(1, cellHeight);

		var aspect = columns * cellWidth / (double)(rows * cellHeight);
		var width = (int)Math.Round(Math.Sqrt(MaximumPixels * aspect));
		var height = (int)Math.Round(width / aspect);

		if (width > WaveformImageRenderer.MaximumWidth)
		{
			width = WaveformImageRenderer.MaximumWidth;
			height = (int)Math.Round(width / aspect);
		}

		if (height > WaveformImageRenderer.MaximumHeight)
		{
			height = WaveformImageRenderer.MaximumHeight;
			width = (int)Math.Round(height * aspect);
		}

		if (width < WaveformImageRenderer.MinimumWidth)
		{
			width = WaveformImageRenderer.MinimumWidth;
			height = (int)Math.Round(width / aspect);
		}

		if (height < WaveformImageRenderer.MinimumHeight)
		{
			height = WaveformImageRenderer.MinimumHeight;
			width = (int)Math.Round(height * aspect);
		}

		return (
			Math.Clamp(width, WaveformImageRenderer.MinimumWidth, WaveformImageRenderer.MaximumWidth),
			Math.Clamp(height, WaveformImageRenderer.MinimumHeight, WaveformImageRenderer.MaximumHeight));
	}
}
