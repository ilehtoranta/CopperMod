using System.Globalization;
using CopperMod.Abstractions;
using CopperMod.Amiga;

namespace CopperMod.Ahx;

internal sealed class Ahx68kSong :
    IModuleSong,
    IAmigaHardwareStateProvider,
    IModuleChannelWaveformProvider,
    IModuleSubSongSelector
{
    private readonly byte[] _moduleBytes;
    private readonly byte[] _playerBytes;
    private readonly AhxModule _module;
    private readonly List<ModuleSubSongMetadata> _subSongs;
    private Ahx68kReferenceMachine _machine;
    private TimeSpan _position;
    private int _currentSubSong;
    private int _completedLoops;
    private float[] _pendingPcm = Array.Empty<float>();
    private int _pendingSampleOffset;
    private int _pendingFrames;
    private int _pendingChannels;
    private int _pendingSampleRate;
    private bool _disposed;
    private bool _channelWaveformCaptureEnabled;

    public Ahx68kSong(ReadOnlySpan<byte> moduleBytes, ReadOnlySpan<byte> playerBytes)
    {
        _moduleBytes = moduleBytes.ToArray();
        _playerBytes = playerBytes.ToArray();
        _module = AhxParser.Parse(_moduleBytes);
        _machine = CreateMachine(0);
        _subSongs = BuildSubSongs(_machine.SubSongCount);
        Metadata = CreateMetadata(_module, _machine.SubSongCount);
        Capabilities = new ModulePlaybackCapabilities(
            canSeekByTime: true,
            canSeekByTrackerPosition: false,
            canReportDuration: true,
            canReportExactDuration: false,
            supportsTickRendering: true,
            supportsLoopControl: true,
            supportsStereoOutput: true,
            supportsSubSongs: _machine.SubSongCount > 1);
    }

    public ModuleMetadata Metadata { get; }

    public ModulePlaybackCapabilities Capabilities { get; }

    public IReadOnlyList<ModuleDiagnostic> Diagnostics => Array.Empty<ModuleDiagnostic>();

    public SongDuration Duration => SongDuration.Approximate(
        TimeSpan.FromSeconds(_module.Positions.Length * _module.TrackLength * 6.0 / (50 * _module.SpeedMultiplier)));

    public PlaybackPosition Position => new(_position, completedLoops: _completedLoops);

    public bool LoopingEnabled { get; set; } = true;

    public AmigaHardwareState AmigaHardwareState => new(_machine.AudioFilterEnabled);

    public bool ChannelWaveformCaptureEnabled
    {
        get => _channelWaveformCaptureEnabled;
        set
        {
            _channelWaveformCaptureEnabled = value;
            if (!value)
            {
                LastChannelWaveform = null;
            }
        }
    }

    public ModuleChannelWaveform? LastChannelWaveform { get; private set; }

    public int SubSongCount => _subSongs.Count;

    public int DefaultSubSongIndex => 0;

    public int CurrentSubSongIndex => _currentSubSong;

    public IReadOnlyList<ModuleSubSongMetadata> SubSongs => _subSongs;

    internal IReadOnlyList<AhxTraceEvent> Trace => _machine.Trace;

    public int GetCurrentTickFrameCount(AudioRenderOptions? options = null)
    {
        EnsureNotDisposed();
        options ??= AudioRenderOptions.Default;
        var numerator = checked(_machine.CiaIntervalCycles * options.SampleRate);
        return Math.Max(1, (int)((numerator + (AmigaConstants.A500PalCpuCyclesPerSecond / 2)) /
            AmigaConstants.A500PalCpuCyclesPerSecond));
    }

    public void Reset()
    {
        EnsureNotDisposed();
        ReplaceMachine(_currentSubSong);
        _position = TimeSpan.Zero;
        _completedLoops = 0;
        ClearPendingFrames();
        LastChannelWaveform = null;
    }

    public void Seek(TimeSpan position)
    {
        EnsureNotDisposed();
        if (position < TimeSpan.Zero)
        {
            position = TimeSpan.Zero;
        }

        Reset();
        var options = AudioRenderOptions.Default;
        while (_position < position)
        {
            var remainingFrames = (int)Math.Ceiling((position - _position).TotalSeconds * options.SampleRate);
            var frames = Math.Min(Math.Max(1, remainingFrames), GetCurrentTickFrameCount(options));
            var scratch = new float[options.GetSampleCount(frames)];
            Render(scratch, options);
            if (_machine.SongEnded && !LoopingEnabled)
            {
                break;
            }
        }
    }

    public void Seek(TrackerPosition position)
    {
        _ = position;
        throw new NotSupportedException("Cycle-scheduled AHX playback does not expose tracker-position seeking.");
    }

    public void SelectSubSong(int index)
    {
        EnsureNotDisposed();
        if ((uint)index >= _subSongs.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "AHX subsong index is outside the available range.");
        }

        _currentSubSong = index;
        Reset();
    }

    public RenderResult Render(Span<float> destination, AudioRenderOptions? options = null)
    {
        EnsureNotDisposed();
        options ??= AudioRenderOptions.Default;
        var channels = options.ChannelCount;
        var framesRequested = destination.Length / channels;
        var framesWritten = 0;
        var loopsBefore = _completedLoops;
        destination.Clear();

        if (_pendingFrames > 0 && (_pendingChannels != channels || _pendingSampleRate != options.SampleRate))
        {
            throw new InvalidOperationException("AHX output format cannot change while partial tick audio is pending.");
        }

        while (framesWritten < framesRequested)
        {
            if (_pendingFrames == 0 && _machine.SongEnded)
            {
                if (!LoopingEnabled)
                {
                    break;
                }

                _completedLoops++;
                ReplaceMachine(_currentSubSong);
            }

            if (_pendingFrames == 0)
            {
                FillPendingTick(options);
            }

            var frames = Math.Min(_pendingFrames, framesRequested - framesWritten);
            var samples = checked(frames * channels);
            _pendingPcm.AsSpan(_pendingSampleOffset, samples)
                .CopyTo(destination.Slice(framesWritten * channels, samples));
            _pendingSampleOffset += samples;
            _pendingFrames -= frames;
            framesWritten += frames;
            _position += TimeSpan.FromSeconds(frames / (double)options.SampleRate);
        }

        return new RenderResult(
            framesWritten,
            framesWritten * channels,
            Position,
            _machine.SongEnded && !LoopingEnabled,
            _completedLoops > loopsBefore,
            _completedLoops - loopsBefore);
    }

    public RenderResult RenderTick(Span<float> destination, AudioRenderOptions? options = null)
    {
        EnsureNotDisposed();
        options ??= AudioRenderOptions.Default;
        var frames = GetCurrentTickFrameCount(options);
        var samples = options.GetSampleCount(frames);
        if (destination.Length < samples)
        {
            throw new ArgumentException("Destination is too small for one AHX CIA tick.", nameof(destination));
        }

        return Render(destination[..samples], options);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _machine.Dispose();
    }

    private Ahx68kReferenceMachine CreateMachine(int subSong)
    {
        var machine = new Ahx68kReferenceMachine(_playerBytes, _moduleBytes);
        try
        {
            machine.Initialize(subSong);
            return machine;
        }
        catch
        {
            machine.Dispose();
            throw;
        }
    }

    private void ReplaceMachine(int subSong)
    {
        var replacement = CreateMachine(subSong);
        _machine.Dispose();
        _machine = replacement;
        ClearPendingFrames();
    }

    private void FillPendingTick(AudioRenderOptions options)
    {
        var frames = GetCurrentTickFrameCount(options);
        var samples = options.GetSampleCount(frames);
        if (_pendingPcm.Length < samples)
        {
            _pendingPcm = new float[samples];
        }

        var slice = _pendingPcm.AsSpan(0, samples);
        slice.Clear();
        _machine.RenderFrames(
            slice,
            frames,
            options.ChannelCount,
            options.SampleRate,
            ChannelWaveformCaptureEnabled);
        LastChannelWaveform = _machine.LastChannelWaveform;
        _pendingSampleOffset = 0;
        _pendingFrames = frames;
        _pendingChannels = options.ChannelCount;
        _pendingSampleRate = options.SampleRate;
    }

    private void ClearPendingFrames()
    {
        _pendingSampleOffset = 0;
        _pendingFrames = 0;
        _pendingChannels = 0;
        _pendingSampleRate = 0;
    }

    private static List<ModuleSubSongMetadata> BuildSubSongs(int count)
    {
        var result = new List<ModuleSubSongMetadata>(Math.Max(1, count));
        for (var i = 0; i < Math.Max(1, count); i++)
        {
            result.Add(new ModuleSubSongMetadata(i, "Subtune " + (i + 1).ToString(CultureInfo.InvariantCulture)));
        }

        return result;
    }

    private static ModuleMetadata CreateMetadata(AhxModule module, int subSongs)
    {
        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Machine"] = "Amiga 500 PAL",
            ["Cpu"] = "MC68000",
            ["Replay"] = "Original AHX 2.3d 68000 binary",
            ["SpeedMultiplier"] = module.SpeedMultiplier.ToString(CultureInfo.InvariantCulture),
            ["Restart"] = module.Restart.ToString(CultureInfo.InvariantCulture),
            ["TrackLength"] = module.TrackLength.ToString(CultureInfo.InvariantCulture),
            ["SubSongs"] = subSongs.ToString(CultureInfo.InvariantCulture),
            ["InstrumentNames"] = string.Join("|", module.InstrumentNames)
        };
        return new ModuleMetadata(
            string.IsNullOrWhiteSpace(module.Title) ? null : module.Title,
            "AHX",
            $"AHX{module.Version}",
            4,
            Math.Max(0, module.Instruments.Length - 1),
            0,
            6,
            50 * module.SpeedMultiplier,
            tags);
    }

    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

