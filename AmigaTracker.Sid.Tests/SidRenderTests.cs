using AmigaTracker.Abstractions;

namespace AmigaTracker.Sid.Tests;

public sealed class SidRenderTests
{
	[Fact]
	public void RenderProducesFiniteNonZeroPcmAndAdvancesPosition()
	{
		var song = new SidFormat().Load(SidFixtureBuilder.CreatePsid(SidFixtureBuilder.SimpleToneProgram()));
		var options = new AudioRenderOptions(sampleRate: 44100, channelCount: 2);
		var frames = song.GetCurrentTickFrameCount(options);
		var buffer = new float[options.GetSampleCount(frames)];

		var result = song.RenderTick(buffer, options);

		Assert.Equal(frames, result.FramesWritten);
		Assert.Equal(buffer.Length, result.SamplesWritten);
		Assert.True(result.Position.Time > TimeSpan.Zero);
		Assert.All(buffer, sample => Assert.True(float.IsFinite(sample)));
		Assert.Contains(buffer, sample => Math.Abs(sample) > 0.0001f);
	}

	[Fact]
	public void SelectSubSongResetsAndChangesInitRegisterWrite()
	{
		var song = (SidSong)new SidFormat().Load(SidFixtureBuilder.CreatePsid(SidFixtureBuilder.SimpleToneProgram()));
		var selector = (IModuleSubSongSelector)song;

		selector.SelectSubSong(0);
		var firstWrite = song.SidWrites.First(write => write.Register == 0);
		selector.SelectSubSong(1);
		var secondWrite = song.SidWrites.First(write => write.Register == 0);

		Assert.Equal(0, firstWrite.Value);
		Assert.Equal(1, secondWrite.Value);
		Assert.Equal(1, selector.CurrentSubSongIndex);
	}

	[Fact]
	public void SidWritesAreTimestampedInCycleOrder()
	{
		var song = (SidSong)new SidFormat().Load(SidFixtureBuilder.CreatePsid(SidFixtureBuilder.SimpleToneProgram()));
		var options = new AudioRenderOptions(sampleRate: 44100, channelCount: 2);
		var buffer = new float[options.GetSampleCount(song.GetCurrentTickFrameCount(options))];

		song.RenderTick(buffer, options);
		var writes = song.SidWrites;

		Assert.True(writes.Count > 4);
		Assert.Equal(writes.OrderBy(write => write.Cycle).Select(write => write.Cycle), writes.Select(write => write.Cycle));
		Assert.Contains(writes, write => write.Register == 0x18 && write.Value == 0x0F);
	}

	[Fact]
	public void SidClockIsResetAfterInitRoutine()
	{
		var song = (SidSong)new SidFormat().Load(SidFixtureBuilder.CreatePsid(
			SidFixtureBuilder.InitThenPlayToneProgram(),
			playAddress: 0x1009));
		var options = new AudioRenderOptions(sampleRate: 44100, channelCount: 2);
		var buffer = new float[options.GetSampleCount(song.GetCurrentTickFrameCount(options))];

		song.RenderTick(buffer, options);

		Assert.True(buffer.Max() - buffer.Min() > 0.05f);
	}

	[Fact]
	public void RealArkanoidFixtureIsRecognizedWhenPresent()
	{
		var path = FindWorkspaceFile("TestTunes", "SID", "Arkanoid.sid");
		if (!File.Exists(path))
		{
			return;
		}

		var data = File.ReadAllBytes(path);
		var format = new SidFormat();

		Assert.True(format.CanLoad(data));
		var song = format.Load(data);
		var options = new AudioRenderOptions(sampleRate: 44100, channelCount: 2);
		var buffer = new float[options.GetSampleCount(song.GetCurrentTickFrameCount(options))];
		var result = song.RenderTick(buffer, options);

		Assert.True(result.Position.Time > TimeSpan.Zero);
		Assert.All(buffer, sample => Assert.True(float.IsFinite(sample)));
		Assert.True(song.Metadata.Tags.ContainsKey("ChipModel"));
	}

	[Fact]
	public void RealArkanoidFixtureDrivesOscillatorVoicesWhenPresent()
	{
		var path = FindWorkspaceFile("TestTunes", "SID", "Arkanoid.sid");
		if (!File.Exists(path))
		{
			return;
		}

		var song = (SidSong)new SidFormat().Load(File.ReadAllBytes(path));
		var options = new AudioRenderOptions(sampleRate: 44100, channelCount: 2);
		var buffer = new float[options.GetSampleCount(song.GetCurrentTickFrameCount(options))];
		for (var frame = 0; frame < 80; frame++)
		{
			song.RenderTick(buffer, options);
		}

		var writes = song.SidWrites;
		Assert.Contains(writes, write => IsVoiceRegister(write.Register));
		Assert.Contains(writes, write => IsControlRegister(write.Register) && (write.Value & 0xF0) != 0);
	}

	[Fact]
	public void RealArkanoidFixtureProducesPulseWaveformAroundSevenSecondsWhenPresent()
	{
		var path = FindWorkspaceFile("TestTunes", "SID", "Arkanoid.sid");
		if (!File.Exists(path))
		{
			return;
		}

		var song = (SidSong)new SidFormat().Load(File.ReadAllBytes(path));
		var options = new AudioRenderOptions(sampleRate: 44100, channelCount: 2);
		var buffer = new float[options.GetSampleCount(song.GetCurrentTickFrameCount(options))];
		var largestRange = 0.0f;
		for (var frame = 0; frame < 371; frame++)
		{
			song.RenderTick(buffer, options);
			if (frame < 330)
			{
				continue;
			}

			var range = buffer.Max() - buffer.Min();
			largestRange = Math.Max(largestRange, range);
		}

		Assert.True(largestRange > 0.2f, $"Expected Arkanoid pulse voices near 7s to produce non-DC PCM, got range {largestRange:0.000}.");
	}

	[Fact]
	public void RealGreenBeretFixtureDoesNotRunCpuGarbageAfterPsidPlayReturnsWhenPresent()
	{
		var path = FindWorkspaceFile("TestTunes", "SID", "Green_Beret.sid");
		if (!File.Exists(path))
		{
			return;
		}

		var song = (SidSong)new SidFormat().Load(File.ReadAllBytes(path));
		var options = new AudioRenderOptions(sampleRate: 44100, channelCount: 2);
		var buffer = new float[options.GetSampleCount(song.GetCurrentTickFrameCount(options))];
		for (var frame = 0; frame < 20; frame++)
		{
			song.RenderTick(buffer, options);
		}

		Assert.Equal(0xFFFF, GetCpuProgramCounter(song));
		Assert.True(buffer.Max() - buffer.Min() > 0.05f);
	}

	private static int GetCpuProgramCounter(SidSong song)
	{
		var machine = typeof(SidSong)
			.GetField("_machine", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
			.GetValue(song)!;
		var cpu = machine.GetType().GetProperty("Cpu")!.GetValue(machine)!;
		return (ushort)cpu.GetType().GetProperty("ProgramCounter")!.GetValue(cpu)!;
	}

	private static string FindWorkspaceFile(params string[] parts)
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory != null)
		{
			var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
			if (File.Exists(candidate))
			{
				return candidate;
			}

			directory = directory.Parent;
		}

		return string.Join(Path.DirectorySeparatorChar, parts);
	}

	private static bool IsVoiceRegister(byte register)
	{
		var offset = register % 7;
		return register < 21 && offset <= 6;
	}

	private static bool IsControlRegister(byte register)
	{
		return register == 4 || register == 11 || register == 18;
	}
}
