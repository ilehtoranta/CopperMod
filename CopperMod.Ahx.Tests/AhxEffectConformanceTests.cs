namespace CopperMod.Ahx.Tests;

public sealed class AhxEffectConformanceTests
{
    [Fact]
    public void DocumentedTrackAndPerformanceEffectMatrixExecutesOnOriginalPlayer()
    {
        var fixture = FindFixtureInputs();
        if (fixture is null)
        {
            AhxTestInputs.ReportMissing("the reference binary, base song, or WinAHX corpus is unavailable.");
            return;
        }

        var player = File.ReadAllBytes(fixture.Value.Player);
        var baseModule = File.ReadAllBytes(fixture.Value.BaseModule);
        using var machine = new Ahx68kReferenceMachine(
            player,
            baseModule,
            fixture.Value.ModuleCapacity,
            enableTracing: true);
        machine.InitializeHost();
        machine.ClearTrace();

        var trackCases = new (string Name, int Command, int Parameter, int? SecondCommand, int SecondParameter)[]
        {
            ("0+B position jump above 99", 0x0, 0x01, 0xB, 0x23),
            ("1 portamento up", 0x1, 0x03, null, 0),
            ("2 portamento down", 0x2, 0x03, null, 0),
            ("3 tone portamento", 0x3, 0x05, null, 0),
            ("4 immediate neutral filter", 0x4, 0x60, null, 0),
            ("5 tone portamento plus volume slide", 0x5, 0x03, null, 0),
            ("8 external timing", 0x8, 0x27, null, 0),
            ("9 square relation", 0x9, 0x20, null, 0),
            ("A volume slide", 0xA, 0x03, null, 0),
            ("B position jump", 0xB, 0x00, null, 0),
            ("C note volume", 0xC, 0x20, null, 0),
            ("C instrument master volume", 0xC, 0x60, null, 0),
            ("C channel master volume", 0xC, 0xB0, null, 0),
            ("D decimal position break", 0xD, 0x32, null, 0),
            ("E1 fine pitch bend up", 0xE, 0x11, null, 0),
            ("E2 fine pitch bend down", 0xE, 0x26, null, 0),
            ("E4 vibrato override", 0xE, 0x48, null, 0),
            ("EA fine volume slide up", 0xE, 0xA3, null, 0),
            ("EB fine volume slide down", 0xE, 0xB6, null, 0),
            ("EC note cut", 0xE, 0xC3, null, 0),
            ("ED note delay", 0xE, 0xD3, null, 0),
            ("F speed", 0xF, 0x06, null, 0),
            ("F zero speed stop", 0xF, 0x00, null, 0)
        };

        foreach (var effect in trackCases)
        {
            var module = PatchTrackEffect(baseModule, effect.Command, effect.Parameter, effect.SecondCommand, effect.SecondParameter);
            var traceStart = PrepareEvidence(machine, module, effect.SecondCommand.HasValue ? 16 : 8);
            AssertRoutineAndAudioEvidence(machine, traceStart, effect.Name, effect.Parameter == 0 && effect.Command == 0xF);
            if (effect.Command == 0x8)
            {
                Assert.Equal(0x27, machine.ReadPublicByte(0));
            }

        }

        var performanceCases = new (string Name, int Effect, int Parameter)[]
        {
            ("0 low-pass filter initialization", 0, 0x01),
            ("0 neutral filter initialization", 0, 0x20),
            ("0 high-pass filter initialization", 0, 0x3F),
            ("1 performance portamento up", 1, 0x03),
            ("2 performance portamento down", 2, 0x03),
            ("3 square relation", 3, 0x20),
            ("4 square positive modulation", 4, 0x01),
            ("4 square negative modulation", 4, 0x0F),
            ("4 filter positive modulation", 4, 0x11),
            ("4 filter and square negative modulation", 4, 0xFF),
            ("5 performance-list jump", 5, 0x00),
            ("6 note volume", 6, 0x20),
            ("6 instrument master volume", 6, 0x60),
            ("6 channel master volume", 6, 0xB0),
            ("7 performance speed", 7, 0x02)
        };

        foreach (var effect in performanceCases)
        {
            var module = PatchPerformanceEffect(baseModule, effect.Effect, effect.Parameter, waveform: 1, fixedNote: false);
            var traceStart = PrepareEvidence(machine, module, 12);
            AssertRoutineAndAudioEvidence(machine, traceStart, effect.Name, allowNoAudioWrites: false);
        }

        foreach (var fixedNote in new[] { false, true })
        {
            var module = PatchPerformanceEffect(baseModule, 7, 1, waveform: 1, fixedNote);
            var traceStart = PrepareEvidence(machine, module, 8);
            AssertRoutineAndAudioEvidence(machine, traceStart, fixedNote ? "fixed performance note" : "relative performance note", false);
        }

        for (var waveform = 1; waveform <= 4; waveform++)
        {
            for (var wavelength = 0; wavelength <= 5; wavelength++)
            {
                var module = PatchWaveform(baseModule, waveform, wavelength);
                var traceStart = PrepareEvidence(machine, module, 8);
                AssertRoutineAndAudioEvidence(machine, traceStart, $"waveform {waveform}, wavelength {wavelength}", false);
            }
        }

        foreach (var envelope in new[]
                 {
                     (Name: "attack boundary", Attack: 1, Decay: 1, Sustain: 1, Release: 1),
                     (Name: "sustain boundary", Attack: 1, Decay: 1, Sustain: 8, Release: 1),
                     (Name: "release boundary", Attack: 1, Decay: 1, Sustain: 1, Release: 8)
                 })
        {
            var module = PatchEnvelope(baseModule, envelope.Attack, envelope.Decay, envelope.Sustain, envelope.Release);
            var traceStart = PrepareEvidence(machine, module, 16);
            AssertRoutineAndAudioEvidence(machine, traceStart, envelope.Name, false);
        }

        foreach (var hardCut in new[]
                 {
                     (Name: "hard cut", Frames: 3, Release: false),
                     (Name: "hard-cut release", Frames: 3, Release: true)
                 })
        {
            var module = PatchHardCut(baseModule, hardCut.Frames, hardCut.Release);
            var traceStart = PrepareEvidence(machine, module, 12);
            AssertRoutineAndAudioEvidence(machine, traceStart, hardCut.Name, false);
        }

        foreach (var vibrato in new[]
                 {
                     (Name: "vibrato delayed", Delay: 4, Depth: 3, Speed: 2),
                     (Name: "vibrato immediate maximum depth", Delay: 0, Depth: 15, Speed: 8)
                 })
        {
            var module = PatchVibrato(baseModule, vibrato.Delay, vibrato.Depth, vibrato.Speed);
            var traceStart = PrepareEvidence(machine, module, 12);
            AssertRoutineAndAudioEvidence(machine, traceStart, vibrato.Name, false);
        }

        foreach (var modulation in new[]
                 {
                     (Name: "square modulation bounds", Filter: false),
                     (Name: "filter modulation bounds", Filter: true)
                 })
        {
            var module = PatchModulationBounds(baseModule, modulation.Filter);
            var traceStart = PrepareEvidence(machine, module, 12);
            AssertRoutineAndAudioEvidence(machine, traceStart, modulation.Name, false);
        }

        foreach (var multiplier in Enumerable.Range(1, 4))
        {
            var module = PatchSpeedMultiplier(baseModule, multiplier);
            machine.TraceEnabled = false;
            machine.LoadModule(module);
            Assert.Equal(28_419 / (2 * multiplier), machine.TempoWord);
            RunInterrupts(machine, 4);
        }
    }

