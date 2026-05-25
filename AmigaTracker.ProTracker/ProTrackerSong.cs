using System;
using System.Collections.Generic;
using AmigaTracker.Abstractions;

namespace AmigaTracker.ProTracker
{
internal sealed class ProTrackerSong : IModuleSong, IAmigaHardwareStateProvider, IModuleChannelWaveformProvider
{
    private const int MaxRenderFramesPerTick = 1_000_000;
    private const float PaulaVoiceScale = 0.25f;

    private readonly ProTrackerModule _module;
    private readonly ModuleMetadata _metadata;
    private readonly ModulePlaybackCapabilities _capabilities;
    private readonly VoiceState[] _voices;
    private readonly TimeSpan _estimatedDuration;
    private readonly int _silenceOffset;

    private int _songPosition;
    private int _patternPosition;
    private int _speed;
    private int _bpm;
    private int _counter;
    private int _patternBreakPosition;
    private bool _positionJumpFlag;
    private bool _patternBreakFlag;
    private int _patternDelayTime;
    private int _patternDelayTime2;
    private bool _ended;
    private bool _audioFilterEnabled;
    private TimeSpan _playbackPosition;
    private int _loopsCompleted;
    private bool _disposed;
    private bool _newRowProcessedThisTick;
    private int _traceSongPosition;
    private int _tracePatternPosition;
    private bool _channelWaveformCaptureEnabled;

    public ProTrackerSong(ProTrackerModule module)
    {
        _module = module ?? throw new ArgumentNullException(nameof(module));
        _silenceOffset = Math.Max(0, _module.SampleArea.Length - 2);
        _voices = new VoiceState[ProTrackerConstants.ChannelCount];
        for (var i = 0; i < _voices.Length; i++)
        {
            _voices[i] = new VoiceState(i);
        }

        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Layout"] = module.Layout.ToString(),
            ["Signature"] = module.Signature ?? string.Empty,
            ["ReplayProfile"] = module.LegacyProfile.ToString(),
            ["PatternCount"] = module.Patterns.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RestartPosition"] = module.RestartPosition.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        _metadata = new ModuleMetadata(
            string.IsNullOrWhiteSpace(module.Title) ? null : module.Title,
            "ProTracker MOD",
            module.FormatVersion,
            ProTrackerConstants.ChannelCount,
            module.SampleCount,
            module.SampleCount,
            ProTrackerConstants.DefaultSpeed,
            ProTrackerConstants.DefaultBpm,
            tags);

        _capabilities = new ModulePlaybackCapabilities(
            canSeekByTime: true,
            canSeekByTrackerPosition: true,
            canReportDuration: true,
            canReportExactDuration: false,
            supportsTickRendering: true,
            supportsLoopControl: true,
            supportsStereoOutput: true,
            supportsSubSongs: false);

        _estimatedDuration = EstimateDuration();
        Reset();
    }

    internal ProTrackerModule Module => _module;

    internal ProTrackerTickTrace? LastTrace { get; private set; }

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

    public ModuleMetadata Metadata => _metadata;

    public ModulePlaybackCapabilities Capabilities => _capabilities;

    public IReadOnlyList<ModuleDiagnostic> Diagnostics => _module.Diagnostics;

    public SongDuration Duration => SongDuration.Approximate(_estimatedDuration);

    public PlaybackPosition Position => new PlaybackPosition(
        _playbackPosition,
        new TrackerPosition(Math.Max(0, _songPosition), Math.Max(0, _patternPosition / ProTrackerConstants.PatternRowLength), Math.Max(0, _counter)),
        _loopsCompleted);

    public bool LoopingEnabled { get; set; } = true;

    public AmigaHardwareState AmigaHardwareState => new AmigaHardwareState(_audioFilterEnabled);

    public int GetCurrentTickFrameCount(AudioRenderOptions? options = null)
    {
        EnsureNotDisposed();
        options ??= AudioRenderOptions.Default;
        var frames = TickSeconds() * NormalizeSampleRate(options.SampleRate);
        return Clamp((int)Math.Round(frames), 1, MaxRenderFramesPerTick);
    }

    public void Reset()
    {
        EnsureNotDisposed();
        _songPosition = 0;
        _patternPosition = 0;
        _speed = ProTrackerConstants.DefaultSpeed;
        _bpm = ProTrackerConstants.DefaultBpm;
        _counter = 0;
        _patternBreakPosition = 0;
        _positionJumpFlag = false;
        _patternBreakFlag = false;
        _patternDelayTime = 0;
        _patternDelayTime2 = 0;
        _ended = _module.Patterns.Count == 0 || _module.SongLength == 0;
        _audioFilterEnabled = false;
        _playbackPosition = TimeSpan.Zero;
        _loopsCompleted = 0;
        _newRowProcessedThisTick = false;
        LastTrace = null;
        LastChannelWaveform = null;

        for (var i = 0; i < _voices.Length; i++)
        {
            _voices[i].Reset();
        }
    }

    public void Seek(TimeSpan position)
    {
        EnsureNotDisposed();
        if (position < TimeSpan.Zero)
        {
            position = TimeSpan.Zero;
        }

        Reset();
        var safety = 0;
        while (!_ended && _playbackPosition < position && safety++ < 1_000_000)
        {
            SimulateTick();
        }
    }

