using System.Text;
using CopperScreen;

namespace CopperScreen.Tests;

public sealed class FloppyDriveAudioTests
{
	private const int SampleRate = 44_100;

	[Fact]
	public void ResolveSoundPackDirectoryUsesNamedPackUnderCopperScreenSounds()
	{
		var baseDirectory = Path.Combine(Path.GetTempPath(), "copperscreen-audio-base");

		var resolved = FloppyDriveAudio.ResolveSoundPackDirectory("default", baseDirectory);

		Assert.Equal(
			Path.GetFullPath(Path.Combine(baseDirectory, "Sounds", "Floppy", "default")),
			resolved);
	}

	[Fact]
	public void ResolveSoundPackDirectoryKeepsExplicitRelativePathRelativeToBaseDirectory()
	{
		var baseDirectory = Path.Combine(Path.GetTempPath(), "copperscreen-audio-base");

		var resolved = FloppyDriveAudio.ResolveSoundPackDirectory(".\\Custom\\FloppyPack", baseDirectory);

		Assert.Equal(
			Path.GetFullPath(Path.Combine(baseDirectory, ".\\Custom\\FloppyPack")),
			resolved);
	}

	[Fact]
	public void MissingSoundPackDisablesDriveAudioWithStatus()
	{
		var baseDirectory = CreateTempDirectory();
		try
		{
			var audio = FloppyDriveAudio.TryCreate(
				new FloppyDriveAudioOptions(true, "missing-pack", 0.25f),
				baseDirectory,
				SampleRate,
				out var status);

			Assert.Null(audio);
			Assert.Contains("sound pack not found", status, StringComparison.OrdinalIgnoreCase);
		}
		finally
		{
			Directory.Delete(baseDirectory, recursive: true);
		}
	}

	[Fact]
	public void DiskInsertTransitionPlaysInsertSample()
	{
		var baseDirectory = CreateTempDirectory();
		try
		{
			var packDirectory = CreatePackDirectory(baseDirectory, "default");
			WritePcm16Wav(Path.Combine(packDirectory, "disk-insert.wav"), SampleRate, channels: 1, amplitude: 0.8f, frames: 128);
			using var audio = AssertFloppyAudio(baseDirectory, "default");
			var buffer = new float[256 * 2];

			audio.Mix(buffer, frames: 128, channels: 2, [Drive(hasDisk: false, motorOn: false, cylinder: 0)]);
			Array.Clear(buffer);
			audio.Mix(buffer, frames: 128, channels: 2, [Drive(hasDisk: true, motorOn: false, cylinder: 0)]);

			Assert.True(MaxAbs(buffer) > 0.1f);
		}
		finally
		{
			Directory.Delete(baseDirectory, recursive: true);
		}
	}

	[Fact]
	public void MotorLoopFadesInWhileMotorIsOn()
	{
		var baseDirectory = CreateTempDirectory();
		try
		{
			var packDirectory = CreatePackDirectory(baseDirectory, "default");
			WritePcm16Wav(Path.Combine(packDirectory, "motor-loop.wav"), SampleRate, channels: 1, amplitude: 0.6f, frames: 64);
			using var audio = AssertFloppyAudio(baseDirectory, "default");
			var buffer = new float[512 * 2];

			audio.Mix(buffer, frames: 512, channels: 2, [Drive(hasDisk: true, motorOn: true, cylinder: 0)]);

			Assert.True(MaxAbs(buffer) > 0.01f);
		}
		finally
		{
			Directory.Delete(baseDirectory, recursive: true);
		}
	}

	[Fact]
	public void RapidCylinderDeltaPrefersSeekSamples()
	{
		var baseDirectory = CreateTempDirectory();
		try
		{
			var packDirectory = CreatePackDirectory(baseDirectory, "default");
			Directory.CreateDirectory(Path.Combine(packDirectory, "step"));
			Directory.CreateDirectory(Path.Combine(packDirectory, "seek"));
			WritePcm16Wav(Path.Combine(packDirectory, "step", "step-01.wav"), SampleRate, channels: 1, amplitude: 0.15f, frames: 128);
			WritePcm16Wav(Path.Combine(packDirectory, "seek", "seek-01.wav"), SampleRate, channels: 1, amplitude: 0.85f, frames: 128);
			using var audio = AssertFloppyAudio(baseDirectory, "default");
			var buffer = new float[256 * 2];

			audio.Mix(buffer, frames: 128, channels: 2, [Drive(hasDisk: true, motorOn: false, cylinder: 0)]);
			Array.Clear(buffer);
			audio.Mix(buffer, frames: 128, channels: 2, [Drive(hasDisk: true, motorOn: false, cylinder: 3)]);

			Assert.True(MaxAbs(buffer) > 0.4f);
		}
		finally
		{
			Directory.Delete(baseDirectory, recursive: true);
		}
	}

