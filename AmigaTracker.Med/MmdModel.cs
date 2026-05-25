using System;
using System.Collections.Generic;
using AmigaTracker.Abstractions;

namespace AmigaTracker.Med
{
    internal enum MmdVersion
    {
        Mmd0 = 0,
        Mmd1 = 1,
        Mmd2 = 2,
        Mmd3 = 3
    }

    internal enum MmdInstrumentKind
    {
        Empty,
        Sample,
        Synth,
        Hybrid,
        Unsupported
    }

    internal sealed class MmdModule
    {
        public MmdModule(byte[] data, MmdVersion version, int moduleLength)
        {
            Data = data;
            Version = version;
            ModuleLength = moduleLength;
            Song = new MmdSongData();
            Blocks = new List<MmdBlock>();
            Instruments = new List<MmdInstrument>();
            Diagnostics = new List<ModuleDiagnostic>();
        }

        public byte[] Data { get; }

        public MmdVersion Version { get; }

        public int ModuleLength { get; }

        public MmdSongData Song { get; set; }

        public List<MmdBlock> Blocks { get; }

        public List<MmdInstrument> Instruments { get; }

        public List<ModuleDiagnostic> Diagnostics { get; }

        public bool UsesLegacyEightChannelMode =>
            Version < MmdVersion.Mmd2 && (Song.Flags & MmdConstants.Flag8Channel) != 0;

        public bool UsesMixingMode =>
            Version >= MmdVersion.Mmd2 && ((Song.Flags2 & MmdConstants.Flag2Mix) != 0 || Version == MmdVersion.Mmd3 || Song.MixingChannels > 4);

        public string VersionName => "MMD" + (int)Version;
    }

    internal sealed class MmdSongData
    {
        public int HeaderOffset { get; set; }

        public int SongOffset { get; set; }

        public int BlockArrayOffset { get; set; }

        public int SampleArrayOffset { get; set; }

        public int ExpansionOffset { get; set; }

        public int NumBlocks { get; set; }

        public int SongLength { get; set; }

        public byte[] LegacyPlaySequence { get; set; } = new byte[256];

        public List<MmdPlaySequence> PlaySequences { get; } = new List<MmdPlaySequence>();

        public List<int> SectionTable { get; } = new List<int>();

        public int DefaultTempo { get; set; }

        public sbyte PlayTranspose { get; set; }

        public byte Flags { get; set; }

        public byte Flags2 { get; set; }

        public byte Tempo2 { get; set; }

        public byte[] TrackVolumes { get; set; } = new byte[16];

        public sbyte[] TrackPans { get; set; } = new sbyte[16];

        public byte MasterVolume { get; set; } = 64;

        public int NumSamples { get; set; }

        public int NumTracks { get; set; }

        public int NumPlaySequences { get; set; }

        public uint Flags3 { get; set; }

        public int VolumeAdjust { get; set; }

        public int MixingChannels { get; set; }

        public byte MixEchoType { get; set; }

        public byte MixEchoDepth { get; set; }

        public int MixEchoLength { get; set; }

        public sbyte MixStereoSeparation { get; set; }

        public byte[] ChannelSplit { get; set; } = new byte[4];

        public string? SongName { get; set; }

        public MmdSampleInfo[] SampleInfos { get; set; } = new MmdSampleInfo[MmdConstants.MaxLegacyInstruments];
    }

    internal sealed class MmdSampleInfo
    {
        public int RepeatOffset { get; set; }

        public int RepeatLength { get; set; }

        public byte MidiChannel { get; set; }

        public byte MidiPreset { get; set; }

        public byte DefaultVolume { get; set; }

        public sbyte Transpose { get; set; }

        public MmdInstrumentExtension Extension { get; } = new MmdInstrumentExtension();
    }

    internal sealed class MmdInstrumentExtension
    {
        public byte Hold { get; set; }

        public byte Decay { get; set; }