    [Fact]
    public void LocalCorpusContainsAhx0Ahx1SubsongAndMultipleSpeedCoverage()
    {
        var fixture = FindFixtureInputs();
        if (fixture is null)
        {
            AhxTestInputs.ReportMissing("the reference binary, base song, or WinAHX corpus is unavailable.");
            return;
        }

        var songs = Directory.GetFiles(fixture.Value.Corpus, "*.ahx", SearchOption.AllDirectories);
        var modules = songs.Select(File.ReadAllBytes).Select(data => AhxParser.Parse(data)).ToArray();

        Assert.Contains(modules, module => module.Version == 0);
        Assert.Contains(modules, module => module.Version == 1);
        Assert.Contains(modules, module => module.SpeedMultiplier == 2);
        Assert.Contains(modules, module => module.SpeedMultiplier == 3);
        Assert.Contains(songs, path => File.ReadAllBytes(path)[13] > 0);
        Assert.NotEmpty(modules.SelectMany(module => module.Tracks).SelectMany(track => track).Where(step => step.Command != 0));
        Assert.NotEmpty(modules.SelectMany(module => module.Instruments).SelectMany(instrument => instrument.Playlist));

        var player = File.ReadAllBytes(fixture.Value.Player);
        var largest = songs.Select(File.ReadAllBytes).OrderByDescending(data => data.Length).First();
        using var machine = new Ahx68kReferenceMachine(player, largest, fixture.Value.ModuleCapacity);
        machine.TraceEnabled = false;
        machine.InitializeHost();
        var smokeCases = new[]
        {
            (Data: File.ReadAllBytes(songs.First(path => File.ReadAllBytes(path)[3] == 0)), SubSong: 0),
            (Data: File.ReadAllBytes(songs.First(path => File.ReadAllBytes(path)[3] == 1)), SubSong: 0),
            (Data: File.ReadAllBytes(songs.First(path => File.ReadAllBytes(path)[13] > 0)), SubSong: 1),
            (Data: File.ReadAllBytes(songs.First(path => (((File.ReadAllBytes(path)[6] >> 5) & 3) + 1) == 2)), SubSong: 0),
            (Data: File.ReadAllBytes(songs.First(path => (((File.ReadAllBytes(path)[6] >> 5) & 3) + 1) == 3)), SubSong: 0)
        };
        foreach (var smoke in smokeCases)
        {
            machine.LoadModule(smoke.Data, smoke.SubSong);
            machine.Interrupt();
            machine.ValidateGuards();
        }

        var ahx0 = smokeCases[0].Data;
        var ahx1 = smokeCases[1].Data;
        foreach (var renderCase in new[]
                 {
                     (Name: "AHX0 44.1 kHz mono", Data: ahx0, Rate: 44_100, Channels: 1),
                     (Name: "AHX0 48 kHz stereo", Data: ahx0, Rate: 48_000, Channels: 2),
                     (Name: "AHX1 44.1 kHz stereo", Data: ahx1, Rate: 44_100, Channels: 2),
                     (Name: "AHX1 48 kHz mono", Data: ahx1, Rate: 48_000, Channels: 1)
                 })
        {
            var frames = renderCase.Rate / 2;
            var first = new float[frames * renderCase.Channels];
            machine.LoadModule(renderCase.Data);
            machine.RenderFrames(first, frames, renderCase.Channels, renderCase.Rate, captureChannels: false);
            Assert.All(first, sample => Assert.True(float.IsFinite(sample), renderCase.Name));
            Assert.Contains(first, sample => Math.Abs(sample) > 0.0001f);
        }

        var ahx0First = RenderFresh(player, ahx0, 44_100, 1);
        var ahx0Second = RenderFresh(player, ahx0, 44_100, 1);
        Assert.Equal(ahx0First, ahx0Second);
    }

