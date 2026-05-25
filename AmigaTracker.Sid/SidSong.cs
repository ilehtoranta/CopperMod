using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AmigaTracker.Abstractions;

namespace AmigaTracker.Sid
{
    /// <summary>
    /// Loaded PSID/RSID song.
    /// </summary>
    internal sealed class SidSong : IModuleSong, IModuleSubSongSelector, IModuleOutputFamilyProvider
    {
        private readonly SidModule _module;
        private readonly C64Machine _machine;
        private readonly ModuleMetadata _metadata;
        private readonly ModulePlaybackCapabilities _capabilities;
        private readonly IReadOnlyList<ModuleSubSongMetadata> _subSongs;
        private TimeSpan _position;
        private int _currentSubSongIndex;

        internal SidSong(SidModule module)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));
            _machine = new C64Machine(module);
            _currentSubSongIndex = module.DefaultSubSongIndex;
            _subSongs = Enumerable.Range(0, module.SubSongCount)
                .Select(index => new ModuleSubSongMetadata(index, FormatSubSongTitle(index)))
                .ToArray();
            _metadata = CreateMetadata(module);
            _capabilities = new ModulePlaybackCapabilities(
                canSeekByTime: true,
                canSeekByTrackerPosition: false,
                canReportDuration: false,
                canReportExactDuration: false,
                supportsTickRendering: true,
                supportsLoopControl: true,
                supportsStereoOutput: true,
                supportsSubSongs: module.SubSongCount > 1);
            Reset();
        }

        public ModuleMetadata Metadata => _metadata;

        public ModulePlaybackCapabilities Capabilities => _capabilities;

        public IReadOnlyList<ModuleDiagnostic> Diagnostics => _module.Diagnostics;

        public SongDuration Duration => SongDuration.Unknown;

        public PlaybackPosition Position => PlaybackPosition.FromTime(_position);

        public bool LoopingEnabled { get; set; } = true;

        public ModuleOutputFamily OutputFamily => ModuleOutputFamily.Commodore64;

        public int SubSongCount => _module.SubSongCount;

        public int DefaultSubSongIndex => _module.DefaultSubSongIndex;

        public int CurrentSubSongIndex => _currentSubSongIndex;

        public IReadOnlyList<ModuleSubSongMetadata> SubSongs => _subSongs;

        internal IReadOnlyList<SidRegisterWrite> SidWrites => _machine.SidWrites;

        public int GetCurrentTickFrameCount(AudioRenderOptions? options = null)
        {
            options ??= AudioRenderOptions.Default;
            var clock = C64ClockProfile.FromSidClock(_module.Clock);
            return Math.Max(1, (int)Math.Round(clock.CyclesPerFrame / clock.CpuClockHz * options.SampleRate));
        }

        public void Reset()
        {
            _position = TimeSpan.Zero;
            _machine.Reset(_currentSubSongIndex);
        }

        public void Seek(TimeSpan position)
        {
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
            var frames = GetCurrentTickFrameCount(options);
            var buffer = new float[options.GetSampleCount(frames)];
            while (_position + TimeSpan.FromSeconds(frames / (double)options.SampleRate) < position)
            {
                RenderTick(buffer, options);
            }
        }

        public void Seek(TrackerPosition position)
        {
            _ = position;
            throw new NotSupportedException("SID playback does not support tracker-position seeking.");
        }

        public void SelectSubSong(int index)
        {
            if (index < 0 || index >= _module.SubSongCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "SID subtune index is outside the available range.");
            }

            _currentSubSongIndex = index;
            Reset();
        }

        public RenderResult Render(Span<float> destination, AudioRenderOptions? options = null)
        {
            options ??= AudioRenderOptions.Default;
            var samplesWritten = 0;
            var framesWritten = 0;
            while (samplesWritten < destination.Length)
            {
                var frames = GetCurrentTickFrameCount(options);
                var samples = options.GetSampleCount(frames);
                if (samplesWritten + samples > destination.Length)
                {
                    break;
                }

                var result = RenderTick(destination.Slice(samplesWritten, samples), options);
                samplesWritten += result.SamplesWritten;
                framesWritten += result.FramesWritten;
            }

            return new RenderResult(framesWritten, samplesWritten, Position, false);
        }

        public RenderResult RenderTick(Span<float> destination, AudioRenderOptions? options = null)
        {
            options ??= AudioRenderOptions.Default;
            var frames = GetCurrentTickFrameCount(options);
            var samples = options.GetSampleCount(frames);
            if (destination.Length < samples)
            {
                throw new ArgumentException("Destination is too small for one SID video-frame render.", nameof(destination));
            }

            var slice = destination.Slice(0, samples);
            _machine.RenderFrame(slice, new AudioRenderOptionsAdapter(options.SampleRate, options.ChannelCount));
            _position += TimeSpan.FromSeconds(frames / (double)options.SampleRate);
            return new RenderResult(frames, samples, Position, false);
        }

        public void Dispose()
        {
        }

        private static ModuleMetadata CreateMetadata(SidModule module)
        {
            var tags = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Author"] = module.Author,
                ["Released"] = module.Released,
                ["Clock"] = module.Clock.ToString(),
                ["ChipModel"] = module.EffectiveChipModel.ToString(),
                ["SubSongs"] = module.SubSongCount.ToString(CultureInfo.InvariantCulture),
                ["DefaultSubSong"] = (module.DefaultSubSongIndex + 1).ToString(CultureInfo.InvariantCulture),
                ["LoadAddress"] = "$" + module.EffectiveLoadAddress.ToString("X4", CultureInfo.InvariantCulture),
                ["InitAddress"] = "$" + module.InitAddress.ToString("X4", CultureInfo.InvariantCulture),
                ["PlayAddress"] = "$" + module.PlayAddress.ToString("X4", CultureInfo.InvariantCulture)
            };

            return new ModuleMetadata(
                title: string.IsNullOrWhiteSpace(module.Title) ? null : module.Title,
                formatName: "SID",
                formatVersion: $"{module.Kind} v{module.Version}",
                channelCount: module.Chips.Count * 3,
                instrumentCount: 0,
                sampleCount: 0,
                initialSpeed: null,
                initialTempo: module.Clock == SidClock.Ntsc ? SidConstants.NtscRefreshHz : SidConstants.PalRefreshHz,
                tags: tags);
        }

        private static string FormatSubSongTitle(int index)
        {
            return "Subtune " + (index + 1).ToString(CultureInfo.InvariantCulture);
        }
    }
}
