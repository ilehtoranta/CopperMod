using CopperMod.Abstractions;
using CopperMod.Rendering;
using CopperMod.Sid;

namespace CopperMod.Tests;

public sealed class ModuleSampleProviderTests
{
	[Fact]
	public void ReadStopsAtEndOfSongInsteadOfPaddingSilenceForever()
	{
		var song = new FiniteSong(ticksBeforeEnd: 2, framesPerTick: 2);
		using var provider = new ModuleSampleProvider(song, sampleRate: 44100, channelCount: 1, AmigaOutputProfile.None);
		var buffer = new float[10];
		var endEvents = 0;
		provider.EndOfSongReached += (_, _) => endEvents++;

		var samplesRead = provider.Read(buffer, 0, buffer.Length);

		Assert.Equal(4, samplesRead);
		Assert.True(provider.EndOfSong);
		Assert.Equal(1, endEvents);
		Assert.All(buffer.Take(samplesRead), sample => Assert.Equal(0.25f, sample));
		Assert.All(buffer.Skip(samplesRead), sample => Assert.Equal(0.0f, sample));
		Assert.Equal(0, provider.Read(buffer, 0, buffer.Length));
		Assert.Equal(1, endEvents);
	}

	[Fact]
	public void InitialLeadInWritesSilenceWithoutAdvancingSong()
	{
		var song = new FiniteSong(ticksBeforeEnd: 1, framesPerTick: 1);
		using var provider = new ModuleSampleProvider(
			song,
			sampleRate: 10,
			channelCount: 1,
			AmigaOutputProfile.None,
			TimeSpan.FromMilliseconds(200));
		var buffer = new float[4];

		var firstRead = provider.Read(buffer, 0, buffer.Length);

		Assert.Equal(3, firstRead);
		Assert.Equal(1, song.RenderedTicks);
		Assert.Equal(0.0f, buffer[0]);
		Assert.Equal(0.0f, buffer[1]);
		Assert.Equal(0.25f, buffer[2]);
		Assert.Equal(0.0f, buffer[3]);
	}

	[Fact]
	public void ResetClearsEndOfSongState()
	{
		var song = new FiniteSong(ticksBeforeEnd: 1, framesPerTick: 1);
		using var provider = new ModuleSampleProvider(song, sampleRate: 44100, channelCount: 1, AmigaOutputProfile.None);
		var buffer = new float[2];

		Assert.Equal(1, provider.Read(buffer, 0, buffer.Length));
		Assert.True(provider.EndOfSong);

		provider.Reset();

		Assert.False(provider.EndOfSong);
		Assert.Equal(1, provider.Read(buffer, 0, buffer.Length));
	}

	[Fact]
	public void SelectSubSongClearsBufferedAudioAndRestartsPosition()
	{
		var song = new SelectableSubSongSong(framesPerTick: 2);
		using var provider = new ModuleSampleProvider(song, sampleRate: 10, channelCount: 1, AmigaOutputProfile.None);
		var buffer = new float[2];

		Assert.Equal(2, provider.Read(buffer, 0, buffer.Length));
		Assert.All(buffer, sample => Assert.Equal(0.125f, sample));

		provider.SelectSubSong(1);

		Assert.Equal(TimeSpan.Zero, provider.Position.Time);
		Assert.Equal(2, provider.Read(buffer, 0, buffer.Length));
		Assert.All(buffer, sample => Assert.Equal(0.5f, sample));
		Assert.Equal(1, song.CurrentSubSongIndex);
	}