    private static float[] RenderFresh(byte[] player, byte[] module, int sampleRate, int channels)
    {
        using var machine = new Ahx68kReferenceMachine(player, module);
        machine.TraceEnabled = false;
        machine.Initialize();
        var frames = sampleRate / 5;
        var pcm = new float[frames * channels];
        machine.RenderFrames(pcm, frames, channels, sampleRate, captureChannels: false);
        return pcm;
    }

    private static void AssertRoutineAndAudioEvidence(Ahx68kReferenceMachine machine, int traceStart, string name, bool allowNoAudioWrites)
    {
        var trace = machine.Trace.Skip(traceStart).ToArray();
        Assert.True(trace.Any(entry => entry.Kind == AhxTraceKind.Routine && entry.Source == Ahx68kEntryPoint.Interrupt.ToString()), name);
        if (!allowNoAudioWrites)
        {
            Assert.True(trace.Any(entry => entry.Kind == AhxTraceKind.CustomWrite && entry.Register is >= 0x0A0 and <= 0x0DF), name);
        }

        machine.ValidateGuards();
    }

    private static void RunInterrupts(Ahx68kReferenceMachine machine, int count)
    {
        for (var i = 0; i < count; i++)
        {
            machine.Interrupt();
        }
    }

    private static int PrepareEvidence(Ahx68kReferenceMachine machine, byte[] module, int interruptCount)
    {
        machine.TraceEnabled = false;
        machine.LoadModule(module);
        RunInterrupts(machine, Math.Max(0, interruptCount - 1));
        machine.ClearTrace();
        machine.TraceEnabled = true;
        machine.Interrupt();
        return 0;
    }

