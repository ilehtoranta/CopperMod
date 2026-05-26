using System;
using System.Collections.Generic;
using System.Globalization;
using CopperMod.Abstractions;

namespace CopperMod.Cust
{
    internal sealed class CustSong : IModuleSong, IAmigaHardwareStateProvider, IModuleChannelWaveformProvider, IModuleSubSongSelector
    {
        private readonly HunkFile _hunk;
        private readonly DeliTagTable _tags;
        private readonly ModuleMetadata _metadata;
        private readonly ModulePlaybackCapabilities _capabilities;
        private readonly List<ModuleSubSongMetadata> _subSongs;
        private CustMachine _machine;
        private TimeSpan _position;
        private bool _disposed;
        private bool _channelWaveformCaptureEnabled;

        public CustSong(HunkFile hunk, DeliTagTable tags, ModuleLoadContext? loadContext = null)
        {
            _hunk = hunk ?? throw new ArgumentNullException(nameof(hunk));
            _tags = tags ?? throw new ArgumentNullException(nameof(tags));
            _machine = new CustMachine(hunk, tags, loadContext);
            _subSongs = BuildSubSongs(_machine.SubSongCount);
            _metadata = CreateMetadata(hunk, _machine.SubSongCount);
            _capabilities = new ModulePlaybackCapabilities(
                canSeekByTime: true,
                canSeekByTrackerPosition: false,
                canReportDuration: false,
                canReportExactDuration: false,
                supportsTickRendering: true,
                supportsLoopControl: true,
                supportsStereoOutput: true,
                supportsSubSongs: _machine.SubSongCount > 1);
        }

        public ModuleMetadata Metadata => _metadata;

        public ModulePlaybackCapabilities Capabilities => _capabilities;

        public IReadOnlyList<ModuleDiagnostic> Diagnostics => _machine.Diagnostics;

        public SongDuration Duration => SongDuration.Unknown;

        public PlaybackPosition Position => PlaybackPosition.FromTime(_position);

        public bool LoopingEnabled { get; set; } = true;

        public AmigaHardwareState AmigaHardwareState => new AmigaHardwareState(_machine.AudioFilterEnabled);

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

        public int CurrentSubSongIndex => _machine.CurrentSubSongIndex;

        public IReadOnlyList<ModuleSubSongMetadata> SubSongs => _subSongs;

        internal IReadOnlyList<CustomRegisterWrite> CustomRegisterWrites => _machine.CustomRegisterWrites;

        public int GetCurrentTickFrameCount(AudioRenderOptions? options = null)
        {
            EnsureNotDisposed();
            options ??= AudioRenderOptions.Default;
            var frames = _machine.QuantumCycleCount / CustConstants.A500PalCpuClockHz * options.SampleRate;
            return Math.Clamp((int)Math.Round(frames), 1, CustConstants.MaxRenderFramesPerTick);
        }

        public void Reset()
        {
            EnsureNotDisposed();
            _position = TimeSpan.Zero;
            _machine.Reset(_machine.CurrentSubSongIndex);
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
            if (position == TimeSpan.Zero)
            {
                return;
            }

            var options = AudioRenderOptions.Default;
            var safety = 0;
            while (_position < position && safety++ < 25_000)
            {
                var frames = GetCurrentTickFrameCount(options);
                var buffer = new float[options.GetSampleCount(frames)];
                RenderTick(buffer, options);
            }
        }

        public void Seek(TrackerPosition position)
        {
            _ = position;
            throw new NotSupportedException("CUST playback does not support tracker-position seeking.");
        }

        public void SelectSubSong(int index)
        {
            EnsureNotDisposed();
            _machine.SelectSubSong(index);
            _position = TimeSpan.Zero;
            LastChannelWaveform = null;
        }

        public RenderResult Render(Span<float> destination, AudioRenderOptions? options = null)
        {
            EnsureNotDisposed();
            options ??= AudioRenderOptions.Default;
            var framesWritten = 0;
            var samplesWritten = 0;
            while (samplesWritten < destination.Length)
            {
                var frames = GetCurrentTickFrameCount(options);
                var samples = options.GetSampleCount(frames);
                if (samplesWritten + samples > destination.Length)
                {
                    break;
                }

                var result = RenderTick(destination.Slice(samplesWritten, samples), options);
                framesWritten += result.FramesWritten;
                samplesWritten += result.SamplesWritten;
                if (result.EndOfSong)
                {
                    break;
                }
            }

            if (samplesWritten < destination.Length)
            {
                destination.Slice(samplesWritten).Clear();
            }

            return new RenderResult(framesWritten, samplesWritten, Position, _machine.SongEnded && !LoopingEnabled);
        }

        public RenderResult RenderTick(Span<float> destination, AudioRenderOptions? options = null)
        {
            EnsureNotDisposed();
            options ??= AudioRenderOptions.Default;
            var frames = GetCurrentTickFrameCount(options);
            var samples = options.GetSampleCount(frames);
            if (destination.Length < samples)
            {
                throw new ArgumentException("Destination is too small for one CUST render quantum.", nameof(destination));
            }

            var slice = destination.Slice(0, samples);
            slice.Clear();
            if (_machine.SongEnded && !LoopingEnabled)
            {
                LastChannelWaveform = ChannelWaveformCaptureEnabled
                    ? new ModuleChannelWaveform(Array.Empty<ModuleChannelWaveformChannel>(), 0, options.SampleRate)
                    : null;
                return new RenderResult(frames, samples, Position, true);
            }

            _machine.RenderQuantum(slice, frames, options.ChannelCount, options.SampleRate, ChannelWaveformCaptureEnabled);
            LastChannelWaveform = _machine.LastChannelWaveform;
            _position += TimeSpan.FromSeconds(frames / (double)options.SampleRate);
            if (_machine.SongEnded && LoopingEnabled)
            {
                _machine.Reset(_machine.CurrentSubSongIndex);
            }

            return new RenderResult(frames, samples, Position, _machine.SongEnded && !LoopingEnabled);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _machine.End();
            _disposed = true;
        }

        private static ModuleMetadata CreateMetadata(HunkFile hunk, int subSongCount)
        {
            var tags = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Machine"] = "Amiga 500 PAL",
                ["Cpu"] = "MC68000",
                ["SubSongs"] = subSongCount.ToString(CultureInfo.InvariantCulture),
                ["CpuClockHz"] = CustConstants.A500PalCpuClockHz.ToString(CultureInfo.InvariantCulture),
                ["PaulaClockHz"] = CustConstants.A500PalPaulaClockHz.ToString(CultureInfo.InvariantCulture)
            };

            return new ModuleMetadata(
                title: DeliTagParser.ExtractVersionTitle(hunk),
                formatName: "Amiga CUST",
                formatVersion: "DeliTracker CUST",
                channelCount: CustConstants.PaulaChannelCount,
                instrumentCount: 0,
                sampleCount: 0,
                initialSpeed: null,
                initialTempo: CustConstants.A500PalVBlankHz,
                tags: tags);
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

        private void EnsureNotDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CustSong));
            }
        }
    }
}
