using CopperMod.Abstractions;

namespace CopperMod.Sid.Tests;

public sealed class SidRenderTests
{
	public static TheoryData<string, int, string[]> MixerFilterWorkloads { get; } = new()
	{
		{ "Commando #1", 0, new[] { "TestTunes", "SID", "Tough", "Commando.sid" } },
		{ "Great Giana Sisters #5", 4, new[] { "TestTunes", "SID", "Tough", "Great_Giana_Sisters.sid" } },
		{ "Spijkerhoek #1", 0, new[] { "TestTunes", "SID", "Tough", "Spijkerhoek.sid" } },
		{ "Flimbo intro #1", 0, new[] { "TestTunes", "SID", "Tough", "Flimbos_Quest_intro.sid" } },
		{ "Tetris RSID #1", 0, new[] { "TestTunes", "SID", "Wally Beben", "Tetris.sid" } },
	};

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

		Assert.Equal(879, song.GetCurrentTickFrameCount(options));
	}

	[Fact]
	public void GetCurrentTickFrameCountPeeksWithoutAdvancingSampleClock()
	{
		var song = (SidSong)new SidFormat().Load(SidFixtureBuilder.CreatePsid(SidFixtureBuilder.SimpleToneProgram()));
		var options = new AudioRenderOptions(sampleRate: 44100, channelCount: 2);

		Assert.Equal(879, song.GetCurrentTickFrameCount(options));
		Assert.Equal(879, song.GetCurrentTickFrameCount(options));

		var buffer = new float[options.GetSampleCount(song.GetCurrentTickFrameCount(options))];
		var result = song.RenderTick(buffer, options);

		Assert.Equal(879, result.FramesWritten);
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

		Assert.Equal(SidIntegerMath.DivRoundNearest(SidConstants.PalCpuCyclesPerSecond, SidConstants.CiaTimerRefreshHz), GetCpuCycle(song));
	}

	[Theory]
	[MemberData(nameof(MixerFilterWorkloads))]
	public void RealMixerFilterWorkloadsRemainFiniteAndAudibleWhenPresent(string name, int subSongIndex, string[] pathParts)
	{
		var path = FindWorkspaceFile(pathParts);
		if (!File.Exists(path))
		{
			return;
		}

		var song = new SidFormat().Load(File.ReadAllBytes(path));
		if (subSongIndex != 0)
		{
			var selector = (IModuleSubSongSelector)song;
			if (subSongIndex >= selector.SubSongCount)
			{
				return;
			}

			selector.SelectSubSong(subSongIndex);
		}

		var options = new AudioRenderOptions(sampleRate: 44100, channelCount: 2);
		var peakRange = 0.0f;
		var ticks = name.Contains("Tetris", StringComparison.Ordinal) ? 180 : 48;
		for (var tick = 0; tick < ticks; tick++)
		{
			var buffer = RenderNextTick(song, options);
			Assert.All(buffer, sample => Assert.True(float.IsFinite(sample)));
			peakRange = Math.Max(peakRange, buffer.Max() - buffer.Min());
		}

		Assert.True(peakRange > 0.001f, $"{name} should stay audible after mixer/filter hot-path changes, peak range was {peakRange:0.000000}.");
	}

	[Fact]
	public void PsidCiaTimedPlayRoutineCanSetNextTickCadence()
	{
		var payload = new byte[]
		{
			0x60,             // init RTS
			0xA9, 0x0B,       // LDA #$0B
			0x8D, 0x05, 0xDC, // STA $DC05
			0xA9, 0xCB,       // LDA #$CB
			0x8D, 0x04, 0xDC, // STA $DC04
			0x60              // play RTS
		};
		var song = (SidSong)new SidFormat().Load(SidFixtureBuilder.CreatePsid(
			payload,
			playAddress: 0x1001,
			songs: 1,
			startSong: 1,
			speed: 1));
		var options = new AudioRenderOptions(sampleRate: 44100, channelCount: 2);
		var firstFrames = song.GetCurrentTickFrameCount(options);
		var buffer = new float[options.GetSampleCount(firstFrames)];

		song.RenderTick(buffer, options);

		const int expectedFrames = 135;
		Assert.Equal(735, firstFrames);
		Assert.Equal(expectedFrames, song.GetCurrentTickFrameCount(options));
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
		for (var frame = 0; frame < 80; frame++)
		{
			_ = RenderNextTick(song, options);
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
		var largestRange = 0.0f;
		for (var frame = 0; frame < 371; frame++)
		{
			var buffer = RenderNextTick(song, options);
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
		var lastBuffer = Array.Empty<float>();
		for (var frame = 0; frame < 20; frame++)
		{
			lastBuffer = RenderNextTick(song, options);
		}

		Assert.Equal(0xFFFF, GetCpuProgramCounter(song));
		Assert.True(lastBuffer.Max() - lastBuffer.Min() > 0.005f);
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
		var peakRms = 0.0;
		for (var frame = 0; frame < 96; frame++)
		{
			var buffer = RenderNextTick(song, options);
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
		var quietestRms = double.MaxValue;
		for (var frame = 0; frame < 32; frame++)
		{
			var buffer = RenderNextTick(song, options);
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
		for (var frame = 0; frame < 80; frame++)
		{
			var buffer = RenderNextTick(song, options);
			Assert.All(buffer, sample => Assert.InRange(sample, -0.999f, 0.999f));
		}
	}

	[Fact]
	public void RealWizballDefaultTuneExercisesFilteredPulseWidthModulationWhenPresent()
	{
		var path = FindWorkspaceFile("TestTunes", "SID", "Galway", "Wizball.sid");
		if (!File.Exists(path))
		{
			return;
		}

		var song = (SidSong)new SidFormat().Load(File.ReadAllBytes(path));
		var selector = (IModuleSubSongSelector)song;
		Assert.Equal(3, selector.CurrentSubSongIndex);
		var options = new AudioRenderOptions(sampleRate: 44100, channelCount: 2);
		var peakRms = 0.0;
		for (var frame = 0; frame < 1200; frame++)
		{
			var buffer = RenderNextTick(song, options);
			Assert.All(buffer, sample => Assert.True(float.IsFinite(sample)));
			Assert.All(buffer, sample => Assert.InRange(sample, -0.999f, 0.999f));
			if (frame >= 16)
			{
				peakRms = Math.Max(peakRms, Rms(buffer));
			}
		}

		Assert.Contains(song.SidWrites, write => write.Register == 0x17 && write.Value == 0xF1);
		Assert.Contains(song.SidWrites, write => write.Register == 0x18 && write.Value == 0x7F);
		Assert.True(song.SidWrites.Count(write => write.Register == 0x02 || write.Register == 0x03) > 1500);
		Assert.True(peakRms > 0.03, $"Expected Wizball filtered PWM title voice to remain audible, peak RMS was {peakRms:0.000}.");
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
		var peakRange = 0.0f;
		for (var frame = 0; frame < 200; frame++)
		{
			var buffer = RenderNextTick(song, options);
			Assert.False(GetCpuHalted(song));
			Assert.All(buffer, sample => Assert.True(float.IsFinite(sample)));
			peakRange = Math.Max(peakRange, buffer.Max() - buffer.Min());
		}

		Assert.True(song.SidWrites.Count > 500, $"Expected Tetris RSID raster IRQ playback to keep writing SID registers, got {song.SidWrites.Count} writes.");
		Assert.True(peakRange > 0.05f, $"Expected Tetris RSID output to remain audible, peak range was {peakRange:0.000}.");
	}

	[Fact]
	public void RealGameOverDigiSectionStaysFiniteAndAvoidsSharpAliasedJumpsWhenPresent()
	{
		var path = FindWorkspaceFile("TestTunes", "SID", "Galway", "Game_Over.sid");
		if (!File.Exists(path))
		{
			return;
		}

		var song = (SidSong)new SidFormat().Load(File.ReadAllBytes(path));
		var options = new AudioRenderOptions(sampleRate: 44100, channelCount: 2);
		var frameIndex = 0L;
		var largestDigiSectionJump = 0.0f;
		var previous = 0.0f;
		var hasPrevious = false;
		for (var tick = 0; tick < 1100; tick++)
		{
			var buffer = RenderNextTick(song, options);
			Assert.All(buffer, sample => Assert.True(float.IsFinite(sample)));
			for (var i = 0; i < buffer.Length; i += options.ChannelCount)
			{
				var sample = buffer[i];
				var seconds = frameIndex / (double)options.SampleRate;
				if (hasPrevious && seconds >= 18.0 && seconds <= 21.0)
				{
					largestDigiSectionJump = Math.Max(largestDigiSectionJump, Math.Abs(sample - previous));
				}

				previous = sample;
				hasPrevious = true;
				frameIndex++;
			}
		}

		var d418Writes = song.SidWrites.Count(write => write.Register == 0x18);
		Assert.True(d418Writes > 1000, $"Expected Game Over to exercise high-rate D418 digi playback, got {d418Writes} D418 writes.");
		Assert.True(largestDigiSectionJump < 0.75f, $"Expected SID-cycle output smoothing to keep Game Over digi/pulse section clean, largest jump was {largestDigiSectionJump:0.000}.");
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

	private static float[] RenderNextTick(IModuleSong song, AudioRenderOptions options)
	{
		var frames = song.GetCurrentTickFrameCount(options);
		var buffer = new float[options.GetSampleCount(frames)];
		song.RenderTick(buffer, options);
		return buffer;
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
