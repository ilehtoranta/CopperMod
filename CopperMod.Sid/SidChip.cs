using System;
using System.Numerics;

namespace CopperMod.Sid
{
    internal sealed class SidChip
    {
        private readonly SidVoice[] _voices = { new SidVoice(), new SidVoice(), new SidVoice() };
        private readonly byte[] _registers = new byte[32];
        private readonly byte[] _forwardedRegisters = new byte[32];
        private readonly byte[] _pendingRegisters = new byte[32];
        private readonly SidFilterProfileDefinition _filterProfile;
        private readonly int _cpuCyclesPerSecond;
        private readonly double _outputLowPassAlpha;
        private uint _pendingRegisterBits;
        private double _filterIntegrator1;
        private double _filterIntegrator2;
        private double _filterG;
        private double _filterDamping;
        private double _filterDenominator = 1.0;
        private int _filterMode;
        private int _filterCutoffRegister;
        private int _filterResonanceNibble;
        private double _filterCutoffHz;
        private double _lastLowPass;
        private double _lastBandPass;
        private double _lastHighPass;
        private bool _filterCoefficientsDirty = true;
        private double _masterVolume;
        private double _volumeOffset;
        private double _outputLowPassState;
        private double _lastOutput;
        private byte _busValue;
        private long _cycle;

        public SidChip(
            SidChipModel model,
            ushort baseAddress,
            int cpuCyclesPerSecond = SidConstants.PalCpuCyclesPerSecond,
            SidFilterProfileId filterProfile = SidFilterProfileId.Auto)
        {
            Model = model == SidChipModel.Mos8580 ? SidChipModel.Mos8580 : SidChipModel.Mos6581;
            BaseAddress = baseAddress;
            _cpuCyclesPerSecond = cpuCyclesPerSecond > 0 ? cpuCyclesPerSecond : SidConstants.PalCpuCyclesPerSecond;
            _filterProfile = SidFilterProfileDefinition.Resolve(Model, filterProfile);
            _outputLowPassAlpha = 1.0 - Math.Exp(-2.0 * Math.PI * SidAnalog.OutputLowPassCutoffHz(Model) / _cpuCyclesPerSecond);
        }

        public SidChipModel Model { get; }

        public ushort BaseAddress { get; }

        public byte[] Registers => _registers;

        public SidChipDebugState DebugState => CreateDebugState();

        public int MutedVoicesMask { get; set; }

        public int TraceChipIndex { get; set; }

        public SidCycleTrace? Trace { get; set; }

        public void Reset()
        {
            Array.Clear(_registers);
            Array.Clear(_forwardedRegisters);
            Array.Clear(_pendingRegisters);
            _pendingRegisterBits = 0;
            foreach (var voice in _voices)
            {
                voice.Reset();
            }

            _filterIntegrator1 = 0;
            _filterIntegrator2 = 0;
            _filterG = 0;
            _filterDamping = 0;
            _filterDenominator = 1.0;
            _filterMode = 0;
            _filterCutoffRegister = 0;
            _filterResonanceNibble = 0;
            _filterCutoffHz = 0;
            _lastLowPass = 0;
            _lastBandPass = 0;
            _lastHighPass = 0;
            _filterCoefficientsDirty = true;
            _masterVolume = SidAnalog.ConvertVolume(0, Model);
            _volumeOffset = SidAnalog.VolumeOffset(0, Model);
            _outputLowPassState = 0;
            _lastOutput = 0;
            _busValue = 0;
            _cycle = 0;
        }

        public void Write(byte register, byte value)
        {
            register = (byte)(register & 0x1F);
            _registers[register] = value;
            _pendingRegisters[register] = value;
            _pendingRegisterBits |= 1u << register;
            _busValue = value;
        }

        public byte Read(byte register)
        {
            register = (byte)(register & 0x1F);
            var value = register switch
            {
                0x19 => (byte)0xFF,
                0x1A => (byte)0xFF,
                0x1B => _voices[2].ReadOscillator(_voices[1], Model),
                0x1C => (byte)_voices[2].EnvelopeCounter,
                _ => _busValue
            };
            _busValue = value;
            return value;
        }

        public double Render(long cycles, double[]? voiceOutputs = null, int voiceOffset = 0)
        {
            var voice1 = 0.0;
            var voice2 = 0.0;
            var voice3 = 0.0;
            for (var i = 0L; i < cycles; i++)
            {
                _lastOutput = ClockOneCycle(++_cycle, out voice1, out voice2, out voice3);
            }

            if (voiceOutputs != null)
            {
                voiceOutputs[voiceOffset] = voice1;
                voiceOutputs[voiceOffset + 1] = voice2;
                voiceOutputs[voiceOffset + 2] = voice3;
            }

            return _lastOutput;
        }

        public double RenderOneCycle(double[]? voiceOutputs = null, int voiceOffset = 0)
        {
            return RenderOneCycle(-1, voiceOutputs, voiceOffset);
        }

