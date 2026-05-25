using System;
using System.Collections.Generic;
using AmigaTracker.Abstractions;

namespace AmigaTracker.Med
{
internal sealed class MmdSong : IModuleSong, IAmigaHardwareStateProvider
{
    private const int DefaultSampleRate = 44100;
    private const int DefaultChannels = 2;
    private const int MaxDurationSimulationTicks = 1_000_000;
    private const int MaxRenderFramesPerTick = 1_000_000;
    private const int NoCommand = -1;
    private static readonly int[] MultiOctaveAddTable = { 0, 6, 12, 18, 24, 30 };
    private static readonly int[] MultiOctaveDivTable = { 31, 7, 3, 15, 63, 127 };
    private static readonly int[] MultiOctaveShiftCounts =
    {
        4, 3, 2, 1, 1, 0, 2, 2, 1, 1, 0, 0, 1, 1, 0, 0, 0, 0,
        3, 3, 2, 2, 1, 0, 5, 4, 3, 2, 1, 0, 6, 5, 4, 3, 2, 1
    };
    private static readonly int[] MultiOctaveLengthMultipliers =
    {
        15, 7, 3, 1, 1, 0, 3, 3, 1, 1, 0, 0, 1, 1, 0, 0, 0, 0,
        7, 7, 3, 3, 1, 0, 31, 15, 7, 3, 1, 0, 63, 31, 15, 7, 3, 1
    };
    private static readonly int[] MultiOctavePeriodStarts =
    {
        12, 12, 12, 12, 24, 24, 0, 12, 12, 24, 24, 36, 0, 12, 12, 24, 36, 36,
        0, 12, 12, 24, 24, 24, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12
    };

    private readonly MmdModule _module;
    private readonly ModuleMetadata _metadata;
    private readonly ModulePlaybackCapabilities _capabilities;
    private readonly List<ModuleDiagnostic> _diagnostics = new List<ModuleDiagnostic>();
    private readonly VoiceState[] _voices;
    private readonly int[] _flatSequence;
    private readonly TimeSpan _estimatedDuration;

    private int _sequenceIndex;
    private int _row;
    private int _tick;
    private int _currentSpeed;
    private int _currentTempo;
    private bool _tempoIsBpm;
    private bool _ended;
    private bool _audioFilterEnabled;
    private TimeSpan _playbackPosition;
    private bool _disposed;
    private int _loopsCompleted;

    public MmdSong(MmdModule module)
    {
        _module = module ?? throw new ArgumentNullException(nameof(module));
        _flatSequence = BuildFlatSequence(module);
        if (_flatSequence.Length == 0 && module.Blocks.Count > 0)
        {
            _flatSequence = new[] { 0 };
        }

        var voiceCount = Math.Max(1, module.Song.NumTracks);
        _voices = new VoiceState[voiceCount];
        for (var i = 0; i < _voices.Length; i++)
        {
            _voices[i] = new VoiceState(i);
        }

        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MmdVersion"] = module.VersionName,
            ["DefaultTempo"] = module.Song.DefaultTempo.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Tempo2"] = module.Song.Tempo2.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["TrackCount"] = module.Song.NumTracks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["MixingChannels"] = module.Song.MixingChannels.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["MixingMode"] = module.UsesMixingMode ? "true" : "false"
        };

        _metadata = new ModuleMetadata(
            string.IsNullOrWhiteSpace(module.Song.SongName) ? null : module.Song.SongName,
            "MED/OctaMED",
            module.VersionName,
            module.Song.NumTracks,
            module.Instruments.Count,
            module.Instruments.Count,
            module.Song.Tempo2,
            module.Song.DefaultTempo,
            tags);

        _capabilities = new ModulePlaybackCapabilities(
            canSeekByTime: true,
            canSeekByTrackerPosition: true,
            canReportDuration: true,
            canReportExactDuration: false,
            supportsTickRendering: true,
            supportsLoopControl: true,
            supportsStereoOutput: true,
            supportsSubSongs: module.Song.PlaySequences.Count > 1 || module.Song.SectionTable.Count > 1);

        AddDiagnostics();
        Reset();
        _estimatedDuration = EstimateDuration();
        Reset();
    }

    internal MmdModule Module => _module;

    internal MmdTickTrace? LastTrace { get; private set; }

    public ModuleMetadata Metadata => _metadata;

    public ModulePlaybackCapabilities Capabilities => _capabilities;

    public IReadOnlyList<ModuleDiagnostic> Diagnostics => _diagnostics;

    public SongDuration Duration => SongDuration.Approximate(_estimatedDuration);

    public PlaybackPosition Position => new PlaybackPosition(
        _playbackPosition,
        new TrackerPosition(Math.Max(0, _sequenceIndex), Math.Max(0, _row), Math.Max(0, _tick)),
        _loopsCompleted);

    public bool LoopingEnabled { get; set; } = true;

    public AmigaHardwareState AmigaHardwareState => new AmigaHardwareState(_audioFilterEnabled);

    public int GetCurrentTickFrameCount(AudioRenderOptions? options = null)
    {
        EnsureNotDisposed();
        options = options ?? AudioRenderOptions.Default;
        var sampleRate = options.SampleRate <= 0 ? DefaultSampleRate : options.SampleRate;
        var frames = TickSeconds() * sampleRate;
        return Clamp((int)Math.Round(frames), 1, MaxRenderFramesPerTick);
    }