    public void Seek(TrackerPosition position)
    {
        EnsureNotDisposed();
        Reset();
        _songPosition = Clamp(position.Order, 0, Math.Max(0, _module.SongLength - 1));
        _patternPosition = Clamp(position.Row, 0, ProTrackerConstants.RowsPerPattern - 1) * ProTrackerConstants.PatternRowLength;
        _counter = Clamp(position.Tick, 0, Math.Max(0, _speed - 1));
    }

    public RenderResult Render(Span<float> destination, AudioRenderOptions? options = null)
    {
        EnsureNotDisposed();
        options ??= AudioRenderOptions.Default;
        var channels = NormalizeChannels(options.ChannelCount);
        var requestedFrames = destination.Length / channels;
        var framesWritten = 0;
        var loopsBefore = _loopsCompleted;

        while (framesWritten < requestedFrames && !_ended)
        {
            var tickFrames = GetCurrentTickFrameCount(options);
            if (tickFrames <= 0 || tickFrames > requestedFrames - framesWritten)
            {
                break;
            }

            RenderTick(destination.Slice(framesWritten * channels, tickFrames * channels), options);
            framesWritten += tickFrames;
        }

        if (framesWritten * channels < destination.Length)
        {
            destination.Slice(framesWritten * channels).Clear();
        }

        return new RenderResult(framesWritten, framesWritten * channels, Position, _ended, _loopsCompleted > loopsBefore, _loopsCompleted - loopsBefore);
    }