	[Fact]
	public async Task SelectSubSongWaitsForActiveRender()
	{
		using var releaseRender = new ManualResetEventSlim(false);
		var song = new BlockingSubSong(releaseRender);
		using var provider = new ModuleSampleProvider(song, sampleRate: 10, channelCount: 1, AmigaOutputProfile.None);

		Assert.True(song.RenderStarted.Wait(TimeSpan.FromSeconds(1)));
		var selectTask = Task.Run(() => provider.SelectSubSong(1));
		try
		{
			var completedEarly = await Task.WhenAny(selectTask, Task.Delay(TimeSpan.FromMilliseconds(100))) == selectTask;
			Assert.False(completedEarly);
			Assert.Equal(0, song.SelectCalls);
			Assert.False(song.SelectedDuringRender);

			releaseRender.Set();

			var completed = await Task.WhenAny(selectTask, Task.Delay(TimeSpan.FromSeconds(1))) == selectTask;
			Assert.True(completed);
			await selectTask;
			Assert.Equal(1, song.SelectCalls);
			Assert.Equal(1, song.CurrentSubSongIndex);
			Assert.False(song.SelectedDuringRender);
		}
		finally
		{
			releaseRender.Set();
			await Task.WhenAny(selectTask, Task.Delay(TimeSpan.FromSeconds(1)));
		}
	}

	[Fact]
	public void ReadDoesNotPublishWaveformByDefault()
	{
		var song = new FiniteSong(ticksBeforeEnd: 1, framesPerTick: 4);
		using var provider = new ModuleSampleProvider(song, sampleRate: 44100, channelCount: 1, AmigaOutputProfile.None);
		var buffer = new float[4];

		WaitForBufferedAudio(provider);
		var samplesRead = provider.Read(buffer, 0, buffer.Length);

		Assert.Equal(4, samplesRead);
		Assert.False(provider.TryReadWaveformSnapshot(out _));
	}

	[Fact]
	public void ReadStoresWaveformForPolling()
	{
		var song = new FiniteSong(ticksBeforeEnd: 1, framesPerTick: 4);
		using var provider = new ModuleSampleProvider(song, sampleRate: 44100, channelCount: 1, AmigaOutputProfile.None)
		{
			WaveformEnabled = true
		};
		var buffer = new float[4];

		WaitForBufferedAudio(provider);
		var samplesRead = provider.Read(buffer, 0, buffer.Length);

		Assert.Equal(4, samplesRead);
		Assert.True(provider.TryReadWaveformSnapshot(out var waveform));
		Assert.Equal(4, waveform.SourceFrameCount);
		Assert.All(waveform.Minimums, sample => Assert.Equal(0.25f, sample));
		Assert.All(waveform.Maximums, sample => Assert.Equal(0.25f, sample));
	}

	[Fact]
	public void ReadPublishesMixedWaveformByDefaultWhenSongProvidesChannelWaveforms()
	{
		var song = new ChannelWaveformSong(framesPerTick: 4);
		using var provider = new ModuleSampleProvider(song, sampleRate: 44100, channelCount: 1, AmigaOutputProfile.None)
		{
			WaveformEnabled = true
		};
		var buffer = new float[4];

		WaitForBufferedAudio(provider);
		var samplesRead = provider.Read(buffer, 0, buffer.Length);

		Assert.Equal(4, samplesRead);
		Assert.False(song.ChannelWaveformCaptureEnabled);
		Assert.True(provider.TryReadWaveformSnapshot(out var waveform));
		Assert.Equal(1, waveform.ChannelCount);
		Assert.All(waveform.Minimums, sample => Assert.Equal(0.125f, sample));
		Assert.All(waveform.Maximums, sample => Assert.Equal(0.125f, sample));
	}

