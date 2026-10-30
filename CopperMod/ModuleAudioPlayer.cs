using CopperMod.Abstractions;
using CopperMod.Rendering;
using CopperMod.Replayers;
using CopperMod.Sid;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace CopperMod;

internal sealed class ModuleAudioPlayer : IDisposable
{
	public const int SampleRate = 44100;
	public const int ChannelCount = 2;
	private static readonly TimeSpan InitialOutputLeadIn = TimeSpan.FromMilliseconds(500);
	private const int DesiredOutputLatencyMilliseconds = 250;
	private const int OutputBufferCount = 4;

	internal static readonly IReadOnlyList<IModuleFormat> SupportedFormats = ModuleFormatRegistry.CreateDefaultFormats();
	private readonly IReadOnlyList<IModuleFormat> _formats = SupportedFormats;
	private IModuleSong? _song;
	private ModuleSampleProvider? _sampleProvider;
	private AmigaOutputProfile _outputProfile = AmigaOutputProfile.A500;
	private C64OutputProfile _c64OutputProfile = C64OutputProfile.C64;
	private SidEmulationProfile _sidEmulationProfile = SidEmulationProfile.Balanced;
	private bool _waveformEnabled;
	private WaveformDisplayMode _waveformDisplayMode = WaveformDisplayMode.MixedOutput;
	private IWavePlayer? _output;
	private bool _disposed;
	private ModuleArchive? _archive;
	private IReadOnlyList<string> _archiveMembers = Array.Empty<string>();

	public event EventHandler? StateChanged;
	public event EventHandler? SongEnded;
	public IReadOnlyList<string> ArchiveMembers => _archiveMembers;
	public int ArchiveMemberIndex { get; private set; } = -1;

	public string? FilePath { get; private set; }

	public ModuleMetadata? Metadata => _song?.Metadata;

	public SongDuration Duration => _song?.Duration ?? SongDuration.Unknown;

	public IReadOnlyList<ModuleDiagnostic> Diagnostics => _song?.Diagnostics ?? Array.Empty<ModuleDiagnostic>();

	public PlaybackPosition Position => _sampleProvider?.Position ?? PlaybackPosition.FromTime(TimeSpan.Zero);

	public PlaybackBufferStatus BufferStatus => _sampleProvider?.BufferStatus ?? new PlaybackBufferStatus(
		TimeSpan.Zero,
		TimeSpan.Zero,
		TimeSpan.Zero,
		0,
		producerEnded: false,
		endOfSong: false);

	public ModuleOutputFamily OutputFamily => (_song as IModuleOutputFamilyProvider)?.OutputFamily ?? ModuleOutputFamily.Amiga;

	public bool HasC64Video => _song is IC64VideoFrameProvider { HasVideoFrameSource: true };

	public IModuleSubSongSelector? SubSongs => _song as IModuleSubSongSelector;

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

	public C64OutputProfile C64OutputProfile
	{
		get => _sampleProvider?.C64OutputProfile ?? _c64OutputProfile;
		set
		{
			_c64OutputProfile = value;
			if (_sampleProvider != null)
			{
				_sampleProvider.C64OutputProfile = value;
			}

			OnStateChanged();
		}
	}

	public SidEmulationProfile SidEmulationProfile
	{
		get => _sampleProvider?.SidEmulationProfile ?? _sidEmulationProfile;
		set
		{
			_sidEmulationProfile = value;
			if (_sampleProvider != null)
			{
				_sampleProvider.SidEmulationProfile = value;
			}

			OnStateChanged();
		}
	}

	public bool WaveformEnabled
	{
		get => _sampleProvider?.WaveformEnabled ?? _waveformEnabled;
		set
		{
			_waveformEnabled = value;
			if (_sampleProvider != null)
			{
				_sampleProvider.WaveformEnabled = value;
			}

			OnStateChanged();
		}
	}

	public WaveformDisplayMode WaveformDisplayMode
	{
		get => _sampleProvider?.WaveformDisplayMode ?? _waveformDisplayMode;
		set
		{
			_waveformDisplayMode = value;
			if (_sampleProvider != null)
			{
				_sampleProvider.WaveformDisplayMode = value;
			}

			OnStateChanged();
		}
	}

	public void Load(string path)
	{
		Load(path, null);
	}

	public void Load(string path, PlayerStartupOptions? startupOptions)
	{
		ThrowIfDisposed();
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("A module path is required.", nameof(path));
		}

