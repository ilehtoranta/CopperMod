using CopperMod.Abstractions;
using CopperMod.Rendering;
using NAudio.Wave;

namespace CopperMod;

internal sealed class ModuleSampleProvider : ISampleProvider, IDisposable
{
	private const int RenderAheadTargetMilliseconds = 5000;
	private const int RenderAheadCapacityMilliseconds = 15000;
	private const int ReadWaitMilliseconds = 2;
	private readonly object _renderSync = new();
	private readonly object _bufferSync = new();
	private readonly object _waveformSync = new();
	private readonly Queue<AudioChunk> _bufferedChunks = new();
	private readonly AudioRenderOptions _renderOptions;
	private readonly AmigaOutputStage _outputStage;
	private readonly C64OutputStage _c64OutputStage;
	private readonly IAmigaHardwareStateProvider? _amigaHardwareStateProvider;
	private readonly IModuleOutputFamilyProvider? _outputFamilyProvider;
	private readonly IModuleChannelWaveformProvider? _channelWaveformProvider;
	private readonly Thread _renderThread;
	private readonly int _renderAheadTargetSamples;
	private readonly int _renderAheadCapacitySamples;
	private IModuleSong _song;
	private float[] _latestWaveformSamples = Array.Empty<float>();
	private int _latestWaveformSampleCount;
	private int _latestWaveformChannelCount;
	private int _latestWaveformSampleRate;
	private long _latestWaveformVersion;
	private long _consumedWaveformVersion;
	private int _queuedSamples;
	private int _leadInSamplesRemaining;
	private int _generation;
	private long _underrunCount;
	private long _playedSamples;
	private TimeSpan _positionBaseTime;
	private bool _renderEndOfSong;
	private bool _endOfSong;
	private bool _endEventRaised;
	private bool _waveformEnabled;
	private bool _disposed;
	private float _volume = 1.0f;
	private WaveformDisplayMode _waveformDisplayMode;