    private static byte[] PatchTrackEffect(byte[] source, int command, int parameter, int? secondCommand, int secondParameter)
    {
        var result = (byte[])source.Clone();
        var layout = FindEditableLayout(result);
        WriteTrackStep(result, layout.TrackOffset, 24, layout.InstrumentNumber, command, parameter);
        if (secondCommand.HasValue)
        {
            WriteTrackStep(result, layout.TrackOffset + 3, 0, 0, secondCommand.Value, secondParameter);
        }

        return result;
    }

    private static byte[] PatchPerformanceEffect(byte[] source, int effect, int parameter, int waveform, bool fixedNote)
    {
        var result = PatchTrackEffect(source, 0, 0, null, 0);
        var layout = FindEditableLayout(result);
        var value = ReadUInt32(result, layout.PerformanceOffset);
        value &= ~((7u << 26) | (7u << 23) | (1u << 22) | (0xFFu << 8));
        value |= ((uint)effect & 7) << 26;
        value |= ((uint)waveform & 7) << 23;
        value |= (fixedNote ? 1u : 0u) << 22;
        value |= (uint)(parameter & 0xFF) << 8;
        WriteUInt32(result, layout.PerformanceOffset, value);
        return result;
    }

    private static byte[] PatchWaveform(byte[] source, int waveform, int wavelength)
    {
        var result = PatchPerformanceEffect(source, 7, 1, waveform, fixedNote: false);
        var layout = FindEditableLayout(result);
        result[layout.InstrumentOffset + 1] = (byte)wavelength;
        return result;
    }

    private static byte[] PatchEnvelope(byte[] source, int attack, int decay, int sustain, int release)
    {
        var result = PatchPerformanceEffect(source, 7, 1, waveform: 1, fixedNote: false);
        var layout = FindEditableLayout(result);
        result[layout.InstrumentOffset + 2] = (byte)attack;
        result[layout.InstrumentOffset + 3] = 64;
        result[layout.InstrumentOffset + 4] = (byte)decay;
        result[layout.InstrumentOffset + 5] = 48;
        result[layout.InstrumentOffset + 6] = (byte)sustain;
        result[layout.InstrumentOffset + 7] = (byte)release;
        result[layout.InstrumentOffset + 8] = 0;
        return result;
    }

    private static byte[] PatchSpeedMultiplier(byte[] source, int multiplier)
    {
        var result = (byte[])source.Clone();
        result[6] = (byte)((result[6] & ~0x60) | ((multiplier - 1) << 5));
        return result;
    }

    private static byte[] PatchHardCut(byte[] source, int frames, bool release)
    {
        var result = PatchEnvelope(source, 1, 1, 1, 8);
        var layout = FindEditableLayout(result);
        var vibratoDepth = result[layout.InstrumentOffset + 14] & 0x0F;
        result[layout.InstrumentOffset + 14] = (byte)(vibratoDepth | ((frames & 7) << 4) | (release ? 0x80 : 0));
        return result;
    }

