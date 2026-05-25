using AmigaTracker.Abstractions;
using AmigaTracker.Med;
using AmigaTracker.ProTracker;
using NAudio.Wave;

namespace CopperMod;

internal sealed class ModuleAudioPlayer : IDisposable
{
	public const int SampleRate = 44100;
	public const int ChannelCount = 2;
	private static readonly TimeSpan InitialOutputLeadIn = TimeSpan.FromMilliseconds(500);

	internal static readonly IReadOnlyList<IModuleFormat> SupportedFormats = new IModuleFormat[]
	{
		new MmdFormat(),
		new ProTrackerFormat()
	};
	private readonly IReadOnlyList<IModuleFormat> _formats = SupportedFormats;
	private IModuleSong? _song;
	private ModuleSampleProvider? _sampleProvider;
	private AmigaOutputProfile _outputProfile = AmigaOutputProfile.A500;
	private WaveOutEvent? _output;
	private bool _disposed;

	public event EventHandler? StateChanged;

	public string? FilePath { get; private set; }

	public ModuleMetadata? Metadata => _song?.Metadata;

	public SongDuration Duration => _song?.Duration ?? SongDuration.Unknown;

	public IReadOnlyList<ModuleDiagnostic> Diagnostics => _song?.Diagnostics ?? Array.Empty<ModuleDiagnostic>();

	public PlaybackPosition Position => _sampleProvider?.Position ?? PlaybackPosition.FromTime(TimeSpan.Zero);

	public bool IsLoaded => _song != null;

	public PlaybackState PlaybackState => _output?.PlaybackState ?? PlaybackState.Stopped;

	public float Volume
	{
		get => _sampleProvider?.Volume ?? 1.0f;
		set
		{
			if (_sampleProvider != null)
			{
				_sampleProvider.Volume = Math.Clamp(value, 0.0f, 1.0f);
			}
		}
	}

	public AmigaOutputProfile OutputProfile
	{
		get => _sampleProvider?.OutputProfile ?? _outputProfile;
		set
		{
			_outputProfile = value;
			if (_sampleProvider != null)
			{
				_sampleProvider.OutputProfile = value;
			}

			OnStateChanged();
		}
	}

	public void Load(string path)
	{
		ThrowIfDisposed();
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("A module path is required.", nameof(path));
		}

		path = Path.GetFullPath(path);
		var data = File.ReadAllBytes(path);
		var format = _formats.FirstOrDefault(candidate => candidate.CanLoad(data));
		if (format == null)
		{
			throw new InvalidDataException("The input file is not a supported tracker module.");
		}

		DisposePlaybackObjects();

		_song = format.Load(data);
		_song.LoopingEnabled = false;
		_sampleProvider = new ModuleSampleProvider(_song, SampleRate, ChannelCount, _outputProfile, InitialOutputLeadIn);
		_sampleProvider.EndOfSongReached += (_, _) => OnStateChanged();
		_output = new WaveOutEvent
		{
			DesiredLatency = 120,
			NumberOfBuffers = 3
		};
		_output.Init(_sampleProvider);
		_output.PlaybackStopped += (_, _) => OnStateChanged();
		FilePath = path;
		OnStateChanged();
	}

	public void Play()
	{
		ThrowIfDisposed();
		EnsureLoaded();
		if (_sampleProvider!.EndOfSong)
		{
			_sampleProvider.Reset();
		}

		_output!.Play();
		OnStateChanged();
	}

	public void Pause()
	{
		ThrowIfDisposed();
		if (_output == null)
		{
			return;
		}

		_output.Pause();
		OnStateChanged();
	}

	public void Stop()
	{
		ThrowIfDisposed();
		if (_output == null || _sampleProvider == null)
		{
			return;
		}

		_output.Pause();
		_sampleProvider.Reset();
		OnStateChanged();
	}

	public void Seek(TimeSpan position)
	{
		ThrowIfDisposed();
		EnsureLoaded();
		_sampleProvider!.Seek(position < TimeSpan.Zero ? TimeSpan.Zero : position);
		OnStateChanged();
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		DisposePlaybackObjects();
		_disposed = true;
	}

	private void DisposePlaybackObjects()
	{
		_output?.Stop();
		_output?.Dispose();
		_output = null;

		_song?.Dispose();
		_song = null;
		_sampleProvider = null;
		FilePath = null;
	}

	private void EnsureLoaded()
	{
		if (_song == null || _output == null)
		{
			throw new InvalidOperationException("Load a module before starting playback.");
		}
	}

	private void ThrowIfDisposed()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(nameof(ModuleAudioPlayer));
		}
	}

	private void OnStateChanged()
	{
		StateChanged?.Invoke(this, EventArgs.Empty);
	}
}
