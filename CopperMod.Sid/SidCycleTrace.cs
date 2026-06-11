using System;
using System.Collections.Generic;

namespace CopperMod.Sid
{
    [Flags]
    internal enum SidCycleTraceEvents
    {
        None = 0,
        ForwardedWrite = 1 << 0,
        GateRising = 1 << 1,
        GateFalling = 1 << 2,
        EnvelopeStep = 1 << 3,
        TestBitReset = 1 << 4,
        TestBitHeld = 1 << 5,
        SyncReset = 1 << 6,
        NoiseShift = 1 << 7,
        NoiseWriteback = 1 << 8
    }

    internal sealed class SidCycleTrace
    {
        private readonly List<SidCycleTraceFrame> _frames = new List<SidCycleTraceFrame>();

        public IReadOnlyList<SidCycleTraceFrame> Frames => _frames;

        public void Clear()
        {
            _frames.Clear();
        }

        internal void Add(SidCycleTraceFrame frame)
        {
            _frames.Add(frame);
        }
    }

    internal readonly struct SidCycleTraceFrame
    {
        public SidCycleTraceFrame(
            long cycle,
            int chipIndex,
            int voiceIndex,
            SidCycleTraceEvents events,
            ushort frequency,
            ushort pulseWidth,
            byte control,
            uint accumulatorBefore,
            uint accumulator,
            uint noiseShiftRegisterBefore,
            uint noiseShiftRegister,
            uint noiseDac,
            int envelopeCounterBefore,
            int envelopeCounter,
            int rateCounter,
            int exponentialCounter,
            int envelopeState,
            uint waveformDac,
            bool pulseHigh,
            bool syncSourceMsb,
            bool ringModInverted,
            bool triangleInverted,
            bool noiseUsesPostShiftRegister,
            double waveformOutput,
            double voiceOutput)
        {
            Cycle = cycle;
            ChipIndex = chipIndex;
            VoiceIndex = voiceIndex;
            Events = events;
            Frequency = frequency;
            PulseWidth = pulseWidth;
            Control = control;
            AccumulatorBefore = accumulatorBefore;
            Accumulator = accumulator;
            NoiseShiftRegisterBefore = noiseShiftRegisterBefore;
            NoiseShiftRegister = noiseShiftRegister;
            NoiseDac = noiseDac;
            EnvelopeCounterBefore = envelopeCounterBefore;
            EnvelopeCounter = envelopeCounter;
            RateCounter = rateCounter;
            ExponentialCounter = exponentialCounter;
            EnvelopeState = envelopeState;
            WaveformDac = waveformDac;
            PulseHigh = pulseHigh;
            SyncSourceMsb = syncSourceMsb;
            RingModInverted = ringModInverted;
            TriangleInverted = triangleInverted;
            NoiseUsesPostShiftRegister = noiseUsesPostShiftRegister;
            WaveformOutput = waveformOutput;
            VoiceOutput = voiceOutput;
        }

        public long Cycle { get; }

        public int ChipIndex { get; }

        public int VoiceIndex { get; }

        public SidCycleTraceEvents Events { get; }

        public ushort Frequency { get; }

        public ushort PulseWidth { get; }

        public byte Control { get; }

        public byte Waveform => (byte)(Control & 0xF0);

        public uint AccumulatorBefore { get; }

        public uint Accumulator { get; }

        public uint NoiseShiftRegisterBefore { get; }

        public uint NoiseShiftRegister { get; }

        public uint NoiseDac { get; }

        public int EnvelopeCounterBefore { get; }

        public int EnvelopeCounter { get; }

        public int RateCounter { get; }

        public int ExponentialCounter { get; }

        public int EnvelopeState { get; }

        public uint WaveformDac { get; }

        public bool PulseHigh { get; }

        public bool SyncSourceMsb { get; }

        public bool RingModInverted { get; }

        public bool TriangleInverted { get; }

        public bool NoiseUsesPostShiftRegister { get; }

        public double WaveformOutput { get; }

        public double VoiceOutput { get; }
    }

    internal readonly struct SidWaveformTrace
    {
        public SidWaveformTrace(
            uint waveformDac,
            bool pulseHigh,
            bool syncSourceMsb,
            bool ringModInverted,
            bool triangleInverted,
            bool noiseUsesPostShiftRegister)
        {
            WaveformDac = waveformDac;
            PulseHigh = pulseHigh;
            SyncSourceMsb = syncSourceMsb;
            RingModInverted = ringModInverted;
            TriangleInverted = triangleInverted;
            NoiseUsesPostShiftRegister = noiseUsesPostShiftRegister;
        }

        public uint WaveformDac { get; }

        public bool PulseHigh { get; }

        public bool SyncSourceMsb { get; }

        public bool RingModInverted { get; }

        public bool TriangleInverted { get; }

        public bool NoiseUsesPostShiftRegister { get; }
    }
}
