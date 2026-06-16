namespace CopperMod.Sid
{
    internal readonly struct SidChipDebugState
    {
        public SidChipDebugState(
            byte[] forwardedRegisters,
            SidVoiceDebugState[] voices,
            SidFilterProfileId filterProfile,
            int filterCutoffRegister,
            double filterCutoffHz,
            int filterResonanceNibble,
            int filterMode,
            double filterDamping,
            double lowPassOutput,
            double bandPassOutput,
            double highPassOutput)
        {
            ForwardedRegisters = forwardedRegisters;
            Voices = voices;
            FilterProfile = filterProfile;
            FilterCutoffRegister = filterCutoffRegister;
            FilterCutoffHz = filterCutoffHz;
            FilterResonanceNibble = filterResonanceNibble;
            FilterMode = filterMode;
            FilterDamping = filterDamping;
            LowPassOutput = lowPassOutput;
            BandPassOutput = bandPassOutput;
            HighPassOutput = highPassOutput;
        }

        public byte[] ForwardedRegisters { get; }

        public SidVoiceDebugState[] Voices { get; }

        public SidFilterProfileId FilterProfile { get; }

        public int FilterCutoffRegister { get; }

        public double FilterCutoffHz { get; }

        public int FilterResonanceNibble { get; }

        public int FilterMode { get; }

        public double FilterDamping { get; }

        public double LowPassOutput { get; }

        public double BandPassOutput { get; }

        public double HighPassOutput { get; }
    }

    internal readonly struct SidVoiceDebugState
    {
        public SidVoiceDebugState(
            uint accumulator,
            uint noiseShiftRegister,
            uint noiseDac,
            int envelopeCounter,
            int rateCounter,
            int exponentialCounter,
            int envelopeState,
            byte control)
        {
            Accumulator = accumulator;
            NoiseShiftRegister = noiseShiftRegister;
            NoiseDac = noiseDac;
            EnvelopeCounter = envelopeCounter;
            RateCounter = rateCounter;
            ExponentialCounter = exponentialCounter;
            EnvelopeState = envelopeState;
            Control = control;
        }

        public uint Accumulator { get; }

        public uint NoiseShiftRegister { get; }

        public uint NoiseDac { get; }

        public int EnvelopeCounter { get; }

        public int RateCounter { get; }

        public int ExponentialCounter { get; }

        public int EnvelopeState { get; }

        public byte Control { get; }
    }

    internal readonly struct SidSystemTimingSnapshot
    {
        public SidSystemTimingSnapshot(
            long audioCycle,
            long registerCycle,
            long registerBusCycle,
            long sampleCycles,
            double sampleAccumulator,
            int channelCaptureFrameIndex,
            int pendingWriteCount,
            int audioPendingWriteIndex,
            int registerPendingWriteIndex,
            int registerBusPendingWriteIndex)
        {
            AudioCycle = audioCycle;
            RegisterCycle = registerCycle;
            RegisterBusCycle = registerBusCycle;
            SampleCycles = sampleCycles;
            SampleAccumulator = sampleAccumulator;
            ChannelCaptureFrameIndex = channelCaptureFrameIndex;
            PendingWriteCount = pendingWriteCount;
            AudioPendingWriteIndex = audioPendingWriteIndex;
            RegisterPendingWriteIndex = registerPendingWriteIndex;
            RegisterBusPendingWriteIndex = registerBusPendingWriteIndex;
        }

        public long AudioCycle { get; }

        public long RegisterCycle { get; }

        public long RegisterBusCycle { get; }

        public long SampleCycles { get; }

        public double SampleAccumulator { get; }

        public int ChannelCaptureFrameIndex { get; }

        public int PendingWriteCount { get; }

        public int AudioPendingWriteIndex { get; }

        public int RegisterPendingWriteIndex { get; }

        public int RegisterBusPendingWriteIndex { get; }
    }
}
