using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CopperMod.Abstractions;

namespace CopperMod.Sid
{
    /// <summary>
    /// Loaded PSID/RSID song.
    /// </summary>
    internal sealed class SidSong : IModuleSong, IModuleSubSongSelector, IModuleOutputFamilyProvider, IModuleChannelWaveformProvider, ISidVoiceMuteController, ISidEmulationProfileController, ISidLoopDetector, ISidDurationDetector, IC64AutostartController, IC64VideoFrameProvider, IC64KeyboardController
    {
        private readonly SidModule _module;
        private readonly ModuleMetadata _metadata;
        private readonly ModulePlaybackCapabilities _capabilities;
        private readonly IReadOnlyList<ModuleSubSongMetadata> _subSongs;
        private C64Machine _machine;
        private TimeSpan _position;
        private SidSampleClock? _sampleClock;
        private long[] _sampleTargetCycles = Array.Empty<long>();
        private int _currentSubSongIndex;
        private bool _channelWaveformCaptureEnabled;
        private SidEmulationProfile _sidEmulationProfile = SidEmulationProfile.Balanced;

        internal SidSong(SidModule module)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));
            _machine = CreateMachine();
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

        internal IReadOnlyList<DigimaxWrite> DigimaxWrites => _machine.DigimaxWrites;

        public SidEmulationProfile SidEmulationProfile
        {
            get => _sidEmulationProfile;
            set
            {
                if (value is not SidEmulationProfile.Balanced and not SidEmulationProfile.ReferenceMeasured)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown SID emulation profile.");
                }

                if (_sidEmulationProfile == value)
                {
                    return;
                }

                _sidEmulationProfile = value;
                var mutedVoicesMask = _machine.Sid.MutedVoicesMask;
                _machine = CreateMachine();
                Reset();
                _machine.Sid.MutedVoicesMask = mutedVoicesMask;
            }
        }

        public int MutedVoicesMask
        {
            get => _machine.Sid.MutedVoicesMask;
            set => _machine.Sid.MutedVoicesMask = value;
        }

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

        public int GetCurrentTickFrameCount(AudioRenderOptions? options = null)
        {
            options ??= AudioRenderOptions.Default;
            var sampleClock = GetSampleClock(options);
            return sampleClock.PeekFrameCount(_machine.Cycle, GetCurrentTickCycleCount());
        }

        public void Reset()
        {
            _position = TimeSpan.Zero;
            _sampleClock = null;
            LastChannelWaveform = null;
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
            var targetCycle = SidIntegerMath.TimeSpanToCycles(position, _machine.Clock.CpuCyclesPerSecond);
            while (_machine.Cycle + GetCurrentTickCycleCount() < targetCycle)
            {
                var frames = GetCurrentTickFrameCount(options);
                var buffer = new float[options.GetSampleCount(frames)];
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

        public SidLoopDetectionResult DetectLoop(SidLoopDetectionOptions? options = null)
        {
            return SidLoopDetector.Detect(_module, _currentSubSongIndex, options ?? new SidLoopDetectionOptions());
        }

        public SidDurationDetectionResult DetectDuration(SidDurationDetectionOptions? options = null)
        {
            return SidLoopDetector.DetectDuration(_module, _currentSubSongIndex, options ?? new SidDurationDetectionOptions());
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
            var sampleClock = GetSampleClock(options);
            var tickCycles = GetCurrentTickCycleCount();
            var frames = sampleClock.PeekFrameCount(_machine.Cycle, tickCycles);
            var samples = options.GetSampleCount(frames);
            if (destination.Length < samples)
            {
                throw new ArgumentException("Destination is too small for one SID tick render.", nameof(destination));
            }

            EnsureSampleTargetCapacity(frames);
            var sampleTargetCycles = _sampleTargetCycles.AsSpan(0, frames);
            _ = sampleClock.FillSampleTargets(_machine.Cycle, tickCycles, sampleTargetCycles);
            var slice = destination.Slice(0, samples);
            if (ChannelWaveformCaptureEnabled)
            {
                _machine.Sid.BeginChannelCapture(frames, options.SampleRate);
            }

            RenderPreparedTick(slice, new AudioRenderOptionsAdapter(options.SampleRate, options.ChannelCount), sampleTargetCycles, tickCycles);
            LastChannelWaveform = ChannelWaveformCaptureEnabled
                ? _machine.Sid.FinishChannelCapture()
                : null;
            sampleClock.AdvanceFrames(frames);
            _position = SidIntegerMath.CyclesToTimeSpan(_machine.Cycle, _machine.Clock.CpuCyclesPerSecond);
            return new RenderResult(frames, samples, Position, false);
        }

        public void Dispose()
        {
        }

        public void ScheduleAutostartKey(string key, TimeSpan delay, TimeSpan hold)
        {
            if (!string.Equals(key, "f3", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(key, "space", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Only F3 and Space autostart are supported for C64 cartridge playback.", nameof(key));
            }

            _machine.ScheduleAutostartKey(key, delay, hold);
        }

        public bool HasVideoFrameSource => _module.IsCartridge;

        public bool TryGetLatestVideoFrame(out C64VideoFrame frame)
        {
            if (!HasVideoFrameSource)
            {
                frame = new C64VideoFrame(1, 1, new[] { new Argb32(255, 0, 0, 0) }, 0, TimeSpan.Zero);
                return false;
            }

            frame = _machine.RenderVideoFrame();
            return true;
        }

        public void SetKeyPressed(C64Key key, bool pressed)
        {
            _machine.SetKeyPressed(key, pressed);
        }

        public void ReleaseAllKeys()
        {
            _machine.ReleaseAllKeys();
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

            if (module.Cartridge != null)
            {
                tags["Cartridge"] = module.Cartridge.Type.ToString();
                tags["CartridgeBanks"] = module.Cartridge.BankCount.ToString(CultureInfo.InvariantCulture);
            }

            return new ModuleMetadata(
                title: string.IsNullOrWhiteSpace(module.Title) ? null : module.Title,
                formatName: module.IsCartridge ? "C64 CRT" : "SID",
                formatVersion: $"{module.Kind} v{module.Version}",
                channelCount: module.Chips.Count * 3,
                instrumentCount: 0,
                sampleCount: 0,
                initialSpeed: null,
                initialTempo: UsesCiaTiming(module, module.DefaultSubSongIndex)
                    ? SidConstants.CiaTimerRefreshHz
                    : module.Clock == SidClock.Ntsc ? SidConstants.NtscRefreshHz : SidConstants.PalRefreshHz,
                tags: tags);
        }

        private C64Machine CreateMachine()
        {
            return new C64Machine(_module, sidEmulationProfile: _sidEmulationProfile);
        }

        private static string FormatSubSongTitle(int index)
        {
            return "Subtune " + (index + 1).ToString(CultureInfo.InvariantCulture);
        }

        private long GetCurrentTickCycleCount()
        {
            return GetCurrentTickCycleCount(_module, _currentSubSongIndex, _machine);
        }

        internal static long GetCurrentTickCycleCount(SidModule module, int subSongIndex, C64Machine machine)
        {
            return UsesCiaTiming(module, subSongIndex)
                ? Math.Max(1, machine.PsidCiaTimerAIntervalCycles)
                : machine.Clock.CyclesPerFrame;
        }

        private SidSampleClock GetSampleClock(AudioRenderOptions options)
        {
            var cpuCyclesPerSecond = _machine.Clock.CpuCyclesPerSecond;
            if (_sampleClock == null || !_sampleClock.Matches(cpuCyclesPerSecond, options.SampleRate))
            {
                _sampleClock = new SidSampleClock(cpuCyclesPerSecond, options.SampleRate, _machine.Cycle);
            }

            return _sampleClock;
        }

        private void EnsureSampleTargetCapacity(int frames)
        {
            if (_sampleTargetCycles.Length >= frames)
            {
                return;
            }

            var capacity = Math.Max(frames, Math.Max(1, _sampleTargetCycles.Length * 2));
            _sampleTargetCycles = new long[capacity];
        }

        [HotPath]
        private void RenderPreparedTick(
            Span<float> destination,
            AudioRenderOptionsAdapter options,
            ReadOnlySpan<long> sampleTargetCycles,
            long tickCycles)
        {
            _machine.RenderFrame(destination, options, sampleTargetCycles, tickCycles);
        }

        internal static bool UsesCiaTiming(SidModule module, int subSongIndex)
        {
            if (module.IsRsid)
            {
                return false;
            }

            var bitIndex = Math.Min(Math.Max(subSongIndex, 0), 31);
            return ((module.Speed >> bitIndex) & 1U) != 0;
        }
    }
}