        public sbyte Finetune { get; set; }

        public byte Flags { get; set; }

        public byte OutputDevice { get; set; }

        public int? LongRepeatOffset { get; set; }

        public int? LongRepeatLength { get; set; }

        public string? Name { get; set; }
    }

    internal sealed class MmdBlock
    {
        public MmdBlock(int index, int trackCount, int lineCount)
        {
            Index = index;
            TrackCount = trackCount;
            LineCount = lineCount;
            Cells = new MmdCell[lineCount, trackCount];
            AdditionalCommandPages = new List<MmdCommandPage>();
        }

        public int Index { get; }

        public int TrackCount { get; }

        public int LineCount { get; }

        public MmdCell[,] Cells { get; }

        public string? Name { get; set; }

        public List<MmdCommandPage> AdditionalCommandPages { get; }
    }

    internal sealed class MmdCommandPage
    {
        public MmdCommandPage(int trackCount, int lineCount)
        {
            Commands = new MmdCommand[lineCount, trackCount];
        }

        public MmdCommand[,] Commands { get; }
    }

    internal struct MmdCell
    {
        public byte Note;

        public byte Instrument;

        public byte Command;

        public byte Data;
    }

    internal struct MmdCommand
    {
        public byte CommandNumber;

        public byte Data;
    }

    internal sealed class MmdPlaySequence
    {
        public string Name { get; set; } = string.Empty;

        public List<int> Blocks { get; } = new List<int>();
    }

    internal sealed class MmdInstrument
    {
        public MmdInstrument(int index)
        {
            Index = index;
            Kind = MmdInstrumentKind.Empty;
            LeftSamples = Array.Empty<float>();
            RightSamples = Array.Empty<float>();
            Waveforms = new List<MmdSynthWaveform>();
            VolumeSequence = Array.Empty<byte>();
            WaveformSequence = Array.Empty<byte>();
        }

        public int Index { get; }

        public int Pointer { get; set; }

        public uint Length { get; set; }

        public short RawType { get; set; }

        public int TypeCode { get; set; }

        public bool Is16Bit { get; set; }

        public bool IsStereo { get; set; }

        public MmdInstrumentKind Kind { get; set; }

        public float[] LeftSamples { get; set; }

        public float[] RightSamples { get; set; }

        public int RepeatOffset { get; set; }

        public int RepeatLength { get; set; }

        public bool LoopEnabled { get; set; }

        public int SynthRepeatOffset { get; set; }

        public int SynthRepeatLength { get; set; }

        public int VolumeSequenceLength { get; set; }

        public int WaveformSequenceLength { get; set; }

        public byte VolumeSpeed { get; set; }

        public byte WaveformSpeed { get; set; }

        public byte DefaultDecay { get; set; }

        public List<MmdSynthWaveform> Waveforms { get; }

        public byte[] VolumeSequence { get; set; }

        public byte[] WaveformSequence { get; set; }
    }

    internal sealed class MmdSynthWaveform
    {
        public MmdSynthWaveform(int index, float[] samples, sbyte[]? envelopeSamples = null)
        {
            Index = index;
            Samples = samples;
            EnvelopeSamples = envelopeSamples ?? CreateEnvelopeSamples(samples);
        }

        public int Index { get; }

        public float[] Samples { get; }

        public sbyte[] EnvelopeSamples { get; }

        private static sbyte[] CreateEnvelopeSamples(float[] samples)
        {
            if (samples.Length == 0)
            {
                return Array.Empty<sbyte>();
            }

            var envelopeSamples = new sbyte[samples.Length];
            for (var i = 0; i < envelopeSamples.Length; i++)
            {
                envelopeSamples[i] = (sbyte)Math.Max(sbyte.MinValue, Math.Min(sbyte.MaxValue, (int)Math.Round(samples[i] * 128.0f)));
            }

            return envelopeSamples;
        }
    }
}