    public void Reset()
    {
        EnsureNotDisposed();
        _sequenceIndex = 0;
        _row = 0;
        _tick = 0;
        _currentSpeed = Math.Max(1, (int)_module.Song.Tempo2);
        _currentTempo = Math.Max(1, _module.Song.DefaultTempo);
        _tempoIsBpm = (_module.Song.Flags2 & MmdConstants.Flag2Bpm) != 0;
        _audioFilterEnabled = (_module.Song.Flags & MmdConstants.FlagFilterOn) != 0;
        _ended = _flatSequence.Length == 0 || _module.Blocks.Count == 0;
        _playbackPosition = TimeSpan.Zero;
        _loopsCompleted = 0;
        LastTrace = null;

        for (var i = 0; i < _voices.Length; i++)
        {
            _voices[i].Reset();
            ApplyTrackDefaults(_voices[i]);
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
        if (position == TimeSpan.Zero)
        {
            return;
        }

        var safety = 0;
        while (!_ended && _playbackPosition < position && safety++ < MaxDurationSimulationTicks)
        {
            SimulateTick();
        }
    }

    public void Seek(TrackerPosition position)
    {
        EnsureNotDisposed();
        Reset();
        _sequenceIndex = Clamp(position.Order, 0, Math.Max(0, _flatSequence.Length - 1));
        var block = CurrentBlock();
        _row = block == null ? 0 : Clamp(position.Row, 0, Math.Max(0, block.LineCount - 1));
        _tick = Clamp(position.Tick, 0, Math.Max(0, _currentSpeed - 1));
    }

    public RenderResult Render(Span<float> destination, AudioRenderOptions? options = null)
    {
        EnsureNotDisposed();
        options = options ?? AudioRenderOptions.Default;
        var channels = NormalizeChannels(options.ChannelCount);
        var sampleRate = NormalizeSampleRate(options.SampleRate);
        var requestedFrames = destination.Length / channels;

        var framesWritten = 0;
        var ticksRendered = 0;
        var loopsBefore = _loopsCompleted;
        while (framesWritten < requestedFrames && !_ended)
        {
            var tickFrames = GetCurrentTickFrameCount(options);
            if (tickFrames <= 0 || tickFrames > requestedFrames - framesWritten)
            {
                break;
            }

            var slice = destination.Slice(framesWritten * channels, tickFrames * channels);
            RenderTick(slice, options);
            framesWritten += tickFrames;
            ticksRendered++;
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
        options = options ?? AudioRenderOptions.Default;
        var channels = NormalizeChannels(options.ChannelCount);
        var sampleRate = NormalizeSampleRate(options.SampleRate);
        var frames = GetCurrentTickFrameCount(options);
        if (destination.Length < frames * channels)
        {
            throw new ArgumentException("The destination buffer is too small for the current MED tick.", nameof(destination));
        }

        if (_ended)
        {
            destination.Slice(0, frames * channels).Clear();
            return new RenderResult(frames, frames * channels, Position, true);
        }

        var loopsBefore = _loopsCompleted;
        ClearTickTraceFlags();
        if (_tick == 0)
        {
            ProcessRow();
        }

        ApplyHoldAndFade();
        if (_tick != 0)
        {
            ApplyTickEffects();
        }

        AdvanceSynths();
        LastTrace = CaptureTrace(frames, sampleRate);
        Mix(destination.Slice(0, frames * channels), frames, channels, sampleRate, options.InterpolationEnabled);
        AdvanceTick(frames, sampleRate);
        return new RenderResult(frames, frames * channels, Position, _ended, _loopsCompleted > loopsBefore, _loopsCompleted - loopsBefore);
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private void SimulateTick()
    {
        if (_ended)
        {
            return;
        }

        ClearTickTraceFlags();
        if (_tick == 0)
        {
            ProcessRow();
        }

        ApplyHoldAndFade();
        if (_tick != 0)
        {
            ApplyTickEffects();
        }

        AdvanceSynths();
        LastTrace = CaptureTrace(GetCurrentTickFrameCount(), DefaultSampleRate);
        var seconds = TickSeconds();
        _playbackPosition += TimeSpan.FromSeconds(seconds);
        AdvanceTickCounters();
    }

    private void ClearTickTraceFlags()
    {
        for (var i = 0; i < _voices.Length; i++)
        {
            _voices[i].PaulaPointerUpdatedThisTick = false;
        }
    }

    private void ProcessRow()
    {
        var block = CurrentBlock();
        if (block == null)
        {
            EndOrLoop();
            return;
        }

        if (_row >= block.LineCount)
        {
            AdvanceRow();
            block = CurrentBlock();
            if (block == null)
            {
                EndOrLoop();
                return;
            }
        }

        var tracks = Math.Min(_voices.Length, block.TrackCount);
        for (var track = 0; track < tracks; track++)
        {
            var voice = _voices[track];
            voice.RowDelay = 0;

            var cell = block.Cells[_row, track];
            var commands = CollectCommands(block, _row, track, cell);
            ProcessCell(voice, cell, commands);
        }
    }

    private List<MmdCommand> CollectCommands(MmdBlock block, int row, int track, MmdCell cell)
    {
        var commands = new List<MmdCommand>(1 + block.AdditionalCommandPages.Count);
        if (cell.Command != 0 || cell.Data != 0)
        {
            commands.Add(new MmdCommand { CommandNumber = cell.Command, Data = cell.Data });
        }

        for (var i = 0; i < block.AdditionalCommandPages.Count; i++)
        {
            var page = block.AdditionalCommandPages[i];
            if (row < page.Commands.GetLength(0) && track < page.Commands.GetLength(1))
            {
                var command = page.Commands[row, track];
                if (command.CommandNumber != 0 || command.Data != 0)
                {
                    commands.Add(command);
                }
            }
        }

        return commands;
    }

    private void ProcessCell(VoiceState voice, MmdCell cell, IReadOnlyList<MmdCommand> commands)
    {
        var note = NormalizeNote(cell.Note);
        var instrumentNumber = cell.Instrument;
        if (instrumentNumber > 0 && instrumentNumber <= _module.Instruments.Count)
        {
            PrepareInstrument(voice, instrumentNumber);
        }

        var hasPortamento = false;
        var hasNoteDelay = false;
        for (var i = 0; i < commands.Count; i++)
        {
            var command = NormalizeCommand(commands[i].CommandNumber);
            var data = commands[i].Data;
            ApplyPreEffect(voice, command, data, note, ref hasPortamento, ref hasNoteDelay);
        }

        if (note > 0 && !hasPortamento && !hasNoteDelay)
        {
            TriggerNote(voice, note, voice.PendingInstrumentNumber, restartSample: true);
        }
        else if (note > 0 && hasPortamento)
        {
            SetPortamentoTarget(voice, note);
        }

        for (var i = 0; i < commands.Count; i++)
        {
            ApplyPostTriggerRowEffect(voice, NormalizeCommand(commands[i].CommandNumber), commands[i].Data);
        }

        if (commands.Count > 0)
        {
            var last = commands[commands.Count - 1];
            voice.Command = NormalizeCommand(last.CommandNumber);
            voice.CommandData = last.Data;
        }
        else
        {
            voice.Command = NoCommand;
            voice.CommandData = 0;
            voice.Arpeggio = 0;
        }
    }

    private void ApplyPostTriggerRowEffect(VoiceState voice, int command, int data)
    {
        switch (command)
        {
            case 0xC:
                SetVoiceVolume(voice, DecodeVolumeCommand(data));
                break;
            case 0x18:
                if (data == 0)
                {
                    SetVoiceVolume(voice, 0);
                }

                break;
            case 0x1A:
                SetVoiceVolume(voice, voice.Volume + data);
                break;
            case 0x1B:
                SetVoiceVolume(voice, voice.Volume - data);
                break;
            case 0x15:
                voice.FineTune = unchecked((sbyte)data);
                voice.Period = voice.Note > 0 ? GetVoicePeriod(voice, voice.Note) : voice.Period;
                voice.BasePeriod = voice.Period;
                break;
            case 0x16:
                voice.Transpose = unchecked((sbyte)data);
                voice.Period = voice.Note > 0 ? GetVoicePeriod(voice, voice.Note) : voice.Period;
                voice.BasePeriod = voice.Period;
                break;
        }
    }

    private void ApplyPreEffect(VoiceState voice, int command, int data, int note, ref bool hasPortamento, ref bool hasNoteDelay)
    {
        switch (command)
        {
            case 0x0:
                voice.Arpeggio = data;
                break;
            case 0x3:
                hasPortamento = note > 0;
                if (data != 0)
                {
                    voice.PortamentoSpeed = data;
                }
                break;
            case 0x8:
                SetInitialHoldAndDecay(voice, data);
                break;
            case 0x9:
                SetSpeed(data);
                break;
            case 0x19:
                voice.SampleOffset = data << 8;
                break;
            case 0xA:
                voice.VolumeSlideMemory = data;
                break;
            case 0xB:
                JumpToSequence(data);
                break;
            case 0xC:
                SetVoiceVolume(voice, DecodeVolumeCommand(data));
                break;
            case 0xD:
                BreakToRow(DecodeBcdRow(data));
                break;
            case 0xF:
                ApplyTempoCommand(data);
                break;
            case 0x15:
                voice.FineTune = unchecked((sbyte)data);
                UpdateVoiceStep(voice);
                break;
            case 0x16:
                voice.Transpose = unchecked((sbyte)data);
                UpdateVoiceStep(voice);
                break;
            case 0x1D:
                BreakToRow(data);
                break;
            case 0x1F:
                var delay = (data >> 4) & 0x0F;
                hasNoteDelay = delay > 0 && note > 0;
                voice.DelayedNote = note;
                voice.NoteDelayTicks = delay;
                break;
            case 0x1E:
                voice.TrackVolume = Clamp(data, 0, 64);
                break;
        }
    }

    private void ApplyHoldAndFade()
    {
        for (var i = 0; i < _voices.Length; i++)
        {
            var voice = _voices[i];

            if (!voice.Releasing && voice.HoldTicks >= 0)
            {
                voice.HoldTicks--;
                if (voice.HoldTicks < 0)
                {
                    StartVoiceRelease(voice);
                }
            }

            if (voice.Releasing && voice.Synth == null && voice.FadeSpeed > 0)
            {
                SetVoiceVolume(voice, Math.Max(0, voice.Volume - voice.FadeSpeed));
            }
        }
    }

    private void ApplyTickEffects()
    {
        for (var i = 0; i < _voices.Length; i++)
        {
            var voice = _voices[i];

            if (voice.NoteDelayTicks > 0)
            {
                voice.NoteDelayTicks--;
                if (voice.NoteDelayTicks == 0 && voice.DelayedNote > 0)
                {
                    TriggerNote(voice, voice.DelayedNote, voice.PendingInstrumentNumber, restartSample: true);
                    voice.DelayedNote = 0;
                }
            }

            switch (voice.Command)
            {
                case 0x0:
                    ApplyArpeggio(voice);
                    break;
                case 0x1:
                    voice.Period = Math.Max(1, voice.Period - voice.CommandData);
                    UpdateVoiceStep(voice);
                    break;
                case 0x2:
                    voice.Period = Math.Min(32767, voice.Period + voice.CommandData);
                    UpdateVoiceStep(voice);
                    break;
                case 0x3:
                    ApplyPortamento(voice);
                    break;
                case 0x4:
                    ApplyVibrato(voice);
                    break;
                case 0x5:
                    ApplyPortamento(voice);
                    ApplyVolumeSlide(voice, voice.CommandData);
                    break;
                case 0x6:
                    ApplyVibrato(voice);
                    ApplyVolumeSlide(voice, voice.CommandData);
                    break;
                case 0x7:
                    ApplyTremolo(voice);
                    break;
                case 0xA:
                    ApplyVolumeSlide(voice, voice.CommandData == 0 ? voice.VolumeSlideMemory : voice.CommandData);
                    break;
                case 0x18:
                    if (_tick == voice.CommandData)
                    {
                        SetVoiceVolume(voice, 0);
                    }

                    break;
                case 0x1F:
                    ApplyRetrigger(voice);
                    break;
            }
        }
    }

    private void TriggerNote(VoiceState voice, int note, int instrumentNumber, bool restartSample)
    {
        if (instrumentNumber <= 0 || instrumentNumber > _module.Instruments.Count)
        {
            instrumentNumber = voice.InstrumentNumber;
        }

        if (instrumentNumber <= 0 || instrumentNumber > _module.Instruments.Count)
        {
            return;
        }

        var targetInstrument = _module.Instruments[instrumentNumber - 1];
        var preserveCurrentDma = restartSample
            && targetInstrument.Kind == MmdInstrumentKind.Synth
            && voice.SynthDmaType > 0
            && voice.SampleLength > 0
            && voice.LeftSamples.Length > 0;

        if (voice.InstrumentNumber != instrumentNumber || voice.Instrument == null)
        {
            PrepareInstrument(voice, instrumentNumber);
        }

        var instrument = voice.Instrument;
        if (instrument == null)
        {
            return;
        }

        voice.Note = note;
        voice.Releasing = false;
        voice.HoldTicks = DecodeInitialHold(voice.InitialHold);
        voice.FadeSpeed = voice.InitialDecay;
        voice.SynthVolume = 64;
        var normalizedNoteIndex = MmdPeriodTables.NormalizeNoteIndex(note, _module.Song.PlayTranspose, voice.Transpose);
        var periodTableIndex = normalizedNoteIndex;
        var useExtendedPeriodTable = false;
        SelectInstrumentWave(voice, instrument, normalizedNoteIndex, ref periodTableIndex, ref useExtendedPeriodTable, preserveCurrentDma);
        voice.NormalizedNoteIndex = normalizedNoteIndex;
        voice.PeriodTableIndex = periodTableIndex;
        voice.UsesExtendedPeriodTable = useExtendedPeriodTable;

        voice.Period = MmdPeriodTables.GetPeriodByIndex(periodTableIndex, voice.FineTune, useExtendedPeriodTable);
        voice.BasePeriod = voice.Period;
        voice.TargetPeriod = voice.Period;
        voice.ArpeggioPeriod = 0;
        voice.VibratoPhase = 0;
        voice.TremoloPhase = 0;
        voice.TremoloVolume = 0;
        UpdateVoiceStep(voice);

        if (restartSample && !preserveCurrentDma)
        {
            if (voice.SampleLength > 0 && voice.LeftSamples.Length > 0)
            {
                var initialOffset = NormalizeSampleStartOffset(voice.SampleOffset, voice.SampleLength);
                voice.Position = initialOffset;
                ConfigurePaulaReload(voice, initialOffset);
            }
            else
            {
                voice.Position = 0.0;
                ClearPaulaPlaybackState(voice);
            }
        }
    }

    private void PrepareInstrument(VoiceState voice, int instrumentNumber)
    {
        if (instrumentNumber <= 0 || instrumentNumber > _module.Instruments.Count)
        {
            return;
        }

        var instrument = _module.Instruments[instrumentNumber - 1];
        var sampleInfo = instrument.Index >= 0 && instrument.Index < _module.Song.SampleInfos.Length
            ? _module.Song.SampleInfos[instrument.Index]
            : null;

        voice.PendingInstrumentNumber = instrumentNumber;
        voice.InstrumentNumber = instrumentNumber;
        voice.Instrument = instrument;
        voice.FineTune = sampleInfo == null ? (sbyte)0 : sampleInfo.Extension.Finetune;
        voice.Transpose = sampleInfo == null ? (sbyte)0 : sampleInfo.Transpose;
        voice.Volume = sampleInfo != null && sampleInfo.DefaultVolume > 0 ? sampleInfo.DefaultVolume : 64;
        voice.InitialHold = sampleInfo == null ? 0 : sampleInfo.Extension.Hold;
        voice.InitialDecay = sampleInfo == null ? 0 : sampleInfo.Extension.Decay;
        voice.SampleOffset = 0;
    }

    private void SelectInstrumentWave(VoiceState voice, MmdInstrument instrument, int normalizedNoteIndex, ref int periodTableIndex, ref bool useExtendedPeriodTable, bool preserveCurrentDma)
    {
        if (!preserveCurrentDma)
        {
            voice.LeftSamples = instrument.LeftSamples;
            voice.RightSamples = instrument.RightSamples;
            voice.SampleLength = Math.Max(instrument.LeftSamples.Length, instrument.RightSamples.Length);
            voice.LoopStart = instrument.RepeatOffset;
            voice.LoopEnd = instrument.RepeatOffset + instrument.RepeatLength;
            voice.LoopEnabled = instrument.LoopEnabled;
            voice.SampleWindowOffset = 0;
        }

        if (instrument.Kind == MmdInstrumentKind.Sample)
        {
            voice.SynthDmaType = 0;
            ApplySampleTypeWindow(voice, instrument, normalizedNoteIndex, ref periodTableIndex, ref useExtendedPeriodTable);
        }

        if (instrument.Kind == MmdInstrumentKind.Synth || instrument.Kind == MmdInstrumentKind.Hybrid)
        {
            voice.SynthDmaType = instrument.Kind == MmdInstrumentKind.Synth ? 1 : -1;
            voice.Synth = SynthRuntime.FromInstrument(instrument);
            if (instrument.Kind == MmdInstrumentKind.Synth)
            {
                useExtendedPeriodTable = true;
            }
        }
        else
        {
            voice.SynthDmaType = 0;
            voice.Synth = null;
        }
    }

    private static void ApplySampleTypeWindow(VoiceState voice, MmdInstrument instrument, int normalizedNoteIndex, ref int periodTableIndex, ref bool useExtendedPeriodTable)
    {
        if (instrument.TypeCode == 0)
        {
            return;
        }

        if (instrument.TypeCode >= 7)
        {
            useExtendedPeriodTable = true;
            return;
        }

        var typeIndex = Clamp(instrument.TypeCode, 1, 6) - 1;
        var octave = normalizedNoteIndex / 12;
        var noteInOctave = normalizedNoteIndex % 12;
        if (octave > 5)
        {
            octave = 5;
        }

        var tableIndex = MultiOctaveAddTable[typeIndex] + octave;
        var divisor = MultiOctaveDivTable[typeIndex];
        var highestOctaveLength = divisor <= 0 ? 0 : voice.SampleLength / divisor;
        if (highestOctaveLength <= 0)
        {
            return;
        }

        var shift = MultiOctaveShiftCounts[tableIndex];
        var selectedLength = highestOctaveLength << shift;
        var selectedOffset = highestOctaveLength * MultiOctaveLengthMultipliers[tableIndex];
        selectedOffset = Clamp(selectedOffset, 0, Math.Max(0, voice.SampleLength - 1));
        selectedLength = Clamp(selectedLength, 0, Math.Max(0, voice.SampleLength - selectedOffset));
        selectedLength &= ~1;
        if (selectedLength <= 0)
        {
            return;
        }

        voice.SampleWindowOffset = selectedOffset;
        voice.LeftSamples = SliceSamples(instrument.LeftSamples, selectedOffset, selectedLength);
        voice.RightSamples = instrument.RightSamples.Length > 0 && !ReferenceEquals(instrument.RightSamples, instrument.LeftSamples)
            ? SliceSamples(instrument.RightSamples, selectedOffset, selectedLength)
            : voice.LeftSamples;
        voice.SampleLength = selectedLength;
        voice.LoopStart = instrument.RepeatOffset << shift;
        voice.LoopEnd = voice.LoopStart + (instrument.RepeatLength << shift);
        voice.LoopEnabled = instrument.LoopEnabled && voice.LoopEnd - voice.LoopStart > 2;
        periodTableIndex = noteInOctave + MultiOctavePeriodStarts[tableIndex];
    }

    private static float[] SliceSamples(float[] samples, int offset, int length)
    {
        if (samples.Length == 0 || length <= 0 || offset >= samples.Length)
        {
            return Array.Empty<float>();
        }

        length = Math.Min(length, samples.Length - offset);
        var slice = new float[length];
        Array.Copy(samples, offset, slice, 0, length);
        return slice;
    }

    private static int NormalizeSampleStartOffset(int requestedOffset, int sampleLength)
    {
        if (requestedOffset <= 0 || sampleLength <= 0)
        {
            return 0;
        }

        return requestedOffset < sampleLength ? requestedOffset : 0;
    }

    private static void ConfigurePaulaReload(VoiceState voice, int initialOffset)
    {
        var sampleLength = FloorToEvenByteCount(voice.SampleLength);
        initialOffset = FloorToEvenByteCount(Clamp(initialOffset, 0, sampleLength));
        voice.PaulaInitialSampleOffset = voice.SampleWindowOffset + initialOffset;
        voice.PaulaPointerUpdatedThisTick = true;
        voice.PaulaStartDelaySeconds = MmdConstants.PaulaDmaStartDelaySeconds;
        voice.PendingReloadLeftSamples = Array.Empty<float>();
        voice.PendingReloadRightSamples = Array.Empty<float>();
        voice.PendingReloadSampleLength = 0;
        voice.PendingReloadLoopStart = 0;
        voice.PendingReloadLoopEnd = 0;
        voice.PendingReloadLoopEnabled = false;

        if (voice.LoopEnabled)
        {
            var loopStart = FloorToEvenByteCount(Clamp(voice.LoopStart, 0, sampleLength));
            var loopEnd = FloorToEvenByteCount(Clamp(voice.LoopEnd <= loopStart ? sampleLength : voice.LoopEnd, loopStart, sampleLength));
            var loopLength = loopEnd - loopStart;
            if (loopLength > 2)
            {
                voice.PaulaInitialSampleLength = Math.Max(0, loopEnd - initialOffset);
                voice.PaulaReloadSampleOffset = voice.SampleWindowOffset + loopStart;
                voice.PaulaReloadSampleLength = loopLength;
                voice.PaulaReloadsSilence = false;
                return;
            }
        }

        voice.PaulaInitialSampleLength = Math.Max(0, sampleLength - initialOffset);
        voice.PaulaReloadSampleOffset = -1;
        voice.PaulaReloadSampleLength = 2;
        voice.PaulaReloadsSilence = true;
    }

    private static void ClearPaulaPlaybackState(VoiceState voice)
    {
        voice.PaulaInitialSampleOffset = 0;
        voice.PaulaInitialSampleLength = 0;
        voice.PaulaReloadSampleOffset = -1;
        voice.PaulaReloadSampleLength = 0;
        voice.PaulaReloadsSilence = false;
        voice.PaulaPointerUpdatedThisTick = false;
        voice.PaulaStartDelaySeconds = 0.0;
        voice.PendingReloadLeftSamples = Array.Empty<float>();
        voice.PendingReloadRightSamples = Array.Empty<float>();
        voice.PendingReloadSampleLength = 0;
        voice.PendingReloadLoopStart = 0;
        voice.PendingReloadLoopEnd = 0;
        voice.PendingReloadLoopEnabled = false;
    }

    private static int FloorToEvenByteCount(int byteCount)
    {
        return Math.Max(0, byteCount) & ~1;
    }

    private static void SelectSynthWaveform(VoiceState voice, MmdSynthWaveform wave, bool restartSample)
    {
        voice.LeftSamples = wave.Samples;
        voice.RightSamples = Array.Empty<float>();
        voice.SampleLength = wave.Samples.Length;
        voice.LoopStart = 0;
        voice.LoopEnd = wave.Samples.Length;
        voice.LoopEnabled = wave.Samples.Length > 2;
        voice.SampleWindowOffset = 0;
        if (restartSample)
        {
            voice.Position = 0.0;
            ConfigurePaulaReload(voice, 0);
        }
    }

    private static void QueueSynthWaveformReload(VoiceState voice, MmdSynthWaveform wave)
    {
        if (wave.Samples.Length == 0)
        {
            return;
        }

        voice.PendingReloadLeftSamples = wave.Samples;
        voice.PendingReloadRightSamples = Array.Empty<float>();
        voice.PendingReloadSampleLength = wave.Samples.Length;
        voice.PendingReloadLoopStart = 0;
        voice.PendingReloadLoopEnd = wave.Samples.Length;
        voice.PendingReloadLoopEnabled = wave.Samples.Length > 2;
        voice.PaulaPointerUpdatedThisTick = true;
        voice.PaulaReloadSampleOffset = 0;
        voice.PaulaReloadSampleLength = FloorToEvenByteCount(wave.Samples.Length);
        voice.PaulaReloadsSilence = false;
    }

    private int GetVoicePeriod(VoiceState voice, int note)
    {
        var normalizedNoteIndex = MmdPeriodTables.NormalizeNoteIndex(note, _module.Song.PlayTranspose, voice.Transpose);
        return MmdPeriodTables.GetPeriodByIndex(normalizedNoteIndex, voice.FineTune, voice.UsesExtendedPeriodTable);
    }

    private void SetPortamentoTarget(VoiceState voice, int note)
    {
        voice.TargetPeriod = GetVoicePeriod(voice, note);
        voice.Note = note;
    }

    private void SetVoiceVolume(VoiceState voice, int volume)
    {
        voice.Volume = Clamp(volume, 0, 64);
    }

    private int DecodeVolumeCommand(int data)
    {
        if ((_module.Song.Flags & MmdConstants.FlagVolHex) != 0 || data >= 0x80)
        {
            return data & 0x7F;
        }

        return (((data >> 4) & 0x0F) * 10) + (data & 0x0F);
    }

    private void SetInitialHoldAndDecay(VoiceState voice, int data)
    {
        voice.InitialDecay = (data >> 4) & 0x0F;
        voice.InitialHold = data & 0x0F;
    }

    private static int DecodeInitialHold(int hold)
    {
        return hold == 0 ? -1 : hold;
    }

    private void StartVoiceRelease(VoiceState voice)
    {
        voice.Releasing = true;
        if (voice.Synth != null)
        {
            voice.Synth.StartDecay(voice.FadeSpeed);
            return;
        }

        if (voice.FadeSpeed <= 0)
        {
            SetVoiceVolume(voice, 0);
        }
    }

    private void ApplyArpeggio(VoiceState voice)
    {
        if (voice.Arpeggio == 0 || voice.Note <= 0)
        {
            voice.ArpeggioPeriod = 0;
            return;
        }

        var phase = _tick % 3;
        var semitone = phase == 1 ? (voice.Arpeggio >> 4) : phase == 2 ? (voice.Arpeggio & 0x0F) : 0;
        voice.ArpeggioPeriod = semitone == 0
            ? 0
            : GetVoicePeriod(voice, voice.Note + semitone);
    }

    private void ApplyPortamento(VoiceState voice)
    {
        if (voice.TargetPeriod <= 0 || voice.PortamentoSpeed <= 0)
        {
            return;
        }

        if (voice.Period < voice.TargetPeriod)
        {
            voice.Period = Math.Min(voice.TargetPeriod, voice.Period + voice.PortamentoSpeed);
        }
        else if (voice.Period > voice.TargetPeriod)
        {
            voice.Period = Math.Max(voice.TargetPeriod, voice.Period - voice.PortamentoSpeed);
        }

        voice.BasePeriod = voice.Period;
        UpdateVoiceStep(voice);
    }

    private void ApplyVibrato(VoiceState voice)
    {
        var speed = voice.CommandData >> 4;
        var depth = voice.CommandData & 0x0F;
        if (speed != 0)
        {
            voice.VibratoSpeed = speed;
        }

        if (depth != 0)
        {
            voice.VibratoDepth = depth;
        }

        voice.VibratoPhase = (voice.VibratoPhase + voice.VibratoSpeed) & 0x3F;
        var value = Math.Sin((voice.VibratoPhase / 64.0) * Math.PI * 2.0) * voice.VibratoDepth * 4.0;
        voice.ArpeggioPeriod = Math.Max(1, voice.BasePeriod + (int)Math.Round(value));
    }

    private void ApplyTremolo(VoiceState voice)
    {
        var speed = voice.CommandData >> 4;
        var depth = voice.CommandData & 0x0F;
        if (speed != 0)
        {
            voice.TremoloSpeed = speed;
        }

        if (depth != 0)
        {
            voice.TremoloDepth = depth;
        }

        voice.TremoloPhase = (voice.TremoloPhase + voice.TremoloSpeed) & 0x3F;
        var value = Math.Sin((voice.TremoloPhase / 64.0) * Math.PI * 2.0) * voice.TremoloDepth * 2.0;
        voice.TremoloVolume = Clamp((int)Math.Round(value), -64, 64);
    }

    private void ApplyVolumeSlide(VoiceState voice, int data)
    {
        if (data == 0)
        {
            return;
        }

        var up = data >> 4;
        var down = data & 0x0F;
        if (up != 0)
        {
            SetVoiceVolume(voice, voice.Volume + up);
        }
        else
        {
            SetVoiceVolume(voice, voice.Volume - down);
        }
    }

    private void ApplyRetrigger(VoiceState voice)
    {
        var interval = voice.CommandData & 0x0F;
        if (interval <= 0 || _tick % interval != 0 || voice.Note <= 0)
        {
            return;
        }

        TriggerNote(voice, voice.Note, voice.InstrumentNumber, restartSample: true);
    }

    private void AdvanceSynths()
    {
        for (var i = 0; i < _voices.Length; i++)
        {
            var voice = _voices[i];
            var synth = voice.Synth;
            if (synth == null)
            {
                continue;
            }

            var changed = synth.Advance(voice);
            if (changed)
            {
                var wave = synth.CurrentWaveform();
                if (wave != null)
                {
                    if (voice.SampleLength <= 0 || voice.LeftSamples.Length == 0)
                    {
                        SelectSynthWaveform(voice, wave, restartSample: true);
                    }
                    else
                    {
                        QueueSynthWaveformReload(voice, wave);
                    }
                }
            }
        }
    }

    private void Mix(Span<float> destination, int frames, int channels, int sampleRate, bool interpolationEnabled)
    {
        destination.Clear();
        var master = Clamp(_module.Song.MasterVolume, 0, 64) / 64.0f;
        var volAdj = _module.Song.VolumeAdjust <= 0 ? 1.0f : _module.Song.VolumeAdjust / 100.0f;
        var normalization = _module.UsesMixingMode || _voices.Length > 4 ? 1.0f / Math.Max(1, _voices.Length / 2.0f) : 0.5f;

        for (var voiceIndex = 0; voiceIndex < _voices.Length; voiceIndex++)
        {
            var voice = _voices[voiceIndex];
            if (!voice.IsAudible)
            {
                continue;
            }

            var pan = voice.TrackPan;
            var leftGain = channels == 1 ? 1.0f : (1.0f - pan) * 0.5f;
            var rightGain = channels == 1 ? 1.0f : (1.0f + pan) * 0.5f;
            var volume = Clamp(voice.Volume + voice.TremoloVolume, 0, 64) / 64.0f;
            volume *= Clamp(voice.SynthVolume, 0, 64) / 64.0f;
            volume *= Clamp(voice.TrackVolume, 0, 64) / 64.0f;
            volume *= master * volAdj * normalization;
            var startDelayFrames = ConsumeStartDelayFrames(voice, frames, sampleRate);

            var step = MmdPeriodTables.GetSampleStep(voice.ArpeggioPeriod > 0 ? voice.ArpeggioPeriod : voice.Period, sampleRate);
            if (step <= 0.0)
            {
                continue;
            }

            for (var frame = 0; frame < frames; frame++)
            {
                if (frame < startDelayFrames)
                {
                    continue;
                }

                if (!voice.AdvanceLoopIfNeeded())
                {
                    break;
                }

                var sampleIndex = (int)voice.Position;
                var fraction = voice.Position - sampleIndex;
                var left = ReadSample(voice.LeftSamples, sampleIndex, fraction, interpolationEnabled);
                var right = voice.RightSamples.Length > 0
                    ? ReadSample(voice.RightSamples, sampleIndex, fraction, interpolationEnabled)
                    : left;

                if (channels == 1)
                {
                    destination[frame] += ((left + right) * 0.5f) * volume;
                }
                else
                {
                    var offset = frame * channels;
                    destination[offset] += left * volume * leftGain;
                    destination[offset + 1] += right * volume * rightGain;
                    for (var ch = 2; ch < channels; ch++)
                    {
                        destination[offset + ch] += ((left + right) * 0.5f) * volume;
                    }
                }

                voice.Position += step;
            }
        }

        for (var i = 0; i < frames * channels; i++)
        {
            destination[i] = Clamp(destination[i], -1.0f, 1.0f);
        }
    }

    private static int ConsumeStartDelayFrames(VoiceState voice, int frames, int sampleRate)
    {
        if (voice.PaulaStartDelaySeconds <= 0.0 || frames <= 0 || sampleRate <= 0)
        {
            return 0;
        }

        var delayFrames = Math.Min(frames, (int)Math.Ceiling(voice.PaulaStartDelaySeconds * sampleRate));
        voice.PaulaStartDelaySeconds = Math.Max(0.0, voice.PaulaStartDelaySeconds - (delayFrames / (double)sampleRate));
        return delayFrames;
    }

    private MmdTickTrace CaptureTrace(int frames, int sampleRate)
    {
        var block = CurrentBlock();
        var trace = new MmdTickTrace
        {
            SequenceIndex = _sequenceIndex,
            BlockIndex = block == null ? -1 : block.Index,
            Row = _row,
            Tick = _tick,
            Speed = _currentSpeed,
            Tempo = _currentTempo,
            TempoIsBpm = _tempoIsBpm,
            FrameCount = frames,
            SampleRate = sampleRate
        };

        for (var i = 0; i < _voices.Length; i++)
        {
            var voice = _voices[i];
            var effectivePeriod = voice.ArpeggioPeriod > 0 ? voice.ArpeggioPeriod : voice.Period;
            trace.Voices.Add(new MmdVoiceTrace
            {
                TrackIndex = voice.TrackIndex,
                Note = voice.Note,
                InstrumentNumber = voice.InstrumentNumber,
                PendingInstrumentNumber = voice.PendingInstrumentNumber,
                InstrumentKind = voice.Instrument == null ? MmdInstrumentKind.Empty : voice.Instrument.Kind,
                Period = voice.Period,
                BasePeriod = voice.BasePeriod,
                TargetPeriod = voice.TargetPeriod,
                ArpeggioPeriod = voice.ArpeggioPeriod,
                NormalizedNoteIndex = voice.NormalizedNoteIndex,
                PeriodTableIndex = voice.PeriodTableIndex,
                UsesExtendedPeriodTable = voice.UsesExtendedPeriodTable,
                SampleStep = MmdPeriodTables.GetSampleStep(effectivePeriod, sampleRate),
                Volume = voice.Volume,
                SynthVolume = voice.SynthVolume,
                TrackVolume = voice.TrackVolume,
                TremoloVolume = voice.TremoloVolume,
                TrackPan = voice.TrackPan,
                SampleLength = voice.SampleLength,
                SampleWindowOffset = voice.SampleWindowOffset,
                SamplePosition = voice.Position,
                PaulaInitialSampleOffset = voice.PaulaInitialSampleOffset,
                PaulaInitialSampleLength = voice.PaulaInitialSampleLength,
                PaulaReloadSampleOffset = voice.PaulaReloadSampleOffset,
                PaulaReloadSampleLength = voice.PaulaReloadSampleLength,
                PaulaReloadsSilence = voice.PaulaReloadsSilence,
                PaulaPointerUpdatedThisTick = voice.PaulaPointerUpdatedThisTick,
                PaulaStartDelayFrames = voice.PaulaStartDelaySeconds <= 0.0
                    ? 0
                    : Math.Min(frames, (int)Math.Ceiling(voice.PaulaStartDelaySeconds * sampleRate)),
                SynthPeriodChange = voice.Synth == null ? 0 : voice.Synth.PeriodChange,
                SynthPeriodChangeSpeed = voice.Synth == null ? 0 : voice.Synth.PeriodChangeSpeed,
                SynthVibratoDepth = voice.Synth == null ? 0 : voice.Synth.SynthVibratoDepth,
                SynthVibratoSpeed = voice.Synth == null ? 0 : voice.Synth.SynthVibratoSpeed,
                SynthArpeggioOffset = voice.Synth == null ? 0 : voice.Synth.SynthArpeggioOffset,
                SynthEnvelopeWaveformIndex = voice.Synth == null ? (int?)null : voice.Synth.EnvelopeWaveformIndex,
                SynthEnvelopePosition = voice.Synth == null ? 0 : voice.Synth.EnvelopePosition,
                SynthEnvelopeRestartEnabled = voice.Synth != null && voice.Synth.EnvelopeRestartEnabled,
                LoopStart = voice.LoopStart,
                LoopEnd = voice.LoopEnd,
                LoopEnabled = voice.LoopEnabled,
                Command = voice.Command,
                CommandData = voice.CommandData,
                HoldTicks = voice.HoldTicks,
                InitialHold = voice.InitialHold,
                FadeSpeed = voice.FadeSpeed,
                Releasing = voice.Releasing,
                SynthWaveformIndex = voice.Synth == null ? (int?)null : voice.Synth.CurrentWaveformIndex,
                IsAudible = voice.IsAudible
            });
        }

        return trace;
    }

    private static float ReadSample(float[] samples, int index, double fraction, bool interpolationEnabled)
    {
        if (samples.Length == 0)
        {
            return 0.0f;
        }

        if (index < 0)
        {
            index = 0;
        }

        if (index >= samples.Length - 1)
        {
            return samples[samples.Length - 1];
        }

        if (!interpolationEnabled)
        {
            return samples[index];
        }

        var current = samples[index];
        var next = samples[index + 1];
        return (float)(current + ((next - current) * fraction));
    }

    private void AdvanceTick(int frames, int sampleRate)
    {
        _playbackPosition += TimeSpan.FromSeconds((double)frames / sampleRate);
        AdvanceTickCounters();
    }

    private void AdvanceTickCounters()
    {
        _tick++;
        if (_tick >= _currentSpeed)
        {
            _tick = 0;
            AdvanceRow();
        }
    }

    private void AdvanceRow()
    {
        _row++;
        var block = CurrentBlock();
        if (block == null || _row >= block.LineCount)
        {
            _row = 0;
            _sequenceIndex++;
            if (_sequenceIndex >= _flatSequence.Length)
            {
                EndOrLoop();
            }
        }
    }

    private void EndOrLoop()
    {
        if (LoopingEnabled && _flatSequence.Length > 0)
        {
            _sequenceIndex = 0;
            _row = 0;
            _tick = 0;
            _loopsCompleted++;
            return;
        }

        _ended = true;
    }

    private void JumpToSequence(int sequence)
    {
        if (_flatSequence.Length == 0)
        {
            _ended = true;
            return;
        }

        _sequenceIndex = Clamp(sequence, 0, _flatSequence.Length - 1);
        _row = 0;
        _tick = 0;
    }

    private void BreakToRow(int row)
    {
        _sequenceIndex++;
        if (_sequenceIndex >= _flatSequence.Length)
        {
            EndOrLoop();
            return;
        }

        var block = CurrentBlock();
        _row = block == null ? 0 : Clamp(row, 0, Math.Max(0, block.LineCount - 1));
        _tick = 0;
    }

    private void SetSpeed(int data)
    {
        var speed = data & 0x1F;
        _currentSpeed = speed == 0 ? 0x20 : speed;
        if (_tick >= _currentSpeed)
        {
            _tick = Math.Max(0, _currentSpeed - 1);
        }
    }

    private void ApplyTempoCommand(int data)
    {
        if (data == 0)
        {
            BreakToRow(0);
            return;
        }

        if (data <= 0xF0)
        {
            _currentTempo = data;
            _tempoIsBpm = (_module.Song.Flags2 & MmdConstants.Flag2Bpm) != 0;
            return;
        }

        if (data == 0xF8)
        {
            _audioFilterEnabled = false;
            return;
        }

        if (data == 0xF9)
        {
            _audioFilterEnabled = true;
            return;
        }

        if (data == 0xFE)
        {
            _ended = true;
        }
    }

    private double TickSeconds()
    {
        if (_tempoIsBpm)
        {
            var beatLength = ((_module.Song.Flags2 & 0x1F) + 1);
            var latch = MmdConstants.PalBpmDiv / Math.Max(1.0, _currentTempo * beatLength);
        return latch / MmdConstants.PalCiaHz;
        }

        if (_currentTempo <= 10)
        {
        return GetStTempoLatch(_currentTempo) / MmdConstants.PalCiaHz;
        }

        return (MmdConstants.PalTimerDiv / Math.Max(1.0, _currentTempo)) / MmdConstants.PalCiaHz;
    }

    private static int GetStTempoLatch(int tempo)
    {
        switch (tempo)
        {
            case 1: return 2417;
            case 2: return 4833;
            case 3: return 7250;
            case 4: return 9666;
            case 5: return 12083;
            case 6: return 14500;
            case 7: return 16916;
            case 8: return 19332;
            case 9: return 21436;
            case 10: return 24163;
            default: return 14500;
        }
    }

    private TimeSpan EstimateDuration()
    {
        var savedLoop = LoopingEnabled;
        LoopingEnabled = false;
        Reset();

        var previous = new HashSet<string>(StringComparer.Ordinal);
        var safety = 0;
        while (!_ended && safety++ < MaxDurationSimulationTicks)
        {
            var key = _sequenceIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                      _row.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                      _tick.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                      _currentSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                      _currentTempo.ToString(System.Globalization.CultureInfo.InvariantCulture);

            if (!previous.Add(key) && _tick == 0)
            {
                break;
            }

            SimulateTick();
        }

        var duration = _playbackPosition;
        LoopingEnabled = savedLoop;
        return duration <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : duration;
    }

    private MmdBlock? CurrentBlock()
    {
        if (_sequenceIndex < 0 || _sequenceIndex >= _flatSequence.Length)
        {
            return null;
        }

        var blockIndex = _flatSequence[_sequenceIndex];
        return blockIndex >= 0 && blockIndex < _module.Blocks.Count ? _module.Blocks[blockIndex] : null;
    }

    private void ApplyTrackDefaults(VoiceState voice)
    {
        var track = voice.TrackIndex;
        var volume = track < _module.Song.TrackVolumes.Length ? _module.Song.TrackVolumes[track] : 64;
        voice.TrackVolume = Clamp(volume, 0, 64);

        if (track < _module.Song.TrackPans.Length)
        {
            voice.TrackPan = DecodePan((byte)_module.Song.TrackPans[track]);
        }
        else
        {
            voice.TrackPan = DefaultPan(track);
        }
    }

    private void UpdateVoiceStep(VoiceState voice)
    {
        if (voice.Period <= 0 && voice.Note > 0)
        {
            voice.Period = GetVoicePeriod(voice, voice.Note);
        }
    }

    private static int NormalizeNote(int note)
    {
        return note <= 0 ? 0 : Clamp(note, 1, 96);
    }

    private static int NormalizeCommand(int command)
    {
        return command & 0x1F;
    }

    private static int DecodeBcdRow(int data)
    {
        var tens = (data >> 4) & 0x0F;
        var ones = data & 0x0F;
        return (tens * 10) + ones;
    }

    private static float DecodePan(byte pan)
    {
        unchecked
        {
            return Clamp((sbyte)pan / 16.0f, -1.0f, 1.0f);
        }
    }

    private static float DefaultPan(int track)
    {
        switch (track & 3)
        {
            case 0:
            case 3:
                return -1.0f;
            default:
                return 1.0f;
        }
    }

    private static int NormalizeSampleRate(int sampleRate)
    {
        return sampleRate > 0 ? sampleRate : DefaultSampleRate;
    }

    private static int NormalizeChannels(int channels)
    {
        return channels <= 0 ? DefaultChannels : channels;
    }

    private static int[] BuildFlatSequence(MmdModule module)
    {
        var result = new List<int>();
        if (module.Song.SectionTable.Count > 0 && module.Song.PlaySequences.Count > 0)
        {
            for (var sectionIndex = 0; sectionIndex < module.Song.SectionTable.Count; sectionIndex++)
            {
                var sequenceIndex = Clamp(module.Song.SectionTable[sectionIndex], 0, module.Song.PlaySequences.Count - 1);
                if (sequenceIndex >= 0 && sequenceIndex < module.Song.PlaySequences.Count)
                {
                    AddPlaySequence(result, module.Song.PlaySequences[sequenceIndex]);
                }
            }
        }

        if (result.Count == 0 && module.Song.PlaySequences.Count > 0)
        {
            for (var i = 0; i < module.Song.PlaySequences.Count; i++)
            {
                AddPlaySequence(result, module.Song.PlaySequences[i]);
            }
        }

        if (result.Count == 0)
        {
            var count = Math.Min(module.Song.SongLength, module.Song.LegacyPlaySequence.Length);
            for (var i = 0; i < count; i++)
            {
                var block = module.Song.LegacyPlaySequence[i];
                if (block >= 0 && block < module.Blocks.Count)
                {
                    result.Add(block);
                }
            }
        }

        return result.ToArray();
    }

    private static void AddPlaySequence(List<int> result, MmdPlaySequence sequence)
    {
        for (var i = 0; i < sequence.Blocks.Count; i++)
        {
            var block = sequence.Blocks[i];
            if (block >= 0)
            {
                result.Add(block);
            }
        }
    }

    private void AddDiagnostics()
    {
        for (var i = 0; i < _module.Diagnostics.Count; i++)
        {
            _diagnostics.Add(_module.Diagnostics[i]);
        }

        if (UsesMidi())
        {
            _diagnostics.Add(new ModuleDiagnostic(ModuleDiagnosticSeverity.Warning, "MIDI instruments and commands are parsed but not rendered.", "MED_MIDI"));
        }

        if (UsesNonPaulaOutput())
        {
            _diagnostics.Add(new ModuleDiagnostic(ModuleDiagnosticSeverity.Warning, "Non-Paula instrument output devices are parsed but not rendered.", "MED_OUTPUT_DEVICE"));
        }

        if (_module.Song.MixEchoType != 0 || _module.Song.MixEchoDepth != 0 || _module.Song.MixEchoLength != 0)
        {
            _diagnostics.Add(new ModuleDiagnostic(ModuleDiagnosticSeverity.Warning, "Soundstudio echo settings are parsed but omitted from rendering.", "MED_ECHO"));
        }
    }

    private bool UsesMidi()
    {
        for (var i = 0; i < _module.Song.SampleInfos.Length; i++)
        {
            if (_module.Song.SampleInfos[i].MidiChannel != 0 || _module.Song.SampleInfos[i].MidiPreset != 0)
            {
                return true;
            }
        }

        return false;
    }

    private bool UsesNonPaulaOutput()
    {
        for (var i = 0; i < _module.Song.SampleInfos.Length; i++)
        {
            if (_module.Song.SampleInfos[i].Extension.OutputDevice != MmdConstants.OutputStd)
            {
                return true;
            }
        }

        return false;
    }

    private void EnsureNotDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MmdSong));
        }
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