    public RenderResult RenderTick(Span<float> destination, AudioRenderOptions? options = null)
    {
        EnsureNotDisposed();
        options ??= AudioRenderOptions.Default;
        var channels = NormalizeChannels(options.ChannelCount);
        var sampleRate = NormalizeSampleRate(options.SampleRate);
        var frames = GetCurrentTickFrameCount(options);
        if (destination.Length < frames * channels)
        {
            throw new ArgumentException("The destination buffer is too small for the current ProTracker tick.", nameof(destination));
        }

        if (_ended)
        {
            destination.Slice(0, frames * channels).Clear();
            LastChannelWaveform = ChannelWaveformCaptureEnabled
                ? CreateChannelWaveform(CreateChannelSampleBuffers(_voices.Length, frames), new bool[_voices.Length], frames, sampleRate)
                : null;
            return new RenderResult(frames, frames * channels, Position, true);
        }

        var loopsBefore = _loopsCompleted;
        StepReplayTick();
        LastTrace = CaptureTrace(frames, sampleRate);
        Mix(destination.Slice(0, frames * channels), frames, channels, sampleRate);
        AdvanceSampleDelays(frames, sampleRate);
        AdvancePlaybackPosition(frames, sampleRate);
        if (_newRowProcessedThisTick)
        {
            AdvancePatternAfterRow();
        }

        return new RenderResult(frames, frames * channels, Position, _ended, _loopsCompleted > loopsBefore, _loopsCompleted - loopsBefore);
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private void SimulateTick()
    {
        StepReplayTick();
        var frames = GetCurrentTickFrameCount(AudioRenderOptions.Default);
        AdvanceSampleDelays(frames, AudioRenderOptions.Default.SampleRate);
        AdvancePlaybackPosition(frames, AudioRenderOptions.Default.SampleRate);
        if (_newRowProcessedThisTick)
        {
            AdvancePatternAfterRow();
        }
    }

    private void StepReplayTick()
    {
        ClearTransientVoiceState();
        _newRowProcessedThisTick = false;
        _traceSongPosition = _songPosition;
        _tracePatternPosition = _patternPosition;

        _counter++;
        if (_counter < _speed)
        {
            ApplyTickEffects();
            return;
        }

        _counter = 0;
        if (_patternDelayTime2 == 0)
        {
            ProcessCurrentRow();
        }
        else
        {
            ApplyTickEffects();
        }

        _newRowProcessedThisTick = true;
    }

    private void ProcessCurrentRow()
    {
        var pattern = CurrentPattern();
        var row = Clamp(_patternPosition / ProTrackerConstants.PatternRowLength, 0, ProTrackerConstants.RowsPerPattern - 1);
        for (var channel = 0; channel < _voices.Length; channel++)
        {
            ProcessCell(_voices[channel], pattern.Cells[row, channel]);
        }
    }

    private void ProcessCell(VoiceState voice, ProTrackerCell cell)
    {
        voice.Note = cell.Period;
        voice.Command = cell.Effect;
        voice.CommandParameter = cell.Parameter;
        voice.OutputPeriod = voice.Period;

        if (cell.SampleNumber > 0 && cell.SampleNumber <= _module.Samples.Count)
        {
            LoadInstrument(voice, cell.SampleNumber);
        }

        if (cell.Period == 0)
        {
            CheckMoreEffects(voice);
            return;
        }

        if (cell.Effect == 0xE && (cell.Parameter & 0xF0) == 0x50)
        {
            SetFineTune(voice, cell.Parameter & 0x0F);
        }

        if (cell.Effect == 0x3 || cell.Effect == 0x5)
        {
            SetTonePortamentoTarget(voice, cell.Period);
            CheckMoreEffects(voice);
            return;
        }

        if (cell.Effect == 0x9)
        {
            CheckMoreEffects(voice);
        }

        SetPeriodFromPatternPeriod(voice, cell.Period);
        if (cell.Effect == 0xE && (cell.Parameter & 0xF0) == 0xD0)
        {
            CheckMoreEffects(voice);
            return;
        }

        TriggerVoice(voice);
        CheckMoreEffects(voice);
    }

    private void LoadInstrument(VoiceState voice, int sampleNumber)
    {
        var sample = _module.Samples[sampleNumber - 1];
        voice.SampleNumber = sampleNumber;
        voice.Sample = sample;
        voice.StartOffset = sample.SampleAreaOffset;
        voice.LengthWords = sample.LengthWords;
        voice.FineTune = sample.FineTune;
        voice.Volume = sample.Volume;
        voice.OutputVolume = sample.Volume;
        voice.PeriodTable = ProTrackerConstants.FineTunePeriods[voice.FineTune & 0x0F];
        voice.RepeatLengthWords = sample.RepeatLengthWords <= 0 ? 1 : sample.RepeatLengthWords;
        if (sample.RepeatOffsetUnits != 0)
        {
            voice.LoopStartOffset = sample.SampleAreaOffset + sample.RepeatOffsetBytes;
            voice.InitialLengthWords = Math.Max(1, (sample.RepeatOffsetBytes / 2) + voice.RepeatLengthWords);
        }
        else
        {
            voice.LoopStartOffset = sample.SampleAreaOffset;
            voice.InitialLengthWords = Math.Max(1, sample.LengthWords);
        }

        voice.ReloadsSilence = sample.RepeatOffsetUnits + sample.RepeatLengthUnits <= 1;
        if (voice.ReloadsSilence)
        {
            voice.LoopStartOffset = _silenceOffset;
            voice.RepeatLengthWords = 1;
        }
    }

    private void SetPeriodFromPatternPeriod(VoiceState voice, int patternPeriod)
    {
        var index = FindPeriodIndex(ProTrackerConstants.NormalPeriods, patternPeriod);
        voice.Period = voice.PeriodTable[index];
        voice.OutputPeriod = voice.Period;
        voice.WantedPeriod = 0;
    }

    private void SetTonePortamentoTarget(VoiceState voice, int patternPeriod)
    {
        var index = FindPeriodIndex(voice.PeriodTable, patternPeriod);
        if ((voice.FineTune & 0x08) != 0 && index > 0)
        {
            index--;
        }

        var wanted = voice.PeriodTable[index];
        voice.WantedPeriod = wanted;
        voice.TonePortamentoDirection = 0;
        if (wanted == voice.Period)
        {
            voice.WantedPeriod = 0;
        }
        else if (wanted < voice.Period)
        {
            voice.TonePortamentoDirection = 1;
        }
    }

    private void TriggerVoice(VoiceState voice)
    {
        if (voice.Sample == null || voice.Period <= 0)
        {
            return;
        }

        voice.CurrentSampleOffset = voice.StartOffset;
        voice.CurrentSampleLengthWords = Math.Max(1, voice.InitialLengthWords);
        voice.Position = voice.CurrentSampleOffset;
        voice.InitialPlaybackEnd = voice.CurrentSampleOffset + (voice.CurrentSampleLengthWords * 2);
        voice.ReloadStartOffset = voice.LoopStartOffset;
        voice.ReloadLengthWords = Math.Max(1, voice.RepeatLengthWords);
        voice.InInitialPlayback = true;
        voice.IsAudible = true;
        voice.TriggeredThisTick = true;
        voice.PaulaStartDelaySeconds = ProTrackerConstants.DmaWaitSeconds;
        voice.OutputPeriod = voice.Period;
        if ((voice.WaveControl & 0x04) == 0)
        {
            voice.VibratoPosition = 0;
        }

        if ((voice.WaveControl & 0x40) == 0)
        {
            voice.TremoloPosition = 0;
        }
    }

    private void ApplyTickEffects()
    {
        foreach (var voice in _voices)
        {
            UpdateFunk(voice);
            if (((voice.Command << 8) | voice.CommandParameter) == 0)
            {
                voice.OutputPeriod = voice.Period;
                continue;
            }

            switch (voice.Command)
            {
                case 0x0:
                    Arpeggio(voice);
                    break;
                case 0x1:
                    PortamentoUp(voice, voice.CommandParameter);
                    break;
                case 0x2:
                    PortamentoDown(voice, voice.CommandParameter);
                    break;
                case 0x3:
                    TonePortamento(voice, true);
                    break;
                case 0x4:
                    Vibrato(voice, true);
                    break;
                case 0x5:
                    TonePortamento(voice, false);
                    VolumeSlide(voice);
                    break;
                case 0x6:
                    Vibrato(voice, false);
                    VolumeSlide(voice);
                    break;
                case 0x7:
                    Tremolo(voice, true);
                    break;
                case 0xA:
                    voice.OutputPeriod = voice.Period;
                    VolumeSlide(voice);
                    break;
                case 0xE:
                    ExtendedEffect(voice);
                    break;
                default:
                    voice.OutputPeriod = voice.Period;
                    break;
            }
        }
    }

    private void CheckMoreEffects(VoiceState voice)
    {
        switch (voice.Command)
        {
            case 0x9:
                SampleOffset(voice);
                break;
            case 0xB:
                PositionJump(voice);
                break;
            case 0xC:
                SetVolume(voice, voice.CommandParameter);
                break;
            case 0xD:
                PatternBreak(voice.CommandParameter);
                break;
            case 0xE:
                ExtendedEffect(voice);
                break;
            case 0xF:
                SetSpeedOrTempo(voice.CommandParameter);
                break;
        }
    }

    private void Arpeggio(VoiceState voice)
    {
        var step = ProTrackerConstants.ArpeggioTickTable[_counter & 0x1F];
        if (step == 0)
        {
            voice.OutputPeriod = voice.Period;
            return;
        }

        var offset = step == 1 ? (voice.CommandParameter >> 4) : (voice.CommandParameter & 0x0F);
        var index = FindPeriodIndex(voice.PeriodTable, voice.Period) + offset;
        if (index >= 0 && index < voice.PeriodTable.Length)
        {
            voice.OutputPeriod = voice.PeriodTable[index];
        }
    }

    private void PortamentoUp(VoiceState voice, int amount)
    {
        var value = amount & voice.LowMask;
        voice.LowMask = 0xFF;
        voice.Period -= value;
        if ((voice.Period & 0x0FFF) < ProTrackerConstants.MinPeriod)
        {
            voice.Period = (voice.Period & unchecked((int)0xF000)) | ProTrackerConstants.MinPeriod;
        }

        voice.OutputPeriod = voice.Period & 0x0FFF;
    }

    private void PortamentoDown(VoiceState voice, int amount)
    {
        var value = amount & voice.LowMask;
        voice.LowMask = 0xFF;
        voice.Period += value;
        if ((voice.Period & 0x0FFF) > ProTrackerConstants.MaxPeriod)
        {
            voice.Period = (voice.Period & unchecked((int)0xF000)) | ProTrackerConstants.MaxPeriod;
        }

        voice.OutputPeriod = voice.Period & 0x0FFF;
    }

    private void TonePortamento(VoiceState voice, bool updateSpeed)
    {
        if (updateSpeed && voice.CommandParameter != 0)
        {
            voice.TonePortamentoSpeed = voice.CommandParameter;
            voice.CommandParameter = 0;
        }

        if (voice.WantedPeriod == 0)
        {
            voice.OutputPeriod = voice.Period;
            return;
        }

        if (voice.TonePortamentoDirection != 0)
        {
            voice.Period -= voice.TonePortamentoSpeed;
            if (voice.Period <= voice.WantedPeriod)
            {
                voice.Period = voice.WantedPeriod;
                voice.WantedPeriod = 0;
            }
        }
        else
        {
            voice.Period += voice.TonePortamentoSpeed;
            if (voice.Period >= voice.WantedPeriod)
            {
                voice.Period = voice.WantedPeriod;
                voice.WantedPeriod = 0;
            }
        }

        var output = voice.Period;
        if ((voice.GlissandoFunk & 0x0F) != 0)
        {
            output = voice.PeriodTable[FindPeriodIndex(voice.PeriodTable, voice.Period)];
        }

        voice.OutputPeriod = output;
    }

    private void Vibrato(VoiceState voice, bool updateCommand)
    {
        if (updateCommand && voice.CommandParameter != 0)
        {
            var command = voice.VibratoCommand;
            var depth = voice.CommandParameter & 0x0F;
            if (depth != 0)
            {
                command = (command & 0xF0) | depth;
            }

            var speed = voice.CommandParameter & 0xF0;
            if (speed != 0)
            {
                command = (command & 0x0F) | speed;
            }

            voice.VibratoCommand = command;
        }

        var delta = GetWaveDelta(voice.VibratoPosition, voice.WaveControl & 0x03);
        delta = (delta * (voice.VibratoCommand & 0x0F)) >> 7;
        voice.OutputPeriod = unchecked((sbyte)voice.VibratoPosition) < 0 ? voice.Period - delta : voice.Period + delta;
        voice.VibratoPosition = (byte)(voice.VibratoPosition + ((voice.VibratoCommand >> 2) & 0x3C));
    }

    private void Tremolo(VoiceState voice, bool updateCommand)
    {
        if (updateCommand && voice.CommandParameter != 0)
        {
            var command = voice.TremoloCommand;
            var depth = voice.CommandParameter & 0x0F;
            if (depth != 0)
            {
                command = (command & 0xF0) | depth;
            }

            var speed = voice.CommandParameter & 0xF0;
            if (speed != 0)
            {
                command = (command & 0x0F) | speed;
            }

            voice.TremoloCommand = command;
        }

        var delta = GetWaveDelta(voice.TremoloPosition, (voice.WaveControl >> 4) & 0x03);
        delta = (delta * (voice.TremoloCommand & 0x0F)) >> 6;
        var volume = unchecked((sbyte)voice.TremoloPosition) < 0 ? voice.Volume - delta : voice.Volume + delta;
        voice.OutputVolume = Clamp(volume, 0, 64);
        voice.OutputPeriod = voice.Period;
        voice.TremoloPosition = (byte)(voice.TremoloPosition + ((voice.TremoloCommand >> 2) & 0x3C));
    }

    private void SampleOffset(VoiceState voice)
    {
        if (voice.CommandParameter != 0)
        {
            voice.SampleOffset = voice.CommandParameter;
        }

        var offsetWords = voice.SampleOffset << 7;
        if (offsetWords >= voice.LengthWords)
        {
            voice.InitialLengthWords = 1;
            return;
        }

        voice.InitialLengthWords = Math.Max(1, voice.InitialLengthWords - offsetWords);
        voice.StartOffset += offsetWords * 2;
    }

    private void VolumeSlide(VoiceState voice)
    {
        var up = voice.CommandParameter >> 4;
        if (up != 0)
        {
            voice.Volume = Math.Min(64, voice.Volume + up);
        }
        else
        {
            voice.Volume = Math.Max(0, voice.Volume - (voice.CommandParameter & 0x0F));
        }

        voice.OutputVolume = voice.Volume;
    }

    private void PositionJump(VoiceState voice)
    {
        _songPosition = (voice.CommandParameter - 1) & 0x7F;
        _patternBreakPosition = 0;
        _positionJumpFlag = true;
    }

    private void SetVolume(VoiceState voice, int volume)
    {
        voice.Volume = Math.Min(64, volume);
        voice.OutputVolume = voice.Volume;
    }

    private void PatternBreak(int parameter)
    {
        var row = ((parameter >> 4) * 10) + (parameter & 0x0F);
        if (row > 63)
        {
            _patternBreakPosition = 0;
        }
        else
        {
            _patternBreakPosition = row;
        }

        _positionJumpFlag = true;
    }

    private void SetSpeedOrTempo(int value)
    {
        if (value == 0)
        {
            _ended = true;
            return;
        }

        if (value < 32)
        {
            _counter = 0;
            _speed = value;
        }
        else
        {
            _bpm = value;
        }
    }

    private void ExtendedEffect(VoiceState voice)
    {
        var command = (voice.CommandParameter >> 4) & 0x0F;
        var value = voice.CommandParameter & 0x0F;
        switch (command)
        {
            case 0x0:
                if (_counter == 0)
                {
                    _audioFilterEnabled = value == 0;
                }
                break;
            case 0x1:
                if (_counter == 0)
                {
                    voice.LowMask = 0x0F;
                    PortamentoUp(voice, value);
                }
                break;
            case 0x2:
                if (_counter == 0)
                {
                    voice.LowMask = 0x0F;
                    PortamentoDown(voice, value);
                }
                break;
            case 0x3:
                voice.GlissandoFunk = (voice.GlissandoFunk & 0xF0) | value;
                break;
            case 0x4:
                voice.WaveControl = (voice.WaveControl & 0xF0) | value;
                break;
            case 0x5:
                SetFineTune(voice, value);
                break;
            case 0x6:
                PatternLoop(voice, value);
                break;
            case 0x7:
                voice.WaveControl = (voice.WaveControl & 0x0F) | (value << 4);
                break;
            case 0x8:
                break;
            case 0x9:
                RetrigNote(voice, value);
                break;
            case 0xA:
                if (_counter == 0)
                {
                    voice.Volume = Math.Min(64, voice.Volume + value);
                    voice.OutputVolume = voice.Volume;
                }
                break;
            case 0xB:
                if (_counter == 0)
                {
                    voice.Volume = Math.Max(0, voice.Volume - value);
                    voice.OutputVolume = voice.Volume;
                }
                break;
            case 0xC:
                if (value == _counter)
                {
                    voice.Volume = 0;
                    voice.OutputVolume = 0;
                }
                break;
            case 0xD:
                if (value == _counter && voice.Note != 0)
                {
                    TriggerVoice(voice);
                }
                break;
            case 0xE:
                if (_counter == 0 && _patternDelayTime2 == 0)
                {
                    _patternDelayTime = value + 1;
                }
                break;
            case 0xF:
                if (_counter == 0)
                {
                    voice.GlissandoFunk = (voice.GlissandoFunk & 0x0F) | (value << 4);
                    if (value != 0)
                    {
                        UpdateFunk(voice);
                    }
                }
                break;
        }
    }

    private void SetFineTune(VoiceState voice, int value)
    {
        voice.FineTune = value & 0x0F;
        voice.PeriodTable = ProTrackerConstants.FineTunePeriods[voice.FineTune];
    }

    private void PatternLoop(VoiceState voice, int value)
    {
        if (_counter != 0)
        {
            return;
        }

        if (value == 0)
        {
            voice.PatternLoopRow = (_patternPosition / ProTrackerConstants.PatternRowLength) & 63;
            return;
        }

        if (voice.PatternLoopCount != 0)
        {
            voice.PatternLoopCount--;
            if (voice.PatternLoopCount == 0)
            {
                return;
            }

            _patternBreakPosition = voice.PatternLoopRow;
            _patternBreakFlag = true;
            return;
        }

        voice.PatternLoopCount = value;
        _patternBreakPosition = voice.PatternLoopRow;
        _patternBreakFlag = true;
    }

    private void RetrigNote(VoiceState voice, int value)
    {
        if (value == 0)
        {
            return;
        }

        if (_counter == 0 && voice.Note != 0)
        {
            return;
        }

        if (_counter % value == 0)
        {
            TriggerVoice(voice);
        }
    }

    private void UpdateFunk(VoiceState voice)
    {
        var speed = (voice.GlissandoFunk >> 4) & 0x0F;
        if (speed == 0)
        {
            return;
        }

        voice.FunkOffset = (byte)(voice.FunkOffset + ProTrackerConstants.FunkTable[speed]);
        if ((voice.FunkOffset & 0x80) == 0)
        {
            return;
        }

        voice.FunkOffset = 0;
        if (voice.FunkWaveOffset < 0)
        {
            voice.FunkWaveOffset = voice.LoopStartOffset;
        }

        voice.FunkWaveOffset++;
        var loopEnd = voice.LoopStartOffset + (voice.RepeatLengthWords * 2);
        if (voice.FunkWaveOffset >= loopEnd)
        {
            voice.FunkWaveOffset = voice.LoopStartOffset;
        }

        if (voice.FunkWaveOffset >= 0 && voice.FunkWaveOffset < _module.SampleArea.Length)
        {
            _module.SampleArea[voice.FunkWaveOffset] = (-1.0f / 128.0f) - _module.SampleArea[voice.FunkWaveOffset];
        }
    }

    private void AdvancePatternAfterRow()
    {
        if (_ended)
        {
            return;
        }

        _patternPosition += ProTrackerConstants.PatternRowLength;
        if (_patternDelayTime != 0)
        {
            _patternDelayTime2 = _patternDelayTime;
            _patternDelayTime = 0;
        }

        if (_patternDelayTime2 != 0)
        {
            _patternDelayTime2--;
            if (_patternDelayTime2 != 0)
            {
                _patternPosition -= ProTrackerConstants.PatternRowLength;
            }
        }

        if (_patternBreakFlag)
        {
            _patternBreakFlag = false;
            _patternPosition = _patternBreakPosition * ProTrackerConstants.PatternRowLength;
            _patternBreakPosition = 0;
        }

        if (_patternPosition >= ProTrackerConstants.PatternLength)
        {
            NextPosition();
        }

        if (_positionJumpFlag)
        {
            NextPosition();
        }
    }

    private void NextPosition()
    {
        _patternPosition = _patternBreakPosition * ProTrackerConstants.PatternRowLength;
        _patternBreakPosition = 0;
        _positionJumpFlag = false;
        _songPosition = (_songPosition + 1) & 0x7F;
        if (_songPosition < _module.SongLength)
        {
            return;
        }

        if (LoopingEnabled)
        {
            _songPosition = 0;
            _loopsCompleted++;
        }
        else
        {
            _ended = true;
        }
    }

    private void Mix(Span<float> destination, int frames, int channels, int sampleRate)
    {
        destination.Clear();
        var captureChannels = ChannelWaveformCaptureEnabled;
        var channelSamples = captureChannels ? CreateChannelSampleBuffers(_voices.Length, frames) : null;
        var channelActive = captureChannels ? new bool[_voices.Length] : null;
        for (var voiceIndex = 0; voiceIndex < _voices.Length; voiceIndex++)
        {
            var voice = _voices[voiceIndex];
            if (!voice.IsAudible || voice.OutputPeriod <= 0)
            {
                continue;
            }

            var delayFrames = ConsumeStartDelayFrames(voice, frames, sampleRate);
            var volume = Clamp(voice.OutputVolume, 0, 64) / 64.0f * PaulaVoiceScale;
            var step = GetSampleStep(voice.OutputPeriod, sampleRate);
            if (channelActive != null)
            {
                channelActive[voiceIndex] = true;
            }

            for (var frame = 0; frame < frames; frame++)
            {
                if (frame < delayFrames)
                {
                    continue;
                }

                var rawSample = ReadVoiceSampleAndAdvance(voice, step);
                if (channelSamples != null)
                {
                    channelSamples[voiceIndex][frame] = rawSample;
                }

                var sample = rawSample * volume;
                WritePannedSample(destination, frame, channels, voiceIndex, sample);
            }
        }

        for (var i = 0; i < destination.Length; i++)
        {
            destination[i] = Math.Clamp(destination[i], -1.0f, 1.0f);
        }

        LastChannelWaveform = channelSamples == null || channelActive == null
            ? null
            : CreateChannelWaveform(channelSamples, channelActive, frames, sampleRate);
    }

    private static float[][] CreateChannelSampleBuffers(int channelCount, int frames)
    {
        var samples = new float[channelCount][];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = new float[frames];
        }

        return samples;
    }

