using CopperMod.Abstractions;

namespace CopperMod.Sid.Tests;

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
	public void PsidSpeedBitsSelectCiaTimingPerSubSong()
	{
		var song = (SidSong)new SidFormat().Load(SidFixtureBuilder.CreatePsid(
			SidFixtureBuilder.SimpleToneProgram(),
			songs: 2,
			startSong: 1,
			speed: 1));
		var selector = (IModuleSubSongSelector)song;
		var options = new AudioRenderOptions(sampleRate: 44100, channelCount: 2);

		Assert.Equal(735, song.GetCurrentTickFrameCount(options));

		selector.SelectSubSong(1);

		Assert.Equal(880, song.GetCurrentTickFrameCount(options));
	}

	[Fact]
	public void PsidPlayRoutineRunsInsideTickCycleBudget()
	{
		var song = (SidSong)new SidFormat().Load(SidFixtureBuilder.CreatePsid(
			SidFixtureBuilder.SimpleToneProgram(),
			songs: 1,
			startSong: 1,
			speed: 1));
		var options = new AudioRenderOptions(sampleRate: 44100, channelCount: 2);
		var buffer = new float[options.GetSampleCount(song.GetCurrentTickFrameCount(options))];

		song.RenderTick(buffer, options);

		Assert.Equal((long)Math.Round(SidConstants.PalCpuClock / SidConstants.CiaTimerRefreshHz), GetCpuCycle(song));
	}

	[Fact]
	public void SidSongCapturesSeparateVoiceWaveforms()
	{
		var song = (SidSong)new SidFormat().Load(SidFixtureBuilder.CreatePsid(
			SidFixtureBuilder.SimpleToneProgram(),
			songs: 1,
			startSong: 1));
		var channelProvider = (IModuleChannelWaveformProvider)song;
		var options = new AudioRenderOptions(sampleRate: 44100, channelCount: 2);
		var frames = song.GetCurrentTickFrameCount(options);
		var buffer = new float[options.GetSampleCount(frames)];

		channelProvider.ChannelWaveformCaptureEnabled = true;
		song.RenderTick(buffer, options);

		Assert.NotNull(channelProvider.LastChannelWaveform);
		var waveform = channelProvider.LastChannelWaveform;
		Assert.Equal(3, waveform.Channels.Count);
		Assert.Equal(frames, waveform.SourceFrameCount);
		Assert.True(waveform.Channels[0].IsActive);
		Assert.False(waveform.Channels[1].IsActive);
		Assert.False(waveform.Channels[2].IsActive);
		Assert.Contains(waveform.Channels[0].Samples, sample => Math.Abs(sample) > 0.001f);
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
		Assert.True(buffer.Max() - buffer.Min() > 0.005f);
	}

	[Fact]
	public void RealGreenBeretSubtuneOneIntroRemainsFiniteAndNonSilentWhenPresent()
	{
		var path = FindWorkspaceFile("TestTunes", "SID", "Green_Beret.sid");
		if (!File.Exists(path))
		{
			return;
		}

		var song = (SidSong)new SidFormat().Load(File.ReadAllBytes(path));
		var options = new AudioRenderOptions(sampleRate: 44100, channelCount: 2);
		var buffer = new float[options.GetSampleCount(song.GetCurrentTickFrameCount(options))];
		var peakRms = 0.0;
		for (var frame = 0; frame < 96; frame++)
		{
			song.RenderTick(buffer, options);
			Assert.All(buffer, sample => Assert.True(float.IsFinite(sample)));
			Assert.All(buffer, sample => Assert.InRange(sample, -0.999f, 0.999f));
			if (frame >= 8)
			{
				peakRms = Math.Max(peakRms, Rms(buffer));
			}
		}

		Assert.True(peakRms > 0.015, $"Expected Green Beret subtune 1 intro to remain audible, peak RMS was {peakRms:0.000}.");
	}

	[Fact]
	public void RealGreenBeretSubtuneTenHardRestartSectionHasNoSilentFrameGapsWhenPresent()
	{
		var path = FindWorkspaceFile("TestTunes", "SID", "Green_Beret.sid");
		if (!File.Exists(path))
		{
			return;
		}

		var song = (SidSong)new SidFormat().Load(File.ReadAllBytes(path));
		var selector = (IModuleSubSongSelector)song;
		if (selector.SubSongCount <= 9)
		{
			return;
		}

		selector.SelectSubSong(9);
		var options = new AudioRenderOptions(sampleRate: 44100, channelCount: 2);
		var buffer = new float[options.GetSampleCount(song.GetCurrentTickFrameCount(options))];
		var quietestRms = double.MaxValue;
		for (var frame = 0; frame < 32; frame++)
		{
			song.RenderTick(buffer, options);
			if (frame >= 12)
			{
				quietestRms = Math.Min(quietestRms, Rms(buffer));
			}
		}

		Assert.True(quietestRms > 0.05, $"Expected Green Beret subtune 10 hard-restart frames to avoid near-silent gaps, quietest RMS was {quietestRms:0.000}.");
	}

	[Fact]
	public void RealShortCircuitFixtureUsesCiaCadenceWhenPresent()
	{
		var path = FindWorkspaceFile("TestTunes", "SID", "Short_Circuit.sid");
		if (!File.Exists(path))
		{
			return;
		}

		var song = (SidSong)new SidFormat().Load(File.ReadAllBytes(path));
		var options = new AudioRenderOptions(sampleRate: 44100, channelCount: 2);

		Assert.Equal(735, song.GetCurrentTickFrameCount(options));
	}

	[Fact]
	public void RealShortCircuitFixtureDoesNotClipResonantIntroWhenPresent()
	{
		var path = FindWorkspaceFile("TestTunes", "SID", "Short_Circuit.sid");
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
			Assert.All(buffer, sample => Assert.InRange(sample, -0.999f, 0.999f));
		}
	}

	[Fact]
	public void RealGIHeroFixtureRendersFasterThanRealtimeWhenPresent()
	{
		var path = FindWorkspaceFile("TestTunes", "SID", "Jeroen Tel", "G_I_Hero.sid");
		if (!File.Exists(path))
		{
			return;
		}

		var song = new SidFormat().Load(File.ReadAllBytes(path));
		var options = new AudioRenderOptions(sampleRate: 44100, channelCount: 2);
		var renderedFrames = 0L;
		var targetFrames = options.SampleRate * 3;
		var stopwatch = System.Diagnostics.Stopwatch.StartNew();
		while (renderedFrames < targetFrames)
		{
			var frames = song.GetCurrentTickFrameCount(options);
			var buffer = new float[options.GetSampleCount(frames)];
			var result = song.RenderTick(buffer, options);
			renderedFrames += result.FramesWritten;
		}

		stopwatch.Stop();
		var renderedSeconds = renderedFrames / (double)options.SampleRate;
		var realtimeFactor = renderedSeconds / Math.Max(0.001, stopwatch.Elapsed.TotalSeconds);
		Assert.True(
			realtimeFactor >= 1.0,
			$"Expected G_I_Hero.sid to render at least realtime; rendered {renderedSeconds:0.00}s in {stopwatch.Elapsed.TotalSeconds:0.00}s ({realtimeFactor:0.00}x).");
	}

	[Fact]
	public void RealTetrisRsidFixtureKeepsRasterInterruptPlaybackAliveWhenPresent()
	{
		var path = FindWorkspaceFile("TestTunes", "SID", "Wally Beben", "Tetris.sid");
		if (!File.Exists(path))
		{
			return;
		}

		var song = (SidSong)new SidFormat().Load(File.ReadAllBytes(path));
		var options = new AudioRenderOptions(sampleRate: 44100, channelCount: 2);
		var buffer = new float[options.GetSampleCount(song.GetCurrentTickFrameCount(options))];
		var peakRange = 0.0f;
		for (var frame = 0; frame < 200; frame++)
		{
			song.RenderTick(buffer, options);
			Assert.False(GetCpuHalted(song));
			Assert.All(buffer, sample => Assert.True(float.IsFinite(sample)));
			peakRange = Math.Max(peakRange, buffer.Max() - buffer.Min());
		}

		Assert.True(song.SidWrites.Count > 500, $"Expected Tetris RSID raster IRQ playback to keep writing SID registers, got {song.SidWrites.Count} writes.");
		Assert.True(peakRange > 0.05f, $"Expected Tetris RSID output to remain audible, peak range was {peakRange:0.000}.");
	}

	private static int GetCpuProgramCounter(SidSong song)
	{
		var machine = typeof(SidSong)
			.GetField("_machine", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
			.GetValue(song)!;
		var cpu = machine.GetType().GetProperty("Cpu")!.GetValue(machine)!;
		return (ushort)cpu.GetType().GetProperty("ProgramCounter")!.GetValue(cpu)!;
	}

	private static long GetCpuCycle(SidSong song)
	{
		var machine = typeof(SidSong)
			.GetField("_machine", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
			.GetValue(song)!;
		var cpu = machine.GetType().GetProperty("Cpu")!.GetValue(machine)!;
		return (long)cpu.GetType().GetProperty("Cycles")!.GetValue(cpu)!;
	}

	private static bool GetCpuHalted(SidSong song)
	{
		var machine = typeof(SidSong)
			.GetField("_machine", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
			.GetValue(song)!;
		var cpu = machine.GetType().GetProperty("Cpu")!.GetValue(machine)!;
		return (bool)cpu.GetType().GetProperty("Halted")!.GetValue(cpu)!;
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

			if (parts.Length > 0)
			{
				var searchRoot = Path.Combine(new[] { directory.FullName }.Concat(parts.Take(parts.Length - 1)).ToArray());
				if (Directory.Exists(searchRoot))
				{
					var recursiveCandidate = Directory.EnumerateFiles(searchRoot, parts[^1], SearchOption.AllDirectories).FirstOrDefault();
					if (recursiveCandidate != null)
					{
						return recursiveCandidate;
					}
				}
			}

			directory = directory.Parent;
		}

		return string.Join(Path.DirectorySeparatorChar, parts);
	}

	private static double Rms(IReadOnlyList<float> samples)
	{
		var sum = 0.0;
		for (var i = 0; i < samples.Count; i++)
		{
			sum += samples[i] * samples[i];
		}

		return Math.Sqrt(sum / Math.Max(1, samples.Count));
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
