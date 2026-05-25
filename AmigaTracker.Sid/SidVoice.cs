using System;

namespace AmigaTracker.Sid
{
    internal sealed class SidVoice
    {
        private const int Attack = 0;
        private const int Decay = 1;
        private const int Sustain = 2;
        private const int Release = 3;
        private static readonly double[] RateSeconds =
        {
            0.002, 0.008, 0.016, 0.024, 0.038, 0.056, 0.068, 0.080,
            0.100, 0.250, 0.500, 0.800, 1.000, 3.000, 5.000, 8.000
        };

        private uint _phase;
        private uint _noise = 0x7FFFF8;
        private double _envelope;
        private int _envelopeState = Release;
        private bool _previousGate;
        private byte _control;

        public ushort Frequency { get; private set; }

        public ushort PulseWidth { get; private set; }

        public byte AttackDecay { get; private set; }

        public byte SustainRelease { get; private set; }

        public byte Control
        {
            get => _control;
            private set
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

                if ((value & 0x08) != 0)
                {
                    _phase = 0;
                }

                _previousGate = gate;
                _control = value;
            }
        }

        public void Reset()
        {
            _phase = 0;
            _noise = 0x7FFFF8;
            _envelope = 0;
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
                    Control = value;
                    break;
                case 5:
                    AttackDecay = value;
                    break;
                case 6:
                    SustainRelease = value;
                    break;
            }
        }

        public double Render(double cycles, SidVoice? syncSource, SidChipModel model)
        {
            AdvanceEnvelope(cycles);
            if ((Control & 0x08) != 0)
            {
                return 0;
            }

            var oldPhase = _phase;
            var increment = (Frequency * cycles * 256.0) % 4294967296.0;
            _phase += (uint)increment;
            if ((Control & 0x02) != 0 && syncSource != null && syncSource.PhaseWrapped(oldPhase))
            {
                _phase = 0;
            }

            AdvanceNoise(oldPhase, _phase);
            var waveform = RenderWaveform(syncSource, model);
            return waveform * _envelope;
        }

        private bool PhaseWrapped(uint previousPhase)
        {
            return _phase < previousPhase;
        }

        private void AdvanceNoise(uint previousPhase, uint currentPhase)
        {
            if (((previousPhase ^ currentPhase) & 0x08000000) == 0)
            {
                return;
            }

            var bit = ((_noise >> 22) ^ (_noise >> 17)) & 1;
            _noise = ((_noise << 1) | bit) & 0x7FFFFF;
        }

        private double RenderWaveform(SidVoice? syncSource, SidChipModel model)
        {
            var waveformMask = Control & 0xF0;
            if (waveformMask == 0)
            {
                return 0;
            }

            var outputs = 0;
            var sum = 0.0;
            if ((waveformMask & 0x10) != 0)
            {
                var phase = (_phase >> 19) & 0x0FFF;
                var invert = (_phase & 0x80000000) != 0;
                if ((Control & 0x04) != 0 && syncSource != null && (syncSource._phase & 0x80000000) != 0)
                {
                    invert = !invert;
                }

                if (invert)
                {
                    phase ^= 0x0FFF;
                }

                sum += NormalizeDac12(phase);
                outputs++;
            }

            if ((waveformMask & 0x20) != 0)
            {
                sum += NormalizeDac12((_phase >> 20) & 0x0FFF);
                outputs++;
            }

            if ((waveformMask & 0x40) != 0)
            {
                var width = PulseWidth & 0x0FFF;
                sum += (_phase >> 20) < width ? 1.0 : -1.0;
                outputs++;
            }

            if ((waveformMask & 0x80) != 0)
            {
                sum += (_noise / 4194303.5) - 1.0;
                outputs++;
            }

            if (outputs == 0)
            {
                return 0;
            }

            var combined = sum / outputs;
            if (outputs > 1 && model == SidChipModel.Mos6581)
            {
                combined *= 0.65;
            }

            return combined;
        }

        private static double NormalizeDac12(uint value)
        {
            return (value / 2047.5) - 1.0;
        }

        private void AdvanceEnvelope(double cycles)
        {
            var seconds = cycles / SidConstants.PalCpuClock;
            switch (_envelopeState)
            {
                case Attack:
                    _envelope += seconds / RateSeconds[(AttackDecay >> 4) & 0x0F];
                    if (_envelope >= 1.0)
                    {
                        _envelope = 1.0;
                        _envelopeState = Decay;
                    }

                    break;
                case Decay:
                    var sustain = ((SustainRelease >> 4) & 0x0F) / 15.0;
                    _envelope -= seconds / RateSeconds[AttackDecay & 0x0F];
                    if (_envelope <= sustain)
                    {
                        _envelope = sustain;
                        _envelopeState = Sustain;
                    }

                    break;
                case Sustain:
                    _envelope = ((SustainRelease >> 4) & 0x0F) / 15.0;
                    break;
                case Release:
                    _envelope -= seconds / RateSeconds[SustainRelease & 0x0F];
                    if (_envelope < 0)
                    {
                        _envelope = 0;
                    }

                    break;
            }
        }
    }
}
