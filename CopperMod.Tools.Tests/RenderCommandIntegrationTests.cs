using System.Buffers.Binary;

namespace CopperMod.Tools.Tests;

public sealed class RenderCommandIntegrationTests
{
	[Fact]
	public void RendersProTrackerFixtureToFloatWav()
	{
		using var temp = TemporaryDirectory.Create();
		var input = FindWorkspaceFile("TestTunes", "ProTracker", "failright.mod");
		var output = Path.Combine(temp.Path, "failright.wav");

		var exitCode = Run("render", input, "--out", output, "--seconds", "1", "--overwrite");

		Assert.Equal(0, exitCode);
		var wav = ReadFloatWav(output);
		Assert.Equal(3, wav.FormatTag);
		Assert.Equal(2, wav.Channels);
		Assert.Equal(44100, wav.SampleRate);
		Assert.Contains(wav.Samples, sample => Math.Abs(sample) > 0.0001f);
		Assert.All(wav.Samples, sample => Assert.True(float.IsFinite(sample)));
	}

	[Fact]
	public void RendersProTrackerFixtureToRawFloatPcm()
	{
		using var temp = TemporaryDirectory.Create();
		var input = FindWorkspaceFile("TestTunes", "ProTracker", "failright.mod");
		var output = Path.Combine(temp.Path, "failright.pcm");

		var exitCode = Run("render", input, "--out", output, "--seconds", "1", "--overwrite");

		Assert.Equal(0, exitCode);
		var bytes = File.ReadAllBytes(output);
		Assert.NotEmpty(bytes);
		Assert.Equal(0, bytes.Length % (2 * sizeof(float)));
		var samples = ReadFloatPcm(bytes);
		Assert.Contains(samples, sample => Math.Abs(sample) > 0.0001f);
		Assert.All(samples, sample => Assert.True(float.IsFinite(sample)));
	}

	[Fact]
	public void RendersProTrackerFixtureToWaveformBitmap()
	{
		using var temp = TemporaryDirectory.Create();
		var input = FindWorkspaceFile("TestTunes", "ProTracker", "failright.mod");
		var output = Path.Combine(temp.Path, "failright.bmp");

		var exitCode = Run(
			"render",
			input,
			"--out",
			output,
			"--seconds",
			"1",
			"--bitmap-width",
			"320",
			"--bitmap-height",
			"120",
			"--overwrite");

		Assert.Equal(0, exitCode);
		var bitmap = ReadBitmap(output);
		Assert.Equal(320, bitmap.Width);
		Assert.Equal(120, bitmap.Height);
		Assert.Equal(24, bitmap.BitsPerPixel);
		Assert.True(
			ContainsColor(bitmap, r: 238, g: 170, b: 92) ||
			ContainsColor(bitmap, r: 86, g: 190, b: 170));
	}

	[Fact]
	public void RendersSidFixtureWithExplicitSecondsDespiteUnknownDuration()
	{
		using var temp = TemporaryDirectory.Create();
		var input = FindWorkspaceFile("TestTunes", "SID", "Galway", "Arkanoid.sid");
		var output = Path.Combine(temp.Path, "arkanoid.pcm");

		var exitCode = Run("render", input, "--out", output, "--seconds", "0.25", "--overwrite");

		Assert.Equal(0, exitCode);
		Assert.True(new FileInfo(output).Length > 0);
	}

	[Fact]
	public void RendersCustFixtureToRawFloatPcm()
	{
		using var temp = TemporaryDirectory.Create();
		var input = FindWorkspaceFile("TestTunes", "Amiga.CUST", "AlteredBeast.CUST");
		var output = Path.Combine(temp.Path, "alteredbeast.pcm");

		var exitCode = Run("render", input, "--out", output, "--seconds", "1", "--overwrite");

		Assert.Equal(0, exitCode);
		var samples = ReadFloatPcm(File.ReadAllBytes(output));
		Assert.Contains(samples, sample => Math.Abs(sample) > 0.0001f);
		Assert.All(samples, sample => Assert.True(float.IsFinite(sample)));
	}

	[Fact]
	public void RawAndPlayerOutputModesDiffer()
	{
		using var temp = TemporaryDirectory.Create();
		var input = FindWorkspaceFile("TestTunes", "ProTracker", "failright.mod");
		var raw = Path.Combine(temp.Path, "raw.pcm");
		var player = Path.Combine(temp.Path, "player.pcm");

		Assert.Equal(0, Run("render", input, "--out", raw, "--seconds", "1", "--overwrite"));
		Assert.Equal(0, Run("render", input, "--out", player, "--seconds", "1", "--output", "player", "--amiga-profile", "a500", "--overwrite"));

		Assert.NotEqual(File.ReadAllBytes(raw), File.ReadAllBytes(player));
	}

