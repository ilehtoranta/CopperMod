using CopperMod.Abstractions;

namespace CopperMod.Sid.Tests;

public sealed class SidLoopDetectorTests
{
	[Fact]
	public void DetectLoopFindsRepeatedSidWriteTicks()
	{
		var song = (SidSong)new SidFormat().Load(SidFixtureBuilder.CreatePsid(
			FourTickLoopProgram(),
			playAddress: 0x1006,
			songs: 1,
			startSong: 1));

		var result = ((ISidLoopDetector)song).DetectLoop(FastOptions());

		Assert.True(result.Detected);
		Assert.Equal(0, result.LoopStartTick);
		Assert.Equal(4, result.LoopEndTick);
		Assert.Equal(4, result.LoopLengthTicks);
		Assert.Equal(result.LoopEnd, result.LoopLength);
		Assert.InRange(result.LoopEnd!.Value.TotalMilliseconds, 79.0, 81.0);
		Assert.True(result.TicksAnalyzed >= 8);
	}

	[Fact]
	public void DetectLoopDoesNotMoveCurrentSongPosition()
	{
		var song = (SidSong)new SidFormat().Load(SidFixtureBuilder.CreatePsid(
			FourTickLoopProgram(),
			playAddress: 0x1006,
			songs: 1,
			startSong: 1));
		var renderOptions = new AudioRenderOptions();
		RenderNextTick(song, renderOptions);
		var positionBeforeDetection = song.Position.Time;

		var result = ((ISidLoopDetector)song).DetectLoop(FastOptions());

		Assert.True(result.Detected);
		Assert.Equal(positionBeforeDetection, song.Position.Time);
		RenderNextTick(song, renderOptions);
		Assert.True(song.Position.Time > positionBeforeDetection);
	}

	[Fact]
	public void DetectLoopReturnsNotDetectedWhenWriteTicksDoNotRepeatWithinSearchWindow()
	{
		var song = (SidSong)new SidFormat().Load(SidFixtureBuilder.CreatePsid(
			IncrementingWriteProgram(),
			playAddress: 0x1006,
			songs: 1,
			startSong: 1));

		var result = ((ISidLoopDetector)song).DetectLoop(FastOptions(maxSearchDuration: TimeSpan.FromMilliseconds(150)));

		Assert.False(result.Detected);
		Assert.Null(result.LoopEnd);
		Assert.True(result.TicksAnalyzed > 0);
		Assert.True(result.SearchDuration > TimeSpan.Zero);
	}

	[Fact]
	public void DetectDurationUsesLoopWhenWriteTicksRepeat()
	{
		var song = (SidSong)new SidFormat().Load(SidFixtureBuilder.CreatePsid(
			FourTickLoopProgram(),
			playAddress: 0x1006,
			songs: 1,
			startSong: 1));

		var result = ((ISidDurationDetector)song).DetectDuration(FastDurationOptions());

		Assert.True(result.Detected);
		Assert.Equal(SidDurationDetectionKind.Loop, result.Kind);
		Assert.NotNull(result.Loop);
		Assert.Equal(result.Loop!.LoopEnd, result.Duration);
	}

	[Fact]
	public void DetectDurationFindsSustainedSilenceAfterActivity()
	{
		var song = (SidSong)new SidFormat().Load(SidFixtureBuilder.CreatePsid(
			SilentEndingProgram(),
			playAddress: 0x1006,
			songs: 1,
			startSong: 1));

		var result = ((ISidDurationDetector)song).DetectDuration(FastDurationOptions());

		Assert.True(result.Detected);
		Assert.Equal(SidDurationDetectionKind.Silence, result.Kind);
		Assert.NotNull(result.Duration);
		Assert.InRange(result.Duration.Value.TotalMilliseconds, 80.0, 180.0);
		Assert.True(result.SearchDuration > result.Duration);
	}

