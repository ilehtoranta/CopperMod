using System;

namespace CopperMod.Sid
{
    internal sealed class SidVoice
    {
        private const int Attack = 0;
        private const int Decay = 1;
        private const int Sustain = 2;
        private const int Release = 3;
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
            Frequency = 0;
            PulseWidth = 0;
            AttackDecay = 0;
            SustainRelease = 0;
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
            if (_envelopeState == Sustain)
            {
                return;
            }

            _rateCounter++;
            if (_rateCounter < GetRatePeriod())
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
                    }

                    if (_envelopeCounter >= 0xFF)
                    {
                        _envelopeCounter = 0xFF;
                        _exponentialCounter = 0;
                        _envelopeState = Decay;
                    }

                    break;
                case Decay:
                    var sustain = GetSustainLevel();
                    if (_envelopeCounter > sustain && ClockExponentialCounter())
                    {
                        _envelopeCounter--;
                    }

                    if (_envelopeCounter <= sustain)
                    {
                        _envelopeCounter = sustain;
                        _exponentialCounter = 0;
                        _envelopeState = Sustain;
                    }

                    break;
                case Release:
                    if (_envelopeCounter > 0 && ClockExponentialCounter())
                    {
                        _envelopeCounter--;
                    }

                    break;
            }
        }

        public void ClockOscillator()
        {
            if (TestEnabled)
            {
                _phase = 0;
                return;
            }

            _phase = (_phase + Frequency) & PhaseMask;
        }

        public void ResetOscillator()
        {
            _phase = 0;
        }

        public void ClockNoise(bool oscillatorBit19Rising)
        {
            if (!oscillatorBit19Rising)
            {
                return;
            }

            var feedback = ((_noise >> 22) ^ (_noise >> 17)) & 1;
            _noise = ((_noise << 1) | feedback) & 0x7FFFFF;
        }

        public double RenderOutput(SidVoice? syncSource, SidChipModel model)
        {
            var waveform = RenderWaveform(syncSource, model);
            return waveform * SidAnalog.ConvertEnvelope(_envelopeCounter, model);
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
            }
            else if (!gate && _previousGate)
            {
                _envelopeState = Release;
            }

            _previousGate = gate;
            _control = value;
            if (TestEnabled)
            {
                _phase = 0;
            }
        }

        private double RenderWaveform(SidVoice? syncSource, SidChipModel model)
        {
            var waveformMask = _control & 0xF0;
            if (waveformMask == 0)
            {
                return 0;
            }

            var outputs = 0;
            uint combinedDac = 0x0FFF;
            if ((waveformMask & 0x10) != 0)
            {
                var phase = (_phase >> 11) & 0x0FFF;
                var invert = (_phase & PhaseMsb) != 0;
                if ((_control & 0x04) != 0 && syncSource != null && (syncSource._phase & PhaseMsb) != 0)
                {
                    invert = !invert;
                }

                if (invert)
                {
                    phase ^= 0x0FFF;
                }

                combinedDac &= phase;
                outputs++;
            }

            if ((waveformMask & 0x20) != 0)
            {
                combinedDac &= (_phase >> 12) & 0x0FFF;
                outputs++;
            }

            if ((waveformMask & 0x40) != 0)
            {
                combinedDac &= ((_phase >> 12) & 0x0FFF) >= (PulseWidth & 0x0FFF) ? 0x0FFFu : 0u;
                outputs++;
            }

            if ((waveformMask & 0x80) != 0)
            {
                combinedDac &= GetNoiseDac();
                outputs++;
            }

            if (outputs == 0)
            {
                return 0;
            }

            return SidAnalog.ConvertWaveformDac12(combinedDac, model) * SidAnalog.CombinedWaveformScale(outputs, model);
        }

        private uint GetNoiseDac()
        {
            var dac = 0u;
            dac |= ((_noise >> 20) & 1u) << 11;
            dac |= ((_noise >> 18) & 1u) << 10;
            dac |= ((_noise >> 14) & 1u) << 9;
            dac |= ((_noise >> 11) & 1u) << 8;
            dac |= ((_noise >> 9) & 1u) << 7;
            dac |= ((_noise >> 5) & 1u) << 6;
            dac |= ((_noise >> 2) & 1u) << 5;
            dac |= (_noise & 1u) << 4;
            return dac;
        }

        private int GetRatePeriod()
        {
            return _envelopeState switch
            {
                Attack => RatePeriods[(AttackDecay >> 4) & 0x0F],
                Decay => RatePeriods[AttackDecay & 0x0F],
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
