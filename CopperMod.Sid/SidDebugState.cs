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
}