    private static float Clamp(float value, float min, float max)
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

    private sealed class VoiceState
    {
        public VoiceState(int trackIndex)
        {
            TrackIndex = trackIndex;
        }

        public int TrackIndex { get; }

        public int Note { get; set; }
        public int InstrumentNumber { get; set; }
        public int PendingInstrumentNumber { get; set; }
        public MmdInstrument? Instrument { get; set; }
        public float[] LeftSamples { get; set; } = Array.Empty<float>();
        public float[] RightSamples { get; set; } = Array.Empty<float>();
        public int SampleLength { get; set; }
        public double Position { get; set; }
        public int LoopStart { get; set; }
        public int LoopEnd { get; set; }
        public bool LoopEnabled { get; set; }
        public int Volume { get; set; } = 64;
        public int SynthVolume { get; set; } = 64;
        public int TrackVolume { get; set; } = 64;
        public float TrackPan { get; set; }
        public sbyte FineTune { get; set; }
        public sbyte Transpose { get; set; }
        public int Period { get; set; }
        public int BasePeriod { get; set; }
        public int TargetPeriod { get; set; }
        public int NormalizedNoteIndex { get; set; }
        public int PeriodTableIndex { get; set; }
        public bool UsesExtendedPeriodTable { get; set; }
        public int PortamentoSpeed { get; set; }
        public int Arpeggio { get; set; }
        public int ArpeggioPeriod { get; set; }
        public int Command { get; set; }
        public int CommandData { get; set; }
        public int VolumeSlideMemory { get; set; }
        public int VibratoPhase { get; set; }
        public int VibratoSpeed { get; set; }
        public int VibratoDepth { get; set; }
        public int TremoloPhase { get; set; }
        public int TremoloSpeed { get; set; }
        public int TremoloDepth { get; set; }
        public int TremoloVolume { get; set; }
        public int SampleOffset { get; set; }
        public int SampleWindowOffset { get; set; }
        public int PaulaInitialSampleOffset { get; set; }
        public int PaulaInitialSampleLength { get; set; }
        public int PaulaReloadSampleOffset { get; set; }
        public int PaulaReloadSampleLength { get; set; }
        public bool PaulaReloadsSilence { get; set; }
        public bool PaulaPointerUpdatedThisTick { get; set; }
        public double PaulaStartDelaySeconds { get; set; }
        public float[] PendingReloadLeftSamples { get; set; } = Array.Empty<float>();
        public float[] PendingReloadRightSamples { get; set; } = Array.Empty<float>();
        public int PendingReloadSampleLength { get; set; }
        public int PendingReloadLoopStart { get; set; }
        public int PendingReloadLoopEnd { get; set; }
        public bool PendingReloadLoopEnabled { get; set; }
        public int NoteDelayTicks { get; set; }
        public int DelayedNote { get; set; }
        public int HoldTicks { get; set; }
        public int RowDelay { get; set; }
        public int InitialHold { get; set; }
        public int InitialDecay { get; set; }
        public int FadeSpeed { get; set; }
        public bool Releasing { get; set; }
        public int SynthDmaType { get; set; }
        public SynthRuntime? Synth { get; set; }