    private static ModuleChannelWaveform CreateChannelWaveform(float[][] samples, bool[] active, int frames, int sampleRate)
    {
        var channels = new ModuleChannelWaveformChannel[samples.Length];
        for (var i = 0; i < samples.Length; i++)
        {
            channels[i] = new ModuleChannelWaveformChannel(i, samples[i], active[i]);
        }

        return new ModuleChannelWaveform(channels, frames, sampleRate);
    }

    private float ReadVoiceSampleAndAdvance(VoiceState voice, double step)
    {
        AdvanceLoopIfNeeded(voice);
        var index = (int)voice.Position;
        var sample = index >= 0 && index < _module.SampleArea.Length ? _module.SampleArea[index] : 0.0f;
        voice.Position += step;
        return sample;
    }

    private void AdvanceLoopIfNeeded(VoiceState voice)
    {
        var currentEnd = voice.InInitialPlayback ? voice.InitialPlaybackEnd : voice.ReloadStartOffset + (voice.ReloadLengthWords * 2);
        if (voice.Position < currentEnd)
        {
            return;
        }

        var loopLength = Math.Max(2, voice.ReloadLengthWords * 2);
        var overflow = voice.Position - currentEnd;
        voice.InInitialPlayback = false;
        voice.Position = voice.ReloadStartOffset + (overflow % loopLength);
    }