	[Fact]
	public void DetectDurationDoesNotMoveCurrentSongPosition()
	{
		var song = (SidSong)new SidFormat().Load(SidFixtureBuilder.CreatePsid(
			SilentEndingProgram(),
			playAddress: 0x1006,
			songs: 1,
			startSong: 1));
		var renderOptions = new AudioRenderOptions();
		RenderNextTick(song, renderOptions);
		var positionBeforeDetection = song.Position.Time;

		var result = ((ISidDurationDetector)song).DetectDuration(FastDurationOptions());

		Assert.True(result.Detected);
		Assert.Equal(positionBeforeDetection, song.Position.Time);
		RenderNextTick(song, renderOptions);
		Assert.True(song.Position.Time > positionBeforeDetection);
	}

	private static SidLoopDetectionOptions FastOptions(TimeSpan? maxSearchDuration = null)
	{
		return new SidLoopDetectionOptions(
			maxSearchDuration: maxSearchDuration ?? TimeSpan.FromSeconds(1),
			minimumLoopDuration: TimeSpan.FromMilliseconds(1),
			maximumActiveCandidates: 256);
	}

	private static SidDurationDetectionOptions FastDurationOptions(TimeSpan? maxSearchDuration = null)
	{
		return new SidDurationDetectionOptions(
			maxSearchDuration: maxSearchDuration ?? TimeSpan.FromSeconds(1),
			minimumLoopDuration: TimeSpan.FromMilliseconds(1),
			silenceDuration: TimeSpan.FromMilliseconds(60),
			activeRangeThreshold: 0.001,
			silenceRangeThreshold: 0.0001,
			sampleRate: 11025,
			maximumActiveCandidates: 256);
	}

	private static void RenderNextTick(IModuleSong song, AudioRenderOptions options)
	{
		var frames = song.GetCurrentTickFrameCount(options);
		var buffer = new float[options.GetSampleCount(frames)];
		song.RenderTick(buffer, options);
	}

	private static byte[] FourTickLoopProgram()
	{
		return new byte[]
		{
			0xA9, 0x00,       // LDA #$00
			0x8D, 0x00, 0x20, // STA $2000
			0x60,             // RTS
			0xAE, 0x00, 0x20, // LDX $2000
			0xBD, 0x1A, 0x10, // LDA $101A,X
			0x8D, 0x00, 0xD4, // STA $D400
			0xE8,             // INX
			0xE0, 0x04,       // CPX #$04
			0xD0, 0x02,       // BNE store
			0xA2, 0x00,       // LDX #$00
			0x8E, 0x00, 0x20, // STX $2000
			0x60,             // RTS
			0x10,
			0x20,
			0x30,
			0x40
		};
	}

	private static byte[] IncrementingWriteProgram()
	{
		return new byte[]
		{
			0xA9, 0x00,       // LDA #$00
			0x8D, 0x00, 0x20, // STA $2000
			0x60,             // RTS
			0xEE, 0x00, 0x20, // INC $2000
			0xAD, 0x00, 0x20, // LDA $2000
			0x8D, 0x00, 0xD4, // STA $D400
			0x60              // RTS
		};
	}

	private static byte[] SilentEndingProgram()
	{
		return new byte[]
		{
			0xA9, 0x00,       // LDA #$00
			0x8D, 0x00, 0x20, // STA $2000
			0x60,             // RTS
			0xEE, 0x00, 0x20, // INC $2000
			0xAD, 0x00, 0x20, // LDA $2000
			0xC9, 0x06,       // CMP #$06
			0xB0, 0x20,       // BCS silence
			0x8D, 0x00, 0xD4, // STA $D400
			0xA9, 0x20,       // LDA #$20
			0x8D, 0x01, 0xD4, // STA $D401
			0xA9, 0x08,       // LDA #$08
			0x8D, 0x03, 0xD4, // STA $D403
			0xA9, 0x41,       // LDA #$41
			0x8D, 0x04, 0xD4, // STA $D404
			0xA9, 0xF0,       // LDA #$F0
			0x8D, 0x05, 0xD4, // STA $D405
			0x8D, 0x06, 0xD4, // STA $D406
			0xA9, 0x0F,       // LDA #$0F
			0x8D, 0x18, 0xD4, // STA $D418
			0x60,             // RTS
			0xC9, 0x06,       // CMP #$06
			0xD0, 0x08,       // BNE return
			0xA9, 0x00,       // LDA #$00
			0x8D, 0x18, 0xD4, // STA $D418
			0x8D, 0x04, 0xD4, // STA $D404
			0x60              // RTS
		};
	}
}
