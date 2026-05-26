namespace AmigaTracker.Sid
{
    internal readonly struct SidChipDebugState
    {
        public SidChipDebugState(byte[] forwardedRegisters, SidVoiceDebugState[] voices)
        {
            ForwardedRegisters = forwardedRegisters;
            Voices = voices;
        }

        public byte[] ForwardedRegisters { get; }

        public SidVoiceDebugState[] Voices { get; }
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
