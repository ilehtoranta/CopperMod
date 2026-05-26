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

	private readonly record struct WavData(short FormatTag, short Channels, int SampleRate, float[] Samples);
}
