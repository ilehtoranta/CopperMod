using System.Collections.Generic;

namespace CopperMod.ProTracker
{
    internal sealed class ProTrackerTickTrace
    {
        public int SongPosition { get; set; }

        public int PatternIndex { get; set; }

        public int Row { get; set; }

        public int Counter { get; set; }

        public int Speed { get; set; }

        public int Bpm { get; set; }

        public int FrameCount { get; set; }

        public int SampleRate { get; set; }

        public bool NewRowProcessed { get; set; }

        public bool Ended { get; set; }

        public List<ProTrackerVoiceTrace> Voices { get; } = new List<ProTrackerVoiceTrace>();
    }

    internal sealed class ProTrackerVoiceTrace
    {
        public int ChannelIndex { get; set; }

        public int SampleNumber { get; set; }

        public int Period { get; set; }

        public int OutputPeriod { get; set; }

        public int WantedPeriod { get; set; }

        public int Volume { get; set; }

        public int TremoloVolume { get; set; }

        public int FineTune { get; set; }

        public int Effect { get; set; }

        public int Parameter { get; set; }

        public double SamplePosition { get; set; }

        public double SampleStep { get; set; }

        public bool IsAudible { get; set; }

        public bool TriggeredThisTick { get; set; }

        public int PaulaStartDelayFrames { get; set; }

        public int PaulaInitialSampleOffset { get; set; }

        public int PaulaInitialSampleLength { get; set; }

        public int PaulaReloadSampleOffset { get; set; }

        public int PaulaReloadSampleLength { get; set; }

        public bool PaulaReloadsSilence { get; set; }
    }
}