        public double RenderOneCycle(long cycle, double[]? voiceOutputs = null, int voiceOffset = 0)
        {
            if (cycle < 0)
            {
                cycle = ++_cycle;
            }
            else
            {
                _cycle = cycle;
            }

            _lastOutput = ClockOneCycle(cycle, out var voice1, out var voice2, out var voice3);
            if (voiceOutputs != null)
            {
                voiceOutputs[voiceOffset] = voice1;
                voiceOutputs[voiceOffset + 1] = voice2;
                voiceOutputs[voiceOffset + 2] = voice3;
            }

            return _lastOutput;
        }

        private double ClockOneCycle(long cycle, out double voice1, out double voice2, out double voice3)
        {
            for (var i = 0; i < _voices.Length; i++)
            {
                _voices[i].BeginCycleTrace();
            }

            var trace = Trace;
            var before1 = trace != null ? _voices[0].GetDebugState() : default;
            var before2 = trace != null ? _voices[1].GetDebugState() : default;
            var before3 = trace != null ? _voices[2].GetDebugState() : default;

            CommitPendingRegisters();

            for (var i = 0; i < _voices.Length; i++)
            {
                _voices[i].ClockEnvelope();
            }

            var previousPhase1 = _voices[0].Phase;
            var previousPhase2 = _voices[1].Phase;
            var previousPhase3 = _voices[2].Phase;
            for (var i = 0; i < _voices.Length; i++)
            {
                _voices[i].ClockOscillator();
            }

            var advancedPhase1 = _voices[0].Phase;
            var advancedPhase2 = _voices[1].Phase;
            var advancedPhase3 = _voices[2].Phase;
            var voice1MsbRising = SidVoice.MsbRising(previousPhase1, advancedPhase1);
            var voice2MsbRising = SidVoice.MsbRising(previousPhase2, advancedPhase2);
            var voice3MsbRising = SidVoice.MsbRising(previousPhase3, advancedPhase3);
            if (_voices[0].SyncEnabled && voice3MsbRising)
            {
                _voices[0].ResetOscillator();
            }

            if (_voices[1].SyncEnabled && voice1MsbRising)
            {
                _voices[1].ResetOscillator();
            }

            if (_voices[2].SyncEnabled && voice2MsbRising)
            {
                _voices[2].ResetOscillator();
            }

            _voices[0].ClockNoise(SidVoice.NoiseClockRising(previousPhase1, _voices[0].Phase));
            _voices[1].ClockNoise(SidVoice.NoiseClockRising(previousPhase2, _voices[1].Phase));
            _voices[2].ClockNoise(SidVoice.NoiseClockRising(previousPhase3, _voices[2].Phase));

            voice1 = _voices[0].RenderOutput(_voices[2], Model, out var waveform1, out var waveformTrace1);
            voice2 = _voices[1].RenderOutput(_voices[0], Model, out var waveform2, out var waveformTrace2);
            voice3 = _voices[2].RenderOutput(_voices[1], Model, out var waveform3, out var waveformTrace3);
            if (trace != null)
            {
                TraceVoice(cycle, 0, before1, waveformTrace1, waveform1, voice1);
                TraceVoice(cycle, 1, before2, waveformTrace2, waveform2, voice2);
                TraceVoice(cycle, 2, before3, waveformTrace3, waveform3, voice3);
            }

            return Mix(voice1, voice2, voice3);
        }

        private void CommitPendingRegisters()
        {
            var pending = _pendingRegisterBits;
            _pendingRegisterBits = 0;
            while (pending != 0)
            {
                var register = BitOperations.TrailingZeroCount(pending);
                pending &= ~(1u << register);
                var value = _pendingRegisters[register];
                _forwardedRegisters[register] = value;
                if (register < 21)
                {
                    var voice = _voices[register / 7];
                    voice.MarkForwardedWrite();
                    voice.Write(register % 7, value);
                }
                else if (register >= 0x15 && register <= 0x18)
                {
                    _filterCoefficientsDirty = true;
                    if (register == 0x18)
                    {
                        var volume = value & 0x0F;
                        _masterVolume = SidAnalog.ConvertVolume(volume, Model);
                        _volumeOffset = SidAnalog.VolumeOffset(volume, Model);
                    }
                }
            }
        }

        private void TraceVoice(
            long cycle,
            int voiceIndex,
            SidVoiceDebugState before,
            SidWaveformTrace waveformTrace,
            double waveformOutput,
            double voiceOutput)
        {
            var voice = _voices[voiceIndex];
            var after = voice.GetDebugState();
            Trace?.Add(new SidCycleTraceFrame(
                cycle,
                TraceChipIndex,
                voiceIndex,
                voice.CycleEvents,
                voice.Frequency,
                voice.PulseWidth,
                after.Control,
                before.Accumulator,
                after.Accumulator,
                before.NoiseShiftRegister,
                after.NoiseShiftRegister,
                after.NoiseDac,
                before.EnvelopeCounter,
                after.EnvelopeCounter,
                after.RateCounter,
                after.ExponentialCounter,
                after.EnvelopeState,
                waveformTrace.WaveformDac,
                waveformTrace.PulseHigh,
                waveformTrace.SyncSourceMsb,
                waveformTrace.RingModInverted,
                waveformTrace.TriangleInverted,
                waveformTrace.NoiseUsesPostShiftRegister,
                waveformOutput,
                voiceOutput));
        }

