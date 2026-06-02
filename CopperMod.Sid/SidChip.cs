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
        private readonly double _filterInputGain;
        private readonly double _voiceMixGain;
        private readonly double _filterLowPassGain;
        private readonly double _filterBandPassGain;
        private readonly double _filterHighPassGain;
        private uint _pendingRegisterBits;
        private double _filterIntegrator1;
        private double _filterIntegrator2;
        private double _filterG;
        private double _filterDamping;
        private double _filterDenominator = 1.0;
        private double _filterOutputGain;
        private int _filterRouting;
        private int _filterMode;
        private int _filterCutoffRegister;
        private int _filterResonanceNibble;
        private double _filterCutoffHz;
        private double _lastLowPass;
        private double _lastBandPass;
        private double _lastHighPass;
        private double _filterVoiceLeakageGain;
        private bool _filterCoefficientsDirty = true;
        private bool _voice3Muted;
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
            _filterInputGain = _filterProfile.FilterInputGain;
            _filterVoiceLeakageGain = _filterProfile.MapFilterVoiceLeakageGain(0);
            _voiceMixGain = SidAnalog.VoiceMixGain(Model);
            _filterLowPassGain = _filterProfile.LowPassGain;
            _filterBandPassGain = _filterProfile.BandPassGain;
            _filterHighPassGain = _filterProfile.HighPassGain;
            _filterOutputGain = _filterProfile.MapFilterOutputGain(0, 0);
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
            _filterOutputGain = _filterProfile.MapFilterOutputGain(0, 0);
            _filterRouting = 0;
            _filterMode = 0;
            _filterCutoffRegister = 0;
            _filterResonanceNibble = 0;
            _filterCutoffHz = 0;
            _lastLowPass = 0;
            _lastBandPass = 0;
            _lastHighPass = 0;
            _filterVoiceLeakageGain = _filterProfile.MapFilterVoiceLeakageGain(0);
            _filterCoefficientsDirty = true;
            _voice3Muted = false;
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

        [HotPath]
        public double Render(long cycles, double[]? voiceOutputs = null, int voiceOffset = 0)
        {
            if (voiceOutputs == null && Trace == null)
            {
                for (var i = 0L; i < cycles; i++)
                {
                    _cycle++;
                    _lastOutput = ClockOneCycleFast();
                }

                return _lastOutput;
            }

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

        [HotPath]
        public double RenderOneCycle(double[]? voiceOutputs = null, int voiceOffset = 0)
        {
            return RenderOneCycle(-1, voiceOutputs, voiceOffset);
        }

        [HotPath]
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

            if (voiceOutputs == null && Trace == null)
            {
                _lastOutput = ClockOneCycleFast();
                return _lastOutput;
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

        [HotPath]
        public double RenderAndSumFast(long firstCycle, long cycles)
        {
            var sum = 0.0;
            for (var i = 0L; i < cycles; i++)
            {
                _cycle = firstCycle + i;
                _lastOutput = ClockOneCycleFast();
                sum += _lastOutput;
            }

            return sum;
        }

        [HotPath]
        private double ClockOneCycle(long cycle, out double voice1, out double voice2, out double voice3)
        {
            var chipVoice1 = _voices[0];
            var chipVoice2 = _voices[1];
            var chipVoice3 = _voices[2];
            var trace = Trace;
            var tracing = trace != null;
            var before1 = default(SidVoiceDebugState);
            var before2 = default(SidVoiceDebugState);
            var before3 = default(SidVoiceDebugState);
            if (tracing)
            {
                chipVoice1.BeginCycleTrace();
                chipVoice2.BeginCycleTrace();
                chipVoice3.BeginCycleTrace();
                before1 = chipVoice1.GetDebugState();
                before2 = chipVoice2.GetDebugState();
                before3 = chipVoice3.GetDebugState();
            }

            CommitPendingRegisters();

            chipVoice1.ClockEnvelope();
            chipVoice2.ClockEnvelope();
            chipVoice3.ClockEnvelope();

            var previousPhase1 = chipVoice1.Phase;
            var previousPhase2 = chipVoice2.Phase;
            var previousPhase3 = chipVoice3.Phase;
            chipVoice1.ClockOscillator();
            chipVoice2.ClockOscillator();
            chipVoice3.ClockOscillator();

            var advancedPhase1 = chipVoice1.Phase;
            var advancedPhase2 = chipVoice2.Phase;
            var advancedPhase3 = chipVoice3.Phase;
            var voice1MsbRising = SidVoice.MsbRising(previousPhase1, advancedPhase1);
            var voice2MsbRising = SidVoice.MsbRising(previousPhase2, advancedPhase2);
            var voice3MsbRising = SidVoice.MsbRising(previousPhase3, advancedPhase3);
            if (chipVoice1.SyncEnabled && voice3MsbRising)
            {
                chipVoice1.ResetOscillator();
            }

            if (chipVoice2.SyncEnabled && voice1MsbRising)
            {
                chipVoice2.ResetOscillator();
            }

            if (chipVoice3.SyncEnabled && voice2MsbRising)
            {
                chipVoice3.ResetOscillator();
            }

            chipVoice1.ClockNoise(SidVoice.NoiseClockRising(previousPhase1, chipVoice1.Phase));
            chipVoice2.ClockNoise(SidVoice.NoiseClockRising(previousPhase2, chipVoice2.Phase));
            chipVoice3.ClockNoise(SidVoice.NoiseClockRising(previousPhase3, chipVoice3.Phase));

            var model = Model;
            if (tracing)
            {
                voice1 = chipVoice1.RenderOutput(chipVoice3, model, out var waveform1, out var waveformTrace1);
                voice2 = chipVoice2.RenderOutput(chipVoice1, model, out var waveform2, out var waveformTrace2);
                voice3 = chipVoice3.RenderOutput(chipVoice2, model, out var waveform3, out var waveformTrace3);
                TraceVoice(cycle, 0, before1, waveformTrace1, waveform1, voice1);
                TraceVoice(cycle, 1, before2, waveformTrace2, waveform2, voice2);
                TraceVoice(cycle, 2, before3, waveformTrace3, waveform3, voice3);
            }
            else
            {
                voice1 = chipVoice1.RenderOutputFast(chipVoice3, model);
                voice2 = chipVoice2.RenderOutputFast(chipVoice1, model);
                voice3 = chipVoice3.RenderOutputFast(chipVoice2, model);
            }

            return Mix(voice1, voice2, voice3);
        }

        [HotPath]
        private double ClockOneCycleFast()
        {
            var chipVoice1 = _voices[0];
            var chipVoice2 = _voices[1];
            var chipVoice3 = _voices[2];

            CommitPendingRegisters();

            chipVoice1.ClockEnvelope();
            chipVoice2.ClockEnvelope();
            chipVoice3.ClockEnvelope();

            var previousPhase1 = chipVoice1.Phase;
            var previousPhase2 = chipVoice2.Phase;
            var previousPhase3 = chipVoice3.Phase;
            chipVoice1.ClockOscillator();
            chipVoice2.ClockOscillator();
            chipVoice3.ClockOscillator();

            var advancedPhase1 = chipVoice1.Phase;
            var advancedPhase2 = chipVoice2.Phase;
            var advancedPhase3 = chipVoice3.Phase;
            var voice1MsbRising = SidVoice.MsbRising(previousPhase1, advancedPhase1);
            var voice2MsbRising = SidVoice.MsbRising(previousPhase2, advancedPhase2);
            var voice3MsbRising = SidVoice.MsbRising(previousPhase3, advancedPhase3);
            if (chipVoice1.SyncEnabled && voice3MsbRising)
            {
                chipVoice1.ResetOscillator();
            }

            if (chipVoice2.SyncEnabled && voice1MsbRising)
            {
                chipVoice2.ResetOscillator();
            }

            if (chipVoice3.SyncEnabled && voice2MsbRising)
            {
                chipVoice3.ResetOscillator();
            }

            chipVoice1.ClockNoise(SidVoice.NoiseClockRising(previousPhase1, chipVoice1.Phase));
            chipVoice2.ClockNoise(SidVoice.NoiseClockRising(previousPhase2, chipVoice2.Phase));
            chipVoice3.ClockNoise(SidVoice.NoiseClockRising(previousPhase3, chipVoice3.Phase));

            var model = Model;
            var voice1 = chipVoice1.RenderOutputFast(chipVoice3, model);
            var voice2 = chipVoice2.RenderOutputFast(chipVoice1, model);
            var voice3 = chipVoice3.RenderOutputFast(chipVoice2, model);
            return Mix(voice1, voice2, voice3);
        }

        [HotPath]
        private void CommitPendingRegisters()
        {
            var pending = _pendingRegisterBits;
            _pendingRegisterBits = 0;
            var filterCoefficientsDirty = false;
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
                    if (register == 0x15 || register == 0x16)
                    {
                        filterCoefficientsDirty = true;
                    }
                    else if (register == 0x17)
                    {
                        _filterRouting = value & 0x07;
                        filterCoefficientsDirty = true;
                    }
                    else
                    {
                        var volume = value & 0x0F;
                        _masterVolume = SidAnalog.ConvertVolume(volume, Model);
                        _volumeOffset = SidAnalog.VolumeOffset(volume, Model);
                        _voice3Muted = (value & 0x80) != 0;
                        filterCoefficientsDirty |= _filterMode != (value & 0x70);
                    }
                }
            }

            if (filterCoefficientsDirty)
            {
                UpdateFilterCoefficients();
            }
        }

        [HotPath]
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

        [HotPath]
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

            var filterRouting = _filterRouting;
            var direct = 0.0;
            var filtered = 0.0;
            if ((filterRouting & 0x01) != 0)
            {
                filtered += voice1;
            }
            else
            {
                direct += voice1;
            }

            if ((filterRouting & 0x02) != 0)
            {
                filtered += voice2;
            }
            else
            {
                direct += voice2;
            }

            if ((filterRouting & 0x04) != 0)
            {
                filtered += voice3;
            }
            else if (!_voice3Muted)
            {
                direct += voice3;
            }

            var leaked = _filterMode == 0 ? 0.0 : filtered * _filterVoiceLeakageGain;
            var voiceSignal = (direct + leaked + ApplyFilter(filtered * _filterInputGain)) *
                _voiceMixGain *
                _masterVolume;
            var output = SidAnalog.SoftClip(voiceSignal + _volumeOffset);
            _outputLowPassState += (output - _outputLowPassState) * _outputLowPassAlpha;
            return _outputLowPassState;
        }

        [HotPath]
        private double ApplyFilter(double input)
        {
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

            var output = _filterMode switch
            {
                0x10 => low * _filterLowPassGain,
                0x20 => band * _filterBandPassGain,
                0x30 => (low * _filterLowPassGain) + (band * _filterBandPassGain),
                0x40 => high * _filterHighPassGain,
                0x50 => (low * _filterLowPassGain) + (high * _filterHighPassGain),
                0x60 => (band * _filterBandPassGain) + (high * _filterHighPassGain),
                0x70 => ((low * _filterLowPassGain) + (band * _filterBandPassGain)) + (high * _filterHighPassGain),
                _ => 0.0
            };

            return output * _filterOutputGain;
        }

        [HotPath]
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
                _filterOutputGain = _filterProfile.MapFilterOutputGain(_filterResonanceNibble, _filterCutoffRegister);
                _filterVoiceLeakageGain = _filterProfile.MapFilterVoiceLeakageGain(_filterCutoffRegister);
                _filterCoefficientsDirty = false;
                return;
            }

            _filterCutoffHz = _filterProfile.MapCutoff(_filterCutoffRegister);
            _filterG = Math.Tan(Math.PI * _filterCutoffHz / _cpuCyclesPerSecond);
            _filterDamping = _filterProfile.MapDamping(_filterResonanceNibble, _filterCutoffRegister);
            _filterDenominator = 1.0 + (_filterDamping * _filterG) + (_filterG * _filterG);
            _filterOutputGain = _filterProfile.MapFilterOutputGain(_filterResonanceNibble, _filterCutoffRegister);
            _filterVoiceLeakageGain = _filterProfile.MapFilterVoiceLeakageGain(_filterCutoffRegister);
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
