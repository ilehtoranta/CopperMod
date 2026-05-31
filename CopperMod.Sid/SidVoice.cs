using System;

namespace CopperMod.Sid
{
    [HotPath]
    internal sealed class SidVoice
    {
        private const int Attack = 0;
        private const int Decay = 1;
        private const int Sustain = 2;
        private const int Release = 3;
        private const int RateCounterMask = 0x7FFF;
        private const uint PhaseMask = 0x00FFFFFF;
        private const uint PhaseMsb = 0x00800000;
        private const uint NoiseClockBit = 0x00080000;
        private static readonly int[] RatePeriods =
        {
            9, 32, 63, 95, 149, 220, 267, 313,
            392, 977, 1954, 3126, 3907, 11720, 19532, 31251
        };

        private uint _phase;
        private uint _noise = 0x7FFFF8;
        private int _envelopeCounter;
        private int _rateCounter;
        private int _exponentialCounter;
        private int _envelopeState = Release;
        private bool _previousGate;
        private byte _control;
        private SidCycleTraceEvents _cycleEvents;

        public ushort Frequency { get; private set; }

        public ushort PulseWidth { get; private set; }

        public byte AttackDecay { get; private set; }

        public byte SustainRelease { get; private set; }

        public byte Control => _control;

        public uint Phase => _phase;

        public uint NoiseShiftRegister => _noise;

        public int EnvelopeCounter => _envelopeCounter;

        public int RateCounter => _rateCounter;

        public int ExponentialCounter => _exponentialCounter;

        public int EnvelopeState => _envelopeState;

        public bool SyncEnabled => (_control & 0x02) != 0;

        public bool TestEnabled => (_control & 0x08) != 0;

        public SidCycleTraceEvents CycleEvents => _cycleEvents;

        public void Reset()
        {
            _phase = 0;
            _noise = 0x7FFFF8;
            _envelopeCounter = 0;
            _rateCounter = 0;
            _exponentialCounter = 0;
            _envelopeState = Release;
            _previousGate = false;
            _control = 0;
            _cycleEvents = SidCycleTraceEvents.None;
            Frequency = 0;
            PulseWidth = 0;
            AttackDecay = 0;
            SustainRelease = 0;
        }

        public void BeginCycleTrace()
        {
            _cycleEvents = SidCycleTraceEvents.None;
        }

        public void MarkForwardedWrite()
        {
            _cycleEvents |= SidCycleTraceEvents.ForwardedWrite;
        }

        public void Write(int offset, byte value)
        {
            switch (offset)
            {
                case 0:
                    Frequency = (ushort)((Frequency & 0xFF00) | value);
                    break;
                case 1:
                    Frequency = (ushort)((Frequency & 0x00FF) | (value << 8));
                    break;
                case 2:
                    PulseWidth = (ushort)((PulseWidth & 0x0F00) | value);
                    break;
                case 3:
                    PulseWidth = (ushort)((PulseWidth & 0x00FF) | ((value & 0x0F) << 8));
                    break;
                case 4:
                    WriteControl(value);
                    break;
                case 5:
                    AttackDecay = value;
                    break;
                case 6:
                    SustainRelease = value;
                    break;
            }
        }

        public void ClockEnvelope()
        {
            _rateCounter = (_rateCounter + 1) & RateCounterMask;
            if (_rateCounter != GetRatePeriod())
            {
                return;
            }

            _rateCounter = 0;
            switch (_envelopeState)
            {
                case Attack:
                    if (_envelopeCounter < 0xFF)
                    {
                        _envelopeCounter++;
                        _cycleEvents |= SidCycleTraceEvents.EnvelopeStep;
                    }

                    if (_envelopeCounter >= 0xFF)
                    {
                        _envelopeCounter = 0xFF;
                        _exponentialCounter = 0;
                        _envelopeState = Decay;
                    }

                    break;
                case Decay:
                case Sustain:
                    ClockDecaySustain();
                    break;
                case Release:
                    if (_envelopeCounter > 0 && ClockExponentialCounter())
                    {
                        _envelopeCounter--;
                        _cycleEvents |= SidCycleTraceEvents.EnvelopeStep;
                    }

                    break;
            }
        }

        private void ClockDecaySustain()
        {
            var sustain = GetSustainLevel();
            if (_envelopeCounter <= sustain)
            {
                _envelopeState = Sustain;
                return;
            }

            _envelopeState = Decay;
            if (ClockExponentialCounter())
            {
                _envelopeCounter--;
                _cycleEvents |= SidCycleTraceEvents.EnvelopeStep;
            }

            if (_envelopeCounter <= sustain)
            {
                _envelopeCounter = sustain;
                _exponentialCounter = 0;
                _envelopeState = Sustain;
            }
        }

