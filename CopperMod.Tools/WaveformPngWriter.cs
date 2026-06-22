using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace CopperMod.Tools;

internal static class WaveformPngWriter
{
	private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

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
			throw new ArgumentException("PNG image must have positive dimensions.", nameof(pixels));
		}

		output.Write(Signature);
		Span<byte> header = stackalloc byte[13];
		BinaryPrimitives.WriteInt32BigEndian(header[..4], width);
		BinaryPrimitives.WriteInt32BigEndian(header.Slice(4, 4), height);
		header[8] = 8;
		header[9] = 2;
		header[10] = 0;
		header[11] = 0;
		header[12] = 0;
		WriteChunk(output, "IHDR", header);
		WriteChunk(output, "IDAT", Compress(ToScanlines(pixels)));
		WriteChunk(output, "IEND", ReadOnlySpan<byte>.Empty);
	}

	private static byte[] ToScanlines(WaveformBitmapColor[,] pixels)
	{
		var width = pixels.GetLength(0);
		var height = pixels.GetLength(1);
		var bytes = new byte[checked(height * ((width * 3) + 1))];
		var offset = 0;
		for (var y = 0; y < height; y++)
		{
			bytes[offset++] = 0;
			for (var x = 0; x < width; x++)
			{
				var color = pixels[x, y];
				bytes[offset++] = color.R;
				bytes[offset++] = color.G;
				bytes[offset++] = color.B;
			}
		}

		return bytes;
	}

	private static byte[] Compress(byte[] data)
	{
		using var output = new MemoryStream();
		using (var deflate = new ZLibStream(output, CompressionLevel.Fastest, leaveOpen: true))
		{
			deflate.Write(data);
		}

		return output.ToArray();
	}

	private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
	{
		Span<byte> buffer = stackalloc byte[4];
		BinaryPrimitives.WriteInt32BigEndian(buffer, data.Length);
		output.Write(buffer);
		var typeBytes = Encoding.ASCII.GetBytes(type);
		output.Write(typeBytes);
		output.Write(data);
		var crc = Crc32(typeBytes, data);
		BinaryPrimitives.WriteUInt32BigEndian(buffer, crc);
		output.Write(buffer);
	}

	private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
	{
		var crc = 0xFFFFFFFFu;
		crc = UpdateCrc(crc, type);
		crc = UpdateCrc(crc, data);
		return crc ^ 0xFFFFFFFFu;
	}

	private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> bytes)
	{
		foreach (var value in bytes)
		{
			crc ^= value;
			for (var i = 0; i < 8; i++)
			{
				crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
			}
		}

		return crc;
	}
}