        public bool IsAudible => SampleLength > 0 && Period > 0 && Volume > 0 && SynthVolume > 0 && LeftSamples.Length > 0;

        public void Reset()
        {
            Note = 0;
            InstrumentNumber = 0;
            PendingInstrumentNumber = 0;
            Instrument = null;
            LeftSamples = Array.Empty<float>();
            RightSamples = Array.Empty<float>();
            SampleLength = 0;
            Position = 0.0;
            LoopStart = 0;
            LoopEnd = 0;
            LoopEnabled = false;
            Volume = 64;
            SynthVolume = 64;
            FineTune = 0;
            Transpose = 0;
            Period = 0;
            BasePeriod = 0;
            TargetPeriod = 0;
            NormalizedNoteIndex = 0;
            PeriodTableIndex = 0;
            UsesExtendedPeriodTable = false;
            PortamentoSpeed = 0;
            Arpeggio = 0;
            ArpeggioPeriod = 0;
            Command = NoCommand;
            CommandData = 0;
            VolumeSlideMemory = 0;
            VibratoPhase = 0;
            VibratoSpeed = 0;
            VibratoDepth = 0;
            TremoloPhase = 0;
            TremoloSpeed = 0;
            TremoloDepth = 0;
            TremoloVolume = 0;
            SampleOffset = 0;
            SampleWindowOffset = 0;
            PaulaInitialSampleOffset = 0;
            PaulaInitialSampleLength = 0;
            PaulaReloadSampleOffset = -1;
            PaulaReloadSampleLength = 0;
            PaulaReloadsSilence = false;
            PaulaPointerUpdatedThisTick = false;
            PaulaStartDelaySeconds = 0.0;
            PendingReloadLeftSamples = Array.Empty<float>();
            PendingReloadRightSamples = Array.Empty<float>();
            PendingReloadSampleLength = 0;
            PendingReloadLoopStart = 0;
            PendingReloadLoopEnd = 0;
            PendingReloadLoopEnabled = false;
            NoteDelayTicks = 0;
            DelayedNote = 0;
            HoldTicks = -1;
            RowDelay = 0;
            InitialHold = 0;
            InitialDecay = 0;
            FadeSpeed = 0;
            Releasing = false;
            SynthDmaType = 0;
            Synth = null;
        }