	[Fact]
	public void SingleCylinderDeltaUsesStepSamples()
	{
		var baseDirectory = CreateTempDirectory();
		try
		{
			var packDirectory = CreatePackDirectory(baseDirectory, "default");
			Directory.CreateDirectory(Path.Combine(packDirectory, "step"));
			Directory.CreateDirectory(Path.Combine(packDirectory, "seek"));
			WritePcm16Wav(Path.Combine(packDirectory, "step", "step-01.wav"), SampleRate, channels: 1, amplitude: 0.75f, frames: 128);
			WritePcm16Wav(Path.Combine(packDirectory, "seek", "seek-01.wav"), SampleRate, channels: 1, amplitude: 0.1f, frames: 128);
			using var audio = AssertFloppyAudio(baseDirectory, "default");
			var buffer = new float[256 * 2];

			audio.Mix(buffer, frames: 128, channels: 2, [Drive(hasDisk: true, motorOn: false, cylinder: 0)]);
			Array.Clear(buffer);
			audio.Mix(buffer, frames: 128, channels: 2, [Drive(hasDisk: true, motorOn: false, cylinder: 1)]);

			Assert.True(MaxAbs(buffer) > 0.35f);
		}
		finally
		{
			Directory.Delete(baseDirectory, recursive: true);
		}
	}

	[Fact]
	public void DisabledModeDoesNotCreateMixer()
	{
		var baseDirectory = CreateTempDirectory();
		try
		{
			var audio = FloppyDriveAudio.TryCreate(
				new FloppyDriveAudioOptions(false, "default", 0.25f),
				baseDirectory,
				SampleRate,
				out var status);

			Assert.Null(audio);
			Assert.Null(status);
		}
		finally
		{
			Directory.Delete(baseDirectory, recursive: true);
		}
	}

	[Fact]
	public void VolumeIsClamped()
	{
		Assert.Equal(0f, FloppyDriveAudioOptions.ClampVolume(-1f));
		Assert.Equal(1f, FloppyDriveAudioOptions.ClampVolume(2f));
		Assert.Equal(FloppyDriveAudioOptions.DefaultVolume, FloppyDriveAudioOptions.ClampVolume(float.NaN));
	}

	[Fact]
	public void FloppyDriveAudioDoesNotLeakIntoCopperModAmigaOrCustProjects()
	{
		var root = FindWorkspaceDirectory();
		foreach (var projectDirectory in new[]
		{
			Path.Combine(root, "CopperMod.Amiga"),
			Path.Combine(root, "CopperMod.Cust")
		})
		{
			foreach (var path in Directory.EnumerateFiles(projectDirectory, "*.*", SearchOption.AllDirectories)
				.Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
					path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
			{
				var text = File.ReadAllText(path);
				Assert.DoesNotContain("FloppyDriveAudio", text, StringComparison.Ordinal);
				Assert.DoesNotContain("floppyDriveSounds", text, StringComparison.Ordinal);
			}
		}
	}

	private static FloppyDriveAudio AssertFloppyAudio(string baseDirectory, string soundPack)
	{
		var audio = FloppyDriveAudio.TryCreate(
			new FloppyDriveAudioOptions(true, soundPack, 1f),
			baseDirectory,
			SampleRate,
			out var status);
		Assert.NotNull(audio);
		Assert.Null(status);
		return audio;
	}

	private static CopperScreenDriveState Drive(bool hasDisk, bool motorOn, int cylinder)
		=> new(0, true, hasDisk, hasDisk ? "disk.adf" : string.Empty, hasDisk ? "disk.adf" : null, cylinder, 0, motorOn, true, false);

	private static string CreatePackDirectory(string baseDirectory, string name)
	{
		var directory = Path.Combine(baseDirectory, "Sounds", "Floppy", name);
		Directory.CreateDirectory(directory);
		return directory;
	}

	private static string CreateTempDirectory()
	{
		var directory = Path.Combine(Path.GetTempPath(), "copperscreen-floppy-audio-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		return directory;
	}

	private static float MaxAbs(float[] samples)
	{
		var max = 0f;
		foreach (var sample in samples)
		{
			max = Math.Max(max, Math.Abs(sample));
		}

		return max;
	}

	private static void WritePcm16Wav(string path, int sampleRate, int channels, float amplitude, int frames)
	{
		var bytesPerSample = 2;
		var dataLength = frames * channels * bytesPerSample;
		using var stream = File.Create(path);
		using var writer = new BinaryWriter(stream, Encoding.ASCII);
		writer.Write(Encoding.ASCII.GetBytes("RIFF"));
		writer.Write(36 + dataLength);
		writer.Write(Encoding.ASCII.GetBytes("WAVE"));
		writer.Write(Encoding.ASCII.GetBytes("fmt "));
		writer.Write(16);
		writer.Write((short)1);
		writer.Write((short)channels);
		writer.Write(sampleRate);
		writer.Write(sampleRate * channels * bytesPerSample);
		writer.Write((short)(channels * bytesPerSample));
		writer.Write((short)16);
		writer.Write(Encoding.ASCII.GetBytes("data"));
		writer.Write(dataLength);

		var value = (short)Math.Clamp((int)(amplitude * short.MaxValue), short.MinValue, short.MaxValue);
		for (var frame = 0; frame < frames; frame++)
		{
			for (var channel = 0; channel < channels; channel++)
			{
				writer.Write(value);
			}
		}
	}

	private static string FindWorkspaceDirectory()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory != null)
		{
			if (File.Exists(Path.Combine(directory.FullName, "CopperMod.sln")))
			{
				return directory.FullName;
			}

			directory = directory.Parent;
		}

		throw new InvalidOperationException("Could not locate the CopperMod workspace.");
	}
}