		path = Path.GetFullPath(path);
		var data = File.ReadAllBytes(path);
		ModuleArchive? archive = null;
		IReadOnlyList<string> members = Array.Empty<string>();
		IModuleSong song;
		if (ModuleArchive.IsArchive(data))
		{
			archive = ModuleArchive.Open(data);
			members = ModuleFormatRegistry.GetArchiveMembers(archive, _formats);
			if (members.Count == 0) throw new ModuleLoadException("Archive contains no playable modules.");
			song = ModuleFormatRegistry.LoadArchiveMember(archive, members[0], _formats);
		}
		else song = ModuleFormatRegistry.Load(new ModuleLoadContext(data, path), _formats);
		InstallSong(song, path, startupOptions);
		_archive = archive;
		_archiveMembers = members;
		ArchiveMemberIndex = archive == null ? -1 : 0;
		OnStateChanged();
	}

	public void SelectArchiveMember(int index)
	{
		ThrowIfDisposed();
		if (_archive == null || index < 0 || index >= _archiveMembers.Count) throw new ArgumentOutOfRangeException(nameof(index));
		var song = ModuleFormatRegistry.LoadArchiveMember(_archive, _archiveMembers[index], _formats);
		InstallSong(song, FilePath!, null);
		ArchiveMemberIndex = index;
		OnStateChanged();
	}

	private void InstallSong(IModuleSong song, string path, PlayerStartupOptions? startupOptions)
	{
		ModuleSampleProvider? provider = null;
		IWavePlayer? output = null;
		try
		{
			startupOptions?.Apply(song);
			song.LoopingEnabled = false;
			provider = new ModuleSampleProvider(song, SampleRate, ChannelCount, _outputProfile, InitialOutputLeadIn, _c64OutputProfile, _sidEmulationProfile);
			provider.WaveformEnabled = _waveformEnabled;
			provider.WaveformDisplayMode = _waveformDisplayMode;
			output = CreateOutputDevice();
			output.Init(provider.ToWaveProvider());
		}
		catch
		{
			output?.Dispose();
			provider?.Dispose();
			song.Dispose();
			throw;
		}
		DisposePlaybackObjects();
		_song = song;
		_sampleProvider = provider;
		_output = output;
		provider.EndOfSongReached += (_, _) =>
		{
			if (!ReferenceEquals(provider, _sampleProvider)) return;
			OnStateChanged();
			SongEnded?.Invoke(this, EventArgs.Empty);
		};
		output.PlaybackStopped += (_, _) => OnStateChanged();
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

	public void SelectSubSong(int index)
	{
		ThrowIfDisposed();
		EnsureLoaded();
		if (_song is not IModuleSubSongSelector)
		{
			throw new NotSupportedException("The loaded module does not expose subtunes.");
		}

		_sampleProvider!.SelectSubSong(index);
		OnStateChanged();
	}

	public bool TryReadWaveformSnapshot(out WaveformSnapshot snapshot)
	{
		if (_sampleProvider != null)
		{
			return _sampleProvider.TryReadWaveformSnapshot(out snapshot);
		}

		snapshot = new WaveformSnapshot(Array.Empty<float>(), Array.Empty<float>(), 0, SampleRate);
		return false;
	}

	public bool TryReadC64VideoFrame(out C64VideoFrame frame)
	{
		if (_sampleProvider != null)
		{
			return _sampleProvider.TryReadC64VideoFrame(out frame);
		}

		frame = new C64VideoFrame(1, 1, new[] { new Argb32(255, 0, 0, 0) }, 0, TimeSpan.Zero);
		return false;
	}

	public void SetC64KeyPressed(C64Key key, bool pressed)
	{
		_sampleProvider?.SetC64KeyPressed(key, pressed);
	}

	public void ReleaseAllC64Keys()
	{
		_sampleProvider?.ReleaseAllC64Keys();
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

		_sampleProvider?.Dispose();
		_sampleProvider = null;
		_song?.Dispose();
		_song = null;
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

	private static IWavePlayer CreateOutputDevice()
	{
		try
		{
			return new WasapiOut(AudioClientShareMode.Shared, useEventSync: true, DesiredOutputLatencyMilliseconds);
		}
		catch (Exception) when (OperatingSystem.IsWindows())
		{
			return new WaveOutEvent
			{
				DesiredLatency = DesiredOutputLatencyMilliseconds,
				NumberOfBuffers = OutputBufferCount
			};
		}
	}

	private void OnStateChanged()
	{
		StateChanged?.Invoke(this, EventArgs.Empty);
	}
}