	[Fact]
	public void Mp3RenderEitherSucceedsOrReportsMediaFoundationRequirement()
	{
		using var temp = TemporaryDirectory.Create();
		var input = FindWorkspaceFile("TestTunes", "ProTracker", "failright.mod");
		var output = Path.Combine(temp.Path, "failright.mp3");
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		var exitCode = CopperModTools.Run(
			new[] { "render", input, "--out", output, "--seconds", "0.25", "--overwrite" },
			stdout,
			stderr);

		if (exitCode == 0)
		{
			var bytes = File.ReadAllBytes(output);
			Assert.True(bytes.Length > 16);
			Assert.True(HasMp3Header(bytes));
		}
		else
		{
			Assert.Contains("Media Foundation MP3 encoder", stderr.ToString());
		}
	}

	private static int Run(params string[] args)
	{
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();
		var exitCode = CopperModTools.Run(args, stdout, stderr);
		if (exitCode != 0)
		{
			throw new InvalidOperationException(stderr.ToString());
		}

		return exitCode;
	}

	private static string FindWorkspaceFile(params string[] parts)
	{
		var relativePath = Path.Combine(parts);
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory != null)
		{
			var candidate = Path.Combine(directory.FullName, relativePath);
			if (File.Exists(candidate))
			{
				return candidate;
			}

			directory = directory.Parent;
		}

		throw new FileNotFoundException("Could not find fixture '" + relativePath + "'.");
	}

	private static WavData ReadFloatWav(string path)
	{
		using var stream = File.OpenRead(path);
		using var reader = new BinaryReader(stream);
		Assert.Equal("RIFF", new string(reader.ReadChars(4)));
		reader.ReadInt32();
		Assert.Equal("WAVE", new string(reader.ReadChars(4)));

		short formatTag = 0;
		short channels = 0;
		var sampleRate = 0;
		float[]? samples = null;
		while (stream.Position + 8 <= stream.Length)
		{
			var chunkId = new string(reader.ReadChars(4));
			var chunkSize = reader.ReadInt32();
			var chunkEnd = stream.Position + chunkSize;
			if (chunkId == "fmt ")
			{
				formatTag = reader.ReadInt16();
				channels = reader.ReadInt16();
				sampleRate = reader.ReadInt32();
			}
			else if (chunkId == "data")
			{
				var data = reader.ReadBytes(chunkSize);
				samples = ReadFloatPcm(data);
			}

			stream.Position = chunkEnd + (chunkSize & 1);
		}

		return new WavData(formatTag, channels, sampleRate, samples ?? Array.Empty<float>());
	}

	private static float[] ReadFloatPcm(byte[] bytes)
	{
		var samples = new float[bytes.Length / sizeof(float)];
		for (var i = 0; i < samples.Length; i++)
		{
			samples[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * sizeof(float), sizeof(float)));
		}

		return samples;
	}

	private static bool HasMp3Header(byte[] bytes)
	{
		if (bytes.Length >= 3 && bytes[0] == (byte)'I' && bytes[1] == (byte)'D' && bytes[2] == (byte)'3')
		{
			return true;
		}

		for (var i = 0; i + 1 < Math.Min(bytes.Length, 4096); i++)
		{
			if (bytes[i] == 0xFF && (bytes[i + 1] & 0xE0) == 0xE0)
			{
				return true;
			}
		}

		return false;
	}

	private static BitmapData ReadBitmap(string path)
	{
		var bytes = File.ReadAllBytes(path);
		Assert.True(bytes.Length >= 54);
		Assert.Equal((byte)'B', bytes[0]);
		Assert.Equal((byte)'M', bytes[1]);
		Assert.Equal(bytes.Length, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(2, sizeof(int))));
		var pixelOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(10, sizeof(int)));
		Assert.Equal(40, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(14, sizeof(int))));
		var width = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(18, sizeof(int)));
		var height = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(22, sizeof(int)));
		Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(26, sizeof(short))));
		var bitsPerPixel = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(28, sizeof(short)));
		Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(30, sizeof(int))));
		return new BitmapData(width, height, bitsPerPixel, pixelOffset, bytes);
	}

	private static bool ContainsColor(BitmapData bitmap, byte r, byte g, byte b)
	{
		var rowBytes = bitmap.Width * 3;
		var stride = (rowBytes + 3) & ~3;
		for (var y = 0; y < bitmap.Height; y++)
		{
			var rowOffset = bitmap.PixelOffset + (y * stride);
			for (var x = 0; x < bitmap.Width; x++)
			{
				var offset = rowOffset + (x * 3);
				if (bitmap.Bytes[offset] == b && bitmap.Bytes[offset + 1] == g && bitmap.Bytes[offset + 2] == r)
				{
					return true;
				}
			}
		}

		return false;
	}

	private readonly record struct WavData(short FormatTag, short Channels, int SampleRate, float[] Samples);

	private readonly record struct BitmapData(int Width, int Height, short BitsPerPixel, int PixelOffset, byte[] Bytes);
}