        public bool AdvanceLoopIfNeeded()
        {
            if (SampleLength <= 0)
            {
                return false;
            }

            if (!LoopEnabled)
            {
                return Position < SampleLength;
            }

            while (true)
            {
                var loopStart = Clamp(LoopStart, 0, SampleLength - 1);
                var loopEnd = Clamp(LoopEnd <= loopStart ? SampleLength : LoopEnd, loopStart + 1, SampleLength);
                var loopLength = loopEnd - loopStart;
                if (loopLength <= 0)
                {
                    return false;
                }

                if (Position < loopEnd)
                {
                    return true;
                }

                Position -= loopLength;
                ApplyPendingReloadSamples();
                if (!LoopEnabled)
                {
                    return Position < SampleLength;
                }
            }
        }

        private void ApplyPendingReloadSamples()
        {
            if (PendingReloadLeftSamples.Length == 0)
            {
                return;
            }

            LeftSamples = PendingReloadLeftSamples;
            RightSamples = PendingReloadRightSamples;
            SampleLength = PendingReloadSampleLength;
            LoopStart = PendingReloadLoopStart;
            LoopEnd = PendingReloadLoopEnd;
            LoopEnabled = PendingReloadLoopEnabled;
            SampleWindowOffset = 0;
            PendingReloadLeftSamples = Array.Empty<float>();
            PendingReloadRightSamples = Array.Empty<float>();
            PendingReloadSampleLength = 0;
            PendingReloadLoopStart = 0;
            PendingReloadLoopEnd = 0;
            PendingReloadLoopEnabled = false;
        }
    }