    private static void WritePannedSample(Span<float> destination, int frame, int channels, int voiceIndex, float sample)
    {
        if (channels == 1)
        {
            destination[frame] += sample;
            return;
        }

        var offset = frame * channels;
        if (voiceIndex == 0 || voiceIndex == 3)
        {
            destination[offset] += sample;
        }
        else
        {
            destination[offset + 1] += sample;
        }

        for (var channel = 2; channel < channels; channel++)
        {
            destination[offset + channel] += sample * 0.5f;
        }
    }

    private int ConsumeStartDelayFrames(VoiceState voice, int frames, int sampleRate)
    {
        if (voice.PaulaStartDelaySeconds <= 0.0)
        {
            return 0;
        }

        var delayFrames = Math.Min(frames, (int)Math.Ceiling(voice.PaulaStartDelaySeconds * sampleRate));
        voice.PaulaStartDelaySeconds = Math.Max(0.0, voice.PaulaStartDelaySeconds - (delayFrames / (double)sampleRate));
        return delayFrames;
    }

    private void AdvanceSampleDelays(int frames, int sampleRate)
    {
        _ = frames;
        _ = sampleRate;
    }

    private ProTrackerTickTrace CaptureTrace(int frames, int sampleRate)
    {
        var patternIndex = CurrentPatternIndex();
        var trace = new ProTrackerTickTrace
        {
            SongPosition = _traceSongPosition,
            PatternIndex = patternIndex,
            Row = _tracePatternPosition / ProTrackerConstants.PatternRowLength,
            Counter = _counter,
            Speed = _speed,
            Bpm = _bpm,
            FrameCount = frames,
            SampleRate = sampleRate,
            NewRowProcessed = _newRowProcessedThisTick,
            Ended = _ended
        };

        for (var i = 0; i < _voices.Length; i++)
        {
            var voice = _voices[i];
            trace.Voices.Add(new ProTrackerVoiceTrace
            {
                ChannelIndex = i,
                SampleNumber = voice.SampleNumber,
                Period = voice.Period,
                OutputPeriod = voice.OutputPeriod,
                WantedPeriod = voice.WantedPeriod,
                Volume = voice.Volume,
                TremoloVolume = voice.OutputVolume,
                FineTune = voice.FineTune,
                Effect = voice.Command,
                Parameter = voice.CommandParameter,
                SamplePosition = voice.Position,
                SampleStep = voice.OutputPeriod <= 0 ? 0.0 : GetSampleStep(voice.OutputPeriod, sampleRate),
                IsAudible = voice.IsAudible,
                TriggeredThisTick = voice.TriggeredThisTick,
                PaulaStartDelayFrames = voice.PaulaStartDelaySeconds <= 0.0 ? 0 : (int)Math.Ceiling(voice.PaulaStartDelaySeconds * sampleRate),
                PaulaInitialSampleOffset = voice.CurrentSampleOffset,
                PaulaInitialSampleLength = voice.CurrentSampleLengthWords * 2,
                PaulaReloadSampleOffset = voice.ReloadsSilence ? -1 : voice.ReloadStartOffset,
                PaulaReloadSampleLength = voice.ReloadLengthWords * 2,
                PaulaReloadsSilence = voice.ReloadsSilence
            });
        }

        return trace;
    }