        public void ClockOscillator()
        {
            if (TestEnabled)
            {
                _cycleEvents |= _phase == 0 ? SidCycleTraceEvents.TestBitHeld : SidCycleTraceEvents.TestBitReset;
                _phase = 0;
                return;
            }

            _phase = (_phase + Frequency) & PhaseMask;
        }

        public void ResetOscillator()
        {
            _phase = 0;
            _cycleEvents |= SidCycleTraceEvents.SyncReset;
        }

        public void ClockNoise(bool oscillatorBit19Rising)
        {
            if (!oscillatorBit19Rising)
            {
                return;
            }

            var feedback = ((_noise >> 22) ^ (_noise >> 17)) & 1;
            _noise = ((_noise << 1) | feedback) & 0x7FFFFF;
            _cycleEvents |= SidCycleTraceEvents.NoiseShift;
        }

        public double RenderOutput(SidVoice? syncSource, SidChipModel model)
        {
            return RenderOutputFast(syncSource, model);
        }

        public double RenderOutput(SidVoice? syncSource, SidChipModel model, out double waveform)
        {
            waveform = RenderWaveform(syncSource, model, captureTrace: false, out _);
            return waveform * SidAnalog.ConvertEnvelope(_envelopeCounter, model);
        }

        public double RenderOutput(SidVoice? syncSource, SidChipModel model, out double waveform, out SidWaveformTrace trace)
        {
            waveform = RenderWaveform(syncSource, model, captureTrace: true, out trace);
            return waveform * SidAnalog.ConvertEnvelope(_envelopeCounter, model);
        }

        public double RenderOutputFast(SidVoice? syncSource, SidChipModel model)
        {
            var waveform = RenderWaveform(syncSource, model, captureTrace: false, out _);
            return waveform * SidAnalog.ConvertEnvelope(_envelopeCounter, model);
        }

        public byte ReadOscillator(SidVoice? syncSource, SidChipModel model)
        {
            _ = RenderWaveform(syncSource, model, captureTrace: true, out var trace);
            return (byte)(trace.WaveformDac >> 4);
        }

        public SidVoiceDebugState GetDebugState()
        {
            return new SidVoiceDebugState(
                _phase,
                _noise,
                GetNoiseDac(),
                _envelopeCounter,
                _rateCounter,
                _exponentialCounter,
                _envelopeState,
                _control);
        }

        public static bool MsbRising(uint previousPhase, uint currentPhase)
        {
            return (previousPhase & PhaseMsb) == 0 && (currentPhase & PhaseMsb) != 0;
        }

        public static bool NoiseClockRising(uint previousPhase, uint currentPhase)
        {
            return (previousPhase & NoiseClockBit) == 0 && (currentPhase & NoiseClockBit) != 0;
        }

        private void WriteControl(byte value)
        {
            var gate = (value & 0x01) != 0;
            if (gate && !_previousGate)
            {
                _envelopeState = Attack;
                _cycleEvents |= SidCycleTraceEvents.GateRising;
            }
            else if (!gate && _previousGate)
            {
                _envelopeState = Release;
                _cycleEvents |= SidCycleTraceEvents.GateFalling;
            }

            _previousGate = gate;
            _control = value;
            if (TestEnabled)
            {
                _cycleEvents |= _phase == 0 ? SidCycleTraceEvents.TestBitHeld : SidCycleTraceEvents.TestBitReset;
                _phase = 0;
            }
        }

        private double RenderWaveform(
            SidVoice? syncSource,
            SidChipModel model,
            bool captureTrace,
            out SidWaveformTrace trace)
        {
            var waveformMask = _control & 0xF0;
            var pulseDac = GetPulseDac();
            var pulseHigh = pulseDac != 0;
            var triangleDac = GetTriangleDac(
                syncSource,
                out var syncSourceMsb,
                out var ringModInverted,
                out var triangleInverted);
            if (waveformMask == 0)
            {
                trace = captureTrace
                    ? new SidWaveformTrace(
                        0,
                        pulseHigh,
                        syncSourceMsb,
                        ringModInverted,
                        triangleInverted,
                        noiseUsesPostShiftRegister: false)
                    : default;
                return 0;
            }

            if (model == SidChipModel.Mos6581 && waveformMask == 0x50)
            {
                return RenderMos6581TrianglePulse(
                    triangleDac,
                    pulseDac,
                    pulseHigh,
                    syncSourceMsb,
                    ringModInverted,
                    triangleInverted,
                    captureTrace,
                    out trace);
            }

            var outputs = 0;
            uint combinedDac = 0x0FFF;
            if ((waveformMask & 0x10) != 0)
            {
                combinedDac &= triangleDac;
                outputs++;
            }

            if ((waveformMask & 0x20) != 0)
            {
                combinedDac &= GetSawDac();
                outputs++;
            }

            if ((waveformMask & 0x40) != 0)
            {
                combinedDac &= pulseDac;
                outputs++;
            }

            var noiseSelected = (waveformMask & 0x80) != 0;
            if ((waveformMask & 0x80) != 0)
            {
                combinedDac &= GetNoiseDac();
                outputs++;
            }

            if (outputs == 0)
            {
                trace = captureTrace
                    ? new SidWaveformTrace(
                        0,
                        pulseHigh,
                        syncSourceMsb,
                        ringModInverted,
                        triangleInverted,
                        noiseUsesPostShiftRegister: false)
                    : default;
                return 0;
            }

            trace = captureTrace
                ? new SidWaveformTrace(
                    combinedDac,
                    pulseHigh,
                    syncSourceMsb,
                    ringModInverted,
                    triangleInverted,
                    noiseSelected)
                : default;
            return SidAnalog.ConvertWaveformDac12(combinedDac, model) * SidAnalog.CombinedWaveformScale(outputs, model);
        }