    private sealed class SynthRuntime
    {
        private static readonly int[] SineTable =
        {
            0, 25, 49, 71, 90, 106, 117, 125,
            127, 125, 117, 106, 90, 71, 49, 25,
            0, -25, -49, -71, -90, -106, -117, -125,
            -127, -125, -117, -106, -90, -71, -49, -25
        };

        private readonly MmdInstrument _instrument;
        private int _volumePosition;
        private int _waveformPosition;
        private int _volumeCounter;
        private int _waveformCounter;
        private int _volumeWait;
        private int _waveformWait;
        private int _volumeSpeed = 1;
        private int _waveformSpeed = 1;
        private int _volumeChange;
        private int _periodChange;
        private int _periodChangeSpeed;
        private int _synthVibratoDepth;
        private int _synthVibratoSpeed;
        private int _synthVibratoOffset;
        private int? _synthVibratoWaveformIndex;
        private int _envelopeWaveformIndex = -1;
        private int? _envelopeRestartWaveformIndex;
        private int _envelopePosition;
        private int _envelopeCount;
        private int _arpeggioStart = -1;
        private int _arpeggioPosition = -1;
        private int _synthArpeggioOffset;

        private SynthRuntime(MmdInstrument instrument)
        {
            _instrument = instrument;
            _volumeSpeed = Math.Max(1, (int)instrument.VolumeSpeed);
            _waveformSpeed = Math.Max(1, (int)instrument.WaveformSpeed);
            _volumeCounter = 0;
            _waveformCounter = 0;
        }