        private double Mix(double voice1, double voice2, double voice3)
        {
            if ((MutedVoicesMask & 0x01) != 0)
            {
                voice1 = 0;
            }

            if ((MutedVoicesMask & 0x02) != 0)
            {
                voice2 = 0;
            }

            if ((MutedVoicesMask & 0x04) != 0)
            {
                voice3 = 0;
            }

            var mixer = _forwardedRegisters[0x18];
            var filterRouting = _forwardedRegisters[0x17] & 0x0F;
            var voice3Muted = (mixer & 0x80) != 0;
            var direct = 0.0;
            var filtered = 0.0;
            RouteVoice(voice1, filtered: (filterRouting & 0x01) != 0, ref direct, ref filtered);
            RouteVoice(voice2, filtered: (filterRouting & 0x02) != 0, ref direct, ref filtered);
            if ((filterRouting & 0x04) != 0)
            {
                RouteVoice(voice3, filtered: true, ref direct, ref filtered);
            }
            else if (!voice3Muted)
            {
                RouteVoice(voice3, filtered: false, ref direct, ref filtered);
            }

            var voiceSignal = (direct + ApplyFilter(filtered * _filterProfile.FilterInputGain)) *
                SidAnalog.VoiceMixGain(Model) *
                _masterVolume;
            var output = SidAnalog.SoftClip(voiceSignal + _volumeOffset);
            _outputLowPassState += (output - _outputLowPassState) * _outputLowPassAlpha;
            return _outputLowPassState;
        }

        private static void RouteVoice(double voice, bool filtered, ref double direct, ref double filterInput)
        {
            if (filtered)
            {
                filterInput += voice;
            }
            else
            {
                direct += voice;
            }
        }

        private double ApplyFilter(double input)
        {
            if (_filterCoefficientsDirty)
            {
                UpdateFilterCoefficients();
            }

            if (_filterMode == 0)
            {
                _lastLowPass = 0;
                _lastBandPass = 0;
                _lastHighPass = 0;
                return 0;
            }

            var high = (input - ((_filterDamping + _filterG) * _filterIntegrator1) - _filterIntegrator2) / _filterDenominator;
            var band = (_filterG * high) + _filterIntegrator1;
            var low = (_filterG * band) + _filterIntegrator2;
            _filterIntegrator1 = (_filterG * high) + band;
            _filterIntegrator2 = (_filterG * band) + low;
            _lastLowPass = low;
            _lastBandPass = band;
            _lastHighPass = high;

            var output = 0.0;
            if ((_filterMode & 0x10) != 0)
            {
                output += low * _filterProfile.LowPassGain;
            }

            if ((_filterMode & 0x20) != 0)
            {
                output += band * _filterProfile.BandPassGain;
            }

            if ((_filterMode & 0x40) != 0)
            {
                output += high * _filterProfile.HighPassGain;
            }

            return output * _filterProfile.MapFilterOutputGain(_filterResonanceNibble, _filterCutoffRegister);
        }

        private void UpdateFilterCoefficients()
        {
            _filterCutoffRegister = (_forwardedRegisters[0x16] << 3) | (_forwardedRegisters[0x15] & 0x07);
            _filterResonanceNibble = _forwardedRegisters[0x17] >> 4;
            _filterMode = _forwardedRegisters[0x18] & 0x70;
            if (_filterMode == 0)
            {
                _filterG = 0;
                _filterDamping = 0;
                _filterDenominator = 1.0;
                _filterCutoffHz = _filterProfile.MapCutoff(_filterCutoffRegister);
                _filterCoefficientsDirty = false;
                return;
            }

            _filterCutoffHz = _filterProfile.MapCutoff(_filterCutoffRegister);
            _filterG = Math.Tan(Math.PI * _filterCutoffHz / _cpuCyclesPerSecond);
            _filterDamping = _filterProfile.MapDamping(_filterResonanceNibble, _filterCutoffRegister);
            _filterDenominator = 1.0 + (_filterDamping * _filterG) + (_filterG * _filterG);
            _filterCoefficientsDirty = false;
        }

        private SidChipDebugState CreateDebugState()
        {
            if (_filterCoefficientsDirty)
            {
                UpdateFilterCoefficients();
            }

            var voices = new SidVoiceDebugState[_voices.Length];
            for (var i = 0; i < voices.Length; i++)
            {
                voices[i] = _voices[i].GetDebugState();
            }

            return new SidChipDebugState(
                (byte[])_forwardedRegisters.Clone(),
                voices,
                _filterProfile.Id,
                _filterCutoffRegister,
                _filterCutoffHz,
                _filterResonanceNibble,
                _filterMode,
                _filterDamping,
                _lastLowPass,
                _lastBandPass,
                _lastHighPass);
        }
    }
}