	[Fact]
	public void ReadPublishesTrackerChannelsWhenTrackerChannelsConfigured()
	{
		var song = new ChannelWaveformSong(framesPerTick: 4);
		using var provider = new ModuleSampleProvider(song, sampleRate: 44100, channelCount: 1, AmigaOutputProfile.None);
		provider.WaveformEnabled = true;
		provider.WaveformDisplayMode = WaveformDisplayMode.TrackerChannels;
		provider.Reset();
		var buffer = new float[4];

		WaitForBufferedAudio(provider);
		var samplesRead = provider.Read(buffer, 0, buffer.Length);

		Assert.Equal(4, samplesRead);
		Assert.True(song.ChannelWaveformCaptureEnabled);
		Assert.True(provider.TryReadWaveformSnapshot(out var waveform));
		Assert.Equal(2, waveform.ChannelCount);
		Assert.Equal(0, waveform.Channels[0].ChannelIndex);
		Assert.Equal(1, waveform.Channels[1].ChannelIndex);
		Assert.Equal(-1.0f, waveform.Channels[0].Minimums.Min());
		Assert.Equal(1.0f, waveform.Channels[0].Maximums.Max());
		Assert.Equal(-0.5f, waveform.Channels[1].Minimums.Min());
		Assert.Equal(0.5f, waveform.Channels[1].Maximums.Max());
	}

	[Fact]
	public void WaveformPollingReturnsEachRenderedSnapshotOnlyOnce()
	{
		var song = new FiniteSong(ticksBeforeEnd: 1, framesPerTick: 4);
		using var provider = new ModuleSampleProvider(song, sampleRate: 44100, channelCount: 1, AmigaOutputProfile.None)
		{
			WaveformEnabled = true
		};
		var buffer = new float[4];

		WaitForBufferedAudio(provider);
		_ = provider.Read(buffer, 0, buffer.Length);

		Assert.True(provider.TryReadWaveformSnapshot(out _));
		Assert.False(provider.TryReadWaveformSnapshot(out _));
	}

	[Fact]
	public void WaveformFollowsConsumedAudioInsteadOfRenderAheadProducer()
	{
		var song = new SequentialChunkSong(framesPerTick: 4);
		using var provider = new ModuleSampleProvider(song, sampleRate: 10, channelCount: 1, AmigaOutputProfile.None)
		{
			WaveformEnabled = true
		};
		var buffer = new float[4];

		WaitForBufferedAudio(provider);

		Assert.False(provider.TryReadWaveformSnapshot(out _));

		var samplesRead = provider.Read(buffer, 0, buffer.Length);

		Assert.Equal(4, samplesRead);
		Assert.All(buffer, sample => Assert.Equal(0.25f, sample));
		Assert.True(provider.TryReadWaveformSnapshot(out var waveform));
		Assert.Equal(4, waveform.SourceFrameCount);
		Assert.All(waveform.Minimums, sample => Assert.Equal(0.25f, sample));
		Assert.All(waveform.Maximums, sample => Assert.Equal(0.25f, sample));
	}

	[Fact]
	public void ReadBypassesAmigaOutputStageForC64OutputFamily()
	{
		var song = new OutputFamilySong(ModuleOutputFamily.Commodore64);
		using var provider = new ModuleSampleProvider(
			song,
			sampleRate: 44100,
			channelCount: 1,
			AmigaOutputProfile.A500LedFilter,
			c64OutputProfile: C64OutputProfile.Clean);
		var buffer = new float[4];

		WaitForBufferedAudio(provider);
		var samplesRead = provider.Read(buffer, 0, buffer.Length);

		Assert.Equal(4, samplesRead);
		Assert.All(buffer, sample => Assert.Equal(0.25f, sample));
		Assert.Equal(0, provider.BufferStatus.UnderrunCount);
	}

	[Fact]
	public void C64VideoRenderingIsThrottledBehindAudioRenderAhead()
	{
		var song = new C64VideoSong(framesPerTick: 10);
		using var provider = new ModuleSampleProvider(
			song,
			sampleRate: 1000,
			channelCount: 1,
			AmigaOutputProfile.None,
			c64OutputProfile: C64OutputProfile.Clean);

		Assert.True(
			SpinWait.SpinUntil(
				() => provider.BufferStatus.QueuedDuration >= TimeSpan.FromSeconds(4.5),
				TimeSpan.FromSeconds(2)));

		var renderedTicks = song.RenderedTicks;
		var videoFrameRequests = song.VideoFrameRequests;

		Assert.True(renderedTicks >= 400, $"Expected the audio render-ahead queue to fill, rendered {renderedTicks} ticks.");
		Assert.InRange(videoFrameRequests, 1, 30);
		Assert.True(
			videoFrameRequests < renderedTicks / 10,
			$"Video captured too often: {videoFrameRequests} requests for {renderedTicks} audio ticks.");
	}

