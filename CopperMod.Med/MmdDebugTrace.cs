using System.Collections.Generic;

namespace CopperMod.Med
{
    internal sealed class MmdTickTrace
    {
        public int SequenceIndex { get; set; }

        public int BlockIndex { get; set; }

        public int Row { get; set; }

        public int Tick { get; set; }

        public int Speed { get; set; }

        public int Tempo { get; set; }

        public bool TempoIsBpm { get; set; }

        public int FrameCount { get; set; }

        public int SampleRate { get; set; }

        public List<MmdVoiceTrace> Voices { get; } = new List<MmdVoiceTrace>();
    }

    internal sealed class MmdVoiceTrace
    {
        public int TrackIndex { get; set; }

        public int Note { get; set; }

        public int InstrumentNumber { get; set; }

        public int PendingInstrumentNumber { get; set; }

        public MmdInstrumentKind InstrumentKind { get; set; }

        public int Period { get; set; }

        public int BasePeriod { get; set; }

        public int TargetPeriod { get; set; }

        public int ArpeggioPeriod { get; set; }

        public int NormalizedNoteIndex { get; set; }

        public int PeriodTableIndex { get; set; }

        public bool UsesExtendedPeriodTable { get; set; }

        public double SampleStep { get; set; }

        public int Volume { get; set; }

        public int SynthVolume { get; set; }

        public int TrackVolume { get; set; }

        public int TremoloVolume { get; set; }

        public float TrackPan { get; set; }

        public int SampleLength { get; set; }

        public int SampleWindowOffset { get; set; }

        public double SamplePosition { get; set; }

        public int PaulaInitialSampleOffset { get; set; }

        public int PaulaInitialSampleLength { get; set; }

        public int PaulaReloadSampleOffset { get; set; }

        public int PaulaReloadSampleLength { get; set; }

        public bool PaulaReloadsSilence { get; set; }

        public bool PaulaPointerUpdatedThisTick { get; set; }

        public int PaulaStartDelayFrames { get; set; }

        public int SynthPeriodChange { get; set; }

        public int SynthPeriodChangeSpeed { get; set; }

        public int SynthVibratoDepth { get; set; }

        public int SynthVibratoSpeed { get; set; }

        public int SynthArpeggioOffset { get; set; }

        public int? SynthEnvelopeWaveformIndex { get; set; }

        public int SynthEnvelopePosition { get; set; }

        public bool SynthEnvelopeRestartEnabled { get; set; }

        public int LoopStart { get; set; }

        public int LoopEnd { get; set; }

        public int LoopLength => LoopEnd > LoopStart ? LoopEnd - LoopStart : 0;

        public bool LoopEnabled { get; set; }

        public int Command { get; set; }

        public int CommandData { get; set; }

        public int HoldTicks { get; set; }

        public int InitialHold { get; set; }

        public int FadeSpeed { get; set; }

        public bool Releasing { get; set; }

        public int? SynthWaveformIndex { get; set; }

        public bool IsAudible { get; set; }
    }
}
