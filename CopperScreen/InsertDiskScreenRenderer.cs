namespace CopperScreen;

internal static class InsertDiskScreenRenderer
{
	private const int Blue = unchecked((int)0xFF1B4AA8);
	private const int White = unchecked((int)0xFFE8ECFF);
	private const int Black = unchecked((int)0xFF05070E);
	private const int Grey = unchecked((int)0xFF8A96B8);
	private const int CopperStartTextX = 2;
	private const int CopperStartTextY = 2;
	private const int CopperStartCharacterWidth = 8;
	private const int CopperStartLineHeight = 8;

	public static void Render(int[] framebuffer, int width, int height)
	{
		Array.Fill(framebuffer, Blue);
		DrawCopperStartVersion(framebuffer, width, height);
		var diskWidth = width / 3;
		var diskHeight = height / 5;
		var x = (width - diskWidth) / 2;
		var y = height / 3;
		Fill(framebuffer, width, x, y, diskWidth, diskHeight, White);
		Fill(framebuffer, width, x + (diskWidth / 10), y + (diskHeight / 5), diskWidth * 8 / 10, diskHeight / 8, Black);
		Fill(framebuffer, width, x + (diskWidth * 7 / 10), y + (diskHeight * 3 / 5), diskWidth / 6, diskHeight / 4, Grey);
		DrawText(framebuffer, width, height, "INSERT DISK", width / 2, y + diskHeight + 28, 3, White);
	}

	public static void RenderStatus(int[] framebuffer, int width, int height, string status)
	{
		Array.Fill(framebuffer, Blue);
		DrawCopperStartVersion(framebuffer, width, height);
		DrawText(framebuffer, width, height, status.ToUpperInvariant(), width / 2, height / 2, 2, White);
	}

	public static void RenderHostStatus(int[] framebuffer, int width, int height, string status)
	{
		Array.Fill(framebuffer, Black);
		DrawText(framebuffer, width, height, status.ToUpperInvariant(), width / 2, height / 2, 2, White);
	}

	private static void Fill(int[] framebuffer, int width, int x, int y, int w, int h, int color)
	{
		for (var row = Math.Max(0, y); row < y + h && row < framebuffer.Length / width; row++)
		{
			for (var column = Math.Max(0, x); column < x + w && column < width; column++)
			{
				framebuffer[(row * width) + column] = color;
			}
		}
	}

	private static void DrawText(int[] framebuffer, int width, int height, string text, int centerX, int y, int scale, int color)
	{
		var textWidth = text.Length * 6 * scale;
		var x = centerX - (textWidth / 2);
		foreach (var ch in text)
		{
			DrawChar(framebuffer, width, height, ch, x, y, scale, color);
			x += 6 * scale;
		}
	}

	private static void DrawCopperStartVersion(int[] framebuffer, int width, int height)
	{
		DrawFixedWidthText(
			framebuffer,
			width,
			height,
			CopperStartMetadata.DisplayVersion,
			CopperStartTextX,
			CopperStartTextY,
			1,
			CopperStartCharacterWidth,
			White);
		DrawFixedWidthText(
			framebuffer,
			width,
			height,
			CopperStartMetadata.GitSha,
			CopperStartTextX,
			CopperStartTextY + CopperStartLineHeight,
			1,
			CopperStartCharacterWidth,
			White);
	}

	private static void DrawFixedWidthText(
		int[] framebuffer,
		int width,
		int height,
		string text,
		int x,
		int y,
		int scale,
		int characterWidth,
		int color)
	{
		foreach (var ch in text)
		{
			DrawChar(framebuffer, width, height, ch, x, y, scale, color);
			x += characterWidth;
		}
	}

	private static void DrawChar(int[] framebuffer, int width, int height, char ch, int x, int y, int scale, int color)
	{
		var glyph = Glyph(ch);
		for (var row = 0; row < 7; row++)
		{
			var bits = (byte)(glyph >> ((6 - row) * 8));
			for (var bit = 0; bit < 5; bit++)
			{
				if (((bits >> (4 - bit)) & 1) == 0)
				{
					continue;
				}

				Fill(framebuffer, width, x + (bit * scale), y + (row * scale), scale, scale, color);
			}
		}
	}

	private static ulong Glyph(char ch)
	{
		return ch switch
		{
			'A' => 0x0E11111F111111UL,
			'B' => 0x1E11111E11111EUL,
			'C' => 0x0F10101010100FUL,
			'D' => 0x1E11111111111EUL,
			'E' => 0x1F10101E10101FUL,
			'G' => 0x0F10101311110FUL,
			'I' => 0x1F04040404041FUL,
			'K' => 0x11121418141211UL,
			'L' => 0x1010101010101FUL,
			'M' => 0x111B1515111111UL,
			'N' => 0x11191513111111UL,
			'O' => 0x0E11111111110EUL,
			'P' => 0x1E11111E101010UL,
			'R' => 0x1E11111E141211UL,
			'S' => 0x0F10100E01011EUL,
			'T' => 0x1F040404040404UL,
			'U' => 0x1111111111110EUL,
			'X' => 0x11110A040A1111UL,
			'0' => 0x0E11131519110EUL,
			'1' => 0x040C040404040EUL,
			'2' => 0x0E11010204081FUL,
			'3' => 0x1E01010E01011EUL,
			'4' => 0x02060A121F0202UL,
			'5' => 0x1F10101E01011EUL,
			'6' => 0x0E10101E11110EUL,
			'7' => 0x1F010204080808UL,
			'8' => 0x0E11110E11110EUL,
			'9' => 0x0E11110F01010EUL,
			'a' => 0x00000E010F110FUL,
			'b' => 0x10101E1111111EUL,
			'c' => 0x00000F1010100FUL,
			'd' => 0x01010F1111110FUL,
			'e' => 0x00000E111F100EUL,
			'f' => 0x06081E08080808UL,
			'k' => 0x10101214181412UL,
			'n' => 0x00001E11111111UL,
			'o' => 0x00000E1111110EUL,
			'p' => 0x00001E11111E10UL,
			'r' => 0x00001618101010UL,
			't' => 0x08081E08080806UL,
			'u' => 0x0000111111130DUL,
			'w' => 0x0000111115150AUL,
			':' => 0x00040400040400UL,
			'.' => 0x00000000000C0CUL,
			'$' => 0x040F140E051E04UL,
			' ' => 0,
			_ => 0x1F010204000404UL
		};
	}
}
