using AmigaTracker.Abstractions;
using NAudio.Wave;

namespace CopperMod;

internal sealed class ModuleSampleProvider : ISampleProvider
{
	private readonly object _syncRoot = new();
	private readonly AudioRenderOptions _renderOptions;
	private readonly AmigaOutputStage _outputStage;
	private readonly IAmigaHardwareStateProvider? _amigaHardwareStateProvider;
	private IModuleSong _song;
	private float[] _tickSamples = Array.Empty<float>();
	private int _tickSampleOffset;
	private int _leadInSamplesRemaining;
	private bool _endOfSong;

	public ModuleSampleProvider(IModuleSong song, int sampleRate, int channelCount, AmigaOutputProfile outputProfile, TimeSpan initialLeadIn = default)
	{
		_song = song ?? throw new ArgumentNullException(nameof(song));
		_renderOptions = new AudioRenderOptions(sampleRate, channelCount);
		_outputStage = new AmigaOutputStage(outputProfile);
		_amigaHardwareStateProvider = song as IAmigaHardwareStateProvider;
		WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channelCount);
		_leadInSamplesRemaining = CalculateLeadInSamples(initialLeadIn, WaveFormat);
	}

	public event EventHandler? EndOfSongReached;

	public WaveFormat WaveFormat { get; }

	public float Volume { get; set; } = 1.0f;

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
		lock (_syncRoot)
		{
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

		Array.Clear(buffer, offset + samplesWritten, count - samplesWritten);
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
		_outputStage.Process(
			_tickSamples,
			_renderOptions.ChannelCount,
			_renderOptions.SampleRate,
			_amigaHardwareStateProvider?.AmigaHardwareState.AudioFilterEnabled == true);
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