        private double RenderMos6581TrianglePulse(
            uint triangleDac,
            uint pulseDac,
            bool pulseHigh,
            bool syncSourceMsb,
            bool ringModInverted,
            bool triangleInverted,
            bool captureTrace,
            out SidWaveformTrace trace)
        {
            if (pulseDac == 0)
            {
                trace = captureTrace
                    ? new SidWaveformTrace(
                        0,
                        pulseHigh,
                        syncSourceMsb,
                        ringModInverted,
                        triangleInverted,
                        noiseUsesPostShiftRegister: false)
                    : default;
                return SidAnalog.ConvertWaveformDac12(0, SidChipModel.Mos6581) *
                    SidAnalog.CombinedWaveformScale(2, SidChipModel.Mos6581);
            }

            var saw = GetSawDac();
            var contentionMask = (_phase >> 10) & 0x0FFF;
            var contentionDac = (triangleDac & contentionMask) | ((triangleDac & saw) >> 1);
            const double mos6581TrianglePulseBias = -1.4;
            trace = captureTrace
                ? new SidWaveformTrace(
                    contentionDac,
                    pulseHigh,
                    syncSourceMsb,
                    ringModInverted,
                    triangleInverted,
                    noiseUsesPostShiftRegister: false)
                : default;
            return (SidAnalog.ConvertWaveformDac12(contentionDac, SidChipModel.Mos6581) *
                SidAnalog.CombinedWaveformScale(2, SidChipModel.Mos6581)) + mos6581TrianglePulseBias;
        }

        private uint GetTriangleDac(
            SidVoice? syncSource,
            out bool syncSourceMsb,
            out bool ringModInverted,
            out bool triangleInverted)
        {
            var phase = (_phase >> 11) & 0x0FFF;
            var invert = (_phase & PhaseMsb) != 0;
            syncSourceMsb = syncSource != null && (syncSource._phase & PhaseMsb) != 0;
            ringModInverted = (_control & 0x04) != 0 && syncSourceMsb;
            if (ringModInverted)
            {
                invert = !invert;
            }

            triangleInverted = invert;
            return invert ? phase ^ 0x0FFFu : phase;
        }

        private uint GetSawDac()
        {
            return (_phase >> 12) & 0x0FFF;
        }

        private uint GetPulseDac()
        {
            return ((_phase >> 12) & 0x0FFF) >= (PulseWidth & 0x0FFF) ? 0x0FFFu : 0u;
        }

        private uint GetNoiseDac()
        {
            var dac = 0u;
            dac |= ((_noise >> 22) & 1u) << 11;
            dac |= ((_noise >> 20) & 1u) << 10;
            dac |= ((_noise >> 16) & 1u) << 9;
            dac |= ((_noise >> 13) & 1u) << 8;
            dac |= ((_noise >> 11) & 1u) << 7;
            dac |= ((_noise >> 7) & 1u) << 6;
            dac |= ((_noise >> 4) & 1u) << 5;
            dac |= ((_noise >> 2) & 1u) << 4;
            return dac;
        }

        private int GetRatePeriod()
        {
            return _envelopeState switch
            {
                Attack => RatePeriods[(AttackDecay >> 4) & 0x0F],
                Decay => RatePeriods[AttackDecay & 0x0F],
                Sustain => RatePeriods[AttackDecay & 0x0F],
                Release => RatePeriods[SustainRelease & 0x0F],
                _ => int.MaxValue
            };
        }

        private int GetSustainLevel()
        {
            return ((SustainRelease >> 4) & 0x0F) * 0x11;
        }

        private bool ClockExponentialCounter()
        {
            _exponentialCounter++;
            if (_exponentialCounter < GetExponentialPeriod(_envelopeCounter))
            {
                return false;
            }

            _exponentialCounter = 0;
            return true;
        }

        private static int GetExponentialPeriod(int envelope)
        {
            return envelope switch
            {
                <= 0x06 => 30,
                <= 0x0E => 16,
                <= 0x1A => 8,
                <= 0x36 => 4,
                <= 0x5D => 2,
                _ => 1
            };
        }
    }
}