    private void ClearTransientVoiceState()
    {
        for (var i = 0; i < _voices.Length; i++)
        {
            _voices[i].TriggeredThisTick = false;
            _voices[i].OutputVolume = _voices[i].Volume;
            _voices[i].OutputPeriod = _voices[i].Period;
        }
    }

    private ProTrackerPattern CurrentPattern()
    {
        return _module.Patterns[CurrentPatternIndex()];
    }

    private int CurrentPatternIndex()
    {
        if (_module.Patterns.Count == 0)
        {
            return 0;
        }

        var orderIndex = Clamp(_songPosition, 0, _module.OrderTable.Length - 1);
        return Clamp(_module.OrderTable[orderIndex], 0, _module.Patterns.Count - 1);
    }

    private TimeSpan EstimateDuration()
    {
        var ticks = Math.Max(1, _module.SongLength) * ProTrackerConstants.RowsPerPattern * ProTrackerConstants.DefaultSpeed;
        return TimeSpan.FromSeconds(ticks * (ProTrackerConstants.PalBpmTimerDiv / ProTrackerConstants.DefaultBpm / ProTrackerConstants.PalCiaClock));
    }

    private void AdvancePlaybackPosition(int frames, int sampleRate)
    {
        _playbackPosition += TimeSpan.FromSeconds(frames / (double)sampleRate);
    }