	public ModuleSampleProvider(
		IModuleSong song,
		int sampleRate,
		int channelCount,
		AmigaOutputProfile outputProfile,
		TimeSpan initialLeadIn = default,
		C64OutputProfile c64OutputProfile = C64OutputProfile.C64)
	{
		_song = song ?? throw new ArgumentNullException(nameof(song));
		_renderOptions = new AudioRenderOptions(sampleRate, channelCount);
		_outputStage = new AmigaOutputStage(outputProfile);
		_c64OutputStage = new C64OutputStage(c64OutputProfile);
		_amigaHardwareStateProvider = song as IAmigaHardwareStateProvider;
		_outputFamilyProvider = song as IModuleOutputFamilyProvider;
		_channelWaveformProvider = song as IModuleChannelWaveformProvider;
		UpdateChannelWaveformCaptureEnabled();

		WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channelCount);
		_leadInSamplesRemaining = CalculateLeadInSamples(initialLeadIn, WaveFormat);
		_renderAheadTargetSamples = CalculateBufferSamples(RenderAheadTargetMilliseconds, WaveFormat);
		_renderAheadCapacitySamples = CalculateBufferSamples(RenderAheadCapacityMilliseconds, WaveFormat);
		_renderThread = new Thread(RenderLoop)
		{
			IsBackground = true,
			Name = "CopperMod render-ahead",
			Priority = ThreadPriority.AboveNormal
		};
		_renderThread.Start();
	}

	public event EventHandler? EndOfSongReached;

	public WaveFormat WaveFormat { get; }

	public float Volume
	{
		get
		{
			lock (_bufferSync)
			{
				return _volume;
			}
		}

		set
		{
			lock (_bufferSync)
			{
				_volume = Math.Clamp(value, 0.0f, 1.0f);
			}
		}
	}

	public bool WaveformEnabled
	{
		get
		{
			lock (_bufferSync)
			{
				return _waveformEnabled;
			}
		}

		set
		{
			float[]? bufferedWaveform = null;
			lock (_bufferSync)
			{
				_waveformEnabled = value;
				UpdateChannelWaveformCaptureEnabled();
				if (value)
				{
					bufferedWaveform = GetLatestBufferedSamples();
				}
			}

			if (bufferedWaveform != null)
			{
				StoreLatestWaveform(bufferedWaveform);
			}
		}
	}

	public WaveformDisplayMode WaveformDisplayMode
	{
		get
		{
			lock (_bufferSync)
			{
				return _waveformDisplayMode;
			}
		}

		set
		{
			lock (_bufferSync)
			{
				_waveformDisplayMode = value;
				UpdateChannelWaveformCaptureEnabled();
			}
		}
	}

	public AmigaOutputProfile OutputProfile
	{
		get
		{
			lock (_renderSync)
			{
				return _outputStage.Profile;
			}
		}

		set
		{
			lock (_renderSync)
			{
				_outputStage.Profile = value;
				_outputStage.Reset();
			}
		}
	}

	public C64OutputProfile C64OutputProfile
	{
		get
		{
			lock (_renderSync)
			{
				return _c64OutputStage.Profile;
			}
		}

		set
		{
			lock (_renderSync)
			{
				_c64OutputStage.Profile = value;
				_c64OutputStage.Reset();
			}
		}
	}

	public PlaybackPosition Position
	{
		get
		{
			lock (_bufferSync)
			{
				var playedFrames = _playedSamples / (double)WaveFormat.Channels;
				return PlaybackPosition.FromTime(_positionBaseTime + TimeSpan.FromSeconds(playedFrames / WaveFormat.SampleRate));
			}
		}
	}

	public bool EndOfSong
	{
		get
		{
			lock (_bufferSync)
			{
				return _endOfSong;
			}
		}
	}

	public PlaybackBufferStatus BufferStatus
	{
		get
		{
			lock (_bufferSync)
			{
				return new PlaybackBufferStatus(
					SamplesToDuration(_queuedSamples),
					SamplesToDuration(_renderAheadTargetSamples),
					SamplesToDuration(_renderAheadCapacitySamples),
					_underrunCount,
					_renderEndOfSong,
					_endOfSong);
			}
		}
	}

	public void Reset()
	{
		lock (_renderSync)
		{
			_song.Reset();
			_outputStage.Reset();
			_c64OutputStage.Reset();
		}

		ResetBufferedState(TimeSpan.Zero, leadInSamples: 0);
		ClearLatestWaveform();
	}

	public void Seek(TimeSpan position)
	{
		if (position < TimeSpan.Zero)
		{
			position = TimeSpan.Zero;
		}

		lock (_renderSync)
		{
			_song.Seek(position);
			_outputStage.Reset();
			_c64OutputStage.Reset();
		}

		ResetBufferedState(position, leadInSamples: 0);
		ClearLatestWaveform();
	}

	public void SelectSubSong(int index)
	{
		lock (_renderSync)
		{
			if (_song is not IModuleSubSongSelector selector)
			{
				throw new NotSupportedException("The loaded module does not expose subtunes.");
			}

			selector.SelectSubSong(index);
			_outputStage.Reset();
			_c64OutputStage.Reset();
		}

		ResetBufferedState(TimeSpan.Zero, leadInSamples: 0);
		ClearLatestWaveform();
	}

	public int Read(float[] buffer, int offset, int count)
	{
		if (buffer is null)
		{
			throw new ArgumentNullException(nameof(buffer));
		}

		lock (_bufferSync)
		{
			if (_disposed)
			{
				Array.Clear(buffer, offset, count);
				return 0;
			}
		}

		var samplesWritten = 0;
		var reachedEnd = false;
		var raiseEndEvent = false;
		while (samplesWritten < count)
		{
			var copied = CopyAvailableSamples(
				buffer,
				offset + samplesWritten,
				count - samplesWritten,
				out reachedEnd,
				out var copiedRaiseEndEvent);
			raiseEndEvent |= copiedRaiseEndEvent;
			samplesWritten += copied;
			if (reachedEnd)
			{
				break;
			}

			if (copied == 0 && !WaitForBufferedSamplesOrEnd())
			{
				RecordUnderrun();
				break;
			}
		}

		Array.Clear(buffer, offset + samplesWritten, count - samplesWritten);
		if (raiseEndEvent)
		{
			EndOfSongReached?.Invoke(this, EventArgs.Empty);
		}

		if (reachedEnd)
		{
			return samplesWritten;
		}

		return count;
	}

	public bool TryReadWaveformSnapshot(out WaveformSnapshot snapshot)
	{
		var hasNoStoredWaveform = false;
		if (TryCopyLatestWaveform(out snapshot, out hasNoStoredWaveform))
		{
			return true;
		}

		float[]? bufferedWaveform = null;
		if (hasNoStoredWaveform)
		{
			lock (_bufferSync)
			{
				if (_waveformEnabled)
				{
					bufferedWaveform = GetLatestBufferedSamples();
				}
			}
		}

		if (bufferedWaveform != null)
		{
			StoreLatestWaveform(bufferedWaveform);
			return TryCopyLatestWaveform(out snapshot, out _);
		}

		snapshot = new WaveformSnapshot(Array.Empty<float>(), Array.Empty<float>(), 0, WaveFormat.SampleRate);
		return false;
	}

	private bool TryCopyLatestWaveform(out WaveformSnapshot snapshot, out bool hasNoStoredWaveform)
	{
		float[] samples;
		int sampleCount;
		int channelCount;
		int sampleRate;
		lock (_waveformSync)
		{
			hasNoStoredWaveform = _latestWaveformSampleCount == 0;
			if (_latestWaveformVersion == _consumedWaveformVersion || _latestWaveformSampleCount == 0)
			{
				snapshot = new WaveformSnapshot(Array.Empty<float>(), Array.Empty<float>(), 0, WaveFormat.SampleRate);
				return false;
			}

			sampleCount = _latestWaveformSampleCount;
			channelCount = _latestWaveformChannelCount;
			sampleRate = _latestWaveformSampleRate;
			samples = new float[sampleCount];
			Array.Copy(_latestWaveformSamples, samples, sampleCount);
			_consumedWaveformVersion = _latestWaveformVersion;
		}

		snapshot = WaveformSampler.CreateSnapshot(samples, channelCount, sampleRate);
		return true;
	}

	public void Dispose()
	{
		lock (_bufferSync)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			Monitor.PulseAll(_bufferSync);
		}

		_renderThread.Join();
		UpdateChannelWaveformCaptureEnabled();
	}

	private int CopyAvailableSamples(float[] buffer, int offset, int count, out bool reachedEnd, out bool raiseEndEvent)
	{
		reachedEnd = false;
		raiseEndEvent = false;
		lock (_bufferSync)
		{
			if (_disposed)
			{
				reachedEnd = true;
				return 0;
			}

			var samplesWritten = 0;
			if (_leadInSamplesRemaining > 0)
			{
				var leadInSamples = Math.Min(count, _leadInSamplesRemaining);
				Array.Clear(buffer, offset, leadInSamples);
				_leadInSamplesRemaining -= leadInSamples;
				samplesWritten += leadInSamples;
			}

			while (samplesWritten < count && _bufferedChunks.Count > 0)
			{
				var chunk = _bufferedChunks.Peek();
				var samplesToCopy = Math.Min(count - samplesWritten, chunk.Remaining);
				var volume = _volume;
				for (var i = 0; i < samplesToCopy; i++)
				{
					buffer[offset + samplesWritten + i] = chunk.Samples[chunk.Offset + i] * volume;
				}

				chunk.Offset += samplesToCopy;
				_queuedSamples -= samplesToCopy;
				_playedSamples += samplesToCopy;
				samplesWritten += samplesToCopy;
				if (chunk.Remaining == 0)
				{
					_bufferedChunks.Dequeue();
				}
			}

			if (_renderEndOfSong && _leadInSamplesRemaining == 0 && _queuedSamples == 0)
			{
				_endOfSong = true;
				reachedEnd = true;
				raiseEndEvent = !_endEventRaised;
				_endEventRaised = true;
			}

			Monitor.PulseAll(_bufferSync);
			return samplesWritten;
		}
	}

	private bool WaitForBufferedSamplesOrEnd()
	{
		lock (_bufferSync)
		{
			if (_disposed || _queuedSamples > 0 || _renderEndOfSong || _leadInSamplesRemaining > 0)
			{
				return true;
			}

			Monitor.PulseAll(_bufferSync);
			Monitor.Wait(_bufferSync, ReadWaitMilliseconds);
			return _queuedSamples > 0 || _renderEndOfSong || _leadInSamplesRemaining > 0;
		}
	}

	private void RenderLoop()
	{
		while (true)
		{
			var generation = WaitForRenderWork();
			if (generation < 0)
			{
				return;
			}

			var chunk = RenderNextChunk(out var reachedEnd);
			if (chunk.Samples.Length == 0 && !reachedEnd)
			{
				Thread.Sleep(1);
				continue;
			}

			EnqueueRenderedChunk(generation, chunk, reachedEnd);
		}
	}

	private int WaitForRenderWork()
	{
		lock (_bufferSync)
		{
			while (!_disposed && (_renderEndOfSong || _queuedSamples >= _renderAheadTargetSamples))
			{
				Monitor.Wait(_bufferSync);
			}

			return _disposed ? -1 : _generation;
		}
	}

	private AudioChunk RenderNextChunk(out bool reachedEnd)
	{
		lock (_renderSync)
		{
			var frames = _song.GetCurrentTickFrameCount(_renderOptions);
			var samples = new float[_renderOptions.GetSampleCount(frames)];
			var result = _song.RenderTick(samples, _renderOptions);
			var samplesWritten = Math.Min(result.SamplesWritten, samples.Length);
			if (samplesWritten != samples.Length)
			{
				Array.Resize(ref samples, samplesWritten);
			}

			if ((_outputFamilyProvider?.OutputFamily ?? ModuleOutputFamily.Amiga) == ModuleOutputFamily.Commodore64)
			{
				_c64OutputStage.Process(samples, _renderOptions.ChannelCount, _renderOptions.SampleRate);
			}
			else
			{
				_outputStage.Process(
					samples,
					_renderOptions.ChannelCount,
					_renderOptions.SampleRate,
					_amigaHardwareStateProvider?.AmigaHardwareState.AudioFilterEnabled == true);
			}

			if (IsWaveformEnabled())
			{
				StoreLatestWaveform(samples);
			}

			reachedEnd = result.EndOfSong;
			return new AudioChunk(samples);
		}
	}

	private void EnqueueRenderedChunk(int generation, AudioChunk chunk, bool reachedEnd)
	{
		lock (_bufferSync)
		{
			while (!_disposed &&
				generation == _generation &&
				_queuedSamples > 0 &&
				_queuedSamples + chunk.Samples.Length > _renderAheadCapacitySamples)
			{
				Monitor.Wait(_bufferSync);
			}

			if (_disposed || generation != _generation)
			{
				return;
			}

			if (chunk.Samples.Length > 0)
			{
				_bufferedChunks.Enqueue(chunk);
				_queuedSamples += chunk.Samples.Length;
			}

			if (reachedEnd)
			{
				_renderEndOfSong = true;
			}

			Monitor.PulseAll(_bufferSync);
		}
	}

	private bool IsWaveformEnabled()
	{
		lock (_bufferSync)
		{
			return _waveformEnabled;
		}
	}

	private void ResetBufferedState(TimeSpan positionBase, int leadInSamples)
	{
		lock (_bufferSync)
		{
			_bufferedChunks.Clear();
			_queuedSamples = 0;
			_leadInSamplesRemaining = leadInSamples;
			_positionBaseTime = positionBase;
			_playedSamples = 0;
			_underrunCount = 0;
			_renderEndOfSong = false;
			_endOfSong = false;
			_endEventRaised = false;
			_generation++;
			Monitor.PulseAll(_bufferSync);
		}
	}

	private void StoreLatestWaveform(ReadOnlySpan<float> samples)
	{
		lock (_waveformSync)
		{
			if (_latestWaveformSamples.Length < samples.Length)
			{
				_latestWaveformSamples = new float[samples.Length];
			}

			samples.CopyTo(_latestWaveformSamples);
			_latestWaveformSampleCount = samples.Length;
			_latestWaveformChannelCount = _renderOptions.ChannelCount;
			_latestWaveformSampleRate = _renderOptions.SampleRate;
			_latestWaveformVersion++;
		}
	}

	private float[]? GetLatestBufferedSamples()
	{
		AudioChunk? latest = null;
		foreach (var chunk in _bufferedChunks)
		{
			latest = chunk;
		}

		return latest?.Samples;
	}

	private void ClearLatestWaveform()
	{
		lock (_waveformSync)
		{
			_latestWaveformSampleCount = 0;
			_latestWaveformVersion++;
		}
	}

	private void UpdateChannelWaveformCaptureEnabled()
	{
		if (_channelWaveformProvider != null)
		{
			_channelWaveformProvider.ChannelWaveformCaptureEnabled = false;
		}
	}

	private static int CalculateLeadInSamples(TimeSpan duration, WaveFormat waveFormat)
	{
		if (duration <= TimeSpan.Zero)
		{
			return 0;
		}

		var frames = (int)Math.Ceiling(duration.TotalSeconds * waveFormat.SampleRate);
		return checked(frames * waveFormat.Channels);
	}

	private static int CalculateBufferSamples(int milliseconds, WaveFormat waveFormat)
	{
		var frames = (int)Math.Ceiling(milliseconds / 1000.0 * waveFormat.SampleRate);
		return checked(frames * waveFormat.Channels);
	}

	private void RecordUnderrun()
	{
		lock (_bufferSync)
		{
			if (!_renderEndOfSong && !_endOfSong)
			{
				_underrunCount++;
			}
		}
	}

	private TimeSpan SamplesToDuration(int samples)
	{
		var frames = samples / (double)WaveFormat.Channels;
		return TimeSpan.FromSeconds(frames / WaveFormat.SampleRate);
	}

	private sealed class AudioChunk
	{
		public AudioChunk(float[] samples)
		{
			Samples = samples;
		}

		public float[] Samples { get; }

		public int Offset { get; set; }

		public int Remaining => Samples.Length - Offset;
	}
}
