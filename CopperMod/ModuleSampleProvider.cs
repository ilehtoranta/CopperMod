using AmigaTracker.Abstractions;
using NAudio.Wave;

namespace CopperMod;

internal sealed class ModuleSampleProvider : ISampleProvider
{
	private readonly object _syncRoot = new();
	private readonly AudioRenderOptions _renderOptions;
	private readonly AmigaOutputStage _outputStage;
	private readonly C64OutputStage _c64OutputStage;
	private readonly IAmigaHardwareStateProvider? _amigaHardwareStateProvider;
	private readonly IModuleOutputFamilyProvider? _outputFamilyProvider;
	private readonly IModuleChannelWaveformProvider? _channelWaveformProvider;
	private IModuleSong _song;
	private float[] _tickSamples = Array.Empty<float>();
	private int _tickSampleOffset;
	private int _leadInSamplesRemaining;
	private bool _endOfSong;
	private bool _waveformEnabled;
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
	}

	public event EventHandler? EndOfSongReached;

	public event EventHandler<WaveformSnapshotEventArgs>? WaveformAvailable;

	public WaveFormat WaveFormat { get; }

	public float Volume { get; set; } = 1.0f;

	public bool WaveformEnabled
	{
		get
		{
			lock (_syncRoot)
			{
				return _waveformEnabled;
			}
		}

		set
		{
			lock (_syncRoot)
			{
				_waveformEnabled = value;
				UpdateChannelWaveformCaptureEnabled();
			}
		}
	}

	public WaveformDisplayMode WaveformDisplayMode
	{
		get
		{
			lock (_syncRoot)
			{
				return _waveformDisplayMode;
			}
		}

		set
		{
			lock (_syncRoot)
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
			lock (_syncRoot)
			{
				return _outputStage.Profile;
			}
		}

		set
		{
			lock (_syncRoot)
			{
				_outputStage.Profile = value;
				_outputStage.Reset();
				ClearBufferedTick();
			}
		}
	}

	public C64OutputProfile C64OutputProfile
	{
		get
		{
			lock (_syncRoot)
			{
				return _c64OutputStage.Profile;
			}
		}

		set
		{
			lock (_syncRoot)
			{
				_c64OutputStage.Profile = value;
				_c64OutputStage.Reset();
				ClearBufferedTick();
			}
		}
	}

	public PlaybackPosition Position
	{
		get
		{
			lock (_syncRoot)
			{
				return _song.Position;
			}
		}
	}

	public bool EndOfSong
	{
		get
		{
			lock (_syncRoot)
			{
				return _endOfSong;
			}
		}
	}

	public void Reset()
	{
		lock (_syncRoot)
		{
			_song.Reset();
			_outputStage.Reset();
			_c64OutputStage.Reset();
			_endOfSong = false;
			_leadInSamplesRemaining = 0;
			ClearBufferedTick();
		}
	}

	public void Seek(TimeSpan position)
	{
		lock (_syncRoot)
		{
			_song.Seek(position);
			_outputStage.Reset();
			_c64OutputStage.Reset();
			_endOfSong = false;
			_leadInSamplesRemaining = 0;
			ClearBufferedTick();
		}
	}

	public int Read(float[] buffer, int offset, int count)
	{
		if (buffer is null)
		{
			throw new ArgumentNullException(nameof(buffer));
		}

		var samplesWritten = 0;
		var reachedEnd = false;
		List<WaveformSnapshot>? channelWaveforms = null;
		var waveformEnabled = false;
		var waveformDisplayMode = WaveformDisplayMode.MixedOutput;
		lock (_syncRoot)
		{
			waveformEnabled = _waveformEnabled;
			waveformDisplayMode = _waveformDisplayMode;
			while (samplesWritten < count)
			{
				if (_leadInSamplesRemaining > 0)
				{
					var leadInSamples = Math.Min(count - samplesWritten, _leadInSamplesRemaining);
					Array.Clear(buffer, offset + samplesWritten, leadInSamples);
					_leadInSamplesRemaining -= leadInSamples;
					samplesWritten += leadInSamples;
					continue;
				}

				if (_tickSampleOffset >= _tickSamples.Length)
				{
					if (_endOfSong)
					{
						break;
					}

					reachedEnd |= RenderNextTick();
					if (waveformEnabled &&
						waveformDisplayMode == WaveformDisplayMode.TrackerChannels &&
						_channelWaveformProvider?.LastChannelWaveform != null)
					{
						channelWaveforms ??= new List<WaveformSnapshot>();
						channelWaveforms.Add(WaveformSampler.CreateSnapshot(_channelWaveformProvider.LastChannelWaveform));
					}
				}

				if (_tickSamples.Length == 0)
				{
					break;
				}

				var samplesToCopy = Math.Min(count - samplesWritten, _tickSamples.Length - _tickSampleOffset);
				var volume = Math.Clamp(Volume, 0.0f, 1.0f);
				for (var i = 0; i < samplesToCopy; i++)
				{
					buffer[offset + samplesWritten + i] = _tickSamples[_tickSampleOffset + i] * volume;
				}

				_tickSampleOffset += samplesToCopy;
				samplesWritten += samplesToCopy;
			}
		}

		var fallbackWaveform = waveformEnabled &&
			waveformDisplayMode == WaveformDisplayMode.MixedOutput &&
			samplesWritten > 0
			? WaveformSampler.CreateSnapshot(buffer.AsSpan(offset, samplesWritten), WaveFormat.Channels, WaveFormat.SampleRate)
			: null;
		Array.Clear(buffer, offset + samplesWritten, count - samplesWritten);
		if (channelWaveforms != null)
		{
			foreach (var waveform in channelWaveforms)
			{
				WaveformAvailable?.Invoke(this, new WaveformSnapshotEventArgs(waveform));
			}
		}
		else if (fallbackWaveform != null)
		{
			WaveformAvailable?.Invoke(this, new WaveformSnapshotEventArgs(fallbackWaveform));
		}

		if (reachedEnd)
		{
			EndOfSongReached?.Invoke(this, EventArgs.Empty);
		}

		return samplesWritten;
	}

	private bool RenderNextTick()
	{
		var frames = _song.GetCurrentTickFrameCount(_renderOptions);
		_tickSamples = new float[_renderOptions.GetSampleCount(frames)];
		var result = _song.RenderTick(_tickSamples, _renderOptions);
		if ((_outputFamilyProvider?.OutputFamily ?? ModuleOutputFamily.Amiga) == ModuleOutputFamily.Commodore64)
		{
			_c64OutputStage.Process(_tickSamples, _renderOptions.ChannelCount, _renderOptions.SampleRate);
		}
		else
		{
			_outputStage.Process(
				_tickSamples,
				_renderOptions.ChannelCount,
				_renderOptions.SampleRate,
				_amigaHardwareStateProvider?.AmigaHardwareState.AudioFilterEnabled == true);
		}
		_tickSampleOffset = 0;
		if (!result.EndOfSong)
		{
			return false;
		}

		_endOfSong = true;
		return true;
	}

	private void ClearBufferedTick()
	{
		_tickSamples = Array.Empty<float>();
		_tickSampleOffset = 0;
	}

	private void UpdateChannelWaveformCaptureEnabled()
	{
		if (_channelWaveformProvider != null)
		{
			_channelWaveformProvider.ChannelWaveformCaptureEnabled =
				_waveformEnabled &&
				_waveformDisplayMode == WaveformDisplayMode.TrackerChannels;
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
}