    private double TickSeconds()
    {
        return (ProTrackerConstants.PalBpmTimerDiv / Math.Max(32, _bpm)) / ProTrackerConstants.PalCiaClock;
    }

    private static double GetSampleStep(int period, int sampleRate)
    {
        if (period <= 0 || sampleRate <= 0)
        {
            return 0.0;
        }

        return (ProTrackerConstants.PalCpuClock / (period * 2.0)) / sampleRate;
    }

    private static int GetWaveDelta(byte position, int waveform)
    {
        var index = (position >> 2) & 0x1F;
        if (waveform == 0)
        {
            return ProTrackerConstants.VibratoTable[index];
        }

        index <<= 3;
        if (waveform == 1)
        {
            return unchecked((sbyte)position) < 0 ? 255 - index : index;
        }

        return 255;
    }

    private static int FindPeriodIndex(ushort[] table, int period)
    {
        for (var i = 0; i < table.Length; i++)
        {
            if (period >= table[i])
            {
                return i;
            }
        }

        return Math.Max(0, table.Length - 2);
    }

    private static int NormalizeChannels(int channels)
    {
        if (channels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "Channel count must be positive.");
        }

        return channels;
    }

    private static int NormalizeSampleRate(int sampleRate)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
        }

        return sampleRate;
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min)
        {
            return min;
        }

        if (value > max)
        {
            return max;
        }

        return value;
    }

    private void EnsureNotDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ProTrackerSong));
        }
    }

    private sealed class VoiceState
    {
        public VoiceState(int channelIndex)
        {
            ChannelIndex = channelIndex;
            PeriodTable = ProTrackerConstants.FineTunePeriods[0];
            Reset();
        }

        public int ChannelIndex { get; }

        public ProTrackerSample? Sample { get; set; }

        public int SampleNumber { get; set; }

        public int Note { get; set; }

        public int Command { get; set; }

        public int CommandParameter { get; set; }

        public int Period { get; set; }

        public int OutputPeriod { get; set; }

        public int WantedPeriod { get; set; }

        public int FineTune { get; set; }

        public ushort[] PeriodTable { get; set; }

        public int Volume { get; set; }

        public int OutputVolume { get; set; }

        public int StartOffset { get; set; }

        public int LengthWords { get; set; }

        public int InitialLengthWords { get; set; }

        public int CurrentSampleOffset { get; set; }

        public int CurrentSampleLengthWords { get; set; }

        public int InitialPlaybackEnd { get; set; }

        public int LoopStartOffset { get; set; }

        public int RepeatLengthWords { get; set; }

        public int ReloadStartOffset { get; set; }

        public int ReloadLengthWords { get; set; }

        public bool ReloadsSilence { get; set; }

        public bool InInitialPlayback { get; set; }

        public double Position { get; set; }

        public bool IsAudible { get; set; }

        public bool TriggeredThisTick { get; set; }

        public double PaulaStartDelaySeconds { get; set; }

        public int TonePortamentoDirection { get; set; }

        public int TonePortamentoSpeed { get; set; }

        public int VibratoCommand { get; set; }

        public byte VibratoPosition { get; set; }

        public int TremoloCommand { get; set; }

        public byte TremoloPosition { get; set; }

        public int WaveControl { get; set; }

        public int GlissandoFunk { get; set; }

        public int SampleOffset { get; set; }

        public int PatternLoopRow { get; set; }

        public int PatternLoopCount { get; set; }

        public byte FunkOffset { get; set; }

        public int FunkWaveOffset { get; set; }

        public int LowMask { get; set; } = 0xFF;

        public void Reset()
        {
            Sample = null;
            SampleNumber = 0;
            Note = 0;
            Command = 0;
            CommandParameter = 0;
            Period = 0;
            OutputPeriod = 0;
            WantedPeriod = 0;
            FineTune = 0;
            PeriodTable = ProTrackerConstants.FineTunePeriods[0];
            Volume = 0;
            OutputVolume = 0;
            StartOffset = 0;
            LengthWords = 0;
            InitialLengthWords = 1;
            CurrentSampleOffset = 0;
            CurrentSampleLengthWords = 1;
            InitialPlaybackEnd = 0;
            LoopStartOffset = 0;
            RepeatLengthWords = 1;
            ReloadStartOffset = 0;
            ReloadLengthWords = 1;
            ReloadsSilence = true;
            InInitialPlayback = false;
            Position = 0.0;
            IsAudible = false;
            TriggeredThisTick = false;
            PaulaStartDelaySeconds = 0.0;
            TonePortamentoDirection = 0;
            TonePortamentoSpeed = 0;
            VibratoCommand = 0;
            VibratoPosition = 0;
            TremoloCommand = 0;
            TremoloPosition = 0;
            WaveControl = 0;
            GlissandoFunk = 0;
            SampleOffset = 0;
            PatternLoopRow = 0;
            PatternLoopCount = 0;
            FunkOffset = 0;
            FunkWaveOffset = -1;
            LowMask = 0xFF;
        }
    }
}
}