        public int CurrentWaveformIndex { get; private set; }

        public int PeriodChange => _periodChange;

        public int PeriodChangeSpeed => _periodChangeSpeed;

        public int SynthVibratoDepth => _synthVibratoDepth;

        public int SynthVibratoSpeed => _synthVibratoSpeed;

        public int SynthArpeggioOffset => _synthArpeggioOffset;

        public int? EnvelopeWaveformIndex => _envelopeWaveformIndex >= 0 ? _envelopeWaveformIndex : (int?)null;

        public int EnvelopePosition => _envelopePosition;

        public bool EnvelopeRestartEnabled => _envelopeRestartWaveformIndex.HasValue;

        public static SynthRuntime FromInstrument(MmdInstrument instrument)
        {
            return new SynthRuntime(instrument);
        }

        public void StartDecay(int decayPosition)
        {
            if (_instrument.VolumeSequence.Length == 0)
            {
                return;
            }

            _volumePosition = Clamp(decayPosition, 0, _instrument.VolumeSequence.Length - 1);
            _volumeWait = 0;
            _volumeCounter = 0;
        }

        public MmdSynthWaveform? CurrentWaveform()
        {
            if (_instrument.Waveforms.Count == 0)
            {
                return null;
            }

            var index = Clamp(CurrentWaveformIndex, 0, _instrument.Waveforms.Count - 1);
            return _instrument.Waveforms[index];
        }

        public bool Advance(VoiceState voice)
        {
            var waveformChanged = false;

            _volumeCounter--;
            if (_volumeCounter <= 0)
            {
                _volumeCounter = Math.Max(1, _volumeSpeed);
                if (_volumeChange != 0)
                {
                    voice.SynthVolume = Clamp(voice.SynthVolume + _volumeChange, 0, 64);
                }

                ApplyVolumeEnvelope(voice);
                ProcessVolumeSequence(voice);
            }

            _waveformCounter--;
            if (_waveformCounter <= 0)
            {
                _waveformCounter = Math.Max(1, _waveformSpeed);
                if (_periodChangeSpeed != 0)
                {
                    _periodChange += _periodChangeSpeed;
                }

                if (_waveformWait > 0)
                {
                    _waveformWait--;
                    if (_waveformWait <= 0)
                    {
                        waveformChanged = ProcessWaveformSequence(voice);
                    }
                }
                else
                {
                    waveformChanged = ProcessWaveformSequence(voice);
                }
            }

            ApplySynthPeriod(voice);
            return waveformChanged;
        }

        private void ProcessVolumeSequence(VoiceState voice)
        {
            var safety = 0;
            while (_instrument.VolumeSequence.Length > 0 && safety++ < 8)
            {
                if (_volumeWait > 0)
                {
                    _volumeWait--;
                    if (_volumeWait > 0)
                    {
                        return;
                    }
                }

                if (_volumePosition < 0 || _volumePosition >= _instrument.VolumeSequence.Length)
                {
                    _volumePosition = 0;
                }

                var value = _instrument.VolumeSequence[_volumePosition++];
                if (value < 0x80)
                {
                    voice.SynthVolume = Clamp(value, 0, 64);
                    return;
                }

                var command = value & 0xFF;
                switch (command)
                {
                    case 0xF0:
                    {
                        var arg = ReadSequenceArgument(_instrument.VolumeSequence, ref _volumePosition);
                        _volumeSpeed = Math.Max(1, arg);
                        break;
                    }

                    case 0xF1:
                    {
                        var arg = ReadSequenceArgument(_instrument.VolumeSequence, ref _volumePosition);
                        _volumeWait = Math.Max(1, arg);
                        return;
                    }

                    case 0xF2:
                    {
                        var arg = ReadSequenceArgument(_instrument.VolumeSequence, ref _volumePosition);
                        _volumeChange = -Math.Abs(arg);
                        break;
                    }

                    case 0xF3:
                    {
                        var arg = ReadSequenceArgument(_instrument.VolumeSequence, ref _volumePosition);
                        _volumeChange = Math.Abs(arg);
                        break;
                    }

                    case 0xF4:
                    {
                        var arg = ReadSequenceArgument(_instrument.VolumeSequence, ref _volumePosition);
                        StartVolumeEnvelope(arg, restart: false);
                        break;
                    }

                    case 0xF5:
                    {
                        var arg = ReadSequenceArgument(_instrument.VolumeSequence, ref _volumePosition);
                        StartVolumeEnvelope(arg, restart: true);
                        break;
                    }

                    case 0xF6:
                        StopVolumeEnvelope();
                        break;

                    case 0xFA:
                    {
                        var arg = ReadSequenceArgument(_instrument.VolumeSequence, ref _volumePosition);
                        _waveformPosition = Clamp(arg, 0, Math.Max(0, _instrument.WaveformSequence.Length - 1));
                        _waveformWait = 0;
                        break;
                    }

                    case 0xFE:
                    {
                        var arg = ReadSequenceArgument(_instrument.VolumeSequence, ref _volumePosition);
                        _volumePosition = Clamp(arg, 0, Math.Max(0, _instrument.VolumeSequence.Length - 1));
                        break;
                    }

                    case 0xFB:
                    case 0xFF:
                        _volumePosition = Math.Max(0, _volumePosition - 1);
                        return;

                    default:
                        return;
                }
            }
        }

