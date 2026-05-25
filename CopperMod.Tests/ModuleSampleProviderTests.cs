using AmigaTracker.Abstractions;

namespace CopperMod.Tests;

public sealed class ModuleSampleProviderTests
{
	[Fact]
	public void ReadStopsAtEndOfSongInsteadOfPaddingSilenceForever()
	{
		var song = new FiniteSong(ticksBeforeEnd: 2, framesPerTick: 2);
		var provider = new ModuleSampleProvider(song, sampleRate: 44100, channelCount: 1, AmigaOutputProfile.None);
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
		var provider = new ModuleSampleProvider(
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
		var provider = new ModuleSampleProvider(song, sampleRate: 44100, channelCount: 1, AmigaOutputProfile.None);
		var buffer = new float[2];

		Assert.Equal(1, provider.Read(buffer, 0, buffer.Length));
		Assert.True(provider.EndOfSong);

		provider.Reset();

		Assert.False(provider.EndOfSong);
		Assert.Equal(1, provider.Read(buffer, 0, buffer.Length));
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
}