	[Fact]
	public void ReadReturnsSilenceQuicklyWhenRenderAheadProducerIsLate()
	{
		using var releaseRender = new ManualResetEventSlim(false);
		var song = new BlockingSong(releaseRender);
		using var provider = new ModuleSampleProvider(song, sampleRate: 44100, channelCount: 1, AmigaOutputProfile.None);
		var buffer = new float[4];

		Assert.True(song.RenderStarted.Wait(TimeSpan.FromSeconds(1)));
		var stopwatch = System.Diagnostics.Stopwatch.StartNew();
		var samplesRead = provider.Read(buffer, 0, buffer.Length);
		stopwatch.Stop();
		releaseRender.Set();

		Assert.Equal(buffer.Length, samplesRead);
		Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(250), $"Read took {stopwatch.Elapsed.TotalMilliseconds:0} ms.");
		Assert.All(buffer, sample => Assert.Equal(0.0f, sample));
	}

	private static void WaitForBufferedAudio(ModuleSampleProvider provider)
	{
		Assert.True(
			SpinWait.SpinUntil(
				() =>
				{
					var status = provider.BufferStatus;
					return status.QueuedDuration > TimeSpan.Zero || status.ProducerEnded || status.EndOfSong;
				},
				TimeSpan.FromSeconds(2)));
	}

	private sealed class FiniteSong : IModuleSong
	{
		private readonly int _ticksBeforeEnd;
		private readonly int _framesPerTick;
		private int _ticksRendered;
		private bool _ended;
		private TimeSpan _position;

		public FiniteSong(int ticksBeforeEnd, int framesPerTick)
		{
			_ticksBeforeEnd = ticksBeforeEnd;
			_framesPerTick = framesPerTick;
		}

		public ModuleMetadata Metadata => ModuleMetadata.Empty;

		public ModulePlaybackCapabilities Capabilities => ModulePlaybackCapabilities.Minimal;

		public IReadOnlyList<ModuleDiagnostic> Diagnostics => Array.Empty<ModuleDiagnostic>();

		public SongDuration Duration => SongDuration.Exact(TimeSpan.FromSeconds(_ticksBeforeEnd));

		public PlaybackPosition Position => PlaybackPosition.FromTime(_position);

		public bool LoopingEnabled { get; set; }

		public int RenderedTicks => _ticksRendered;

		public int GetCurrentTickFrameCount(AudioRenderOptions? options = null)
		{
			return _framesPerTick;
		}

		public void Reset()
		{
			_ticksRendered = 0;
			_ended = false;
			_position = TimeSpan.Zero;
		}

		public void Seek(TimeSpan position)
		{
			_position = position < TimeSpan.Zero ? TimeSpan.Zero : position;
			_ended = false;
		}

		public void Seek(TrackerPosition position)
		{
			_ = position;
			Reset();
		}

		public RenderResult Render(Span<float> destination, AudioRenderOptions? options = null)
		{
			return RenderTick(destination, options);
		}

		public RenderResult RenderTick(Span<float> destination, AudioRenderOptions? options = null)
		{
			options ??= AudioRenderOptions.Default;
			var samples = options.GetSampleCount(_framesPerTick);
			if (destination.Length < samples)
			{
				throw new ArgumentException("Destination too small.", nameof(destination));
			}

			destination.Slice(0, samples).Fill(0.25f);
			_ticksRendered++;
			_position += TimeSpan.FromSeconds(_framesPerTick / (double)options.SampleRate);
			_ended = !LoopingEnabled && _ticksRendered >= _ticksBeforeEnd;
			return new RenderResult(_framesPerTick, samples, Position, _ended);
		}

		public void Dispose()
		{
		}
	}

	private sealed class SequentialChunkSong : IModuleSong
	{
		private readonly int _framesPerTick;
		private int _ticksRendered;
		private TimeSpan _position;

		public SequentialChunkSong(int framesPerTick)
		{
			_framesPerTick = framesPerTick;
		}

		public ModuleMetadata Metadata => ModuleMetadata.Empty;

		public ModulePlaybackCapabilities Capabilities => ModulePlaybackCapabilities.Minimal;

		public IReadOnlyList<ModuleDiagnostic> Diagnostics => Array.Empty<ModuleDiagnostic>();

		public SongDuration Duration => SongDuration.Unknown;

		public PlaybackPosition Position => PlaybackPosition.FromTime(_position);

		public bool LoopingEnabled { get; set; }

		public int GetCurrentTickFrameCount(AudioRenderOptions? options = null)
		{
			return _framesPerTick;
		}

		public void Reset()
		{
			_ticksRendered = 0;
			_position = TimeSpan.Zero;
		}

		public void Seek(TimeSpan position)
		{
			_ticksRendered = 0;
			_position = position < TimeSpan.Zero ? TimeSpan.Zero : position;
		}

		public void Seek(TrackerPosition position)
		{
			_ = position;
			Reset();
		}

		public RenderResult Render(Span<float> destination, AudioRenderOptions? options = null)
		{
			return RenderTick(destination, options);
		}

		public RenderResult RenderTick(Span<float> destination, AudioRenderOptions? options = null)
		{
			options ??= AudioRenderOptions.Default;
			var samples = options.GetSampleCount(_framesPerTick);
			var value = (_ticksRendered + 1) * 0.25f;
			destination.Slice(0, samples).Fill(value);
			_ticksRendered++;
			_position += TimeSpan.FromSeconds(_framesPerTick / (double)options.SampleRate);
			return new RenderResult(_framesPerTick, samples, Position);
		}

		public void Dispose()
		{
		}
	}

	private sealed class ChannelWaveformSong : IModuleSong, IModuleChannelWaveformProvider
	{
		private readonly int _framesPerTick;
		private bool _ended;

		public ChannelWaveformSong(int framesPerTick)
		{
			_framesPerTick = framesPerTick;
		}

		public ModuleMetadata Metadata => ModuleMetadata.Empty;

		public ModulePlaybackCapabilities Capabilities => ModulePlaybackCapabilities.Minimal;

		public IReadOnlyList<ModuleDiagnostic> Diagnostics => Array.Empty<ModuleDiagnostic>();

		public SongDuration Duration => SongDuration.Exact(TimeSpan.FromSeconds(1));

		public PlaybackPosition Position => PlaybackPosition.FromTime(TimeSpan.Zero);

		public bool LoopingEnabled { get; set; }

		public bool ChannelWaveformCaptureEnabled { get; set; }

		public ModuleChannelWaveform? LastChannelWaveform { get; private set; }

		public int GetCurrentTickFrameCount(AudioRenderOptions? options = null)
		{
			return _framesPerTick;
		}

		public void Reset()
		{
			_ended = false;
			LastChannelWaveform = null;
		}

		public void Seek(TimeSpan position)
		{
			_ = position;
			Reset();
		}

		public void Seek(TrackerPosition position)
		{
			_ = position;
			Reset();
		}

		public RenderResult Render(Span<float> destination, AudioRenderOptions? options = null)
		{
			return RenderTick(destination, options);
		}

		public RenderResult RenderTick(Span<float> destination, AudioRenderOptions? options = null)
		{
			options ??= AudioRenderOptions.Default;
			var samples = options.GetSampleCount(_framesPerTick);
			destination.Slice(0, samples).Fill(0.125f);
			LastChannelWaveform = ChannelWaveformCaptureEnabled
				? new ModuleChannelWaveform(
					new[]
					{
						new ModuleChannelWaveformChannel(0, new[] { -1.0f, -0.5f, 0.5f, 1.0f }, true),
						new ModuleChannelWaveformChannel(1, new[] { -0.5f, -0.25f, 0.25f, 0.5f }, true)
					},
					_framesPerTick,
					options.SampleRate)
				: null;
			_ended = true;
			return new RenderResult(_framesPerTick, samples, Position, _ended);
		}

		public void Dispose()
		{
		}
	}

	private sealed class OutputFamilySong : IModuleSong, IModuleOutputFamilyProvider
	{
		private readonly ModuleOutputFamily _outputFamily;
		private TimeSpan _position;

		public OutputFamilySong(ModuleOutputFamily outputFamily)
		{
			_outputFamily = outputFamily;
		}

		public ModuleMetadata Metadata => ModuleMetadata.Empty;

		public ModulePlaybackCapabilities Capabilities => ModulePlaybackCapabilities.Minimal;

		public IReadOnlyList<ModuleDiagnostic> Diagnostics => Array.Empty<ModuleDiagnostic>();

		public SongDuration Duration => SongDuration.Unknown;

		public PlaybackPosition Position => PlaybackPosition.FromTime(_position);

		public bool LoopingEnabled { get; set; }

		public ModuleOutputFamily OutputFamily => _outputFamily;

		public int GetCurrentTickFrameCount(AudioRenderOptions? options = null)
		{
			return 4;
		}

		public void Reset()
		{
			_position = TimeSpan.Zero;
		}

		public void Seek(TimeSpan position)
		{
			_position = position;
		}

		public void Seek(TrackerPosition position)
		{
			_ = position;
			Reset();
		}

		public RenderResult Render(Span<float> destination, AudioRenderOptions? options = null)
		{
			return RenderTick(destination, options);
		}

		public RenderResult RenderTick(Span<float> destination, AudioRenderOptions? options = null)
		{
			options ??= AudioRenderOptions.Default;
			var samples = options.GetSampleCount(4);
			destination.Slice(0, samples).Fill(0.25f);
			_position += TimeSpan.FromSeconds(4 / (double)options.SampleRate);
			return new RenderResult(4, samples, Position, false);
		}

		public void Dispose()
		{
		}
	}

	private sealed class C64VideoSong : IModuleSong, IC64VideoFrameProvider
	{
		private readonly int _framesPerTick;
		private int _renderedTicks;
		private int _videoFrameRequests;
		private TimeSpan _position;

		public C64VideoSong(int framesPerTick)
		{
			_framesPerTick = framesPerTick;
		}

		public ModuleMetadata Metadata => ModuleMetadata.Empty;

		public ModulePlaybackCapabilities Capabilities => ModulePlaybackCapabilities.Minimal;

		public IReadOnlyList<ModuleDiagnostic> Diagnostics => Array.Empty<ModuleDiagnostic>();

		public SongDuration Duration => SongDuration.Unknown;

		public PlaybackPosition Position => PlaybackPosition.FromTime(_position);

		public bool LoopingEnabled { get; set; }

		public bool HasVideoFrameSource => true;

		public int RenderedTicks => Volatile.Read(ref _renderedTicks);

		public int VideoFrameRequests => Volatile.Read(ref _videoFrameRequests);

		public int GetCurrentTickFrameCount(AudioRenderOptions? options = null)
		{
			return _framesPerTick;
		}

		public void Reset()
		{
			Interlocked.Exchange(ref _renderedTicks, 0);
			Interlocked.Exchange(ref _videoFrameRequests, 0);
			_position = TimeSpan.Zero;
		}

		public void Seek(TimeSpan position)
		{
			_position = position < TimeSpan.Zero ? TimeSpan.Zero : position;
		}

		public void Seek(TrackerPosition position)
		{
			_ = position;
			Reset();
		}

		public RenderResult Render(Span<float> destination, AudioRenderOptions? options = null)
		{
			return RenderTick(destination, options);
		}

		public RenderResult RenderTick(Span<float> destination, AudioRenderOptions? options = null)
		{
			options ??= AudioRenderOptions.Default;
			var samples = options.GetSampleCount(_framesPerTick);
			destination.Slice(0, samples).Fill(0.25f);
			Interlocked.Increment(ref _renderedTicks);
			_position += TimeSpan.FromSeconds(_framesPerTick / (double)options.SampleRate);
			return new RenderResult(_framesPerTick, samples, Position);
		}

		public bool TryGetLatestVideoFrame(out C64VideoFrame frame)
		{
			var frameNumber = Interlocked.Increment(ref _videoFrameRequests);
			frame = new C64VideoFrame(
				1,
				1,
				new[] { new Argb32(255, 255, 255, 255) },
				frameNumber,
				_position);
			return true;
		}

		public void Dispose()
		{
		}
	}

	private sealed class SelectableSubSongSong : IModuleSong, IModuleSubSongSelector
	{
		private readonly int _framesPerTick;
		private TimeSpan _position;

		public SelectableSubSongSong(int framesPerTick)
		{
			_framesPerTick = framesPerTick;
		}

		public ModuleMetadata Metadata => ModuleMetadata.Empty;

		public ModulePlaybackCapabilities Capabilities => ModulePlaybackCapabilities.Minimal;

		public IReadOnlyList<ModuleDiagnostic> Diagnostics => Array.Empty<ModuleDiagnostic>();

		public SongDuration Duration => SongDuration.Unknown;

		public PlaybackPosition Position => PlaybackPosition.FromTime(_position);

		public bool LoopingEnabled { get; set; }

		public int SubSongCount => 2;

		public int DefaultSubSongIndex => 0;

		public int CurrentSubSongIndex { get; private set; }

		public IReadOnlyList<ModuleSubSongMetadata> SubSongs { get; } =
			new[] { new ModuleSubSongMetadata(0), new ModuleSubSongMetadata(1) };

		public int GetCurrentTickFrameCount(AudioRenderOptions? options = null)
		{
			return _framesPerTick;
		}

		public void Reset()
		{
			_position = TimeSpan.Zero;
		}

		public void Seek(TimeSpan position)
		{
			_position = position < TimeSpan.Zero ? TimeSpan.Zero : position;
		}

		public void Seek(TrackerPosition position)
		{
			_ = position;
			Reset();
		}

		public void SelectSubSong(int index)
		{
			if (index < 0 || index >= SubSongCount)
			{
				throw new ArgumentOutOfRangeException(nameof(index));
			}

			CurrentSubSongIndex = index;
			Reset();
		}

		public RenderResult Render(Span<float> destination, AudioRenderOptions? options = null)
		{
			return RenderTick(destination, options);
		}

		public RenderResult RenderTick(Span<float> destination, AudioRenderOptions? options = null)
		{
			options ??= AudioRenderOptions.Default;
			var samples = options.GetSampleCount(_framesPerTick);
			destination.Slice(0, samples).Fill(CurrentSubSongIndex == 0 ? 0.125f : 0.5f);
			_position += TimeSpan.FromSeconds(_framesPerTick / (double)options.SampleRate);
			return new RenderResult(_framesPerTick, samples, Position);
		}

		public void Dispose()
		{
		}
	}

	private sealed class BlockingSubSong : IModuleSong, IModuleSubSongSelector
	{
		private readonly ManualResetEventSlim _releaseRender;
		private int _activeRenderCount;

		public BlockingSubSong(ManualResetEventSlim releaseRender)
		{
			_releaseRender = releaseRender;
		}

		public ManualResetEventSlim RenderStarted { get; } = new();

		public ModuleMetadata Metadata => ModuleMetadata.Empty;

		public ModulePlaybackCapabilities Capabilities => ModulePlaybackCapabilities.Minimal;

		public IReadOnlyList<ModuleDiagnostic> Diagnostics => Array.Empty<ModuleDiagnostic>();

		public SongDuration Duration => SongDuration.Unknown;

		public PlaybackPosition Position => PlaybackPosition.FromTime(TimeSpan.Zero);

		public bool LoopingEnabled { get; set; }

		public int SubSongCount => 2;

		public int DefaultSubSongIndex => 0;

		public int CurrentSubSongIndex { get; private set; }

		public int SelectCalls { get; private set; }

		public bool SelectedDuringRender { get; private set; }

		public IReadOnlyList<ModuleSubSongMetadata> SubSongs { get; } =
			new[] { new ModuleSubSongMetadata(0), new ModuleSubSongMetadata(1) };

		public int GetCurrentTickFrameCount(AudioRenderOptions? options = null)
		{
			return 4;
		}

		public void Reset()
		{
		}

		public void Seek(TimeSpan position)
		{
			_ = position;
		}

		public void Seek(TrackerPosition position)
		{
			_ = position;
		}

		public void SelectSubSong(int index)
		{
			if (Volatile.Read(ref _activeRenderCount) > 0)
			{
				SelectedDuringRender = true;
			}

			SelectCalls++;
			CurrentSubSongIndex = index;
		}

		public RenderResult Render(Span<float> destination, AudioRenderOptions? options = null)
		{
			return RenderTick(destination, options);
		}

		public RenderResult RenderTick(Span<float> destination, AudioRenderOptions? options = null)
		{
			options ??= AudioRenderOptions.Default;
			Interlocked.Increment(ref _activeRenderCount);
			try
			{
				RenderStarted.Set();
				_releaseRender.Wait();
				var samples = options.GetSampleCount(4);
				destination.Slice(0, samples).Fill(0.25f);
				return new RenderResult(4, samples, Position);
			}
			finally
			{
				Interlocked.Decrement(ref _activeRenderCount);
			}
		}

		public void Dispose()
		{
			RenderStarted.Dispose();
		}
	}

	private sealed class BlockingSong : IModuleSong
	{
		private readonly ManualResetEventSlim _releaseRender;

		public BlockingSong(ManualResetEventSlim releaseRender)
		{
			_releaseRender = releaseRender;
		}

		public ManualResetEventSlim RenderStarted { get; } = new();

		public ModuleMetadata Metadata => ModuleMetadata.Empty;

		public ModulePlaybackCapabilities Capabilities => ModulePlaybackCapabilities.Minimal;

		public IReadOnlyList<ModuleDiagnostic> Diagnostics => Array.Empty<ModuleDiagnostic>();

		public SongDuration Duration => SongDuration.Unknown;

		public PlaybackPosition Position => PlaybackPosition.FromTime(TimeSpan.Zero);

		public bool LoopingEnabled { get; set; }

		public int GetCurrentTickFrameCount(AudioRenderOptions? options = null)
		{
			return 4;
		}

		public void Reset()
		{
		}

		public void Seek(TimeSpan position)
		{
			_ = position;
		}

		public void Seek(TrackerPosition position)
		{
			_ = position;
		}

		public RenderResult Render(Span<float> destination, AudioRenderOptions? options = null)
		{
			return RenderTick(destination, options);
		}

		public RenderResult RenderTick(Span<float> destination, AudioRenderOptions? options = null)
		{
			options ??= AudioRenderOptions.Default;
			RenderStarted.Set();
			_releaseRender.Wait();
			var samples = options.GetSampleCount(4);
			destination.Slice(0, samples).Fill(0.5f);
			return new RenderResult(4, samples, Position);
		}

		public void Dispose()
		{
			RenderStarted.Dispose();
		}
	}
}