        private bool ProcessWaveformSequence(VoiceState voice)
        {
            var waveformChanged = false;
            var safety = 0;
            while (_instrument.WaveformSequence.Length > 0 && safety++ < 8)
            {
                if (_waveformPosition < 0 || _waveformPosition >= _instrument.WaveformSequence.Length)
                {
                    _waveformPosition = 0;
                }

                var value = _instrument.WaveformSequence[_waveformPosition++];
                if (value < 0x80)
                {
                    CurrentWaveformIndex = Clamp(value, 0, Math.Max(0, _instrument.Waveforms.Count - 1));
                    waveformChanged = true;
                    return waveformChanged;
                }

                var command = value & 0xFF;
                switch (command)
                {
                    case 0xF0:
                    {
                        var arg = ReadSequenceArgument(_instrument.WaveformSequence, ref _waveformPosition);
                        _waveformSpeed = Math.Max(1, arg);
                        break;
                    }

                    case 0xF1:
                    {
                        var arg = ReadSequenceArgument(_instrument.WaveformSequence, ref _waveformPosition);
                        _waveformWait = Math.Max(1, arg);
                        return waveformChanged;
                    }

                    case 0xF2:
                    {
                        var arg = ReadSequenceArgument(_instrument.WaveformSequence, ref _waveformPosition);
                        _periodChangeSpeed = Math.Abs(arg);
                        break;
                    }

                    case 0xF3:
                    {
                        var arg = ReadSequenceArgument(_instrument.WaveformSequence, ref _waveformPosition);
                        _periodChangeSpeed = -Math.Abs(arg);
                        break;
                    }

                    case 0xF4:
                    {
                        var arg = ReadSequenceArgument(_instrument.WaveformSequence, ref _waveformPosition);
                        _synthVibratoDepth = arg;
                        break;
                    }

                    case 0xF5:
                    {
                        var arg = ReadSequenceArgument(_instrument.WaveformSequence, ref _waveformPosition);
                        _synthVibratoSpeed = arg + 1;
                        break;
                    }

                    case 0xF6:
                        _periodChange = 0;
                        voice.Period = voice.BasePeriod;
                        break;

                    case 0xF7:
                    {
                        var arg = ReadSequenceArgument(_instrument.WaveformSequence, ref _waveformPosition);
                        _synthVibratoWaveformIndex = Clamp(arg, 0, Math.Max(0, _instrument.Waveforms.Count - 1));
                        break;
                    }

                    case 0xFA:
                    {
                        var arg = ReadSequenceArgument(_instrument.WaveformSequence, ref _waveformPosition);
                        _volumePosition = Clamp(arg, 0, Math.Max(0, _instrument.VolumeSequence.Length - 1));
                        _volumeWait = 0;
                        break;
                    }

                    case 0xFC:
                        StartSynthArpeggio(_waveformPosition);
                        while (_waveformPosition < _instrument.WaveformSequence.Length &&
                               _instrument.WaveformSequence[_waveformPosition] < 0x80)
                        {
                            _waveformPosition++;
                        }

                        break;

                    case 0xFD:
                        break;

                    case 0xFE:
                    {
                        var arg = ReadSequenceArgument(_instrument.WaveformSequence, ref _waveformPosition);
                        _waveformPosition = Clamp(arg, 0, Math.Max(0, _instrument.WaveformSequence.Length - 1));
                        break;
                    }

                    case 0xFB:
                    case 0xFF:
                        _waveformPosition = Math.Max(0, _waveformPosition - 1);
                        return waveformChanged;

                    default:
                        return waveformChanged;
                }
            }

            return waveformChanged;
        }

        private void ApplySynthPeriod(VoiceState voice)
        {
            var period = ApplySynthArpeggio(voice);
            if (period <= 0)
            {
                period = voice.BasePeriod;
            }

            if (_synthVibratoDepth != 0)
            {
                var value = GetSynthVibratoValue();
                period += (value * _synthVibratoDepth) >> 8;
                _synthVibratoOffset = (_synthVibratoOffset + _synthVibratoSpeed) & 0xFFFF;
            }

            period += _periodChange;
            voice.Period = Clamp(period, 113, 32767);
        }

        private void ApplyVolumeEnvelope(VoiceState voice)
        {
            if (_envelopeWaveformIndex < 0 || _envelopeWaveformIndex >= _instrument.Waveforms.Count)
            {
                return;
            }

            var samples = _instrument.Waveforms[_envelopeWaveformIndex].EnvelopeSamples;
            if (samples.Length == 0)
            {
                StopVolumeEnvelope();
                return;
            }

            if (_envelopePosition >= samples.Length)
            {
                if (!_envelopeRestartWaveformIndex.HasValue)
                {
                    StopVolumeEnvelope();
                    return;
                }

                _envelopeWaveformIndex = Clamp(_envelopeRestartWaveformIndex.Value, 0, _instrument.Waveforms.Count - 1);
                _envelopePosition = 0;
                samples = _instrument.Waveforms[_envelopeWaveformIndex].EnvelopeSamples;
                if (samples.Length == 0)
                {
                    StopVolumeEnvelope();
                    return;
                }
            }

            var sample = samples[_envelopePosition++];
            voice.SynthVolume = (sample + 128) >> 2;
            _envelopeCount++;
            if (_envelopeCount < 128)
            {
                return;
            }

            _envelopeCount = 0;
            if (_envelopeRestartWaveformIndex.HasValue)
            {
                _envelopeWaveformIndex = Clamp(_envelopeRestartWaveformIndex.Value, 0, _instrument.Waveforms.Count - 1);
                _envelopePosition = 0;
            }
            else
            {
                StopVolumeEnvelope();
            }
        }

        private void StartVolumeEnvelope(int waveformIndex, bool restart)
        {
            if (_instrument.Waveforms.Count == 0)
            {
                StopVolumeEnvelope();
                return;
            }

            _envelopeWaveformIndex = Clamp(waveformIndex, 0, _instrument.Waveforms.Count - 1);
            _envelopeRestartWaveformIndex = restart ? _envelopeWaveformIndex : (int?)null;
            _envelopePosition = 0;
            _envelopeCount = 0;
        }

        private void StopVolumeEnvelope()
        {
            _envelopeWaveformIndex = -1;
            _envelopeRestartWaveformIndex = null;
            _envelopePosition = 0;
            _envelopeCount = 0;
        }

        private void StartSynthArpeggio(int startPosition)
        {
            if (startPosition < 0 || startPosition >= _instrument.WaveformSequence.Length)
            {
                _arpeggioStart = -1;
                _arpeggioPosition = -1;
                _synthArpeggioOffset = 0;
                return;
            }

            _arpeggioStart = startPosition;
            _arpeggioPosition = startPosition;
            _synthArpeggioOffset = 0;
        }

        private int ApplySynthArpeggio(VoiceState voice)
        {
            _synthArpeggioOffset = 0;
            if (_arpeggioPosition < 0 || _arpeggioStart < 0 || _instrument.WaveformSequence.Length == 0)
            {
                return 0;
            }

            if (_arpeggioPosition >= _instrument.WaveformSequence.Length ||
                _instrument.WaveformSequence[_arpeggioPosition] >= 0x80)
            {
                _arpeggioPosition = _arpeggioStart;
            }

            if (_arpeggioPosition < 0 ||
                _arpeggioPosition >= _instrument.WaveformSequence.Length ||
                _instrument.WaveformSequence[_arpeggioPosition] >= 0x80)
            {
                return 0;
            }

            var offset = _instrument.WaveformSequence[_arpeggioPosition];
            _synthArpeggioOffset = offset;
            var next = _arpeggioPosition + 1;
            _arpeggioPosition = next >= _instrument.WaveformSequence.Length ||
                                _instrument.WaveformSequence[next] >= 0x80
                ? _arpeggioStart
                : next;

            if (voice.Note <= 0)
            {
                return 0;
            }

            return MmdPeriodTables.GetPeriodByIndex(
                voice.PeriodTableIndex + offset,
                voice.FineTune,
                voice.UsesExtendedPeriodTable);
        }

        private int GetSynthVibratoValue()
        {
            var index = (_synthVibratoOffset >> 4) & 0x1F;
            if (_synthVibratoWaveformIndex.HasValue && _synthVibratoWaveformIndex.Value < _instrument.Waveforms.Count)
            {
                var wave = _instrument.Waveforms[_synthVibratoWaveformIndex.Value].Samples;
                if (wave.Length > 0)
                {
                    return Clamp((int)Math.Round(wave[index % wave.Length] * 128.0f), -128, 127);
                }
            }

            return SineTable[index];
        }

        private static int ReadSequenceArgument(byte[] sequence, ref int position)
        {
            if (position >= sequence.Length)
            {
                return 0;
            }

            return sequence[position++];
        }
    }
}
}
