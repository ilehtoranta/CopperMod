using CopperMod.Sid;
using TgColor = Terminal.Gui.Drawing.Color;

namespace CopperMod;

internal static class C64VideoImageRenderer
{
	public const int DefaultWidth = 384;
	public const int DefaultHeight = 272;
	private static readonly TgColor Background = new(0, 0, 0, 255);

	public static TgColor[,] Render(C64VideoFrame? frame)
	{
		if (frame == null)
		{
			var empty = new TgColor[DefaultWidth, DefaultHeight];
			Fill(empty, Background);
			return empty;
		}

		var image = new TgColor[frame.Width, frame.Height];
		for (var y = 0; y < frame.Height; y++)
		{
			for (var x = 0; x < frame.Width; x++)
			{
				var pixel = frame.Pixels[(y * frame.Width) + x];
				image[x, y] = new TgColor(pixel.Red, pixel.Green, pixel.Blue, pixel.Alpha);
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
}
