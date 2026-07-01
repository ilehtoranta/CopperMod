using CopperMod.Abstractions;
using System.Text;

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
	public void SidSongExposesEmulationProfileController()
	{
		using var song = new SidFormat().Load(SidFixtureBuilder.CreatePsid(SidFixtureBuilder.SimpleToneProgram()));
		var controller = Assert.IsAssignableFrom<ISidEmulationProfileController>(song);

		controller.SidEmulationProfile = SidEmulationProfile.ReferenceMeasured;

		Assert.Equal(SidEmulationProfile.ReferenceMeasured, controller.SidEmulationProfile);
	}

	[Fact]
	public void SidEmulationProfileChangePreservesMutedVoices()
	{
		using var song = new SidFormat().Load(SidFixtureBuilder.CreatePsid(SidFixtureBuilder.SimpleToneProgram()));
		var controller = Assert.IsAssignableFrom<ISidEmulationProfileController>(song);
		var muteController = Assert.IsAssignableFrom<ISidVoiceMuteController>(song);
		muteController.MutedVoicesMask = 0x06;

		controller.SidEmulationProfile = SidEmulationProfile.ReferenceMeasured;

		Assert.Equal(0x06, muteController.MutedVoicesMask);
	}

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
	public void VbiPsidPlayRoutineUsesSidPlayFpDriverPhase()
	{
		var song = (SidSong)new SidFormat().Load(CreateOneWritePsid(speed: 0));
		var options = new AudioRenderOptions(sampleRate: 48000, channelCount: 1);
		var buffer = new float[options.GetSampleCount(song.GetCurrentTickFrameCount(options))];

		song.RenderTick(buffer, options);

		var write = Assert.Single(song.SidWrites.Where(write => write.Register == 0x18 && write.Value == 0x0F));
		Assert.Equal(C64Machine.SidPlayFpPsidVbiFirstPlayCycleAfterInitEntry - 1, write.Cycle);
	}

	[Fact]
	public void PsidInitThatEnablesInterruptsStillReachesScheduledPlayRoutine()
	{
		var payload = new byte[]
		{
			0x58,             // init: CLI
			0x60,             // RTS
			0xA9, 0x0F,       // play: LDA #$0F
			0x8D, 0x18, 0xD4, // STA $D418
			0x60              // RTS
		};
		var song = (SidSong)new SidFormat().Load(SidFixtureBuilder.CreatePsid(
			payload,
			playAddress: 0x1002,
			songs: 1,
			startSong: 1,
			speed: 0,
			title: "CLI Init PSID"));
		var options = new AudioRenderOptions(sampleRate: 48000, channelCount: 1);
		var buffer = new float[options.GetSampleCount(song.GetCurrentTickFrameCount(options))];

		song.RenderTick(buffer, options);

		Assert.NotEqual(0x0000, GetCpuProgramCounter(song));
		Assert.Contains(song.SidWrites, write => write.Register == 0x18 && write.Value == 0x0F);
	}

	[Fact]
	public void PsidPlayAddressZeroRunsInstalledInterruptHandler()
	{
		var payload = new byte[]
		{
			0x78,             // init: SEI
			0xA9, 0x10,       // LDA #<irq
			0x8D, 0x14, 0x03, // STA $0314
			0xA9, 0x10,       // LDA #>irq
			0x8D, 0x15, 0x03, // STA $0315
			0x58,             // CLI
			0x60,             // RTS
			0xEA, 0xEA, 0xEA, 0xEA, // pad to $1010
			0xEE, 0x00, 0x02, // irq: INC $0200
			0xAD, 0x00, 0x02, // LDA $0200
			0x8D, 0x18, 0xD4, // STA $D418
			0x4C, 0x31, 0xEA  // JMP $EA31
		};
		var song = (SidSong)new SidFormat().Load(SidFixtureBuilder.CreatePsid(
			payload,
			playAddress: 0x0000,
			songs: 1,
			startSong: 1,
			speed: 1,
			title: "PSID Play Zero IRQ"));
		var options = new AudioRenderOptions(sampleRate: 48000, channelCount: 1);

		for (var tick = 0; tick < 32; tick++)
		{
			_ = RenderNextTick(song, options);
		}

		Assert.False(GetCpuHalted(song));
		Assert.True(GetCpuStackPointer(song) > 0xE0);
		Assert.Contains(song.SidWrites, write => write.Register == 0x18 && write.Value != 0x0F);
	}

	[Fact]
	public void CiaTimedPsidPlayRoutineStillStartsAtTickStart()
	{
		var song = (SidSong)new SidFormat().Load(CreateOneWritePsid(speed: 1));
		var options = new AudioRenderOptions(sampleRate: 48000, channelCount: 1);
		var buffer = new float[options.GetSampleCount(song.GetCurrentTickFrameCount(options))];

		song.RenderTick(buffer, options);

		var write = Assert.Single(song.SidWrites.Where(write => write.Register == 0x18 && write.Value == 0x0F));
		Assert.Equal(5, write.Cycle);
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

	[Fact]
	public void LongPsidPlayRoutineContinuesAcrossRenderTicks()
	{
		var song = (SidSong)new SidFormat().Load(CreateLongPlayPsid());
		var options = new AudioRenderOptions(sampleRate: 48000, channelCount: 1);

		_ = RenderNextTick(song, options);

		Assert.Contains(song.SidWrites, write => write.Register == 0x00 && write.Value == 0x01);
		Assert.DoesNotContain(song.SidWrites, write => write.Register == 0x01 && write.Value == 0x02);

		_ = RenderNextTick(song, options);

		Assert.Contains(song.SidWrites, write => write.Register == 0x01 && write.Value == 0x02);
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
	public void RawPrgBasicProgramConsumesAutostartInputAndRunsPastFalseIfAndRemColon()
	{
		var prg = SidFixtureBuilder.CreateBasicPrg(
			(10, Basic(0x85, " A$")),
			(20, Basic(0x8B, " A$", 0xB2, "\"Q\" ", 0xA7, " ", 0x97, " 54296,0: ", 0x80)),
			(30, Basic(0x8B, " A$", 0xB2, "\"A\" ", 0xA7, " 50")),
			(40, Basic(0x80)),
			(50, Basic(0x8F, " TEST 1: BASIC WAVEFORMS")),
			(60, Basic(0x97, " 54296,15: ", 0x97, " 54272,49: ", 0x97, " 54273,28")),
			(70, Basic(0x97, " 54277,0: ", 0x97, " 54278,240: ", 0x97, " 54276,17")),
			(80, Basic(0x80)));
		using var song = (SidSong)new SidFormat().Load(new ModuleLoadContext(prg, Path.Combine(Path.GetTempPath(), "sidtest.prg")));
		var autostart = (IC64AutostartController)song;
		autostart.ScheduleAutostartKey("a", TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
		autostart.ScheduleAutostartKey("return", TimeSpan.FromMilliseconds(2), TimeSpan.FromMilliseconds(1));
		var options = new AudioRenderOptions(sampleRate: 44100, channelCount: 2);

		for (var tick = 0; tick < 8; tick++)
		{
			_ = RenderNextTick(song, options);
		}

		Assert.Contains(song.SidWrites, write => write.Register == 0x18 && write.Value == 0x0F);
		Assert.Contains(song.SidWrites, write => write.Register == 0x04 && write.Value == 0x11);
		Assert.DoesNotContain(song.SidWrites, write => write.Register == 0x18 && write.Value == 0x00);
	}

	[Fact]
	public void C64RomPathInstallsSixteenKilobyteBasicAndKernalRom()
	{
		var path = Path.Combine(Path.GetTempPath(), "CopperMod-C64Rom-" + Guid.NewGuid().ToString("N") + ".bin");
		var rom = new byte[0x4000];
		rom[0] = 0x42;
		rom[0x1FFF] = 0x43;
		rom[0x2000] = 0x99;
		rom[0x3FFF] = 0x9A;
		File.WriteAllBytes(path, rom);
		try
		{
			var prg = SidFixtureBuilder.CreateBasicPrg((10, Basic(0x80)));
			using var song = (SidSong)new SidFormat().Load(new ModuleLoadContext(prg, Path.Combine(Path.GetTempPath(), "romtest.prg")));

			((IC64RomController)song).C64RomPath = path;

			var machine = GetMachine(song);
			var basicRom = (byte[])machine.GetType()
				.GetField("_basicRom", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
				.GetValue(machine)!;
			var kernalRom = (byte[])machine.GetType()
				.GetField("_kernalRom", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
				.GetValue(machine)!;
			Assert.Equal(0x42, basicRom[0]);
			Assert.Equal(0x43, basicRom[^1]);
			Assert.Equal(0x99, kernalRom[0]);
			Assert.Equal(0x9A, kernalRom[^1]);
		}
		finally
		{
			File.Delete(path);
		}
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
	public void PsidCiaTimedInitRoutineCanSetInitialTickCadence()
	{
		var payload = new byte[]
		{
			0xA9, 0x31,       // LDA #$31
			0x8D, 0x04, 0xDC, // STA $DC04
			0xA9, 0x13,       // LDA #$13
			0x8D, 0x05, 0xDC, // STA $DC05
			0x60,             // init RTS
			0x60              // play RTS
		};
		var song = (SidSong)new SidFormat().Load(SidFixtureBuilder.CreatePsid(
			payload,
			playAddress: 0x100B,
			songs: 1,
			startSong: 1,
			speed: 1));
		var options = new AudioRenderOptions(sampleRate: 48000, channelCount: 1);

		Assert.Equal(239, song.GetCurrentTickFrameCount(options));
	}

	[Fact]
	public void RealYieArKungFuIIFixtureUsesInitProgrammedCiaCadenceWhenPresent()
	{
		var path = FindWorkspaceFile("TestTunes", "SID", "Galway", "Yie_Ar_Kung_Fu_II.sid");
		if (!File.Exists(path))
		{
			return;
		}

		var song = (SidSong)new SidFormat().Load(File.ReadAllBytes(path));
		var options = new AudioRenderOptions(sampleRate: 48000, channelCount: 1);

		Assert.Equal(239, song.GetCurrentTickFrameCount(options));
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
	public void SidAndRsidFilesDoNotExposeC64VideoFrames()
	{
		var psid = Assert.IsAssignableFrom<IC64VideoFrameProvider>(
			new SidFormat().Load(SidFixtureBuilder.CreatePsid(SidFixtureBuilder.SimpleToneProgram())));
		var rsid = Assert.IsAssignableFrom<IC64VideoFrameProvider>(
			new SidFormat().Load(SidFixtureBuilder.CreateRsid(SidFixtureBuilder.SimpleToneProgram())));

		Assert.False(psid.HasVideoFrameSource);
		Assert.False(psid.TryGetLatestVideoFrame(out _));
		Assert.False(rsid.HasVideoFrameSource);
		Assert.False(rsid.TryGetLatestVideoFrame(out _));
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

		Assert.Equal(687, song.GetCurrentTickFrameCount(options));
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
	public void RealGIHeroFixtureRendersFiniteAudibleOutputWhenPresent()
	{
		var path = FindWorkspaceFile("TestTunes", "SID", "Jeroen Tel", "G_I_Hero.sid");
		if (!File.Exists(path))
		{
			return;
		}

		var song = (SidSong)new SidFormat().Load(File.ReadAllBytes(path));
		var options = new AudioRenderOptions(sampleRate: 44100, channelCount: 2);
		var renderedFrames = 0L;
		var targetFrames = options.SampleRate * 3;
		var peakRange = 0.0f;
		var peakRms = 0.0;
		while (renderedFrames < targetFrames)
		{
			var frames = song.GetCurrentTickFrameCount(options);
			var buffer = new float[options.GetSampleCount(frames)];
			var result = song.RenderTick(buffer, options);
			Assert.True(result.FramesWritten > 0);
			Assert.All(buffer, sample => Assert.True(float.IsFinite(sample)));
			peakRange = Math.Max(peakRange, buffer.Max() - buffer.Min());
			peakRms = Math.Max(peakRms, Rms(buffer));
			renderedFrames += result.FramesWritten;
		}

		Assert.False(GetCpuHalted(song));
		Assert.True(renderedFrames >= targetFrames);
		Assert.True(peakRange > 0.05f, $"Expected G_I_Hero.sid to produce audible output, peak range was {peakRange:0.000}.");
		Assert.True(peakRms > 0.005, $"Expected G_I_Hero.sid to produce non-trivial RMS output, peak RMS was {peakRms:0.000}.");
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
	public void RsidInitThatReturnsAfterHostTimeoutFallsBackToIdleLoop()
	{
		var program = new byte[]
		{
			0xA2, 0x00,       // LDX #$00; 256 outer loops
			0xA0, 0x00,       // outer: LDY #$00; 256 inner loops
			0x88,             // inner: DEY
			0xD0, 0xFD,       // BNE inner
			0xCA,             // DEX
			0xD0, 0xF8,       // BNE outer; total delay exceeds the init watchdog
			0xA9, 0x0F,       // LDA #$0F
			0x8D, 0x18, 0xD4, // STA $D418
			0x60              // RTS after the watchdog has left the init running
		};
		var song = (SidSong)new SidFormat().Load(SidFixtureBuilder.CreateRsid(program));
		var options = new AudioRenderOptions(sampleRate: 48000, channelCount: 1);

		for (var tick = 0; tick < 32; tick++)
		{
			_ = RenderNextTick(song, options);
		}

		var pc = GetCpuProgramCounter(song);
		Assert.False(GetCpuHalted(song));
		Assert.InRange(pc, 0xFF94, 0xFF98);
		Assert.Contains(song.SidWrites, write => write.Register == 0x18 && write.Value == 0x0F);
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
		var machine = GetMachine(song);
		var cpu = machine.GetType().GetProperty("Cpu")!.GetValue(machine)!;
		return (ushort)cpu.GetType().GetProperty("ProgramCounter")!.GetValue(cpu)!;
	}

	private static long GetCpuCycle(SidSong song)
	{
		var machine = GetMachine(song);
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
		var machine = GetMachine(song);
		var cpu = machine.GetType().GetProperty("Cpu")!.GetValue(machine)!;
		return (bool)cpu.GetType().GetProperty("Halted")!.GetValue(cpu)!;
	}

	private static int GetCpuStackPointer(SidSong song)
	{
		var machine = GetMachine(song);
		var cpu = machine.GetType().GetProperty("Cpu")!.GetValue(machine)!;
		return (byte)cpu.GetType().GetProperty("StackPointer")!.GetValue(cpu)!;
	}

	private static byte[] CreateOneWritePsid(uint speed)
	{
		return SidFixtureBuilder.CreatePsid(
			new byte[]
			{
				0x60,             // init: RTS
				0xA9, 0x0F,       // play: LDA #$0F
				0x8D, 0x18, 0xD4, // STA $D418
				0x60              // RTS
			},
			loadAddress: 0x1000,
			initAddress: 0x1000,
			playAddress: 0x1001,
			songs: 1,
			startSong: 1,
			speed: speed,
			title: "One Write Phase");
	}

	private static byte[] CreateLongPlayPsid()
	{
		return SidFixtureBuilder.CreatePsid(
			new byte[]
			{
				0x60,             // init: RTS
				0xA9, 0x01,       // play: LDA #$01
				0x8D, 0x00, 0xD4, // STA $D400
				0xA0, 0x14,       // LDY #$14
				0xA2, 0xFF,       // LDX #$FF
				0xCA,             // DEX
				0xD0, 0xFD,       // BNE DEX
				0x88,             // DEY
				0xD0, 0xF8,       // BNE LDX #$FF
				0xA9, 0x02,       // LDA #$02
				0x8D, 0x01, 0xD4, // STA $D401
				0x60              // RTS
			},
			loadAddress: 0x1000,
			initAddress: 0x1000,
			playAddress: 0x1001,
			songs: 1,
			startSong: 1,
			speed: 1,
			title: "Long Play");
	}

	private static object GetMachine(SidSong song)
	{
		return typeof(SidSong)
			.GetField("_machine", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
			.GetValue(song)!;
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

	private static byte[] Basic(params object[] parts)
	{
		var bytes = new List<byte>();
		foreach (var part in parts)
		{
			switch (part)
			{
				case int value:
					bytes.Add((byte)value);
					break;
				case string text:
					bytes.AddRange(Encoding.ASCII.GetBytes(text));
					break;
				default:
					throw new ArgumentException("Unsupported BASIC token part: " + part, nameof(parts));
			}
		}

		return bytes.ToArray();
	}

	private static bool IsControlRegister(byte register)
	{
		return register == 4 || register == 11 || register == 18;
	}
}