    private static byte[] PatchVibrato(byte[] source, int delay, int depth, int speed)
    {
        var result = PatchPerformanceEffect(source, 7, 1, waveform: 1, fixedNote: false);
        var layout = FindEditableLayout(result);
        result[layout.InstrumentOffset + 13] = (byte)delay;
        result[layout.InstrumentOffset + 14] = (byte)((result[layout.InstrumentOffset + 14] & 0xF0) | (depth & 0x0F));
        result[layout.InstrumentOffset + 15] = (byte)speed;
        return result;
    }

    private static byte[] PatchModulationBounds(byte[] source, bool filter)
    {
        var result = PatchPerformanceEffect(source, 4, filter ? 0x11 : 0x01, waveform: 3, fixedNote: false);
        var layout = FindEditableLayout(result);
        if (filter)
        {
            result[layout.InstrumentOffset + 1] = (byte)((4 << 3) | (result[layout.InstrumentOffset + 1] & 7));
            result[layout.InstrumentOffset + 12] = 0x08;
            result[layout.InstrumentOffset + 19] = 0x38;
        }
        else
        {
            result[layout.InstrumentOffset + 16] = 0x08;
            result[layout.InstrumentOffset + 17] = 0x38;
            result[layout.InstrumentOffset + 18] = 4;
        }

        return result;
    }

    private static EditableLayout FindEditableLayout(byte[] data)
    {
        var positionCount = ((data[6] & 0x0F) << 8) | data[7];
        var trackLength = data[10];
        var maxTrack = data[11];
        var instrumentCount = data[12];
        var subSongs = data[13];
        var positionsOffset = 14 + (subSongs * 2);
        var selectedTrack = Enumerable.Range(0, 4).Select(channel => data[positionsOffset + (channel * 2)]).First(track => track != 0);
        var firstStoredTrack = (data[6] & 0x80) != 0 ? 1 : 0;
        var tracksOffset = positionsOffset + (positionCount * 8);
        var selectedTrackOffset = tracksOffset + ((selectedTrack - firstStoredTrack) * trackLength * 3);
        var instrumentsOffset = tracksOffset + ((maxTrack - firstStoredTrack + 1) * trackLength * 3);
        var instrumentOffset = instrumentsOffset;
        var instrumentNumber = 1;
        for (; instrumentNumber <= instrumentCount; instrumentNumber++)
        {
            var playlistLength = data[instrumentOffset + 21];
            if (playlistLength > 0)
            {
                break;
            }

            instrumentOffset += 22;
        }

        if (instrumentNumber > instrumentCount)
        {
            throw new InvalidOperationException("No editable AHX instrument playlist was found.");
        }

        return new EditableLayout(selectedTrackOffset, instrumentNumber, instrumentOffset, instrumentOffset + 22);
    }

    private static void WriteTrackStep(byte[] data, int offset, int note, int instrument, int command, int parameter)
    {
        var value = (note << 18) | (instrument << 12) | (command << 8) | parameter;
        data[offset] = (byte)(value >> 16);
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)value;
    }

    private static uint ReadUInt32(byte[] data, int offset)
        => ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];

    private static void WriteUInt32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)(value >> 16);
        data[offset + 2] = (byte)(value >> 8);
        data[offset + 3] = (byte)value;
    }

    private static FixtureInputs? FindFixtureInputs()
    {
        var player = Path.Combine(AhxTestInputs.Root, "ahx-reference", "Players", "AHX-Replayer000.BIN");
        var corpus = Path.Combine(AhxTestInputs.Root, "winahx-corpus", "Songs");
        var baseModule = Path.Combine(corpus, "Pink", "Stormlord (Jeroen Tel).ahx");
        if (!File.Exists(player) || !File.Exists(baseModule) || !Directory.Exists(corpus))
        {
            return null;
        }

        var capacity = Directory.GetFiles(corpus, "*.ahx", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path).Length)
            .Max();
        return new FixtureInputs(player, corpus, baseModule, checked((int)capacity));
    }

    private readonly record struct EditableLayout(int TrackOffset, int InstrumentNumber, int InstrumentOffset, int PerformanceOffset);

    private readonly record struct FixtureInputs(string Player, string Corpus, string BaseModule, int ModuleCapacity);
}
