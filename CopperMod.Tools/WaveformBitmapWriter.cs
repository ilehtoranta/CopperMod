using System.Text;

namespace CopperMod.Tools;

internal static class WaveformBitmapWriter
{
	private const int FileHeaderSize = 14;
	private const int InfoHeaderSize = 40;
	private const short BitsPerPixel = 24;

	public static void Write(Stream output, WaveformBitmapColor[,] pixels)
	{
		if (output is null)
		{
			throw new ArgumentNullException(nameof(output));
		}

		if (pixels is null)
		{
			throw new ArgumentNullException(nameof(pixels));
		}

		var width = pixels.GetLength(0);
		var height = pixels.GetLength(1);
		if (width <= 0 || height <= 0)
		{
			throw new ArgumentException("Bitmap must have positive dimensions.", nameof(pixels));
		}

		var rowBytes = checked(width * 3);
		var stride = (rowBytes + 3) & ~3;
		var imageSize = checked(stride * height);
		var pixelOffset = FileHeaderSize + InfoHeaderSize;
		var fileSize = checked(pixelOffset + imageSize);

		using var writer = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true);
		writer.Write((byte)'B');
		writer.Write((byte)'M');
		writer.Write(fileSize);
		writer.Write(0);
		writer.Write(pixelOffset);

		writer.Write(InfoHeaderSize);
		writer.Write(width);
		writer.Write(height);
		writer.Write((short)1);
		writer.Write(BitsPerPixel);
		writer.Write(0);
		writer.Write(imageSize);
		writer.Write(0);
		writer.Write(0);
		writer.Write(0);
		writer.Write(0);

		var padding = new byte[stride - rowBytes];
		for (var y = height - 1; y >= 0; y--)
		{
			for (var x = 0; x < width; x++)
			{
				var color = pixels[x, y];
				writer.Write(color.B);
				writer.Write(color.G);
				writer.Write(color.R);
			}

			if (padding.Length > 0)
			{
				writer.Write(padding);
			}
		}
	}
}
